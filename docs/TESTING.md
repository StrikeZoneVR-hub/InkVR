# Testing InkVR

## Current build: 0.7.2 (0.7.1 plus weapon hits that always count)

### Fixed in 0.7.2: swings that sometimes didn't hit
Swinging the axe, pipe or plunger sometimes didn't register. Real swings could lose their hit four ways, and each is fixed:
- **A wind-up that scrapes a wall, the floor or the ceiling** used up the swing's one hit on that surface, so the real blow did nothing. Now a wall still thuds (once, with a lighter buzz) but doesn't use the swing up. If a plank or an enemy is in the same sweep as the wall, the plank or enemy wins.
- **Quick chop-chop** counted as one long swing unless you paused between chops, so the second chop couldn't hit. Now each chop is its own swing as soon as the weapon turns back.
- **A fast swing that lost tracking for a moment** cancelled the swing. Now it carries on.
- **Enemies or objects with trigger hit-boxes** weren't hit at all. Now hittable triggers count.
- **Lighter starting threshold:** a swing now starts at 1.8 m/s at the weapon head (it was 2.2 m/s), so short close-range chops count too.

### Fixed in 0.7.2: doors that barely opened
Some doors only swung about 7° before stopping, for example Chapter 3's tiled-wall doors. The check for walls in the door's path counted the door's own frame and the game's invisible doorway blocker as walls.
- Now anything the closed door already touches is ignored, and only real walls stop it.
- Chapter 3's `Tile_Wall_Door_Unlocked (2)` swings −100..100° again, and one push takes it to about 36°.
- Doors without a box collider are now measured from their other colliders too.

### Checked in 0.7.2: every chapter
A test session loaded CH1–CH5 and the Archives in turn. In each one it stood up, looked around while timing frames, pushed the nearest door, splashed ink and checked the weapon and hurt-ink state (`tools/dev-walkthrough.sh`).
- **No errors from InkVR in any chapter.** Unity's log had only the game's own null-reference errors from loading chapters directly.
- **Frame rates on the test runtime:**

| Chapter | fps |
|---|---|
| CH1 | 92–120 |
| CH2 | ~105 |
| CH3 | ~80 |
| CH4 | ~79 |
| CH5 | 92–105 |
| Archives | ~84 |

- **Ink:** 0.5–0.9 ms a frame. CH3 and CH4 are heavy scenes, which the later performance pass is for.
- **Stalls:**
  - Chapter loads stall for 1–4 s behind the loading fade.
  - About 140 ms the moment Henry's body first appears.
  - One 17 ms ink "dip" stall in CH3.
- **Found and fixed:** the 7° doors above.

## Previous build: 0.7.1, the release (fixes from your 0.7.0 recording)

Everything is installed in your game folder. Start it as before: Virtual Desktop Streamer → `InkVR Launcher.exe` → PLAY IN VR. Your HEIGHT ADJUST is on NONE now, which is what's recommended.

### Fixed in 0.7.1: the lag when heavy ink falls on you
In the Chapter 1 finale (the ink rain and the big splashes just before the cut-out scare), heavy ink made the game stutter. It was measured in a test session that stood in that ink, with extra bursts and drips on top:
- **Before:** in one pour, 7 stalls of 16–28 ms from the falling drops, the worst frames 40–80 ms, and down to 63 fps.
- **Now:** no stalls; the worst frames are 22–27 ms.

What changed:
- **Ink hitting you:** only the first 12 blots a frame are painted one by one. The rest of that frame's hits on each part of Henry pool into one bigger blot, with one splash.
- **Splashes:** they thin out when the air is already full of drops, rather than multiplying.
- **Ink on the world:** splats on the floor and walls no longer ask the game about every other splat for each drop that lands, and only a few new ones are made a frame.
- **Pouring pipes:** they paint each part of Henry once, not once for every capsule on it.
- **Head under the ink:** this now only counts for a pool you could be standing in, with nothing solid between your head and the surface. A big ink surface that also spans the rooms at other heights no longer counts as "over your head".
  - A real dunk (crouching your head into a pool) soaks Henry over a few frames, and staying under keeps him soaked instead of soaking him again every 2 seconds.

