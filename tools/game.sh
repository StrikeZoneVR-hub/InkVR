#!/bin/bash
# Dev helper. Usage:
#   tools/game.sh start            launch the game directly (BepInEx loads via winhttp.dll)
#   tools/game.sh stop             stop the game, only if a dev session started it (never the player's own session)
#   tools/game.sh busy             exit 0 when the player's own game is running (hands off the game folder)
#   tools/game.sh cmd "<command>"  send a DevTools command (see src/InkVR.Plugin/Dev/DevTools.cs)
#   tools/game.sh log [n]          tail the BepInEx log
#   tools/game.sh wait "<regex>" [timeout_s]  wait until the log matches
GAME="${INKVR_GAME_DIR:-C:/Program Files (x86)/Steam/steamapps/common/Bendy and the Ink Machine}"
LOG="$GAME/BepInEx/LogOutput.log"
DATA="$GAME/BepInEx/InkVR"
PIDFILE="$(dirname "$0")/../analysis/dev-game.pid"
running_pids() { tasklist //FI "IMAGENAME eq Bendy and the Ink Machine.exe" //FO CSV //NH 2>/dev/null | grep -i bendy | cut -d, -f2 | tr -d '"'; }
case "$1" in
  start)
    if [ -n "$(running_pids)" ]; then echo "GAME ALREADY RUNNING (not starting another)"; exit 1; fi
    rm -f "$LOG" # a fresh log, so waits only see this session
    args=""; [ -n "$2" ] && args="-ArgumentList '$2'"
    mkdir -p "$(dirname "$PIDFILE")"
    powershell.exe -NoProfile -Command "(Start-Process -FilePath '$GAME/Bendy and the Ink Machine.exe' -WorkingDirectory '$GAME' $args -PassThru).Id" | tr -d '\r' > "$PIDFILE" ;;
  stop)
    pids="$(running_pids)"
    [ -z "$pids" ] && { echo stopped; exit 0; }
    mine="$(cat "$PIDFILE" 2>/dev/null)"
    for p in $pids; do
      if [ "$p" != "$mine" ]; then echo "THE PLAYER'S OWN GAME IS RUNNING (pid $p): not stopping it"; exit 1; fi
    done
    taskkill //F //PID "$mine" >/dev/null 2>&1; rm -f "$PIDFILE"
    for i in $(seq 1 40); do [ -z "$(running_pids)" ] && break; sleep 0.25; done
    echo stopped ;;
  busy)
    pids="$(running_pids)"; mine="$(cat "$PIDFILE" 2>/dev/null)"
    for p in $pids; do [ "$p" != "$mine" ] && exit 0; done
    exit 1 ;;
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
