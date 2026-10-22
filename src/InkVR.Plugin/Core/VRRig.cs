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
public static class VRRig
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
    public static float Scale => ModConfig.WorldScale.Value;
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
        if (!XrManager.IsRunning)
        {
            if (_cinemaOrigin != null) _cinemaOrigin.gameObject.SetActive(false);
            return;
        }

        UICapture.Update();

        var player = GameManager.Instance?.Player;
        if (player != null && player != _player) AttachToPlayer(player);
        if (player == null && _player != null) DetachFromPlayer();

        EnsureCinema();
        SetCinema(!InGame || IsPaused());
        ApplyHeadPose();
    }

    public static void LateUpdate()
    {
        if (!XrManager.IsRunning) return;
        ApplyHeadPose();

        if (InGame && !_cinemaActive)
        {
            ApplySmoothTurn();
            FollowHeadWithBody();
            PlaceGamePanel();
            Rendering.VRGraphics.Update(_gameCam);
        }

        _cinema?.Animate();
        UpdateFade();
        Hands.Update();
        LaserPointer.Update();
        if (InGame && !_cinemaActive) ScaleStereo();
    }

    private static bool IsPaused()
    {
        var gm = GameManager.Instance;
        return gm != null && gm.isPaused;
    }

    // ---------------- tracking ----------------

    public static Vector3 HeadLocalPosition =>
        (DevHeadOverride?.pos ?? InputTracking.GetLocalPosition(XRNode.CenterEye)) + Vector3.up * ModConfig.HeightOffset.Value;

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
        }
    }

    /// <summary>Tracking-space pose (metres) to world, through whichever rig is active.</summary>
    public static void TrackingToWorld(Vector3 localPos, Quaternion localRot, out Vector3 worldPos, out Quaternion worldRot)
    {
        var origin = ActiveOrigin;
        localPos += Vector3.up * ModConfig.HeightOffset.Value;
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
        if (on) Recenter();

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
        if (!InGame || _cinemaActive || _player.isLocked) return;
        _origin.RotateAround(_gameCam.transform.position, Vector3.up, degrees);
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
    private static void FollowHeadWithBody()
    {
        if (_player.isLocked) return;

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

        var delta = headPos - _player.transform.position;
        delta.y = 0;
        if (delta.sqrMagnitude > 0.0001f && _cc != null && _cc.enabled && !_player.isMoveLocked)
            _cc.Move(delta);

        _origin.SetPositionAndRotation(originPos, originRot);
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
    private static void ScaleStereo()
    {
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
        _fade.Color = c;
        _fade.Visible = c.a > 0.001f;
    }
}
