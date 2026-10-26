using HarmonyLib;
using UnityEngine;

namespace InkVR.Core;

/// <summary>
/// Cutscenes and scripted moments in VR. The game plays them in three ways: it takes its camera holder off Henry's
/// head and moves it on its own (waking up on the floor at the start of Chapter 2), it sets the camera's pose itself
/// (Sammy's knockout), or it tilts and rolls the player object to fake Henry falling and being dragged. Whichever it
/// does, the headset follows that camera through the world, so you're on the floor, getting up, being dragged, in VR.
/// Only its position is followed: the way it faces is taken once when the scene starts (and again on a cut), and your
/// own head does all the looking, so the world never tilts, rolls or swings round you (the play space is always kept
/// level). A short blink of black hides the jump in and out and any cuts. Henry's body stays yours throughout: it
/// stands under your view and your arms move it; only walking is held until the scene is over.
/// Being hit or killed isn't a scene: the game's camera shake and death fall are ignored, and you stay in Henry.
/// Nor is any other camera shake (the Chapter 1 finale's ink flood, Brute Boris's footsteps, the Beast's charge):
/// in VR your view stays steady.
/// </summary>
public static partial class VRRig
{
    private const float CutDistance = 2.5f;  // game units in one frame: a cut, not a move
    private const float CutTurn = 60f;       // degrees in one frame
    private const float EndAfter = 0.25f;    // s back home before the scene counts as over
    private const float BlinkIn = 0.35f;     // s from black back to the scene
    private const float HitShake = 1.5f;     // s after a hit when the game's camera shake is ignored
    private const float DeathFall = 8f;      // s after dying when the game's death camera is ignored
    private const float ShakeRadius = 0.8f;  // m: a camera nudged less than this, still facing the same way, is a shake

    /// <summary>The game has the camera (a cutscene or scripted moment): the headset is following it.</summary>
    public static bool CutsceneActive { get; private set; }

    /// <summary>A cutscene is playing: you keep Henry's body, arms and head, but the stick doesn't walk him.</summary>
    public static bool MovementHeld => CutsceneActive && InGame && !_cinemaActive;

    private static Transform _camOwner;                 // a rig the game put the camera itself on, if it did
    private static Vector3 _ownerLocalPos;
    private static Quaternion _ownerLocalRot = Quaternion.identity;
    private static Vector3 _view, _lastRawView;         // the game camera's position (smoothed, and as it was last frame)
    private static float _lastRawYaw;
    private static float _cutYaw;                       // the play space's turn while following
    private static Vector3 _cutHead;                    // your tracked head (metres) when the scene was lined up
    private static float _homeFor, _blink;
    private static float _hurtUntil;                    // a hit's shake or a death's fall is playing until then
    private static float _shakeUntil;                   // the game is shaking its camera until then

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
        if (Hurting || Shaking)
        {
            // the game shaking its camera (a hit, the Chapter 1 finale's ink flood) or dropping it for a death: not
            // a scene, you stay in Henry
            _written = false;
            return;
        }

        if (HolderAway())
        {
            // moved relative to the holder the game took away (the Chapter 2 wake-up): the holder is what's followed
            _written = false;
            return;
        }

        if (lp.sqrMagnitude < 0.0025f && (Quaternion.Angle(lr, Quaternion.identity) < 1f || Quaternion.Angle(lr, _appliedRot) < 3f))
        {
            // the game putting its camera back to rest (zero, relative to the holder it thinks it's in), as a shake
            // ends: not a scene's view (it's Henry's feet in VR), and any scene it set is over
            if (_written) Plugin.L.LogInfo("Cutscene: the game put its camera back to rest");
            _written = false;
            return;
        }

