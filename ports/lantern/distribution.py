"""Assemble a complete, isolated Windows package from pinned runtime resources.

The released package's executables are not the engine reconstructed from its public
source snapshot. Only its models, DLLs, icons, notices and defaults are reused. The
engine, helper programs, launcher and preparation scripts all come from our build.
"""
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import sys
import urllib.request
from zipfile import ZipFile

BASE_URL = ('https://github.com/Awakenchan/LanternEngine/releases/download/'
            'v0.9.36/LanternEngine-v0.9.36-win64.zip')
BASE_SHA = '38b0de16231da36daa8ffd01f8d2a2be5040209d6f03e8ceef38b1a4c77f385c'
PYTHON_URL = 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip'
PYTHON_SHA = '4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3'


def archive(url, digest, cache):
    path = cache / url.rsplit('/', 1)[-1]
    if not path.exists():
        urllib.request.urlretrieve(url, path)
    if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
        raise RuntimeError(f'Unexpected SHA-256: {path}')
    return ZipFile(path)


def assemble(engine, overlay, destination, cache):
    if destination.exists():
        raise RuntimeError('Use an empty destination; never merge a distribution into a player folder')
    cache.mkdir(parents=True, exist_ok=True)
    destination.mkdir(parents=True)
    runtime = destination / 'engine'
    # PowerShell-built ZIPs have backslashes even when inspected on Linux.
    with archive(BASE_URL, BASE_SHA, cache) as base:
        for member in base.infolist():
            path = PurePosixPath(member.filename.replace('\\', '/'))
            if member.is_dir() or '..' in path.parts or not path.parts or path.parts[0] != 'BloodbornePC':
                continue
            relative = PurePosixPath(*path.parts[1:])
            if not relative.parts:
                continue
            if relative.suffix.lower() == '.exe' and relative.name != 'Bloodborne.exe':
                continue  # no stock launcher/start/probe/helper executables
            if relative.parts[:2] == ('engine', 'out') and relative.suffix.lower() not in ('.dll', '.ico'):
                continue  # no stock cache identifiers or prepared files
            target = destination.joinpath(*relative.parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(base.read(member))
    # Scripts and patches match the compiled runtime, rather than the release binary.
    for name in ('scripts', 'patches'):
        target = runtime / name
        if target.exists():
            shutil.rmtree(target)
        shutil.copytree(engine / name, target,
                        ignore=shutil.ignore_patterns('__pycache__', '*.pyc'))
    shutil.copytree(overlay / 'out', runtime / 'out', dirs_exist_ok=True)
    shutil.copytree(overlay / 'licenses', destination / 'licenses', dirs_exist_ok=True)
    shutil.copy2(overlay / 'README-PsychoV30.md', destination / 'README-PsychoV30.md')
    # The launcher build is Lantern Engine.exe since Lantern 9.8 (Bloodborne Launcher.exe before).
    launcher = overlay / 'Lantern Engine.exe'
    if not launcher.is_file():
        launcher = overlay / 'Bloodborne Launcher.exe'
    shutil.copy2(launcher, destination / 'Lantern Engine.exe')
    # The native Bloodborne.exe forwarder from this release starts Lantern Engine.exe --play.
    python = runtime / 'tools/python'
    python.mkdir(parents=True)
    with archive(PYTHON_URL, PYTHON_SHA, cache) as embedded:
        embedded.extractall(python)
    # Embedded Python ignores the script directory unless explicitly added to its _pth.
    (python / 'python312._pth').write_text('python312.zip\n.\n../../scripts\n', encoding='ascii')
    (destination / 'package.json').write_text(json.dumps({
        'version': '0.9.36-psychov30', 'engine': 'lantern-16d7b51-psychov30-v1',
        'source': 'https://github.com/Cham17-dev/LanternEngine/tree/lantern-psychov30',
        'runtime_resources': 'v0.9.36', 'python': '3.12.10',
    }, indent=2), encoding='utf-8')
    # Explicit shipped HDR defaults, without replacing any player's existing INI.
    ini = destination / 'bbport.ini'
    defaults = dict(line.split('=', 1) for line in (overlay / 'psychov30-example.ini').read_text().splitlines())
    defaults.pop('hdr')  # HDR output remains opt-in; Windows HDR must be enabled too.
    # Peak and paper white come from the display's peak unless the player sets them.
    unset = {'hdr_peak', 'hdr_paper_white'}
    lines = [line for line in ini.read_text(encoding='utf-8-sig').splitlines()
             if line.split('=', 1)[0].strip() not in defaults.keys() | unset]
    ini.write_text('\n'.join(lines + [f'{k}={v}' for k, v in defaults.items()]) + '\n', encoding='utf-8')
    required = ['Lantern Engine.exe', 'Bloodborne.exe', 'engine/out/bb-probe.exe', 'engine/out/bb-start.exe',
                'engine/out/bb-engine-assets.exe', 'engine/out/bb-gpu-capabilities.exe',
                'engine/out/nvngx_dlss.dll', 'engine/scripts/run_windows.py',
                'engine/patches/redzone.json', 'engine/tools/python/python.exe',
                'licenses/RenoDX-MIT.txt']
    for name in required:
        if not (destination / name).is_file():
            raise RuntimeError(f'Incomplete distribution: {name}')
    # out\ at the top is the layout before engine\ (bb-start.exe in engine\out is ours, built here).
    if (destination / 'out').exists():
        raise RuntimeError('Mixed engine layouts in distribution')
    print(f'Complete PsychoV30 package: {destination}')


if __name__ == '__main__':
    assemble(*map(Path, sys.argv[1:5]))
