using Action = System.Action;
using Exception = System.Exception;
using Il2CppInterop.Runtime;
using InkVR.Body;
using InkVR.Input;
using InkVR.UI;
using UnityEngine;
using UnityEngine.XR;

namespace InkVR.Core;

/// <summary>
/// Owns the VR camera setup.
///
/// Cinema (title screen, pause, loading): a mod-owned theatre far below the level; the game's UI
/// plays on its big screen. While paused mid-game the game cameras render to a tiny texture instead
/// of the headset.
///
/// Gameplay: the game's own camera moves under a head-tracked, world-scaled origin on the player.
///   PlayerController (yaw = your head yaw, so movement is head-relative)
///     InkVR_Origin   at the capsule's feet, scale = WorldScale (game units per metre)
///       GameCamera_PostProcess (+WeaponCam, InvisibleCam)
///       InkVR_UICamera   (layer 31: HUD panel, laser, fade)
///       InkVR_UIPanel    (subtitles, prompts, objectives, notes)
/// </summary>
public static partial class VRRig
{
    private const float GamePanelDistance = 1.6f, GamePanelWidth = 1.5f;
    private const float PanelFollowAngle = 35f;

    // Cinema rig (persistent).
    private static Transform _cinemaOrigin;
    private static Camera _cinemaCam;
    private static Cinema _cinema;
    private static bool _cinemaActive;

    // Player rig (lives on the current PlayerController, destroyed with it).
    private static PlayerController _player;
    private static Transform _origin;
    private static Camera _gameCam;
    private static Camera _uiCam;
    private static VRPanel _gamePanel;
    private static CharacterController _cc;
    private static float _panelYaw;
    private static RenderTexture _parkedTarget;

    private static VRPanel _fade;

    public static bool InGame => _player != null && _origin != null;
    public static bool CinemaActive => _cinemaActive;
    public static Camera GameCamera => InGame ? _gameCam : null;
    public static Transform Head => _cinemaActive ? _cinemaCam?.transform : InGame ? _gameCam.transform : null;
    public static Transform ActiveOrigin => _cinemaActive || !InGame ? _cinemaOrigin : _origin;
    /// <summary>
    /// Game units per real metre right now. It only changes when a level starts or when you come back from
    /// the pause theatre, never while you're playing (a size change mid-walk feels like shrinking).
    /// </summary>
    public static float Scale => _currentScale > 0 ? _currentScale : TargetScale;

    /// <summary>AUTO: Henry's eye height over yours; otherwise the WORLD SIZE setting.</summary>
    public static float TargetScale => ModConfig.AutoWorldScale.Value && AutoScale > 0 ? AutoScale : ModConfig.WorldScale.Value;

    public static float AutoScale
    {
        get
        {
            var h = ModConfig.SeatedMode.Value ? SeatedTargetEye : _measuredHeight > 0 ? _measuredHeight : ModConfig.MeasuredEyeHeight.Value;
            return h > 0.8f ? Mathf.Clamp(_henryEye / h, MinAutoScale, MaxAutoScale) : -1f;
        }
    }

    private const float MinAutoScale = 2.4f, MaxAutoScale = 4.6f;
    private static float _currentScale = -1, _measuredHeight = -1, _henryEye = 5.03f;

    /// <summary>Your standing eye height in metres (measured, else last saved, else average), plus HEIGHT ADJUST.</summary>
    public static float StandingEyeHeight =>
        (ModConfig.SeatedMode.Value ? SeatedTargetEye : _measuredHeight > 0 ? _measuredHeight : ModConfig.MeasuredEyeHeight.Value > 0.8f ? ModConfig.MeasuredEyeHeight.Value : 1.62f)
        + ModConfig.HeightOffset.Value;

    /// <summary>
    /// Metres added to your tracked height: HEIGHT ADJUST, plus in SEATED mode the lift from your seated eye height
    /// up to standing height. Everything that turns tracking into the game world goes through this.
    /// </summary>
    public static float HeightLift => ModConfig.HeightOffset.Value + SeatedLift;

    /// <summary>SEATED mode: how far your view is lifted (0 when standing).</summary>
    public static float SeatedLift { get; private set; }

    /// <summary>SEATED mode lifts you to this standing eye height: yours as last measured standing, unless that looks like it was measured sitting down.</summary>
    public static float SeatedTargetEye => ModConfig.MeasuredEyeHeight.Value >= 1.35f ? ModConfig.MeasuredEyeHeight.Value : 1.62f;

    private static readonly System.Collections.Generic.List<float> SeatedHeights = new();
    private static bool _seatedSet, _seatedEasing;
    private static readonly System.Collections.Generic.List<float> Heights = new();
    private static float _nextHeightSample, _lastHeightSave;

