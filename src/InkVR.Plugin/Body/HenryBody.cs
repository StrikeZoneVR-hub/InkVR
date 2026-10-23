using System;
using System.Collections.Generic;
using InkVR.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InkVR.Body;

public enum BodyStyle
{
    /// <summary>Same style as the hands: chunky, rounded, cartoon shoes, red braces.</summary>
    Toon,

    /// <summary>Lifelike proportions, clothes (waistcoat, tie, pinstripes, boots) and hands, still inked.</summary>
    Lifelike
}

/// <summary>
/// Henry's body below the head, for full-body VR. A skinned mesh on a ten-bone skeleton (pelvis, spine,
/// chest, neck, and thigh/shin/foot per leg), built procedurally in the game's ink style, posed every frame
/// from your head and hands:
/// - the torso hangs from your neck and turns with your head (and towards your hands), leaning and
///   bending the knees as you crouch;
/// - the arms (the existing <see cref="HandModel"/> arms) reach from its shoulders to your controllers;
/// - the feet plant on the ground (stairs and ramps included) and step when you move or turn.
/// There is no head: you are looking out of it.
/// Model space is metres, facing +z, with the eyes 1.62 m up; the whole body scales to your eye height.
/// </summary>
public sealed class HenryBody
{
    public const float EyeHeight = 1.62f;

    private enum B
    {
        Pelvis,
        Spine,
        Chest,
        Neck,
        LThigh,
        LShin,
        LFoot,
        RThigh,
        RShin,
        RFoot
    }

    private const int BoneCount = 10;

    // ---- rest skeleton (model metres) ----
    private static readonly Vector3 HipC = new(0, 0.95f, 0), Waist = new(0, 1.10f, 0), ChestP = new(0, 1.28f, 0);
    private static readonly Vector3 NeckB = new(0, 1.45f, -0.02f), NeckT = new(0, 1.53f, -0.01f), Eye = new(0, EyeHeight, 0.09f);
    private static Vector3 HipJ(float s) => new(0.095f * s, 0.93f, 0);
    private static Vector3 Knee(float s) => new(0.095f * s, 0.5f, 0.015f);
    private static Vector3 Ankle(float s) => new(0.1f * s, 0.085f, -0.01f);
    private static Vector3 Toe(float s) => Ankle(s) + new Vector3(0, -0.04f, 0.17f);
    private static readonly float ThighLen = (HipJ(1) - Knee(1)).magnitude, ShinLen = (Knee(1) - Ankle(1)).magnitude;

    public readonly BodyStyle Style;
    private readonly Transform _root;
    private readonly Transform[] _bones = new Transform[BoneCount];
    private readonly Vector3[] _head = new Vector3[BoneCount], _tail = new Vector3[BoneCount];
    private readonly List<Renderer> _renderers = new();

    public HenryBody(BodyStyle style, Transform parent, Func<string, Material> material)
    {
        Style = style;
        _root = new GameObject(style == BodyStyle.Toon ? "InkVR_HenryBody_Toon" : "InkVR_HenryBody_Lifelike").transform;
        _root.SetParent(parent, false);

        Rest(B.Pelvis, HipC, Waist, Vector3.forward);
        Rest(B.Spine, Waist, ChestP, Vector3.forward);
        Rest(B.Chest, ChestP, NeckB, Vector3.forward);
        Rest(B.Neck, NeckB, NeckT, Vector3.forward);
        Rest(B.LThigh, HipJ(-1), Knee(-1), Vector3.forward);
        Rest(B.LShin, Knee(-1), Ankle(-1), Vector3.forward);
        Rest(B.LFoot, Ankle(-1), Toe(-1), Vector3.up);
        Rest(B.RThigh, HipJ(1), Knee(1), Vector3.forward);
        Rest(B.RShin, Knee(1), Ankle(1), Vector3.forward);
        Rest(B.RFoot, Ankle(1), Toe(1), Vector3.up);

        if (style == BodyStyle.Toon) BuildToon();
        else BuildLifelike();
        Assemble(material);
    }

    private void Rest(B b, Vector3 head, Vector3 tail, Vector3 up)
    {
        var t = new GameObject(b.ToString()).transform;
        t.SetParent(_root, false);
        t.localPosition = head;
        t.localRotation = Quaternion.LookRotation(tail - head, up);
        _bones[(int)b] = t;
        _head[(int)b] = head;
        _tail[(int)b] = tail;
    }

