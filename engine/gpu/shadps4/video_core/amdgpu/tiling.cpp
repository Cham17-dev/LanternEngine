// SPDX-FileCopyrightText: Copyright 2025 shadPS4 Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

#include <algorithm>
#include <atomic>
#include <bit>
#include "common/assert.h"
#include "common/logging/log.h"
#include "video_core/amdgpu/tiling.h"

#include <magic_enum/magic_enum.hpp>

namespace AmdGpu {

static constexpr u32 MICROTILE_SIZE = 8;
static constexpr u32 DRAM_ROW_SIZE = 1024;

/// bbport: a mode outside the tables (a garbage or unsupported descriptor) is reported a few
/// times and treated as a known one (linear, 1x1_16 banks); it ended the game (UNREACHABLE,
/// exit 23). At worst that surface renders wrong.
static void NoteUnknown(const char* what, u32 value) {
    static std::atomic<u32> notes{0};
    if (notes.fetch_add(1, std::memory_order_relaxed) < 8) {
        LOG_ERROR(Render, "Unknown {} = {}, using a fallback", what, value);
    }
}

std::string_view NameOf(TileMode tile_mode) {
    return magic_enum::enum_name(tile_mode);
}

ArrayMode GetArrayMode(TileMode tile_mode) {
    switch (tile_mode) {
    case TileMode::Depth1DThin:
    case TileMode::Display1DThin:
    case TileMode::Thin1DThin:
        return ArrayMode::Array1DTiledThin1;
    case TileMode::Depth2DThin64:
    case TileMode::Depth2DThin128:
    case TileMode::Depth2DThin256:
    case TileMode::Depth2DThin512:
    case TileMode::Depth2DThin1K:
    case TileMode::Display2DThin:
    case TileMode::Thin2DThin:
        return ArrayMode::Array2DTiledThin1;
    case TileMode::DisplayThinPrt:
    case TileMode::ThinThinPrt:
        return ArrayMode::ArrayPrtTiledThin1;
    case TileMode::Depth2DThinPrt256:
    case TileMode::Depth2DThinPrt1K:
    case TileMode::Display2DThinPrt:
    case TileMode::Thin2DThinPrt:
        return ArrayMode::ArrayPrt2DTiledThin1;
    case TileMode::Thin3DThin:
    case TileMode::Thin3DThinPrt:
        return ArrayMode::Array3DTiledThin1;
    case TileMode::Thick1DThick:
        return ArrayMode::Array1DTiledThick;
    case TileMode::Thick2DThick:
        return ArrayMode::Array2DTiledThick;
    case TileMode::Thick3DThick:
        return ArrayMode::Array3DTiledThick;
    case TileMode::ThickThickPrt:
        return ArrayMode::ArrayPrtTiledThick;
    case TileMode::Thick2DThickPrt:
        return ArrayMode::ArrayPrt2DTiledThick;
    case TileMode::Thick3DThickPrt:
        return ArrayMode::ArrayPrt3DTiledThick;
    case TileMode::Thick2DXThick:
        return ArrayMode::Array2DTiledXThick;
    case TileMode::Thick3DXThick:
        return ArrayMode::Array3DTiledXThick;
    case TileMode::DisplayLinearAligned:
        return ArrayMode::ArrayLinearAligned;
    case TileMode::DisplayLinearGeneral:
        return ArrayMode::ArrayLinearGeneral;
    default:
        NoteUnknown("tile mode", u32(tile_mode));
        return ArrayMode::ArrayLinearAligned;
    }
}

MicroTileMode GetMicroTileMode(TileMode tile_mode) {
    switch (tile_mode) {
    case TileMode::Depth2DThin64:
    case TileMode::Depth2DThin128:
    case TileMode::Depth2DThin256:
    case TileMode::Depth2DThin512:
    case TileMode::Depth2DThin1K:
    case TileMode::Depth1DThin:
    case TileMode::Depth2DThinPrt256:
    case TileMode::Depth2DThinPrt1K:
        return MicroTileMode::Depth;
    case TileMode::DisplayLinearAligned:
    case TileMode::Display1DThin:
    case TileMode::Display2DThin:
    case TileMode::DisplayThinPrt:
    case TileMode::Display2DThinPrt:
    case TileMode::DisplayLinearGeneral:
        return MicroTileMode::Display;
    case TileMode::Thin1DThin:
    case TileMode::Thin2DThin:
    case TileMode::Thin3DThin:
    case TileMode::ThinThinPrt:
    case TileMode::Thin2DThinPrt:
    case TileMode::Thin3DThinPrt:
        return MicroTileMode::Thin;
    case TileMode::Thick1DThick:
    case TileMode::Thick2DThick:
    case TileMode::Thick3DThick:
    case TileMode::ThickThickPrt:
    case TileMode::Thick2DThickPrt:
    case TileMode::Thick3DThickPrt:
    case TileMode::Thick2DXThick:
    case TileMode::Thick3DXThick:
        return MicroTileMode::Thick;
    default:
        NoteUnknown("tile mode", u32(tile_mode));
        return MicroTileMode::Display;
    }
}

PipeConfig GetPipeConfig(TileMode tile_mode) {
    switch (tile_mode) {
    case TileMode::Depth2DThin64:
    case TileMode::Depth2DThin128:
    case TileMode::Depth2DThin256:
    case TileMode::Depth2DThin512:
    case TileMode::Depth2DThin1K:
    case TileMode::Depth1DThin:
    case TileMode::Depth2DThinPrt256:
    case TileMode::Depth2DThinPrt1K:
    case TileMode::DisplayLinearAligned:
    case TileMode::Display1DThin:
    case TileMode::Display2DThin:
    case TileMode::Display2DThinPrt:
    case TileMode::Thin1DThin:
    case TileMode::Thin2DThin:
    case TileMode::Thin2DThinPrt:
    case TileMode::Thin3DThinPrt:
    case TileMode::Thick1DThick:
    case TileMode::Thick2DThick:
    case TileMode::Thick2DThickPrt:
    case TileMode::Thick2DXThick:
        return PipeConfig::P8_32x32_16x16;
    case TileMode::DisplayThinPrt:
    case TileMode::Thin3DThin:
    case TileMode::ThinThinPrt:
    case TileMode::Thick3DThick:
    case TileMode::ThickThickPrt:
    case TileMode::Thick3DThickPrt:
    case TileMode::Thick3DXThick:
        return PipeConfig::P8_32x32_8x16;
    case TileMode::DisplayLinearGeneral:
        return PipeConfig::P2;
    default:
        NoteUnknown("tile mode", u32(tile_mode));
        return PipeConfig::P8_32x32_16x16;
    }
}

PipeConfig GetAltPipeConfig(TileMode tile_mode) {
    switch (tile_mode) {
    case TileMode::Depth2DThin64:
    case TileMode::Depth2DThin128:
    case TileMode::Depth2DThin256:
    case TileMode::Depth2DThin512:
    case TileMode::Depth2DThin1K:
    case TileMode::Depth1DThin:
    case TileMode::Depth2DThinPrt256:
    case TileMode::Depth2DThinPrt1K:
    case TileMode::DisplayLinearAligned:
    case TileMode::Display1DThin:
    case TileMode::Display2DThin:
    case TileMode::DisplayThinPrt:
    case TileMode::Display2DThinPrt:
    case TileMode::Thin1DThin:
    case TileMode::Thin2DThin:
    case TileMode::Thin3DThin:
    case TileMode::ThinThinPrt:
    case TileMode::Thin2DThinPrt:
    case TileMode::Thin3DThinPrt:
    case TileMode::Thick1DThick:
    case TileMode::Thick2DThick:
    case TileMode::Thick3DThick:
    case TileMode::ThickThickPrt:
    case TileMode::Thick2DThickPrt:
    case TileMode::Thick3DThickPrt:
    case TileMode::Thick2DXThick:
    case TileMode::Thick3DXThick:
        return PipeConfig::P16_32x32_8x16;
    case TileMode::DisplayLinearGeneral:
        return PipeConfig::P2;
    default:
        NoteUnknown("tile mode", u32(tile_mode));
        return PipeConfig::P16_32x32_8x16;
    }
}

u32 GetSampleSplit(TileMode tile_mode) {
    switch (tile_mode) {
    case TileMode::Depth2DThin64:
    case TileMode::Depth2DThin128:
    case TileMode::Depth2DThin256:
    case TileMode::Depth2DThin512:
    case TileMode::Depth2DThin1K:
    case TileMode::Depth1DThin:
    case TileMode::Depth2DThinPrt256:
    case TileMode::Depth2DThinPrt1K:
    case TileMode::DisplayLinearAligned:
    case TileMode::Display1DThin:
    case TileMode::Thin1DThin:
    case TileMode::Thick1DThick:
    case TileMode::Thick2DThick:
    case TileMode::Thick3DThick:
    case TileMode::ThickThickPrt:
    case TileMode::Thick2DThickPrt:
    case TileMode::Thick3DThickPrt:
    case TileMode::Thick2DXThick:
    case TileMode::Thick3DXThick:
    case TileMode::DisplayLinearGeneral:
        return 1;
    case TileMode::Display2DThin:
    case TileMode::DisplayThinPrt:
    case TileMode::Display2DThinPrt:
    case TileMode::Thin2DThin:
    case TileMode::Thin3DThin:
    case TileMode::ThinThinPrt:
    case TileMode::Thin2DThinPrt:
    case TileMode::Thin3DThinPrt:
        return 2;
    default:
        NoteUnknown("tile mode", u32(tile_mode));
        return 1;
    }
}

u32 GetTileSplitHw(TileMode tile_mode) {
    switch (tile_mode) {
    case TileMode::Depth2DThin64:
    case TileMode::Depth1DThin:
    case TileMode::DisplayLinearAligned:
    case TileMode::Display1DThin:
    case TileMode::Display2DThin:
    case TileMode::DisplayThinPrt:
    case TileMode::Display2DThinPrt:
    case TileMode::Thin1DThin:
    case TileMode::Thin2DThin:
    case TileMode::Thin3DThin:
    case TileMode::ThinThinPrt:
    case TileMode::Thin2DThinPrt:
    case TileMode::Thin3DThinPrt:
    case TileMode::Thick1DThick:
    case TileMode::Thick2DThick:
    case TileMode::Thick3DThick:
    case TileMode::ThickThickPrt:
    case TileMode::Thick2DThickPrt:
    case TileMode::Thick3DThickPrt:
    case TileMode::Thick2DXThick:
    case TileMode::Thick3DXThick:
    case TileMode::DisplayLinearGeneral:
        return 64;
    case TileMode::Depth2DThin128:
        return 128;
    case TileMode::Depth2DThin256:
    case TileMode::Depth2DThinPrt256:
        return 256;
    case TileMode::Depth2DThin512:
        return 512;
    case TileMode::Depth2DThin1K:
    case TileMode::Depth2DThinPrt1K:
        return 1024;
    default:
        NoteUnknown("tile mode", u32(tile_mode));
        return 64;
    }
}

u32 GetBankWidth(MacroTileMode mode) {
    switch (mode) {
    case MacroTileMode::Mode_1x4_16:
    case MacroTileMode::Mode_1x2_16:
    case MacroTileMode::Mode_1x1_16:
    case MacroTileMode::Mode_1x1_16_Dup:
    case MacroTileMode::Mode_1x1_8:
    case MacroTileMode::Mode_1x1_4:
    case MacroTileMode::Mode_1x1_2:
    case MacroTileMode::Mode_1x1_2_Dup:
    case MacroTileMode::Mode_1x8_16:
    case MacroTileMode::Mode_1x4_16_Dup:
    case MacroTileMode::Mode_1x2_16_Dup:
    case MacroTileMode::Mode_1x1_16_Dup2:
    case MacroTileMode::Mode_1x1_8_Dup:
    case MacroTileMode::Mode_1x1_4_Dup:
    case MacroTileMode::Mode_1x1_2_Dup2:
    case MacroTileMode::Mode_1x1_2_Dup3:
        return 1;
    default:
        NoteUnknown("macro tile mode", u32(mode));
        return 1;
    }
}

u32 GetBankHeight(MacroTileMode mode) {
    switch (mode) {
    case MacroTileMode::Mode_1x1_16:
    case MacroTileMode::Mode_1x1_16_Dup:
    case MacroTileMode::Mode_1x1_8:
    case MacroTileMode::Mode_1x1_4:
    case MacroTileMode::Mode_1x1_2:
    case MacroTileMode::Mode_1x1_2_Dup:
    case MacroTileMode::Mode_1x1_16_Dup2:
    case MacroTileMode::Mode_1x1_8_Dup:
    case MacroTileMode::Mode_1x1_4_Dup:
    case MacroTileMode::Mode_1x1_2_Dup2:
    case MacroTileMode::Mode_1x1_2_Dup3:
        return 1;
    case MacroTileMode::Mode_1x2_16:
    case MacroTileMode::Mode_1x2_16_Dup:
        return 2;
    case MacroTileMode::Mode_1x4_16:
    case MacroTileMode::Mode_1x4_16_Dup:
        return 4;
    case MacroTileMode::Mode_1x8_16:
        return 8;
    default:
        NoteUnknown("macro tile mode", u32(mode));
        return 1;
    }
}

u32 GetNumBanks(MacroTileMode mode) {
    switch (mode) {
    case MacroTileMode::Mode_1x1_2:
    case MacroTileMode::Mode_1x1_2_Dup:
    case MacroTileMode::Mode_1x1_2_Dup2:
    case MacroTileMode::Mode_1x1_2_Dup3:
        return 2;
    case MacroTileMode::Mode_1x1_4:
    case MacroTileMode::Mode_1x1_4_Dup:
        return 4;
    case MacroTileMode::Mode_1x1_8:
    case MacroTileMode::Mode_1x1_8_Dup:
        return 8;
    case MacroTileMode::Mode_1x4_16:
    case MacroTileMode::Mode_1x2_16:
    case MacroTileMode::Mode_1x1_16:
    case MacroTileMode::Mode_1x1_16_Dup:
    case MacroTileMode::Mode_1x8_16:
    case MacroTileMode::Mode_1x4_16_Dup:
    case MacroTileMode::Mode_1x2_16_Dup:
    case MacroTileMode::Mode_1x1_16_Dup2:
        return 16;
    default:
        NoteUnknown("macro tile mode", u32(mode));
        return 16;
    }
}

u32 GetMacrotileAspect(MacroTileMode mode) {
    switch (mode) {
    case MacroTileMode::Mode_1x1_8:
    case MacroTileMode::Mode_1x1_4:
    case MacroTileMode::Mode_1x1_2:
    case MacroTileMode::Mode_1x1_2_Dup:
    case MacroTileMode::Mode_1x1_8_Dup:
    case MacroTileMode::Mode_1x1_4_Dup:
    case MacroTileMode::Mode_1x1_2_Dup2:
    case MacroTileMode::Mode_1x1_2_Dup3:
        return 1;
    case MacroTileMode::Mode_1x2_16:
    case MacroTileMode::Mode_1x1_16:
    case MacroTileMode::Mode_1x1_16_Dup:
    case MacroTileMode::Mode_1x2_16_Dup:
    case MacroTileMode::Mode_1x1_16_Dup2:
        return 2;
    case MacroTileMode::Mode_1x4_16:
    case MacroTileMode::Mode_1x8_16:
    case MacroTileMode::Mode_1x4_16_Dup:
        return 4;
    default:
        NoteUnknown("macro tile mode", u32(mode));
        return 2;
    }
}

u32 GetAltBankHeight(MacroTileMode mode) {
    switch (mode) {
    case MacroTileMode::Mode_1x1_8:
    case MacroTileMode::Mode_1x1_4:
    case MacroTileMode::Mode_1x1_2:
    case MacroTileMode::Mode_1x1_2_Dup:
    case MacroTileMode::Mode_1x1_16_Dup2:
    case MacroTileMode::Mode_1x1_8_Dup:
    case MacroTileMode::Mode_1x1_4_Dup:
    case MacroTileMode::Mode_1x1_2_Dup2:
    case MacroTileMode::Mode_1x1_2_Dup3:
        return 1;
    case MacroTileMode::Mode_1x1_16:
    case MacroTileMode::Mode_1x1_16_Dup:
    case MacroTileMode::Mode_1x2_16_Dup:
        return 2;
    case MacroTileMode::Mode_1x4_16:
    case MacroTileMode::Mode_1x2_16:
    case MacroTileMode::Mode_1x8_16:
    case MacroTileMode::Mode_1x4_16_Dup:
        return 4;
    default:
        NoteUnknown("macro tile mode", u32(mode));
        return 2;
    }
}

u32 GetAltNumBanks(MacroTileMode mode) {
    switch (mode) {
    case MacroTileMode::Mode_1x1_2_Dup:
    case MacroTileMode::Mode_1x1_2_Dup2:
    case MacroTileMode::Mode_1x1_2_Dup3:
        return 2;
    case MacroTileMode::Mode_1x1_2:
    case MacroTileMode::Mode_1x1_8_Dup:
    case MacroTileMode::Mode_1x1_4_Dup:
        return 4;
    case MacroTileMode::Mode_1x4_16:
    case MacroTileMode::Mode_1x2_16:
    case MacroTileMode::Mode_1x1_16:
    case MacroTileMode::Mode_1x1_16_Dup:
    case MacroTileMode::Mode_1x1_8:
    case MacroTileMode::Mode_1x1_4:
    case MacroTileMode::Mode_1x4_16_Dup:
    case MacroTileMode::Mode_1x2_16_Dup:
    case MacroTileMode::Mode_1x1_16_Dup2:
        return 8;
    case MacroTileMode::Mode_1x8_16:
        return 16;
    default:
        NoteUnknown("macro tile mode", u32(mode));
        return 8;
    }
}

u32 GetAltMacrotileAspect(MacroTileMode mode) {
    switch (mode) {
    case MacroTileMode::Mode_1x1_16:
    case MacroTileMode::Mode_1x1_16_Dup:
    case MacroTileMode::Mode_1x1_8:
    case MacroTileMode::Mode_1x1_4:
    case MacroTileMode::Mode_1x1_2:
    case MacroTileMode::Mode_1x1_2_Dup:
    case MacroTileMode::Mode_1x2_16_Dup:
    case MacroTileMode::Mode_1x1_16_Dup2:
    case MacroTileMode::Mode_1x1_8_Dup:
    case MacroTileMode::Mode_1x1_4_Dup:
    case MacroTileMode::Mode_1x1_2_Dup2:
    case MacroTileMode::Mode_1x1_2_Dup3:
        return 1;
    case MacroTileMode::Mode_1x4_16:
    case MacroTileMode::Mode_1x2_16:
    case MacroTileMode::Mode_1x8_16:
    case MacroTileMode::Mode_1x4_16_Dup:
        return 2;
    default:
        NoteUnknown("macro tile mode", u32(mode));
        return 1;
    }
}

bool IsMacroTiled(ArrayMode array_mode) {
    switch (array_mode) {
    case ArrayMode::ArrayLinearGeneral:
    case ArrayMode::ArrayLinearAligned:
    case ArrayMode::Array1DTiledThin1:
    case ArrayMode::Array1DTiledThick:
        return false;
    case ArrayMode::Array2DTiledThin1:
    case ArrayMode::ArrayPrtTiledThin1:
    case ArrayMode::ArrayPrt2DTiledThin1:
    case ArrayMode::Array2DTiledThick:
    case ArrayMode::Array2DTiledXThick:
    case ArrayMode::ArrayPrtTiledThick:
    case ArrayMode::ArrayPrt2DTiledThick:
    case ArrayMode::ArrayPrt3DTiledThin1:
    case ArrayMode::Array3DTiledThin1:
    case ArrayMode::Array3DTiledThick:
    case ArrayMode::Array3DTiledXThick:
    case ArrayMode::ArrayPrt3DTiledThick:
        return true;
    default:
        NoteUnknown("array mode", u32(array_mode));
        return false;
    }
}

bool IsPrt(ArrayMode array_mode) {
    switch (array_mode) {
    case ArrayMode::ArrayPrtTiledThin1:
    case ArrayMode::ArrayPrtTiledThick:
    case ArrayMode::ArrayPrt2DTiledThin1:
    case ArrayMode::ArrayPrt2DTiledThick:
    case ArrayMode::ArrayPrt3DTiledThin1:
    case ArrayMode::ArrayPrt3DTiledThick:
        return true;
    case ArrayMode::ArrayLinearGeneral:
    case ArrayMode::ArrayLinearAligned:
    case ArrayMode::Array1DTiledThin1:
    case ArrayMode::Array1DTiledThick:
    case ArrayMode::Array2DTiledThin1:
    case ArrayMode::Array2DTiledThick:
    case ArrayMode::Array2DTiledXThick:
    case ArrayMode::Array3DTiledThin1:
    case ArrayMode::Array3DTiledThick:
    case ArrayMode::Array3DTiledXThick:
        return false;
    default:
        NoteUnknown("array mode", u32(array_mode));
        return false;
    }
}

u32 GetMicroTileThickness(ArrayMode array_mode) {
    switch (array_mode) {
    case ArrayMode::ArrayLinearGeneral:
    case ArrayMode::ArrayLinearAligned:
    case ArrayMode::Array1DTiledThin1:
    case ArrayMode::Array2DTiledThin1:
    case ArrayMode::ArrayPrtTiledThin1:
    case ArrayMode::ArrayPrt2DTiledThin1:
    case ArrayMode::ArrayPrt3DTiledThin1:
    case ArrayMode::Array3DTiledThin1:
        return 1;
    case ArrayMode::Array1DTiledThick:
    case ArrayMode::Array2DTiledThick:
    case ArrayMode::Array3DTiledThick:
    case ArrayMode::ArrayPrtTiledThick:
    case ArrayMode::ArrayPrt2DTiledThick:
    case ArrayMode::ArrayPrt3DTiledThick:
        return 4;
    case ArrayMode::Array2DTiledXThick:
    case ArrayMode::Array3DTiledXThick:
        return 8;
    default:
        NoteUnknown("array mode", u32(array_mode));
        return 1;
    }
}

u32 GetPipeCount(PipeConfig pipe_cfg) {
    switch (pipe_cfg) {
    case PipeConfig::P2:
        return 2;
    case PipeConfig::P8_32x32_8x16:
    case PipeConfig::P8_32x32_16x16:
        return 8;
    case PipeConfig::P16_32x32_8x16:
        return 16;
    default:
        NoteUnknown("pipe config", u32(pipe_cfg));
        return 8;
    }
}

u32 CalculateTileSplit(TileMode tile_mode, ArrayMode array_mode, MicroTileMode micro_tile_mode,
                       u32 bpp) {
    const u32 sample_split = GetSampleSplit(tile_mode);
    const u32 tile_split_hw = GetTileSplitHw(tile_mode);
    const u32 tile_thickness = GetMicroTileThickness(array_mode);
    const u32 tile_bytes_1x = (bpp * MICROTILE_SIZE * MICROTILE_SIZE * tile_thickness + 7) / 8;
    const u32 color_tile_split = std::max(256U, sample_split * tile_bytes_1x);
    const u32 tile_split =
        micro_tile_mode == MicroTileMode::Depth ? tile_split_hw : color_tile_split;
    return std::min(DRAM_ROW_SIZE, tile_split);
}

MacroTileMode CalculateMacrotileMode(TileMode tile_mode, u32 bpp, u32 num_samples) {
    // bbport: input out of range (a garbage sharp: MSAA levels, format) is clamped and reported
    // (NoteUnknown) instead of the assertions that ended the game.
    if (!std::has_single_bit(num_samples) || num_samples > 16) {
        NoteUnknown("sample count", num_samples);
        num_samples = 1;
    }
    if (bpp < 1 || bpp > 128) {
        NoteUnknown("bits per element", bpp);
        bpp = std::clamp(bpp, 1u, 128u);
    }

    const ArrayMode array_mode = GetArrayMode(tile_mode);
    if (!IsMacroTiled(array_mode)) {
        NoteUnknown("macro tiled mode", u32(tile_mode));
        return MacroTileMode::Mode_1x1_16;
    }

    const MicroTileMode micro_tile_mode = GetMicroTileMode(tile_mode);
    const u32 tile_thickness = GetMicroTileThickness(array_mode);
    const u32 tile_bytes_1x = bpp * MICROTILE_SIZE * MICROTILE_SIZE * tile_thickness / 8;
    const u32 tilesplic = CalculateTileSplit(tile_mode, array_mode, micro_tile_mode, bpp);
    const u32 tile_bytes = std::min(tilesplic, num_samples * tile_bytes_1x);
    const u32 mtm_idx = std::bit_width(std::max(tile_bytes / 64, 1u)) - 1;
    return IsPrt(array_mode) ? MacroTileMode(mtm_idx + 8) : MacroTileMode(mtm_idx);
}

} // namespace AmdGpu
