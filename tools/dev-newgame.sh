#!/bin/bash
# Dev: from a fresh launch, start a new game in save slot 3 and skip the intro letter.
cd "$(dirname "$0")/.."
M="[UI MANAGER]/[UI Layer] - VIEW/TitleScreen(Clone)/Visuals"
tools/game.sh wait "Scenes: Empty" 120 >/dev/null || exit 1
sleep 6
tools/game.sh cmd "click $M/MainMenu/Navigation/BeginBtn"; sleep 2
tools/game.sh cmd "click $M/SlotMenu/Navigation/Slot_03"; sleep 2
tools/game.sh cmd "click $M/BeginMenu/Navigation/NewGameBtn"; sleep 12
tools/game.sh cmd "press interact"; sleep 1
tools/game.sh cmd "press attack"; sleep 10
tools/game.sh cmd "shot ch1"
