# InkVR 2.0: The Grand Reopening

2.0 is the final InkVR update. After it, only bug fixes.

The 1.0 lobby has a sign on its boarded-up street doors: *CLOSED FOR RENOVATION, by order of J. Drew*. 2.0 is
that renovation finished. The theatre is overhauled, with a lot more for fans to find and do, an arcade, and
more quality of life and features across the whole game.

Ground rules carry on from 1.0:
- Every build goes to your headset first. Nothing goes public until you've tested that exact build.
- No official assets are shipped. Art is read from your installed game, sounds are synthesised, and any music is
  public domain or made in code.
- InkVR's progress (finds, tickets, scores, prizes) lives in InkVR's own file, never in the game's save.
- Scares in the theatre are optional (a THEATRE SCARES switch).

---

## The builds

Each build is one test round for you. Your feedback can reorder what follows.

| Build | What it brings | Done when (your test) |
|---|---|---|
| **1.1 Foundations** | Fixes from your 1.0 test. The theatre draws only the rooms near you, and its build is cached so startup stays at 1 to 2 seconds however big it gets. InkVR's progress file (finds, tickets, high scores, prizes). A tally board in the lobby for the fan finds. | The theatre and the game run as smoothly as in 1.0, and a find you made is still marked after a restart. |
| **1.2 Theatre overhaul, part 1** | The hall rebuilt and dressed up. Sit in any seat. When you start or continue a game the house lights dim and the curtains open. The projection booth opens up (house lights, the projector, film reels). Backstage and the dressing rooms. | You can walk from the stage to backstage and up to the booth, and the show starts with the curtains. |
| **1.3 The Fun Hall** | The arcade room, the first four games, tickets, the prize counter, and high scores. | Each game is fun for a few rounds and feels right by hand, and tickets and prizes are kept. |
| **1.4 Theatre overhaul, part 2** | The basement and its ink machine, the full fan-find hunt with all the lore nods, the photo booth, seasonal dressing, the locked office (opened by finding everything), and the other four arcade games. | You can find everything, the office opens, and nothing breaks. |
| **1.5 Quality of life and features** | Comfort options, highlights on what you can grab, the reworked VR SETTINGS, first-run tips, launcher upgrades, physical in-game items, and the rest of the cutscene checks. | A first-time player can learn InkVR without a guide. |
| **1.6 Performance and certification** | A 90 fps pass through every chapter on your laptop (Chapter 3 is at 60 to 75 fps today), then a full playthrough of Chapters 1 to 5 and the Archives in VR, plus a stress test of quitting to the menu. | Every chapter plays start to finish with no softlocks and steady frame rates. |
| **2.0 The Grand Reopening** | Polish from your tests. The street doors finally open (the 2.0 reveal). A trailer, then GitHub. | You're happy to call it finished. |

---

## The theatre, overhauled

### Does an arcade fit?

Yes. The theatre is built in code in empty space, so size isn't the limit. The real limits are frame rate and
startup time, and 1.1 handles both before anything big is added: only the room you're in (and what you can see
through its doors) is drawn, and the built theatre is cached between runs.

The arcade goes through a new door at the foot of the right-hand staircase, where the lobby's side wall is free:
a hall of about 14 × 12 m.

### The map in 2.0

- **The hall:** rebuilt. Sit in any seat, the house lights dim, and the curtains open and close for the show.
  Plus an organ by the stage.
- **The stage and backstage:** through the wings to the dressing rooms (Alice's has the mirror), a prop store and the
  fly ropes.
- **The balcony and the projection booth:** up a ladder from the balcony. The projector, film cans and the house
  light switches, and Norman's notes (he's the projectionist).
- **The lobby:** opened up, with the fan-find tally board, the photo booth, the high-score board and a trophy case.
- **The Fun Hall:** the arcade, off the lobby.
- **The basement:** down from backstage, to a working replica of the ink machine.
- **Joey's office:** locked until you've found everything.

### Fan finds

A proper hunt, with every chapter nodded to. Some examples:

