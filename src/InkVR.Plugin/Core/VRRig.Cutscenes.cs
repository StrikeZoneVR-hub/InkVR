using UnityEngine;

namespace InkVR.Core;

/// <summary>
/// Cutscenes and scripted moments in VR. The game plays them in three ways: it takes its camera holder off Henry's
/// head and moves it on its own (waking up on the floor at the start of Chapter 2), it sets the camera's pose itself
/// (Sammy's knockout), or it tilts and rolls the player object to fake Henry falling and being dragged. Whichever it
/// does, the headset follows that camera through the world, so you're on the floor, getting up, being dragged, in VR.
/// Only its position is followed: the way it faces is taken once when the scene starts (and again on a cut), and your
/// own head does all the looking, so the world never tilts, rolls or swings round you (the play space is always kept
/// level). A short blink of black hides the jump in and out and any cuts. Henry's body steps aside while the game has
/// the camera, or the player, and the hands stay.
/// </summary>
public static partial class VRRig
{
    private const float CutDistance = 2.5f;  // game units in one frame: a cut, not a move
    private const float CutTurn = 60f;       // degrees in one frame
    private const float EndAfter = 0.25f;    // s back home before the scene counts as over
    private const float BlinkIn = 0.35f;     // s from black back to the scene

    /// <summary>The game has the camera (a cutscene or scripted moment): the headset is following it.</summary>
    public static bool CutsceneActive { get; private set; }

    /// <summary>Henry's body (not the hands) steps aside: a cutscene has the camera, or a scripted moment has the player.</summary>
    public static bool BodyHidden => InGame && !_cinemaActive && (CutsceneActive || _lockedFor > 0.3f);
    // (held still while still able to look round counts too: tied up for Sammy's sacrifice)

    private static Transform _camOwner;                 // a rig the game put the camera itself on, if it did
    private static Vector3 _ownerLocalPos;
    private static Quaternion _ownerLocalRot = Quaternion.identity;
    private static Vector3 _view, _lastRawView;         // the game camera's position (smoothed, and as it was last frame)
    private static float _lastRawYaw;
    private static float _cutYaw;                       // the play space's turn while following
    private static Vector3 _cutHead;                    // your tracked head (metres) when the scene was lined up
    private static float _homeFor, _lockedFor, _blink;

    // the camera pose the game last set itself (it never does outside scripted scenes)
    private static Vector3 _appliedPos, _writtenPos;
    private static Quaternion _appliedRot = Quaternion.identity, _writtenRot = Quaternion.identity;
    private static bool _appliedValid, _written;
    private static float _writtenAt;
    private static Vector3 _writtenPlayerPos;

    /// <summary>InkVR just put the camera on your head: remember exactly where, to notice the game moving it.</summary>
    private static void NoteApplied(Vector3 pos, Quaternion rot)
    {
        _appliedPos = pos;
        _appliedRot = rot;
        _appliedValid = true;
    }

    /// <summary>
    /// Before InkVR poses the camera: if it isn't where InkVR left it, the game set it (a scripted scene: Sammy's
    /// knockout puts it on the floor of his office). That pose is followed like any other cutscene camera.
    /// </summary>
    private static void NoteCameraWrite()
    {
        if (!_appliedValid || _gameCam == null || _origin == null) return;
        var t = _gameCam.transform;
        if (t.parent != _origin) return; // put on a rig of its own: NoteCameraOwner follows that
        var lp = t.localPosition;
        var lr = t.localRotation;
        var moved = (lp - _appliedPos).sqrMagnitude > 0.04f; // 20 cm (the rig works in metres)
        var turned = Quaternion.Angle(lr, _appliedRot) > 12f;
        if (!moved && !turned) return;
        _appliedValid = false;
        if (HolderAway())
        {
            // moved relative to the holder the game took away (the Chapter 2 wake-up): the holder is what's followed
            _written = false;
            return;
        }

        if (lp.sqrMagnitude < 0.0025f && Quaternion.Angle(lr, Quaternion.identity) < 1f)
        {
            // the game putting its camera back to rest (zero, relative to the holder it thinks it's in): the scene is over
            if (_written) Plugin.L.LogInfo("Cutscene: the game put its camera back to rest");
            _written = false;
            return;
        }

        if (!_written) Plugin.L.LogInfo($"Cutscene: the game set its camera itself ({(t.position - _player.transform.position).magnitude:F1} units from Henry); following it in VR");
        _written = true;
        _writtenPos = t.position;
        _writtenRot = t.rotation;
        _writtenAt = Time.unscaledTime;
        _writtenPlayerPos = _player.transform.position;
    }

