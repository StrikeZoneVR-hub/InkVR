using UnityEngine;

namespace InkVR.Core;

/// <summary>
/// Cutscenes and scripted moments in VR. The game plays them by taking its camera holder off Henry's head and
/// moving it on its own (waking up on the floor at the start of Chapter 2, knockouts, falls, being carried), or by
/// putting the camera on a rig of its own. While it does, the headset follows that camera through the world, so
/// you're on the floor, getting up, being dragged, in VR. Only its position is followed: the way it faces is taken
/// once when the scene starts (and again on a cut), and your own head does all the looking, so the world never
/// tilts, rolls or swings round you. A short blink of black hides the jump in and out and any cuts. Henry's body
/// steps aside while the game has the camera, or the player, and the hands stay.
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

    private static Transform _camOwner;                 // a rig the game put the camera itself on, if it did
    private static Vector3 _ownerLocalPos;
    private static Quaternion _ownerLocalRot = Quaternion.identity;
    private static Vector3 _view, _lastRawView;         // the game camera's position (smoothed, and as it was last frame)
    private static float _lastRawYaw;
    private static float _cutYaw;                       // the play space's turn while following
    private static Vector3 _cutHead;                    // your tracked head (metres) when the scene was lined up
    private static float _homeFor, _lockedFor, _blink;

    /// <summary>The game put its camera on something of its own (not the player's camera holder): follow that.</summary>
    private static void NoteCameraOwner(Transform cam)
    {
        var holder = GameManager.Instance?.GameCamera?.CameraContainer;
        var parent = cam.parent;
        if (parent == null || parent == _origin || parent == holder)
        {
            if (_camOwner != null) Plugin.L.LogInfo("Cutscene: the game gave its camera back to Henry's head");
            _camOwner = null;
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

        var gc = GameManager.Instance?.GameCamera;
        var holder = gc?.CameraContainer;
        var head = gc?.HeadContainer;
        if (holder == null || head == null) return false;
        pos = holder.position;
        rot = holder.rotation;
        var moved = holder.parent != head;
        var dist = (holder.position - head.position).magnitude;
        var turned = Quaternion.Angle(holder.rotation, head.rotation);
        away = moved || (_player.isLocked && (dist > 0.6f || turned > 40f));
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
        _lockedFor = _player.isLocked ? _lockedFor + dt : 0f;
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
        _lockedFor = _homeFor = 0f;
    }

    /// <summary>The blink of black over cuts, on top of the game's own fades.</summary>
    private static float CutsceneBlink => _blink;
}
