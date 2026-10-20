"""Apply the pinned Lantern baseline, then the separate PsychoV30 engine/launcher patches."""
import argparse
from pathlib import Path
import subprocess

here = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('stage', choices=['baseline', 'port'])
parser.add_argument('engine', type=Path)
parser.add_argument('lantern', type=Path)
args = parser.parse_args()
args.engine = args.engine.resolve()
args.lantern = args.lantern.resolve()

def apply(root, patch):
    # Lantern's published patch contains CRLF source. Normalize only files touched by our
    # patch; matching and output are then identical on Windows and Linux.
    if args.stage == 'port':
        for line in patch.read_text().splitlines():
            if line.startswith('+++ b/'):
                path = root / line[6:]
                if path.is_file():
                    data = path.read_bytes()
                    path.write_bytes(data.replace(b'\r\n', b'\n'))
    subprocess.run(['git', '-C', str(root), 'apply', '--check', str(patch)], check=True)
    subprocess.run(['git', '-C', str(root), 'apply', '--whitespace=nowarn', str(patch)], check=True)

if args.stage == 'baseline':
    apply(args.engine, args.lantern / 'engine/bbhost-changes.patch')
else:
    apply(args.engine, here / 'psychov30-engine.patch')
    apply(args.lantern, here / 'psychov30-launcher.patch')

