"""Windows smoke checks: exercise the shipped embedded Python, not the CI Python."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile

root = Path(sys.argv[1]).resolve()
python = root / 'engine/tools/python/python.exe'
scripts = root / 'engine/scripts'
subprocess.run([python, '-c', 'import mods, prepare, online, patches, engine_assets; print("Preparation imports OK")'],
               cwd=root, check=True)
with tempfile.TemporaryDirectory() as data:
    env = dict(os.environ, BB_DATA_DIR=data, BB_CONFIG=str(Path(data) / 'bbport.ini'),
               BB_ONLINE_PAUSED='1', PYTHONUTF8='1')
    result = subprocess.run([python, scripts / 'run_windows.py', '--game-dir', str(Path(data) / 'missing-game')],
                            cwd=root, env=env, capture_output=True, text=True, encoding='utf-8')
    print(result.stdout)
    print(result.stderr)
    if result.returncode != 1 or 'No eboot.bin' not in result.stderr:
        raise RuntimeError('Packaged preparation entry point did not reach game detection')
assert 'hdr_mode=psychov30' in (root / 'bbport.ini').read_text()
assert 'hdr_paper_white=203' in (root / 'bbport.ini').read_text()
print('Packaged Python and game-detection smoke check passed; an in-game launch is still required.')