    // =====================================================================================================
    // The two models
    // =====================================================================================================

    private readonly SkinBuilder _skin = new();

    private void Add(MeshBuilder mb, string material, float outline, params B[] bones) => _skin.Add(mb, material, outline, v => Weigh(v, bones));

    private static MeshBuilder Tube(Vector3[] centers, float[] radii, Vector3 up, int sides, bool capStart, bool capEnd, Vector2? ellipse = null)
    {
        var mb = new MeshBuilder();
        mb.Tube(centers, radii, up, sides, capStart, capEnd, ellipse);
        return mb;
    }

    private static MeshBuilder Blob(Vector3 center, Vector3 radii, int sides = 18, int stacks = 10)
    {
        var mb = new MeshBuilder();
        mb.Ellipsoid(center, radii, Quaternion.identity, sides, stacks);
        return mb;
    }

    private static readonly B[] TorsoBones = { B.Pelvis, B.Spine, B.Chest, B.Neck };
    private static readonly B[] UpperBones = { B.Spine, B.Chest, B.Neck };

    private void BuildToon()
    {
        // Shirt over a rounded barrel of a chest, closing in at the collar.
        Add(Tube(new[] { new Vector3(0, 0.97f, 0), new Vector3(0, 1.05f, 0.006f), new Vector3(0, 1.15f, 0.012f), new Vector3(0, 1.25f, 0.012f), new Vector3(0, 1.34f, 0.004f), new Vector3(0, 1.4f, -0.012f), new Vector3(0, 1.445f, -0.022f), new Vector3(0, 1.47f, -0.026f) },
            new[] { 0.176f, 0.174f, 0.18f, 0.195f, 0.205f, 0.19f, 0.14f, 0.075f }, Vector3.forward, 30, false, false, new Vector2(1f, 0.68f)), "toonShirt", 0.005f, TorsoBones);
        foreach (var s in new[] { -1f, 1f })
            Add(Blob(new Vector3(0.185f * s, 1.405f, -0.02f), new Vector3(0.075f, 0.07f, 0.075f)), "toonShirt", 0.004f, B.Chest);
        Collar("toonShirt", 0.078f);

        // Trousers: a sturdy seat, a belt with a brass buckle, chunky legs with turned-up cuffs.
        Add(Tube(new[] { new Vector3(0, 0.84f, 0), new Vector3(0, 0.92f, 0.003f), new Vector3(0, 1.0f, 0.006f) }, new[] { 0.17f, 0.182f, 0.18f }, Vector3.forward, 28, true, false, new Vector2(1f, 0.74f)),
            "toonHips", 0.005f, B.Pelvis);
        Add(Tube(new[] { new Vector3(0, 0.985f, 0.006f), new Vector3(0, 1.03f, 0.007f) }, new[] { 0.186f, 0.186f }, Vector3.forward, 28, false, false, new Vector2(1f, 0.73f)), "belt", 0.003f, B.Pelvis);
        Add(Blob(new Vector3(0, 1.008f, 0.139f), new Vector3(0.038f, 0.029f, 0.012f), 12, 6), "gold", 0.002f, B.Pelvis);
        foreach (var s in new[] { -1f, 1f })
        {
            Add(Tube(new[]
                {
                    new Vector3(0.095f * s, 0.98f, 0), new Vector3(0.095f * s, 0.86f, 0.006f), new Vector3(0.095f * s, 0.7f, 0.012f), new Vector3(0.095f * s, 0.52f, 0.018f),
                    new Vector3(0.096f * s, 0.36f, 0.012f), new Vector3(0.098f * s, 0.22f, 0), new Vector3(0.1f * s, 0.14f, -0.006f), new Vector3(0.1f * s, 0.11f, -0.008f)
                }, new[] { 0.125f, 0.12f, 0.108f, 0.097f, 0.091f, 0.088f, 0.094f, 0.094f }, Vector3.forward, 22, true, true), "toonTrousers", 0.005f,
                s < 0 ? new[] { B.LThigh, B.LShin } : new[] { B.RThigh, B.RShin });

            // Big round cartoon shoes, like the ones in his cartoons.
            var a = Ankle(s);
            var foot = s < 0 ? B.LFoot : B.RFoot;
            Add(Blob(a + new Vector3(0, -0.02f, 0.05f), new Vector3(0.082f, 0.07f, 0.17f)), "toonShoe", 0.0045f, foot);
            Add(Blob(a + new Vector3(0, -0.015f, 0.17f), new Vector3(0.097f, 0.08f, 0.097f)), "toonShoe", 0.0045f, foot);
            Add(Blob(a + new Vector3(0, -0.07f, 0.065f), new Vector3(0.092f, 0.018f, 0.2f), 18, 6), "belt", 0.003f, foot);

            // Red braces over the shoulders, clipped to the waistband.
            var x = s;
            Add(Tube(new[]
                {
                    new Vector3(0.075f * x, 1.03f, 0.137f), new Vector3(0.08f * x, 1.15f, 0.138f), new Vector3(0.086f * x, 1.27f, 0.146f), new Vector3(0.092f * x, 1.37f, 0.136f),
                    new Vector3(0.095f * x, 1.474f, 0.01f), new Vector3(0.09f * x, 1.41f, -0.128f), new Vector3(0.075f * x, 1.27f, -0.142f), new Vector3(0.06f * x, 1.12f, -0.136f), new Vector3(0.055f * x, 1.03f, -0.135f)
                }, new[] { 0.013f, 0.013f, 0.013f, 0.013f, 0.013f, 0.013f, 0.013f, 0.013f, 0.013f }, Vector3.right, 8, false, false, new Vector2(0.45f, 1.9f)),
                "braces", 0.002f, TorsoBones);
            Add(Blob(new Vector3(0.075f * x, 1.045f, 0.142f), new Vector3(0.022f, 0.016f, 0.008f), 10, 6), "gold", 0.0015f, B.Pelvis, B.Spine);
        }
    }

