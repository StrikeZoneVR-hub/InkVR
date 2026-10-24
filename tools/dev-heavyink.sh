#!/bin/bash
# Dev: the Chapter 1 finale's heavy ink (its ink rain and big splashes, which fall on Henry just before the cut-out
# scare), plus extra drips and bursts on top, measured with PERF/INKPROF. Run in a test session already in CH1
# (tools/vr-start.sh, tools/dev-newgame.sh). Prints the measurements.
cd "$(dirname "$0")/.."
B="[ENVIRONMENT]/[SECTIONS]/InkMachineRoom/Section_Controller/BendyFinaleController/InkFinalieFX_Container"
c() { tools/game.sh cmd "$1"; }
c "gamestate PLAY"; sleep 1
# stand up: the test runtime puts your eyes at floor level, which hides the body and dunks the head in floor ink
c "head 0 1.65 0 0 15"; c "hands 0.2 1.15 0.35 20 0"; sleep 1
c "active $B 1"; c "active $B/InkFinalie_particles 1"; sleep 1
c "goto2 $B/Splashes/InkSplash_Big"; sleep 3
c "inkclear"; c "perf"; c "say HEAVYINK START"; sleep 1
for k in 1 2 3; do
  c "active $B/Splashes 0"; c "active $B/Splashes 1"; c "inkburst 40"; c "dripon 3 500"; sleep 1.5
  c "inkburst 40"; sleep 1.5
done
sleep 3
c "inkstate"; c "perf off"; sleep 2
tools/game.sh log 600 | sed -n '/> goto2 .*Splashes/,$p' | grep -E "PERF avg|INKPROF|INKHITCH|FEETHITCH|HITCH frame|INKSTATE|soaked" | cut -c1-260
