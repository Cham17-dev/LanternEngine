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
# bb-start.exe: the native start (boot image, mods, patches, then bb-probe), as Lantern 9.8 runs.
for name in ['bb-probe.exe', 'bb-start.exe', 'bb-gpu-capabilities.exe', 'bb-engine-assets.exe']:
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
# The repository's README (this port's) is the package's README-PsychoV30.md.
shutil.copy2(Path(__file__).resolve().parents[2] / 'README.md', stage / 'README-PsychoV30.md')
notices = Path(__file__).with_name('licenses')
shutil.copytree(notices, stage / 'licenses', dirs_exist_ok=True)
(stage / 'psychov30-example.ini').write_text(
    # No hdr_peak / hdr_paper_white: the engine and launcher take them from the display's peak.
    'hdr=1\nhdr_mode=psychov30\n'
    'hdr_tonemapper=4\nhdr_exposure=1\nhdr_grade_highlights=50\nhdr_shadows=50\n'
    'hdr_contrast=50\nhdr_saturation=50\nhdr_bloom=50\nhdr_scene_grading=100\n'
    'hdr_black_floor=0\n', encoding='utf-8')
print(f'Engine overlay: {len(seen)} imported DLL names checked, {stage}')

