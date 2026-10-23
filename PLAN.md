# InkVR — Full 6DoF VR Mod for Bendy and the Ink Machine

Status: **Test build 0.5.1 ready for your next headset test**: fixes from your 0.5.0 recording (Chapter 2 wake-up spin, legs hidden when looking down), on top of 0.5.0's Henry's full body in two styles (lifelike and toon) with BODY options, lifelike hands and a real wristwatch, plus 0.4.0's first motion controls, the 1930s picture palace and the shrinking and footstep-noise fixes. Headset-tested so far: M1 VR core, M2 button controls + Henry's hands, M3 launcher, M5 VR SETTINGS page + theatre menus (two rounds, 0.2.0 and 0.3.1). Go-ahead given for M0 → M6 (through cutscenes) on 2026-10-22; full body (M7) is for the full release.

Target setup: **Quest 3 · Virtual Desktop · VDXR OpenXR runtime** · RTX 4050 Laptop. Release: **open source on GitHub**.

Goals (all required):
- Full 6DoF (room-scale) VR with full motion controls
- Full-body IK
- Animated VR launcher using official artwork, with a closing animation
- A VR settings menu inside the game's own Options menu, in the game's own style
- 90 fps at max graphics settings
- Every chapter playable from start to finish

---

## Roadmap at a glance (updated 2026-10-22)

Each test build goes to your headset before the next one starts, and your feedback can reorder it.

| Build | What it brings | State |
|---|---|---|
| 0.2.0 | VR core, button controls, Henry's hands, launcher, theatre menus, VR SETTINGS | ✅ tested |
| 0.3.x | Correct world scale (AUTO = your height), single light beams, smooth walking, ink on the hands, hand-hover interaction | ✅ tested |
| 0.4.0 | **Motion controls part 1:** swing the axe, pipe or plunger with your arm; guns fire where your hand points; no more shrinking while walking; no more footstep/landing spam; the menu theatre built out into a full 1930s picture palace | ✅ built (tested with 0.5.0) |
| **0.5.0** | **Full body (M7), moved up at your request:** two styles you can pick in options, LIFELIKE (real-looking hands, waistcoat and tie) and TOON (cartoon hands, braces, big shoes); BODY: full body / arms & hands / hands only / none; feet that plant and step; a real tank wristwatch | ✅ tested (your recording, 2026-10-23) |
| **0.5.1** | **Fixes from your 0.5.0 recording:** the Chapter 2 wake-up spin (the game moved its camera out of the VR rig), legs visible when looking down, body hidden instead of sinking through the floor | 🟡 **your test now** |
| Trailer | **Teaser trailer:** opens with the launcher's opening animation, then your headset footage of the theatre and gameplay; two versions (official soundtrack, original music) | 🔨 in progress |
| 0.6.0 | **Motion controls part 2 (finishes M4):** throw Bacon Soup cans with your arm (Ch4), pull levers and turn valves by hand where the game has them, two-handed Tommy Gun, eat soup by raising it to your mouth, weapon holster | next |
| 0.7.0 | **Cutscenes & scripted moments (M6):** knockouts, falls, Sammy/Bendy grabs, deaths, the Ch1 floor collapse; your head always stays yours; fades on cuts; optional Cinema mode | |
| 0.8.0 | **UI pass (rest of M5):** world-space subtitles, readable prompts, note viewer, VR button glyphs, chapter cards, credits | |
| 0.9.0 | **Performance (M8):** a measured 90 fps pass through every chapter on your laptop | |
| 0.95 | **All chapters certified (M9):** full Ch1–5 + Archives VR playthrough, softlock checks, boss fights | |
| **1.0 full release** | Polish from your feedback, body ink and shadows, GitHub release | |

---

## 1. What's actually on your machine (recon results)

