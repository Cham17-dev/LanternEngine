#!/usr/bin/env bash
# The engine on Mesa's software Vulkan driver (lavapipe) with the Khronos validation layer, to prove
# that what reaches a GPU driver is valid Vulkan before a release (docs/COMPATIBILITY.md, rule 2).
# NVIDIA's driver builds much invalid usage that AMD's ends the process on; lavapipe plus the
# layer reports it on any PC, also as an older GPU family with BB_VK_HIDE.
#
#   tools/validate_pipelines.sh <phase> [family] [seconds]
#
# Phases, in this order the first time:
#   profile  an empty cache: the engine writes this GPU's (and family's) shader profile
#   seed     copies $LANTERN_CACHE (a player's or the dev PC's cache folder, CUSA03173) and
#            recompiles it for that profile with bb-shader-lab, then checks it (--check-cache)
#   warmup   a start that builds every cached pipeline under validation
#   play     a start from an empty cache: pipelines compiled in play, under validation
# Family: none (default), nvidia, gtx10, rdna, polaris (BB_VK_HIDE, vk_instance.cpp).
#
# Needs: a built engine (build.sh), mesa-vulkan-drivers and vulkan-validationlayers (Ubuntu),
# BB_GAME_DIR (the game folder) and, for seed, LANTERN_CACHE. WSL: run as root in the distro.
set -uo pipefail
phase=${1:-warmup}
family=${2:-none}
seconds=${3:-300}
engine=$(cd -- "$(dirname -- "$0")/.." && pwd)
work=${LANTERN_VALIDATE_DIR:-/tmp/lantern-validate}/$family
cache=$work/gpu/cache/CUSA03173
: "${BB_GAME_DIR:?set BB_GAME_DIR to the game folder}"
mkdir -p "$work/data" "$work/gpu/cache"
export BB_DATA_DIR=$work/data BB_GPU_USER_DIR=$work/gpu BB_UPSCALER=none
export SDL_VIDEODRIVER=${SDL_VIDEODRIVER:-x11} DISPLAY=${DISPLAY:-:0}
[[ $family != none ]] && export BB_VK_HIDE=$family
lab() { LD_LIBRARY_PATH=$engine/out/gpu "$engine/out/gpu/bb-shader-lab" "$@"; }

case $phase in
profile)
    rm -rf "$work/gpu/cache"
    mkdir -p "$work/gpu/cache"
    ;;
seed)
    : "${LANTERN_CACHE:?set LANTERN_CACHE to a cache folder (…/cache/CUSA03173) to copy}"
    [[ -f $cache/profile.bin ]] || { echo "run the profile phase first"; exit 1; }
    profile=$(mktemp)
    cp "$cache/profile.bin" "$profile"
    rm -rf "$cache"
    cp -r "$LANTERN_CACHE" "$cache"
    rm -f "$cache/warmup.running" "$cache/warmup.off" "$cache"/catalog-*.applied
    cp "$profile" "$cache/profile.bin"
    rm -f "$profile"
    lab --recompile-cache "$cache" 2>&1 | grep -v '^GPU \[' | tail -3
    lab --check-cache "$cache" 2>&1 | grep -v '^GPU \['
    exit 0
    ;;
play)
    profile=$(mktemp)
    cp "$cache/profile.bin" "$profile" 2>/dev/null || { echo "run the profile phase first"; exit 1; }
    rm -rf "$cache"
    mkdir -p "$cache"
    cp "$profile" "$cache/profile.bin"
    rm -f "$profile"
    ;;
warmup) ;;
*) echo "phase: profile, seed, warmup or play"; exit 2 ;;
esac

if [[ $phase == warmup || $phase == play ]]; then
    export VK_LOADER_LAYERS_ENABLE='*validation'
    export VK_LAYER_SETTINGS_PATH=$work/vk_layer_settings.txt
    cat > "$VK_LAYER_SETTINGS_PATH" <<SETTINGS
khronos_validation.validate_core = true
khronos_validation.validate_sync = false
khronos_validation.thread_safety = true
khronos_validation.report_flags = error
khronos_validation.debug_action = VK_DBG_LAYER_ACTION_LOG_MSG
khronos_validation.log_filename = $work/validation.txt
khronos_validation.enable_message_limit = true
khronos_validation.duplicate_message_limit = 1000
SETTINGS
    rm -f "$work/validation.txt"
fi
cd "$engine"
timeout --signal=KILL "$seconds" bash run.sh --software > "$work/$phase.log" 2>&1
echo "engine exit $? ($phase, family $family); log $work/$phase.log"
grep -E 'GPU cache|^GPU: [^ ]+ .*driver|hidden for testing|STOP' "$work/$phase.log" | head -12
if [[ ( $phase == warmup || $phase == play ) ]]; then
    if [[ -f $work/validation.txt ]]; then
        echo "validation errors: $(grep -c 'Validation Error' "$work/validation.txt") ($work/validation.txt)"
        grep -o 'VUID-[A-Za-z0-9_-]*' "$work/validation.txt" | sort | uniq -c | sort -rn | head -20
    else
        echo "validation errors: 0"
    fi
fi
