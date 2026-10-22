using BendyVR.Input;
using BendyVR.UI;
using UnityEngine;
using UnityEngine.XR;

namespace BendyVR.Core;

/// <summary>
/// Owns the VR camera setup. In menus a mod-owned rig shows the game UI on a panel in a dark room;
/// in gameplay the game's own camera is moved under a head-tracked, world-scaled origin on the player.
///
///   PlayerController (yaw = your head yaw, so movement is head-relative)
///     BendyVR_Origin   at the capsule's feet, scale = WorldScale (game units per metre)
///       GameCamera_PostProcess (+WeaponCam, InvisibleCam)   ← tracked by Unity XR
///       BendyVR_UICamera   (layer 31 only: UI panel + fade)  ← tracked by Unity XR
///       BendyVR_UIPanel
/// </summary>
public static class VRRig
{
    private const float MenuPanelDistance = 2.4f, MenuPanelWidth = 3.2f;
    private const float GamePanelDistance = 1.6f, GamePanelWidth = 1.5f;
    private const float PanelFollowAngle = 35f;

    // Menu rig (persistent).
    private static Transform _menuOrigin;
    private static Camera _menuCam;
    private static VRPanel _menuPanel;

    // Player rig (lives on the current PlayerController, destroyed with it).
    private static PlayerController _player;
    private static Transform _origin;
    private static Camera _gameCam;
    private static Camera _uiCam;
    private static VRPanel _gamePanel;
    private static VRPanel _fade;
    private static CharacterController _cc;
    private static float _panelYaw;

    public static bool InGame => _player != null && _origin != null;
    public static Transform Origin => InGame ? _origin : _menuOrigin;
    public static Camera GameCamera => InGame ? _gameCam : null;
    public static Transform Head => InGame ? _gameCam.transform : _menuCam != null ? _menuCam.transform : null;
    public static float Scale => ModConfig.WorldScale.Value;

    /// <summary>Dev/testing: replaces the tracked head pose (metres, tracking space).</summary>
    public static (Vector3 pos, Quaternion rot)? DevHeadOverride;

    public static void Update()
    {
        if (!XrManager.IsRunning)
        {
            if (_menuOrigin != null) _menuOrigin.gameObject.SetActive(false);
            return;
        }

        UICapture.Update();

        var player = GameManager.Instance?.Player;
        if (player != null && player != _player) AttachToPlayer(player);
        if (player == null && _player != null) DetachFromPlayer();

        EnsureMenuRig();
        _menuOrigin.gameObject.SetActive(!InGame);
        ApplyHeadPose();
    }

    /// <summary>
    /// XR SDK cameras are not tracked implicitly (that is TrackedPoseDriver's job), so copy the
    /// centre-eye pose onto the rig cameras. Runs in Update (for game logic) and again in
    /// LateUpdate (Application.onBeforeRender is stripped from this build).
    /// </summary>
    public static void ApplyHeadPose()
    {
        if (!XrManager.IsRunning) return;
        var pos = DevHeadOverride?.pos ?? InputTracking.GetLocalPosition(XRNode.CenterEye);
        var rot = DevHeadOverride?.rot ?? InputTracking.GetLocalRotation(XRNode.CenterEye);
        pos.y += ModConfig.HeightOffset.Value;
        if (InGame)
        {
            _gameCam.transform.localPosition = pos;
            _gameCam.transform.localRotation = rot;
            _uiCam.transform.localPosition = pos;
            _uiCam.transform.localRotation = rot;
        }
        else if (_menuCam != null)
        {
            _menuCam.transform.localPosition = pos;
            _menuCam.transform.localRotation = rot;
        }
    }

    public static void LateUpdate()
    {
        if (!XrManager.IsRunning) return;
        ApplyHeadPose();

        if (InGame)
        {
            ApplyTurn();
            FollowHeadWithBody();
            PlaceGamePanel();
            UpdateFade();
            Rendering.VRGraphics.Update(_gameCam);
            ScaleStereo();
        }
        else if (_menuCam != null)
        {
            UpdateFade();
        }
    }

    /// <summary>Every stereo camera inside the scaled origin (game, weapon, invisible, UI cameras).</summary>
    private static void ScaleStereo()
    {
        foreach (var cam in _origin.GetComponentsInChildren<Camera>())
            if (cam.enabled && cam.targetTexture == null)
                StereoScale.Apply(cam, Scale);
    }

