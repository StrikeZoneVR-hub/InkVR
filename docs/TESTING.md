# Testing InkVR

## Current build: 0.2.0 test build

This build includes the launcher, Quest 3 button controls, Henry's hands and arms, the theatre screens for the menu and pause, and VR SETTINGS in the game's Options menu. Motion controls (swinging weapons, grabbing things) come next, after this test.

Everything is already installed in your game folder:
`C:\Program Files (x86)\Steam\steamapps\common\Bendy and the Ink Machine`

### Start it
1. On the PC, start the **Virtual Desktop Streamer**, then connect from your Quest 3.
2. Run **`InkVR Launcher.exe`** in the game folder. For easy access: right-click it → *Send to* → *Desktop (create shortcut)*.
3. Watch the opening (click or press any key to skip it), then press **PLAY IN VR**.
4. The launcher closes on its iris animation while the game starts. Put the headset on.

`F11` toggles the launcher between windowed and fullscreen. Drag the window anywhere on the paper to move it.

### Quest 3 controls (right-handed; OPTIONS → MAIN HAND swaps them)

| Left controller | | Right controller | |
|---|---|---|---|
| Thumbstick | Move · click it to run | Thumbstick | Turn (snap 45° by default, or smooth) |
| Trigger | Run | Trigger | Weapon · clicks in menus |
| X | Interact | A | Jump |
| Grip | Interact | B | Back (menus) |
| Menu ☰ | Pause | Grip | Interact |

- Look around with your head. Walking around your room moves you in the game.
- Menus: point your right hand at the screen. A laser appears; pull the trigger to click.
- Interact by pointing your right hand at something and pressing grip or X. You can switch this to "look at it" in the settings.

### What to check

**Launcher**
- [ ] Opening: the film leader counts 3-2-1, then the StrikeZoneVR card, then ink splats reveal the menu, the logo drops in and the VR stamp slams down (with sound).
- [ ] The menu: hovering an entry brushes the dark ink splash behind it, and Bendy's head hops along the entries.
- [ ] CONTROLS shows the Quest 3 controller sheet. VR CHECK is all green ticks. OPTIONS changes stick.
- [ ] PLAY IN VR: Bendy fills with ink, then the iris closes on his head, then "Dreams come true", and the game starts.
- [ ] QUIT: the iris closes on a waving Bendy, then "The End".

**In the headset**
- [ ] The title screen plays on a projector screen in a little studio theatre ("JOEY DREW STUDIOS" above, the InkVR credit below).
- [ ] The laser pointer clicks menu buttons. Options → **VR SETTINGS** opens the VR page, with "VR MOD MADE BY STRIKEZONEVR" at the bottom.
- [ ] Starting Chapter 1 puts you in the studio at life size: doors are a bit taller than you, posters at eye level.
- [ ] Head tracking is smooth in both eyes, with no doubling or swimming.
- [ ] You see Henry's hands and sleeves where your hands are. Fingers curl with grip and trigger, and there's a watch on the left wrist and a ring on the left hand.
- [ ] Moving with the left stick, running, jumping (A), and turning with the right stick. Try both snap and smooth turning (VR SETTINGS → COMFORT, or the launcher's OPTIONS).
- [ ] Interacting with things in Chapter 1 (tape recorders, the axe, doors) by pointing and pressing grip or X.
- [ ] Once you have the axe, it sits in your right hand and the trigger swings it (the game's own swing, for now).
- [ ] ☰ pauses: the game moves onto the theatre screen. Resume works.
- [ ] It's comfortable and smooth. Performance is logged automatically.

### After testing
Just tell me how it went. I can read both logs myself:
- the game and mod: `BepInEx\LogOutput.log` in the game folder (including `VRPERF` frame timings);
- the launcher: `%LOCALAPPDATA%\InkVR\launcher.log`.

### Verified here, and not yet
Verified without a headset, on Unity's mock OpenXR runtime:
- the launcher starting the game in VR, with the session running and the Touch controller profile attached;
- the Quest buttons reaching the game: jump and pause;
- laser clicks in menus, the theatre screens, and VR SETTINGS;
- the installer, both copying the mod and downloading the OpenXR plugin.

These need your headset:
- real head tracking and hand tracking;
- how the hand grip angle feels;
- turning comfort;
- the held-axe angle.

### Known limits (next milestones)
- Weapons use the game's own button swing; swinging with your arm is M4.
- Scripted camera moves and cutscenes don't take over the camera properly yet (M6).
- Crouching is physical only: the player collider doesn't shrink yet.
- Turning VR off: use **PLAY WITHOUT VR** in the launcher. Launching from Steam always starts in VR (setting `[VR] Enabled` in the cfg file).
