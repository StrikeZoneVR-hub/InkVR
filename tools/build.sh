#!/bin/bash
# Build the plugin (deploys into the game when it is not running). Prints errors compactly.
cd "$(dirname "$0")/.."
out=$(dotnet build src/BendyVR.Plugin -nologo -v q 2>&1)
echo "$out" | grep -oE '[A-Za-z]+\.cs\([0-9]+,[0-9]+\): error [A-Z0-9]+: [^[]+' | sort -u | head -40
echo "$out" | grep -qE "Build succeeded" && echo "BUILD OK" || echo "BUILD FAILED"