    private void BuildLifelike()
    {
        // Shirt, then a fitted waistcoat over it, a tie in the V, a watch chain across the front.
        Add(Tube(new[] { new Vector3(0, 0.97f, 0), new Vector3(0, 1.05f, 0.006f), new Vector3(0, 1.15f, 0.012f), new Vector3(0, 1.25f, 0.012f), new Vector3(0, 1.34f, 0.004f), new Vector3(0, 1.4f, -0.012f), new Vector3(0, 1.445f, -0.022f), new Vector3(0, 1.47f, -0.026f) },
            new[] { 0.152f, 0.148f, 0.153f, 0.17f, 0.184f, 0.176f, 0.13f, 0.072f }, Vector3.forward, 30, false, false, new Vector2(1f, 0.68f)), "studioShirt", 0.004f, TorsoBones);
        foreach (var s in new[] { -1f, 1f })
            Add(Blob(new Vector3(0.178f * s, 1.405f, -0.02f), new Vector3(0.068f, 0.064f, 0.068f)), "studioShirt", 0.0035f, B.Chest);
        Add(Tube(new[] { new Vector3(0, 0.955f, 0.003f), new Vector3(0, 1.05f, 0.008f), new Vector3(0, 1.15f, 0.014f), new Vector3(0, 1.25f, 0.014f), new Vector3(0, 1.34f, 0.006f), new Vector3(0, 1.395f, -0.006f) },
            new[] { 0.16f, 0.157f, 0.162f, 0.18f, 0.195f, 0.187f }, Vector3.forward, 30, false, false, new Vector2(1f, 0.69f)), "vest", 0.0045f, TorsoBones);
        Collar("studioShirt", 0.074f);
        Add(Tube(new[] { new Vector3(0, 1.47f, 0.06f), new Vector3(0, 1.42f, 0.118f), new Vector3(0, 1.33f, 0.142f), new Vector3(0, 1.25f, 0.137f) },
            new[] { 0.016f, 0.024f, 0.032f, 0.034f }, Vector3.right, 10, false, true, new Vector2(0.25f, 1.2f)), "tie", 0.002f, UpperBones);
        Add(Tube(new[] { new Vector3(0.01f, 1.112f, 0.127f), new Vector3(0.05f, 1.095f, 0.124f), new Vector3(0.09f, 1.106f, 0.113f) }, new[] { 0.0045f, 0.0045f, 0.0045f }, Vector3.forward, 6, true, true),
            "gold", 0.0012f, B.Spine);

        // Pressed pinstripe trousers, a slim belt, laced work boots.
        Add(Tube(new[] { new Vector3(0, 0.84f, 0), new Vector3(0, 0.92f, 0.003f), new Vector3(0, 0.99f, 0.006f) }, new[] { 0.145f, 0.158f, 0.16f }, Vector3.forward, 28, true, false, new Vector2(1f, 0.72f)),
            "studioHips", 0.0045f, B.Pelvis);
        Add(Tube(new[] { new Vector3(0, 0.975f, 0.006f), new Vector3(0, 1.005f, 0.007f) }, new[] { 0.164f, 0.164f }, Vector3.forward, 28, false, false, new Vector2(1f, 0.71f)), "belt", 0.0025f, B.Pelvis);
        Add(Blob(new Vector3(0, 0.99f, 0.118f), new Vector3(0.024f, 0.017f, 0.009f), 10, 6), "gold", 0.0015f, B.Pelvis);
        foreach (var s in new[] { -1f, 1f })
        {
            Add(Tube(new[]
                {
                    new Vector3(0.093f * s, 0.98f, 0), new Vector3(0.093f * s, 0.86f, 0.006f), new Vector3(0.093f * s, 0.7f, 0.012f), new Vector3(0.094f * s, 0.52f, 0.017f),
                    new Vector3(0.095f * s, 0.36f, 0.012f), new Vector3(0.097f * s, 0.22f, 0), new Vector3(0.1f * s, 0.14f, -0.006f), new Vector3(0.1f * s, 0.11f, -0.008f)
                }, new[] { 0.093f, 0.09f, 0.08f, 0.07f, 0.064f, 0.06f, 0.062f, 0.062f }, Vector3.forward, 22, true, true), "studioTrousers", 0.0045f,
                s < 0 ? new[] { B.LThigh, B.LShin } : new[] { B.RThigh, B.RShin });

            var a = Ankle(s);
            var foot = s < 0 ? B.LFoot : B.RFoot;
            Add(Tube(new[] { a + new Vector3(0, -0.03f, -0.006f), a + new Vector3(0, 0.07f, -0.01f) }, new[] { 0.058f, 0.056f }, Vector3.forward, 16, false, false), "studioBoot", 0.0035f, foot);
            Add(Blob(a + new Vector3(0, -0.032f, 0.04f), new Vector3(0.053f, 0.05f, 0.13f)), "studioBoot", 0.0035f, foot);
            Add(Blob(a + new Vector3(0, -0.037f, 0.12f), new Vector3(0.056f, 0.044f, 0.07f)), "studioBoot", 0.0035f, foot);
            Add(Blob(a + new Vector3(0, -0.074f, 0.045f), new Vector3(0.06f, 0.013f, 0.155f), 18, 6), "belt", 0.0025f, foot);
            Add(Blob(a + new Vector3(0, -0.06f, -0.04f), new Vector3(0.05f, 0.028f, 0.05f), 14, 6), "belt", 0.0025f, foot);
        }
    }