| Item | Finding | Why it matters |
|---|---|---|
| Game install | `C:\Program Files (x86)\Steam\steamapps\common\Bendy and the Ink Machine` (3.0 GB) | Inside OneDrive. Sync can lock or duplicate mod files (see Risks) |
| Engine | **Unity 2022.3.62f2** | No built-in "enable VR" switch. We have to inject an XR runtime ourselves |
| Scripting backend | **IL2CPP** (`GameAssembly.dll`, metadata in `il2cpp_data`) | Needs an IL2CPP mod loader. Game code is native, so we use generated interop assemblies, not direct DLL patching |
| Render pipeline | **Built-in** (no URP/HDRP or PostProcessing v2 assemblies) | Post effects are custom. Game shaders were built without stereo variants, so expect **multi-pass** stereo |
| Input | **InControl** | We can register a virtual VR controller device so every game action works in VR from day one |
| UI | uGUI + TextMeshPro + **DOTween** + **I2 Localization** | The VR menu can clone the game's real widgets, tweens and fonts so it matches the existing style |
| Cutscenes | **Unity Timeline** | Camera takeovers are detectable through `PlayableDirector`, so they can be made VR-safe |
| AI | Unity NavMesh (`Unity.AI.Navigation`) | Enemies path normally. Room-scale movement mustn't break them |
| Also present | `UnityEngine.SpatialTracking`, `XR.LegacyInputHelpers`, XR/VR modules | Some XR types survived the build, which helps injection (IL2CPP may still have stripped methods) |
| Logging | `boot.config` has `nolog=` (Player.log disabled) | We use BepInEx logging, and can re-enable Player.log for development |
| Build ID | `a8806fc9c9364648a7262c77abcffd72` | The mod and launcher pin to this build and warn when a game update changes it |
| Scenes | `level0`–`level10` (11 scenes) | Mapped to chapters/menus in M0 |
| Existing modding | Thunderstore has a **BepInExPack for Bendy and the Ink Machine** | Confirms BepInEx already runs on this IL2CPP build |
| GPU / CPU / RAM | **RTX 4050 Laptop (6 GB)**, i5-13420H, 32 GB | The 90 fps target is tight on this GPU (see §5) |
| VR runtime | Active OpenXR runtime: **Virtual Desktop (VDXR)**. Meta Horizon Link also installed | We target OpenXR directly, without going through SteamVR |
| Dev tools | .NET SDK 9 + 10, git, Python, Unity Hub (2018.4, 2021.3, 6000.3) | Need **Unity 2022.3.62f2** (exact match for AssetBundles) and **Blender** later |

---

## 2. Key technical decisions

1. **Mod loader: BepInEx 6 (IL2CPP)**, starting from the game's existing Thunderstore BepInExPack. Fallback: MelonLoader.
2. **VR backend: OpenXR.** It runs natively on VDXR and Meta Link, and on SteamVR for Index/Vive/WMR. It avoids the SteamVR layer, which matters for performance on a laptop GPU.
3. **XR injection: two candidate paths, spiked in M1.**
   - **Path A (preferred): Unity's own XR subsystem.** Ship Unity's native OpenXR plugin plus its `UnitySubsystemsManifest.json` into the game's data folder, then bring up the display and input subsystems from mod code. The engine then handles stereo rendering, pose prediction and frame pacing, which gives the best latency and performance.
   - **Path B (fallback): direct OpenXR.** Our own OpenXR session renders each eye camera into textures and submits them to OpenXR swapchains (D3D11). This approach bypasses Unity XR completely, and it's how [UnityVRMod](https://github.com/NewUnityModder/UnityVRMod) works. That project is GPL-3.0, so borrowing its code makes our mod GPL-3.0 as well.
   - The main risk is that IL2CPP code stripping removed XR methods the game never used. Path B doesn't depend on them.
4. **Stereo mode: multi-pass, almost certainly.** The game's shaders weren't compiled with single-pass-instanced variants, so each eye is rendered separately. That roughly doubles CPU draw submission and makes 90 fps harder. We plan for it from the start rather than hoping otherwise.
5. **Full-body IK: our own solver, written in the plugin.** Animation Rigging can't be loaded into an IL2CPP game, and FinalIK is a paid asset we can't redistribute. The body mesh and animation clips ship as an **AssetBundle** built with Unity 2022.3.62f2.
6. **Official artwork is never bundled.** The launcher and the in-game VR menu read the art, fonts and UI sprites from your installed game files at runtime. That guarantees genuine official artwork and keeps the mod download free of copyrighted assets.
7. **Launcher: WPF on .NET Framework 4.8** (ships with Windows 10/11), so it's a single ~280 KB `.exe` with nothing to install. Native Windows with GPU-accelerated animation; film grain, flicker and ink effects are procedural.

