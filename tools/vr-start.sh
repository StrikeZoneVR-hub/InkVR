#!/bin/bash
# Dev: launch the game against Unity's OpenXR mock runtime (no headset needed).
cd "$(dirname "$0")/.."
export INKVR_DEV_NOSAVE=1  # test runs never write the player's save slots
export XR_RUNTIME_JSON="$(cygpath -w "$PWD/vendor/openxr/package/Runtime/MockRuntime/unity-mock-runtime.json")"
if tools/game.sh busy; then echo "NOT STARTING: the player's game is running"; exit 1; fi
tools/game.sh stop >/dev/null || exit 1
tools/game.sh start "-logFile \"$(cygpath -w "$PWD/analysis/player.log")\"" || exit 1
sleep 5
tools/game.sh wait "OpenXR session running|VR init failed" 90