### Fixed in 0.7.1: the ink box when something hits you
When a Searcher hits you, the game's ink splatters creep in from the edges of the screen. In VR they were drawn on the floating game panel, so they showed as a box hanging in front of you.
- They're now drawn around the edges of your own view (your headset's field of view is read from the game), so the ink frames your vision the way it frames a monitor.
- Past the edges they carry on, so you never see where they stop.
- The black that closes in at the last hit covers your whole view.

**Try this:** let a Searcher hit you once or twice in Chapter 2, then stand in the Chapter 1 finale's ink as it falls.

## Previous build: 0.7.0 (motion controls finished: doors by hand, pressing, picking up)

### New in 0.7.0: doors you open with your hands

The studio's doors now swing like real doors.
- **Push:** put your open hand on a door and push. It swings away as hard as you pushed it, keeps swinging, and slows down.
- **Pull:** grip a door near its handle side and pull it towards you, or push it away, with your hand.
- **Fling it:** let go while it's moving and it carries on. Swing it back towards the frame and it bangs shut and latches.
- **It meets things:** a door stops against your body (it won't swing through you) and against walls. Once a door is ajar you can walk it open with your body. A shut door is latched, so walking into it doesn't open it.
- **Locked doors** rattle in their frame when you push or pull them. You feel it in the controller, and the game gives its own "locked" response, as if you'd tried the handle.
- The game still knows about every door you open and close: its sounds, and anything in the story waiting on that door, all still happen.
- X still opens a door the game's way. Some doors stay open after that, as they always have.
- Setting: launcher OPTIONS → HENRY → DOORS (PUSH & PULL or PRESS). In the game, VR SETTINGS → MOTION CONTROLS switches it along with the others.

### New in 0.7.0: press things with your fingers

Tape recorders, Bendy squeaky toys, piano and organ keys, the Chapter 3 lift buttons, the radios and the projector now react to your hand.
- Press them with a fingertip, or hit them with your palm.
- It has to be a press: your hand moving into it. Resting your hand on something doesn't keep pressing it.
- Grip and X still work on all of them.

### New in 0.7.0: lift, pull and pick up

- **Trunk lids, drawers and switch boxes:** grip and lift the lid, pull the drawer, or flip the switch, about a hand's width, the same way levers work. Holding still for 2.5 seconds completes it too, so nothing can get stuck.
- **Items:** grip a collectable (Chapter 1's inkwell, gear, record and the rest, and other small items the game collects) and it comes into your hand as the game collects it. Let go and it goes into your pocket.

The setting for all of these is launcher OPTIONS → HENRY → GRAB, TURN, PRESS (BY HAND or BUTTON). It replaces VALVES, LEVERS, SOUP.

### Also in 0.7.0
- Fixed: another short freeze. While stains on Henry's clothes were slowly drying, their textures were re-sent to the graphics card several times a second, which caused a 0.3 s stall every few seconds. They're now sent every 1.5 seconds.
- Tested on the test runtime:
  - pushing, pulling, flinging and latching doors
  - locked doors rattling
  - pressing a tape recorder with a fingertip
  - taking Chapter 1's inkwell into the hand, after which the game counts it collected
  - every chapter loads with doors and pressable things found and no errors

**Not yet checked:**
- the Chapter 1 pedestals after collecting (the game seems to place items there by itself)
- walking an ajar door open with your body, and a door swinging back into you
- the lift buttons, organ and piano in a real playthrough (a directly loaded chapter starts partway through)
- trunk lids and drawers by hand

### New in 0.6.2: the ink is simulated in real time

You said the ink didn't feel real: walking through most ink left no footprints, and hands under falling ink barely changed. This update replaces the ink with a real-time simulation.

**Falling ink hits you**
- Every drop of the game's drips and ink rain is followed as it falls. If it meets your hand, arm, sleeve or clothes, it stops there and leaves a blot the size of the drop, with a little splash.
- Hold your hand under a drip and it builds up drop by drop, then starts to run.

**Ink runs and drips**
- Wet ink runs downhill over your hand and arm, whichever way you're holding it.
- It drips off the low edges (fingertips, knuckles, cuffs), and the drips fall, land and splat on the floor.

**Dipping**
- Put your hand or arm into ink and it's coated up to the ink line, in real time.
- The line follows as you push in deeper, so you can dip just your fingertips or go in up to the elbow.
- You feel the ink drag in the controller as you move through it.

**Shake it off**
- Flick your hand hard and the wet ink flies off as drops.
- A faint stain stays where it was.

**Footprints from all floor ink**
Every kind of floor ink now loads your soles:
- ink pools and flooded floors
- the game's inky floors (any floor that plays the ink footstep sound)
- ink puddles
- splats on the floor, including the ones you made yourself

Walk out of a flooded corridor and you leave a trail.

**Ink pools ripple and splash** (new, from your idea about messing around in water):
- The ink around you is now a liquid surface.
- Your hands, arms and legs push it: a hand dragged through leaves a wake, plunging in sends out rings, and wading leaves a trail.
- The waves spread, bounce off walls and anything standing in the ink, then settle (ink is heavy).
- Plunging in fast or slapping the surface throws up real drops. They land on you, on the floor, or back in the ink with a ring of their own. Swishing fast sprays.
- Ink dripping off your hand into the pool rings it, and so do the game's own drips and rain.
- The splashes use the game's own ink sounds.
- Because the ink is black, you see the ripples as moving glints of light, like the game's own shine on its ink.
- In hands-only mode, your unseen legs still make ripples as you wade.

**Performance:** all of this costs about 1–2 ms a frame at the busiest (splashing about in a pool), and about 0.1–0.3 ms when nothing is happening. Tested at 90 fps.

**Try this**
- **Chapter 1:** hold your hand under the drips in the first corridors and watch them land, run and drip. Then flick your hand to shake it off.
- **Chapter 2, the flooded hallway:**
  - Crouch and put your hand in the ink, then pull it out and watch it drip.
  - Swish your hand through the ink, slap it, and plunge in.
  - Wade through, then walk out onto the wooden floor and look back at your footprints.
- **Anywhere it rains ink:** stand in it and look at your sleeves.

**Not yet checked on the test runtime:**
- pouring streams
- pressing a hand into ink on a wall
- the TOON style's ink
- pipes that only start dripping later in a chapter
- the pool sounds (they play, but I can't hear them in a test run)

Please tell me if any of these look wrong.

0.6.2 also contains everything in 0.6.1, 0.6.0 and 0.5.1 below. 0.7.0 contains all of it.

### New in 0.6.1: the world is the right size in Chapter 2 again
At the start of Chapter 2, you and the whole world looked tiny, like a model.

The cause: for the wake-up scene the game moves its camera onto its own mount, and adjusts the camera's scale as it does. InkVR put the camera back in the VR rig (the 0.5.1 spin fix) but kept that adjusted scale, so the world size counted twice and your eyes were drawn about three and a half times too far apart. Eyes that far apart make everything look like a miniature.

InkVR now resets the camera's scale whenever it brings it back, and checks it every frame. Tested on the same scene: the eye spacing stays correct all the way through.

0.6.1 also contains everything in 0.6.0 and 0.5.1 below.

### New in 0.6.0: motion controls part 2

Grip is now **GRAB / USE**. X still does the classic one-press interaction for everything.

**Turn valves and wheels by hand**
- Put your hand on a valve (Chapter 1's basement cranks, Chapter 3's ink valves, any wheel), grip, and turn it.
- Wind your hand round, or twist your wrist; about a third of a turn does it.
- You feel it ratchet as it turns, then the game's own valve animation and puzzle logic take over.

**Pull levers**
Grip a lever (Chapter 1's power lever, Chapter 3's levers) and pull it about a hand's width, any direction.

**Eat Bacon Soup**
- Grip a can and it comes off the shelf into your hand.
- Bring it up to your mouth to eat it: the game's own eating sound and health.
- Let go anywhere else and it goes back on the shelf.

**Throw with your arm (Chapter 4's Bacon Soup cans)**
Hold the trigger, swing your arm, and let go of the trigger. The can flies the way your hand was moving, as hard as you threw it. A gentle release just lobs it where your hand points.

**Two hands on the gun**
Holding a gun, grip just in front of it with your other hand: it now aims along both hands (much steadier), and you feel the recoil in both.

**Holster over your shoulder**
- Reach over your dominant shoulder with what you're holding and grip: it goes away.
- Reach back there with an empty hand and grip: you draw it again.

**Never stuck:** if a valve or lever won't count your motion, holding your hand still on it for 2.5 seconds completes it too.

**Settings:**
- In the launcher (OPTIONS → HENRY) each one has its own switch: VALVES, LEVERS, SOUP; THROWING; TWO-HANDED GUNS; HOLSTER.
- In the game's VR SETTINGS there's one MOTION CONTROLS row that switches them all.

### New in 0.6.0: ink that really gets on Henry

Ink from the world now gets onto all of Henry, not just his hands, and it builds up realistically.

**What causes it**
- **Wading:** walk into an ink pool or flooded floor and your shoes and trousers get coated up to the ink line, with a ragged wet edge and splashes just above it. The deeper you go, the higher it climbs.
- **Getting drenched:** go under the ink (falling in) and Henry is soaked from head to toe.
- **Pouring ink:** whatever stands under a stream of ink gets coated.
- **Drips and splashes** from pipes and the ceiling land on your shoulders and clothes, not just your hands.
- **Ink bursts** (scares, ink attacks) splatter across your arms and the front of your shirt and waistcoat.
- **Running ink:** fresh ink runs down whatever it lands on, towards the floor.

**Wet ink drips.** For a while after it lands, ink drips off your hands, sleeves and clothes. A soaked Henry drips a lot.

**Footprints**
- Inky shoes leave footprints on the floor: ink pools, puddles and splats on the floor all load your soles.
- The trail starts solid and gets patchier step by step as the ink wears off your soles.
- Footprints stay a couple of minutes, then fade.
- With the full body the prints are exactly where Henry's feet come down. With the other BODY options they follow your path, a stride at a time.

**It dries.** Fresh ink slowly fades back to the chapter's usual stains over a few minutes, so Henry shows what happened recently rather than going black for good.

**Works with every BODY option:** full body (clothes, shoes, footprints), arms & hands, hands only (hands, sleeves and footprints) and both STYLES.

The setting is now called INK ON HENRY (launcher: OPTIONS → HENRY; game: VR SETTINGS → HENRY).

### New in 0.5.1: fixes from your recording

**The Chapter 2 spinning is fixed**
When you wake up in Chapter 2 ("my head..."), the view spun out of control and the game was unplayable.

The cause: for that scene the game moves its camera onto a different object (its "CameraContainer"). In VR that object turns with your head, and the camera inside it turned again, so every head turn fed itself and the view spun.

InkVR now keeps the camera in the VR rig whatever the game does with it, every frame. The wake-up plays normally: you look around freely, and the objective appears when you stand up.

The same fix applies anywhere else the game tries this.

**You can see your legs when you look down**
Henry's neck used to bend the wrong way when you looked down, which brought the collar and tie up into your eyes and hid everything below.

Now the neck bends forward and the hips ease back a little while the feet stay where they are, like a real person looking at their shoes:
- around 60° down you see your trousers and shoes;
- looking straight down you see the waistcoat (or braces), legs and feet.

**No body poking through the floor**
If you lie down or put the headset on the floor, the body hides itself instead of sinking through the ground.

0.5.1 also contains the full body update below and everything from 0.4.0 (motion controls part 1, the movie palace, and the shrinking and footstep fixes).

### Before you play: set HEIGHT ADJUST back to NONE
Your HEIGHT ADJUST is at UP 30 CM (it was UP 60 CM before your last session). You first raised it in an earlier test, when the world scale was wrong and you felt too short.

WORLD SIZE: AUTO now puts your eyes at Henry's height by itself, so UP 30 CM makes you about 30 cm taller than Henry. Set it to NONE in VR SETTINGS → CONTROLS → HEIGHT ADJUST, or in the launcher under OPTIONS → HEIGHT → AS TRACKED.

### New in 0.5.0: Henry's full body

**Pick how much of Henry you see, and how he looks**
VR SETTINGS → HENRY (or the launcher under OPTIONS → HENRY) has two settings:
- **BODY:** FULL BODY, ARMS & HANDS, HANDS ONLY or NONE.
- **STYLE:**
  - **LIFELIKE** (the default): real-looking hands, and a body in a waistcoat, tie, pinstripe trousers and work boots, with a gold watch chain.
  - **TOON:** the cartoon hands you had before, and a chunky body with red braces, baggy trousers and big round cartoon shoes.

Any BODY works with either STYLE.

**The full body follows you**
- The torso hangs from your head and turns with it (and a little towards your hands).
- The arms reach from its shoulders to your controllers.
- The feet stand on the floor (including stairs and ramps) and take steps when you walk, turn or lean.
- When you crouch, Henry bends his knees and leans forward.
- There's no head, because you're looking out of it.
- Henry is sized to your own height.

**Lifelike hands**
Each hand is now one smooth piece of skin rather than jointed segments, so the knuckles bend like real ones. It has:
- a shaped palm, with knuckles across the back and fleshy pads at the thumb and heel;
- tapering fingers with rounded fingertips and fingernails;
- natural skin tones, with creases at the joints, palm lines and faint veins;
- a thin ink line, so it still belongs in the game's drawn world.

Ink from the world still gets onto them.

**A watch that looks like a watch**
The old round watch was built from a cylinder with rounded ends, which made it an egg. It's now a 1930s rectangular "tank" watch:
- a gilt case with lugs and a winding crown;
- a cream dial with a minute track and 12 / 3 / 6 / 9 numerals;
- bold black hands that show your real time.

It sits on the wrist like a real watch: raise your wrist to read it and 12 is at the top.

### Also new since your last test (0.4.0)

**The menus now play in a full 1930s picture palace**
The title screen, pause menu and loading screens are on the big screen of an old movie theatre, and you can look all around it:
- **In front:** the stage, with velvet drapes, footlights, two of Bendy's cardboard cut-outs, and a gilded arch with Bendy's face and JOEY DREW STUDIOS above it.
- **Either side of the stage:** organ grilles and the exit doors.
- **Around you:** rows of velvet seats stepping up behind you, and sconce-lit walls with cartoon posters ("Bendy in SENT", "THE DANCING DEMON"...) and lobby cards of the five chapters.
- **Behind and above:** a balcony with a "DREAMS COME TRUE" marquee whose bulbs chase, the projection booth with its flickering beam overhead, a coffered ceiling with a chandelier, and ink running down the walls.

The screen is bigger and further away (6.6 m wide at 6 m) but looks the same size as before, so the menus read just as well. Your hands now pass in front of it properly. CALM THEATRE FLICKER also steadies the marquee and the sconces.

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
MELEE and GUN AIM are in VR SETTINGS → CONTROLS, and in the launcher under OPTIONS → HENRY. The launcher's HEIGHT option now covers ±60 cm, like the one in the game.

### What to check
- [ ] Grip a valve and turn it, grip a lever and pull it: the game's own animation plays after.
- [ ] Grip a can of Bacon Soup, raise it to your mouth: you eat it. Let go elsewhere: it goes back.
- [ ] Chapter 4: hold the trigger with a soup can, swing and release: it flies where you threw it.
- [ ] Hold a gun and grip its front with your other hand: it aims along both hands.
- [ ] Reach over your shoulder and grip: your weapon goes away; do it again to draw it.
- [ ] Walk into ink: your shoes and trousers get inky up to the ink line, then you leave footprints that fade out.
- [ ] Get hit by an ink burst: ink on your arms and the front of your clothes, dripping for a while.
- [ ] Chapter 2's opening: when Henry wakes up holding his head, the view stays steady, you can look around, and the world is its normal size.
- [ ] Look down while standing: you see your legs and shoes.
- [ ] Look down: you see your body, and your feet step as you walk and turn. Crouch and Henry crouches.
- [ ] Switch BODY and STYLE in VR SETTINGS: all four BODY choices and both STYLES work, and the change is instant.
- [ ] The LIFELIKE hands look like real hands when you open and close them (grip and trigger).
- [ ] Raise your left wrist: the watch is readable and shows the right time.
- [ ] At the title screen and the pause menu, look all around the theatre (behind you and up too): it should feel like a real old movie palace, with nothing flickering wrongly or looking broken.
- [ ] The menus on the screen are as easy to read and use as before.
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
- **The theatre:** I checked it looking front, left, right, behind and up, at the title screen and paused in Chapter 1. It draws in under 2 ms a frame and takes about 1.6 s to build once, at startup.

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
