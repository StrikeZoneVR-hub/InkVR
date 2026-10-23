using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using InkVR.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace InkVR.Body;

/// <summary>Which part of Henry a paintable piece is (what kind of ink reaches it).</summary>
public enum InkRegion
{
    Hand,
    Arm,
    Torso,
    Legs,
    Feet
}

/// <summary>
/// One paintable piece of Henry (a finger, the palm, a sleeve, the shirt, the trousers, the shoes...) with its
/// own copy of its texture. Rigid pieces follow their transform; skinned pieces (the body, the lifelike hands)
/// follow their bones, so ink lands where the piece really is.
/// </summary>
public sealed class StainPart
{
    public Transform T;
    public Renderer R;
    public Material Mat;
    public Texture2D Tex;
    public string BaseName;
    public bool Sleeve;
    public InkRegion Region;
    public int W, H;
    public Color32[] Base, Px;
    public Vector3[] Verts, Normals;
    public Vector2[] Uvs;
    public int[] Tris;

    /// <summary>Per vertex: the piece's surface (local/bind metres) per texel along U and along V.</summary>
    public Vector3[] TanU, TanV;

    /// <summary>Blotchy noise (0..255) over the texture: ragged wet lines, patchy soaking.</summary>
    public byte[] Noise;

    /// <summary>Drying: the next row to fade.</summary>
    public int FadeRow;

    // skinned pieces
    public Transform[] Bones;
    public Matrix4x4[] Bind;
    public SkinWeight[] Weights;

    public bool Dirty, Stained;
    public float LastSplat = -10, LastUpload = -10;

    /// <summary>Fresh ink is still running until then (drips fall off it).</summary>
    public float WetUntil;

    /// <summary>The last wading coat: its ink line (world y) and when (re-coated only when deeper, or now and then).</summary>
    public float CoatLine = float.MinValue, CoatTime = -10;

    private Vector3[] _world;
    private int _worldFrame = -1;
    private Matrix4x4[] _skin;

    public bool Skinned => Bones != null;
    public bool Visible => R != null && R.enabled && R.gameObject.activeInHierarchy;

    /// <summary>Every vertex in world space (skinned with the same weights the renderer uses), cached per frame.</summary>
    public Vector3[] WorldVerts()
    {
        if (_worldFrame == Time.frameCount && _world != null) return _world;
        _worldFrame = Time.frameCount;
        _world ??= new Vector3[Verts.Length];
        if (!Skinned)
        {
            var m = T.localToWorldMatrix;
            for (var i = 0; i < Verts.Length; i++) _world[i] = m.MultiplyPoint3x4(Verts[i]);
            return _world;
        }

        UpdateSkin();
        for (var i = 0; i < Verts.Length; i++)
        {
            var w = Weights[i];
            var p = _skin[w.B0].MultiplyPoint3x4(Verts[i]) * w.W0;
            if (w.B1 >= 0) p += _skin[w.B1].MultiplyPoint3x4(Verts[i]) * w.W1;
            _world[i] = p;
        }

        return _world;
    }

    private void UpdateSkin()
    {
        _skin ??= new Matrix4x4[Bones.Length];
        for (var b = 0; b < Bones.Length; b++) _skin[b] = Bones[b] != null ? Bones[b].localToWorldMatrix * Bind[b] : Matrix4x4.identity;
    }

    /// <summary>Local/bind space → world for one vertex (its main bone, or the transform).</summary>
    public Matrix4x4 VertexMatrix(int i)
    {
        if (!Skinned) return T.localToWorldMatrix;
        UpdateSkin();
        return _skin[Weights[i].B0];
    }

    public Vector3 WorldNormal(int i) => VertexMatrix(i).MultiplyVector(Normals[i]).normalized;

    /// <summary>The vertex nearest a world point (and how far).</summary>
    public int Nearest(Vector3 world, out float distance)
    {
        var wv = WorldVerts();
        var best = -1;
        var bestD = float.MaxValue;
        for (var i = 0; i < wv.Length; i++)
        {
            var d = (wv[i] - world).sqrMagnitude;
            if (d >= bestD) continue;
            bestD = d;
            best = i;
        }

        distance = best >= 0 ? Mathf.Sqrt(bestD) : float.MaxValue;
        return best;
    }
}

