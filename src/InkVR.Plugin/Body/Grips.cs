using InkVR.Core;
using InkVR.Input;
using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// Natural grips (0.10.0): what you hold sits in your hand by its handle, the way a hand really holds it, and your
/// fingers close round it to its thickness.
///
/// The hand's grip channel: each hand is built round the controller's handle, the line through the closed fist along
/// the grip pose's +Z (towards the index finger and thumb), through the grip pose's origin. A tool or weapon is held
/// with its handle on that line: the knob end a few centimetres past the little finger, the head beyond the thumb
/// (a hammer grip), turned about the handle the way the game holds it. A gun keeps its own aim, with its grip in your
/// hand. Your other hand on the axe's handle or the gun's barrel is put on it, where you took hold, and its arm follows.
/// Things you carry (a gear, a battery, a can) sit in the palm: the long way through your fist, a flat one against it.
/// </summary>
public static class Grips
{
    /// <summary>A held weapon's shape, from its model's bounds (local to the model).</summary>
    private sealed class Shape
    {
        public Transform Model;
        public Vector3 AxisLocal, NearLocal, FarLocal; // the handle's length: knob end → head (model local)
        public float LengthM, RadiusM;                  // metres
        public bool IsGun;
        // at rest, relative to the game's HandContainer
        public Vector3 AxisRel, GripRel;
        public bool HaveRest;
    }

    private static Shape _held;
    private static System.IntPtr _heldWeapon;

    // per hand: a pose for the hand model (world), set when the hand is put on something
    private static readonly (Vector3 pos, Quaternion rot)?[] Override = new (Vector3, Quaternion)?[2];
    private static readonly float[] CurlLimit = { 1f, 1f };
    private static readonly float[] HeldRadius = { 0f, 0f };

    /// <summary>The hand model is put here instead of on the controller this frame (world), when it holds something by a handle.</summary>
    public static bool HandPose(Hand hand, out Vector3 pos, out Quaternion rot)
    {
        var o = Override[(int)hand];
        pos = o?.pos ?? default;
        rot = o?.rot ?? default;
        return o.HasValue;
    }

    /// <summary>How far this hand's fingers may close (they stop on what they hold), 0..1.</summary>
    public static float MaxCurl(Hand hand) => CurlLimit[(int)hand];

    /// <summary>Holding something of this radius (metres) in this hand (0 = nothing): the fingers close round it.</summary>
    public static void Holding(Hand hand, float radiusMetres)
    {
        HeldRadius[(int)hand] = radiusMetres;
    }

    /// <summary>How closed a hand is round a handle or object of this radius (metres): a fist round a thin handle, open round a can.</summary>
    public static float CurlFor(float radius) => radius <= 0 ? 1f : Mathf.Clamp(1.12f - (radius - 0.013f) * 9f, 0.38f, 1f);

    public static void BeginFrame()
    {
        Override[0] = Override[1] = null;
        for (var h = 0; h < 2; h++) CurlLimit[h] = HeldRadius[h] > 0 ? CurlFor(HeldRadius[h]) : 1f;
    }

    // ---------------- the weapon in your hand ----------------

    /// <summary>
    /// Place the game's HandContainer so the held weapon's handle runs through the dominant hand's fist (or along both
    /// hands, with two on it). `rot` comes in as the game-style placement (the mount on the grip, turned like the aim)
    /// and is turned the least it can be to put the handle on the line, so the blade still faces the way the game holds it.
    /// </summary>
    public static bool PlaceWeapon(Transform container, BaseWeapon weapon, Vector3 gripWorld, Quaternion gripRotWorld, Quaternion rot, Vector3? twoHandDir)
    {
        if (!ModConfig.NaturalGrips.Value || weapon == null) return false;
        if (weapon.Pointer != _heldWeapon)
        {
            _heldWeapon = weapon.Pointer;
            _held = Analyse(weapon);
        }

        var s = _held;
        if (s == null || s.Model == null || s.IsGun) return false;
        // Where the weapon is under the container. The game bobs it gently as you stand and walk: followed every frame,
        // the handle stays put in your fist. Only the game's own button swing (the attack button) is let through, from
        // where it was when it started.
        if (!s.HaveRest || !MeleeSwing.GameSwinging)
        {
            s.AxisRel = Quaternion.Inverse(container.rotation) * s.Model.TransformDirection(s.AxisLocal).normalized;
            s.GripRel = container.InverseTransformPoint(GripPointWorld(s));
            s.HaveRest = true;
        }

        var scale = VRRig.ActiveScale;
        var wanted = twoHandDir ?? gripRotWorld * Vector3.forward;
        var d0 = rot * s.AxisRel;
        var fixedRot = Quaternion.FromToRotation(d0, wanted) * rot;
        container.rotation = fixedRot;
        container.position = gripWorld - fixedRot * Vector3.Scale(s.GripRel, container.lossyScale);

        var dom = ControllerBindings.Dominant;
        if (twoHandDir.HasValue)
        {
            // your weapon hand turns to the handle too
            var up = Vector3.ProjectOnPlane(gripRotWorld * Vector3.up, wanted);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.up, wanted);
            Override[(int)dom] = (gripWorld, Quaternion.LookRotation(wanted, up.normalized));
        }

