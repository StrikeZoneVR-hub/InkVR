#!/bin/bash
# Dev: from a fresh test session (tools/vr-start.sh + tools/dev-newgame.sh), load Chapter 2 and pick up the axe by hand.
cd "$(dirname "$0")/.."
GAME="${INKVR_GAME_DIR:-C:/Program Files (x86)/Steam/steamapps/common/Bendy and the Ink Machine}"
LOG="$GAME/BepInEx/LogOutput.log"
tools/game.sh cmd "chapter CH2"
for i in $(seq 1 120); do grep -q "Scenes: CH2; active=CH2" "$LOG" && break; sleep 1; done
sleep 12
tools/game.sh cmd "gamestate PLAY"; sleep 1
tools/game.sh cmd "interhere Weapon_Axe"; sleep 2
tools/game.sh cmd "pad x 0.15"; sleep 3
tools/game.sh cmd "hands off"; tools/game.sh cmd "weapons"; sleep 2
grep -E "WEAPONS held" "$LOG" | tail -1