/// <summary>
/// Ink that gets onto Henry from the world, painted into each piece's own texture:
///   Splat: an irregular wet blot where something touched or landed, with a run of ink down towards the floor
///   Coat:  everything under the ink's surface (wading, dipping a hand), with a ragged wet line and splashes
///   Soak:  the whole piece (fallen in, stood under a pour)
///   SplashFrom: a spray of blots on the side facing where the ink came from (ink bursts)
/// Stains slowly dry and fade back to the chapter's base level, so Henry tells the story of the last few
/// minutes rather than going black.
/// </summary>
public static class InkStains
{
    public static readonly List<StainPart> Parts = new();
    public static int SplatCount;
    private static readonly Random Rng = new(4242);
    private static readonly Color32 Ink = new(11, 8, 6, 255);
    private static int _fadeCursor, _uploadCursor;
    private static float _nextFade;

    /// <summary>Gives a rigid piece its own material and paintable texture (null if the base texture is unknown).</summary>
    public static StainPart Register(Transform t, Renderer r, Material shared, MeshBuilder mb, bool sleeve, InkRegion region = InkRegion.Hand)
    {
        var p = Make(t, r, shared, mb, region);
        if (p == null) return null;
        p.Sleeve = sleeve;
        return p;
    }

    /// <summary>Gives a skinned piece (body clothes, lifelike hands) its own material and paintable texture.</summary>
    public static StainPart RegisterSkinned(SkinnedMeshRenderer r, MeshBuilder mb, SkinWeight[] weights, Transform[] bones, Matrix4x4[] bind, InkRegion region)
    {
        var p = Make(r.transform, r, r.sharedMaterial, mb, region);
        if (p == null) return null;
        p.Bones = bones;
        p.Bind = bind;
        p.Weights = weights;
        return p;
    }