---

## 3. Architecture

```
InkVR/
├─ src/
│  ├─ InkVR.Plugin/            BepInEx 6 IL2CPP plugin (C#, net6.0)
│  │  ├─ Core/                   XR bootstrap (Path A/B), camera rig, frame timing, logging
│  │  ├─ Input/                  OpenXR actions/profiles, InControl virtual device, haptics
│  │  ├─ Locomotion/             smooth move, turning, sprint/jump/crouch, room-scale collision
│  │  ├─ Interaction/            grab system, levers/valves/doors/buttons, items, holsters
│  │  ├─ Combat/                 melee swing detection, ranged aiming, hit routing
│  │  ├─ Body/                   IK solver, procedural legs, calibration
│  │  ├─ UI/                     world-space canvas conversion, laser pointer, VR settings tab
│  │  ├─ Cinematics/             camera-takeover handling, comfort modes, fades
│  │  ├─ Rendering/              stereo fixes, head-locked effect fixes, perf telemetry
│  │  └─ Chapters/               per-chapter patches (Ch1–Ch5, Archives)
│  ├─ InkVR.Launcher/          WPF .NET 10 launcher + installer
│  └─ InkVR.Shared/            settings schema used by both plugin and launcher
├─ unity/InkVR.Assets/         Unity 2022.3.62f2 project → AssetBundles (body, hands, VR shaders)
├─ tools/                        build/deploy/run scripts, frame-time CSV analyzer
├─ docs/GAME_MAP.md              game classes and hooks discovered in M0
└─ PLAN.md
```

Runtime flow: **Launcher** (artwork, checks, closing animation) → launches through **Steam** with a VR-mode flag → **BepInEx** loads `InkVR.Plugin` → XR bootstrap → VR rig replaces the player camera → input, interaction, body, UI and cinematics systems patch the game through Harmony hooks found in M0.

Steam launch keeps DRM, achievements and cloud saves working. Save files are unchanged, so VR and flat saves stay interchangeable.

---

## 4. Milestones

Every milestone ends with a **"Done when"** checklist that you run in the headset. I build, install, launch and read logs on my side, but I can't wear the headset, so your test pass is the sign-off.

### M0 — Recon & toolchain *(risk: low)* ✅ done
- Back up the clean game (Steam "Verify integrity" also restores it).
- Install the BepInExPack, generate interop assemblies, and add UnityExplorer for live inspection.
- Decompile the interop assemblies and write `docs/GAME_MAP.md` covering:
  - the player controller and camera
  - the interaction raycast ("look + press")
  - weapons and the damage path
  - inventory and items
  - the Options/Pause menus
  - chapter loading and saves
  - the cutscene system
- Map `level0`–`level10` to the menu, Ch1–5 and the Archives.
- Asset inventory:
  - official art usable by the launcher (key art, logo, chapter cards)
  - Options menu widgets
  - confirm whether any player body or hand model exists (I expect none)
- Set up the repo, the solution skeleton and the dev loop (`tools/deploy.ps1` builds and copies the plugin, `tools/run.ps1` launches and tails the log).
- Record a **flat-screen baseline**: frame times at max settings in a heavy area of each chapter.
- **Done when:** the game runs normally with BepInEx, the plugin logs "InkVR loaded", and the game map is written.

