#!/bin/bash
# Dev: from the title screen, start a new game in save slot 3 and skip the intro letter.
# Only for test sessions started by tools/vr-start.sh (INKVR_DEV_NOSAVE=1: the game cannot save, so the
# player's real slots are never overwritten).
cd "$(dirname "$0")/.."
M="[UI MANAGER]/[UI Layer] - VIEW/TitleScreen(Clone)/Visuals"
LOG="${INKVR_GAME_DIR:-C:/Program Files (x86)/Steam/steamapps/common/Bendy and the Ink Machine}/BepInEx/LogOutput.log"
tools/game.sh wait "Scenes: Empty" 120 >/dev/null || exit 1
sleep 6
for attempt in 1 2 3; do
  tools/game.sh cmd "click $M/MainMenu/Navigation/BeginBtn"; sleep 2
  tools/game.sh cmd "click $M/SlotMenu/Navigation/Slot_03"; sleep 2
  tools/game.sh cmd "click $M/BeginMenu/Navigation/NewGameBtn"; sleep 2
  tools/game.sh cmd "pad a 0.08"; sleep 3   # confirms "overwrite save?" (saving is disabled in test runs)
  for i in $(seq 1 40); do grep -q "Scenes: CH1; active=CH1" "$LOG" && break; sleep 0.5; done
  grep -q "Scenes: CH1; active=CH1" "$LOG" && break
  tools/game.sh cmd "pad b 0.08"; sleep 1; tools/game.sh cmd "pad b 0.08"; sleep 1; tools/game.sh cmd "pad b 0.08"; sleep 2
done
grep -q "Scenes: CH1; active=CH1" "$LOG" || { echo "new game failed"; exit 1; }
sleep 8
tools/game.sh cmd "press interact"; sleep 1
tools/game.sh cmd "press attack"; sleep 6
echo "in CH1"
