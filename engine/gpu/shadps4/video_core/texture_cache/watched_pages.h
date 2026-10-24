// SPDX-License-Identifier: GPL-2.0-or-later
// bbport: how many tracked images watch each guest page, readable without the texture cache's
// lock (docs/pipeline_compiles.md, "Write faults"). No Vulkan here: tests/test_pipeline_compile.cpp.

#pragma once

#include <algorithm>
#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <vector>

namespace VideoCore {

/// The pages each tracked image protects (TextureCache::TrackImage and the Untrack functions:
/// the same ranges, counted per page as the page manager counts them), per 4 KiB page. A write
/// fault on a page no tracked image watches (a buffer's page, most faults) needs nothing from
/// the texture cache: the guest thread then does not take its lock, which the draw recording
/// thread holds while it creates an image or uploads one (0.5-0.6 s each with memory short).
///
/// Writers hold the texture cache's lock (one at a time); readers may be any thread. A reader
/// that races with a track sees the page either way: unwatched, its write lands before the
/// image's upload reads the memory; watched, it takes the lock as before. Counts go up before
/// the pages are protected and down after they are unprotected.
class WatchedPages {
public:
    static constexpr std::uint32_t PageBits = 12;      ///< the page manager's 4 KiB pages
    static constexpr std::uint32_t ChunkPageBits = 16; ///< 256 MiB of guest memory per chunk
    static constexpr std::uint32_t AddressBits = 40;   ///< guest addresses (page manager)

    /// `delta` (+1/-1) for every page of [address, address + size), as
    /// PageManager::UpdatePageWatchers counts them. Writers hold the texture cache's lock.
    void Add(std::uint64_t address, std::uint64_t size, int delta) {
        const std::uint64_t first = address >> PageBits;
        const std::uint64_t end = (address + size + PageSize - 1) >> PageBits;
        for (std::uint64_t page = first; page < end; ++page) {
            const std::uint64_t chunk = page >> ChunkPageBits;
            if (chunk >= NumChunks) {
                return;
            }
            Count* counts = chunks[chunk].load(std::memory_order_relaxed);
            if (!counts) {
                if (delta < 0) {
                    continue;
                }
                owned.push_back(std::make_unique<Count[]>(ChunkPages));
                counts = owned.back().get();
                chunks[chunk].store(counts, std::memory_order_release);
            }
            Count& count = counts[page & (ChunkPages - 1)];
            const std::uint32_t now = count.load(std::memory_order_relaxed);
            // One writer: a plain read-modify-write (no locked instruction per page).
            count.store(static_cast<std::uint16_t>(delta < 0 ? (now ? now - 1 : 0) : now + 1),
                        std::memory_order_release);
        }
    }

    /// Whether a tracked image watches a page of [address, address + size). Any thread.
    [[nodiscard]] bool Any(std::uint64_t address, std::uint64_t size) const noexcept {
        const std::uint64_t first = address >> PageBits;
        const std::uint64_t end = (address + std::max<std::uint64_t>(size, 1) + PageSize - 1) >>
                                  PageBits;
        for (std::uint64_t page = first; page < end; ++page) {
            const std::uint64_t chunk = page >> ChunkPageBits;
            if (chunk >= NumChunks) {
                return true; // outside the table: assume watched (the locked path)
            }
            const Count* counts = chunks[chunk].load(std::memory_order_acquire);
            if (counts && counts[page & (ChunkPages - 1)].load(std::memory_order_acquire) != 0) {
                return true;
            }
        }
        return false;
    }

private:
    using Count = std::atomic<std::uint16_t>;
    static constexpr std::uint64_t PageSize = std::uint64_t{1} << PageBits;
    static constexpr std::size_t ChunkPages = std::size_t{1} << ChunkPageBits;
    static constexpr std::size_t NumChunks = std::size_t{1}
                                             << (AddressBits - PageBits - ChunkPageBits);
    std::array<std::atomic<Count*>, NumChunks> chunks{};
    std::vector<std::unique_ptr<Count[]>> owned; ///< writers only
};

} // namespace VideoCore
