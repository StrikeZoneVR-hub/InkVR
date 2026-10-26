#!/bin/bash
# Dev: a smoke test of every chapter in one test session (tools/vr-start.sh + tools/dev-newgame.sh first).
# For each chapter: load it, stand up, look around while measuring frame times, push the nearest door by hand,
# press the nearest pressable thing, splash some ink, check the weapon and the hurt ink; then gather errors and
# measurements. Writes analysis/walkthrough.txt. Usage: tools/dev-walkthrough.sh [CH1 CH2 ... Archives]
cd "$(dirname "$0")/.."
[ -f "$(dirname "$0")/local.sh" ] && . "$(dirname "$0")/local.sh"  # untracked: export INKVR_GAME_DIR=... for a game installed elsewhere
GAME="${INKVR_GAME_DIR:-C:/Program Files (x86)/Steam/steamapps/common/Bendy and the Ink Machine}"
LOG="$GAME/BepInEx/LogOutput.log"
PLOG="analysis/player.log"
OUT="analysis/walkthrough.txt"
CHAPTERS=${*:-"CH1 CH2 CH3 CH4 CH5 Archives"}
c() { tools/game.sh cmd "$1"; }
: > "$OUT"
for ch in $CHAPTERS; do
  n=$(wc -l < "$LOG"); pn=$(wc -l < "$PLOG")
  if ! grep -q "Scenes: $ch; active=$ch" <(tail -n +1 "$LOG" | tail -n 400) || [ "$ch" != "CH1" ]; then
    c "chapter $ch"
    for i in $(seq 1 180); do tail -n +$((n+1)) "$LOG" | grep -q "Scenes: $ch; active=$ch" && break; sleep 1; done
    sleep 12
  fi
  c "gamestate PLAY"; sleep 1
  c "head 0 1.65 0 0 0"; c "hands 0.2 1.15 0.35 20 0"; sleep 6
  c "perf"
  for yaw in 0 90 180 270 0; do c "head 0 1.65 0 $yaw 5"; sleep 2; done
  c "perf off"
  c "door list"; c "door stand 0.6 1"; sleep 1; c "door"; c "door hand 0.8 1.0 0.12 0"; sleep 0.6; c "door hand 0.8 1.0 -0.2 0.5"; sleep 1.5; c "door"
  c "hands 0.2 1.15 0.35 20 0"
  c "touch 0"; sleep 0.5  # reports the nearest pressable thing (pressing it from across the room would stretch the arm)
  c "inkburst 20"; sleep 2; c "inkstate"; c "inkclear"
  c "weapons"; c "hurt state"
  sleep 2
  {
    echo "===== $ch ====="
    tail -n +$((n+1)) "$LOG" | grep -E "Scenes: |Doors: |Press by touch: |Ink in this level|PERF avg|INKPROF|HITCH frame|INKHITCH|FEETHITCH|DOOR |DOORS|PRESS |Pressed|pressed|INKSTATE|WEAPONS|HURT |Exception|Error|error|Unknown command" | grep -v "> " | cut -c1-260
    echo "--- Unity log: $(tail -n +$((pn+1)) "$PLOG" | grep -c "Exception") exception lines, $(tail -n +$((pn+1)) "$PLOG" | grep -c "InkVR") mentioning InkVR"
    tail -n +$((pn+1)) "$PLOG" | grep -B2 -A6 "Exception" | grep -E "Exception|InkVR\." | sort | uniq -c | sort -rn | head -12
  } >> "$OUT"
done
echo "done: $OUT"
