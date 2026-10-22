# Game map — Bendy and the Ink Machine 2.1.0 (Steam build 23764460)

Unity **2022.3.62f2**, IL2CPP (metadata v31.1), **Direct3D 11**, built-in render pipeline, **deferred shading**.
Modding: BepInEx **6.0.0-be.790** IL2CPP (CoreCLR .NET 6). `steam_appid.txt` (622650) is needed to launch the exe directly.

Interop assemblies: `BepInEx/interop`. Decompiled signatures: `analysis/acs` (not committed). Asset index: `analysis/asset_index.tsv` (`tools/asset_index.py`).

## Scenes
| Build index | File | Scene | Content |
|---|---|---|---|
| 0 | level0 | InitializeGame | boot |
| — | level9/10 | Empty | title screen host (UI lives in DontDestroyOnLoad `[UI MANAGER]`) |
| 1 | level1 | CH1 | Moving Pictures |
| 2 | level2 | CH2 | The Old Song |
| 3 | level3 | CH3 | Rise and Fall |
| 4 | level4 | CH4 | Colossal Wonders |
| 5 | level5 | CH5 | The Last Reel |
| 6 | level6 | credits / post-credits | |
| 7 | level7 | Archives | |
| 8 | level8 | (PlayerController test scene) | |

## Core singletons
- `GameManager.Instance` → `GameCamera`, `Player` (PlayerController), `CurrentChapter`, `UIManager`, `GameData`, `PlayerSettings`, `isPaused`, `ShowScreenBlocker/Hide…` (fades), `ShowSubtitles`, `Pause/Unpause`, `LoadScene`.
- `CharacterManager` → `Bendy`, `Boris`, `BruteBoris`, `Player`.

## Player rig (CH1, spawned as `PlayerController(Clone)`)
```
PlayerController(Clone)  [CharacterController, PlayerController]   layer 8
  HeadContainer (0, 2.15, 0)
    CameraContainer
      GameCamera_PostProcess  [Camera depth 0, fov 64, near .3 far 215, deferred, HDR, mask ~(L10|L17)]
                              [GameCamera, PostProcessingBehaviour (PPv1), AmplifyPostProcess(off), VolumetricLightRenderer,
                               VisionEffectController, SimpleCull, CameraDepthOfFieldDistanceSetter, FlareLayer, AudioListener]
        WeaponCam             [Camera depth 1, layer 10 only, far 20, clear depth, PPv1, BrightnessImageEffect]
        InvisibleCam          [Camera depth -100, layer 17 only]
      InkExplosionEffect, Dust_ParticleField (PAParticleField), Secret_ParticleField
    Forward Camera Target (0,0,1)
  HandContainer (0, 2.15, 0)
    Hand (0.75,-1.25,1.25) [Animator]   ← invisible mount point, no mesh
      WeaponAnimator                     ← held weapons/items parent here
  FeetContainer (0,-2.15,0)  footstep audio
```
- **Eye height ≈ 4.3 units above the feet** → VR world scale ≈ 2.6 (1 m ≈ 2.6 units).
- **No first-person hand/arm meshes exist in the game.** Weapons (e.g. `Axe` = MeshFilter+MeshRenderer only) float in front of the camera.
- `PlayerController`: `m_PlayerLook` (CharacterLook: yaw on body, pitch on camera), `m_CameraMovement` (sway), `m_CameraFOV`, `m_HeadBob`, `m_Interaction` (InteractableInputController), `SetLock/SetLockedMovement/LockRotation`, `GoToAndLookAt`, `LookRotation`, `EquipWeapon`, `Die`.
- UI camera: `[UI MANAGER]/UIVisualControls/UICamera` (depth 99, layer 5, forward). Canvases under `[UI Layer] - …` objects.

## Input
- Game code reads `TMG.Controls.PlayerInput` statics: `Any, Attack, AttackHold, Run, RunDown, Jump, Pause, MoveX, MoveY, LookX, LookY, BackOnPressed, InteractOnPressed, InteractOnReleased` (+ InControl via `GamepadInput`).
- Hooked by `Input/VirtualInput.cs` (Harmony postfixes) — confirmed working (skipped the CH1 intro letter).
- Menus: `BaseUIButton` (IPointer* handlers), `MenuItemButton`; `CustomInputModule : StandaloneInputModule` has a virtual cursor (`m_cursorPos`).

## Interaction / combat
- `InteractableInputController.UpdateInteraction(origin, direction[, distance])` → sphere-cast; `Interact()`.
- `Interactable` (highlight/shimmer, `OnInteract`), `Holdable`, `InteractablePowerLever`, `InteractableSwitchBox`, `CH1PipeValve`, doors (`BaseDoorController`…).
- `BaseWeapon` (`Attack`, `CheckHitRaycast`, `OnRaycastHit`, `HandleSwingHit`), `MeleeWeapon` (DOTween swing anims), `GunWeapon`, `ThrowWeapon`, `BaconSoupWeapon`.

## Rendering effects (VR attention list)
PPv1 (`UnityEngine.PostProcessing` compiled into Assembly-CSharp): bloom, DoF, AO, grain, motion blur, AA, fog.
`VolumetricLightRenderer`, `AmplifyPostProcess` (off), `VisionEffectController`, `BrightnessImageEffect`, `HurtBordersController` (UI), `GlobalInkEffectManager`, `DynamicDecals`, `ScreenBlockerController` (fades).

## Settings (registry `HKCU\Software\Joey Drew Studios\Bendy and the Ink Machine`)
QUALITY 3, all effects on (bloom, DoF, volumetric, AO, grain, motion blur, AA, fog, shadows, dust), 1920×1080.

## Baseline (flat, 1080p, all max, CH1 start corridor)
~7.0 ms/frame (143 fps), vsync-limited at 144 Hz.
