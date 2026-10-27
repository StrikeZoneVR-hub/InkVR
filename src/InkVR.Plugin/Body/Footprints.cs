using System.Collections.Generic;
using InkVR.Core;
using InkVR.UI;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InkVR.Body;

/// <summary>
/// Inky shoes leave footprints. Each sole carries a load of ink (from wading, pools, puddles, falling in);
/// every step prints a sole and uses some of it up, so a trail fades from solid prints to patchy ones to
/// nothing, and drying soles stop printing after a while too. Prints stay on the floor for a few minutes,
/// then fade. With the full body the prints are where Henry's feet come down; with any other body option
/// they follow your path, a stride at a time. Nothing prints while you're still standing in the ink.
/// </summary>
public static class Footprints
{
    private sealed class Print
    {
        public Transform T;
        public MeshRenderer R;
        public float Born, Alpha;
    }

    private const int Max = 80;
    private const float Life = 150f, FadeTime = 45f;
    private static readonly List<Print> Prints = new();
    private static readonly float[] Load = new float[2];
    private static MaterialPropertyBlock _mpb;
    private static Material[] _mats;
    private static Mesh _left, _right;
    private static HenryBody _listening;
    private static Vector3 _lastPos;
    private static bool _haveLast;
    private static float _travel;
    private static int _nextFoot;

    /// <summary>Set by the ink sources each tick: which feet are in ink right now (a foot prints once it steps out).</summary>
    private static readonly bool[] InInkFoot = new bool[2];

    public static bool InInk => InInkFoot[0] || InInkFoot[1];

    public static void SetInInk(int foot, bool on) => InInkFoot[foot] = on;

    public static int Count => Prints.Count;
    public static float LoadLeft => Load[0];
    public static float LoadRight => Load[1];

    /// <summary>Ink onto a sole (0 = left, 1 = right).</summary>
    public static void Wet(int foot, float amount) => Load[foot] = Mathf.Clamp01(Mathf.Max(Load[foot], amount));

    public static void WetBoth(float amount)
    {
        Wet(0, amount);
        Wet(1, amount);
    }

    public static void Update()
    {
        if (!ModConfig.InkOnHands.Value || !XrManager.IsRunning || !VRRig.InGame || VRRig.CinemaActive)
        {
            Listen(null);
            _haveLast = false;
            return;
        }

        var dt = InkClock.Dt;
        for (var i = 0; i < 2; i++) Load[i] = Mathf.Max(0, Load[i] - dt / 180f); // soles dry in about three minutes

        // Full body: print where Henry's feet land. Otherwise: a stride at a time along your path.
        Listen(Hands.Body);
        if (Hands.Body == null) Stride();
        else _haveLast = false;

        FadePrints();
    }

    private static void Listen(HenryBody body)
    {
        if (body == _listening) return;
        if (_listening != null) _listening.FootPlanted -= OnPlanted;
        _listening = body;
        if (body != null) body.FootPlanted += OnPlanted;
    }

    private static float BodyScale => VRRig.ActiveScale * Mathf.Clamp(VRRig.StandingEyeHeight / HenryBody.EyeHeight, 0.75f, 1.35f);

    private static void OnPlanted(int foot, Vector3 ground, float yaw)
    {
        var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
        Place(foot, ground + fwd * (0.07f * BodyScale), yaw);
    }

    private static void Stride()
    {
        var head = VRRig.Head;
        if (head == null) return;
        var k = BodyScale;
        var pos = new Vector3(head.position.x, 0, head.position.z);
        if (!_haveLast)
        {
            _lastPos = pos;
            _haveLast = true;
            _travel = 0;
            return;
        }

        var move = pos - _lastPos;
        var d = move.magnitude;
        _lastPos = pos;
        if (d > 1.5f * k)
        {
            _travel = 0; // teleported (respawn, level change)
            return;
        }

        _travel += d;
        var stride = 0.36f * k;
        if (_travel < stride || d < 1e-5f) return;
        _travel -= stride;
        var yaw = Mathf.Atan2(move.x, move.z) * Mathf.Rad2Deg;
        var side = _nextFoot == 0 ? -1f : 1f;
        var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
        var at = head.position - Vector3.up * (0.25f * k) + right * (0.1f * side * k); // found on the floor below
        Place(_nextFoot, at, yaw + 7f * side);
        _nextFoot = 1 - _nextFoot;
    }

