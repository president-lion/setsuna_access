#!/bin/sh
# Dev boot test: start the game windowed, wait N seconds, print the mod's log lines and speech.log,
# then close it. Refuses if the game is already running (the user may be playing).
G=/e/modgames/setsuna/I.am.Setsuna
if tasklist | grep -qi SETSUNA.exe; then echo "game running - not touching it"; exit 1; fi
cd "$G"
./SETSUNA.exe -screen-fullscreen 0 -screen-width 960 -screen-height 540 >/dev/null 2>&1 &
sleep ${1:-40}
pid=$(tasklist | grep -i SETSUNA.exe | awk '{print $2}')
echo "pid=$pid"
sed 's/\x1b\[[0-9;]*m//g' MelonLoader/Latest.log | grep -E "Setsuna_Access|ERROR|Exception" | cut -c1-300 | head -${2:-60}
echo "--- speech.log"; cat UserData/SetsunaAccess/speech.log
[ -n "$pid" ] && taskkill //PID $pid //F >/dev/null
