using System.Collections.Generic;
using HarmonyLib;
using InkVR.Body;
using InkVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.Input;

/// <summary>
/// The studio's fairground games by hand: the darts in Chapter 1's break room, and Chapter 4's ball toss and shooting
/// gallery. The flat game aims all three with the camera (your head, in VR) and holds the dart, ball or pellet gun up
/// in front of it; here what you hold is in your hand, and your hand aims:
///   the ball toss: point at a bottle and pull the trigger (let go to throw; swing too if you like): the ball leaves
///                  your hand on the arc that lands where you pointed, shown by a gold arc as you aim;
///   the shooting gallery: the pellet gun is in your hand with a gold sight line; the trigger fires where it points;
///   the darts: hold the trigger, swing and let go: the dart flies the way your hand moved.
/// The game does the rest: the bottles, the targets, the score, the reload.
/// </summary>
public static class MinigameThrow
{
    private static readonly List<WinnableMiniGameBaseController> Games = new();
    private static string _scene = "";
    private static WinnableMiniGameBaseController _game;
    private static Quaternion _heldRel = Quaternion.identity;
    private static bool _armed;
    private static float _pendingUntil = -1f;
    private static Vector3 _dir, _from;
    private static float _gain;

    // the ball toss: where the ball's going, and the ball the game is about to launch
    private static Vector3 _target;
    private static bool _haveTarget, _targetFrozen;
    private static float _ballUntil = -1f;
    private static int _ballsBefore;
    private static Vector3 _ballFrom, _ballTo;
    private static LineRenderer _line;

    /// <summary>A throwing game is on and the dominant trigger is the throw (the game doesn't see it).</summary>
    public static bool Owns { get; private set; }

    /// <summary>The shooting gallery is on: its shots go where your hand points (see <see cref="GalleryAim"/>).</summary>
    internal static Minigame_ShootingGallery Gallery { get; private set; }

    /// <summary>Dev: the last throw.</summary>
    public static string Last = "";