    /// <summary>The game's camera pose stays the scene's view while it keeps setting it, or while it still holds Henry.</summary>
    private static bool WrittenViewLive()
    {
        if (!_written) return false;
        var since = Time.unscaledTime - _writtenAt;
        var teleported = (_player.transform.position - _writtenPlayerPos).sqrMagnitude > 4f * Scale * Scale;
        if (since < 0.6f || (_player.isLocked && since < 30f && !teleported)) return true;
        _written = false;
        Plugin.L.LogInfo("Cutscene: the game stopped setting its camera");
        return false;
    }

    /// <summary>The game has taken its camera holder off Henry's head, or is tilting the player it hangs from.</summary>
    private static bool HolderAway()
    {
        var gc = GameManager.Instance?.GameCamera;
        var holder = gc?.CameraContainer;
        var head = gc?.HeadContainer;
        if (holder == null || head == null || _player == null) return false;
        if (holder.parent != head || PlayerTilted) return true;
        return _player.isLocked && ((holder.position - head.position).magnitude > 0.6f || Quaternion.Angle(holder.rotation, head.rotation) > 40f);
    }

    // ---------------- the play space's own place in the world ----------------

    private static bool _anchored;
    private static Vector3 _anchorPos, _anchorPlayer;
    private static float _anchorYaw;

    /// <summary>
    /// The play space hangs from the player object, so anything the game does to the player moves it too. The game
    /// moving Henry (a lift, being carried, a teleport) should carry you along; the game turning him should not: tied
    /// up for Sammy's sacrifice the game keeps turning Henry back into the view it allows while InkVR turns him to
    /// face where you look, and the whole world flipped between the two. So the play space keeps its own facing
    /// (InkVR's turns only) and follows only the player's moves. Called at the start of each frame's VR update.
    /// </summary>
    private static void ReAnchor(bool allowRealign = true)
    {
        if (!_anchored || _origin == null || _player == null) return;
        var moved = _player.transform.position - _anchorPlayer;
        if (allowRealign && !CutsceneActive && !_player.isLocked && moved.sqrMagnitude > 2.25f * Scale * Scale)
        {
            // The game put Henry somewhere else while you can still look around (tied up, respawned): in a blink,
            // stand where he is and face the way the game turned him.
            var headFwd = HeadLocalRotation * Vector3.forward;
            var yaw = YawOf(_player.transform.rotation) - Mathf.Atan2(headFwd.x, headFwd.z) * Mathf.Rad2Deg;
            var rot = Quaternion.Euler(0, yaw, 0);
            var head = HeadLocalPosition;
            var feet = _player.transform.TransformPoint(FeetLocal());
            _origin.SetPositionAndRotation(feet - rot * new Vector3(head.x, 0, head.z) * Scale, rot);
            _blink = 1f;
            Plugin.L.LogInfo($"The game moved Henry {moved.magnitude / Scale:F1} m; facing the way it turned him ({YawOf(_player.transform.rotation):F0} deg)");
            StoreAnchor();
            return;
        }

        _origin.SetPositionAndRotation(_anchorPos + moved, Quaternion.Euler(0, _anchorYaw, 0));
    }

    /// <summary>After the frame's VR update: where the play space is, and where the player was, to carry on from.</summary>
    private static void StoreAnchor()
    {
        if (_origin == null || _player == null) return;
        _anchorPos = _origin.position;
        _anchorYaw = YawOf(_origin.rotation);
        _anchorPlayer = _player.transform.position;
        _anchored = true;
    }

