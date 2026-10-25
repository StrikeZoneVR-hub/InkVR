using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InkVR.Body;
using InkVR.Core;
using InkVR.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.Input;

/// <summary>
/// Pick up the studio's loose props (the chairs, boxes, buckets, cans, bottles) and throw them. Close a grip with your
/// hand on one and it comes with you, held as you took it; let go and it drops, or flies the way your hand was
/// moving if you threw it, tumbling and clattering where it lands. It stays where it ends up.
///
/// The game built its props as fixed scenery (most merged into the scene's static mesh), so a prop you take hold of
/// is swapped for a physical copy of itself, with a box-shaped body, and the original is hidden and stops blocking.
/// Nothing that's part of a puzzle, a door, a weapon or the building itself is ever picked up.
/// </summary>
public static class PropGrab
{
    private const float Reach = 0.075f;         // metres around the palm
    private const float MinSize = 0.1f, MaxSize = 1.6f; // metres, the prop's longest side (the studio's chairs are 1.4 m)
    private const float MaxVolume = 1.6f;       // cubic metres of its box

    private sealed class Prop
    {
        public GameObject Body;
        public Rigidbody Rb;
        public BoxCollider Box;
        public string Name;
    }

    private sealed class Holding
    {
        public Prop Prop;
        public Vector3 Offset;
        public Quaternion Rot;
        public Quaternion LastHand;
        public Vector3 Spin;
    }

    private static readonly Holding[] Held = new Holding[2];
    private static readonly List<Prop> Props = new();
    private static Il2CppReferenceArray<Collider> _overlap;
    private static string _scene = "";

    /// <summary>Dev: the last grab or throw.</summary>
    public static string Last = "";

    public static bool IsHolding(Hand hand) => Held[(int)hand] != null;

    /// <summary>Dev: what's around the right palm (within a radius, real metres) and whether each one could be picked up.</summary>
    public static string DevProbe(float radius)
    {
        var player = GameManager.Instance?.Player;
        if (player == null) return "no player";
        var (grip, rot) = HandGrip(Hand.Right);
        var palm = Palm(Hand.Right, grip, rot);
        var hits = Physics.OverlapSphere(palm, radius * VRRig.ActiveScale, Physics.DefaultRaycastLayers & ~(1 << player.gameObject.layer) & ~VRLayers.UIMask, QueryTriggerInteraction.Ignore);
        var sb = new System.Text.StringBuilder($"palm {palm}, {hits.Length} collider(s)");
        foreach (var c in hits)
        {
            var d = (c.bounds.ClosestPoint(palm) - palm).magnitude / VRRig.ActiveScale;
            var root = PropRoot(c, out var why);
            sb.Append($"\n  {c.name} ({c.GetIl2CppType().Name}, parent {(c.transform.parent != null ? c.transform.parent.name : "-")}) {d:F2} m -> {(root != null ? "PROP " + root.name : "no: " + why)}");
        }

        return sb.ToString();
    }

    // ---------------- taking hold ----------------

    /// <summary>A grip just closed: is there a prop under this hand? Then it's picked up (and the grip isn't an interact).</summary>
    public static bool TryGrab(Hand hand)
    {
        if (!ModConfig.GrabProps.Value || Held[(int)hand] != null) return false;
        var player = GameManager.Instance?.Player;
        if (player == null) return false;
        var k = VRRig.ActiveScale;
        var (grip, rot) = HandGrip(hand);
        var palm = Palm(hand, grip, rot);
        _overlap ??= new Il2CppReferenceArray<Collider>(24);
        var mask = Physics.DefaultRaycastLayers & ~(1 << player.gameObject.layer) & ~VRLayers.UIMask;
        var n = Physics.OverlapSphereNonAlloc(palm, Reach * k, _overlap, mask, QueryTriggerInteraction.Ignore);
        Prop best = null;
        Transform bestRoot = null;
        var bestDist = float.MaxValue;
        for (var i = 0; i < n; i++)
        {
            var col = _overlap[i];
            if (col == null || col.transform.IsChildOf(player.transform)) continue;
            var d = (col.bounds.ClosestPoint(palm) - palm).magnitude;
            if (d >= bestDist) continue;
            var ours = Ours(col);
            if (ours != null)
            {
                best = ours;
                bestRoot = null;
                bestDist = d;
                continue;
            }

            var root = PropRoot(col);
            if (root == null) continue;
            best = null;
            bestRoot = root;
            bestDist = d;
        }

        if (best == null && bestRoot != null) best = Free(bestRoot, player);
        if (best == null) return false;
        if (Held[1 - (int)hand]?.Prop == best) Held[1 - (int)hand] = null; // taken from the other hand

        best.Rb.isKinematic = true;
        best.Rb.interpolation = RigidbodyInterpolation.None; // moved by the hand each frame
        var t = best.Body.transform;
        Held[(int)hand] = new Holding { Prop = best, Offset = Quaternion.Inverse(rot) * (t.position - grip), Rot = Quaternion.Inverse(rot) * t.rotation, LastHand = rot };
        XrInput.Haptic(hand, 0.35f, 0.05f);
        Last = $"took the {best.Name} in the {hand} hand";
        Plugin.L.LogInfo($"Prop: {Last}");
        return true;
    }

