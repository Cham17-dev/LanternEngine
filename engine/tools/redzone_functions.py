"""Recipes that move a red-zone leaf function's locals above rsp: patches/redzone_functions.json.

link_modules.protect_red_zones runs the stores of red-zone leaf functions with rsp 128 bytes
lower, so a write fault on a GPU-tracked page does not land on the function's locals. Anything
else Windows writes below a guest thread's rsp (another fault kind, a context the kernel pushes)
still does, and the effect ribbon writers (0x28ce7b0, 0x28ceec0) kept losing [rsp-0x60] on a 14th
gen Intel CPU with every store protected (guest+0x28cf0d6, GXWorker).

This tool describes each listed function so the linker can run a copy of it whose whole body
sits 128 bytes lower: `lea rsp, [rsp-0x80]` after the prologue's `sub rsp`, `lea rsp, [rsp+0x80]`
before the epilogue's `add rsp`, and every [rsp+d] operand in between as [rsp+d+0x80]. The red
zone then lies inside the frame, so nothing below rsp is live any more. The recipe holds no game
code: the function's sha256 and, per instruction, its length and what to change.

Instruction kinds (link_modules.rewrite_red_zone_functions and start/prepare.cpp follow them):
  [n]                      copy n bytes
  [n, 1, modrm, disp, w]   [rsp+d] operand: ModRM at `modrm`, displacement at `disp`, w bytes (0/1/4)
  [n, 2, disp]             rip-relative operand: disp32 at `disp`, retargeted for the copy
  [n, 3, cc, target]       jump inside the function (cc 0-15: jcc, 16: jmp), emitted as rel32;
                           target: offset from the function start
  [n, 4]                   the prologue's `sub rsp`: copied, then lea rsp, [rsp-0x80]
  [n, 5]                   the epilogue's `add rsp`: lea rsp, [rsp+0x80], then copied

Developer tool (pip install capstone):
  python tools/redzone_functions.py <game dir with eboot.bin> [function offsets, hex]
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import capstone
from capstone import x86

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(Path(__file__).resolve().parent))
from redzone_sites import load  # noqa: E402

RSP, RIP = x86.X86_REG_RSP, x86.X86_REG_RIP
# The effect ribbon writers (attack trails, boss effect ribbons): link_modules.protect_red_zones.
DEFAULT = (0x28ce7b0, 0x28ceec0)
JCC = {0x70 + cc: cc for cc in range(16)}


def recipe(md, image, start, end, relocs=()):
    insns = list(md.disasm(image[start:end], start))
    if not insns or sum(i.size for i in insns) != end - start:
        raise ValueError(f'{start:#x}: does not disassemble to its end')
    while insns and insns[-1].mnemonic in ('nop', 'int3'):
        insns.pop()  # alignment padding before the next function
    end = insns[-1].address + insns[-1].size
    if any(start <= r < end + 8 and r + 8 > start for r in relocs):
        raise ValueError(f'{start:#x}: holds a relocation')
    out, state = [], 'prologue'
    for i in insns:
        ops = i.operands
        if i.group(capstone.CS_GRP_CALL):
            raise ValueError(f'{i.address:#x}: call (not a leaf)')
        is_rsp_imm = len(ops) == 2 and ops[0].type == x86.X86_OP_REG and ops[0].reg == RSP and \
            ops[1].type == x86.X86_OP_IMM
        if state == 'prologue':
            if i.mnemonic == 'mov' and 'rbp, rsp' in i.op_str:
                raise ValueError(f'{i.address:#x}: frame pointer (rbp-based red zone) not supported')
            if i.mnemonic == 'push':
                out.append([i.size])
                continue
            if i.mnemonic == 'sub' and is_rsp_imm:
                out.append([i.size, 4])
                state = 'body'
                continue
            raise ValueError(f'{i.address:#x}: {i.mnemonic} {i.op_str} before the frame is set up')
        if state == 'body' and i.mnemonic == 'add' and is_rsp_imm:
            out.append([i.size, 5])
            state = 'epilogue'
            continue
        if state == 'epilogue':
            if i.mnemonic in ('pop', 'ret'):
                out.append([i.size])
                if i.mnemonic == 'ret':
                    state = 'done'
                continue
            raise ValueError(f'{i.address:#x}: {i.mnemonic} in the epilogue')
        if state == 'done':
            raise ValueError(f'{i.address:#x}: code after the return (several exits)')
        _, written = i.regs_access()
        if RSP in written or x86.X86_REG_ESP in written or i.mnemonic in ('push', 'pop', 'ret', 'leave', 'enter'):
            raise ValueError(f'{i.address:#x}: {i.mnemonic} {i.op_str} changes rsp in the body')
        if i.group(capstone.CS_GRP_JUMP):
            op = ops[0]
            if op.type != x86.X86_OP_IMM or not start <= op.imm < end:
                raise ValueError(f'{i.address:#x}: jump out of the function or indirect')
            first = i.bytes[0]
            if first == 0xeb or first == 0xe9:
                cc = 16
            elif first in JCC:
                cc = JCC[first]
            elif first == 0x0f and 0x80 <= i.bytes[1] <= 0x8f:
                cc = i.bytes[1] - 0x80
            else:
                raise ValueError(f'{i.address:#x}: {i.mnemonic} (loop/jrcxz) not supported')
            out.append([i.size, 3, cc, op.imm - start])
            continue
        mem = [op for op in ops if op.type == x86.X86_OP_MEM]
        if any(op.mem.index == RSP for op in mem):
            raise ValueError(f'{i.address:#x}: rsp as an index')
        rsp = [op for op in mem if op.mem.base == RSP]
        if rsp:
            if i.modrm_offset == 0 or image[i.address + i.modrm_offset] & 7 != 4:
                raise ValueError(f'{i.address:#x}: unexpected [rsp] encoding')
            if i.disp_size not in (0, 1, 4):
                raise ValueError(f'{i.address:#x}: displacement size {i.disp_size}')
            out.append([i.size, 1, i.modrm_offset, i.disp_offset if i.disp_size else i.modrm_offset + 2,
                        i.disp_size])
        elif any(op.mem.base == RIP for op in mem):
            if i.disp_size != 4:
                raise ValueError(f'{i.address:#x}: rip-relative without disp32')
            out.append([i.size, 2, i.disp_offset])
        else:
            out.append([i.size])
    if state != 'done':
        raise ValueError(f'{start:#x}: prologue/body/epilogue not found ({state})')
    # Merge runs of plain copies (smaller file); a jump target starts an item of its own.
    targets = {item[3] for item in out if len(item) > 1 and item[1] == 3}
    merged, pos = [], 0
    for item in out:
        if len(item) == 1 and merged and len(merged[-1]) == 1 and pos not in targets:
            merged[-1][0] += item[0]
        else:
            merged.append(item)
        pos += item[0]
    return dict(offset=start, size=end - start, sha256=hashlib.sha256(image[start:end]).hexdigest(),
                insns=merged)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('game', type=Path)
    p.add_argument('functions', nargs='*', help='function start offsets (hex); default: the ribbon writers')
    p.add_argument('-o', '--out', type=Path, default=ROOT / 'patches' / 'redzone_functions.json')
    a = p.parse_args()
    image, starts = load(a.game)
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    md.detail = True
    wanted = [int(f, 16) for f in a.functions] or list(DEFAULT)
    functions = []
    for start in wanted:
        if start not in starts:
            raise SystemExit(f'{start:#x} is not a function start in .eh_frame_hdr')
        end = starts[starts.index(start) + 1]
        functions.append(recipe(md, image, start, end))
    a.out.write_text(json.dumps(dict(eboot='Bloodborne 01.09', functions=functions),
                                separators=(',', ':')) + '\n', encoding='ascii')
    print(f'{len(functions)} functions -> {a.out}')


if __name__ == '__main__':
    main()