    private static StainPart Make(Transform t, Renderer r, Material shared, MeshBuilder mb, InkRegion region)
    {
        var tex = shared?.mainTexture;
        if (tex == null || !HenryTextures.Pixels.TryGetValue(tex.name, out var basePx)) return null;

        var p = new StainPart
        {
            T = t, R = r, BaseName = tex.name, Region = region, W = basePx.W, H = basePx.H,
            Base = basePx.Px, Px = (Color32[])basePx.Px.Clone(), Verts = mb.VertexArray, Normals = mb.NormalArray, Uvs = mb.UvArray,
            Tris = mb.TriangleArray,
        };
        Tangents(p);
        p.Noise = NoiseMap(p.W, p.H);

        p.Tex = new Texture2D(p.W, p.H, TextureFormat.RGBA32, false) { name = tex.name + "_Stained", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        p.Tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(p.Tex);
        p.Mat = new Material(shared) { name = shared.name + "_" + t.name };
        p.Mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(p.Mat);
        p.Mat.SetTexture("_MainTex", p.Tex);
        r.sharedMaterial = p.Mat;
        Upload(p);
        Parts.Add(p);
        return p;
    }

    /// <summary>
    /// How the texture lies on the surface at each vertex: the step on the surface for one texel along U and
    /// along V (from each triangle's UV layout), so a blot is the same size and shape wherever it lands.
    /// </summary>
    private static void Tangents(StainPart p)
    {
        var n = p.Verts.Length;
        p.TanU = new Vector3[n];
        p.TanV = new Vector3[n];
        var wsum = new float[n];
        var avgU = Vector3.zero;
        var avgV = Vector3.zero;
        var tris = p.Tris;
        for (var t = 0; t + 2 < tris.Length; t += 3)
        {
            int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
            var e1 = p.Verts[i1] - p.Verts[i0];
            var e2 = p.Verts[i2] - p.Verts[i0];
            var du1 = Wrap((p.Uvs[i1].x - p.Uvs[i0].x) * p.W, p.W);
            var du2 = Wrap((p.Uvs[i2].x - p.Uvs[i0].x) * p.W, p.W);
            var dv1 = (p.Uvs[i1].y - p.Uvs[i0].y) * (p.H - 1);
            var dv2 = (p.Uvs[i2].y - p.Uvs[i0].y) * (p.H - 1);
            var det = du1 * dv2 - du2 * dv1;
            if (Mathf.Abs(det) < 1e-4f) continue;
            var tu = (dv2 * e1 - dv1 * e2) / det;
            var tv = (du1 * e2 - du2 * e1) / det;
            var area = Vector3.Cross(e1, e2).magnitude;
            foreach (var i in new[] { i0, i1, i2 })
            {
                p.TanU[i] += tu * area;
                p.TanV[i] += tv * area;
                wsum[i] += area;
            }

            avgU += tu * area;
            avgV += tv * area;
        }

        var fallbackU = avgU.sqrMagnitude > 1e-14f ? avgU.normalized * MeanLength(p.TanU, wsum) : Vector3.right * 0.001f;
        var fallbackV = avgV.sqrMagnitude > 1e-14f ? avgV.normalized * MeanLength(p.TanV, wsum) : Vector3.forward * 0.001f;
        for (var i = 0; i < n; i++)
        {
            if (wsum[i] > 0)
            {
                p.TanU[i] /= wsum[i];
                p.TanV[i] /= wsum[i];
            }

            if (p.TanU[i].sqrMagnitude < 1e-14f) p.TanU[i] = fallbackU;
            if (p.TanV[i].sqrMagnitude < 1e-14f) p.TanV[i] = fallbackV;
        }
    }

    private static float MeanLength(Vector3[] v, float[] w)
    {
        var s = 0f;
        var n = 0;
        for (var i = 0; i < v.Length; i++)
        {
            if (w[i] <= 0) continue;
            s += (v[i] / w[i]).magnitude;
            n++;
        }

        return n > 0 ? s / n : 0.001f;
    }

    private static float Wrap(float d, int w)
    {
        if (d > w * 0.5f) d -= w;
        else if (d < -w * 0.5f) d += w;
        return d;
    }

    /// <summary>Stop tracking pieces that were destroyed (style change).</summary>
    public static void Forget(List<StainPart> parts)
    {
        if (parts == null) return;
        foreach (var p in parts)
        {
            Parts.Remove(p);
            if (p.Tex != null) Object.Destroy(p.Tex);
            if (p.Mat != null) Object.Destroy(p.Mat);
        }
    }

    /// <summary>New chapter: new base textures (the studio's ink creeps further up), and today's stains wash off.</summary>
    public static void Rebase()
    {
        foreach (var p in Parts)
        {
            if (p.Mat == null) continue;
            var shared = HenryTextures.Pixels.TryGetValue(p.BaseName, out var basePx) ? basePx : default;
            if (shared.Px == null || shared.W != p.W || shared.H != p.H) continue;
            p.Base = shared.Px;
            p.Px = (Color32[])shared.Px.Clone();
            p.Stained = false;
            p.WetUntil = 0;
            p.CoatLine = float.MinValue;
            p.Dirty = true;
        }
    }

    /// <summary>Match a shader swap on the shared materials, keeping each piece's own texture.</summary>
    public static void AdoptShader(Material from, string baseName)
    {
        foreach (var p in Parts)
        {
            if (p.Mat == null || p.BaseName != baseName) continue;
            p.Mat.shader = from.shader;
            p.Mat.CopyPropertiesFromMaterial(from);
            p.Mat.SetTexture("_MainTex", p.Tex);
        }
    }

    private static bool Usable(StainPart p) => p?.Mat != null && ModConfig.InkOnHands.Value;

    /// <summary>Dev: slow paint operations get logged.</summary>
    private static void Timed(string op, StainPart p, System.Diagnostics.Stopwatch sw)
    {
        var ms = sw.Elapsed.TotalMilliseconds;
        if (ms > 8) Plugin.L.LogInfo($"INKTIME {op} {p?.T?.name} {p?.W}x{p?.H} tris={p?.Tris?.Length / 3} {ms:F1} ms"); // only the unexpectedly slow
    }

    // =====================================================================================================
    // Splats
    // =====================================================================================================

    /// <summary>Paint a blot where <paramref name="worldPoint"/> touches the piece (radius in metres of Henry).</summary>
    public static void Splat(StainPart p, Vector3 worldPoint, float radius, float strength, bool runs = true)
    {
        if (!Usable(p)) return;
        var i = p.Nearest(worldPoint, out _);
        if (i >= 0) SplatAtVertex(p, i, radius, strength, runs);
    }

    /// <summary>A spray of blots over the side of the piece facing where the ink came from.</summary>
    public static void SplashFrom(StainPart p, Vector3 travel, int blots, float strength, float minRadius = 0.007f, float maxRadius = 0.02f)
    {
        if (!Usable(p) || blots <= 0) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var from = -travel.normalized;
        for (var b = 0; b < blots; b++)
        {
            for (var tries = 0; tries < 8; tries++)
            {
                var i = Rng.Next(p.Verts.Length);
                if (Vector3.Dot(p.WorldNormal(i), from) < 0.15f) continue;
                // Painted over the next frames with the coats (a burst is dozens of blots).
                Blots.Add((p, i, Mathf.Lerp(minRadius, maxRadius, (float)Rng.NextDouble()), strength * (0.75f + 0.25f * (float)Rng.NextDouble())));
                break;
            }
        }

        Timed("splash", p, sw);
    }

    private static void SplatAtVertex(StainPart p, int i, float radius, float strength, bool runs, int blobs = -1)
    {
        var uv = p.Uvs[i];
        var cx = Frac(uv.x) * p.W;
        var cy = Mathf.Clamp01(uv.y) * (p.H - 1);
        // Where the texture pinches (the poles of a rounded piece) a texel is tiny on the surface: cap the blot.
        var rx = Mathf.Clamp(radius / Mathf.Max(1e-6f, p.TanU[i].magnitude), 1.5f, Mathf.Min(40f, p.W * 0.2f));
        var ry = Mathf.Clamp(radius / Mathf.Max(1e-6f, p.TanV[i].magnitude), 1.5f, Mathf.Min(40f, p.H * 0.2f));
        if (rx > ry * 3f) rx = ry * 3f;
        if (ry > rx * 3f) ry = rx * 3f;

        // A main blob, a few lobes and flecks: the same hand-drawn splat the game's ink decals use.
        Blob(p, cx, cy, rx, ry, strength);
        var lobes = blobs >= 0 ? blobs : 3 + Rng.Next(3);
        for (var k = 0; k < lobes; k++)
        {
            var a = Rng.NextDouble() * Math.PI * 2;
            var d = 0.55f + (float)Rng.NextDouble() * 0.5f;
            var s = 0.35f + (float)Rng.NextDouble() * 0.35f;
            Blob(p, cx + (float)Math.Cos(a) * rx * d, cy + (float)Math.Sin(a) * ry * d, rx * s, ry * s, strength);
        }

        if (blobs < 0)
            for (var k = 0; k < 3; k++)
            {
                var a = Rng.NextDouble() * Math.PI * 2;
                var d = 1.4f + (float)Rng.NextDouble();
                Blob(p, cx + (float)Math.Cos(a) * rx * d, cy + (float)Math.Sin(a) * ry * d, rx * 0.15f, ry * 0.15f, strength);
            }

        if (runs) Run(p, i, cx, cy, rx, ry, strength);
        Mark(p);
        SplatCount++;
    }

    /// <summary>Wet ink runs towards the floor: follow world-down across the texture from the blot.</summary>
    private static void Run(StainPart p, int i, float cx, float cy, float rx, float ry, float strength)
    {
        var downLocal = p.VertexMatrix(i).inverse.MultiplyVector(Vector3.down);
        var tu = p.TanU[i];
        var tv = p.TanV[i];
        var du = Vector3.Dot(tu, downLocal) / Mathf.Max(1e-12f, tu.sqrMagnitude);
        var dv = Vector3.Dot(tv, downLocal) / Mathf.Max(1e-12f, tv.sqrMagnitude);
        var dir = new Vector2(du, dv);
        // How steep the surface is here: a run only forms where ink can flow down.
        var slope = (du * tu + dv * tv).normalized;
        if (dir.sqrMagnitude < 1e-12f || Vector3.Dot(slope, downLocal.normalized) < 0.35f) return;
        dir.Normalize();
        var w = Mathf.Min(rx, ry) * 0.32f;
        var len = Mathf.Min(Mathf.Max(rx, ry) * (1.5f + (float)Rng.NextDouble() * 2.5f), 120f);
        var steps = Mathf.Clamp(Mathf.CeilToInt(len / Mathf.Max(1f, w * 0.6f)), 2, 60);
        var side = new Vector2(-dir.y, dir.x);
        for (var s = 0; s <= steps; s++)
        {
            var t = (float)s / Mathf.Max(1, steps);
            var wob = (float)Math.Sin(t * 3f) * w * 0.4f;
            Blob(p, cx + dir.x * t * len + side.x * wob, cy + dir.y * t * len + side.y * wob, w * (1f - 0.4f * t), w * (1f - 0.4f * t), strength);
        }

        Blob(p, cx + dir.x * len, cy + dir.y * len, w * 0.9f, w * 0.9f, strength);
    }

    private static void Mark(StainPart p)
    {
        p.Stained = true;
        p.Dirty = true;
        p.LastSplat = Time.unscaledTime;
        p.WetUntil = Mathf.Max(p.WetUntil, Time.unscaledTime + 20f);
    }

    private static void Blob(StainPart p, float cx, float cy, float rx, float ry, float strength)
    {
        var x0 = Mathf.FloorToInt(cx - rx - 1);
        var x1 = Mathf.CeilToInt(cx + rx + 1);
        var y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry - 1));
        var y1 = Mathf.Min(p.H - 1, Mathf.CeilToInt(cy + ry + 1));
        for (var y = y0; y <= y1; y++)
        {
            var dy = (y - cy) / ry;
            for (var xx = x0; xx <= x1; xx++)
            {
                var dx = (xx - cx) / rx;
                var d = dx * dx + dy * dy;
                if (d > 1.15f) continue;
                Paint(p, xx, y, Mathf.Clamp01((1.15f - d) / 0.3f) * strength);
            }
        }
    }

    private static void Paint(StainPart p, int xx, int y, float a)
    {
        if (a <= 0.004f) return;
        var x = ((xx % p.W) + p.W) % p.W;
        var i = y * p.W + x;
        var c = p.Px[i];
        p.Px[i] = new Color32(
            (byte)(c.r + (Ink.r - c.r) * a),
            (byte)(c.g + (Ink.g - c.g) * a),
            (byte)(c.b + (Ink.b - c.b) * a), 255);
    }

    private static float Frac(float v) => v - Mathf.Floor(v);

    // =====================================================================================================
    // Coats: everything of a piece where the ink is. Big jobs queue up and are painted a slice at a time
    // (a couple of milliseconds a frame), so wading in or falling in never stalls the game.
    // =====================================================================================================

    private enum CoatMode
    {
        Below,
        Inside,
        Soak
    }

    private sealed class CoatJob
    {
        public StainPart P;
        public CoatMode Mode;
        public float Surface, Edge, Strength;
        public Bounds Box;
        public Vector3[] World;
        public int Next; // next triangle (coats) or row (soak)
        public bool Touched;
    }

    private static readonly List<CoatJob> Jobs = new();
    private static readonly List<(StainPart P, int I, float Radius, float Strength)> Blots = new();
    private static readonly System.Diagnostics.Stopwatch Budget = new();

    /// <summary>Coats waiting to be painted (dev).</summary>
    public static int PendingCoats => Jobs.Count + Blots.Count;

    /// <summary>
    /// Coat whatever of the piece is under the ink's surface (world y), with a ragged wet line
    /// <paramref name="edge"/> metres (world) high and a few splashes just above it.
    /// </summary>
    public static void Coat(StainPart p, float surfaceWorldY, float strength, float edge = -1f, bool splashes = true)
    {
        if (!Usable(p)) return;
        if (edge < 0) edge = 0.015f * Core.VRRig.ActiveScale;
        var wv = p.WorldVerts();
        var lowest = float.MaxValue;
        foreach (var v in wv) lowest = Mathf.Min(lowest, v.y);
        if (lowest > surfaceWorldY + edge) return; // nothing of it is in the ink
        Queue(p, CoatMode.Below, j =>
        {
            j.Surface = surfaceWorldY;
            j.Edge = edge;
            j.Strength = strength;
        });
        if (splashes) SplashLine(p, surfaceWorldY, edge, strength);
    }

    /// <summary>The whole piece, soaked (patchy where the ink ran thin).</summary>
    public static void Soak(StainPart p, float strength)
    {
        if (!Usable(p)) return;
        Queue(p, CoatMode.Soak, j => j.Strength = strength);
    }

    /// <summary>Whatever of the piece is inside a box (standing in a pouring stream, a sole in a puddle).</summary>
    public static void CoatInside(StainPart p, Bounds box, float strength)
    {
        if (!Usable(p)) return;
        Queue(p, CoatMode.Inside, j =>
        {
            j.Box = box;
            j.Strength = strength;
        });
    }

    /// <summary>One job per piece and kind: a newer request updates the waiting one rather than piling up.</summary>
    private static void Queue(StainPart p, CoatMode mode, Action<CoatJob> set)
    {
        CoatJob job = null;
        foreach (var j in Jobs)
            if (j.P == p && j.Mode == mode && j.Next == 0)
            {
                job = j;
                break;
            }

        if (job == null)
        {
            job = new CoatJob { P = p, Mode = mode };
            Jobs.Add(job);
        }

        set(job);
        if (mode != CoatMode.Soak) job.World = (Vector3[])p.WorldVerts().Clone(); // where the piece was when the ink touched it
    }

    /// <summary>Kicked-up drops just above a wading line.</summary>
    private static void SplashLine(StainPart p, float surfaceY, float edge, float strength)
    {
        var wv = p.WorldVerts();
        var want = 2 + Rng.Next(3);
        for (var tries = 0; tries < 30 && want > 0; tries++)
        {
            var i = Rng.Next(wv.Length);
            var above = wv[i].y - surfaceY;
            if (above < edge || above > edge * 6f) continue;
            SplatAtVertex(p, i, 0.004f + 0.006f * (float)Rng.NextDouble(), strength * 0.8f, true, 1);
            want--;
        }
    }

    /// <summary>Paint queued coats for up to <paramref name="ms"/> milliseconds.</summary>
    private static void PumpCoats(double ms)
    {
        Budget.Restart();
        var b = 0;
        for (; b < Blots.Count && Budget.Elapsed.TotalMilliseconds < ms; b++)
        {
            var (p, i, radius, strength) = Blots[b];
            if (p?.Mat != null) SplatAtVertex(p, i, radius, strength, true);
        }

        if (b > 0) Blots.RemoveRange(0, b);
        while (Jobs.Count > 0 && Budget.Elapsed.TotalMilliseconds < ms)
        {
            var job = Jobs[0];
            if (job.P?.Mat == null)
            {
                Jobs.RemoveAt(0);
                continue;
            }

            var done = job.Mode == CoatMode.Soak ? SoakRows(job, 24) : CoatTriangles(job, 24);
            if (job.Touched) Mark(job.P);
            if (done) Jobs.RemoveAt(0);
        }
    }

    private static bool SoakRows(CoatJob job, int rows)
    {
        var p = job.P;
        var end = Mathf.Min(p.H, job.Next + rows);
        var s = job.Strength;
        for (var y = job.Next; y < end; y++)
        {
            var row = y * p.W;
            for (var x = 0; x < p.W; x++)
            {
                var a = Mathf.Clamp01(0.72f + p.Noise[row + x] * (0.5f / 255f)) * s; // wet through, a little thinner here and there
                PaintAt(p, row + x, a);
            }
        }

        job.Touched = true;
        job.Next = end;
        return end >= p.H;
    }

    /// <summary>Rasterise a few of the piece's triangles in texture space, interpolating where on the body each texel is.</summary>
    private static bool CoatTriangles(CoatJob job, int count)
    {
        var p = job.P;
        var tris = p.Tris;
        var wv = job.World;
        var end = Mathf.Min(tris.Length, job.Next + count * 3);
        var e = job.Edge;
        var below = job.Mode == CoatMode.Below;
        var box = job.Box;
        for (var t = job.Next; t + 2 < end; t += 3)
        {
            int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
            Vector3 pa = wv[ia], pb = wv[ib], pc = wv[ic];
            if (below)
            {
                if (Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y)) > job.Surface + e) continue;
            }
            else
            {
                var tb = new Bounds(pa, Vector3.zero);
                tb.Encapsulate(pb);
                tb.Encapsulate(pc);
                if (!tb.Intersects(box)) continue;
            }

            var ax = p.Uvs[ia].x * p.W;
            var ay = Mathf.Clamp01(p.Uvs[ia].y) * (p.H - 1);
            var bx = ax + Wrap((p.Uvs[ib].x - p.Uvs[ia].x) * p.W, p.W);
            var by = Mathf.Clamp01(p.Uvs[ib].y) * (p.H - 1);
            var cx = ax + Wrap((p.Uvs[ic].x - p.Uvs[ia].x) * p.W, p.W);
            var cy = Mathf.Clamp01(p.Uvs[ic].y) * (p.H - 1);
            var area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
            if (Mathf.Abs(area) < 1e-6f) continue;
            var inv = 1f / area;
            // barycentric weights as linear functions of the texel position
            float w0x = (by - cy) * inv, w0y = (cx - bx) * inv, w0c = (bx * cy - cx * by) * inv;
            float w1x = (cy - ay) * inv, w1y = (ax - cx) * inv, w1c = (cx * ay - ax * cy) * inv;

            var x0 = Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx)));
            var x1 = Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx)));
            var y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, Mathf.Min(by, cy))));
            var y1 = Mathf.Min(p.H - 1, Mathf.CeilToInt(Mathf.Max(ay, Mathf.Max(by, cy))));
            const float eps = -0.02f;
            for (var y = y0; y <= y1; y++)
            {
                var py = y + 0.5f;
                for (var x = x0; x <= x1; x++)
                {
                    var px = x + 0.5f;
                    var w0 = w0x * px + w0y * py + w0c;
                    if (w0 < eps) continue;
                    var w1 = w1x * px + w1y * py + w1c;
                    if (w1 < eps) continue;
                    var w2 = 1f - w0 - w1;
                    if (w2 < eps) continue;
                    var wx = ((x % p.W) + p.W) % p.W;
                    var idx = y * p.W + wx;
                    var n = p.Noise[idx] * (1f / 255f);
                    float a;
                    if (below)
                    {
                        var h = pa.y * w0 + pb.y * w1 + pc.y * w2;
                        a = Mathf.Clamp01((job.Surface + (n - 0.5f) * e * 2f - h) / (e * 0.35f) + 0.5f);
                    }
                    else
                    {
                        var pos = pa * w0 + pb * w1 + pc * w2;
                        a = box.Contains(pos) ? Mathf.Clamp01(0.35f + n) : 0f;
                    }

                    if (a <= 0.004f) continue;
                    PaintAt(p, idx, a * job.Strength);
                    job.Touched = true;
                }
            }
        }

        job.Next = end;
        return end >= tris.Length - 2;
    }

    private static void PaintAt(StainPart p, int i, float a)
    {
        if (a <= 0.004f) return;
        var c = p.Px[i];
        p.Px[i] = new Color32(
            (byte)(c.r + (Ink.r - c.r) * a),
            (byte)(c.g + (Ink.g - c.g) * a),
            (byte)(c.b + (Ink.b - c.b) * a), 255);
    }

    /// <summary>Smooth blotchy noise over a texture (0..255), made once per piece.</summary>
    private static byte[] NoiseMap(int w, int h)
    {
        const int N = 64;
        var r = new Random(77 + w * 31 + h);
        var lattice = new float[N * N];
        for (var i = 0; i < lattice.Length; i++) lattice[i] = (float)r.NextDouble();
        var map = new byte[w * h];
        for (var y = 0; y < h; y++)
        {
            var fy = y / 7f;
            var iy = (int)fy;
            var ty = fy - iy;
            ty = ty * ty * (3 - 2 * ty);
            int r0 = (iy % N) * N, r1 = ((iy + 1) % N) * N;
            for (var x = 0; x < w; x++)
            {
                var fx = x / 7f;
                var ix = (int)fx;
                var tx = fx - ix;
                tx = tx * tx * (3 - 2 * tx);
                int c0 = ix % N, c1 = (ix + 1) % N;
                var v = Mathf.Lerp(Mathf.Lerp(lattice[r0 + c0], lattice[r0 + c1], tx), Mathf.Lerp(lattice[r1 + c0], lattice[r1 + c1], tx), ty);
                var fine = lattice[(y * 13 + x * 7) & (N * N - 1)];
                map[y * w + x] = (byte)(Mathf.Clamp01(v * 0.8f + fine * 0.2f) * 255f);
            }
        }

        return map;
    }

    // =====================================================================================================
    // Drying and uploading
    // =====================================================================================================

    /// <summary>Per frame: paint queued coats, let old ink dry a few rows at a time, upload changed textures (within a budget).</summary>
    public static void Tick()
    {
        if (Parts.Count == 0) return;
        PumpCoats(2.0);

        // Drying: about twice a second each stained piece fades a little towards its base, a slice per frame.
        var minutes = Mathf.Max(0.5f, ModConfig.InkFadeMinutes.Value);
        if (Time.unscaledTime >= _nextFade)
        {
            for (var tries = 0; tries < Parts.Count; tries++)
            {
                var p = Parts[_fadeCursor % Parts.Count];
                if (!p.Stained || Time.unscaledTime - p.LastSplat < 8f || Jobs.Count > 0)
                {
                    _fadeCursor++;
                    continue;
                }

                // After `minutes` a tenth of the stain is left.
                if (FadeRows(p, 1f - Mathf.Pow(0.1f, 1f / (minutes * 120f)), 32))
                {
                    _fadeCursor++;
                    _nextFade = Time.unscaledTime + 0.5f / Mathf.Max(1, CountStained());
                }

                break;
            }
        }

        // Upload: up to ~1 MB of texture a frame, and no piece more than ten times a second.
        var budget = 1 << 20;
        for (var tries = 0; tries < Parts.Count && budget > 0; tries++)
        {
            _uploadCursor = (_uploadCursor + 1) % Parts.Count;
            var p = Parts[_uploadCursor];
            if (!p.Dirty || Time.unscaledTime - p.LastUpload < 0.1f) continue;
            Upload(p);
            budget -= p.W * p.H * 4;
        }
    }

    private static int CountStained()
    {
        var n = 0;
        foreach (var p in Parts)
            if (p.Stained)
                n++;
        return n;
    }

    /// <summary>Fade some rows of a piece back towards its base; true when the whole piece has had its turn.</summary>
    private static bool FadeRows(StainPart p, float k, int rows)
    {
        var end = Mathf.Min(p.H, p.FadeRow + rows);
        var any = p.FadeRow > 0 && p.Stained;
        for (var i = p.FadeRow * p.W; i < end * p.W; i++)
        {
            var c = p.Px[i];
            var b = p.Base[i];
            if (c.r == b.r && c.g == b.g && c.b == b.b) continue;
            any = true;
            p.Px[i] = new Color32(Step(c.r, b.r, k), Step(c.g, b.g, k), Step(c.b, b.b, k), 255);
        }

        p.Dirty = true;
        p.FadeRow = end;
        if (end < p.H)
        {
            p.Stained = true;
            return false;
        }

        p.FadeRow = 0;
        p.Stained = any;
        return true;
    }

    private static byte Step(byte from, byte to, float k)
    {
        var d = (to - from) * k;
        if (Mathf.Abs(d) < 1f) d = Mathf.Sign(to - from);
        return (byte)Mathf.Clamp(from + d, Mathf.Min(from, to), Mathf.Max(from, to));
    }

    private static void Upload(StainPart p)
    {
        if (p.Tex == null) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        UploadInner(p);
        Timed("upload", p, sw);
    }

    private static void UploadInner(StainPart p)
    {
        var handle = GCHandle.Alloc(p.Px, GCHandleType.Pinned);
        try
        {
            p.Tex.LoadRawTextureData(handle.AddrOfPinnedObject(), p.Px.Length * 4);
        }
        finally
        {
            handle.Free();
        }

        p.Tex.Apply(false, false);
        p.Dirty = false;
        p.LastUpload = Time.unscaledTime;
    }
}
