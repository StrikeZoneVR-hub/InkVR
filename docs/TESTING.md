# Testing InkVR

## Current build: 0.3.0 (fixes from your first headset test)

Everything is installed in your game folder:
`C:\Program Files (x86)\Steam\steamapps\common\Bendy and the Ink Machine`

Start it the same way as before:
1. Start the Virtual Desktop Streamer and connect.
2. Run `InkVR Launcher.exe`.
3. Press PLAY IN VR.

### What changed since your first test

**Jitter**
- **Walking judder is fixed.** The game moves you on a 50 Hz physics tick, but the headset shows 90 frames a second, so your view stepped on some frames and stood still on others. InkVR now draws you smoothly between physics steps.
- **The head pose is applied later.** It now happens right before each frame is drawn, so the image matches the pose sent to the headset.
- **Frame rate is protected.** At your resolution (1728×1824 per eye) Chapter 1 needs about 12–13 ms of GPU time against 11.1 ms for 90 Hz, so frames were being dropped. **AUTO RESOLUTION** (on by default) lowers the resolution a step when frames run late and raises it again when there's room. It changes only every few seconds, never in menus, and resets behind each loading screen.

**World size and double vision**
- **Eye spacing measures itself now.** The eye spacing is set to exactly your IPD × the world scale, whichever way the runtime reports it. If Virtual Desktop was reporting already-scaled eyes, InkVR had been scaling them a second time, which matches what you saw: a miniature world, small hands and double vision up close. The log now records which case your headset is in.
- **WORLD SIZE** is a new setting (VR SETTINGS → COMFORT, or the launcher's OPTIONS). If the world still looks small or large, adjust it there; 100% is Henry's eye height.

**Menus**
- **The laser pointer is off by default.** Turn it on with LASER POINTER if you want it.
- **The thumbstick reaches VR SETTINGS.** Use down/up in the Options list, then A to open it.
- **On the VR page:** stick up/down picks a row, left/right (or A) changes it, and B goes back.

**Henry's hands**
- Knuckles on the back of the hand, and joint bulges on each finger.
- A chunkier 1930s watch: brass case, crown, cream dial, bigger hands.
- A thicker wedding band with an ink outline.

**Ink on the hands and sleeves (new)**
- **Splats:** touching an ink splat on a wall or floor leaves ink where you touched, and it runs down toward the floor.
- **Pools:** dipping your hand into an ink pool (like the Flow Room floor) coats whatever went under.
- **Drips:** ink drips and splashes that land on your hands mark them.
- **Bursts:** when the game splashes ink at you, your arms and hands get splattered too.
- **Drying:** fresh ink drips off for a moment, then slowly dries back to the chapter's usual stains over about 3 minutes. INK ON HANDS in the launcher's OPTIONS turns it off.

### What to check
- [ ] Walking and turning feel smooth, with no stepping or stutter while you move.
- [ ] The world feels life-size. If not, change WORLD SIZE and tell me which value felt right.
- [ ] No double vision up close (hands, objects at arm's length).
- [ ] Options: stick down to VR SETTINGS, A opens it, the stick changes values, B goes back.
- [ ] Touch an ink splat on a wall or floor in Chapter 1: ink appears on your fingers or palm.
- [ ] The watch, ring and knuckles read clearly when you look at your hands.

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