    // ---------------- menu rig ----------------

    private static void EnsureMenuRig()
    {
        if (_menuOrigin != null) return;

        var root = new GameObject("BendyVR_MenuRig");
        Object.DontDestroyOnLoad(root);
        root.transform.position = new Vector3(0, -500, 0); // away from anything the game draws
        _menuOrigin = root.transform;

        _menuCam = new GameObject("BendyVR_MenuCamera").AddComponent<Camera>();
        _menuCam.transform.SetParent(_menuOrigin, false);
        _menuCam.clearFlags = CameraClearFlags.SolidColor;
        _menuCam.backgroundColor = new Color(0.03f, 0.025f, 0.015f);
        _menuCam.cullingMask = VRLayers.UIMask;
        _menuCam.nearClipPlane = 0.05f;
        _menuCam.farClipPlane = 100f;
        _menuCam.depth = 0;

        _menuPanel = new VRPanel("BendyVR_MenuPanel", _menuOrigin, UICapture.Texture, Color.white, 0);
        _menuPanel.SetSize(MenuPanelWidth, MenuPanelWidth * UICapture.Height / UICapture.Width);
        _menuPanel.Transform.localPosition = new Vector3(0, 1.6f, MenuPanelDistance);
    }

    // ---------------- player rig ----------------

    private static void AttachToPlayer(PlayerController player)
    {
        var gameCamera = GameManager.Instance.GameCamera;
        if (gameCamera == null || gameCamera.Camera == null) return;

        _player = player;
        _cc = player.CharacterController;

        var origin = new GameObject("BendyVR_Origin").transform;
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

        _uiCam = new GameObject("BendyVR_UICamera").AddComponent<Camera>();
        _uiCam.transform.SetParent(_origin, false);
        _uiCam.clearFlags = CameraClearFlags.Depth;
        _uiCam.cullingMask = VRLayers.UIMask;
        _uiCam.nearClipPlane = 0.02f * Scale;
        _uiCam.farClipPlane = 20f * Scale;
        _uiCam.depth = 50;

        _gamePanel = new VRPanel("BendyVR_UIPanel", _origin, UICapture.Texture, Color.white, 0);
        _gamePanel.SetSize(GamePanelWidth, GamePanelWidth * UICapture.Height / UICapture.Width);
        _panelYaw = 0;

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

    /// <summary>Stick turning rotates the play space around your head (the game's own look input is muted in VR).</summary>
    private static void ApplyTurn()
    {
        var x = VirtualInput.TurnInput;
        if (Mathf.Abs(x) < 0.15f) return;
        var angle = x * ModConfig.SmoothTurnSpeed.Value * Time.unscaledDeltaTime;
        RotateAroundHead(angle);
    }

    private static void RotateAroundHead(float angle)
    {
        _origin.RotateAround(_gameCam.transform.position, Vector3.up, angle);
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

        // Yaw: turn the body to face where the head looks.
        var headFwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (headFwd.sqrMagnitude > 1e-4f)
        {
            var bodyRot = Quaternion.LookRotation(headFwd.normalized, Vector3.up);
            _player.transform.rotation = bodyRot;
            _player.m_PlayerLook.m_CharacterTargetRotation = bodyRot;
        }

        // Position: drag the capsule under the head (room-scale), with collision.
        var delta = headPos - _player.transform.position;
        delta.y = 0;
        if (delta.sqrMagnitude > 0.0001f && _cc != null && _cc.enabled && !_player.isMoveLocked)
            _cc.Move(delta);

        // Undo the side effects on the origin so the camera does not move.
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

    // ---------------- fades ----------------

    /// <summary>The game fades with a full-screen UI image; in VR that has to cover the whole view.</summary>
    private static void UpdateFade()
    {
        var cam = InGame ? _uiCam : _menuCam;
        if (cam == null) return;
        if (_fade == null || _fade.GameObject == null)
        {
            _fade = new VRPanel("BendyVR_Fade", cam.transform, Texture2D.whiteTexture, Color.clear, 100);
            _fade.Transform.localPosition = new Vector3(0, 0, 0.1f);
            _fade.SetSize(1f, 1f);
        }
        else if (_fade.Transform.parent != cam.transform)
        {
            _fade.Transform.SetParent(cam.transform, false);
        }

        var c = ScreenFade.Current();
        _fade.Color = c;
        _fade.Visible = c.a > 0.001f;
    }
}