    /// <summary>Take the latest size (AUTO height or WORLD SIZE): at level start and on resuming.</summary>
    private static void AdoptScale()
    {
        var before = _currentScale;
        _currentScale = TargetScale;
        if (before > 0 && Mathf.Abs(before - _currentScale) > 0.01f)
            Plugin.L.LogInfo($"World size now {_currentScale:F2} units per metre ({2.95f / _currentScale:P0})");
    }
    public static float ActiveScale => _cinemaActive || !InGame ? 1f : Scale;

    /// <summary>Smooth-turn input (-1..1) from the controllers this frame.</summary>
    public static float SmoothTurnInput;

    /// <summary>A menu or modal is up and wants pointer input (laser), rather than gameplay.</summary>
    public static bool MenuWantsPointer => _cinemaActive || (InGame && Cursor.lockState != CursorLockMode.Locked);

    /// <summary>The panel currently showing the game's UI that the laser can click.</summary>
    public static VRPanel PointerPanel => _cinemaActive ? _cinema?.Screen : MenuWantsPointer ? _gamePanel : null;

    /// <summary>Dev/testing: replaces the tracked head pose (metres, tracking space).</summary>
    public static (Vector3 pos, Quaternion rot)? DevHeadOverride;

    public static void Update()
    {
        // Game logic only ever sees real positions: undo last frame's between-physics-steps offset.
        MotionSmoothing.Remove();

        if (!XrManager.IsRunning)
        {
            if (_cinemaOrigin != null) _cinemaOrigin.gameObject.SetActive(false);
            HurtVisor.Clear();
            return;
        }

        UICapture.Update();

        var player = GameManager.Instance?.Player;
        if (player != null && player != _player) AttachToPlayer(player);
        if (player == null && _player != null) DetachFromPlayer();

        EnsureCinema();
        SetCinema(!InGame || IsPaused());
        HookPreCull();
        if (InGame && Time.unscaledTime < DevTiltUntil)
        {
            // dev: roll and pitch the player the way a scripted knockout does (game-side, before InkVR's update)
            var k = Mathf.Sin((DevTiltUntil - Time.unscaledTime) * 2.1f);
            var yaw = _player.transform.eulerAngles.y;
            _player.transform.rotation = Quaternion.Euler(DevTiltRoll * 0.4f * k, yaw + 20f * Time.unscaledDeltaTime, DevTiltRoll * k);
        }

        if (DevCamWatch > 0 && InGame) CamWatch("U");
        if (InGame && !_cinemaActive) NoteCameraWrite();
        if (InGame) KeepCameraInRig();
        ApplyHeadPose();
    }

