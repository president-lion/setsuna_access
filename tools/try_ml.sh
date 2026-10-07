#!/bin/sh
# Usage: try_ml.sh <tag>  - swaps in that MelonLoader x86 build, boots the game ~40s, prints the log.
# Only for dev boot tests: refuses if the game is already running.
set -e
G=/e/modgames/setsuna/I.am.Setsuna
D=/e/modgames/setsuna/mod/tools/dl
tag=$1
if tasklist | grep -qi SETSUNA.exe; then echo "game running - not touching it"; exit 1; fi
[ -f "$D/ML-$tag.x86.zip" ] || curl -sL -o "$D/ML-$tag.x86.zip" "https://github.com/LavaGang/MelonLoader/releases/download/$tag/MelonLoader.x86.zip"
rm -rf "$G/MelonLoader" "$G/version.dll"
unzip -q -o "$D/ML-$tag.x86.zip" -d "$G"
cd "$G"
./SETSUNA.exe -screen-fullscreen 0 -screen-width 960 -screen-height 540 >/dev/null 2>&1 &
sleep ${2:-40}
pid=$(tasklist | grep -i SETSUNA.exe | awk '{print $2}')
echo "pid=$pid"
sed 's/\x1b\[[0-9;]*m//g' MelonLoader/Latest.log | cut -c1-250 | grep -v "^\s*at " | head -${3:-60}
[ -n "$pid" ] && taskkill //PID $pid //F >/dev/null