| Chapter | Finds |
|---|---|
| 1 | Wally's "I'm outta here" note. Henry's old desk. The "Dreams come true" wall. |
| 2 | Wally's lost keys. Sammy's sheet music and a lyric on the organ. A sheep's bell. |
| 3 | Alice's mirror and her dressing table. Boris's soup bowl and bunk. A Bendy cutout that's been moved. |
| 4 | A Bendy Land ticket booth. A Bertrum blueprint. A gear from the Abyss. |
| 5 | A seeing-tool lens. A golden reel. The Archives shelves. |
| Theatre | Hidden cutouts to collect (moved around between visits), staff notes, a secret lever that floods the stage with ink, and a Searcher in the basement (only with THEATRE SCARES on). |

The lobby board counts what you've found. Finding everything opens Joey's office, which holds the 2.0 reward.

### More things to do

- Play the instruments: the piano (already in), the organ, a banjo and a drum kit.
- Run the projector in the booth: thread a reel and choose what's on the screen behind the menu (built from your
  game's own art), and work the house lights.
- Photo booth: strike a pose and get a souvenir photo of Henry with a Bendy frame, saved to your Pictures folder.
- Seasonal dressing that appears by itself on the right dates: Halloween (InkVR came out on 24 October),
  Christmas and more.
- Ring the ticket-booth bell: it now gives you a ticket for the arcade.

---

## The Fun Hall (arcade)

Every game is played with your hands. All of them are built in code in the game's ink style, and each one nods to
the game's own Bendy Land.

| Game | How you play | Nod to |
|---|---|---|
| Ink Toss | Throw balls into Bendy's grin | Chapter 4's ball toss |
| Strong Man | Swing a mallet and ring the bell | Chapter 4's hammer game |
| Shooting Gallery | A cork gun against Butcher Gang targets | Chapter 4's gallery |
| Whack-a-Searcher | Searchers rise from ink holes | Chapter 2's Searchers |
| Ink Ball | Bowl rolling balls up the lanes | Skee-ball |
| Darts | Board, scores, rounds | Chapter 1's darts |
| Claw Machine | Grab a Bendy plush | The plush toys around the studio |
| Ink Machine Pachinko | Drop a ball, let it bounce through the pins | The ink machine |

- **Tickets:** earned from scores, and spent at the prize counter.
- **Prizes are cosmetic only:** watch faces, glove styles, laser colours, theatre posters, a Bendy plush you can
  carry and throw. Nothing that affects the game itself.
- **High scores** go on the board in the lobby.

---

## Quality of life

### Comfort
- A comfort vignette for walking and smooth turning (off, light or strong).
- Walking where your hand points, as an option to walking where you look.
- Recentring and height calibration from VR SETTINGS. Seated height as a slider.
- Haptics strength, HUD distance and size, subtitle size, and the stick dead zone.

### Interaction
- A soft ink outline on what you can grab, pull or use from afar, and the gravity pull showing which it will do.
- A second holster slot for your off hand (the flashlight or an item).
- The things you hold in the game made physical where it can't break anything: hold Chapter 5's seeing tool up
  to your eye, and carry Chapter 3 and 4's flashlight in your hand.
- A check of guns held in two hands with the new grips.

### Menus and learning
- VR SETTINGS reorganised into tabs, with a line about each option, and three presets (Comfort, Default,
  Immersive).
- A "What's new" card the first time you start after an update, and tips the first time each feature comes up
  (for example, "click the move stick to explore").

### Launcher
- An optional update check that tells you when a newer release is out.
- REPORT A BUG: zips your logs and settings with one click.
- Graphics presets, and import and export of your settings.

### Performance and stability
- 90 fps in every chapter on your RTX 4050 laptop.
- Errors are logged once, not thousands of times, so a bug can't eat your frame rate again.
- Quit-to-menu and level-reload robustness checked for everything InkVR keeps.

---

## More features

- **Cutscenes:** the rest of the scripted moments checked one by one (knockouts, falls, grabs, Chapter 1's floor
  collapse), with an optional CINEMA view for big scenes.
- **The UI pass:** world-space subtitles and prompts, VR button glyphs, chapter cards and credits.
- **Mirrors:** the dressing-room mirror shows your full body, using the mirror from Dark Revival VR.
- **The trophy case:** InkVR's own achievements for the theatre and the arcade, shown in the lobby.
- **Photo mode in the game:** a camera you hold, for screenshots from inside the levels.

---

## Order of work

2.0 is big, so it comes in the steps above, each one tested by you before the next starts. Dark Revival VR comes
first after your 1.0 test, as you planned. InkVR 1.1 starts after that, unless you'd rather run the two side by side.
