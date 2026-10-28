#!/bin/bash
# Build a release folder + zip: dist/InkVR-v<version>/ = InkVR Launcher.exe + InkVR-Payload/ (BepInEx + InkVR).
# Unity's OpenXR plugin is NOT included: the launcher downloads it from Unity's registry when installing.
set -e
cd "$(dirname "$0")/.."
VERSION=$(grep -oP '(?<=<Version>)[^<]+' src/InkVR.Launcher/InkVR.Launcher.csproj)
OUT="dist/InkVR-v$VERSION"
[ -f "$(dirname "$0")/local.sh" ] && . "$(dirname "$0")/local.sh"  # untracked: export INKVR_GAME_DIR=... for a game installed elsewhere
GAME="${INKVR_GAME_DIR:-C:/Program Files (x86)/Steam/steamapps/common/Bendy and the Ink Machine}"

tools/build.sh
dotnet build src/InkVR.Launcher -c Release -nologo -v q | grep -E "error|Build succeeded"

rm -rf "$OUT" "$OUT.zip"
mkdir -p "$OUT/InkVR-Payload/BepInEx/plugins/InkVR" "$OUT/InkVR-Payload/BepInEx/config"
cp "src/InkVR.Launcher/bin/Release/InkVR Launcher.exe" "src/InkVR.Launcher/bin/Release/InkVR Launcher.exe.config" "$OUT/"
unzip -q vendor/bepinex-be790.zip -d "$OUT/InkVR-Payload"
rm -f "$OUT/InkVR-Payload/changelog.txt"
cp "$GAME/BepInEx/plugins/InkVR/InkVR.dll" "$OUT/InkVR-Payload/BepInEx/plugins/InkVR/"
# Henry and Toon Henry's bodies (built in Blender from MakeHuman's CC0 base: tools/henry)
mkdir -p "$OUT/InkVR-Payload/BepInEx/plugins/InkVR/models"
cp models/*.inkmodel "$OUT/InkVR-Payload/BepInEx/plugins/InkVR/models/"
# BepInEx settings InkVR wants: no console window, log flushed as it's written (for bug reports)
cp "$GAME/BepInEx/config/BepInEx.cfg" "$OUT/InkVR-Payload/BepInEx/config/"
cp src/InkVR.Launcher/Assets/Fonts/Bangers-OFL.txt "$OUT/Bangers font license.txt"
cat > "$OUT/README.txt" <<TXT
InkVR $VERSION - VR mod for Bendy and the Ink Machine
VR mod made by StrikeZoneVR. Unofficial fan mod, not affiliated with Joey Drew Studios Inc.

1. Keep "InkVR Launcher.exe" and the "InkVR-Payload" folder together in a normal folder
   (Desktop, Documents, C:\Games\InkVR...). Very deep folders (paths over 260 characters)
   stop Windows from starting the launcher.
2. Run InkVR Launcher.exe. It finds the game through Steam, installs InkVR on first use
   (and downloads Unity's OpenXR plugin from Unity), then PLAY IN VR starts the game.
3. Quest 3 over Virtual Desktop: start the Virtual Desktop Streamer and connect first.

VR CHECK in the launcher shows what's installed and anything that needs fixing.
"InkVR Launcher.exe --play vr" starts the game straight away (for a desktop shortcut).
TXT
(cd dist && powershell.exe -NoProfile -Command "Compress-Archive -Path 'InkVR-v$VERSION' -DestinationPath 'InkVR-v$VERSION.zip' -Force")
du -sh "$OUT" "$OUT.zip"
