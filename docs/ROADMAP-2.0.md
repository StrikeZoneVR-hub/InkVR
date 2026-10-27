# InkVR 2.0: Polish and Comfort

The theatre's rebuild (the hall, backstage, the dressing room, the cellar, the projection booth, the Fun Hall, the
fan finds and Mr. Drew's office) came in 1.0, with the twelve body styles and the watches. 2.0 adds no new
features: it's bug fixes and comfort only, so that every chapter plays smoothly and anyone can play in comfort.

Ground rules carry on from 1.0:
- Every build goes to your headset first. Nothing goes public until you've tested that exact build.
- No official assets are shipped. Art is read from your installed game, and sounds are synthesised.
- InkVR's own progress (finds, tickets, scores, prizes) stays in InkVR's own file, never in the game's save.
- Nothing new to learn: every change is a fix, or an option that makes what's already there more comfortable.

---

## The builds

Each build is one test round for you. Your feedback can reorder what follows.

| Build | What it brings | Done when (your test) |
|---|---|---|
| **1.1 Fixes from your 1.0 test** | Everything you and your testers find in 1.0, in the theatre and in the game. | Nothing from your 1.0 list is left. |
| **1.2 Comfort** | The comfort options below, in VR SETTINGS and the launcher. | You can play a whole chapter in comfort with the settings you like, seated or standing. |
| **1.3 Performance** | A frame-rate pass through every chapter and the theatre on your laptop. | 90 fps in the theatre and steady frame rates in every chapter. |
| **1.4 Certification** | A full playthrough of Chapters 1 to 5 and the Archives in VR, every cutscene and scripted moment checked, and a stress test of quitting to the menu and reloading. | Every chapter plays start to finish with no softlocks. |
| **2.0** | Polish from your tests, then GitHub. | You're happy to call it finished. |

---

## Bug fixes

- **Your 1.0 list first.** Anything that breaks, looks wrong or feels off in the new theatre, the Fun Hall's games,
  the styles, the watches or the game itself.
- **Cutscenes and scripted moments:** the rest checked one by one (knockouts, falls, grabs, Chapter 1's floor
  collapse), so the view never jumps, tilts or ends up in the floor.
- **Grips:** guns held in two hands with the natural grips, and every item checked in each hand.
- **The satchel:** using a satchel item on its target in every chapter.
- **Errors are logged once**, not thousands of times, so a bug can never eat your frame rate.
- **Quitting to the menu and reloading** checked for everything InkVR keeps (ink, footprints, held items, the
  satchel, the theatre).

## Comfort

### Moving
- A comfort vignette for walking and smooth turning (off, light or strong), in the game and in the theatre.
- Walking where your hand points, as an option to walking where you look.
- Snap turning in the theatre too, with the same angle as in the game.
- The stick dead zone, and the walking speed in the theatre.

### Your body and the view
- Recentring and height calibration from VR SETTINGS, without the launcher. Seated height as a slider.
- The seat height in the theatre's seats, for seated play.
- Body and arm length checked for tall and short players.

### What you see and hear
- HUD distance and size, and subtitle size.
- Haptics strength.
- Volumes for InkVR's own sounds (the theatre's footsteps, the Fun Hall's games and the theatre's ambience) separate
  from the game's.

### Knowing what's there
- A soft ink outline on what you can grab, pull or use from afar (optional).
- VR SETTINGS laid out in tabs with a line about each option, and three presets: Comfort, Default and Immersive.
- A "What's new" card the first time you start after an update, and a tip the first time each control comes up
  (for example, "click the move stick to walk the theatre").

### The launcher
- REPORT A BUG: zips your logs and settings in one click, ready to send.
- Import and export of your settings.

## Performance

- 90 fps in every chapter on your RTX 4050 laptop (Chapter 3 runs at 60 to 75 fps today).
- The theatre draws only the rooms near you (and what you can see through their doors), and its light is cached
  between runs, so it opens in a second however you've dressed it.

---

## Order of work

Dark Revival VR comes first after your 1.0 test, as you planned. InkVR 1.1 starts after that, unless you'd rather run
the two side by side.
