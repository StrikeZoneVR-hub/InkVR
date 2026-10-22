# Testing InkVR

## Current build: M1 (VR core)

Everything is already installed into your game folder (BepInEx, the InkVR plugin, Unity's OpenXR provider).

### Start it
1. Start **Virtual Desktop** and connect your Quest 3 (VDXR is your active OpenXR runtime).
2. Launch **Bendy and the Ink Machine** from Steam as usual.
3. The game starts in VR after about a second. The desktop window mirrors your left eye.

Turn VR off without uninstalling: set `Enabled = false` under `[VR]` in
`BepInEx\config\com.strikezonevr.inkvr.cfg`.

### Controls in M1
Quest controllers are not hooked up until M2. Use an **Xbox-style gamepad** (or keyboard and mouse):
- left stick: move (the direction you look is forward)
- right stick / mouse X: smooth turn
- the game's normal buttons: interact, jump, sprint, pause

Menus are on a floating screen and are driven by the gamepad.

### What to check
- [ ] The title screen floats in front of you on a dark backdrop, and you can start or continue a game with the gamepad.
- [ ] In Chapter 1, head tracking feels right: looking around, leaning and crouching.
- [ ] Scale feels life-size. Doors are a bit taller than you and the posters are at eye level.
- [ ] Walking in your room moves you in the game, and walls stop you.
- [ ] Turning with the right stick turns you around your head.
- [ ] Subtitles and objective notes float in front of you and stay readable.
- [ ] Fades to black/white cover your whole view.
- [ ] Both eyes look correct: no doubled or flickering effects, and the fog and lights look normal.
- [ ] Comfort, and how smooth it feels. Performance is logged automatically.

### After testing
Just tell me how it went. I can read `BepInEx\LogOutput.log` myself, which includes `VRPERF` lines with real frame and GPU times.

### Known M1 limits (handled in later milestones)
- Held items and the axe float where the flat game puts them. They move to your hands in M2.
- Interacting means looking at the object and pressing interact.
- Scripted camera moves and cutscenes don't take over the camera yet (M6).
- No Quest controller input, no laser pointer, and no VR settings menu yet (M2/M5).