    /// <summary>Print a sole near <paramref name="at"/> (found on the floor below it), if the sole still has ink.</summary>
    public static void Place(int foot, Vector3 at, float yaw)
    {
        if (InInkFoot[foot] || Load[foot] < 0.05f) return;
        var k = BodyScale;
        var mask = Physics.DefaultRaycastLayers & ~VRLayers.UIMask;
        var player = GameManager.Instance?.Player;
        if (player != null) mask &= ~(1 << player.gameObject.layer);
        if (!Physics.Raycast(at + Vector3.up * (0.3f * k), Vector3.down, out var hit, 3f * k, mask, QueryTriggerInteraction.Ignore)) return;
        if (Vector3.Dot(hit.normal, Vector3.up) < 0.7f) return; // not on walls or steep slopes

        var load = Load[foot];
        Load[foot] *= 0.8f;
        var print = Take();
        var fwd = Vector3.ProjectOnPlane(Quaternion.Euler(0, yaw, 0) * Vector3.forward, hit.normal).normalized;
        var cartoon = Hands.Body != null && Hands.Body.Def.CartoonFeet;
        var size = cartoon ? new Vector3(0.17f, 1f, 0.36f) : new Vector3(0.12f, 1f, 0.31f);
        print.T.SetPositionAndRotation(hit.point + hit.normal * (0.003f * k), Quaternion.LookRotation(fwd, hit.normal));
        print.T.localScale = size * k;
        print.T.GetComponent<MeshFilter>().sharedMesh = foot == 0 ? _left : _right;
        // The less ink on the sole, the patchier the print.
        var variant = load > 0.7f ? 0 : load > 0.45f ? 1 : load > 0.25f ? 2 : 3;
        print.R.sharedMaterial = _mats[variant];
        print.Born = InkClock.Now;
        print.Alpha = Mathf.Clamp01(0.55f + load * 0.45f) * Random.Range(0.85f, 1f);
        print.T.gameObject.SetActive(true);
        SetAlpha(print, print.Alpha);
    }

    private static void FadePrints()
    {
        var now = InkClock.Now;
        foreach (var p in Prints)
        {
            if (p.T == null || !p.T.gameObject.activeSelf) continue;
            var age = now - p.Born;
            if (age > Life)
            {
                p.T.gameObject.SetActive(false);
                continue;
            }

            if (age > Life - FadeTime) SetAlpha(p, p.Alpha * (Life - age) / FadeTime);
        }
    }

    /// <summary>A new level: yesterday's prints don't follow you.</summary>
    public static void Clear()
    {
        foreach (var p in Prints)
            if (p.T != null)
                p.T.gameObject.SetActive(false);
        Load[0] = Load[1] = 0;
        InInkFoot[0] = InInkFoot[1] = false;
        _haveLast = false;
    }

    private static void SetAlpha(Print p, float a)
    {
        _mpb ??= new MaterialPropertyBlock();
        _mpb.SetColor("_Color", new Color(1, 1, 1, a));
        p.R.SetPropertyBlock(_mpb);
    }

    private static Print Take()
    {
        Build();
        // Quitting to the menu destroys everything kept between levels, our prints too (all at once): start afresh.
        if (Prints.Count > 0 && Prints[0].T == null) Prints.Clear();
        if (Prints.Count < Max)
        {
            var go = new GameObject("InkVR_Footprint");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<MeshFilter>().sharedMesh = _left;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var p = new Print { T = go.transform, R = r };
            Prints.Add(p);
            return p;
        }

        // Reuse the oldest.
        var oldest = Prints[0];
        foreach (var p in Prints)
            if (!p.T.gameObject.activeSelf || p.Born < oldest.Born)
            {
                oldest = p;
                if (!p.T.gameObject.activeSelf) break;
            }

        return oldest;
    }