    public static void Update()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != _scene)
        {
            // the games in this scene, found once (a search like this costs too much mid-play)
            _scene = scene;
            Games.Clear();
            foreach (var o in Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<WinnableMiniGameBaseController>()))
            {
                var g = o.Cast<WinnableMiniGameBaseController>();
                if (g.TryCast<Minigame_Darts>() != null || g.TryCast<Minigame_BallToss>() != null || g.TryCast<Minigame_ShootingGallery>() != null) Games.Add(g);
            }

            _game = null;
        }

        WinnableMiniGameBaseController active = null;
        if (ModConfig.PhysicalThrow.Value && XrManager.IsRunning && VRRig.InGame && !VRRig.CinemaActive)
            foreach (var g in Games)
                if (g != null && g.CurrentState == WinnableMiniGameBaseController.MiniGameState.ACTIVE)
                    active = g;

        if (active == null)
        {
            Owns = false;
            Gallery = null;
            _armed = false;
            _game = null;
            ShowLine(false);
            return;
        }

        var gallery = active.TryCast<Minigame_ShootingGallery>();
        var toss = active.TryCast<Minigame_BallToss>();
        if (_game == null || _game.Pointer != active.Pointer)
        {
            // how the game turns what it holds against its view: the same against your hand
            _game = active;
            var held0 = active.m_HeldItemPositionTransform;
            var look0 = active.m_LocalLookTransform;
            _heldRel = held0 != null && look0 != null ? Quaternion.Inverse(look0.rotation) * held0.rotation : Quaternion.identity;
            _armed = false;
            Plugin.L.LogInfo(gallery != null
                ? $"Shooting game: {active.name} by hand (point the pellet gun, pull the trigger)"
                : toss != null
                    ? $"Throwing game: {active.name} by hand (point at a bottle, pull and let go of the trigger)"
                    : $"Throwing game: {active.name} by hand (hold the trigger, swing, let go)");
        }

        var dom = ControllerBindings.Dominant;
        var (grip, rot) = Grip(dom);
        var aim = Aim(dom);
        var palm = grip + rot * new Vector3(dom == Hand.Right ? -0.03f : 0.03f, -0.01f, 0.05f) * VRRig.ActiveScale;

        // what the game holds up, in your hand
        var held = active.m_HeldItemPositionTransform;
        if (held != null) held.SetPositionAndRotation(palm, aim * _heldRel);

        if (gallery != null)
        {
            // the trigger is the game's own fire button; only the aim is yours
            Owns = false;
            Gallery = gallery;
            Sight(palm, aim);
            if (DevAuto) DevGallery(gallery);
            return;
        }

        Gallery = null;
        Owns = true;
        if (toss != null)
        {
            Toss(toss, dom, palm, aim);
            return;
        }

        ShowLine(false);
        var trigger = TriggerValue(dom);
        if (!_armed && trigger > 0.6f)
        {
            _armed = true;
            XrInput.Haptic(dom, 0.15f, 0.03f);
        }
        else if (_armed && trigger < 0.3f)
        {
            _armed = false;
            Throw(dom, palm, aim);
        }
    }

    // ---------------- the darts ----------------

    private static void Throw(Hand hand, Vector3 palm, Quaternion aim)
    {
        var v = WeaponHandling.Velocity(hand);
        var speed = v.magnitude / VRRig.ActiveScale; // real metres per second
        if (speed > 1.1f)
        {
            _dir = v.normalized;
            _gain = Mathf.Clamp(speed / 4f, 0.4f, 1.5f);
        }
        else
        {
            _dir = (aim * Vector3.forward + Vector3.up * 0.1f).normalized;
            _gain = 0.6f;
        }

        _from = palm;
        _pendingUntil = Time.unscaledTime + 0.35f;
        VirtualInput.Tap(GameAction.Attack); // the game throws on its own attack: the next frame
        XrInput.Haptic(hand, 0.4f, 0.06f);
        Last = $"{speed:F1} m/s, gain {_gain:F2}, along {_dir}";
        Plugin.L.LogInfo($"Throwing game: threw ({Last})");
    }

    /// <summary>The game is launching a thrown object: if it's the one just thrown by hand, it leaves the hand the way the hand went.</summary>
    internal static bool TakeThrow(ThrowableObject thrown, ref Vector3 force)
    {
        if (Time.unscaledTime > _pendingUntil) return false;
        _pendingUntil = -1f;
        force = _dir * force.magnitude * _gain;
        thrown.transform.SetPositionAndRotation(_from, Quaternion.LookRotation(_dir));
        return true;
    }

    // ---------------- the ball toss ----------------

    private static void Toss(Minigame_BallToss toss, Hand hand, Vector3 palm, Quaternion aim)
    {
        var k = VRRig.ActiveScale;

        // the ball the game has just launched (along your head's view): from your hand, on the arc to your target
        if (_ballUntil > 0)
        {
            var balls = toss.m_Balls;
            if (balls != null && balls.Count > _ballsBefore)
            {
                var ball = balls[balls.Count - 1];
                _ballUntil = -1f;
                if (ball != null)
                {
                    ball.position = _ballFrom;
                    ball.transform.position = _ballFrom;
                    ball.velocity = Launch(_ballFrom, _ballTo, Gravity(ball));
                    ball.angularVelocity = Vector3.zero;
                }
            }
            else if (Time.unscaledTime > _ballUntil) _ballUntil = -1f;
        }

        // where you're pointing (until a swing starts: then the throw goes where you pointed as you began it)
        var speed = WeaponHandling.Velocity(hand).magnitude / k;
        if (!(_armed && _targetFrozen))
        {
            _haveTarget = Bottle(toss, palm, aim * Vector3.forward, out _target) || Target(palm, aim * Vector3.forward, out _target);
            if (_armed && speed > 1.2f) _targetFrozen = true;
        }

        var reloading = toss.m_Reloading || toss.m_BallCount >= toss.m_BallLimit;
        if (_haveTarget && !reloading) Arc(palm, _target, toss);
        else ShowLine(false);

        var trigger = TriggerValue(hand);
        if (!_armed && trigger > 0.6f)
        {
            _armed = true;
            _targetFrozen = false;
            XrInput.Haptic(hand, 0.15f, 0.03f);
        }
        else if (_armed && trigger < 0.3f)
        {
            _armed = false;
            _targetFrozen = false;
            if (!_haveTarget || reloading) return;
            _ballsBefore = toss.m_Balls != null ? toss.m_Balls.Count : 0;
            _ballFrom = palm;
            _ballTo = _target;
            _ballUntil = Time.unscaledTime + 0.5f;
            VirtualInput.Tap(GameAction.Attack); // the game launches a ball; the next frames it's taken from your hand
            XrInput.Haptic(hand, 0.4f, 0.06f);
            Last = $"ball at {(_target - palm).magnitude / k:F1} m ({speed:F1} m/s hand)";
            Plugin.L.LogInfo($"Throwing game: threw a ball to where you pointed, {Last}");
        }
    }

    /// <summary>
    /// The bottle nearest where the hand points (still standing, within a few degrees): the ball goes for the middle of
    /// it, so a throw that's nearly right is right. (A bottle scores when it's knocked off its stand.)
    /// </summary>
    private static bool Bottle(Minigame_BallToss toss, Vector3 from, Vector3 dir, out Vector3 at)
    {
        at = default;
        var bottles = toss.m_Bottles;
        if (bottles == null) return false;
        var best = 12f;
        for (var i = 0; i < bottles.Length; i++)
        {
            var b = bottles[i];
            if (b == null || b.IsHit) continue;
            var col = b.GetComponentInChildren<Collider>();
            var c = col != null ? col.bounds.center : b.transform.position + Vector3.up * (0.15f * VRRig.ActiveScale);
            var angle = Vector3.Angle(dir, c - from);
            if (angle >= best) continue;
            best = angle;
            at = c;
        }

        return best < 12f;
    }

    /// <summary>What the hand points at (the bottles, their stands, the booth): the first thing the aim meets.</summary>
    private static bool Target(Vector3 from, Vector3 dir, out Vector3 at)
    {
        var k = VRRig.ActiveScale;
        var player = GameManager.Instance?.Player;
        var mask = Physics.DefaultRaycastLayers & ~UI.VRLayers.UIMask & ~(player != null ? 1 << player.gameObject.layer : 0);
        if (Physics.Raycast(from, dir, out var hit, 12f * k, mask, QueryTriggerInteraction.Ignore))
        {
            at = hit.point;
            return true;
        }

        at = from + dir * 6f * k;
        return true;
    }

    /// <summary>How hard the ball falls: the world's gravity, made heavier by its own modifier (three times, in Chapter 4).</summary>
    private static float Gravity(Rigidbody ball)
    {
        var gm = ball != null ? ball.GetComponent<GravityModifier>() : null;
        var g = Physics.gravity.magnitude;
        return gm != null && gm.modifier > 0 ? g * gm.modifier : g;
    }

    private static float _tossGravity = 29.4f;

    /// <summary>The velocity that carries a ball from <paramref name="from"/> to <paramref name="to"/> under gravity <paramref name="g"/>, briskly.</summary>
    private static Vector3 Launch(Vector3 from, Vector3 to, float g)
    {
        var k = VRRig.ActiveScale;
        var d = to - from;
        var flat = new Vector3(d.x, 0, d.z).magnitude;
        var t = Mathf.Clamp(flat / (9f * k), 0.2f, 1.2f); // about 9 m/s across the ground: brisk and flat, hard enough to knock a bottle off
        return d / t + Vector3.up * (0.5f * g * t);
    }

    /// <summary>The arc a ball will fly, drawn from your hand to where you point.</summary>
    private static void Arc(Vector3 from, Vector3 to, Minigame_BallToss toss)
    {
        if (toss.m_BallObject != null) _tossGravity = Gravity(toss.m_BallObject);
        var v = Launch(from, to, _tossGravity);
        var d = to - from;
        var flat = new Vector3(d.x, 0, d.z).magnitude;
        var t = Mathf.Clamp(flat / (9f * VRRig.ActiveScale), 0.2f, 1.2f);
        var line = Line();
        const int n = 24;
        line.positionCount = n + 1;
        for (var i = 0; i <= n; i++)
        {
            var s = t * i / n;
            line.SetPosition(i, from + v * s + Vector3.down * (0.5f * _tossGravity * s * s));
        }

        line.startWidth = 0.006f * VRRig.ActiveScale;
        line.endWidth = 0.012f * VRRig.ActiveScale;
        ShowLine(true);
    }

    // ---------------- the shooting gallery ----------------

    /// <summary>A gold sight line from the pellet gun to what it points at.</summary>
    private static void Sight(Vector3 palm, Quaternion aim)
    {
        var dir = aim * Vector3.forward;
        Target(palm, dir, out var at);
        var line = Line();
        line.positionCount = 2;
        line.SetPosition(0, palm + dir * (0.12f * VRRig.ActiveScale));
        line.SetPosition(1, at);
        line.startWidth = 0.003f * VRRig.ActiveScale;
        line.endWidth = 0.002f * VRRig.ActiveScale;
        ShowLine(true);
    }

    /// <summary>Where the gallery's shots come from and go: your hand, the way it points (world).</summary>
    internal static (Vector3 pos, Quaternion rot) GunAim()
    {
        var dom = ControllerBindings.Dominant;
        return (Grip(dom).pos, Aim(dom));
    }

    // ---------------- dev: a player that aims and fires (the mock runtime has no hands) ----------------

    /// <summary>Dev: the right hand plays the gallery itself: it points at whichever good target is up and fires.</summary>
    public static bool DevAuto;

    private static float _devNext;

    private static void DevGallery(Minigame_ShootingGallery sg)
    {
        var o = VRRig.ActiveOrigin;
        if (o == null) return;
        CH4FairGameTarget up = null;
        foreach (var t in sg.GetComponentsInChildren<CH4FairGameTarget>())
            if (!t.IsBad && t.HitCollider != null && t.HitCollider.enabled)
                up = t;
        var now = Time.unscaledTime;
        if (up == null)
        {
            XrInput.DevRightTrigger = 0f;
            return;
        }

        var local = o.InverseTransformPoint(up.HitCollider.bounds.center) - Vector3.up * VRRig.HeightLift;
        var from = new Vector3(0.2f, 1.25f, 0.3f);
        Hands.DevRight = (from, Quaternion.LookRotation(local - from));
        if (now < _devNext) return;
        // a pull, then a let go, every few tenths of a second
        var pulled = XrInput.DevRightTrigger is > 0.5f;
        XrInput.DevRightTrigger = pulled ? 0f : 1f;
        _devNext = now + (pulled ? 0.25f : 0.1f);
    }

    // ---------------- drawing ----------------

    private static LineRenderer Line()
    {
        if (_line != null) return _line;
        var go = new GameObject("InkVR_GameAim");
        Object.DontDestroyOnLoad(go);
        _line = go.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.material = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3100 };
        _line.startColor = new Color(0.98f, 0.86f, 0.55f, 0.35f);
        _line.endColor = new Color(0.98f, 0.86f, 0.55f, 0.85f);
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        go.SetActive(false);
        return _line;
    }

    private static void ShowLine(bool on)
    {
        if (_line == null) return;
        if (_line.gameObject.activeSelf != on) _line.gameObject.SetActive(on);
    }

    // ---------------- hands ----------------

    private static (Vector3 pos, Quaternion rot) Grip(Hand hand)
    {
        var dev = hand == Hand.Right ? Hands.DevRight : Hands.DevLeft;
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, dev?.rot ?? c.GripRotation, out var p, out var r);
        return (p, r);
    }

    private static Quaternion Aim(Hand hand)
    {
        var dev = hand == Hand.Right ? Hands.DevRight : Hands.DevLeft;
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(c.AimPosition, dev?.rot ?? c.AimRotation, out _, out var r);
        return r;
    }

    private static float TriggerValue(Hand hand) => hand == Hand.Right && XrInput.DevRightTrigger.HasValue ? XrInput.DevRightTrigger.Value : XrInput.Get(hand).Trigger;
}

/// <summary>
/// The shooting gallery fires from the game's camera, the way it faces: your head, in VR. For the moment the game
/// runs its frame, the camera is where the pellet gun in your hand is, facing the way it points, so its shots go where
/// you aim with your hand; then it's put straight back (you never see it move).
/// </summary>
[HarmonyPatch(typeof(Minigame_ShootingGallery), nameof(Minigame_ShootingGallery.Update))]
internal static class GalleryAim
{
    private static Transform _cam;
    private static Vector3 _pos;
    private static Quaternion _rot;

    [HarmonyPrefix]
    private static void Prefix(Minigame_ShootingGallery __instance)
    {
        _cam = null;
        if (MinigameThrow.Gallery == null || MinigameThrow.Gallery.Pointer != __instance.Pointer) return;
        var cam = Camera.main;
        if (cam == null) return;
        _cam = cam.transform;
        _pos = _cam.position;
        _rot = _cam.rotation;
        var (pos, rot) = MinigameThrow.GunAim();
        _cam.SetPositionAndRotation(pos, rot);
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_cam == null) return;
        _cam.SetPositionAndRotation(_pos, _rot);
        _cam = null;
    }
}
