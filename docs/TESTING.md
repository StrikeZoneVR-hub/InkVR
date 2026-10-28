# Testing InkVR

## Current build: 1.1.0, the Henry Update (with 1.0.1's tape notes and mirror laser)

1.0 is on GitHub; 1.1.0 is local and installed in your game. Nothing goes up until you've tried it.

### Doors (you: "doors would not open slowly with hands, or push open like a real door")
- **What went wrong:** when your hand opened a door, InkVR told the game (so the story hears it, as with X), and the game then swung the door to its own 145 degrees and locked it open: a gentle push flung it wide and it never moved again.
- **Now:** the door stays exactly where your hand has it, slow or fast; when the game locks a door open it stays yours to swing (stopping just short of the frame); a grip takes hold from 14 cm of the leaf or handle (8 cm missed often).

### Henry, rebuilt (you: "a way better model for henry in blender ... only two need a rebuild which is henry and toon henry", then "just remove toon henry and have just that one with the watches fitting the arm correctly")
- **One Henry:** built in Blender from MakeHuman's free (CC0) figure, aged to about 60: grey hair, waistcoat and tie, rolled shirt sleeves, trousers, shoes, a 53-bone skeleton. It ships with InkVR as a 7 MB file and loads in about 0.1 s.
- **Taken out:** Toon Henry and every other style. A saved choice of another style comes back as Henry. STYLE is gone from VR SETTINGS, the launcher and the mirror's panel (the panel now changes only the watch).
- **Worn like the 1.1 characters were:** Henry's invisible skeleton does the standing, crouching and stepping; the model's arms reach your hands, its fingers close as you grip, and your view leaves its head out (the mirror and your shadow show it). The forearm now turns half way with your hand, so the wrist doesn't wring.
- **The watch fits his arm:** the strap goes round the skin of his forearm just above the wrist (fitted to its shape every frame, so it never sinks in or floats), and it turns with your hand like a real watch: turn your wrist to read it. All six watches.
- **Hands like the old Henry's:** see the fixes above.
- **Ink:** drips land on his hand and run down the wrist; wading inks his trouser legs and shoes up to the ink line; a soaking covers him head to toe. About 1.3 ms a frame while he's soaked.
- **Checked on the test runtime:** Henry in the lobby mirror and from all sides; the watch from four sides with the wrist turned both ways, and from your eyes; the mirror's watch arrows (next and back); Chapter 1 (the body costs about 0.5 ms a frame); drips, wading and a soaking. Your save is unchanged and your settings were put back.

