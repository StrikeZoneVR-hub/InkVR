# Testing InkVR

## Current build: 0.3.1 (from your second test)

Everything is installed in your game folder. Start it as before: Virtual Desktop Streamer → `InkVR Launcher.exe` → PLAY IN VR.

### What changed since 0.3.0

**You're Henry's height now (WORLD SIZE: AUTO, the new default)**
Your second test's log showed your eyes about 1.45–1.5 m above the floor, while Henry's are 5.03 game units. At the old fixed scale that put you about 20 cm shorter than Henry, in a world whose furniture is already oversized for him, so you felt tiny.

AUTO measures your standing eye height for a few seconds as each level starts and sizes the world so your eyes are at Henry's eye height. Your real floor stays the virtual floor, so you can still reach down to things. The page shows it, for example "WORLD SIZE: AUTO (86%)". Step left or right for a fixed size instead.

**Light beams no longer double**
The game's volumetric light beams (the ones in the Ink Machine room) were projected the same way for both eyes. Each eye now gets its own projection, so the beams sit where they shine. You can switch them off with LIGHT BEAMS (OFF also saves GPU time).

**Interact by hovering either hand**
Hold either hand over something interactable, touch it, or reach toward it. The game's own highlight and prompt appear, and grip (either hand) or X uses it. The hands find the object; the game's targeting does the rest. This is the groundwork for motion controls. INTERACT: LOOK AT IT brings back the flat game's look-to-interact.

**Steadier auto-resolution**
It now waits for two slow seconds before lowering the resolution, and won't raise it again for 30 s after a drop, so it settles instead of see-sawing.

**Eye spacing simplified**
Unity already scales the eyes for the world, so InkVR no longer adjusts them at all. It only logs your IPD (yours is 62.7 mm).

### What to check
- [ ] After a few seconds in a level, you're standing at Henry's height (doors, desks and posters at a natural height).
- [ ] The light beams in the Ink Machine room look single and solid.
- [ ] Hovering a hand over a door, a pedestal, a battery or a tape recorder shows the game's highlight and prompt, and grip uses it.
- [ ] The frame rate feels steady, without frequent small hitches.
- [ ] Ink still gets onto your hands from the splats, pools and bursts.

### After testing
Tell me how it went. The log now includes, every 5 seconds:
- your head height and IPD;
- how far apart the eyes are rendered;
- the current resolution;
- the frame time.

`BepInEx\LogOutput.log` lines starting `VRPERF` and `Stereo:` show me exactly what your headset reports.

### Verified here, and not yet
Verified on Unity's simulated OpenXR runtime:
- smooth walking: the camera now moves every frame;
- the late head pose;
- the stereo check;
- stick navigation into and around VR SETTINGS;
- ink from decals, pools, bursts and the drying;
- no errors in a full new-game run.

These need your headset: the world-size feel, the double vision, the jitter, and how the ink looks.

### Known limits (next milestones)
- Weapons use the game's own button swing; swinging with your arm is M4 (next).
- Scripted camera moves and cutscenes (M6).
- Crouching is physical only: the player collider doesn't shrink yet.
