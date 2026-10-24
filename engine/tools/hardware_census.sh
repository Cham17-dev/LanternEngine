#!/usr/bin/env bash
# The GPUs, drivers and CPUs in a folder of players' logs (docs/COMPATIBILITY.md, "Who plays"), and
# the Vulkan extensions each GPU's driver lacks ("Extension ... unavailable").
#   tools/hardware_census.sh <folder of lantern-*.log / last_session.log / message.txt>
set -uo pipefail
folder=${1:?a folder of logs}
mapfile -t logs < <(grep -rl --include='*.log' --include='*.txt' -e '^GPU: ' -e '^CPU: ' "$folder")
echo "== GPUs (driver)"
grep -h -o '^GPU: [^,]*, driver [^ ]* ([^)]*)' "${logs[@]}" | sed 's/^GPU: //' | sort | uniq -c | sort -rn
echo "== CPUs"
grep -h -o '^CPU: [^,]*, [0-9]* hardware threads' "${logs[@]}" | sed 's/^CPU: //; s/  */ /g' | sort | uniq -c | sort -rn
echo "== Missing Vulkan extensions per GPU and driver"
for f in "${logs[@]}"; do
    gpu=$(grep -m1 -o '^GPU: [^,]*, driver [^ ]*' "$f" | sed 's/^GPU: //')
    [[ -z $gpu ]] && continue
    echo "$gpu|$(grep -o 'Extension [A-Za-z0-9_]* unavailable' "$f" | awk '{print $2}' | sed 's/^VK_//' | sort -u | tr '\n' ' ')"
done | sort -u | awk -F'|' '!seen[$1]++ { print $1 ": " $2 }'