### M1 — VR core: 6DoF head, stereo rendering, perf gate *(risk: HIGH)* ✅ headset-tested (0.2.0, 0.3.1)
> **Headset findings (VDXR, Quest 3):**
> - Unity already scales eye separation by the world scale, so InkVR no longer touches the eyes. (0.2.0 scaled them twice: tiny world, double vision.)
> - WORLD SIZE: AUTO sizes the world so your standing eye height is Henry's (5.03 units). It's adopted only as a level starts or when you resume, never mid-walk (0.4.0).
> - The game moves the player at 50 Hz, so InkVR interpolates the rig between physics steps, and poses the head and hands again right before rendering.
> - The volumetric light beams are re-projected per eye.
> - AUTO RESOLUTION settles around 70–85% in Chapter 1 to hold 90 Hz.
> - The body now stays grounded under head motion, so the game's landing sound no longer fires constantly; its own footsteps play as before (0.4.0).
> **Result so far (mock runtime, 1512×1680 per eye):** OpenXR injection works through Unity's own OpenXR provider (path A).
> - In Chapter 1, frame time is ~10.7–11.8 ms with VR tuning: no motion blur or DoF, half-res AO, FXAA.
> - Before tuning it was ~13.6 ms.
> - The game is GPU-bound: half resolution gives ~6.4 ms.
> - Per-effect cost: AO ≈ 3.1 ms full-res / ~1.5 ms half-res, bloom ≈ 1.2 ms, fog ≈ 0.5 ms.
> - World scale 2.95 units/m (eye height 5.03 units), with stereo eye separation scaled to match.

- XR bootstrap: spike Path A, and fall back to Path B if stripping blocks it.
- VR camera rig on the player:
  - the headset drives pitch and roll (mouse pitch disabled)
  - yaw is body yaw plus head yaw
  - room-scale position: the CharacterController follows your head, with push-back and a fade to black when your head goes into walls
- World scale calibrated to Henry's eye height.
- Merge any extra cameras (weapon or UI cameras) into a single stereo-correct setup.
- Desktop mirror window.
- Per-eye check of the game's custom post effects.
- Menus shown on a temporary floating screen (driven by gamepad or keyboard for now).
- Perf telemetry: in-headset overlay plus a CSV log of CPU and GPU frame time.
- **Feasibility gate:** measure VR frame time at max settings in the heaviest areas, and report real numbers before going further.
- **Done when:** you start Ch1 and walk around with a gamepad. Head tracking is correct in both eyes, scale feels right, and nothing swims or doubles.