    /// <summary>Just before rendering: the same again, in case the game turned the player after InkVR's update.</summary>
    private static void HoldAnchor()
    {
        if (!_anchored || _origin == null || _player == null) return;
        var pos = _anchorPos + (_player.transform.position - _anchorPlayer) + MotionSmoothing.OffsetOn(_origin);
        _origin.SetPositionAndRotation(pos, Quaternion.Euler(0, _anchorYaw, 0));
    }

    /// <summary>The game is tilting or rolling the player object (faking Henry falling or being dragged).</summary>
    private static bool PlayerTilted => _player != null && Vector3.Angle(_player.transform.up, Vector3.up) > 8f;

    /// <summary>The play space never tilts or rolls, whatever the game does to the player it hangs from.</summary>
    private static void KeepLevel()
    {
        if (_origin == null || Vector3.Angle(_origin.up, Vector3.up) < 0.01f) return;
        _origin.rotation = Quaternion.Euler(0, YawOf(_origin.rotation), 0);
    }

    /// <summary>The game put its camera on something of its own (not the player's camera holder): follow that.</summary>
    private static void NoteCameraOwner(Transform cam)
    {
        var holder = GameManager.Instance?.GameCamera?.CameraContainer;
        var parent = cam.parent;
        if (parent == null || parent == _origin || parent == holder)
        {
            if (_camOwner != null) Plugin.L.LogInfo("Cutscene: the game gave its camera back to Henry's head");
            _camOwner = null;
            if (parent == holder) _written = false; // handed back to its holder: any pose it set is over
            return;
        }

        _camOwner = parent;
        _ownerLocalPos = cam.localPosition;
        _ownerLocalRot = cam.localRotation;
        Plugin.L.LogInfo($"Cutscene: the game put its camera on '{parent.name}' (local {_ownerLocalPos}); following it in VR");
    }

    /// <summary>Where the game wants its camera this frame, and whether that's away from Henry's head.</summary>
    private static bool GameView(out Vector3 pos, out Quaternion rot, out bool away)
    {
        pos = default;
        rot = Quaternion.identity;
        away = false;
        if (_camOwner != null)
        {
            pos = _camOwner.TransformPoint(_ownerLocalPos);
            rot = _camOwner.rotation * _ownerLocalRot;
            away = true;
            return true;
        }

        var holder = GameManager.Instance?.GameCamera?.CameraContainer;
        if (holder == null) return false;
        pos = holder.position;
        rot = holder.rotation;
        away = HolderAway();
        if (away)
        {
            // The holder is the scene's camera; anything the game does to the camera itself now is relative to it
            // (the Chapter 2 wake-up nudges it as Henry kneels), so it isn't a pose of its own.
            _written = false;
            return true;
        }

        // The holder is home, but the game set the camera itself (Sammy's knockout): that pose is the scene's view.
        if (WrittenViewLive())
        {
            pos = _writtenPos;
            rot = _writtenRot;
            away = true;
        }

        return true;
    }

    /// <summary>
    /// The way a camera faces across the floor. Lying on your back looking up (or bent over looking down), its
    /// forward hardly points anywhere across the floor, but its top does: sitting up you'll face away from where
    /// the top of your head was. The two are blended by how steeply it looks, so the facing never jumps.
    /// </summary>
    private static float YawOf(Quaternion q)
    {
        var f = q * Vector3.forward;
        var u = q * Vector3.up;
        var steep = Mathf.Abs(f.y);
        var across = new Vector3(f.x, 0, f.z) + (f.y > 0 ? -1f : 1f) * steep * new Vector3(u.x, 0, u.z);
        return across.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(across.x, across.z) * Mathf.Rad2Deg;
    }