    private static Prop Ours(Collider col)
    {
        foreach (var p in Props)
            if (p.Box != null && p.Box.Pointer == col.Pointer)
                return p;
        return null;
    }

    /// <summary>
    /// The prop this collider belongs to, if it's a loose one: from the level's props (or chairs), a sensible size,
    /// and nothing anyone needs to stay put (no interaction, no puzzle, no door, no weapon, nothing breakable).
    /// </summary>
    private static Transform PropRoot(Collider col) => PropRoot(col, out _);

    private static Transform PropRoot(Collider col, out string why)
    {
        why = "physics";
        if (col.attachedRigidbody != null) return null; // the game's own physics objects are left to the game
        var t = col.transform;
        // "Prop_WoodChair_Static/Chair/AiCollider": the prop is the "Prop_" object holding its model and colliders
        var root = t;
        var up = 0;
        for (var p = t; p != null && up < 3; p = p.parent, up++)
            if (p.name.StartsWith("Prop_") && p.childCount <= 4)
            {
                root = p;
                break;
            }
        var loose = false;
        for (var p = root; p != null; p = p.parent)
        {
            var n = p.name.ToLowerInvariant();
            if (n.Contains("door") || n.Contains("wall") || n.Contains("floor") || n.Contains("stair") || n.Contains("window") || n.Contains("pipe") ||
                n.Contains("railing") || n.Contains("shelf") || n.Contains("shelv") || n.Contains("machine") || n.Contains("puzzle") || n.Contains("controller"))
            {
                why = "part of " + p.name;
                return null;
            }
            if (n == "props" || n == "chairs" || n.StartsWith("prop_") || n == "clutter") loose = true;
        }

        why = "not a prop";
        if (!loose) return null;
        why = "interactable";
        if (root.GetComponentInParent<Interactable>() != null || root.GetComponentInChildren<Interactable>() != null) return null;
        why = "weapon";
        if (root.GetComponentInParent<BaseWeapon>() != null) return null;
        why = "hittable";
        if (Hittable.InOrUnder(root)) return null;
        why = "physics";
        if (root.GetComponentInChildren<Rigidbody>() != null) return null;

        // its size, from what's drawn of it
        var any = false;
        Bounds b = default;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) b = r.bounds;
            else b.Encapsulate(r.bounds);
            any = true;
        }

        why = "not drawn";
        if (!any) return null;
        var size = b.size / VRRig.ActiveScale;
        var longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        why = $"size {size.x:F2} x {size.y:F2} x {size.z:F2} m";
        if (longest < MinSize || longest > MaxSize || size.x * size.y * size.z > MaxVolume) return null;
        return root;
    }

    /// <summary>Swap the prop for a physical copy of itself: its look, a box body around it, weight to match.</summary>
    private static Prop Free(Transform root, PlayerController player)
    {
        var body = new GameObject("InkVR_Prop_" + root.name);
        body.layer = root.gameObject.layer;
        body.transform.SetPositionAndRotation(root.position, root.rotation);
        var hidden = new List<Renderer>();
        if (!MeshCopy.AddAll(root, body.transform, false, out var b, hidden))
        {
            Object.Destroy(body);
            return null;
        }

        // the original stops blocking where it stood
        foreach (var c in root.GetComponentsInChildren<Collider>()) c.enabled = false;

        var box = body.AddComponent<BoxCollider>();
        box.center = body.transform.InverseTransformPoint(b.center);
        var local = body.transform.InverseTransformVector(b.size);
        box.size = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z)) * 0.92f;
        var rb = body.AddComponent<Rigidbody>();
        var size = b.size / VRRig.ActiveScale;
        rb.mass = Mathf.Clamp(size.x * size.y * size.z * 60f, 0.5f, 12f);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.drag = 0.05f;
        rb.angularDrag = 0.3f;
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) Physics.IgnoreCollision(box, cc, true);

        var prop = new Prop { Body = body, Rb = rb, Box = box, Name = root.name.Replace("Prop_", "").Replace("_Static", "").ToLowerInvariant() };
        Props.Add(prop);
        Plugin.L.LogInfo($"Prop: freed '{root.name}' ({size.x:F2} x {size.y:F2} x {size.z:F2} m, {rb.mass:F1} kg, {hidden.Count} part(s){(hidden.Count > 0 && hidden[0].isPartOfStaticBatch ? ", from the static batch" : "")})");
        return prop;
    }

    // ---------------- holding and throwing ----------------

    public static void Update()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != _scene)
        {
            _scene = scene;
            Props.Clear(); // they went with their scene
            Held[0] = Held[1] = null;
        }

        for (var i = Props.Count - 1; i >= 0; i--)
            if (Props[i].Body == null)
                Props.RemoveAt(i);

        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var h = Held[(int)hand];
            if (h == null) continue;
            if (h.Prop.Body == null || !VRRig.InGame || VRRig.CinemaActive)
            {
                Held[(int)hand] = null;
                continue;
            }

            var (grip, rot) = HandGrip(hand);
            if (Grip(hand) < 0.35f)
            {
                Release(hand, h);
                continue;
            }

            var dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            (rot * Quaternion.Inverse(h.LastHand)).ToAngleAxis(out var angle, out var axis);
            if (angle > 180f) angle -= 360f;
            h.Spin = Vector3.Lerp(h.Spin, axis * (angle * Mathf.Deg2Rad / dt), 0.5f);
            h.LastHand = rot;
            h.Prop.Body.transform.SetPositionAndRotation(grip + rot * h.Offset, rot * h.Rot);
        }
    }

    private static void Release(Hand hand, Holding h)
    {
        Held[(int)hand] = null;
        var rb = h.Prop.Rb;
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var v = WeaponHandling.Velocity(hand);
        var speed = v.magnitude / VRRig.ActiveScale;
        // a real throw carries a little extra, so a chair you hurl goes where you meant it to
        rb.velocity = speed > 1.2f ? v * 1.25f : v;
        rb.angularVelocity = Vector3.ClampMagnitude(h.Spin, 14f);
        rb.WakeUp();
        XrInput.Haptic(hand, speed > 1.2f ? 0.45f : 0.2f, 0.05f);
        Last = speed > 1.2f ? $"threw the {h.Prop.Name} at {speed:F1} m/s" : $"put the {h.Prop.Name} down";
        Plugin.L.LogInfo($"Prop: {Last}");
    }

    private static float Grip(Hand hand) => (hand == Hand.Right ? XrInput.DevRightGrip : XrInput.DevLeftGrip) ?? XrInput.Get(hand).Grip;

    private static (Vector3 pos, Quaternion rot) HandGrip(Hand hand)
    {
        var dev = hand == Hand.Right ? Hands.DevRight : Hands.DevLeft;
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, dev?.rot ?? c.GripRotation, out var p, out var r);
        return (p, r);
    }

    private static Vector3 Palm(Hand hand, Vector3 grip, Quaternion rot) =>
        grip + rot * new Vector3(hand == Hand.Right ? -0.03f : 0.03f, -0.01f, 0.04f) * VRRig.ActiveScale;
}