### M2 — Controllers, locomotion, the game's own hands & arms on button presses, VR-usable menus *(risk: medium)* ✅ headset-tested (0.2.0, 0.3.1)
> Built: Quest Touch (+ Index / simple) actions feed an InControl virtual gamepad in the game's own layout; off-hand stick locomotion (stick-click or trigger to run); snap or smooth turning; hand-aimed interaction; a laser pointer for every menu; held items follow your hand. Hands and arms are made by InkVR in the game's ink-outlined style (Henry's rolled sleeves, wedding ring, wristwatch, ink creeping up chapter by chapter), as you asked on 2026-10-22. Not yet: physical crouch collider, seated mode, comfort vignette.
- OpenXR controller profiles: Quest Touch first, with Index, Vive and WMR mapped as well.
- InControl virtual device, so every existing game action (jump, interact, sprint, pause, menu navigation) works on controllers straight away.
- Locomotion:
  - smooth movement, head- or hand-relative
  - snap and smooth turn
  - sprint and jump
  - physical crouch (collider height follows your head)
  - seated mode and height calibration
  - comfort vignette
  - teleport assessed as an option where it can't skip chase triggers
- **Use the game's own first-person hands/arms/viewmodels** (no custom body yet): attach them to the tracked controllers, so held items and weapons follow your real hands; actions still fire on **button presses** (attack, use, interact) through the game's normal logic.
- Laser pointer for all existing menus (main, pause, options, chapter select).
- **Done when:** Ch1 is playable start to finish with controllers only; the game's hands/arms follow your controllers; interactions and attacks are button presses.

### M3 — Animated launcher + installer *(risk: low)* ✅ headset-tested
> Built as `InkVR Launcher.exe`. The game's art is read straight from `resources.assets` by InkVR's own reader and BC7 decoder (pixel-identical to UnityPy), then cached in `%LOCALAPPDATA%\InkVR\art`.
> **Opening:** a film-leader countdown, a "StrikeZoneVR presents" card, an ink-splat reveal of the game's menu, the logo dropping in and a "VR" rubber stamp. **Idle:** projector flicker, grain, scratches, ink drips, and Bendy bouncing in the menu ring.
> **Closing:** the menu clears away, Bendy fills with ink like the game's loader, and a Bendy-head-shaped iris closes to "Dreams come true" ("The End" when you quit).
> **Pages:** the Quest 3 controls sheet (painted controller art), VR CHECK, OPTIONS (the shared cfg, runtime choice and launcher preferences), and ABOUT.
> It installs from `InkVR-Payload/` and downloads Unity's OpenXR plugin, checking its SHA-256. The game is started directly (`steam_appid.txt`) with `INKVR_MODE` and `XR_RUNTIME_JSON` set for that launch only, so the PC-wide runtime never changes. `--play vr` skips the menu for shortcuts.
> Changed from the plan: no music sting from the game (the sounds are synthesised); no in-headset boot splash yet; the GPU check reports the adapter but doesn't change Windows' per-app GPU setting.
- **Reference (not to copy):** your Outlast 2 VR launcher — `Outlast 2\Binaries\Win64\Outlast2VR_Launcher.cs` + its README/art. Same idea: a single `.exe` that launches the game in VR, with opening and closing animations, here built from Bendy's official artwork.
- Official artwork extracted from your installed game at runtime (AssetsTools.NET) and cached locally. None of it is shipped.
- **Opening animation:** the projector starts up (flicker, film grain, sepia, frame jitter), ink drips reveal the key art, the logo stamps in, and an optional music sting plays from the game's audio.
- **Main screen:**
  - Play in VR / Play flat
  - VR Settings (same config as in game)
  - Install / Repair / Update / Uninstall
  - Diagnostics
- **Pre-flight checks:**
  - game found and build supported
  - BepInEx and the mod present and current
  - OpenXR runtime active (VDXR / Meta Link / SteamVR), with one-click switching
  - NVIDIA GPU assigned to the game (not the Intel iGPU)
  - laptop on AC power
- **Closing animation:** a 1930s-cartoon iris-out closing on Bendy, plus an ink-flood wipe, then the game launches through Steam. The same closing animation plays when you quit the launcher.
- A matching in-headset splash while the game boots.
- **Done when:** a double-click takes you to the in-headset game through the full opening → menu → closing sequence, and the closing animation plays every time.

### M4 — True motion controls: interactions & combat *(risk: medium-high)* 🟡 part 1 built (0.4.0)
> **Built in 0.3.1–0.4.0:**
> - **Hand hover:** hold either hand over, touch, or reach toward anything interactable. The game's own highlight and prompt appear, and grip or X uses it.
> - **Swing to hit (MELEE: SWING YOUR ARM):**
>   - Every frame, the held melee weapon's head is swept through the world.
>   - A swing faster than about 2.2 m/s that passes through something triggers the game's own hit there (damage, breaking, sounds). It counts once per swing, with a strong haptic pulse and the weapon's whoosh.
>   - Walking and turning never count, and tracking glitches are ignored.
>   - The game's own button swing still works and is never double-counted.
>   - Verified on the mock runtime: axe swings broke planks in Ch2 and Ch3.
> - **Hand-aimed guns (GUN AIM: HAND):** the game's hit check for guns uses the dominant controller's aim ray. Not yet verified: the Tommy Gun sits behind Ch3's weapon station, which a directly loaded test chapter can't open.
>
> **Still to build (0.6.0):** throwing (the Ch4 Bacon Soup cans are a `ThrowWeapon`), lever, valve and switch handlers, eating soup, two-handed gun, holster.
- Replace the camera-centre "look + press E" interaction with hand-based targeting: reach and grab, plus an optional hand-ray for distant objects.
- Physical handlers for each interaction type:
  - pickups and puzzle items (including placing items, like the Ch1 Ink Machine parts)
  - levers you pull
  - valves you turn
  - switches and buttons you press
  - doors
  - tape recorders
  - bacon soup (eat by bringing it to your mouth)
  - Little Miracle Stations
  - collectibles
- **Melee** (axe, pipes, plunger and the rest; exact list from M0): physical swings with velocity and contact detection, routed into the game's own damage code, with impact haptics and grip poses.
- **Ranged** (tommy gun): aim with the controller, two-handed stabilisation, recoil haptics. Ammo stays on the game's rules.
- Holsters on the body (back and hip) that still respect the game's weapon-switch logic.
- Haptics throughout. Point-and-press stays available as an accessibility option.
- **Done when:** Ch1–2 are playable with physical interactions only, and combat feels good.

### M5 — VR settings menu (game style) + full UI pass *(risk: medium)* 🟡 partly built
> Built: a VR SETTINGS page in the game's Options menu (main and pause), made from the game's own widgets, with "VR MOD MADE BY STRIKEZONEVR" at the bottom. The title, pause and loading screens play on the big screen of a full 1930s picture palace (0.4.0): stage, velvet drapes and footlights under a gilded proscenium with Bendy's face, organ grilles, about 480 velvet seats, sconce-lit walls with cartoon posters and chapter lobby cards, a balcony with a chasing-light marquee, the projection booth and its beam, a coffered ceiling and chandelier, and ink dripping down the walls. It's built and painted in code (no shipped art; the posters use the game's own images via the launcher), with the lighting baked in, so it costs about nothing to draw. Still to do: the rest of the full UI pass.
- Add a **VR tab to the game's own Options menu**, on both the main menu and the pause menu. It's built at runtime by cloning the game's real buttons, sliders, toggles, tab headers, fonts, sprites, DOTween transitions, menu sounds and I2 localisation, so it looks native.
- Settings:
  - **Comfort:** move direction, turn mode/angle/speed, vignette, cutscene comfort mode
  - **Controls:** dominant hand, sprint/crouch modes, seated mode, grip angle, interaction style
  - **Body:** show body, height and arm-span calibration, IK options
  - **Graphics & Performance:** render scale, dynamic resolution, AA, VR toggles for head-locked effects, perf overlay
  - **Interface:** wrist HUD, subtitle placement, prompt style
- Settings apply live and save to `BepInEx/config/InkVR.cfg`, shared with the launcher.
- Full UI pass, with every screen placed in world space and readable:
  - main menu and pause menu (pause summons in front of you)
  - subtitles (lazy head-follow) and audio-log captions
  - interaction prompts on objects
  - note and collectible viewer
  - death and loading screens, chapter cards, credits
  - **VR controller button glyphs** instead of keyboard/gamepad glyphs
- **Done when:** every screen is usable in the headset, and side by side the VR tab is indistinguishable in style from the game's own tabs.

### M6 — Cutscenes, scripted sequences, effects, comfort *(risk: medium)*
- Inventory every camera takeover in every chapter:
  - Timeline directors
  - animated cameras and scripted lerps
  - knockouts and falls
  - grabs, deaths and respawns
- VR policy for takeovers:
  - the game drives your position, but **your head rotation always stays yours**
  - the intended look direction becomes a one-time yaw alignment
  - shakes and roll are damped
  - cuts and teleports fade through black
  - optional **Cinema mode** plays the sequence on a large virtual screen
- Head-locked effects (ink on screen, damage, vignettes, fades) become stereo- and depth-correct versions. Sepia and film grain stay.
- Any video sequences play on a virtual screen.
- Head tracking stays smooth during loads.
- Jumpscares are preserved, and look-direction triggers still fire.
- **Done when:** every scripted sequence in every chapter plays comfortably and nothing softlocks.

### M7 — Full-body IK *(built in 0.5.0: moved up at your request on 2026-10-23)*
> **Built:**
> - A skinned ten-bone body (pelvis, spine, chest, neck, thigh/shin/foot per leg) posed from your head and hands.
> - The feet plant on the ground with short raycasts (stairs, ramps) and step one at a time when you move or turn.
> - Crouching bends the knees.
> - The body is sized to your eye height.
> - Two styles: LIFELIKE (waistcoat, tie, pinstripes, boots, with new skinned lifelike hands) and TOON (braces, baggy trousers, big cartoon shoes, with the original hands).
> - Settings: BODY (full body / arms & hands / hands only / none) and STYLE (lifelike / toon) in VR SETTINGS and the launcher.
> - The game's build stripped Mesh.boneWeights; InkVR sets the weights through the engine call behind Mesh.SetBoneWeights.
>
> **Not yet:** ink on the body, a body shadow, arm-length calibration.
- **Two body models for you to choose from.** Both are Henry, rigged to the same skeleton, so the IK, grips and ink stains work on either:
  - **A. "Toon Henry":** the same style as the current hands and arms. Chunky ink-outlined forms, flat sepia shading, rolled sleeves and suspenders, with the watch and ring carried over.
  - **B. "Studio Henry":** more realistic proportions and folds (shirt, vest, trousers, work boots), still drawn with the game's ink outlines, hatching and sepia palette so it belongs in the studio.
  - You'll get turntable images and an in-headset test of both. Keep one, or keep both as a BODY STYLE option.
- **BODY option in VR SETTINGS and the launcher:** FULL BODY / ARMS & HANDS / HANDS ONLY. The hands-only and arms-and-hands modes keep working exactly as they do now.
- Built as an AssetBundle with 2022.3.62f2 and shaded with the game's own shaders loaded at runtime, so it sits in the ink and sepia look.
- **Custom IK solver** driven by head and hands:
  - pelvis and spine estimation
  - two-bone arms with elbow hints
  - legs blended from locomotion-speed animation and procedural stepping
  - foot planting with ground raycasts on stairs and ramps
  - crouch and kneel, jump and fall, turn-in-place foot shuffle, seated pose
- **First person:**
  - your body is visible when you look down
  - the head is hidden from your view but still casts a shadow, with no visible neck stump
  - the body's own hands replace the placeholder hands
  - weapons and items attach to the hand bones with grip poses
- **Calibration:** automatic from height, plus a manual stand-straight/T-pose step in the VR menu.
- Debug third-person/mirror camera for checking the result.
- *Stretch:* Quest body tracking (`XR_FB_body_tracking`) or SteamVR trackers, if your runtime supports them.
- **Done when:** the body follows you naturally while walking, sprinting, crouching, turning, jumping and swinging, with no major clipping in normal play.

### M8 — Performance pass: 90 fps at max settings *(risk: HIGH on this GPU)*
- Profile the heavy spots in every chapter using the M1 telemetry.
- Fix per-scene hotspots:
  - effects being paid twice (once per eye)
  - shadow cost
  - light and LOD culling
  - camera far-clip
- Test the `boot.config` graphics-jobs options.
- Make per-frame mod code allocation-free (no GC spikes) and pre-warm shaders so first use doesn't hitch.
- Dynamic resolution as the safety net, so render resolution drops in hotspots while game settings stay at max.
- Tune the VD / Link encoder for latency and quality.
- **Done when:** CSV logs show ≤ 11.1 ms frames (90 fps) through full runs of every chapter on your laptop, reported per chapter.

### M9 — All-chapter certification + release *(risk: medium)*
- A full VR playthrough of Ch1–5 and the Archives, against per-chapter checklists:
  - softlocks
  - unreachable items
  - head clipping past triggers
  - enemy AI against room-scale movement
  - boss fights
- VR and flat saves are interchangeable, and achievements still fire.
- Packaging: install through the launcher, README, and an uninstall that restores the vanilla game.
- **Done when:** you've finished every chapter in VR with no workarounds.

---

## 5. Performance target, honestly

- **Budget:** 11.1 ms CPU and 11.1 ms GPU per frame at 90 Hz.
- **"Max graphics settings"** means every in-game graphics option at its highest, at the VR runtime's *recommended* render resolution.
- **The hard part:** multi-pass stereo on an RTX 4050 Laptop (6 GB) at Quest-class resolution. The game's scenes are fairly light, which helps, but drawing everything twice isn't free.
- **When we'll know:** **M1 gives real numbers early.** If 90 doesn't hold at full recommended resolution everywhere, the plan keeps settings at max and uses dynamic resolution in hotspots. I'll report the measured numbers rather than claim 90 we don't hit.
- **Laptop-specific requirements:** plugged in, Windows/NVIDIA "high performance" for the game exe, and the laptop's performance power mode. The launcher checks these.

---

## 6. Chapter coverage (expanded with specifics in M0)

| Chapter | VR-sensitive content to handle |
|---|---|
| Ch1 – Moving Pictures | item collection and Ink Machine restore, cutout jumpscares, axe, ending run and floor collapse (forced camera) |
| Ch2 – The Old Song | Searchers combat, Sammy sequences (knockout and forced camera), Boris companion, chase sequence |
| Ch3 – Rise and Fall | Alice Angel tasks, Little Miracle Stations (hiding), new weapons, Butcher Gang enemies |
| Ch4 – Colossal Wonders | boss fights (Bertrum, Brute Boris), large set-piece areas |
| Ch5 – The Last Reel | finale scripted sequences, final encounter |
| The Archives | exploration and audio. Verify it's present in this build |

---

## 7. Risks & mitigations

| Risk | Mitigation |
|---|---|
| IL2CPP stripped XR APIs, so Path A fails | Path B (direct OpenXR), decided in M1 |
| 90 fps at max on the RTX 4050 Laptop | Measured in M1, gated in every milestone, dedicated M8 pass, dynamic resolution safety net |
| A game update breaks hooks | Pin build `a8806fc9…`, launcher detects build changes, keep hooks behind a version check |
| Game folder inside OneDrive (sync locks, file churn) | Exclude the Steam folder from OneDrive or move the library (Steam → Storage → Move) |
| Body art quality | Decision 3 below. The IK system is model-agnostic, so the model can be swapped later |
| Room-scale exploits (head through walls, skipping triggers) | Collider follows the head, plus push-back and fade-to-black |
| Motion sickness from scripted cameras | M6 policy plus Cinema mode |
| I can't test in the headset | Each milestone ends with a short headset checklist for you, plus logs and telemetry I can read |

---

## 8. Decisions (answered 2026-10-22)

1. **Headset:** Quest 3 over Virtual Desktop, VDXR OpenXR runtime.
2. **Launcher:** a standalone `.exe` that launches the game in VR, animated with official game artwork, opening + closing animations. Reference: the Outlast 2 VR launcher (for ideas, not copied).
3. **Body:** InkVR's own Henry hands and arms for now. Full-body IK is M7, in the 1.0 full release (see 9).
4. **Release:** open source on GitHub. Licence to pick before release (GPL-3.0 if any UnityVRMod code is borrowed).
5. **Order & scope:** M0 → M1 → M2 → M3 launcher → M4 motion controls → M5 VR settings → M6 cutscenes. Authorized through M6.
6. **Name:** InkVR, by StrikeZoneVR. Project folder: `InkVR`.
7. **Hands & arms:** made in the game's style and true to the lore (they're Henry's), not just the flat game's viewmodel.
8. **Test gate:** your first headset test comes once the launcher, settings, hands/arms, Quest button controls, theatre menu and pause, and the in-game VR settings (with the StrikeZoneVR credit) are all done. Motion controls (M4) follow that test.
9. **Full body (2026-10-22, moved up 2026-10-23):** two models, both selectable in options: one in the hands' style (TOON), one more realistic but still in the game's style (LIFELIKE, which also gets real-looking hands). Plus BODY options for arms & hands and for hands only. Built in 0.5.0 instead of waiting for the full release.
10. **Sounds:** the game's native footsteps stay. InkVR must not add or trigger extra movement sounds. (Fixed in 0.4.0: the landing sound no longer repeats while you walk.)
11. **Interaction foundation:** hand hover plus the mapped button (grip or X) is the base every later motion control builds on.