### Fixed from your first try of the new Henry
- **"some skin shows on the shoes":** the foot under each shoe showed through the leather on the instep and the heel. The whole foot inside the shoe is gone now (checked with his skin tinted green: none shows).
- **"hands don't close correctly like thumb not right and fingers go into hand on close" / "normal hand pose too looks like claws":** his fingers and thumb now copy the old Henry hand joint for joint (it's still posed every frame, hidden), so they rest, close and hold things exactly as before; his palm is also lined up with the old hand's (it was 11 degrees off). Checked open, half and closed against the old hands side by side.
- **"when stretching arms it glitches out a bit":** his shirt sleeves ended in a ragged edge halfway up the arm and, with the arms up, tore away in strips down his sides, because the cloth followed MakeHuman's helper layer instead of the skin. Now the shirt and waistcoat follow the skin right under them, the sleeves are rolled to just below the elbow with a proper rolled cuff, and the waistcoat's armholes are a clean line. Also: the forearm's turn no longer flips half a turn, the arm's roll stays steady when it's straight, and on a long reach his shoulder comes forward first.
- **"watch still showing skin a bit":** the strap is fitted to sixteen points round the arm at three places along it (it was eight at one), with a little more room.

### What to try (1.1.0)
1. A door: ease it open slowly with your hand, then push one hard. Pull one towards you by its handle.
2. The lobby mirror: wave, crouch, turn round. Does Henry look right? Change the watch with the arrows.
3. Raise your left hand and turn your wrist to read the watch, with each watch.
4. In a chapter: look down and forward; do the arms and hands feel like yours? Walk through some ink.
5. A tape note (from 1.0.1): does the page come up, and can you read it?

## Previous try of 1.1.0: the Characters Update (since replaced by one Henry)

### The characters (you: "a complete realism and everything connected overhaul ... as close as possible to the actual characters")
- **Eight styles are the game's own models**, loaded from your install when they're picked: the Ink Demon, Sammy, the Projectionist and a Searcher (the game keeps these ready to load), and Boris, Chapter 3's Alice Angel, Brute Boris and Bendy (read from the statues in the game's Archives scene; I wrote a reader for the game's files so the mod can take them from your copy).
- **How they move:** Henry's body still does the standing, crouching, stepping and stairs, invisibly. Each character is fitted to it: scaled so its eyes are at yours, its torso hung from your neck the way Henry's is (it drops back when you look down, so your view stays clear), its legs reaching the planted feet, its arms reaching your hands (elbows down and back), its own fingers closing as you grip, your watch on its wrist.
- **What you see:** your own view shows the body without its head; the lobby mirror and your shadow show the whole character.
- **Bendy:** the game only has him as a T-posed concept statue in the Archives, so InkVR gives it a skeleton as it loads. The statue's head is a cutaway (a slice taken out to show the gears inside), so he wears InkVR's drawn Bendy head, made bigger to suit his body.
- **Ink:** the game models take ink like Henry (wading, drips, splashes); tested wading as Sammy.
- **Still drawn, as you chose:** Henry, Toon Henry, Wally and Joey; their shoulders now move with the arms. Beast Bendy stays drawn for now too.
- **Checked on the test runtime:** every game-model style from the front, without its head (your view), and looking down; open hands and fists; the watch on Sammy's and the Projectionist's wrists; Sammy in Chapter 1; the axe in Boris's fist in Chapter 2; ink wading as Sammy (about 0.7 ms a frame). Your save is unchanged and your settings were put back.

### What to try (that try)
1. In the lobby mirror, step through the styles with the laser and look at each one: hands up, crouch, turn round.
2. In a chapter, pick a few: do the arms and hands feel like yours? Is anything in your view when you look down or forward?
3. Pick up the axe and swing as Boris or Brute Boris (Brute Boris's hands are as big as the game makes them).
4. A tape note (from 1.0.1): does the page come up, and can you read it?

## Previous build: 1.0.1 (folded into 1.1.0) (tape notes up close and sharp, a laser for the mirror)

1.0 is on GitHub. 1.0.1 is local and installed in your game; it goes on GitHub once you've tried it.

### Tape notes (you: "not readable, very pixelated and kind of blurry")
- **What went wrong:** the close-up page never came up in 1.0. To lift the note out of the HUD, InkVR asks where each piece of the game's note card is on the screen. It handed the game a plain array to fill in, which the game filled in a copy of, so every corner came back as zero: the whole card a point in the middle of the screen, which InkVR took for "no note". What you read was the note in the corner of the far HUD, 1.6 m away.
- **Now:** the page comes up half a metre in front of you, a little below and to the left (0.42 m tall, a touch larger than 1.0 meant). The game's UI is drawn at 4K instead of 1440p, so the note has about 1100 x 1450 pixels, and the page has its own sharp copy of it.
- **Your headset's resolution:** your logs show the game rendering each eye at 1210 x 1278 (70%). That softens all small text in VR; a higher resolution in Virtual Desktop or SteamVR makes the notes even crisper, if your laptop has room for it.
- **Checked on the test runtime:** a Chapter 1 tape: the page comes up and reads clearly (pictures taken); the menus, Options and the pause menu lay out exactly as before at 4K, and the laser clicks Options and Resume. Your save is byte for byte unchanged and your settings were put back.

### The mirror's arrows by laser (you: "a laser point for choosing full body styles in the mirror will be better than pressing the button")
- In the lobby, point either hand at the brass panel beside the mirror: a laser and a dot show where. The arrow you point at sinks a little (and the controller ticks). Pull that hand's trigger to press it. The panel now says POINT AT AN ARROW OR PRESS IT; pressing with a finger still works.
- **Checked on the test runtime:** pointing at STYLE's next arrow and pulling the trigger twice went Henry, Toon Henry, Bendy (the mirror showed each); WATCH's back arrow went from the Ink Demon's watch to Alice's.

### What to try (1.0.1)
1. Play a tape in any chapter: does the page come up in front of you, and can you read it comfortably? Too big, too small, too close, too low?
2. In the theatre lobby, point at the mirror's arrows and pull the trigger, with each hand.
3. Menus with the laser, as before.

## Previous build: 1.0.0 (the update: everything since 0.9.1, the old theatre with a mirror, thirteen styles, six watches)

1.0 is the single update that goes on GitHub once you've tested it: 0.9.2, 0.10.0 and 0.10.1 below, plus these. The release notes are in [CHANGELOG.md](../CHANGELOG.md).

### Fixed in 1.0 (from your fifth test): the axe and the throwing cans in your hand
- **The axe:** the game has copies of the same axe model turned differently inside them. Chapter 2's (the one you've played with) has its blade a quarter turn round from Chapter 4's (Bertrum's, the one you got next) and from a spare one in Chapter 2. The grip took whatever turn the copy had, so Chapter 4's sat with its blade the wrong way. And which end is the head was judged from the game's hand mount, which one Chapter 3 copy has at the wrong end. Now the head is found from the model itself (the wide end), and a blade is turned with its flat across your palm, so every copy sits like Chapter 2's.
- **The throwing cans** (Chapter 4's empty cans, a weapon you throw: the model is literally "EmptyCan", and they can't be eaten in the flat game either): they were gripped like a tool handle, through a closed fist near one end, upside down, and the game's held can is drawn 32 cm tall for a camera right behind it. Now: upright (the lid end up), held round its middle with the fingers open round it, at a soup can's size (14 cm). The full cans you eat were fine: your log shows four eaten.
- **Checked on the test runtime:** the Chapter 2 axe holds exactly as before; the same axe turned to the Chapter 4 copy's angle now sits identically (handle along the fist, blade flat across the palm); a Chapter 4 can, picked up, sits upright in the hand with its label the right way up (picture taken); the Gent pipe on your own Chapter 5 save is unchanged (its two ends are alike, so the old rule still decides). Your save is byte for byte unchanged.
- **Not reachable on the test runtime:** Chapter 3's weapon station (plunger, wrench, ink tool, Tommy gun) only opens through the story. The new rules only change bladed tools (the axe, possibly the wrench, held flat across the palm) and tools with one clearly wide end (held by the other end); round tools and guns are as before. Please look at each as you pick it up.

