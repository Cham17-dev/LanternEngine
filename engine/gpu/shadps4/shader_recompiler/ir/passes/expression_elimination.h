// SPDX-FileCopyrightText: Copyright 2026 shadPS4 Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

#pragma once

#include <algorithm>
#include <unordered_map>
#include <boost/container/small_vector.hpp>

#include "shader_recompiler/ir/basic_block.h"
#include "shader_recompiler/ir/microinstruction.h"
#include "shader_recompiler/ir/opcodes.h"

namespace Shader::Optimization {

static bool IsAllowedInst(const IR::Inst& inst) {
    return inst.GetOpcode() == IR::Opcode::Ballot ||
           inst.GetOpcode() == IR::Opcode::UnpackUint2x32 ||
           inst.GetOpcode() == IR::Opcode::BitCastU32F32 ||
           inst.GetOpcode() == IR::Opcode::BitCastF32U32;
}

static bool IsIdenticalInst(const IR::Inst* a, const IR::Inst* b) {
    if (a->GetOpcode() != b->GetOpcode()) {
        return false;
    }
    for (size_t i = 0; i < a->NumArgs(); i++) {
        if (a->Arg(i) != b->Arg(i)) {
            return false;
        }
    }
    return true;
}

static void RunLocalCSE(IR::Block* block) {
    // bbport: found by hash, not by a scan of every earlier one: a GCN register holds bits, so a
    // block has hundreds of bit casts, and the scan made this pass 10% of a shader's translation
    // (docs/native_shader_compiler.md). The first identical instruction still wins.
    std::unordered_multimap<u64, IR::Inst*> seen;
    auto it = std::ranges::find_if_not(*block, IR::IsPhi);
    for (; it != block->end(); ++it) {
        IR::Inst& inst{*it};
        if (!IsAllowedInst(inst)) {
            continue;
        }
        u64 key = static_cast<u64>(inst.GetOpcode());
        for (size_t i = 0; i < inst.NumArgs(); i++) {
            key = key * 0x9E3779B97F4A7C15ull ^ inst.Arg(i).Hash();
        }
        const auto [first, last] = seen.equal_range(key);
        const auto found = std::find_if(
            first, last, [&](const auto& entry) { return IsIdenticalInst(&inst, entry.second); });
        if (found != last) {
            inst.ReplaceUsesWithAndRemove(IR::Value{found->second});
        } else {
            seen.emplace(key, &inst);
        }
    }
}

} // namespace Shader::Optimization
