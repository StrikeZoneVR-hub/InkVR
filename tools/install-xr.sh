#!/bin/bash
# Dev: copy Unity's OpenXR provider (com.unity.xr.openxr, downloaded to vendor/) into the game.
# The release installer downloads the package from Unity's registry instead of redistributing it.
cd "$(dirname "$0")/.."
GAME="${INKVR_GAME_DIR:-C:/Program Files (x86)/Steam/steamapps/common/Bendy and the Ink Machine}"
DATA="$GAME/Bendy and the Ink Machine_Data"
PKG=vendor/openxr/package
mkdir -p "$DATA/UnitySubsystems/UnityOpenXR" "$DATA/Plugins/x86_64"
cp "$PKG/Runtime/UnitySubsystemsManifest.json" "$DATA/UnitySubsystems/UnityOpenXR/"
cp "$PKG/Runtime/windows/x64/UnityOpenXR.dll" "$DATA/Plugins/x86_64/"
cp "$PKG/RuntimeLoaders/windows/x64/openxr_loader.dll" "$DATA/Plugins/x86_64/"
echo "OpenXR provider installed."