### Fixed in 1.0 (from your fourth test): Chapter 4's fairground games
- **What went wrong:** to power the haunted house, the story needs both the ball toss (knock down 5 bottles with 3 balls) and the shooting gallery (10 hits from 20 shots) won. The flat game aims both with its camera, which in VR is your head. The ball toss threw every ball the way you were looking (the ball isn't the kind of throw InkVR steered by hand), and the gallery shot where you looked, with no gun in your hand. Your log: plenty of throws, no win, and the fair games lever pulled before the games were won.
- **The ball toss now:** point at a bottle with your throwing hand and pull, then let go of, the trigger. The ball leaves your hand on the arc that lands on the bottle nearest where you point (within about 12 degrees), aimed at its middle, brisk enough to knock it off its stand. A gold arc shows where it'll go; swing as you let go if you like (the throw goes where you pointed as you began the swing). No more weak lobs from a gentle release.
- **The shooting gallery now:** the pellet gun is in your hand with a thin gold sight line; the trigger fires where it points (the game's shot is taken from your hand for the moment it fires; your view never moves).
- **Checked on your own save** (Slot 1, read-only; byte for byte unchanged): the ball toss won with three balls (6 bottles of 5 needed) by pointing at the bottom bottle of each stand; the gallery won with the gun aimed by hand (16 hits of 10 needed). Both of the game's own "won" flags set in one session. Next, pull the fair games lever.
- The hammer (High Striker) is a press-the-trigger-fast game and isn't needed to go on; it works as before.

### Fixed in 1.0 (from your third test): Chapter 4's flashlight in the vent, for real this time
- **The real cause:** the flashlight is switched on by the story when the stairwell door into the holding room is opened, and the story listens for the door being *interacted with*. A door pushed open by hand was opened directly, so the story never heard it: the stairwell never finished, the holding room never started, and the flashlight stayed off (`m_Active = False` at your checkpoint, after your door push). My earlier test switched the flashlight on by hand, which hid this.
- **Fix:** a door pushed open by hand now tells the game through its own interaction (exactly what X does), then stays where your hand has it. If the story locks the door open behind you (this one does), it stays as wide as the game opens it, so you can't end up locked behind a crack.
- **Checked on your own save** (Slot 1, loaded read-only in a test session that can't save; your save is byte for byte unchanged): finished the Abyss the game's own way, pushed the stairwell door open 5 degrees by the hand path: the story moved on, the door locked open, the flashlight switched on. Then, standing at the vent and looking at the flashlight, a grip played the game's vent scene (Henry in the vent, the flashlight lit, the Lost Ones peering in), and the chapter moved on.
- This applies to every door you open by hand, in every chapter, so no other story door can stall the same way.

### Fixed in 1.0 (from your second test video): using things, and Chapter 4's vent
- **What went wrong:** things only answered a hand right on them (within about 40 cm), so prompts didn't show until you walked up to things in real life (the bridge lever). In the Lost Ones' room, the vent's way in is the flashlight lying inside it; your hands never got close enough, and your grip went to the trunk 2.5 m away instead (your log shows it used twice).
- **Now, three ways find what you use, in this order:** a hand on it; either hand pointing at it, out to the game's own reach (8 units, about 2.4 m, walls stop it); or, with neither hand on or pointing at anything, what you look at. The game's highlight and prompt follow it, and grip (either hand) or X uses it. Something nearer that you point at or look at always wins over a gravity pull further off.
- **Motion controls made easy:** with your hand on a valve or a lever, keep gripping for 1.5 s and it's done however your hand moved (it was 4 s of holding perfectly still). A valve needs 90 degrees, a lever a shorter pull. A gravity pull starts as soon as you grip.
- **Also fixed:** the weapon wheel failing to open in Chapter 4 (a destroyed weapon object; four errors in your log).
- **Checked on the test runtime:** Chapter 1, pointing the right hand at a door 1.4 m away made it the game's target. Chapter 4, standing at the vent: pointing at the flashlight from 1.25 m, then gripping, took it (in your game, that's what sends Henry through the vent); with no hand pointing, just looking at it made it the target, and a grip with the other hand took it. The launcher's controls page explains the new way.

### Changed in 1.0 (from your test): the theatre is back to how it was
- The rebuilt theatre (backstage, the cellar, the booth, the Fun Hall, the fan finds, the office) is gone, as you asked: the theatre is the one you walked in 0.10, with the hall, the stage, the lobby, the stairs and the balcony.
- Kept: your body walks with you, and soft footsteps play on the boards and the carpet.
- **New: a mirror in the lobby**, on the side of the left staircase by the piano. A brass panel beside it has STYLE and WATCH arrows: press them with a fingertip. The style's name is over the mirror.
- Beast Bendy is now an ordinary 13th style (it used to be the office's prize).
- **Checked on the test runtime:** the mirror shows you and the lobby; pressing STYLE went Henry, Toon Henry, Bendy (Bendy in the glass, head to toe); pressing WATCH changed the watch; walking from the lobby into the hall; the theatre builds in 1.5 s with no errors.

### Fixed in 1.0 (from your test): the laser after you've used the stick
- **Cause:** the game's menus have a mouse mode and a controller mode. Any push of the stick switches them to controller mode, and then pointing doesn't move the highlight, and a click acts on whatever the stick picked last. On the title screen, with the stick on QUIT, a laser click on OPTIONS quit the game.
- **Fix:** while the laser is on the screen, the game is told you're using a mouse. Pointing moves the highlight again, and a click goes to what you point at.
- **Checked:** title screen: stick to QUIT, laser click on OPTIONS opened Options. Options: stick to ADVANCED, laser click on SETTINGS opened Settings, and on VR SETTINGS opened it. Pause menu in Chapter 1: stick to OPTIONS, laser click on RESUME resumed the game.
- Each laser click now writes one line to the log (what was pressed and what got clicked), so if one goes astray, your log will show why.

### Fixed in 1.0: laser clicks that didn't register
- **Causes:** (1) the click went to whatever the laser was on when you let go of the trigger, and letting go jolts the hand, so on a small button the laser could slide off it just then and the click landed on the background; (2) the trigger had one threshold, so a half-pull could flicker on and off; (3) after quitting to the menu, the laser kept using the old menu system's pointer, so clicks could go nowhere.
- **Fix:** what you press is what gets clicked, if you let go on it or near it. The laser steadies as you squeeze and holds still on what you pressed (sliders still drag). The trigger presses at 55% and lets go at 30%. A new menu system gets a new pointer.
- **Checked:** on the title screen, the laser opened Options; pressing VR SETTINGS, sliding 55 pixels off it and letting go still opened it (that click used to be lost); two clicks on TURNING switched it to SNAP and back.

### Changed in 1.0: the laser is on by default
- For the main menu, the pause menu, Options and VR SETTINGS (in the theatre the stick can walk you about now, so the laser picks). New installs start with it on; your own settings already have it on.

### Fixed in 1.0: throwing what you carry with the satchel off
- The throw was part of the satchel, so with the satchel off (the new default) a carried thing only went into your pocket. Now let go mid-swing and it flies, lands, and melts away (it's in your inventory already).
- **Checked:** Chapter 1's wrench, satchel off, thrown at 4.7 m/s.

### What to try (1.0): a start-to-end playthrough
Play Chapters 1 to 5 and the Archives start to end. Things to look at on the way:
1. Every weapon as you pick it up (Chapter 2's axe, Chapter 3's station weapons, Chapter 4's axe and throwing cans, Chapter 5's pipe): held by its handle, the right way round? Can you swing and throw?
2. Every door the story waits on: does the next part start once you've pushed it open?
3. Using things: point (either hand) or look, then grip or X. Valves and levers: just hold the grip if the motion's awkward.
4. Chapter 1's darts and Chapter 4's fair games.
5. Bacon Soup: pick a full can up, pull the lid off with your other hand, tip it to your mouth.
6. Menus with the laser, also after moving the stick; the theatre mirror; quitting to the menu and continuing.
If anything stops you, your log (BepInEx\LogOutput.log) says what happened: send it with a note of where you were.

## Previous build: 0.10.1 (pull and reach story items, solid weapons, the satchel optional)

From your 0.10.0 test: "all the story objects and other stuff does not let me pick up with the gravity pull... for most puzzle stuff I have to bend down, reach up or move very far", "weapons when held look like the hand is see-through and not very physics based", "the backpack can be off by default". Plus three bugs found in your log.

### Fixed in 0.10.1: story items you couldn't pull
- **Cause:** the pull only took things the mod recognised as pickups, by their name and by the game marking them "use once". Many story items aren't marked that way (Chapter 3's ink hearts, for one), so the pull ignored them. Your whole session's log has only one pull in it (a book).
- **Fix:** story items count by their name even without that mark (hearts, keys, batteries, books, records, reels, spoons, dolls, gears, cores, thick ink...). Empty slots and placeholders never count (Chapter 1's invisible battery slots, Chapter 5's empty reel and pipe spots).
- **Checked:** a Chapter 3 heart pulled 2 m into the hand. A pull the game doesn't accept (an errand that hasn't started yet, say) puts nothing in your satchel or pocket: the thing stays where it was.

### New in 0.10.1: use things out of reach
- Point at something that stays put (a valve, a lever, a switch, a button, a tape recorder, a chest, a toilet, a punch clock...), grip and flick back (or keep gripping): the ink thread snaps to it and it's used where it is, as if you'd reached it. So the puzzle bits up high, down low or across the room no longer need you to bend, stretch or walk over.
- Only when it's out of arm's reach (over 75 cm). Closer than that, your hands work it as before (turn the valve yourself, and so on).
- Something you can pick up wins over something to use, when both are where you point. Doors stay yours to push, and the hiding cupboards, enemies, the fairground games and the carts are never used from afar.
- **Checked:** a Chapter 2 tape recorder used from 2.2 m (Sammy's note came up).

### Fixed in 0.10.1: the hand looking see-through round a weapon
- **Cause:** the game draws the weapon in your hand with a camera of its own, after everything else, so on a monitor it never pokes into walls. In VR that painted it over Henry's fingers and wrist, and over walls.
- **Fix:** the weapon is now drawn with the world. Your fingers and wrist close over the handle, and a wall hides what's in it. The game's brightness setting still applies (its weapon camera is also where that happens, so it stays on, drawing nothing).
- **Checked:** the same view with this on and off: off, the handle is painted over the forearm; on, the wrist is in front of it.

### New in 0.10.1: solid weapons
- The weapon in your hand stops at walls and floors instead of sinking into them. First it tips back in your fist, the way a wrist gives (rest the axe's head on the floor and it stays on the floor, your hand where your hand is). Only past 50° does your hand get held back too. The controller buzzes against the wall.
- Only solid, unmoving things stop it. Enemies, planks and everything else it can hit are left to your swing.
- A swing knocks loose things about, but only things the game already lets roll and fly (pool balls, broken bits): nothing it holds still is moved, so nothing the story needs can be knocked out of place.
- WEAPONS in VR SETTINGS (Henry) puts back the game's way: drawn over everything.
- **Checked:** the axe held pointing down tips 13° about the fist instead of pushing your hand up; held level, nothing changes.

### Changed in 0.10.1: the satchel is optional, and off unless you turn it on
- SATCHEL in VR SETTINGS (Controls) or SATCHEL (OPTIONAL) in the launcher. The launcher's controls page and the README say how it works.
- **Your own settings still have it on** (0.10.0 added it switched on). Switch it off there if you'd rather not have it.

### Fixed in 0.10.1: errors after quitting to the menu (from your log)
- **Ink splashes:** after you quit to the menu and went back into Chapter 3, every ink drop that landed failed (about 109,000 errors in your log, through Chapters 3 and 4). Quitting destroys what InkVR keeps between levels, and the splash pool kept reusing its destroyed splashes. It now starts afresh.
- **Footprints:** the same, about 350 times.
- **The theatre's toys** and Henry's head lookup tripped once each on the same reset.
- **Checked:** ink bursts, quit to the menu, back into a chapter, more ink bursts: no errors.
- Your Chapter 3 frame rate (60 to 75 fps) was the chapter itself, not these errors: it was the same before they started.

### Noticed in your settings
- WALK THE THEATRE is off. If that wasn't on purpose, switch it back on in VR SETTINGS (Controls).

### What to try (0.10.1)
1. Pull story items in Chapter 3 and 4 from a few metres (hearts, gears, whatever the errand wants). Point at a valve or lever up high and grip-flick it.
2. Hold the axe and look at your hand on the handle; rest it on the floor; push it into a wall.
3. Swing at a pool table's balls in Chapter 2's break room (if they're there in your save).

## Previous build: 0.10.0 (walk the theatre, a satchel, natural grips, a quiet axe)

From your tester's report (a backpack on the left shoulder, throwing, a more natural axe, a main menu to walk round and explore, seated gravity pull) and your own notes (hands in the shadow, natural grips for everything, blurry tape notes, the loud axe).

### Fixed in 0.10.0: the axe making a loud sound at every small movement
- **Cause:** the axe's head is 60 cm out from your hand, so even a turn of the wrist moves it fast, and every one counted as a swing and played the game's whoosh. Knocking a wall or the floor with it also played the game's loud hit.
- **Fix:** the whoosh now plays only for a real swing of the arm (the head fast *and* your hand moving), once per swing, louder the harder you swing. Bumping a wall or the floor buzzes the controller but stays quiet unless you really swung. Hits on enemies and planks are unchanged.

### New in 0.10.0: a satchel over your left shoulder
- Pick something up and it stays in your hand. Let go of it behind your left shoulder and it goes in the satchel.
- Reach over your left shoulder and grip: the satchel comes round, held by its strap, with what's in it floating in front for your other hand to take.
- Hold a thing to what it's for (a gear to its machine, a battery to its slot) and let go to use it. Let go anywhere else and it goes back in the satchel.
- A new objective empties the satchel, the way the game uses your items up.
- SATCHEL in the launcher (Controls) turns it off.
- **Checked on the test runtime:** Chapter 1's wrench taken by hand, put away at the shoulder, the satchel brought round with the wrench floating in front of it, taken out again. Not tried yet: using an item on its machine from your hand. Tell me how that goes.

### New in 0.10.0: throw what you carry
- Let go of anything you're carrying mid-swing and it flies, bounces and lands, then goes back in the satchel. Bacon Soup off a shelf flies too, and lands where it lands (still a can of soup you can pick up).
- **Checked:** the wrench thrown, landed, and back in the satchel; a Chapter 1 soup can thrown at 4 m/s.

### New in 0.10.0: natural grips
- The axe sits in your fist by its handle, the knob at the bottom; your other hand snaps onto the handle when you hold it with both, and your arm follows it. The game's own bob of the weapon no longer moves it about in your hand.
- Tools, items and soup cans settle into your hand by their longest side, fingers closed round them.
- GRIPS in the launcher (Henry) puts them back the way the game holds them.
- **Checked:** the axe's handle lies along your fist (within a centimetre), and the second hand lands exactly on the handle. Guns keep their aim as before; their two-handed grip hasn't been tried with the new grips yet.

### New in 0.10.0: walk the theatre
- In the main menu or the pause menu, **click the move stick** to walk around the theatre (click again to use the menu). The move stick walks where you look (the left trigger hurries), the turn stick turns. With the laser on, pushing the stick while you're not pointing at the screen walks too.
- Fully mapped out: the aisles, three steps up onto the stage, through the auditorium doors at the back into a new lobby, and up either staircase to the balcony. Walls, seats and rails stop you, and the floor rises and falls with the rows and stairs.
- Things to find: the auditorium doors swing open as you come; a ticket booth with a desk bell to ring; Bacon Soup on the concession counter to pick up and throw; an upright piano with all 88 keys to play (it may play itself when your back's turned); a cardboard Bendy that moves when you aren't looking; writing that seeps out of the wall when you look at it; locked doors that rattle (and something knocks back); a little Bendy doll on the balcony rail; a reserved seat in the front row; a coming-soon poster and a plaque.
- Every time the theatre opens you're back in your seat, facing the screen, and everything's back where it was.
- WALK THE THEATRE in VR SETTINGS or the launcher turns it off.
- **Checked on the test runtime:** walking the hall, the stage steps, the lobby, both staircases and the balcony tiers, with the floor heights right everywhere; the bell, a piano key, a thrown can, the thrown doll (it lands in the aisle below the balcony), the doors, the writing and the cut-out moving.

### New in 0.10.0: seated gravity pull
- Seated, the pull reaches 8.5 m (5 m standing) and forgives a looser aim. More of the game's pickups can be pulled and taken by hand: Chapter 2's missing valve wheel, Chapter 3's valve cranks, records, reels, nuts and the like.

### Fixed in 0.10.0: your shadow without hands (Lifelike), blurry tape notes
- In Lifelike style the hands now cast their shadow too (fingers and all). YOUR SHADOW also switches straight away now, and has a row in VR SETTINGS.
- A tape's note (who's speaking and what they say) comes up close in front of you as a sharp page to read, instead of small in the far HUD; the game's menus and HUD are drawn sharper too. TAPE NOTES in the launcher puts it back in the HUD.

### What to try (0.10.0)
1. Pick up the axe and move it about gently: no sound. Swing properly: one whoosh per swing. Hold it in both hands.
2. Take a gear or a battery, put it over your left shoulder, take it back out, throw it.
3. At the main menu, click the move stick and go exploring.
4. Play seated and gravity-pull something across the room.
5. Play a tape and read its note.

## Previous build: 0.9.2 (your own shadow)

### New in 0.9.2: Henry casts your shadow
- As you asked: Henry's body, arms and hands cast a real-time shadow that moves with you, wherever the game has a light that casts shadows. His head casts one too, though you never see it: a head-sized shape that's drawn only into the shadow, so nothing blocks your view.
- **Checked:** under a test light, the shadow on the floor ahead is Henry's: body, arm, hands and head, moving with you. With no shadow-casting light nearby (most of the studio's light is baked), there's no shadow, just as for everything else there.
- YOUR SHADOW in the launcher's options turns it off.

## Previous build: 0.9.1 (steady camera shakes: the Chapter 1 finale works again; no more prop pick-up)

From your 25 Oct showcase recording ("cutscenes broke").

### Fixed in 0.9.1: the Chapter 1 finale throwing your view about and into the floor
- **Seen in your recording at 22:05 to 22:32:** in the ink-flood chase at the end of Chapter 1, the view kept jumping and blinking, dropped to floor level, and ended under the ink.
- **Cause:** the finale shakes the game's camera. InkVR took every shake for a cutscene and followed it. When a shake ended, the game put its camera back to its resting spot, which in VR is down at Henry's feet, and InkVR followed it there too. It wasn't the props: your log shows the shakes starting at the finale, long after the last chair.
- **The same would have happened** with every other shake in the game: Chapter 3's ending, Brute Boris's footsteps (Chapter 4), the Beast's charge (Chapter 5).
- **Fix:** InkVR now knows when the game starts shaking its camera, and keeps your view steady instead (a shake in VR only makes you sick). A small jiggle that doesn't turn the camera never starts a scene, and the camera going back to rest is never a scene's view.
- **Checked:** the finale's own shake, called in a test: no scene started, and screenshots before, during and after it are identical. Real scenes still play: the darts game's camera, Chapter 2's opening, and Sammy's knockout (its blow is now steady; the camera move after it plays as a scene as before).

### Removed in 0.9.1: picking up chairs and props
- As you asked. Grabbing chairs, stools, bins and gear panels is gone, along with its PICK UP PROPS option. Soup, darts, items, valves, levers, doors and the two-handed axe are unchanged.

### What to try (0.9.1)
1. Play the end of Chapter 1 (power the ink machine, then run from the ink flood): your view should stay in Henry's head, steady, through the chase. The fall at the very end hasn't been tried yet: tell me how it looks.
2. Anything from 0.9.0 below (soup, valves, levers, darts, the two-handed axe).

## Previous build: 0.9.0 (fully manual: soup, valves, levers, darts, chairs, two hands on the axe)

From a tester's report ("everything should be manual") and your own notes ("soup cans disappear in hand when grabbing it", "holding axe with two hands would be amazing").

### Fixed in 0.9.0: Bacon Soup vanishing from your hand
- **Cause:** when the game was built, most small props were merged into one big mesh per level, the cans on the shelves included. A can you took had no mesh of its own to copy into your hand, so the copy drew some other part of the level somewhere else, and your hand looked empty.
- **Fix:** a merged prop is now drawn from its own part of the level's mesh, wherever your hand carries it. The same fix covers items flying to you on the ink thread and the chairs below.
- **Checked:** a can taken from the shelf shows in your hand, label and all.

### Changed in 0.9.0: soup by hand, start to finish
- Grip a can to pick it up (no X). It's sealed: grip the lid with your other hand and pull it away, and it comes off and falls. Then tip the can to your mouth to eat.
- A sealed can held at your mouth buzzes, and opens by itself after a couple of seconds, so you can still eat with one hand.

### Changed in 0.9.0: valves and wheels turn in your hand
- Grip a valve or wheel and wind it round (or twist your wrist on a wheel held across its face). It turns with your hand and clicks every 15°. About a third of a turn opens it. Let go part way and it stays where you left it, ready to carry on.
- **Checked:** Chapter 1's basement valve: five winds of 25° round its middle opened it, the wheel visibly turning.

### Changed in 0.9.0: levers you pull yourself
- Grip a lever's handle and pull: the handle swings with your hand, and a firm pull (about 45°) throws it. Let go early and it springs back. This includes Chapter 1's power lever once the items are on their pedestals (no more X).
- **Fixed on the way:** that lever's handle didn't move at all, because its base is merged into the level's mesh and InkVR took hold of the base. It now takes the handle.
- Holding any valve or lever still for 4 seconds still finishes it, so nothing can get stuck.

### New in 0.9.0: darts and the ball toss by hand
- Start the darts in Chapter 1's break room (or the ball toss in Chapter 4) as usual. The dart is now in your hand. Hold the trigger, throw, and let go: it flies the way your hand was moving, as hard as you threw it. A gentle release lobs it where your hand points.
- **Checked:** a hard throw stuck in the board; a gentle lob landed short in the wall below; the next dart appears in your hand each time.

### New in 0.9.0: pick up chairs and props, and throw them (removed in 0.9.1)
- Grip a chair (or another loose prop: a box, a bucket) to pick it up. Let go to drop it, or throw it: it flies the way your hand moved and tumbles where it lands, and stays there.
- Never picked up: anything that's part of a puzzle, a door, a weapon, something you can break (planks, barrels, cutouts), and the building itself.
- **Checked:** the break room's chairs: picked up, shown in the hand, thrown across the room.
- **Turn it off:** PICK UP PROPS in the launcher's options (in the game it's part of MOTION CONTROLS).

### New in 0.9.0: two hands on the axe
- With the axe in your hand, grip its handle with your other hand: both hands hold it, and the axe lies along the line between them, so you can swing it two-handed like a real axe. Let go with either hand to go back to one.
- **Checked:** in Chapter 2, the handle followed the second hand to within a few degrees.

### Fixed in 0.9.0: a wall knock using up your swing
- 0.7.2 meant scraping a wall on the wind-up to only thud, and still let the swing hit. It didn't work: the game marks its whole level as "hittable" (so any wall makes a sound when struck), and InkVR took every wall, floor and invisible trigger box for something to hit.
- InkVR now counts only real targets: enemies, planks, barrels and cutouts.

### What to try (0.9.0)
1. Chapter 1: take a can of Bacon Soup from a shelf, pull the lid off with your other hand, and tip it to your mouth.
2. Turn the basement valves by winding them round; pull the power lever yourself once the items are placed.
3. Play darts in the break room, throwing for real, then pick up a chair and throw it.
4. Chapter 2 onwards: take the axe, grip the handle with your other hand too, and swing at a plank.

## Previous build: 0.8.4 (hits keep you in Henry, your body in every scene, solid ink, a steady theatre)

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
