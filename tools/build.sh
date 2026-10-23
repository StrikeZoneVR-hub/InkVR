#!/bin/bash
# Stop the game (it locks the plugin DLL), build, and deploy. Exit code 1 on failure.
cd "$(dirname "$0")/.."
# Never touch the game folder while the player's own game is running (they may be testing).
if tools/game.sh busy; then echo "BUILD SKIPPED: the player's game is running"; exit 1; fi
tools/game.sh stop >/dev/null || exit 1
out=$(dotnet build src/InkVR.Plugin -nologo -v q 2>&1)
echo "$out" | grep -oE '[A-Za-z]+\.cs\([0-9]+,[0-9]+\): error [A-Z0-9]+: [^[]+' | sort -u | head -40
echo "$out" | grep -oE 'error MSB[0-9]+: [^[]{0,160}' | sort -u | head -5
if echo "$out" | grep -qE "Build succeeded"; then echo "BUILD OK"; else echo "BUILD FAILED"; exit 1; fi
