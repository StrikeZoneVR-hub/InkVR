# InkVR — Full 6DoF VR Mod for Bendy and the Ink Machine

Status: **Test build 0.2.0 ready for your headset test** (M1 VR core · M2 button controls + Henry's hands · M3 launcher · M5 VR SETTINGS page + theatre menus) · next after your test: M4 motion controls · Go-ahead given for M0 → M6 (through cutscenes) on 2026-10-22

Target setup: **Quest 3 · Virtual Desktop · VDXR OpenXR runtime** · RTX 4050 Laptop. Release: **open source on GitHub**.

Goals (all required):
- Full 6DoF (room-scale) VR with full motion controls
- Full-body IK
- Animated VR launcher using official artwork, with a closing animation
- A VR settings menu inside the game's own Options menu, in the game's own style
- 90 fps at max graphics settings
- Every chapter playable from start to finish

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

### M1 — VR core: 6DoF head, stereo rendering, perf gate *(risk: HIGH)* 🟡 built, awaiting headset test
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

### M2 — Controllers, locomotion, the game's own hands & arms on button presses, VR-usable menus *(risk: medium)* 🟡 built, awaiting headset test
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

### M3 — Animated launcher + installer *(risk: low)* 🟡 built, awaiting your test
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

### M4 — True motion controls: interactions & combat *(risk: medium-high)*
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
> Built: a VR SETTINGS page in the game's Options menu (main and pause), made from the game's own widgets, with "VR MOD MADE BY STRIKEZONEVR" at the bottom. The title, pause and loading screens play on a projector screen in a small studio theatre. Still to do: the rest of the full UI pass.
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

### M7 — Full-body IK *(deferred — after M6, not yet authorized; M2–M6 use the game's own hands/arms)*
- **Body model:** a Henry-style rigged body (see Decision 3), built as an AssetBundle with 2022.3.62f2 and shaded with the game's own shaders loaded at runtime, so it sits in the ink and sepia look.
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
3. **Body:** use the game's own hands/arms for now. Full-body IK moves to M7 (later).
4. **Release:** open source on GitHub. Licence to pick before release (GPL-3.0 if any UnityVRMod code is borrowed).
5. **Order & scope:** M0 → M1 → M2 → M3 launcher → M4 motion controls → M5 VR settings → M6 cutscenes. Authorized through M6.
6. **Name:** InkVR, by StrikeZoneVR. Project folder: `InkVR`.
7. **Hands & arms:** made in the game's style and true to the lore (they're Henry's), not just the flat game's viewmodel.
8. **Test gate:** your first headset test comes once the launcher, settings, hands/arms, Quest button controls, theatre menu and pause, and the in-game VR settings (with the StrikeZoneVR credit) are all done. Motion controls (M4) follow that test.
