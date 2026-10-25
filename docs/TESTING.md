# Testing InkVR

## Current build: 0.8.4 (hits keep you in Henry, your body in every scene, solid ink, a steady theatre)

### Fixed in 0.8.4: "going out of full body and into the floor or ceiling when getting hit"
- **Cause:** when an enemy hits Henry, the game shakes its camera. The shake puts the camera down by Henry's feet, about 3 units below his eyes. 0.8.1 to 0.8.3 treated any camera move by the game as a cutscene. So every hit sent your view to the floor and hid the body.
  - Your 25 Oct session log shows it on every hit in Chapter 3: "the game set its camera itself (3.0 units from Henry)" next to the hurt border.
  - Dying did the same with the game's death camera.
- **Fix:** InkVR now knows when Henry is hit (the game's hurt border) or dies. For a moment after a hit (1.5 s), and through a death (until the respawn, or 8 s), nothing the game does to its camera is followed. You stay in Henry's body, your view stays in your head, and the hurt border still shows.
- **Checked:** three hits in a row in Chapter 2. The game's camera shook down to the floor each time (logged), and InkVR started no scene. A real scene (the knockout roll) still plays in VR.

### Changed in 0.8.4: your body stays yours in cutscenes and scripted moments
- **Before:** Henry's body stepped aside whenever a cutscene had the camera or the game held Henry still (tied up, talking, lifts). Only the hands stayed.
- **Now:** the full body and arms stay, and they follow your head and hands as usual. Only walking is held: the stick doesn't move Henry until the scene is over. You can still look round, lean and use your arms.
  - In a scene the body stands under your view, its feet on the floor below you.
  - If the scene puts you down on the floor (the knockout, waking up), the body steps aside until you're up, as it always has when your head is near the floor.
- **Checked:** body shots while the game held Henry still, and in the middle of a scene (the knockout roll). The full body was there both times, standing on the floor.

### Fixed in 0.8.4: see-through ink
- **Seen in your recording at 24:17 (Chapter 3):** round your hands, the ink pool was see-through, and the floorboards showed through it.
- **Cause:** InkVR draws a 3 m patch of moving ink round you over the game's own pool. Many of the game's ink floors carry extra settings of their own on top of the shared ink material. The patch only copied the shared material, so it drew as a different ink, one that dissolved to nothing, and it hid the real pool under it.
  - The patch also carried its light glints in its vertex colours, which the ink shader can read as "fade out here".
- **Fix:** the patch now copies each pool's own settings (and keeps them up to date). The glints moved to a layer of their own, so the ink itself is always solid.
- **Checked:** Chapter 3's inked floor by the lift (the same kind as in your recording) looks the same with the moving patch on and off: solid black, with the pool's own ragged edges. Ripple glints still show in Chapter 2's flooded hallway.

### Fixed in 0.8.4: the flicker above the curtains in the theatre
- **Cause:** two thin gold strips sat almost on top of other parts:
  - the patterned frieze along the arch over the curtains, only 5 mm in front of the beam behind it;
  - the cornice frieze higher up, with an open gap behind it you could see into.
  - At that distance the headset can't tell 5 mm apart, so the two kept swapping places as you moved: a shimmering line.
- **Fix:** the arch frieze now stands 4 cm clear of the beam, the cornice has an underside closing the gap, and the same spacing went to the plaque lettering, Bendy's medallion ring and the balcony's frieze and sign.
- **Checked:** the flickering pixels under the plaque went from about 600 to none, in shots taken with the head moved by under a millimetre.

### What to try (0.8.4)
1. Let an enemy hit you (a Searcher in Chapter 3 is easiest): you stay standing in Henry's body, with the hurt border round your view.
2. Play a scripted moment (a conversation, a lift, Sammy's scene): look down to see your body, and move your arms.
3. Stand in the inked floor by the Chapter 3 lift (or any ink pool) and look round your hands: solid black ink, with glints when you splash.
4. In the main-menu theatre, look at the gold band over the curtains while moving your head: it should hold still.

## Previous build: 0.8.3 (no more flipping when tied up, a weapon wheel that works every time)

### Fixed in 0.8.3: "stuck between two places, glitching in and out"
Tied up for Sammy's sacrifice, the view flipped back and forth between Sammy and the wall behind you.
- **Cause:** a fight over which way Henry faces.
  - Henry is held in place but still allowed to look round within limits, and the game keeps turning him back into the view it allows (towards Sammy).
  - InkVR kept turning him to face where your head looks.
  - Your play space hangs from Henry, so every turn the game gave him turned your whole world, and the two fought every frame.
- **Fix:** the play space now has its own place and facing in the world.
  - It still goes wherever the game moves Henry (walking, lifts, being carried).
  - Turns the game gives him no longer turn you. Only your snap and smooth turns do.
- **When the game moves Henry somewhere new while you can still look round** (tied up, a respawn), you now face the way the game points him, in a blink. Tied up, that's Sammy.
- **Henry's body also steps aside** while he's held in place but able to look round.
- **Checked on your own Save 2** (loaded in a test session where saving is blocked; your save file is unchanged):
  - Pulled the lever in Sammy's office and watched the knockout, then the whole tied-up monologue.
  - The view stayed facing Sammy the whole time, with no flips (one facing over 1,151 samples).
  - Snap turns and walking still work as before.

### Fixed in 0.8.3: the weapon wheel
- **It only worked once per session.**
  - **Cause:** every opening threw its labels away and made new ones, and from the second opening on, the new labels were made before the wheel was showing. TextMeshPro can't set a label up then, so it crashed.
  - **Fix:** the labels are made once and reused, so the wheel works every time. The labels are now spaced further apart, so INVENTORY and HANDS no longer overlap.
- **It was awkward to work:** you had to hold the stick pressed in while tilting it. The new controls:
  - **Open:** click the right stick (a quick press is enough). The wheel opens and stays open.
  - **Pick:** flick the stick towards a choice and let it spring back, point your hand at one and pull the trigger, or click the stick again. The old way still works too: hold the click, tilt, let go.
  - **Close:** press B, or leave it for 8 seconds with nothing picked.
  - The trigger doesn't swing your weapon while the wheel is open.
- **Checked:** five openings in a row, using every way to pick.

### What to try (0.8.3)
1. Load Save 2, pull the lever in Sammy's office and sit through the scene in the headset: no flipping, and you face Sammy.
2. Use the weapon wheel several times: flick to HANDS, flick to your weapon, open it and press B.
3. Snap and smooth turn, walk, and ride a lift: everything should move as before.

## Earlier build: 0.8.2 (Sammy's knockout, a world that never tilts, quitting to the menu)

### Fixed in 0.8.2: the sickening knockout in Chapter 2 (from your recording)
Your video showed Sammy's knockout. After the hit, the whole world rolled 20–30° and swept round you, and then, tied up, you faced the wooden post instead of Sammy.
- **The roll:**
  - **Cause:** to fake Henry falling and being dragged, the game tilts and rolls the player object. InkVR's play space hangs from that object, and in normal play it kept whatever tilt it inherited, which built up frame after frame.
  - **Fix:** the play space is now always kept level, whatever the game does to the player.
  - **Follow:** a tilted or rolled player now counts as a scene, so its position is followed and you go down with Henry.
- **The knockout itself:**
  - **Cause:** the game sets the camera's pose directly. It never does that in normal play (checked), so InkVR now takes it as a scene and follows it.
  - **Result:** you drop to the floor of Sammy's office, level, and see Sammy's legs walk up to you.
- **Tied up facing the post:**
  - **Cause:** while the game holds Henry still, InkVR kept the direction you faced before the scene.
  - **Fix:** when the game moves Henry somewhere new while holding him (tied up for the sacrifice), you now face the way the game turned him, in a blink.
  - **Also:** a hold no longer nudges your view by your distance from the middle of your play area when it starts.

### Fixed in 0.8.2: quitting to the main menu and loading again
Your log had 27,500 errors after you quit to the menu and loaded again.
- **Cause:** the game's reset on quitting destroys everything kept between scenes, Henry included. The hands rebuilt themselves, but the full body and Henry's ink stains kept pointing at the destroyed pieces.
- **Effect:** every frame threw an error, which also stopped the hands and held weapon being posed.
- **Fix:** Henry is now rebuilt cleanly after a reset. Destroyed stain pieces are also cleared out once a second, as a safety net.
- **Checked:** quit to the menu through the pause menu's OK, then started Chapter 2 again. One error, from the very frame of the reset (now guarded), against 27,500 before.

### Checked in 0.8.2
- The openings of Chapters 2, 3 and 5 still follow as one clean scene each, with no false cuts.
- The tumble test: the player object rolled up to 30°, and the view stayed level (0–1°).

### What to try (0.8.2)
1. **Chapter 2 (in VR):** play to Sammy's office and pull the lever.
   - The knockout should drop you to the floor without any roll.
   - Tied up, you should face Sammy.
2. **Quit to the main menu,** load your save again, and play on. Henry's hands, body and weapon should all work.

## Earlier build: 0.8.1 (cutscenes in VR, melee hits anywhere on an enemy)

### Fixed in 0.8.1: cutscenes and scripted moments now play in VR
Before, InkVR kept you standing where Henry was while the game played its scene somewhere else, and Henry's body stayed drawn through it.
- **How the game does it:** for its scenes (waking up on the floor, getting knocked down, getting up, being carried) the game takes its camera off Henry's head and moves it itself.
- **Now the headset follows that camera through the world.** You're on the floor when Henry is, and you rise with him as he gets up.
- **Comfort:**
  - Only the position is followed. The direction is set once when the scene starts (and again on a real cut).
  - Your own head does all the looking, so the world never tilts, rolls or spins round you.
  - A quick blink of black hides the jump into a scene and any cuts. Leaving a scene blinks only if the view has to jump.
- **Henry's body and arms step aside** while the game has the camera, or holds Henry for a scripted moment. Your hands stay.
- **Checked:** the openings of Chapters 2–5 and dying.
  - **Chapter 2:** lying on your side among the candles, then kneeling and standing.
  - **Chapter 3:** on your back by the cot, then sitting up.
  - **Chapter 4:** a short hold, with no camera move.
  - **Chapter 5:** lying behind the lattice, looking through the hatch, then the corridor.
  - **Dying:** you walk the ink tunnel as normal.

### Fixed in 0.8.1: weapons hit enemies anywhere
- **Any part of the enemy counts:** head, arms and legs. Before, only a narrow capsule round its middle counted, about 25 cm across the Fisher.
  - While an enemy is alive, the game switches off the collision on its skeleton; it's only for the ragdoll when it dies.
  - InkVR now measures your swing against those bones as they animate.
- **Any part of the weapon counts:** the head and down the shaft, not just the tip.
- **Pulling the trigger mid-swing no longer cancels your swing.** The trigger started the game's own canned swing, which:
  - took the weapon over for about a second;
  - only hit what your eyes pointed at.
  Now a trigger pulled while your arm is already moving is left to your swing. Pulled with your arm still, it's the game's swing as before.
- **Checked on Chapter 3's Striker and Fisher:**
  - Striker's head: 4 → 1 health.
  - Striker's upper forearm, well outside its body: 1 → dead (it fell as a ragdoll).
  - Fisher: 5 → 2 → dead.
  - A trigger pulled mid-swing was held back, and the swing landed.

### What to try (0.8.1)
1. Start Chapter 2 from chapter select. You should wake on the floor and get up with Henry, with no body in the way.
2. Play the Chapter 1 ending (the floor gives way) and the Chapter 2 ending (Sammy). Tell me how they feel; those two weren't tested here.
3. Fight a few Searchers and the Butcher Gang:
   - hit their heads, arms and legs;
   - try with and without pulling the trigger.

## Earlier build: 0.8.0 (seated play, gravity pull, weapon wheel, inventory card, cartoon Toon body)

All of these are options, and all are on by default except SEATED play. They're in the VR SETTINGS page (in the game's Options) and the launcher's OPTIONS page.

### New in 0.8.0: seated play
- **COMFORT > PLAY: SEATED.** Sitting down, your view is lifted to standing height. The game sees a standing Henry, and the world isn't shrunk to your seated height.
- It measures you for 2 s once you switch it on: sit as you'll play. After that it follows you slowly (30 cm a second), so sitting up or slouching never jolts the view, and leaning down to the floor doesn't count.
- **Your standing height:** it uses the one InkVR measured standing, if there is one; if not, 1.62 m.
- **Checked:** sitting at 1.15 m lifted the view 0.50 m. Switched on while standing and then sitting at 1.10 m, the lift eased to 0.52 m.

### New in 0.8.0: gravity pull (like Half-Life: Alyx, in ink)
- **To pull something,** point your open hand at an item out of reach (up to 5 m), then grip. A thread of ink beads ties it to your hand, and the item twitches. Flick your hand back towards you and it flies into your hand along an arc, dripping ink. You can also just keep holding the grip for ¾ s.
- **What it can pull:** anything the game lets you take: pickups, Bacon Soup, and weapons lying about.
- **When it pulls:**
  - Only items you can see. It won't pull through a wall, but an item sitting on a shelf or in a box still counts.
  - Not into the hand already holding a weapon: use your other hand, or put the weapon away first.
- **Seated players:** this is how you take things off the floor without bending down.
- **Checked:** the axe from 4.3 m and a Bacon Soup from 2.6 m.

### New in 0.8.0: weapon wheel
- **To open it,** hold the right thumbstick down (about ⅕ s). An ink splat opens by your hand.
- **To choose,** tilt the stick (or move your hand) towards one choice and let go. The choices:
  - your weapon (draw it);
  - HANDS (put it away);
  - SEEING TOOL (once the game gives it to Henry);
  - INVENTORY.
- Turning pauses while the wheel is open.
- **Checked:** putting the axe away and drawing it again.

### New in 0.8.0: inventory card
- **To see it,** hold Y. A scrap of the studio's paper comes up in your other hand. It shows:
  - the current objective, in the game's own words and lettering;
  - the items it asks for (the ones you've found inked in, the rest faint), with the count.
- Let go of Y and it's put away. A short tap of Y still does what it did before.
- **Checked:** Chapter 1's objective on the card, in the game's lettering.

### New in 0.8.0: the Toon body as a cartoon
- **Hands:** white rubber-hose cartoon gloves:
  - three fat fingers and a thumb, a ball at each joint;
  - three stitch lines on the back;
  - a flared cuff.
- **Clothes and shoes:**
  - flat two-tone (cel-shaded) clothes with a few bold folds;
  - glossy black cartoon shoes with a white shine;
  - bolder ink outlines (about 1¾ times as thick).
- Ink still splashes on, and still stays black.

### Changed in 0.8.0
- **The VR SETTINGS page is in two columns now** (COMFORT and HENRY | CONTROLS and GRAPHICS), with bigger lettering, so it no longer runs into the credit line. The stick goes down the first column, then the second.
- **The launcher's CONTROLS page** shows the wheel (hold the right stick), the inventory (hold Y), gravity pull and seated play.

### What to try
1. **Seated:**
   - Sit, switch on PLAY: SEATED and check you're at standing height.
   - Pull items from the floor and from shelves.
   - Play through a stretch of Chapter 1 sitting.
2. **Pull:**
   - Pull a Bacon Soup and drink it. Pull the axe in Chapter 2.
   - Try pulling through a wall: it shouldn't.
3. **Wheel:**
   - Put the axe away and draw it.
   - Pick INVENTORY.
   - Check the stick doesn't turn you while the wheel is open.
4. **Inventory:**
   - Hold Y in each chapter. Check the objective matches.
   - Check a short tap of Y still works as before.
5. **Toon:**
   - Switch HENRY > STYLE to TOON.
   - Look at your gloves and shoes; get ink on them.
   - Check Lifelike looks as it did.

## Earlier build: 0.7.2 (0.7.1 plus weapon hits that always count)

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

## Earlier build: 0.7.1 (fixes from your 0.7.0 recording)

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