    public static void LateUpdate()
    {
        if (!XrManager.IsRunning) return;
        if (DevCamWatch > 0 && InGame) CamWatch("L");
        if (InGame && !_cinemaActive) NoteCameraWrite();
        if (InGame) KeepCameraInRig();
        ApplyHeadPose();

        MeasureHeight();

        if (InGame && !_cinemaActive && Mathf.Abs(_origin.localScale.x - Scale) > 1e-4f)
        {
            // WORLD SIZE changed in the VR menu: rescale the play space around the feet.
            _origin.localScale = Vector3.one * Scale;
            _uiCam.nearClipPlane = 0.02f * Scale;
            _uiCam.farClipPlane = 20f * Scale;
        }

        if (InGame && !_cinemaActive)
        {
            ReAnchor();
            UpdateCutscene();
            ApplySmoothTurn();
            FollowHeadWithBody();
            StoreAnchor();
            if (CutsceneActive) MotionSmoothing.Remove();
            else MotionSmoothing.Apply(_origin);
            PlaceGamePanel();
            HurtVisor.Update(_uiCam);
            Rendering.VRGraphics.Update(_gameCam);
        }

        _cinema?.Animate();
        UpdateFade();
        MeleeSwing.DevPreUpdate();
        Hands.Update();
        if (InGame && !_cinemaActive) MeleeSwing.Update();
        WeaponHandling.Update();
        UI.WeaponWheel.Update();
        UI.InventoryCard.Update();
        Input.PhysicalInteract.Update();
        Input.GravityPull.Update();
        Input.PhysicalDoors.Update();
        Input.PhysicalTouch.Update();
        InkWorld.Update();
        Footprints.Update();
        LaserPointer.Update();
        if (InGame && !_cinemaActive) ScaleStereo();
        if (DevLookAt.HasValue && InGame && !_cinemaActive)
        {
            var headWorld = _origin.TransformPoint(new Vector3(0, 1.7f, 0));
            var dirLocal = _origin.InverseTransformDirection(DevLookAt.Value - headWorld);
            DevHeadOverride = (new Vector3(0, 1.7f, 0), Quaternion.LookRotation(dirLocal.normalized, Vector3.up));
        }

        if (DevPendingTouch.HasValue && InGame && !_cinemaActive)
        {
            // Grip pose so the palm/fingers reach the point: grip half a hand-length back from it.
            var local = _origin.InverseTransformPoint(DevPendingTouch.Value) - Vector3.up * HeightLift;
            var head = HeadLocalPosition;
            var dir = (local - head).normalized;
            Body.Hands.DevRight = (local - dir * 0.06f, Quaternion.LookRotation(Vector3.ProjectOnPlane(dir, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.ProjectOnPlane(dir, Vector3.up) : Vector3.forward) * Quaternion.Euler(60, 0, 0));
        }

        if (DevGroundCount > 0 && _cc != null)
        {
            DevGroundCount--;
            if (!_cc.isGrounded) DevUngrounded++;
            if (DevGroundCount == 0) Plugin.L.LogInfo($"GROUND not grounded in {DevUngrounded} of the sampled frames");
        }

        if (DevTraceFrames > 0 && _player != null)
        {
            DevTraceFrames--;
            var p = _player.transform.position;
            Plugin.L.LogInfo($"TRACE f={Time.frameCount} player=({p.x:F3},{p.y:F4},{p.z:F3}) rot={_player.transform.eulerAngles} originRot={_origin.eulerAngles} camRot={_gameCam.transform.eulerAngles} camLocal={_gameCam.transform.localEulerAngles} locked={_player.isLocked} moveLocked={_player.isMoveLocked} originLocalY={_origin.localPosition.y:F4} grounded={_cc?.isGrounded}");
        }

        if (DevStereoReport)
        {
            DevStereoReport = false;
            StereoReport();
        }
    }

    /// <summary>Dev: keep the head (1.7 m) looking at this world point.</summary>
    public static Vector3? DevLookAt;

    /// <summary>Dev: a world point the right hand should be pressed onto (set by "inktouch").</summary>
    public static Vector3? DevPendingTouch;

    /// <summary>Dev: count frames where the character isn't grounded (after InkVR's moves).</summary>
    public static int DevGroundCount, DevUngrounded;

    /// <summary>Dev: log player/camera positions for this many frames.</summary>
    public static int DevTraceFrames;

    /// <summary>Dev: until this time, roll the player object by up to this many degrees each frame (a scripted knockout's tumble).</summary>
    public static float DevTiltUntil, DevTiltRoll;

    /// <summary>Dev: frames left to watch what the game does to its camera (every 3rd frame, before InkVR puts it back).</summary>
    public static int DevCamWatch;

    private static void CamWatch(string phase)
    {
        if (phase == "L") DevCamWatch--;
        if (Time.frameCount % 3 != 0) return;
        string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
        string E(Quaternion q) { var e = q.eulerAngles; return $"({e.x:F0},{e.y:F0},{e.z:F0})"; }
        var t = _gameCam.transform;
        var p = _player.transform;
        var gc = GameManager.Instance?.GameCamera;
        var hc = gc?.HeadContainer;
        var cc = gc?.CameraContainer;
        var parent = t.parent;
        var path = parent == null ? "none" : (parent.parent != null ? parent.parent.name + "/" : "") + parent.name;
        Plugin.L.LogInfo($"CAMWATCH {phase} f={Time.frameCount} locked={_player.isLocked}/{_player.isMoveLocked} parent={path} camW={V(t.position - p.position)} camRot={E(t.rotation)} camLocal={V(t.localPosition)} {E(t.localRotation)}"
                         + $" | player={V(p.position)} {E(p.rotation)} | head={(hc != null ? V(hc.localPosition) + E(hc.localRotation) : "-")} camC={(cc != null ? V(cc.localPosition) + E(cc.localRotation) : "-")} ccW={(cc != null ? V(cc.position - p.position) + E(cc.rotation) : "-")}");
    }

    /// <summary>Dev: log the eye poses Unity renders with, before and after InkVR's world scaling.</summary>
    public static bool DevStereoReport;

    private static void StereoReport()
    {
        var cam = _cinemaActive || !InGame ? _cinemaCam : _gameCam;
        if (cam == null) return;
        Vector3 Pos(Matrix4x4 view) => view.inverse.GetColumn(3);
        var sl = Pos(cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left));
        var sr = Pos(cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right));
        cam.ResetStereoViewMatrices();
        var dl = Pos(cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left));
        var dr = Pos(cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right));
        if (InGame && !_cinemaActive) StereoScale.Apply(cam, Scale);
        var c = cam.transform.position;
        var trackL = InputTracking.GetLocalPosition(XRNode.LeftEye);
        var trackR = InputTracking.GetLocalPosition(XRNode.RightEye);
        var trackC = InputTracking.GetLocalPosition(XRNode.CenterEye);
        string V(Vector3 v) => $"({v.x:F4}, {v.y:F4}, {v.z:F4})";
        Plugin.L.LogInfo($"STEREO cam={cam.name} pos={V(c)} lossyScale={cam.transform.lossyScale.x:F2} scale={ActiveScale:F2}");
        Plugin.L.LogInfo($"STEREO unity default: sep={Vector3.Distance(dl, dr):F4} mid-cam={V((dl + dr) / 2 - c)} | left-cam={V(dl - c)}");
        Plugin.L.LogInfo($"STEREO after InkVR:   sep={Vector3.Distance(sl, sr):F4} mid-cam={V((sl + sr) / 2 - c)}");
        var pl = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
        var pr = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);
        Plugin.L.LogInfo($"STEREO proj L m00={pl.m00:F4} m02={pl.m02:F4} m11={pl.m11:F4} m12={pl.m12:F4} | R m00={pr.m00:F4} m02={pr.m02:F4} | eyeTex={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} fov={cam.fieldOfView:F1} customProj={!cam.usePhysicalProperties}");
        Plugin.L.LogInfo($"STEREO tracking (m):  IPD={Vector3.Distance(trackL, trackR):F4} centre={V(trackC)} eyeMid-centre={V((trackL + trackR) / 2 - trackC)}");
    }

    private static bool IsPaused()
    {
        var gm = GameManager.Instance;
        return gm != null && gm.isPaused;
    }

    // ---------------- tracking ----------------

    public static Vector3 HeadLocalPosition =>
        (DevHeadOverride?.pos ?? InputTracking.GetLocalPosition(XRNode.CenterEye)) + DevSway + Vector3.up * HeightLift;

    /// <summary>Dev: simulate a real head's constant small movement (metres of sway; 0 = off).</summary>
    public static float DevSwayAmount;

    private static Vector3 DevSway => DevSwayAmount <= 0 ? Vector3.zero
        : new Vector3(Mathf.Sin(Time.time * 2.3f), 0.3f * Mathf.Sin(Time.time * 3.1f), Mathf.Sin(Time.time * 1.7f)) * DevSwayAmount;

    public static Quaternion HeadLocalRotation => DevHeadOverride?.rot ?? InputTracking.GetLocalRotation(XRNode.CenterEye);

    /// <summary>
    /// XR SDK cameras are not tracked implicitly (that is TrackedPoseDriver's job), so copy the
    /// centre-eye pose onto the active rig cameras. Runs in Update (for game logic) and again in
    /// LateUpdate (Application.onBeforeRender is stripped from this build).
    /// </summary>
    public static void ApplyHeadPose()
    {
        if (!XrManager.IsRunning) return;
        var pos = HeadLocalPosition;
        var rot = HeadLocalRotation;
        if (_cinemaActive || !InGame)
        {
            if (_cinemaCam != null) _cinemaCam.transform.SetLocalPositionAndRotation(pos, rot);
        }
        else
        {
            _gameCam.transform.SetLocalPositionAndRotation(pos, rot);
            _uiCam.transform.SetLocalPositionAndRotation(pos, rot);
            NoteApplied(pos, rot);
        }
    }

    // ---------------- late pose ----------------

    private static Camera.CameraCallback _preCull;
    private static int _latePoseFrame = -1;
    private static bool _latePoseSeen;

    /// <summary>
    /// Right before the first head camera is culled (after Unity's before-render tracking update), pose the
    /// head and hands again with the newest tracking, so what is drawn matches the pose sent to the headset.
    /// LateUpdate's pose stays as the fallback if this callback never arrives.
    /// </summary>
    private static void HookPreCull()
    {
        if (_preCull != null) return;
        try
        {
            _preCull = DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new System.Action<Camera>(OnPreCull));
            var existing = Camera.onPreCull;
            Camera.onPreCull = existing == null ? _preCull : Il2CppSystem.Delegate.Combine(existing, _preCull).Cast<Camera.CameraCallback>();
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("Late head pose unavailable (Camera.onPreCull): " + e.Message);
        }
    }

    private static void OnPreCull(Camera cam)
    {
        if (cam == null || Time.frameCount == _latePoseFrame || !XrManager.IsRunning) return;
        var ours = _cinemaActive || !InGame ? cam == _cinemaCam : _origin != null && cam.transform.IsChildOf(_origin);
        if (!ours) return;
        _latePoseFrame = Time.frameCount;
        if (!_latePoseSeen)
        {
            _latePoseSeen = true;
            Plugin.L.LogInfo("Late head pose active (posed right before rendering)");
        }

        if (InGame && !_cinemaActive)
        {
            NoteCameraWrite();
            if (CutsceneActive) PlaceCutsceneView();
            else if (_player.isLocked) HoldPlaySpaceSteady();
            else HoldAnchor();
            KeepLevel();
            KeepCameraInRig();
        }

        ApplyHeadPose();
        Input.XrInput.LocatePoses();
        Hands.LatePose();
        if (InGame && !_cinemaActive) ScaleStereo();
    }

    /// <summary>Tracking-space pose (metres) to world, through whichever rig is active.</summary>
    public static void TrackingToWorld(Vector3 localPos, Quaternion localRot, out Vector3 worldPos, out Quaternion worldRot)
    {
        var origin = ActiveOrigin;
        localPos += Vector3.up * HeightLift;
        if (origin == null)
        {
            worldPos = localPos;
            worldRot = localRot;
            return;
        }

        worldPos = origin.TransformPoint(localPos);
        worldRot = origin.rotation * localRot;
    }

    // ---------------- cinema ----------------

    private static void EnsureCinema()
    {
        if (_cinemaOrigin != null) return;

        var root = new GameObject("InkVR_Cinema");
        Object.DontDestroyOnLoad(root);
        root.transform.position = new Vector3(0, -5000, 0); // far away from anything the game draws
        _cinemaOrigin = root.transform;

        _cinemaCam = new GameObject("InkVR_CinemaCamera").AddComponent<Camera>();
        _cinemaCam.transform.SetParent(_cinemaOrigin, false);
        _cinemaCam.clearFlags = CameraClearFlags.SolidColor;
        _cinemaCam.backgroundColor = new Color(0.018f, 0.014f, 0.01f);
        _cinemaCam.cullingMask = VRLayers.UIMask;
        _cinemaCam.renderingPath = RenderingPath.Forward;
        _cinemaCam.nearClipPlane = 0.03f;
        _cinemaCam.farClipPlane = 60f;
        _cinemaCam.depth = 60; // above every game camera, so a paused game never shows through

        _cinema = new Cinema(_cinemaOrigin, UICapture.Texture);
        _cinemaActive = true;
        Recenter();
    }

    private static void SetCinema(bool on)
    {
        if (on == _cinemaActive && _cinemaOrigin.gameObject.activeSelf == on) return;
        _cinemaActive = on;
        _cinemaOrigin.gameObject.SetActive(on);
        if (!on && InGame) AdoptScale(); // coming back from the pause theatre: take any new size now
        if (on)
        {
            Recenter();
            _cinema?.Refresh();
        }

        if (InGame)
        {
            ParkGameCameras(on);
            _uiCam.enabled = !on;
            if (_gamePanel != null) _gamePanel.Visible = !on;
        }
    }

    /// <summary>Turn the theatre so its screen is straight ahead of where you are looking.</summary>
    public static void Recenter()
    {
        if (_cinemaOrigin == null) return;
        var local = HeadLocalPosition;
        var fwd = HeadLocalRotation * Vector3.forward;
        var yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        var rot = Quaternion.Euler(0, -yaw, 0);
        _cinemaOrigin.rotation = rot;
        var basePos = new Vector3(0, -5000, 0);
        _cinemaOrigin.position = basePos - rot * new Vector3(local.x, 0, local.z);
    }

    /// <summary>While paused, game cameras render into a tiny texture instead of the headset.</summary>
    private static void ParkGameCameras(bool park)
    {
        if (_parkedTarget == null)
        {
            _parkedTarget = new RenderTexture(16, 16, 16) { name = "InkVR_Parked" };
            Object.DontDestroyOnLoad(_parkedTarget);
        }

        foreach (var cam in _origin.GetComponentsInChildren<Camera>(true))
        {
            if (cam == _uiCam) continue;
            if (park)
            {
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = _parkedTarget;
            }
            else if (cam.targetTexture == _parkedTarget)
            {
                cam.targetTexture = null;
                cam.stereoTargetEye = StereoTargetEyeMask.Both;
            }
        }
    }

    // ---------------- player rig ----------------

    private static void AttachToPlayer(PlayerController player)
    {
        var gameCamera = GameManager.Instance.GameCamera;
        if (gameCamera == null || gameCamera.Camera == null) return;

        _player = player;
        _cc = player.CharacterController;

        // Henry's eye height in the flat game, measured before the camera is taken over.
        var feetWorld = player.transform.TransformPoint(FeetLocal());
        var eye = gameCamera.Camera.transform.position.y - feetWorld.y;
        if (eye > 2f && eye < 9f) _henryEye = eye;
        AdoptScale();

        var origin = new GameObject("InkVR_Origin").transform;
        origin.SetParent(player.transform, false);
        origin.localPosition = FeetLocal();
        origin.localRotation = Quaternion.identity;
        origin.localScale = Vector3.one * Scale;
        _origin = origin;

        _gameCam = gameCamera.Camera;
        _gameCam.transform.SetParent(_origin, false);
        _gameCam.transform.localPosition = Vector3.zero;
        _gameCam.transform.localRotation = Quaternion.identity;
        _gameCam.transform.localScale = Vector3.one;
        _gameCam.cullingMask &= ~VRLayers.UIMask;

        _uiCam = new GameObject("InkVR_UICamera").AddComponent<Camera>();
        _uiCam.transform.SetParent(_origin, false);
        _uiCam.clearFlags = CameraClearFlags.Depth;
        _uiCam.cullingMask = VRLayers.UIMask;
        _uiCam.renderingPath = RenderingPath.Forward;
        _uiCam.nearClipPlane = 0.02f * Scale;
        _uiCam.farClipPlane = 20f * Scale;
        _uiCam.depth = 50;

        _gamePanel = new VRPanel("InkVR_UIPanel", _origin, UICapture.Texture, Color.white, 0);
        _gamePanel.SetSize(GamePanelWidth, GamePanelWidth * UICapture.Height / UICapture.Width);
        _panelYaw = 0;

        // Re-apply the current cinema state to the new cameras.
        var cinema = _cinemaActive;
        _cinemaActive = !cinema;
        SetCinema(cinema);

        Plugin.L.LogInfo($"VR rig attached to player (scale {Scale}, feet at {origin.localPosition.y:F2})");
    }

    private static void DetachFromPlayer()
    {
        MotionSmoothing.Reset();
        _anchored = false;
        ResetCutscene();
        _player = null;
        _origin = null;
        _gameCam = null;
        _uiCam = null;
        _gamePanel = null;
        _cc = null;
    }

    private static Vector3 FeetLocal()
    {
        if (_cc == null) return new Vector3(0, -2.15f, 0);
        return _cc.center - Vector3.up * (_cc.height * 0.5f);
    }

    /// <summary>Snap/smooth turning rotates the play space around your head.</summary>
    public static void Turn(float degrees)
    {
        if (!InGame || _cinemaActive || _player.isLocked || CutsceneActive) return;
        ReAnchor(false);
        _origin.RotateAround(_gameCam.transform.position, Vector3.up, degrees);
        StoreAnchor();
    }

    private static void ApplySmoothTurn()
    {
        // Controllers (SmoothTurnInput) or a gamepad's right stick (captured game look input).
        var x = Mathf.Abs(SmoothTurnInput) > 0 ? SmoothTurnInput : VirtualInput.TurnInput;
        if (Mathf.Abs(x) < 0.15f) return;
        Turn(x * ModConfig.SmoothTurnSpeed.Value * Time.unscaledDeltaTime);
    }

    /// <summary>
    /// Keep the character's yaw equal to the head's yaw and its capsule under the head, while the
    /// camera stays exactly where the headset is. Skipped while a scripted sequence locks the player.
    /// </summary>
    private static float _nextCameraNote;

    /// <summary>
    /// Scripted moments (waking up at the start of Chapter 2, for one) move the game's camera onto their own
    /// animated camera rig, then hand it back to the player's camera holder. In VR that swings the whole view
    /// around you, and afterwards the camera turns with the player while the player turns to face the camera:
    /// an endless spin. The camera is the headset, so it always goes straight back into the VR rig.
    /// </summary>
    private static void KeepCameraInRig()
    {
        if (_gameCam == null || _origin == null) return;
        var t = _gameCam.transform;
        if (t.parent == _origin)
        {
            KeepCameraScale(t);
            return;
        }

        if (Time.unscaledTime > _nextCameraNote)
        {
            _nextCameraNote = Time.unscaledTime + 5f;
            Plugin.L.LogInfo($"The game moved its camera to '{(t.parent != null ? t.parent.name : "nothing")}': keeping it in the VR rig");
        }

        NoteCameraOwner(t);
        t.SetParent(_origin, false);
        KeepCameraScale(t);
        ApplyHeadPose();
    }

    /// <summary>
    /// The camera sits in the rig at scale 1: the rig carries the world size. When the game parents the camera
    /// somewhere else it keeps the camera's world size by giving it the rig's scale locally; brought back into
    /// the rig that scale would count twice, rendering the eyes several times too far apart (a miniature world).
    /// </summary>
    private static void KeepCameraScale(Transform t)
    {
        var s = t.localScale;
        if (Mathf.Abs(s.x - 1f) < 1e-4f && Mathf.Abs(s.y - 1f) < 1e-4f && Mathf.Abs(s.z - 1f) < 1e-4f) return;
        Plugin.L.LogInfo($"The game's camera came back scaled {s}: reset to 1 (the rig sets the world size)");
        t.localScale = Vector3.one;
    }

    private static bool _heldSteady;
    private static float _steadyYaw;
    private static Vector3 _steadyOffset, _steadyPlayerPos;

    /// <summary>
    /// The game sometimes takes the player over (waking up in Chapter 2, knockouts, grabs) and tilts and turns
    /// the player object to fake Henry's head moving. The play space hangs off the player, so in VR the whole
    /// world would swing around you. While the game holds the player, keep the play space level and facing the
    /// way it was; it still follows wherever the game moves the player. Your own head does the looking.
    /// </summary>
    private static void HoldPlaySpaceSteady()
    {
        if (_player == null || _origin == null || _cinemaActive) return;
        if (!_player.isLocked)
        {
            _heldSteady = false;
            return;
        }

        var t = _player.transform;
        var feet = t.position + Vector3.up * (FeetLocal().y * t.lossyScale.y);
        if (!_heldSteady)
        {
            // hold the play space exactly where it is (your head stays put), level
            _steadyYaw = YawOf(_origin.rotation);
            _steadyOffset = Vector3.ProjectOnPlane(_origin.position - feet, Vector3.up);
            _heldSteady = true;
            Plugin.L.LogInfo("Scripted moment: holding the play space level and steady");
        }
        else if ((t.position - _steadyPlayerPos).sqrMagnitude > 2.25f * Scale * Scale)
        {
            // The game moved Henry somewhere else (tied up for Sammy's sacrifice): in a blink, stand where he is and
            // face the way the game turned him.
            var headFwd = HeadLocalRotation * Vector3.forward;
            _steadyYaw = YawOf(t.rotation) - Mathf.Atan2(headFwd.x, headFwd.z) * Mathf.Rad2Deg;
            var head = HeadLocalPosition;
            _steadyOffset = -(Quaternion.Euler(0, _steadyYaw, 0) * new Vector3(head.x, 0, head.z) * Scale);
            _blink = 1f;
            HurtOver();
            Plugin.L.LogInfo($"Scripted moment: the game moved Henry; facing the way it turned him ({YawOf(t.rotation):F0} deg)");
        }

        _steadyPlayerPos = t.position;
        _origin.SetPositionAndRotation(feet + _steadyOffset, Quaternion.Euler(0, _steadyYaw, 0));
    }

    private static void FollowHeadWithBody()
    {
        if (CutsceneActive)
        {
            PlaceCutsceneView();
            return;
        }

        if (_player.isLocked)
        {
            HoldPlaySpaceSteady();
            return;
        }

        _heldSteady = false;

        var head = _gameCam.transform;
        var headPos = head.position;
        var originPos = _origin.position;
        var originRot = _origin.rotation;

        var headFwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (headFwd.sqrMagnitude > 1e-4f)
        {
            var bodyRot = Quaternion.LookRotation(headFwd.normalized, Vector3.up);
            _player.transform.rotation = bodyRot;
            _player.m_PlayerLook.m_CharacterTargetRotation = bodyRot;
        }

        // Move the body under the head. Ignore sub-centimetre head sway (it isn't walking), and press a little
        // into the floor while doing it: a purely sideways Move leaves the CharacterController "not grounded"
        // until the next physics step, which the game hears as a landing, dozens of times a second.
        var delta = headPos - _player.transform.position;
        delta.y = 0;
        var deadZone = 0.012f * Scale;
        if (delta.sqrMagnitude > deadZone * deadZone && _cc != null && _cc.enabled && !_player.isMoveLocked)
        {
            var stick = _cc.isGrounded ? Vector3.down * (0.06f * Scale) : Vector3.zero;
            _cc.Move(delta + stick);
        }

        // The play space stays where it was across the floor, and always sits on the player's feet (and level: a
        // tilt the game gave the player this frame mustn't stay in it).
        _origin.SetPositionAndRotation(originPos, Quaternion.Euler(0, YawOf(originRot), 0));
        var lp = _origin.localPosition;
        lp.y = FeetLocal().y;
        _origin.localPosition = lp;
    }

    private static void PlaceGamePanel()
    {
        if (_gamePanel == null) return;
        var head = _gameCam.transform;
        var headLocalPos = _origin.InverseTransformPoint(head.position);
        var fwd = _origin.InverseTransformDirection(head.forward);
        var yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        if (Mathf.Abs(Mathf.DeltaAngle(_panelYaw, yaw)) > PanelFollowAngle)
            _panelYaw = Mathf.MoveTowardsAngle(_panelYaw, yaw, 180f * Time.unscaledDeltaTime);
        var rot = Quaternion.Euler(10f, _panelYaw, 0);
        _gamePanel.Transform.localRotation = rot;
        _gamePanel.Transform.localPosition = headLocalPos + rot * Vector3.forward * GamePanelDistance;
    }

    /// <summary>Every stereo camera inside the scaled origin (game, weapon, invisible, UI cameras).</summary>
    /// <summary>
    /// AUTO world size: keep reading how high your eyes are (ten times a second, the last ~12 s, in menus
    /// and in game) and take a high percentile, so glancing down or crouching doesn't count. The result
    /// sets Henry's eye height = yours; it's adopted at the next level start or resume (see AdoptScale).
    /// </summary>
    private static void MeasureHeight()
    {
        var now = Time.unscaledTime;
        if (now < _nextHeightSample) return;
        _nextHeightSample = now + 0.1f;
        var y = (DevHeadOverride?.pos ?? InputTracking.GetLocalPosition(XRNode.CenterEye)).y;
        if (y < 0.3f || y > 2.5f) return;
        if (ModConfig.SeatedMode.Value)
        {
            // Sitting: this isn't your standing height (it mustn't shrink the world); it sets the lift instead.
            MeasureSeated(y);
            return;
        }

        if (SeatedLift > 0 || _seatedSet)
        {
            SeatedLift = 0;
            _seatedSet = false;
            _seatedEasing = false;
            SeatedHeights.Clear();
            Plugin.L.LogInfo("Seated mode off: your view is your own height again");
        }

        Heights.Add(y);
        if (Heights.Count > 120) Heights.RemoveAt(0);
        if (Heights.Count < 40) return;

        var sorted = new System.Collections.Generic.List<float>(Heights);
        sorted.Sort();
        var h = sorted[(int)(sorted.Count * 0.8f)];
        if (h < 0.8f || h > 2.2f) return;
        var first = _measuredHeight < 0;
        _measuredHeight = h;
        if ((first || Mathf.Abs(ModConfig.MeasuredEyeHeight.Value - h) > 0.015f) && now - _lastHeightSave > 20f)
        {
            _lastHeightSave = now;
            ModConfig.MeasuredEyeHeight.Value = (float)System.Math.Round(h, 3);
            Plugin.L.LogInfo($"Height: your eyes are {h:F2} m up; Henry's are {_henryEye:F2} units, so AUTO makes the world {AutoScale:F2} units per metre ({2.95f / AutoScale:P0} world size)");
        }
    }

    /// <summary>
    /// SEATED mode: your seated eye height over the last 6 s (a high percentile, so leaning down to the floor doesn't
    /// count), and the lift that takes it to standing height. Set at once the first time, then eased (30 cm/s) so
    /// sitting up or slouching never jolts the view.
    /// </summary>
    private static void MeasureSeated(float y)
    {
        SeatedHeights.Add(y);
        if (SeatedHeights.Count > 60) SeatedHeights.RemoveAt(0);
        if (SeatedHeights.Count < 20) return;
        var sorted = new System.Collections.Generic.List<float>(SeatedHeights);
        sorted.Sort();
        // the first time, the middle of two seconds of sitting; after that a high percentile, so leaning down doesn't count
        var seated = sorted[(int)(sorted.Count * (_seatedSet ? 0.8f : 0.5f))];
        var want = Mathf.Clamp(SeatedTargetEye - seated, 0f, 1.2f);
        if (!_seatedSet)
        {
            _seatedSet = true;
            SeatedLift = want;
            Plugin.L.LogInfo($"Seated mode: your eyes are {seated:F2} m up sitting; lifted {want:F2} m to standing height {SeatedTargetEye:F2} m");
        }
        else if (_seatedEasing || Mathf.Abs(want - SeatedLift) > 0.05f)
        {
            // once it starts moving it goes all the way, rather than stopping 5 cm short
            SeatedLift = Mathf.MoveTowards(SeatedLift, want, 0.03f);
            _seatedEasing = Mathf.Abs(want - SeatedLift) > 0.005f;
            if (!_seatedEasing) Plugin.L.LogInfo($"Seated mode: now {seated:F2} m up sitting; lift eased to {SeatedLift:F2} m");
        }
    }

    /// <summary>Dev: switch InkVR's eye-separation scaling off to compare.</summary>
    public static bool DevNoStereoScale;

    private static void ScaleStereo()
    {
        if (DevNoStereoScale)
        {
            foreach (var cam in _origin.GetComponentsInChildren<Camera>()) cam.ResetStereoViewMatrices();
            return;
        }

        foreach (var cam in _origin.GetComponentsInChildren<Camera>())
            if (cam.enabled && cam.targetTexture == null)
                StereoScale.Apply(cam, Scale);
    }

    // ---------------- fades ----------------

    /// <summary>The game fades with a full-screen UI image; in VR that has to cover the whole view.</summary>
    private static void UpdateFade()
    {
        var cam = _cinemaActive || !InGame ? _cinemaCam : _uiCam;
        if (cam == null) return;
        if (_fade == null || _fade.GameObject == null)
        {
            _fade = new VRPanel("InkVR_Fade", cam.transform, Texture2D.whiteTexture, Color.clear, 100);
            _fade.Transform.localPosition = new Vector3(0, 0, 0.1f);
            _fade.SetSize(1f, 1f);
        }
        else if (_fade.Transform.parent != cam.transform)
        {
            _fade.Transform.SetParent(cam.transform, false);
            _fade.Transform.localPosition = new Vector3(0, 0, 0.1f);
            _fade.Transform.localRotation = Quaternion.identity;
        }

        // In the theatre only the game's own fades during loads matter; a paused game shows no blocker.
        var c = ScreenFade.Current();
        if (_cinemaActive && InGame) c.a = 0;
        else if (InGame && CutsceneBlink > c.a) c = new Color(0, 0, 0, CutsceneBlink);
        _fade.Color = c;
        _fade.Visible = c.a > 0.001f;
    }
}
