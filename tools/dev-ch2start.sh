#!/bin/bash
# Dev: from the title screen of a test session, start Chapter 2 through the game's own chapter select
# (so its opening sequence runs, unlike tools/dev-chapter.sh). Saving is blocked in test sessions.
cd "$(dirname "$0")/.."
GAME="${INKVR_GAME_DIR:-C:/Program Files (x86)/Steam/steamapps/common/Bendy and the Ink Machine}"
LOG="$GAME/BepInEx/LogOutput.log"
M="[UI MANAGER]/[UI Layer] - VIEW/TitleScreen(Clone)/Visuals"
tools/game.sh cmd "head 0 1.6 0 0 0"
tools/game.sh cmd "click $M/MainMenu/Navigation/BeginBtn"; sleep 2
tools/game.sh cmd "click $M/SlotMenu/Navigation/Slot_03"; sleep 2
tools/game.sh cmd "click $M/BeginMenu/Navigation/ChaptersBtn"; sleep 2.5
tools/game.sh cmd "stick 1 0 0.15"; sleep 1.5
n=$(wc -l < "$LOG")
tools/game.sh cmd "pad a 0.1"
for i in $(seq 1 90); do tail -n +$((n+1)) "$LOG" | grep -q "Scenes: CH2; active=CH2" && break; sleep 1; done
echo "CH2 loading done"