        if (!_written && (lp - _appliedPos).sqrMagnitude < ShakeRadius * ShakeRadius && Quaternion.Angle(lr, _appliedRot) < 3f)
        {
            // jiggled round where your head put it, still facing the same way: a shake the game didn't announce
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

    // ---------------- hits and deaths ----------------

    private static bool Hurting => Time.unscaledTime < _hurtUntil;

    /// <summary>
    /// An enemy hit Henry (or he died). The game shakes its camera for a hit and drops it to the floor for a death,
    /// which looked like a scene: the view went into the floor (or up near the ceiling) and Henry's body vanished.
    /// For a moment, nothing the game does to its camera is followed. A scene that only just started is that shake.
    /// </summary>
    internal static void NoteHurt(bool died)
    {
        _hurtUntil = Mathf.Max(_hurtUntil, Time.unscaledTime + (died ? DeathFall : HitShake));
        _written = false;
        if (CutsceneActive && _camOwner == null && !HolderAway() && _player != null)
        {
            Plugin.L.LogInfo(died ? "Henry died: not following the game's death camera" : "Henry was hit: not following the game's camera shake");
            EndCutscene();
            _blink = 0f; // it only just started: no blink for a hit
        }
    }

    /// <summary>The game took Henry somewhere else (a respawn): any hit or death before it is over.</summary>
    private static void HurtOver() => _hurtUntil = 0f;

    // ---------------- camera shakes ----------------

    private static bool Shaking => Time.unscaledTime < _shakeUntil;

    /// <summary>
    /// The game started shaking its camera (for this long at most). Its shakes jiggle the camera round where it was
    /// and then put it back to rest, which looked like a scene: in the Chapter 1 finale the view jumped about and
    /// ended up in the floor, under the ink. While it shakes, nothing it does to the camera is followed.
    /// </summary>
    internal static void NoteShake(float seconds)
    {
        if (!Shaking) Plugin.L.LogInfo("The game is shaking its camera: keeping your view steady");
        _shakeUntil = Mathf.Max(_shakeUntil, Time.unscaledTime + seconds);
        _written = false;
        if (CutsceneActive && _camOwner == null && !HolderAway() && _player != null)
        {
            // a scene started from the shake's first jiggles: it was never one
            EndCutscene();
            _blink = 0f;
        }
    }

    /// <summary>The game stopped shaking its camera: its last moves (back to rest) are still ignored for a moment.</summary>
    internal static void ShakeStopped() => _shakeUntil = Mathf.Min(_shakeUntil, Time.unscaledTime + 0.5f);

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
            HurtOver();
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
        _blink = Mathf.MoveTowards(_blink, 0f, dt / BlinkIn);

        if (!GameView(out var pos, out var rot, out var away))
        {
            if (CutsceneActive) EndCutscene();
            return;
        }

        if (!CutsceneActive)
        {
            // (a hit or a death tilting the player or moving the holder isn't a scene either)
            if (away && !Hurting) BeginCutscene(pos, rot);
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
        _homeFor = _hurtUntil = _shakeUntil = 0f;
    }

    /// <summary>The blink of black over cuts, on top of the game's own fades.</summary>
    private static float CutsceneBlink => _blink;
}

/// <summary>The game's hit (the hurt border, which shakes the camera) and Henry's death.</summary>
[HarmonyPatch]
internal static class HurtPatches
{
    [HarmonyPostfix, HarmonyPatch(typeof(HurtBordersController), nameof(HurtBordersController.ShowBorder))]
    private static void Hit() => VRRig.NoteHurt(false);

    [HarmonyPostfix, HarmonyPatch(typeof(GameManager), nameof(GameManager.ShowHurtBorder))]
    private static void HitShown() => VRRig.NoteHurt(false);

    [HarmonyPrefix, HarmonyPatch(typeof(PlayerController), nameof(PlayerController.Die))]
    private static void Died() => VRRig.NoteHurt(true);
}

/// <summary>
/// The game's camera shakes, chapter by chapter. Patched one by one (not by attribute), so a method a game update
/// renames costs only its own shake, never the rest of InkVR's patches.
/// </summary>
internal static class ShakePatches
{
    private const float Burst = 3f;  // s: a one-off shake (each call starts another)
    private const float Long = 30f;  // s: a shake that runs until the game stops it

    internal static void Apply(Harmony harmony)
    {
        Hook(harmony, typeof(CH1BendyFinaleController), "ShakeCamera", nameof(OnBurst));      // Ch1's ink flood
        Hook(harmony, typeof(CH3ClosingSequenceController), "DoShake", nameof(OnLong));         // Ch3's ending
        Hook(harmony, typeof(CH3ClosingSequenceController), "KillShake", nameof(OnStop));
        Hook(harmony, typeof(BruteBorisAi), "ApplyShake", nameof(OnBurst));                     // Ch4's Brute Boris
        Hook(harmony, typeof(BruteBorisAnimationEvents), "ShakeCamera", nameof(OnBurst));
        Hook(harmony, typeof(BeastBendy_Ai), "ApplyShake", nameof(OnBurst));                    // Ch5's Beast
        Hook(harmony, typeof(Ch5BeastBendyChargeController), "ApplyShake", nameof(OnBurst));
        // Not S13Audio's evt_screen_shake_start/stop: they're empty, and the game's build shares one body between
        // every empty method, so hooking them hooked all of those and froze the game at startup.
    }

    private static void Hook(Harmony harmony, System.Type type, string method, string prefix)
    {
        // dev: INKVR_SKIP_SHAKE="Type.Method,..." leaves those unhooked
        var skip = System.Environment.GetEnvironmentVariable("INKVR_SKIP_SHAKE");
        if (skip != null && (skip == "*" || skip.Contains($"{type.Name}.{method}"))) return;
        try
        {
            var target = AccessTools.Method(type, method);
            if (target == null)
            {
                Plugin.L.LogWarning($"Camera shake: {type.Name}.{method} not found (that shake won't be steadied)");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(ShakePatches), prefix)));
            Plugin.L.LogDebug($"Camera shake: hooked {type.Name}.{method}");
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning($"Camera shake: couldn't hook {type.Name}.{method}: {e.Message}");
        }
    }

    private static void OnBurst() => VRRig.NoteShake(Burst);
    private static void OnLong() => VRRig.NoteShake(Long);
    private static void OnStop() => VRRig.ShakeStopped();
}