    /// <summary>Once a frame (LateUpdate): start, follow or finish a cutscene.</summary>
    private static void UpdateCutscene()
    {
        var dt = Time.unscaledDeltaTime;
        _lockedFor = _player.isLocked || _player.isMoveLocked ? _lockedFor + dt : 0f;
        _blink = Mathf.MoveTowards(_blink, 0f, dt / BlinkIn);

        if (!GameView(out var pos, out var rot, out var away))
        {
            if (CutsceneActive) EndCutscene();
            return;
        }

        if (!CutsceneActive)
        {
            if (away) BeginCutscene(pos, rot);
            return;
        }

        if (!away)
        {
            _homeFor += dt;
            if (_homeFor >= EndAfter)
            {
                EndCutscene();
                return;
            }
        }
        else _homeFor = 0f;

        // A cut (the camera jumps or spins in one frame): blink, and line the scene up again.
        var yaw = YawOf(rot);
        if ((pos - _lastRawView).magnitude > CutDistance || Mathf.Abs(Mathf.DeltaAngle(yaw, _lastRawYaw)) > CutTurn)
        {
            LineUp(pos, rot);
            Plugin.L.LogInfo("Cutscene: a cut; lined up again");
        }
        else
        {
            // Follow the game's camera closely, without its little shakes.
            _view = Vector3.Lerp(_view, pos, 1f - Mathf.Exp(-dt / 0.06f));
        }

        _lastRawView = pos;
        _lastRawYaw = yaw;
    }

    private static void BeginCutscene(Vector3 pos, Quaternion rot)
    {
        CutsceneActive = true;
        _homeFor = 0f;
        LineUp(pos, rot);
        _lastRawView = pos;
        _lastRawYaw = YawOf(rot);
        Plugin.L.LogInfo($"Cutscene: the game has the camera; following it in VR (facing {_lastRawYaw:F0}°)");
    }

    /// <summary>Face the way the scene looks, from where you are standing in your room, in a blink.</summary>
    private static void LineUp(Vector3 pos, Quaternion rot)
    {
        _cutHead = HeadLocalPosition;
        var headFwd = HeadLocalRotation * Vector3.forward;
        var headYaw = Mathf.Atan2(headFwd.x, headFwd.z) * Mathf.Rad2Deg;
        _cutYaw = YawOf(rot) - headYaw;
        _view = pos;
        _blink = 1f;
    }

    private static void EndCutscene()
    {
        CutsceneActive = false;
        _camOwner = null;
        _heldSteady = false; // a scripted moment still holding Henry keeps the way you face now, not before the scene
        if (_origin == null || _player == null) return;

        // Back on Henry's feet: your head straight over the player, facing the way you are.
        var before = _origin.TransformPoint(HeadLocalPosition);
        var rot = Quaternion.Euler(0, _cutYaw, 0);
        var head = HeadLocalPosition;
        var feet = _player.transform.TransformPoint(FeetLocal());
        _origin.SetPositionAndRotation(feet - rot * new Vector3(head.x, 0, head.z) * Scale, rot);
        var lp = _origin.localPosition;
        lp.y = FeetLocal().y;
        _origin.localPosition = lp;
        var jump = (_origin.TransformPoint(HeadLocalPosition) - before).magnitude / Scale;
        if (jump > 0.25f) _blink = 1f; // only blink if the view really has to jump (metres)
        Plugin.L.LogInfo($"Cutscene: over; back on Henry's feet (view moved {jump:F2} m)");
    }

    /// <summary>Put the play space where your head sees from the game's camera (every frame, and again just before rendering).</summary>
    private static void PlaceCutsceneView()
    {
        if (!CutsceneActive || _origin == null) return;
        var rot = Quaternion.Euler(0, _cutYaw, 0);
        _origin.SetPositionAndRotation(_view - rot * (_cutHead * Scale), rot);
    }

    private static void ResetCutscene()
    {
        CutsceneActive = false;
        _camOwner = null;
        _written = _appliedValid = false;
        _lockedFor = _homeFor = 0f;
    }

    /// <summary>The blink of black over cuts, on top of the game's own fades.</summary>
    private static float CutsceneBlink => _blink;
}
