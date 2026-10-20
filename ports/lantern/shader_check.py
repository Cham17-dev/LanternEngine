"""Compile Lantern's literal host-shader configurations and check the HDR push-constant ABI."""
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile

root = Path(sys.argv[1]).resolve()
source = root / 'gpu/shadps4/video_core/host_shaders'
count = 0
with tempfile.TemporaryDirectory() as temp:
    out = Path(temp) / 'shader.spv'
    for line in (root / 'gpu/CMakeLists.txt').read_text().splitlines():
        match = re.fullmatch(r'add_host_shader\((.*)\)', line)
        if not match:
            continue
        file, name, *defines = match[1].split()
        subprocess.run(['glslangValidator', '--target-env', 'vulkan1.3',
                        *['-D' + d for d in defines], str(source / file), '-o', str(out)], check=True)
        subprocess.run(['spirv-val', '--target-env', 'vulkan1.3', str(out)], check=True)
        if name == 'bb_composite_hdr_frag':
            reflection = json.loads(subprocess.check_output(['spirv-cross', str(out), '--reflect']))
            members = next(t['members'] for t in reflection['types'].values() if t.get('name') == 'AuxData')
            offsets = {m['name']: m['offset'] for m in members}
            expected = dict(buf_offsets0=80, buf_offsets1=96, buf_offsets2=112,
                            motion_param=120, scene_size=124, bb_hdr=128, bb_hdr2a=144, bb_hdr2b=160)
            if any(offsets.get(k) != v for k, v in expected.items()):
                raise RuntimeError(f'HDR push-constant ABI mismatch: {offsets}')
        count += 1
print(f'{count} host shader configurations compiled and validated; HDR push-constant ABI checked')
