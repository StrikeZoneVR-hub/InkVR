# Testing InkVR

## Current build: 0.4.0 (from your third test)

Everything is installed in your game folder. Start it as before: Virtual Desktop Streamer → `InkVR Launcher.exe` → PLAY IN VR.

### Before you play: set HEIGHT ADJUST back to NONE
Your HEIGHT ADJUST is at UP 60 CM. You set that in an earlier test, when the world scale was wrong and you felt too short.

WORLD SIZE: AUTO now puts your eyes at Henry's height by itself, so UP 60 CM would make you about 60 cm taller than Henry. Set it to NONE in VR SETTINGS → CONTROLS → HEIGHT ADJUST, or in the launcher under OPTIONS → HEIGHT → AS TRACKED.

### What changed since 0.3.1

**You no longer shrink as you walk**
0.3.1 kept re-measuring your height while you played and resized the world on the spot, so you seemed to shrink as you walked. The size is now decided once as each level starts, and again when you resume from pause, and then it stays put. Henry's feet are also pinned to the floor, so your height can't drift.

**No more constant thud while walking**
Every small head movement made the game think Henry had left the ground and landed again, so it kept playing its landing sound. Henry now stays grounded. The game's own footsteps are unchanged.

**Swing to hit (new): MELEE: SWING YOUR ARM**
Swing the axe (or the pipe, plunger or wrench) with your real arm. When the head of the weapon passes through a plank, a Searcher or anything else fast enough, the game's own hit happens there: the same damage, breaking and sounds as a normal swing.
- One hit per swing, with a strong buzz in your hand and the weapon's whoosh.
- Small waves, walking and turning don't count.
- The trigger still swings the axe the flat-game way.
- MELEE: BUTTON ONLY turns arm swings off.

**Guns fire where your hand points (new): GUN AIM: HAND**
For the Tommy Gun in Chapter 3. GUN AIM: HEAD brings back the flat game's look-to-aim.

**Where the new settings are**
MELEE and GUN AIM are in VR SETTINGS → CONTROLS, and in the launcher under OPTIONS → WEAPONS. The launcher's HEIGHT option now covers ±60 cm, like the one in the game.

### What to check
- [ ] In Chapter 1, you stay the same size the whole time you walk around.
- [ ] Walking is quiet apart from Henry's own footsteps.
- [ ] In Chapter 2, pick up the axe: hold a hand on it and press grip or X.
- [ ] Swing at the boarded-up door: a real swing breaks the planks, a small wave doesn't.
- [ ] A swing at a Searcher hurts it.
- [ ] The trigger still swings the axe.
- [ ] (Chapter 3, if you get that far) The Tommy Gun hits where your hand points.

### Verified here, and not yet
Verified on Unity's simulated OpenXR runtime, in test sessions that never write your saves:
- **Chapter 2:** the axe was picked up by hand hover + X. A scripted swing at about 4 m/s broke the planks in the starting room and on its door.
- **Chapter 3:** the same swing broke a plank on the Accounting door.
- **No double hits:** the game's own button swing moved the axe head at 2.9 m/s and was not counted as an arm swing.
- **Glitch guard:** a tracking jump (149 m/s) was ignored.
- **Earlier fixes:** the world size stayed fixed while walking, and Henry stayed grounded in all 200 sampled frames.
- **Menus:** both new settings show and fit in the in-game VR SETTINGS page and the launcher.

Not yet verified:
- **Gun aim:** a directly loaded Chapter 3 can't open the weapon station, so I couldn't hold the Tommy Gun.
- **A real Searcher:** it takes hits through the same game code as the planks, but needs your playthrough.
- **How swinging feels:** the speed it needs (about 2.2 m/s at the axe head) may need tuning after you try it.

### Known limits (next builds)
- Throwing Bacon Soup cans (Chapter 4) still uses the button. Throwing with your arm comes in 0.5.0, along with levers, valves and eating soup.
- Scripted camera moves and cutscenes come in 0.6.0.
- Full body comes in the 1.0 full release, with two models for you to choose from and options for arms & hands or hands only. The roadmap is in PLAN.md.

## Previous build: 0.3.1

#### What changed since 0.3.0

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

#### What to check
- [ ] After a few seconds in a level, you're standing at Henry's height (doors, desks and posters at a natural height).
- [ ] The light beams in the Ink Machine room look single and solid.
- [ ] Hovering a hand over a door, a pedestal, a battery or a tape recorder shows the game's highlight and prompt, and grip uses it.
- [ ] The frame rate feels steady, without frequent small hitches.
- [ ] Ink still gets onto your hands from the splats, pools and bursts.

#### After testing
Tell me how it went. The log now includes, every 5 seconds:
- your head height and IPD;
- how far apart the eyes are rendered;
- the current resolution;
- the frame time.

`BepInEx\LogOutput.log` lines starting `VRPERF` and `Stereo:` show me exactly what your headset reports.

#### Verified here, and not yet
Verified on Unity's simulated OpenXR runtime:
- smooth walking: the camera now moves every frame;
- the late head pose;
- the stereo check;
- stick navigation into and around VR SETTINGS;
- ink from decals, pools, bursts and the drying;
- no errors in a full new-game run.

These need your headset: the world-size feel, the double vision, the jitter, and how the ink looks.

#### Known limits (next milestones)
- Weapons use the game's own button swing; swinging with your arm is M4 (next).
- Scripted camera moves and cutscenes (M6).
- Crouching is physical only: the player collider doesn't shrink yet.
