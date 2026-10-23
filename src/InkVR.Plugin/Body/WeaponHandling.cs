using HarmonyLib;
using InkVR.Core;
using InkVR.Input;
using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// Motion controls part 2 for what you hold:
///   THROWING: with a throwable (Ch4's Bacon Soup cans) the trigger picks the throw up rather than firing it;
///             swing your arm and let go of the trigger, and the can flies the way your hand was moving, as
///             hard as you threw it (a gentle release lobs it where your hand points).
///   TWO HANDS: hold a gun and grip just in front of it with your other hand: the gun aims along both hands
///             (steadier), and you feel the recoil in both.
///   HOLSTER:  reach over your shoulder with what you're holding and grip: it goes away (the game's own
///             unequip). Reach back there with an empty hand and grip to draw it again.
/// </summary>
[HarmonyPatch]
public static class WeaponHandling
{
    // ---------------- hand motion (for throws) ----------------

    private const int History = 12;
    private static readonly Vector3[,] Pos = new Vector3[2, History];
    private static readonly float[,] When = new float[2, History];
    private static readonly int[] Head = new int[2];

    private static void Track()
    {
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var h = (int)hand;
            var (p, _) = Grip(hand);
            Head[h] = (Head[h] + 1) % History;
            Pos[h, Head[h]] = p;
            When[h, Head[h]] = Time.unscaledTime;
        }
    }

    /// <summary>Dev: pretend the dominant hand moves at this velocity (real m/s, world axes).</summary>
    public static Vector3? DevVelocity;

    /// <summary>The hand's world velocity over the last ~80 ms.</summary>
    public static Vector3 Velocity(Hand hand)
    {
        if (DevVelocity.HasValue && hand == ControllerBindings.Dominant) return DevVelocity.Value * VRRig.ActiveScale;
        var h = (int)hand;
        var now = Head[h];
        for (var back = 1; back < History; back++)
        {
            var i = (now - back + History) % History;
            var dt = When[h, now] - When[h, i];
            if (dt >= 0.08f) return (Pos[h, now] - Pos[h, i]) / dt;
        }

        var oldest = (now + 1) % History;
        var span = When[h, now] - When[h, oldest];
        return span > 1e-4f ? (Pos[h, now] - Pos[h, oldest]) / span : Vector3.zero;
    }

    private static (Vector3 pos, Quaternion rot) Grip(Hand hand)
    {
        var dev = hand == Hand.Right ? Hands.DevRight : Hands.DevLeft;
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, dev?.rot ?? c.GripRotation, out var p, out var r);
        return (p, r);
    }

    private static (Vector3 pos, Quaternion rot) Aim(Hand hand)
    {
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(c.AimPosition, c.AimRotation, out var p, out var r);
        return (p, r);
    }

    private static float GripValue(Hand hand) => (hand == Hand.Right ? XrInput.DevRightGrip : XrInput.DevLeftGrip) ?? XrInput.Get(hand).Grip;

    private static float TriggerValue(Hand hand) => hand == Hand.Right && XrInput.DevRightTrigger.HasValue ? XrInput.DevRightTrigger.Value : XrInput.Get(hand).Trigger;

    public static void Update()
    {
        if (!XrManager.IsRunning) return;
        Track();
        if (!VRRig.InGame || VRRig.CinemaActive)
        {
            _armed = null;
            OffHandOnGun = false;
            return;
        }

        UpdateThrow();
        UpdateTwoHands();
        CheckHolster();
        UpdateHolster();
    }

    private static BaseWeapon Held()
    {
        var go = GameManager.Instance?.Player?.WeaponGameObject;
        return go != null && go.activeInHierarchy ? go.GetComponent<BaseWeapon>() : null;
    }

    // ---------------- throwing ----------------

    private static ThrowWeapon _armed;
    private static bool _releasing;
    private static Vector3 _throwDir;
    private static float _throwGain;

    /// <summary>Dev: the last throw.</summary>
    public static string LastThrow = "";

    [HarmonyPrefix, HarmonyPatch(typeof(ThrowWeapon), nameof(ThrowWeapon.OnAttack))]
    private static bool OnAttack(ThrowWeapon __instance)
    {
        if (_releasing || !ModConfig.PhysicalThrow.Value || !XrManager.IsRunning || !VRRig.InGame) return true;
        // The trigger picks the throw up; letting go of it throws.
        _armed = __instance;
        XrInput.Haptic(ControllerBindings.Dominant, 0.15f, 0.03f);
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ThrowableObject), nameof(ThrowableObject.Throw))]
    private static void Thrown(ThrowableObject __instance)
    {
        var rb = __instance.m_RigidBody;
        Plugin.L.LogInfo($"Thrown {__instance.name} from {__instance.transform.position}: force {__instance.m_ThrowForce}, velocity {(rb != null ? rb.velocity : Vector3.zero)}");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ThrowableObject), nameof(ThrowableObject.Initialize))]
    private static void Initialize(ref Vector3 force)
    {
        if (!_releasing) return;
        force = _throwDir * force.magnitude * _throwGain;
    }

    private static void UpdateThrow()
    {
        if (_armed == null) return;
        var dom = ControllerBindings.Dominant;
        if (Held()?.Pointer != _armed.Pointer)
        {
            _armed = null; // swapped weapons, holstered, cutscene
            return;
        }

        if (TriggerValue(dom) > 0.3f) return;

        // Let go: the can goes the way the hand was moving, as hard as it was moving.
        var v = Velocity(dom);
        var speed = v.magnitude / VRRig.ActiveScale; // real metres per second
        if (speed > 1.1f)
        {
            _throwDir = v.normalized;
            _throwGain = Mathf.Clamp(speed / 3.6f, 0.45f, 1.8f);
        }
        else
        {
            var (_, aim) = Aim(dom);
            _throwDir = (aim * Vector3.forward + Vector3.up * 0.15f).normalized;
            _throwGain = 0.55f;
        }

        var weapon = _armed;
        _armed = null;
        _releasing = true;
        try
        {
            weapon.OnAttack();
        }
        finally
        {
            _releasing = false;
        }

        XrInput.Haptic(dom, 0.4f, 0.06f);
        LastThrow = $"{speed:F1} m/s → gain {_throwGain:F2} along {_throwDir}";
        Plugin.L.LogInfo($"Throw: {LastThrow}");
    }

    // ---------------- two hands on a gun ----------------

    /// <summary>The off hand is holding the front of the gun (its grip isn't a reach for something else).</summary>
    public static bool OffHandOnGun { get; private set; }

    /// <summary>With two hands: the gun's aim (world), from the dominant grip towards the off hand.</summary>
    public static bool TwoHandAim(out Vector3 from, out Quaternion rot)
    {
        from = default;
        rot = default;
        if (!OffHandOnGun) return false;
        var dom = Aim(ControllerBindings.Dominant);
        var off = Grip(ControllerBindings.OffHand);
        var fwd = off.pos - dom.pos;
        if (fwd.sqrMagnitude < 1e-6f) return false;
        from = dom.pos;
        rot = Quaternion.LookRotation(fwd.normalized, dom.rot * Vector3.up);
        return true;
    }

    /// <summary>Is this grip closing on the gun's front, or reaching for the holster? (Then it isn't an interact.)</summary>
    public static bool WantsGrip(Hand hand)
    {
        if (!VRRig.InGame) return false;
        var held = Held();
        if (ModConfig.Holster.Value && hand == ControllerBindings.Dominant && AtShoulder(hand) && (held != null || Holstered)) return true;
        if (!ModConfig.TwoHandedGuns.Value || hand != ControllerBindings.OffHand || held?.TryCast<GunWeapon>() == null) return false;
        var dom = Aim(ControllerBindings.Dominant);
        var front = dom.pos + dom.rot * Vector3.forward * (0.24f * VRRig.ActiveScale);
        return (Grip(hand).pos - front).magnitude < 0.2f * VRRig.ActiveScale;
    }

    private static bool _offWasGripping;

    private static void UpdateTwoHands()
    {
        var gun = Held()?.TryCast<GunWeapon>();
        var off = ControllerBindings.OffHand;
        var gripping = GripValue(off) > 0.6f;
        if (gun == null || !ModConfig.TwoHandedGuns.Value || !gripping)
        {
            if (OffHandOnGun) XrInput.Haptic(off, 0.1f, 0.02f);
            OffHandOnGun = false;
            _offWasGripping = gripping;
            return;
        }

        // Taking hold: the grip has to close near the front of the gun (a little ahead of the dominant hand).
        if (!OffHandOnGun && !_offWasGripping)
        {
            var dom = Aim(ControllerBindings.Dominant);
            var front = dom.pos + dom.rot * Vector3.forward * (0.24f * VRRig.ActiveScale);
            if ((Grip(off).pos - front).magnitude < 0.2f * VRRig.ActiveScale)
            {
                OffHandOnGun = true;
                XrInput.Haptic(off, 0.3f, 0.05f);
            }
        }

        _offWasGripping = gripping;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(GunWeapon), nameof(GunWeapon.OnAttack))]
    private static void Recoil()
    {
        if (!XrManager.IsRunning) return;
        XrInput.Haptic(ControllerBindings.Dominant, 0.55f, 0.04f);
        if (OffHandOnGun) XrInput.Haptic(ControllerBindings.OffHand, 0.35f, 0.04f);
    }

    // ---------------- the holster ----------------

    /// <summary>A weapon put away over the shoulder (the game keeps it as the player's inactive weapon).</summary>
    public static bool Holstered { get; private set; }

    private static bool _domWasGripping;

    /// <summary>Is the hand reaching back over its own shoulder?</summary>
    public static bool AtShoulder(Hand hand)
    {
        var head = VRRig.Head;
        if (head == null) return false;
        var scale = VRRig.ActiveScale;
        var p = Grip(hand).pos;
        var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, fwd);
        var side = hand == Hand.Right ? 1f : -1f;
        var spot = head.position + right * (0.14f * side * scale) - fwd * (0.12f * scale) - Vector3.up * (0.12f * scale);
        return (p - spot).magnitude < 0.2f * scale;
    }

    private static void UpdateHolster()
    {
        var dom = ControllerBindings.Dominant;
        var gripping = GripValue(dom) > 0.6f;
        var pressed = gripping && !_domWasGripping;
        _domWasGripping = gripping;
        if (!ModConfig.Holster.Value || !pressed || !AtShoulder(dom)) return;

        var p = GameManager.Instance?.Player;
        if (p == null) return;
        var held = Held();
        if (held != null)
        {
            // Put away: the weapon leaves your hand (the game's own unequip only lowers it out of a flat view).
            _holstered = held.gameObject;
            _holstered.SetActive(false);
            Holstered = true;
            XrInput.Haptic(dom, 0.45f, 0.08f);
            Plugin.L.LogInfo($"Holster: {_holstered.name} put away over the shoulder");
        }
        else if (Holstered && _holstered != null)
        {
            _holstered.SetActive(true);
            Holstered = false;
            XrInput.Haptic(dom, 0.45f, 0.08f);
            Plugin.L.LogInfo($"Holster: {_holstered.name} drawn");
        }
    }

    private static GameObject _holstered;

    /// <summary>The game swapped what you carry (a new pickup, a chapter change): nothing is in the holster any more.</summary>
    private static void CheckHolster()
    {
        if (!Holstered) return;
        var go = GameManager.Instance?.Player?.WeaponGameObject;
        if (_holstered == null || go == null || go.Pointer != _holstered.Pointer)
        {
            Holstered = false;
            _holstered = null;
        }
    }
}
