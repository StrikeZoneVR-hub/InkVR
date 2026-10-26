#!/bin/bash
# Dev: from a fresh test session, load a chapter directly (CH2..CH5) and let it play. Usage: tools/dev-chapter.sh CH3
cd "$(dirname "$0")/.."
[ -f "$(dirname "$0")/local.sh" ] && . "$(dirname "$0")/local.sh"  # untracked: export INKVR_GAME_DIR=... for a game installed elsewhere
GAME="${INKVR_GAME_DIR:-C:/Program Files (x86)/Steam/steamapps/common/Bendy and the Ink Machine}"
LOG="$GAME/BepInEx/LogOutput.log"
n=$(wc -l < "$LOG")
tools/game.sh cmd "chapter $1"
for i in $(seq 1 150); do tail -n +$((n+1)) "$LOG" | grep -q "Scenes: $1; active=$1" && break; sleep 1; done
sleep 10
tools/game.sh cmd "gamestate PLAY"; sleep 1
echo "in $1"
