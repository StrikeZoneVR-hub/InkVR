#!/bin/bash
# Dev: launch the game against Unity's OpenXR mock runtime (no headset needed).
cd "$(dirname "$0")/.."
export XR_RUNTIME_JSON="$(cygpath -w "$PWD/vendor/openxr/package/Runtime/MockRuntime/unity-mock-runtime.json")"
tools/game.sh stop >/dev/null
tools/game.sh start "-logFile \"$(cygpath -w "$PWD/analysis/player.log")\""
sleep 5
tools/game.sh wait "OpenXR session running|VR init failed" 90
