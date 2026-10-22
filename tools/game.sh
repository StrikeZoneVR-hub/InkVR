#!/bin/bash
# Dev helper. Usage:
#   tools/game.sh start            launch the game directly (BepInEx loads via winhttp.dll)
#   tools/game.sh stop             kill the game
#   tools/game.sh cmd "<command>"  send a DevTools command (see src/BendyVR.Plugin/Dev/DevTools.cs)
#   tools/game.sh log [n]          tail the BepInEx log
#   tools/game.sh wait "<regex>" [timeout_s]  wait until the log matches
GAME="${BENDY_GAME_DIR:-C:/Program Files (x86)/Steam/steamapps/common/Bendy and the Ink Machine}"
LOG="$GAME/BepInEx/LogOutput.log"
DATA="$GAME/BepInEx/BendyVR"
case "$1" in
  start) powershell.exe -NoProfile -Command "Start-Process -FilePath '$GAME/Bendy and the Ink Machine.exe' -WorkingDirectory '$GAME' -ArgumentList '$2'" ;;
  stop) taskkill //F //IM "Bendy and the Ink Machine.exe" >/dev/null 2>&1; echo stopped ;;
  cmd) mkdir -p "$DATA"; printf '%s\n' "$2" >> "$DATA/cmd.txt" ;;
  log) tail -n "${2:-40}" "$LOG" ;;
  wait)
    t=${3:-120}
    for ((i=0;i<t*2;i++)); do
      grep -qE "$2" "$LOG" 2>/dev/null && { grep -E "$2" "$LOG" | tail -3; exit 0; }
      tasklist | grep -qi "Bendy and the Ink" || { echo "GAME NOT RUNNING"; tail -20 "$LOG"; exit 2; }
      sleep 0.5
    done
    echo "TIMEOUT waiting for: $2"; exit 1 ;;
  *) echo "usage: $0 start|stop|cmd|log|wait"; exit 1 ;;
esac
