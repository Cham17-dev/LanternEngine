#!/usr/bin/env bash
# Starts Bloodborne with the Lantern Engine from a terminal (Linux package; the launcher, "Lantern
# Engine", does the same with its settings).
#
#   ./lantern.sh [game folder] [bb-probe options...]
#
# The game folder holds the game's files (eboot.bin, sce_module, ...): the first argument, else
# BB_GAME_DIR, else the last one used, else CUSA03173 next to this folder or inside it.
# Settings are lantern.ini here (the in-game menu: Insert or L3+R3); saves, mods and logs stay in
# this folder too.
set -euo pipefail
here=$(cd -- "$(dirname -- "$0")" && pwd)
export BB_DATA_DIR=${BB_DATA_DIR:-$here}
remembered=$here/engine/out/game_dir.txt
game=${BB_GAME_DIR:-}
if [[ $# -gt 0 && -f $1/eboot.bin ]]; then
    game=$(cd -- "$1" && pwd)
    shift
fi
if [[ -z $game && -f $remembered ]]; then
    game=$(<"$remembered")
fi
if [[ -z $game || ! -f $game/eboot.bin ]]; then
    for candidate in "$here/../CUSA03173" "$here/CUSA03173"; do
        if [[ -f $candidate/eboot.bin ]]; then game=$(cd -- "$candidate" && pwd); break; fi
    done
fi
if [[ -z $game || ! -f $game/eboot.bin ]]; then
    echo 'No game folder found: pass it as the first argument (the folder with eboot.bin),' >&2
    echo "for example: $0 ~/Games/CUSA03173" >&2
    exit 1
fi
printf '%s\n' "$game" > "$remembered"
exec "$here/engine/out/bb-start" --game-dir "$game" "$@"