        _ = scale;
        return true;
    }

    /// <summary>Where the dominant hand's fist closes on the handle: a little up from the knob end (model local → world).</summary>
    private static Vector3 GripPointWorld(Shape s)
    {
        var near = s.Model.TransformPoint(s.NearLocal);
        var far = s.Model.TransformPoint(s.FarLocal);
        var len = (far - near).magnitude;
        if (len < 1e-5f) return near;
        // the knob a few centimetres past the little finger; a short tool is held at its middle
        var fromKnob = s.LengthM < 0.25f ? 0.42f : Mathf.Clamp(0.065f / s.LengthM, 0.08f, 0.3f);
        return near + (far - near) * fromKnob;
    }

    /// <summary>A weapon's handle from its biggest model's bounds: the longest way through it, knob end nearest the hand mount.</summary>
    private static Shape Analyse(BaseWeapon weapon)
    {
        MeshFilter best = null;
        var bestSize = 0f;
        foreach (var mf in weapon.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var r = mf.GetComponent<MeshRenderer>();
            if (r == null) continue;
            var size = Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale).magnitude;
            if (size <= bestSize) continue;
            bestSize = size;
            best = mf;
        }

        if (best == null) return null;
        var b = best.sharedMesh.bounds;
        var e = b.extents;
        var axis = e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward;
        var half = Vector3.Scale(e, axis);
        var a = b.center + half;
        var z = b.center - half;
        var container = GameManager.Instance?.Player?.m_HandContainer;
        var mount = container != null && container.childCount > 0 ? container.GetChild(0).position : weapon.transform.position;
        var hand = best.transform.InverseTransformPoint(mount);
        var near = (a - hand).sqrMagnitude < (z - hand).sqrMagnitude ? a : z;
        var far = near == a ? z : a;
        var scale = VRRig.ActiveScale;
        var lossy = best.transform.lossyScale;
        var lengthM = Vector3.Scale(far - near, lossy).magnitude / Mathf.Max(1e-4f, scale);
        // the handle's thickness: the smaller of the other two extents near the knob is unknowable from bounds, so a
        // tool's handle is taken as a typical 1.6 cm (a fist round it); a gun is held by its own grip
        var isGun = weapon.TryCast<GunWeapon>() != null;
        var s = new Shape { Model = best.transform, AxisLocal = (far - near).normalized, NearLocal = near, FarLocal = far, LengthM = lengthM, RadiusM = isGun ? 0.016f : 0.016f, IsGun = isGun };
        Plugin.L.LogInfo($"Natural grip: {weapon.name} ({best.name}), {lengthM:F2} m long{(isGun ? ", a gun (held by its own grip, aimed with your hand)" : ", held by its handle through your fist")}");
        return s;
    }

    /// <summary>The held weapon's main model and its length (knob end → far end, world), for the other hand.</summary>
    public static bool WeaponLine(out Vector3 near, out Vector3 far)
    {
        near = far = default;
        var s = _held;
        if (s == null || s.Model == null || !s.Model.gameObject.activeInHierarchy) return false;
        near = s.Model.TransformPoint(s.NearLocal);
        far = s.Model.TransformPoint(s.FarLocal);
        return true;
    }

    public static bool HeldIsGun => _held != null && _held.IsGun;

    /// <summary>
    /// Your other hand holding the axe's handle or the gun's barrel: put it on the line where you took hold (kept
    /// clear of the weapon hand), turned along it the way your controller is turned about it.
    /// </summary>
    public static void PlaceOffHand(Hand off, Vector3 controllerPos, Quaternion controllerRot, Vector3 near, Vector3 far, Vector3 domGrip, bool gun)
    {
        if (!ModConfig.NaturalGrips.Value) return;
        var line = far - near;
        var len = line.magnitude;
        if (len < 1e-5f) return;
        var dir = line / len;
        var t = Vector3.Dot(controllerPos - near, dir);
        var scale = VRRig.ActiveScale;
        var lo = gun ? len * 0.32f : -0.06f * scale;
        var hi = len * (gun ? 0.95f : 0.85f);
        t = Mathf.Clamp(t, lo, hi);
        // never inside the weapon hand: a hand's width apart along the handle
        var domT = Vector3.Dot(domGrip - near, dir);
        var gap = 0.09f * scale;
        if (Mathf.Abs(t - domT) < gap) t = domT + (t >= domT ? gap : -gap);
        var point = near + dir * t;
        var z = Vector3.Dot(controllerRot * Vector3.forward, dir) >= 0 ? dir : -dir;
        var up = Vector3.ProjectOnPlane(controllerRot * Vector3.up, z);
        if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.up, z);
        Override[(int)off] = (point, Quaternion.LookRotation(z, up.normalized));
        CurlLimit[(int)off] = CurlFor(gun ? 0.02f : 0.016f);
    }

    // ---------------- things you carry ----------------

    /// <summary>
    /// How something you carry sits in the hand (relative to the grip pose): its middle in the fist, pushed out of the
    /// palm by its own thickness; its longest side along the fist (a battery, a bone, a valve crank's spindle), its
    /// thinnest across the palm (a gear lies flat against it). Returns the radius the fingers close round (metres).
    /// </summary>
    public static float CarryPose(Transform copy, Quaternion gripRot, bool leftHand, out Quaternion holdRot, out Vector3 holdOffset)
    {
        var scale = VRRig.ActiveScale;
        var local = LocalBounds(copy);
        var lossy = copy.lossyScale;
        var sizes = new[] { local.size.x * Mathf.Abs(lossy.x), local.size.y * Mathf.Abs(lossy.y), local.size.z * Mathf.Abs(lossy.z) };
        var axes = new[] { copy.right, copy.up, copy.forward };
        int longest = 0, thinnest = 0;
        for (var i = 1; i < 3; i++)
        {
            if (sizes[i] > sizes[longest]) longest = i;
            if (sizes[i] < sizes[thinnest]) thinnest = i;
        }

        if (thinnest == longest) thinnest = (longest + 1) % 3;
        var handZ = gripRot * Vector3.forward;
        var across = gripRot * Vector3.right; // across the palm (out of the back of the hand one way, the palm the other)
        // the longest side along the fist, the thinnest across the palm; whichever way round is the least turn from how you took it
        var longAxis = Vector3.Dot(axes[longest], handZ) >= 0 ? axes[longest] : -axes[longest];
        var r1 = Quaternion.FromToRotation(longAxis, handZ);
        var thinAfter = Vector3.ProjectOnPlane(r1 * axes[thinnest], handZ);
        var target = Vector3.ProjectOnPlane(across, handZ);
        if (Vector3.Dot(thinAfter, target) < 0) target = -target;
        var r2 = thinAfter.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(thinAfter, target) : Quaternion.identity;
        var worldRot = r2 * r1 * copy.rotation;
        holdRot = Quaternion.Inverse(gripRot) * worldRot;

        // the fingers wrap its middle-sized side; a thick thing sits out from the palm by how much thicker than a handle it is
        var mid = sizes[0] + sizes[1] + sizes[2] - sizes[longest] - sizes[thinnest];
        var wrap = Mathf.Clamp(Mathf.Max(sizes[thinnest], Mathf.Min(mid, sizes[thinnest] * 2.5f)) * 0.5f / Mathf.Max(1e-4f, scale), 0.008f, 0.06f);
        var side = leftHand ? -1f : 1f;
        var outOfPalm = new Vector3(-side * Mathf.Max(0f, sizes[thinnest] * 0.5f / scale - 0.017f), 0, 0) * scale; // grip space
        holdOffset = outOfPalm - holdRot * Vector3.Scale(local.center, lossy);
        return wrap;
    }

    /// <summary>The copy's meshes' bounds in its own space.</summary>
    private static Bounds LocalBounds(Transform root)
    {
        var have = false;
        var b = new Bounds();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            for (var i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!have)
                {
                    b = new Bounds(p, Vector3.zero);
                    have = true;
                }
                else b.Encapsulate(p);
            }
        }

        return have ? b : new Bounds(Vector3.zero, Vector3.one * 0.05f);
    }
}
