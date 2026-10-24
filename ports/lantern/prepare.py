"""Apply the separate HDR16 engine and launcher patches to a pinned Lantern checkout.

Since 0.9.37, Lantern's repository holds the engine's full source in engine/: the engine patch
applies to a copy of it, the launcher patch to the checkout itself (a check: the launcher is
built from this fork's own launcher/, which already holds it).
"""
import argparse
from pathlib import Path
import subprocess

here = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('engine', type=Path, help="a copy of the pinned Lantern's engine/ folder")
parser.add_argument('lantern', type=Path, help='the pinned Lantern checkout')
args = parser.parse_args()


def apply(root, patch):
    # Normalize the files the patch touches to LF; matching and output are then identical on
    # Windows and Linux whatever the checkout's line endings.
    for line in patch.read_text(encoding='utf-8').splitlines():
        if line.startswith('+++ b/'):
            path = root / line[6:]
            if path.is_file():
                data = path.read_bytes()
                path.write_bytes(data.replace(b'\r\n', b'\n'))
    # git apply takes the paths from the top of the repository it runs in: the copy is made one
    # of its own (inside this fork's checkout it would otherwise apply to the fork's root).
    if not (root / '.git').exists():
        subprocess.run(['git', 'init', '-q'], cwd=root, check=True)
    subprocess.run(['git', 'apply', '--check', str(patch)], cwd=root, check=True)
    subprocess.run(['git', 'apply', '--whitespace=nowarn', str(patch)], cwd=root, check=True)


apply(args.engine.resolve(), here / 'psychov30-engine.patch')
apply(args.lantern.resolve(), here / 'psychov30-launcher.patch')
