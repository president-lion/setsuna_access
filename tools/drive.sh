#!/bin/sh
# Dev self-test driver. Commands (one per arg):
#   launch            start the game (refuses if already running - the user may be playing)
#   until:<text>      wait (max 120s) until speech.log contains <text>
#   keys:<keys.ps1 script>
#   kill              close the game
G=/e/modgames/setsuna/I.am.Setsuna
SL=$G/UserData/SetsunaAccess/speech.log
for c in "$@"; do
  case "$c" in
    launch) tasklist | grep -qi SETSUNA.exe && { echo "game running - not touching it"; exit 1; }
            (cd $G && ./SETSUNA.exe -screen-fullscreen 0 >/dev/null 2>&1 &); sleep 8 ;;
    until:*) t=${c#until:}; n=0; until grep -q "$t" $SL 2>/dev/null; do sleep 1; n=$((n+1)); [ $n -gt 120 ] && { echo "timeout: $t"; break; }; done ;;
    keys:*) powershell -ExecutionPolicy Bypass -File 'E:\modgames\setsuna\mod\tools\keys.ps1' "${c#keys:}" >/dev/null ;;
    kill) taskkill //IM SETSUNA.exe //F >/dev/null ;;
  esac
done