    // ---------------- the print itself ----------------

    private static void Build()
    {
        if (_mats != null) return;
        _left = Quad(true);
        _right = Quad(false);
        _mats = new Material[4];
        var cut = new[] { 0f, 0.3f, 0.48f, 0.64f };
        for (var i = 0; i < 4; i++)
        {
            var mat = new Material(Shader.Find("UI/Default")) { name = "InkVR_Footprint" + i, mainTexture = SoleTexture(cut[i], 900 + i), renderQueue = 2950 };
            mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
            mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            Object.DontDestroyOnLoad(mat);
            _mats[i] = mat;
        }
    }

    /// <summary>A unit quad on the ground (x across the sole, z from heel to toe), the left one mirrored.</summary>
    private static Mesh Quad(bool left)
    {
        var m = new Mesh { name = left ? "InkVR_FootprintL" : "InkVR_FootprintR" };
        m.SetVertices(new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) });
        var u0 = left ? 1f : 0f;
        var u1 = 1f - u0;
        m.SetUVs(0, new[] { new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, 1), new Vector2(u0, 1) });
        m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        m.RecalculateBounds();
        m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(m);
        return m;
    }

    /// <summary>
    /// A right sole's print in ink (toe at the top): the ball and toes, a narrow waist where the arch
    /// lifts off the floor, the heel; tread lines, ragged wet edges, a few flicked drops. Higher
    /// <paramref name="cut"/> = a drier sole: only the parts that pressed hardest still print.
    /// </summary>
    private static Texture2D SoleTexture(float cut, int seed)
    {
        const int W = 96, H = 224;
        var rng = new System.Random(seed);
        var lattice = new float[13 * 29];
        for (var i = 0; i < lattice.Length; i++) lattice[i] = (float)rng.NextDouble();
        float Lat(float x, float y)
        {
            var fx = x / 8f;
            var fy = y / 8f;
            int ix = Mathf.Clamp((int)fx, 0, 11), iy = Mathf.Clamp((int)fy, 0, 27);
            float tx = fx - ix, ty = fy - iy;
            var a = Mathf.Lerp(lattice[iy * 13 + ix], lattice[iy * 13 + ix + 1], tx);
            var b = Mathf.Lerp(lattice[(iy + 1) * 13 + ix], lattice[(iy + 1) * 13 + ix + 1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        var px = new Color32[W * H];
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
            var n = Lat(x, y);
            var edge = (n - 0.5f) * 0.08f;
            // ball of the foot and toes (a little turned in), and the heel
            var ball = Sq((u - 0.52f + (v - 0.7f) * 0.12f) / 0.43f) + Sq((v - 0.7f) / 0.27f);
            var heel = Sq((u - 0.5f) / 0.36f) + Sq((v - 0.15f) / 0.13f);
            var waist = Mathf.Abs(u - 0.62f) < 0.14f && v > 0.3f && v < 0.5f ? 0.9f : 9f; // the outer edge still touches
            var inside = Mathf.Min(ball, Mathf.Min(heel, waist)) < 1f + edge * 4f;
            var a = 0f;
            if (inside)
            {
                a = 0.92f;
                // tread: bars across the sole and the heel, a little smeared
                var bar = Mathf.Sin(v * H * 0.42f + Mathf.Sin(u * 9f) * 0.8f);
                if (bar > 0.86f) a *= 0.6f;
                // the hardest-pressed parts keep their ink longest
                var press = Mathf.Max(1f - ball, 1f - heel * 1.1f);
                if (n * 0.7f + press * 0.5f < cut) a = 0;
            }

            // flicked drops around the print
            if (!inside && rng.NextDouble() < 0.0025 * (1 - cut)) a = 0.85f;
            px[y * W + x] = new Color32(11, 8, 6, (byte)(a * 255));
        }

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "InkVR_Sole" + seed, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        tex.SetPixels32(px);
        tex.Apply(true, true);
        tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(tex);
        return tex;
    }

    private static float Sq(float v) => v * v;
}