    private void Collar(string material, float radius)
    {
        Add(Tube(new[] { new Vector3(0, 1.44f, -0.022f), new Vector3(0, 1.5f, -0.027f) }, new[] { radius, radius * 0.93f }, Vector3.forward, 24, false, false, new Vector2(1f, 0.92f)), material, 0.003f, B.Chest, B.Neck);
        foreach (var s in new[] { -1f, 1f })
            Add(Blob(new Vector3(0.036f * s, 1.452f, 0.042f), new Vector3(0.034f, 0.011f, 0.03f), 12, 6), material, 0.002f, B.Chest, B.Neck);
    }

    // =====================================================================================================
    // Skinning
    // =====================================================================================================

    private static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
        return (p - (a + ab * t)).magnitude;
    }

    /// <summary>The nearest one or two bones of those allowed, blended sharply (rigid limbs, soft joints).</summary>
    private SkinWeight Weigh(Vector3 v, B[] allowed)
    {
        int b1 = -1, b2 = -1;
        float d1 = float.MaxValue, d2 = float.MaxValue;
        foreach (var b in allowed)
        {
            var d = SegmentDistance(v, _head[(int)b], _tail[(int)b]);
            if (d < d1)
            {
                b2 = b1;
                d2 = d1;
                b1 = (int)b;
                d1 = d;
            }
            else if (d < d2)
            {
                b2 = (int)b;
                d2 = d;
            }
        }

        if (b2 < 0) return SkinWeight.Rigid(b1);
        var w1 = 1f / (Mathf.Pow(d1, 4) + 1e-9f);
        var w2 = 1f / (Mathf.Pow(d2, 4) + 1e-9f);
        return SkinWeight.Blend(b1, b2, w2 / (w1 + w2));
    }

    private void Assemble(Func<string, Material> material) =>
        _renderers.AddRange(_skin.BuildCombined(_root, _bones, _bones[(int)B.Pelvis], material, _root.name));

    public void SetVisible(bool on)
    {
        if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        if (!on) _init = false;
    }

    public void SetLayer(int layer)
    {
        foreach (var r in _renderers)
            if (r != null && r.gameObject.layer != layer)
                r.gameObject.layer = layer;
    }

    public void Destroy() => Object.Destroy(_root.gameObject);

    // =====================================================================================================
    // Posing: torso from the head, legs by IK onto planted feet
    // =====================================================================================================

    private struct Foot
    {
        public Vector3 Planted, From, To;
        public float Yaw, FromYaw, ToYaw, T, Duration;
        public bool Stepping;
    }

    private readonly Foot[] _feet = new Foot[2];
    private bool _init;
    private float _yaw;
    private Vector3 _lastPelvis, _velocity;
    private int _nextFoot;

    /// <summary>Where the arms attach (world), and which way the elbows should point.</summary>
    public Vector3 ShoulderLeft { get; private set; }

    public Vector3 ShoulderRight { get; private set; }
    public Vector3 ElbowHintLeft { get; private set; }
    public Vector3 ElbowHintRight { get; private set; }

    /// <summary>Dev: the last pose's pelvis and feet (world).</summary>
    public Vector3 Pelvis { get; private set; }

    public Vector3 FootLeft => FootPos(0);
    public Vector3 FootRight => FootPos(1);

    private static float YawOf(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

    /// <param name="eye">Your eyes (world).</param>
    /// <param name="headRot">Your head's rotation (world).</param>
    /// <param name="k">World units per metre of Henry (world scale × body size).</param>
    /// <param name="groundAt">Ground height (world y) under a world point.</param>
    /// <param name="dt">Seconds since the last pose; 0 re-poses the upper body only (late pose).</param>
    public void Pose(Vector3 eye, Quaternion headRot, float k, Vector3? handLeft, Vector3? handRight, Func<Vector3, float> groundAt, float dt)
    {
        _root.localScale = Vector3.one * k;

        // --- facing: follow the head, a little towards where the hands are working ---
        var headFwd = headRot * Vector3.forward;
        var headYaw = YawOf(headFwd);
        var target = headYaw;
        if (handLeft.HasValue && handRight.HasValue)
        {
            var mid = (handLeft.Value + handRight.Value) * 0.5f - eye;
            mid.y = 0;
            if (mid.magnitude > 0.15f * k)
            {
                var d = Mathf.DeltaAngle(headYaw, YawOf(mid));
                if (Mathf.Abs(d) < 75f) target = headYaw + d * 0.4f;
            }
        }

        var speed = new Vector3(_velocity.x, 0, _velocity.z).magnitude / Mathf.Max(k, 1e-3f);
        if (!_init) _yaw = target;
        if (dt > 0)
        {
            var off = Mathf.DeltaAngle(_yaw, target);
            if (Mathf.Abs(off) > 35f) _yaw += off - Mathf.Sign(off) * 35f;
            _yaw = Mathf.MoveTowardsAngle(_yaw, target, dt * (speed > 0.3f ? 220f : 45f));
        }

        var yawRot = Quaternion.Euler(0, _yaw, 0);
        var f = yawRot * Vector3.forward;
        var up = Vector3.up;

        // --- neck from the head, pelvis under it; crouching bends the knees and leans the back ---
        var neckTop = eye + headRot * ((NeckT - Eye) * k);
        var neckBase = neckTop + up * ((NeckB.y - NeckT.y) * k) + f * ((NeckB.z - NeckT.z) * k);
        var floorY = groundAt(new Vector3(neckBase.x, eye.y, neckBase.z));
        var spineLen = (NeckB - HipC).magnitude * k;
        var stand = floorY + HipC.y * k;
        var ideal = neckBase.y - spineLen;
        Vector3 pelvis;
        if (ideal >= stand)
        {
            pelvis = neckBase - f * (0.02f * k);
            pelvis.y = Mathf.Min(ideal, stand + 0.04f * k);
        }
        else
        {
            var drop = stand - ideal;
            var py = Mathf.Max(stand - drop * 0.9f, floorY + 0.3f * k); // the knees take most of a crouch; the back stays fairly upright
            var vert = neckBase.y - py;
            var horiz = Mathf.Sqrt(Mathf.Max(0, spineLen * spineLen - vert * vert));
            pelvis = neckBase - f * (horiz + 0.02f * k);
            pelvis.y = py;
        }

        if (dt > 0)
        {
            if (_init && (pelvis - _lastPelvis).magnitude > 1.5f * k) _init = false; // teleported (level load, respawn)
            _velocity = _init ? Vector3.Lerp(_velocity, (pelvis - _lastPelvis) / dt, 1f - Mathf.Exp(-dt * 8f)) : Vector3.zero;
            _lastPelvis = pelvis;
        }

        Pelvis = pelvis;
        var tu = (neckBase - pelvis).normalized;
        var ft = Vector3.ProjectOnPlane(f, tu).normalized;
        var rt = Vector3.Cross(tu, ft);
        var waist = Vector3.Lerp(pelvis, neckBase, 0.3f);
        var chest = Vector3.Lerp(pelvis, neckBase, 0.66f) + ft * (0.01f * k);

        Set(B.Pelvis, pelvis, waist, ft);
        Set(B.Spine, waist, chest, ft);
        Set(B.Chest, chest, neckBase, ft);
        Set(B.Neck, neckBase, neckTop, Vector3.ProjectOnPlane(headFwd, (neckTop - neckBase).normalized));

        ShoulderLeft = neckBase - rt * (0.19f * k) - tu * (0.03f * k);
        ShoulderRight = neckBase + rt * (0.19f * k) - tu * (0.03f * k);
        ElbowHintLeft = ShoulderLeft + (-rt * 0.25f - tu * 0.45f - ft * 0.25f) * k;
        ElbowHintRight = ShoulderRight + (rt * 0.25f - tu * 0.45f - ft * 0.25f) * k;

        // --- feet ---
        var rFlat = yawRot * Vector3.right;
        if (!_init)
        {
            for (var i = 0; i < 2; i++)
            {
                var s = i == 0 ? -1f : 1f;
                var p = new Vector3(pelvis.x, 0, pelvis.z) + rFlat * (0.1f * s * k);
                p.y = groundAt(p + up * (eye.y - p.y));
                _feet[i] = new Foot { Planted = p, Yaw = _yaw };
            }

            _init = true;
        }

        if (dt > 0) Step(pelvis, rFlat, f, eye.y, k, speed, groundAt, dt);

        for (var i = 0; i < 2; i++)
        {
            var s = i == 0 ? -1f : 1f;
            var ground = FootPos(i);
            var footYaw = _feet[i].Stepping ? Mathf.LerpAngle(_feet[i].FromYaw, _feet[i].ToYaw, Smooth(_feet[i].T)) : _feet[i].Yaw;
            var footRot = Quaternion.Euler(0, footYaw + 7f * s, 0);
            var ankle = ground + up * (Ankle(1).y * k);
            var hip = pelvis + rt * (0.095f * s * k) - tu * (0.02f * k);
            var footFwd = footRot * Vector3.forward;
            var knee = SolveLeg(hip, ankle, footFwd, k, out ankle);
            var toe = ankle + footRot * ((Toe(1) - Ankle(1)) * k);
            Set(i == 0 ? B.LThigh : B.RThigh, hip, knee, footFwd);
            Set(i == 0 ? B.LShin : B.RShin, knee, ankle, footFwd);
            Set(i == 0 ? B.LFoot : B.RFoot, ankle, toe, up);
        }
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private Vector3 FootPos(int i)
    {
        var foot = _feet[i];
        if (!foot.Stepping) return foot.Planted;
        var t = Smooth(foot.T);
        var lift = Mathf.Sin(foot.T * Mathf.PI) * 0.08f * _root.localScale.x;
        return Vector3.Lerp(foot.From, foot.To, t) + Vector3.up * lift;
    }

    /// <summary>Plant each foot under the hip; step (one foot at a time) when it falls behind or the body turns.</summary>
    private void Step(Vector3 pelvis, Vector3 rFlat, Vector3 f, float eyeY, float k, float speed, Func<Vector3, float> groundAt, float dt)
    {
        for (var i = 0; i < 2; i++)
        {
            if (!_feet[i].Stepping) continue;
            _feet[i].T += dt / _feet[i].Duration;
            if (_feet[i].T < 1f) continue;
            _feet[i].Stepping = false;
            _feet[i].Planted = _feet[i].To;
            _feet[i].Yaw = _feet[i].ToYaw;
        }

        if (_feet[0].Stepping || _feet[1].Stepping) return;

        var lead = new Vector3(_velocity.x, 0, _velocity.z) * 0.22f;
        var best = -1;
        var bestNeed = 0f;
        var targets = new Vector3[2];
        for (var i = 0; i < 2; i++)
        {
            var s = i == 0 ? -1f : 1f;
            var p = new Vector3(pelvis.x, 0, pelvis.z) + rFlat * (0.1f * s * k) + lead;
            p.y = groundAt(new Vector3(p.x, eyeY, p.z));
            targets[i] = p;
            var flat = new Vector3(p.x - _feet[i].Planted.x, 0, p.z - _feet[i].Planted.z).magnitude / k;
            var turn = Mathf.Abs(Mathf.DeltaAngle(_feet[i].Yaw, _yaw)) / 40f;
            var height = Mathf.Abs(p.y - _feet[i].Planted.y) / k / 0.2f;
            var need = Mathf.Max(flat / 0.2f, Mathf.Max(turn, height));
            if (i == _nextFoot) need *= 1.15f; // alternate feet when both want to go
            if (need > 1f && need > bestNeed)
            {
                bestNeed = need;
                best = i;
            }
        }

        if (best < 0) return;
        _feet[best].From = _feet[best].Planted;
        _feet[best].To = targets[best];
        _feet[best].FromYaw = _feet[best].Yaw;
        _feet[best].ToYaw = _yaw;
        _feet[best].T = 0;
        _feet[best].Duration = Mathf.Clamp(0.32f - speed * 0.06f, 0.16f, 0.32f);
        _feet[best].Stepping = true;
        _nextFoot = 1 - best;
    }

    /// <summary>Two-bone leg: hip → knee → ankle, knee bending forward over the foot. Out: the reachable ankle.</summary>
    private static Vector3 SolveLeg(Vector3 hip, Vector3 ankleTarget, Vector3 forward, float k, out Vector3 ankle)
    {
        var a = ThighLen * k;
        var b = ShinLen * k;
        var to = ankleTarget - hip;
        var dist = to.magnitude;
        var dir = dist > 1e-5f ? to / dist : Vector3.down;
        var d = Mathf.Clamp(dist, Mathf.Abs(a - b) + 1e-4f, (a + b) * 0.999f);
        ankle = hip + dir * d;
        var cosA = (a * a + d * d - b * b) / (2 * a * d);
        var sinA = Mathf.Sqrt(Mathf.Max(0, 1 - cosA * cosA));
        var bend = Vector3.ProjectOnPlane(forward, dir).normalized;
        if (bend.sqrMagnitude < 1e-6f) bend = forward;
        return hip + dir * (a * cosA) + bend * (a * sinA);
    }

    private void Set(B b, Vector3 head, Vector3 tail, Vector3 up)
    {
        var t = _bones[(int)b];
        var dir = tail - head;
        t.position = head;
        if (dir.sqrMagnitude > 1e-10f) t.rotation = Quaternion.LookRotation(dir, up.sqrMagnitude > 1e-6f ? up : Vector3.forward);
    }
}
