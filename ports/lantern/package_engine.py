"""Produce an overlay for a separate copy of the pinned Lantern Windows package."""
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

engine, stage, launcher = map(Path, sys.argv[1:4])
stage.mkdir(parents=True, exist_ok=True)
out = stage / 'out'
out.mkdir(exist_ok=True)
clang_bin = Path(os.environ['BB_CLANG_BIN'])
queue = []
for name in ['bb-probe.exe', 'bb-gpu-capabilities.exe', 'bb-engine-assets.exe']:
    src = engine / 'out' / name
    if not src.is_file():
        raise RuntimeError(f'Missing build output: {src}')
    dst = out / name
    shutil.copy2(src, dst)
    queue.append(dst)
seen = set()
while queue:
    binary = queue.pop()
    imports = subprocess.check_output(['objdump', '-p', str(binary)], text=True)
    for name in re.findall(r'DLL Name:\s*(\S+)', imports):
        if name.lower() in seen:
            continue
        seen.add(name.lower())
        source = clang_bin / name
        if source.is_file():
            target = out / name
            shutil.copy2(source, target)
            queue.append(target)
        elif (engine / 'out' / name).is_file():
            target = out / name
            shutil.copy2(engine / 'out' / name, target)
            queue.append(target)
        # Windows system DLLs remain provided by Windows; never copy them from the runner.
for src in launcher.iterdir():
    if src.is_file():
        shutil.copy2(src, stage / src.name)
shutil.copy2(Path(__file__).with_name('README.md'), stage / 'README-PsychoV30.md')
(stage / 'psychov30-example.ini').write_text(
    'hdr=1\nhdr_mode=psychov30\nhdr_paper_white=203\nhdr_peak=1000\n'
    'hdr_tonemapper=3\nhdr_exposure=1\nhdr_grade_highlights=50\nhdr_shadows=50\n'
    'hdr_contrast=50\nhdr_saturation=50\nhdr_bloom=50\nhdr_scene_grading=100\n'
    'hdr_black_floor=0\n', encoding='utf-8')
print(f'Engine overlay: {len(seen)} imported DLL names checked, {stage}')

