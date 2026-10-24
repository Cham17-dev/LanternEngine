// SPDX-FileCopyrightText: Copyright 2024 shadPS4 Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

#include <algorithm>
#include <array>
#include <bit>
#include <chrono>
#include <cstdio>
#include <span>
#include <vector>

#include "shader_recompiler/info.h"
#include "video_core/renderer_vulkan/vk_rasterizer.h"
#include "video_core/renderer_vulkan/vk_runtime.h"
#include "video_core/renderer_vulkan/vk_shader_hle.h"
#include "video_core/renderer_vulkan/vk_shader_util.h"
#include "video_core/renderer_vulkan/vk_instance.h"
#include "video_core/renderer_vulkan/vk_scheduler.h"
#include "video_core/host_shaders/buffer_multi_copy_comp.h"
#include "bbport_sections.h"
#include "bbport_toggles.h"
#include "video_core/renderer_vulkan/vk_gpu_profiler.h"
#include "video_core/renderer_vulkan/vk_gpu_savings.h"

extern std::unique_ptr<AmdGpu::Liverpool> liverpool;

namespace Vulkan {

static constexpr u64 COPY_SHADER_HASH = 0xfefebf9f;

/// A range of each buffer a batch reads or writes (barrier checks and access tracking).
struct AccessRange {
    u64 src_min, src_max, dst_min, dst_max;
};

// bbport: the copy shader runs ~57 times per frame with ~1024 small ranges each. As one
// vkCmdCopyBuffer with that many regions it cost ~37 ns of GPU time per range (~2.1 ms per
// frame) plus the recording; buffer_multi_copy.comp copies all ranges in one dispatch.
/// `ranges`: the batches merged into this dispatch (BB_HLE_COPY_MERGE), each checked and
/// tracked on its own; empty: the bounds of all the copies, as one batch.
static bool MultiCopy(Rasterizer& rasterizer, const VideoCore::Buffer* src,
                      const VideoCore::Buffer* dst, std::span<const vk::BufferCopy> copies,
                      std::span<const AccessRange> ranges = {}) {
    if (copies.size() < 8 || BbToggle::Disabled(BbToggle::MultiCopyShader)) {
        return false;
    }
    u64 src_min = ~0ull, src_max = 0, dst_min = ~0ull, dst_max = 0;
    for (const auto& copy : copies) {
        if ((copy.srcOffset | copy.dstOffset | copy.size) & 3) {
            return false; // dword copies only
        }
        src_min = std::min(src_min, copy.srcOffset);
        src_max = std::max(src_max, copy.srcOffset + copy.size);
        dst_min = std::min(dst_min, copy.dstOffset);
        dst_max = std::max(dst_max, copy.dstOffset + copy.size);
    }
    auto& runtime = rasterizer.GetRuntime();
    auto& scheduler = runtime.GetScheduler();
    const auto& instance = runtime.GetInstance();
    const u64 align = instance.StorageMinAlignment();
    src_min = Common::AlignDown(src_min, align);
    dst_min = Common::AlignDown(dst_min, align);
    // Merged batches may span more than a descriptor or a dispatch can hold.
    if (!ranges.empty() &&
        (src_max - src_min > instance.MaxStorageBufferRange() ||
         dst_max - dst_min > instance.MaxStorageBufferRange() ||
         copies.size() > instance.MaxComputeWorkGroupCountX())) {
        return false;
    }

    struct Pipeline {
        vk::UniqueDescriptorSetLayout set_layout;
        vk::UniquePipelineLayout layout;
        vk::UniquePipeline pipeline;
    };
    static Pipeline pipe = [&] {
        const auto device = instance.GetDevice();
        Pipeline p;
        std::array<vk::DescriptorSetLayoutBinding, 3> bindings{};
        for (u32 i = 0; i < 3; ++i) {
            bindings[i] = {.binding = i,
                           .descriptorType = vk::DescriptorType::eStorageBuffer,
                           .descriptorCount = 1,
                           .stageFlags = vk::ShaderStageFlagBits::eCompute};
        }
        p.set_layout = Check(device.createDescriptorSetLayoutUnique({
            .flags = vk::DescriptorSetLayoutCreateFlagBits::ePushDescriptorKHR,
            .bindingCount = static_cast<u32>(bindings.size()),
            .pBindings = bindings.data(),
        }));
        const vk::PushConstantRange range{.stageFlags = vk::ShaderStageFlagBits::eCompute,
                                          .offset = 0,
                                          .size = sizeof(u32)};
        p.layout = Check(device.createPipelineLayoutUnique({
            .setLayoutCount = 1,
            .pSetLayouts = &*p.set_layout,
            .pushConstantRangeCount = 1,
            .pPushConstantRanges = &range,
        }));
        const auto module = CompileSPV(BUFFER_MULTI_COPY_COMP, device);
        p.pipeline = Check(device.createComputePipelineUnique(
            {}, vk::ComputePipelineCreateInfo{
                    .stage = {.stage = vk::ShaderStageFlagBits::eCompute,
                              .module = module,
                              .pName = "main"},
                    .layout = *p.layout,
                }));
        device.destroyShaderModule(module);
        return p;
    }();

    // The region table goes into the stream buffer (host writes, visible at submission).
    thread_local std::vector<u32> regions;
    regions.resize(copies.size() * 4);
    for (size_t i = 0; i < copies.size(); ++i) {
        regions[i * 4 + 0] = static_cast<u32>((copies[i].srcOffset - src_min) / 4);
        regions[i * 4 + 1] = static_cast<u32>((copies[i].dstOffset - dst_min) / 4);
        regions[i * 4 + 2] = static_cast<u32>(copies[i].size / 4);
        regions[i * 4 + 3] = 0;
    }
    auto& stream = rasterizer.GetBufferCache().GetStreamBuffer();
    const u64 table_size = regions.size() * sizeof(u32);
    const u64 table_offset = stream.Copy(regions.data(), table_size, align);

    if (auto* profiler = GpuProfiler::Get()) {
        profiler->Mark(0xC0B1ull, [] { return std::string{"copy shader HLE: barrier + dispatch"}; });
    }
    scheduler.EndRendering();
    const u64 src_size = src_max - src_min, dst_size = dst_max - dst_min;
    auto& savings = GpuSavings::Get();
    savings.hle_dispatches.fetch_add(1, std::memory_order_relaxed);
    bool flush = false;
    if (ranges.empty()) {
        flush = runtime.IsBufferAccessed(src, src_min, src_size) ||
                runtime.IsBufferAccessed(dst, dst_min, dst_size, true);
    } else {
        // The batches of one guest dispatch run unordered on the hardware too: one barrier
        // before all of them, for what came before.
        for (const auto& r : ranges) {
            if (runtime.IsBufferAccessed(src, r.src_min, r.src_max - r.src_min) ||
                runtime.IsBufferAccessed(dst, r.dst_min, r.dst_max - r.dst_min, true)) {
                flush = true;
                break;
            }
        }
    }
    if (flush) {
        savings.hle_barriers.fetch_add(1, std::memory_order_relaxed);
        runtime.FlushBarriers();
    }
    const u32 count = static_cast<u32>(copies.size());
    scheduler.RecordCrumb({.name = "HLE copy shader"}, [src = src->Handle(), dst = dst->Handle(), table = stream.Handle(), src_min,
                      src_size, dst_min, dst_size, table_offset, table_size,
                      count](vk::CommandBuffer cmdbuf) {
        const std::array<vk::DescriptorBufferInfo, 3> infos{{
            {src, src_min, src_size},
            {dst, dst_min, dst_size},
            {table, table_offset, table_size},
        }};
        std::array<vk::WriteDescriptorSet, 3> writes{};
        for (u32 i = 0; i < 3; ++i) {
            writes[i] = {.dstBinding = i,
                         .descriptorCount = 1,
                         .descriptorType = vk::DescriptorType::eStorageBuffer,
                         .pBufferInfo = &infos[i]};
        }
        cmdbuf.bindPipeline(vk::PipelineBindPoint::eCompute, *pipe.pipeline);
        cmdbuf.pushDescriptorSetKHR(vk::PipelineBindPoint::eCompute, *pipe.layout, 0, writes);
        cmdbuf.pushConstants(*pipe.layout, vk::ShaderStageFlagBits::eCompute, 0, sizeof(count),
                             &count);
        cmdbuf.dispatch(count, 1, 1);
    });
    if (ranges.empty()) {
        runtime.AccessBuffer(src, src_min, src_size, vk::PipelineStageFlagBits2::eComputeShader,
                             vk::AccessFlagBits2::eShaderRead);
        runtime.AccessBuffer(dst, dst_min, dst_size, vk::PipelineStageFlagBits2::eComputeShader,
                             vk::AccessFlagBits2::eShaderWrite);
        return true;
    }
    for (const auto& r : ranges) {
        runtime.AccessBuffer(src, r.src_min, r.src_max - r.src_min,
                             vk::PipelineStageFlagBits2::eComputeShader,
                             vk::AccessFlagBits2::eShaderRead);
        runtime.AccessBuffer(dst, r.dst_min, r.dst_max - r.dst_min,
                             vk::PipelineStageFlagBits2::eComputeShader,
                             vk::AccessFlagBits2::eShaderWrite);
    }
    return true;
}

namespace {
/// One batch of the copy shader's ranges: the end of its ranges and their bounds.
struct CopyBatch {
    u32 end;
    vk::DeviceSize src_min, src_max, dst_min, dst_max;
};

/// The batches of the greedy merge: consecutive ranges while both their source and their
/// destination bounds stay within `max_distance` bytes.
void FindBatches(std::span<const vk::BufferCopy> copies, vk::DeviceSize max_distance,
                 std::vector<CopyBatch>& batches) {
    batches.clear();
    for (u32 i = 0; i < copies.size();) {
        CopyBatch batch{0, copies[i].srcOffset, copies[i].srcOffset + copies[i].size,
                        copies[i].dstOffset, copies[i].dstOffset + copies[i].size};
        for (++i; i < copies.size(); ++i) {
            const auto& [src_offset, dst_offset, size] = copies[i];
            const auto src_min = std::min(batch.src_min, src_offset);
            const auto src_max = std::max(batch.src_max, src_offset + size);
            if (src_max - src_min > max_distance) {
                break;
            }
            const auto dst_min = std::min(batch.dst_min, dst_offset);
            const auto dst_max = std::max(batch.dst_max, dst_offset + size);
            if (dst_max - dst_min > max_distance) {
                break;
            }
            batch = {0, src_min, src_max, dst_min, dst_max};
        }
        batch.end = i;
        batches.push_back(batch);
    }
}

/// bbport: the guest lists its ranges in its own order and batches merge only neighbours in it,
/// so a shuffled list takes a batch (two ObtainBuffer calls and a dispatch) per few ranges.
/// `grouped` gets them by destination window of 2^window_shift bytes (windows in order of first
/// appearance, the guest's order within each). False, and nothing reordered, when that could
/// change the result: source and destination overlap in guest memory, or a range crosses a
/// window (ranges that may overlap then share a window and keep their order). The hardware runs
/// the ranges as unordered threads of one dispatch anyway.
bool GroupByDestination(std::span<const vk::BufferCopy> copies, u32 window_shift, VAddr src_base,
                        VAddr dst_base, std::vector<vk::BufferCopy>& grouped) {
    constexpr u32 Slots = 512;
    constexpr u32 MaxGroups = 256;
    std::array<u64, Slots> windows;
    windows.fill(~0ull);
    std::array<u16, Slots> ids;
    std::array<u32, MaxGroups + 1> starts{};
    thread_local std::vector<u16> group_of;
    group_of.resize(copies.size());
    u32 groups = 0;
    u64 last_window = ~0ull;
    u16 last_id = 0;
    vk::DeviceSize src_min = ~0ull, src_max = 0, dst_min = ~0ull, dst_max = 0;
    for (u32 i = 0; i < copies.size(); ++i) {
        const auto& [src_offset, dst_offset, size] = copies[i];
        const u64 window = dst_offset >> window_shift;
        if (((dst_offset + size - 1) >> window_shift) != window) {
            return false;
        }
        src_min = std::min(src_min, src_offset);
        src_max = std::max(src_max, src_offset + size);
        dst_min = std::min(dst_min, dst_offset);
        dst_max = std::max(dst_max, dst_offset + size);
        if (window != last_window) {
            u32 slot = static_cast<u32>((window * 0x9E3779B97F4A7C15ull) >> 55);
            while (windows[slot] != ~0ull && windows[slot] != window) {
                slot = (slot + 1) & (Slots - 1);
            }
            if (windows[slot] == ~0ull) {
                if (groups == MaxGroups) {
                    return false;
                }
                windows[slot] = window;
                ids[slot] = static_cast<u16>(groups++);
            }
            last_window = window;
            last_id = ids[slot];
        }
        group_of[i] = last_id;
        ++starts[last_id + 1];
    }
    if (groups < 2 ||
        (src_base + src_min < dst_base + dst_max && dst_base + dst_min < src_base + src_max)) {
        return false;
    }
    for (u32 g = 1; g <= groups; ++g) {
        starts[g] += starts[g - 1];
    }
    grouped.resize(copies.size());
    for (u32 i = 0; i < copies.size(); ++i) {
        grouped[starts[group_of[i]]++] = copies[i];
    }
    return true;
}
/// bbport (BB_HLE_COPY_MERGE): whether ranges of two different batches write the same bytes.
/// Batch after batch, the later one's bytes won; in one dispatch the order would be lost.
bool DestinationsShared(std::span<const vk::BufferCopy> copies,
                        std::span<const CopyBatch> batches) {
    // Cheap first: batches whose destination bounds are disjoint share nothing.
    bool bounds_overlap = false;
    for (size_t i = 0; i < batches.size() && !bounds_overlap; ++i) {
        for (size_t j = i + 1; j < batches.size(); ++j) {
            if (batches[i].dst_min < batches[j].dst_max && batches[j].dst_min < batches[i].dst_max) {
                bounds_overlap = true;
                break;
            }
        }
    }
    if (!bounds_overlap) {
        return false;
    }
    struct Interval {
        vk::DeviceSize begin, end;
        u32 batch;
    };
    thread_local std::vector<Interval> intervals;
    intervals.clear();
    u32 start = 0;
    for (u32 b = 0; b < batches.size(); ++b) {
        for (u32 i = start; i < batches[b].end; ++i) {
            intervals.push_back({copies[i].dstOffset, copies[i].dstOffset + copies[i].size, b});
        }
        start = batches[b].end;
    }
    std::sort(intervals.begin(), intervals.end(),
              [](const Interval& a, const Interval& b) { return a.begin < b.begin; });
    // Sweep: the furthest end so far and its batch, and whether two batches reach it.
    vk::DeviceSize reach = 0;
    u32 reach_batch = 0;
    bool any = false;
    for (const auto& interval : intervals) {
        if (any && interval.begin < reach && interval.batch != reach_batch) {
            return true;
        }
        if (!any || interval.end > reach) {
            // A longer interval of another batch hides the one before; both were checked
            // against everything before them.
            reach = interval.end;
            reach_batch = interval.batch;
            any = true;
        }
    }
    return false;
}
} // namespace

static bool ExecuteCopyShaderHLE(const Shader::Info& info, const AmdGpu::ComputeProgram& cs_program,
                                 Rasterizer& rasterizer) {
    auto& runtime = rasterizer.GetRuntime();
    auto& buffer_cache = rasterizer.GetBufferCache();
    BbSections::Scope batch_scope{BbSections::HleBatch};

    // Copy shader defines three formatted buffers as inputs: control, source, and destination.
    const auto ctl_buf_sharp = info.buffers[0].GetSharp(info);
    const auto src_buf_sharp = info.buffers[1].GetSharp(info);
    const auto dst_buf_sharp = info.buffers[2].GetSharp(info);
    const auto buf_stride = src_buf_sharp.GetStride();
    ASSERT(buf_stride == dst_buf_sharp.GetStride());

    struct CopyShaderControl {
        u32 dst_idx;
        u32 src_idx;
        u32 end;
    };
    static_assert(sizeof(CopyShaderControl) == 12);
    ASSERT(ctl_buf_sharp.GetStride() == sizeof(CopyShaderControl));
    const auto ctl_buf = reinterpret_cast<const CopyShaderControl*>(ctl_buf_sharp.base_address);

    static std::vector<vk::BufferCopy> copies;
    copies.clear();
    copies.reserve(cs_program.dim_x);

    for (u32 i = 0; i < cs_program.dim_x; i++) {
        const auto& [dst_idx, src_idx, end] = ctl_buf[i];
        const u32 local_dst_offset = dst_idx * buf_stride;
        const u32 local_src_offset = src_idx * buf_stride;
        const u32 local_size = (end + 1) * buf_stride;
        copies.emplace_back(local_src_offset, local_dst_offset, local_size);
    }

    // bbport: 64 KiB instead of 64 MiB. The copies are a few KiB spread over up to 57 MiB, and
    // each batch synchronizes (uploads, marks GPU-modified) its whole range: GPU time of the copy
    // shader 1.5 -> 0.6 ms/frame, GPU busy 85% -> 77%, frame rate no lower.
    // BB_COPY_MERGE_KB overrides it.
    static const vk::DeviceSize MaxDistanceForMerge = [] {
        const char* env = std::getenv("BB_COPY_MERGE_KB");
        return env ? vk::DeviceSize(std::strtoull(env, nullptr, 10)) * 1024 : vk::DeviceSize(64_KB);
    }();
    static std::vector<CopyBatch> batches, grouped_batches;
    static std::vector<vk::BufferCopy> grouped;
    FindBatches(copies, MaxDistanceForMerge, batches);
    // Statistics (BB_FRAME_STATS) and the throttle of the grouping: this runs on one thread at a
    // time (the draw recording thread, or the GPU command thread after a drain).
    static u64 stat_dispatches = 0, stat_ranges = 0, stat_batches = 0, stat_saved = 0;
    static u32 grouping_misses = 0, grouping_pause = 0;
    const size_t in_order = batches.size();
    // Worth a try with a few batches or more (each costs a few microseconds, the grouping a few
    // nanoseconds per range); paused for a while after attempts that did not help.
    if (in_order >= 4 && !BbToggle::Disabled(BbToggle::MultiCopyShader)) {
        const u32 window_shift =
            static_cast<u32>(std::bit_width(std::max<u64>(MaxDistanceForMerge, 2)) - 1);
        if (grouping_pause) {
            --grouping_pause;
        } else if (GroupByDestination(copies, window_shift, src_buf_sharp.base_address,
                                      dst_buf_sharp.base_address, grouped) &&
                   (FindBatches(grouped, MaxDistanceForMerge, grouped_batches),
                    grouped_batches.size() < in_order)) {
            copies.swap(grouped);
            batches.swap(grouped_batches);
            grouping_misses = 0;
        } else if (++grouping_misses >= 4) {
            grouping_misses = 0;
            grouping_pause = 64;
        }
    }
    batch_scope.End();
    // BB_FRAME_STATS: which ObtainBuffer path the batches take (BufferCache::hle_obtain).
    using VideoCore::BufferCache;
    static std::array<u64, 6> path_calls_before{}, path_bytes_before{};
    if (BbStats::enabled) {
        ++stat_dispatches;
        stat_ranges += copies.size();
        stat_batches += batches.size();
        stat_saved += in_order - batches.size();
        static auto window = std::chrono::steady_clock::now();
        const auto now = std::chrono::steady_clock::now();
        if (now - window >= std::chrono::seconds(5)) {
            const double seconds = std::chrono::duration<double>(now - window).count();
            std::printf("HLE copy shader per second: %.0f dispatches, %.0f ranges, %.0f batches "
                        "(%.0f fewer by destination grouping)\n",
                        stat_dispatches / seconds, stat_ranges / seconds, stat_batches / seconds,
                        stat_saved / seconds);
            // [source, destination] x [in place, stream, VRAM]: calls/s and KiB per call.
            const char* names[6] = {"src in place", "src stream", "src VRAM",
                                    "dst in place", "dst stream", "dst VRAM"};
            std::printf("  HLE copy ObtainBuffer paths:");
            for (u32 i = 0; i < 6; ++i) {
                const u64 calls = BufferCache::hle_path_calls[i] - path_calls_before[i];
                const u64 bytes = BufferCache::hle_path_bytes[i] - path_bytes_before[i];
                if (calls) {
                    std::printf(" %s %.0f/s %.1f KiB,", names[i], calls / seconds,
                                bytes / 1024.0 / calls);
                }
            }
            std::printf("\n");
            path_calls_before = BufferCache::hle_path_calls;
            path_bytes_before = BufferCache::hle_path_bytes;
            window = now;
            stat_dispatches = stat_ranges = stat_batches = stat_saved = 0;
        }
    }
    GpuSavings::Get().hle_batches.fetch_add(batches.size(), std::memory_order_relaxed);
    // bbport (BB_HLE_COPY_MERGE): every batch's buffers obtained first (uploads and residency
    // all before the copies), then one barrier check and one dispatch per run of batches on the
    // same buffers, instead of a barrier check and a dispatch per batch. Only when no source
    // range overlaps a destination range in guest memory: the order of the batches cannot
    // matter then (the guest's single dispatch has no order between its ranges either).
    if (batches.size() > 1 && GpuSavings::HleMergeOn() &&
        !BbToggle::Disabled(BbToggle::MultiCopyShader)) {
        vk::DeviceSize src_lo = ~0ull, src_hi = 0, dst_lo = ~0ull, dst_hi = 0;
        for (const auto& batch : batches) {
            src_lo = std::min(src_lo, batch.src_min);
            src_hi = std::max(src_hi, batch.src_max);
            dst_lo = std::min(dst_lo, batch.dst_min);
            dst_hi = std::max(dst_hi, batch.dst_max);
        }
        const VAddr src_base = src_buf_sharp.base_address;
        const VAddr dst_base = dst_buf_sharp.base_address;
        const bool overlap =
            (src_base + src_lo < dst_base + dst_hi && dst_base + dst_lo < src_base + src_hi) ||
            DestinationsShared(copies, batches);
        if (!overlap) {
            struct Obtained {
                const VideoCore::Buffer* src;
                const VideoCore::Buffer* dst;
                AccessRange range;
            };
            static std::vector<Obtained> obtained;
            static std::vector<AccessRange> run_ranges;
            obtained.clear();
            u32 start = 0;
            for (const auto& batch : batches) {
                BbSections::Scope obtain_scope{BbSections::HleObtain};
                VideoCore::BufferCache::hle_obtain = BbStats::enabled;
                const auto [src_buf, src_buf_offset] = buffer_cache.ObtainBuffer(
                    src_base + batch.src_min, batch.src_max - batch.src_min, false);
                const auto [dst_buf, dst_buf_offset] = buffer_cache.ObtainBuffer(
                    dst_base + batch.dst_min, batch.dst_max - batch.dst_min, true);
                VideoCore::BufferCache::hle_obtain = false;
                obtain_scope.End();
                for (auto& copy : std::span{copies}.subspan(start, batch.end - start)) {
                    copy.srcOffset = copy.srcOffset - batch.src_min + src_buf_offset;
                    copy.dstOffset = copy.dstOffset - batch.dst_min + dst_buf_offset;
                }
                obtained.push_back(
                    {src_buf, dst_buf,
                     {src_buf_offset, src_buf_offset + (batch.src_max - batch.src_min),
                      dst_buf_offset, dst_buf_offset + (batch.dst_max - batch.dst_min)}});
                start = batch.end;
            }
            BB_SECTION(HleCopy);
            u32 first = 0, copy_start = 0;
            while (first < batches.size()) {
                u32 last = first + 1;
                while (last < batches.size() && obtained[last].src == obtained[first].src &&
                       obtained[last].dst == obtained[first].dst) {
                    ++last;
                }
                const u32 copy_end = batches[last - 1].end;
                run_ranges.clear();
                for (u32 b = first; b < last; ++b) {
                    run_ranges.push_back(obtained[b].range);
                }
                const auto run = std::span{copies}.subspan(copy_start, copy_end - copy_start);
                if (!MultiCopy(rasterizer, obtained[first].src, obtained[first].dst, run,
                               run_ranges)) {
                    // Batch by batch, as without the merge.
                    u32 at = copy_start;
                    for (u32 b = first; b < last; ++b) {
                        const auto part = std::span{copies}.subspan(at, batches[b].end - at);
                        if (!MultiCopy(rasterizer, obtained[b].src, obtained[b].dst, part)) {
                            runtime.CopyBuffer(obtained[b].src, obtained[b].dst, part);
                        }
                        at = batches[b].end;
                    }
                }
                first = last;
                copy_start = copy_end;
            }
            return true;
        }
    }
    u32 batch_start = 0;
    for (const auto& batch : batches) {
        const u32 batch_end = batch.end;
        const auto src_offset_min = batch.src_min;
        const auto src_offset_max = batch.src_max;
        const auto dst_offset_min = batch.dst_min;
        const auto dst_offset_max = batch.dst_max;

        // Obtain buffers for the total source and destination ranges.
        BbSections::Scope obtain_scope{BbSections::HleObtain};
        VideoCore::BufferCache::hle_obtain = BbStats::enabled;
        const auto [src_buf, src_buf_offset] = buffer_cache.ObtainBuffer(
            src_buf_sharp.base_address + src_offset_min, src_offset_max - src_offset_min, false);
        const auto [dst_buf, dst_buf_offset] = buffer_cache.ObtainBuffer(
            dst_buf_sharp.base_address + dst_offset_min, dst_offset_max - dst_offset_min, true);
        VideoCore::BufferCache::hle_obtain = false;
        obtain_scope.End();
        BB_SECTION(HleCopy);

        // Apply found buffer base.
        const auto vk_copies = std::span{copies}.subspan(batch_start, batch_end - batch_start);
        for (auto& copy : vk_copies) {
            copy.srcOffset = copy.srcOffset - src_offset_min + src_buf_offset;
            copy.dstOffset = copy.dstOffset - dst_offset_min + dst_buf_offset;
        }

        // Execute buffer copies.
        LOG_TRACE(Render_Vulkan, "HLE buffer copy: src_size = {}, dst_size = {}",
                  src_offset_max - src_offset_min, dst_offset_max - dst_offset_min);
        if (!MultiCopy(rasterizer, src_buf, dst_buf, vk_copies)) {
            runtime.CopyBuffer(src_buf, dst_buf, vk_copies);
        }
        batch_start = batch_end;
    }

    return true;
}

bool ExecuteShaderHLE(const Shader::Info& info, const AmdGpu::Regs& regs,
                      const AmdGpu::ComputeProgram& cs_program, Rasterizer& rasterizer) {
    switch (info.pgm_hash) {
    case COPY_SHADER_HASH:
        return ExecuteCopyShaderHLE(info, cs_program, rasterizer);
    default:
        return false;
    }
}

} // namespace Vulkan
