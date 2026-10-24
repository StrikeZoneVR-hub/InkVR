using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using InkVR.Core;
using InkVR.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

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

/// <summary>A collision stand-in for part of a piece: a capsule in a bone's space (or the piece's own).</summary>
public struct InkCapsule
{
    public int Bone; // -1: the piece's transform
    public F3 A, B;
    public float R;
}

/// <summary>A trickle of wet ink running down a piece, in texture space.</summary>
public sealed class InkRun
{
    public float U, V, Volume;
    public Vector3 LastWorld;
}

/// <summary>
/// One paintable piece of Henry (a finger, the palm, a sleeve, the shirt, the trousers, the shoes...) with its
/// own copy of its texture and two layers of ink over it: how much ink is there (Cov) and how much of it is
/// still liquid (Wet). Rigid pieces follow their transform; skinned pieces follow their bones.
/// </summary>
public sealed class StainPart
{
    public Transform T;
    public Renderer R;
    public Material Mat;
    public Texture2D Tex;
    public string BaseName;
    public bool Sleeve;

    /// <summary>The base texture is stacked this many times (one band per piece sharing it).</summary>
    public int Bands = 1;

    public InkRegion Region;
    public int W, H;
    public Color32[] Base, Px;
    public Vector2[] Uvs;
    public int[] Tris;

    /// <summary>Local (bind-space) vertices and normals.</summary>
    public F3[] LV, LN;

    /// <summary>Per vertex: the piece's surface (local/bind space) per texel along U and along V.</summary>
    public F3[] TanU, TanV;

    public byte[] Noise;
    public byte[] Cov, Wet;
    public TexelMap Map;
    public InkCapsule[] Caps = Array.Empty<InkCapsule>();

    // skinned pieces
    public Transform[] Bones;
    public M34[] Bind;
    public SkinWeight[] Weights;

    public bool Dirty, Inked, PassAny;
    public int DirtyY0 = int.MaxValue, DirtyY1 = -1, PassRow;

    /// <summary>Rows composed but not yet sent to the GPU.</summary>
    public int UpY0 = int.MaxValue, UpY1 = -1;
    public float LastInk = -10, LastUpload = -10, LastPass, InInkUntil, NextRun, PassWet;

    /// <summary>Roughly how much liquid ink is on the piece (runs and shaking off use it).</summary>
    public float WetAmount;

    public readonly List<InkRun> Runs = new();

    private F3[] _world, _normals;
    private Vector3[] _worldV;
    private int _worldFrame = -1, _normalFrame = -1, _skinFrame = -1, _worldVFrame = -1;
    private M34[] _skin;
    private M34 _m;
    private int _mFrame = -1;

    public bool Skinned => Bones != null;
    public bool Ready => Map != null && Map.Ready;
    public bool Visible => R != null && R.enabled && R.gameObject.activeInHierarchy;
    public float LastSplat => LastInk;

    /// <summary>The rigid piece's transform this frame.</summary>
    public M34 Matrix()
    {
        if (_mFrame == Time.frameCount) return _m;
        _mFrame = Time.frameCount;
        _m = M34.Of(T.localToWorldMatrix);
        return _m;
    }

    public M34[] SkinMatrices()
    {
        if (_skinFrame == Time.frameCount && _skin != null) return _skin;
        _skinFrame = Time.frameCount;
        _skin ??= new M34[Bones.Length];
        for (var b = 0; b < Bones.Length; b++) _skin[b] = Bones[b] != null ? M34.Mul(M34.Of(Bones[b].localToWorldMatrix), Bind[b]) : Bind[b];
        return _skin;
    }

    /// <summary>Every vertex in world space (skinned the way the renderer does), once per frame.</summary>
    public F3[] World()
    {
        if (_worldFrame == Time.frameCount && _world != null) return _world;
        _worldFrame = Time.frameCount;
        _world ??= new F3[LV.Length];
        if (!Skinned)
        {
            var m = Matrix();
            for (var i = 0; i < LV.Length; i++) _world[i] = m.Point(LV[i]);
            return _world;
        }

        var skin = SkinMatrices();
        for (var i = 0; i < LV.Length; i++)
        {
            var w = Weights[i];
            var p = skin[w.B0].Point(LV[i]) * w.W0;
            if (w.B1 >= 0) p += skin[w.B1].Point(LV[i]) * w.W1;
            _world[i] = p;
        }

        return _world;
    }

    public F3[] WorldNormals()
    {
        if (_normalFrame == Time.frameCount && _normals != null) return _normals;
        _normalFrame = Time.frameCount;
        _normals ??= new F3[LN.Length];
        if (!Skinned)
        {
            var m = Matrix();
            for (var i = 0; i < LN.Length; i++) _normals[i] = m.Vec(LN[i]).Norm;
            return _normals;
        }

        var skin = SkinMatrices();
        for (var i = 0; i < LN.Length; i++) _normals[i] = skin[Weights[i].B0].Vec(LN[i]).Norm;
        return _normals;
    }

    /// <summary>World vertices as Unity vectors (for the odd caller outside the hot loops).</summary>
    public Vector3[] WorldVerts()
    {
        if (_worldVFrame == Time.frameCount && _worldV != null) return _worldV;
        _worldVFrame = Time.frameCount;
        var w = World();
        _worldV ??= new Vector3[w.Length];
        for (var i = 0; i < w.Length; i++) _worldV[i] = w[i].V;
        return _worldV;
    }

    /// <summary>Local/bind space → world for one vertex (its main bone, or the transform).</summary>
    public M34 VertexMatrix(int i) => Skinned ? SkinMatrices()[Weights[i].B0] : Matrix();

    /// <summary>World position of a texture entry (a texel on the surface), and its nearest corner.</summary>
    public F3 EntryWorld(int entry, out int mainVertex)
    {
        var t = Map.TriangleOf(entry);
        int ia = Tris[t * 3], ib = Tris[t * 3 + 1], ic = Tris[t * 3 + 2];
        float w0 = Map.B0[entry] / 255f, w1 = Map.B1[entry] / 255f, w2 = 1f - w0 - w1;
        mainVertex = w0 >= w1 && w0 >= w2 ? ia : w1 >= w2 ? ib : ic;
        var wv = World();
        return F3.Bary(wv[ia], wv[ib], wv[ic], w0, w1, w2);
    }

    /// <summary>The point of the piece's surface (a vertex) nearest a world point.</summary>
    public F3 NearestSurface(F3 p)
    {
        var wv = World();
        var best = p;
        var bestD = float.MaxValue;
        for (var i = 0; i < wv.Length; i++)
        {
            var d = (wv[i] - p).Len2;
            if (d >= bestD) continue;
            bestD = d;
            best = wv[i];
        }

        return best;
    }

    public void MarkRows(int y0, int y1)
    {
        if (y0 < DirtyY0) DirtyY0 = y0;
        if (y1 > DirtyY1) DirtyY1 = y1;
        Dirty = true;
        Inked = true;
        LastInk = InkClock.Now;
    }
}

/// <summary>
/// Ink on Henry, as a liquid. Everything that inks him goes through here, in world space:
///   Deposit: a drop or a touch lands at a point (a blot that favours the side it came from)
///   DepositBelow: everything under an ink surface (dipping a hand, wading), every frame, so the line follows you
///   DepositInside: whatever is inside a volume (standing in a pouring stream)
///   Soak: the whole piece
/// Wet ink runs downhill and drips off fingertips and hems, a sharp flick shakes it off, and it dries: first the
/// glossy black goes matte, then over a few minutes it fades back to the chapter's usual stains.
/// </summary>
public static class InkStains
{
    public static readonly List<StainPart> Parts = new();
    public static int SplatCount;

    /// <summary>Dev: how many texels the last Deposit touched.</summary>
    public static int LastDepositTexels;

    private static readonly Color32 WetInk = new(6, 5, 5, 255), DryInk = new(36, 30, 26, 255);
    private static readonly System.Random Rng = new(4242);
    private static int _passCursor, _uploadCursor;
    private static uint _rand = 2463534242;

    private static float NextRand()
    {
        _rand ^= _rand << 13;
        _rand ^= _rand >> 17;
        _rand ^= _rand << 5;
        return (_rand & 0xFFFFFF) / 16777216f;
    }

    /// <summary>A run of ink reached an edge or an overhang: a drop falls from there (world point, size 0..1, the piece).</summary>
    public static Action<Vector3, float, StainPart> DropFalls;

    // =====================================================================================================
    // Registration
    // =====================================================================================================

    public static StainPart Register(Transform t, Renderer r, Material shared, MeshBuilder mb, bool sleeve, InkRegion region = InkRegion.Hand)
    {
        var p = Make(t, r, shared, mb, region);
        if (p == null) return null;
        p.Sleeve = sleeve;
        p.Caps = CapsulesRigid(p);
        return p;
    }

    public static StainPart RegisterSkinned(SkinnedMeshRenderer r, MeshBuilder mb, SkinWeight[] weights, Transform[] bones, Matrix4x4[] bind, InkRegion region, int bands = 1)
    {
        var p = Make(r.transform, r, r.sharedMaterial, mb, region, bands);
        if (p == null) return null;
        p.Bones = bones;
        p.Bind = new M34[bind.Length];
        for (var i = 0; i < bind.Length; i++) p.Bind[i] = M34.Of(bind[i]);
        p.Weights = weights;
        p.Caps = CapsulesSkinned(p);
        return p;
    }

    private static StainPart Make(Transform t, Renderer r, Material shared, MeshBuilder mb, InkRegion region, int bands = 1)
    {
        var tex = shared?.mainTexture;
        if (tex == null || !HenryTextures.Pixels.TryGetValue(tex.name, out var basePx)) return null;

        var baseTiled = Stack(basePx.Px, bands);
        var verts = mb.VertexArray;
        var normals = mb.NormalArray;
        var p = new StainPart
        {
            T = t, R = r, BaseName = tex.name, Region = region, W = basePx.W, H = basePx.H * bands, Bands = bands,
            Base = baseTiled, Px = (Color32[])baseTiled.Clone(), Uvs = mb.UvArray, Tris = mb.TriangleArray,
            LV = new F3[verts.Length], LN = new F3[normals.Length],
        };
        for (var i = 0; i < verts.Length; i++) p.LV[i] = F3.Of(verts[i]);
        for (var i = 0; i < normals.Length; i++) p.LN[i] = F3.Of(normals[i]);
        p.Cov = new byte[p.W * p.H];
        p.Wet = new byte[p.W * p.H];
        p.Noise = NoiseMap(p.W, p.H);
        Tangents(p);
        p.Map = TexelMap.For(p.W, p.H, p.Uvs, p.Tris);

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

    private static Color32[] Stack(Color32[] px, int bands)
    {
        if (bands <= 1) return px;
        var o = new Color32[px.Length * bands];
        for (var b = 0; b < bands; b++) Array.Copy(px, 0, o, b * px.Length, px.Length);
        return o;
    }

    /// <summary>How the texture lies on the surface at each vertex: the surface step for one texel along U and V.</summary>
    private static void Tangents(StainPart p)
    {
        var n = p.LV.Length;
        p.TanU = new F3[n];
        p.TanV = new F3[n];
        var wsum = new float[n];
        var tris = p.Tris;
        for (var t = 0; t + 2 < tris.Length; t += 3)
        {
            int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
            var e1 = p.LV[i1] - p.LV[i0];
            var e2 = p.LV[i2] - p.LV[i0];
            var du1 = Wrap((p.Uvs[i1].x - p.Uvs[i0].x) * p.W, p.W);
            var du2 = Wrap((p.Uvs[i2].x - p.Uvs[i0].x) * p.W, p.W);
            var dv1 = (p.Uvs[i1].y - p.Uvs[i0].y) * (p.H - 1);
            var dv2 = (p.Uvs[i2].y - p.Uvs[i0].y) * (p.H - 1);
            var det = du1 * dv2 - du2 * dv1;
            if (Math.Abs(det) < 1e-4f) continue;
            var tu = (e1 * dv2 - e2 * dv1) / det;
            var tv = (e2 * du1 - e1 * du2) / det;
            var area = F3.Cross(e1, e2).Len;
            p.TanU[i0] += tu * area;
            p.TanV[i0] += tv * area;
            wsum[i0] += area;
            p.TanU[i1] += tu * area;
            p.TanV[i1] += tv * area;
            wsum[i1] += area;
            p.TanU[i2] += tu * area;
            p.TanV[i2] += tv * area;
            wsum[i2] += area;
        }

        for (var i = 0; i < n; i++)
        {
            if (wsum[i] <= 0) continue;
            p.TanU[i] = p.TanU[i] / wsum[i];
            p.TanV[i] = p.TanV[i] / wsum[i];
        }
    }

    private static float Wrap(float d, int w)
    {
        if (d > w * 0.5f) d -= w;
        else if (d < -w * 0.5f) d += w;
        return d;
    }

    // ---------------- collision capsules ----------------

    private static InkCapsule[] CapsulesRigid(StainPart p) =>
        Fit(p.LV, -1) is { } c ? new[] { c } : Array.Empty<InkCapsule>();

    private static InkCapsule[] CapsulesSkinned(StainPart p)
    {
        var groups = new Dictionary<int, List<F3>>();
        for (var i = 0; i < p.LV.Length; i++)
        {
            var b = p.Weights[i].B0;
            if (!groups.TryGetValue(b, out var list)) groups[b] = list = new List<F3>();
            list.Add(p.Bind[b].Point(p.LV[i])); // in the bone's own space
        }

        var caps = new List<InkCapsule>();
        foreach (var kv in groups)
            if (kv.Value.Count >= 6 && Fit(kv.Value.ToArray(), kv.Key) is { } c)
                caps.Add(c);
        return caps.ToArray();
    }

    /// <summary>A capsule along the points' long axis, as thick as most of them.</summary>
    private static InkCapsule? Fit(F3[] pts, int bone)
    {
        if (pts.Length < 3) return null;
        var m = default(F3);
        foreach (var v in pts) m += v;
        m = m / pts.Length;
        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var v in pts)
        {
            var d = v - m;
            xx += d.X * d.X;
            xy += d.X * d.Y;
            xz += d.X * d.Z;
            yy += d.Y * d.Y;
            yz += d.Y * d.Z;
            zz += d.Z * d.Z;
        }

        var u = new F3(0.3f, 0.5f, 0.8f).Norm;
        for (var k = 0; k < 16; k++)
        {
            var nu = new F3(xx * u.X + xy * u.Y + xz * u.Z, xy * u.X + yy * u.Y + yz * u.Z, xz * u.X + yz * u.Y + zz * u.Z);
            if (nu.Len2 < 1e-20f) break;
            u = nu.Norm;
        }

        float tmin = float.MaxValue, tmax = float.MinValue;
        var radial = new float[pts.Length];
        for (var i = 0; i < pts.Length; i++)
        {
            var d = pts[i] - m;
            var t = F3.Dot(d, u);
            tmin = Math.Min(tmin, t);
            tmax = Math.Max(tmax, t);
            radial[i] = (d - u * t).Len;
        }

        Array.Sort(radial);
        var r = Math.Max(radial[(int)(radial.Length * 0.85f)], 1e-4f);
        var half = Math.Max(0, (tmax - tmin) * 0.5f - r * 0.6f);
        var mid = m + u * ((tmin + tmax) * 0.5f);
        return new InkCapsule { Bone = bone, A = mid - u * half, B = mid + u * half, R = r };
    }

    /// <summary>A capsule in the world now (ends and radius).</summary>
    public static void CapsuleWorld(StainPart p, in InkCapsule c, out F3 a, out F3 b, out float r)
    {
        var m = c.Bone >= 0 && p.Bones != null && p.Bones[c.Bone] != null ? M34.Of(p.Bones[c.Bone].localToWorldMatrix) : p.Matrix();
        a = m.Point(c.A);
        b = m.Point(c.B);
        r = c.R * m.ScaleX;
    }

    // =====================================================================================================
    // Lifecycle
    // =====================================================================================================

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

    /// <summary>All of Henry's ink washed off (dev "inkclear").</summary>
    public static void WashAll()
    {
        foreach (var p in Parts)
        {
            if (p.Cov == null) continue;
            Array.Clear(p.Cov, 0, p.Cov.Length);
            Array.Clear(p.Wet, 0, p.Wet.Length);
            p.Runs.Clear();
            p.WetAmount = 0;
            p.MarkRows(0, p.H - 1);
        }
    }

    /// <summary>New chapter: new base textures (the studio's ink creeps further up), and today's ink washes off.</summary>
    public static void Rebase()
    {
        foreach (var p in Parts)
        {
            if (p.Mat == null) continue;
            var shared = HenryTextures.Pixels.TryGetValue(p.BaseName, out var basePx) ? basePx : default;
            if (shared.Px == null || shared.W != p.W || shared.H * p.Bands != p.H) continue;
            p.Base = Stack(shared.Px, p.Bands);
            Array.Clear(p.Cov, 0, p.Cov.Length);
            Array.Clear(p.Wet, 0, p.Wet.Length);
            p.Runs.Clear();
            p.WetAmount = 0;
            p.MarkRows(0, p.H - 1);
            p.Inked = false;
        }
    }

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

    private static bool Usable(StainPart p) => p?.Mat != null && p.Ready && ModConfig.InkOnHands.Value;

    // =====================================================================================================
    // Putting ink on
    // =====================================================================================================

    private static void Add(StainPart p, int idx, float k, ref int y0, ref int y1)
    {
        var add = (int)(k * 255f);
        if (add <= 0) return;
        int c0 = p.Cov[idx], w0 = p.Wet[idx];
        if (c0 == 255 && w0 == 255) return;
        var c = c0 + add;
        var w = w0 + add;
        p.Cov[idx] = (byte)(c > 255 ? 255 : c);
        p.Wet[idx] = (byte)(w > 255 ? 255 : w);
        var y = idx / p.W;
        if (y < y0) y0 = y;
        if (y > y1) y1 = y;
    }

    private static void Finish(StainPart p, int y0, int y1, float wetAdded)
    {
        if (y1 < y0) return;
        p.MarkRows(y0, y1);
        p.WetAmount += wetAdded;
    }

    public static void Deposit(StainPart p, Vector3 center, float radius, float amount, Vector3 travel = default) =>
        Deposit(p, F3.Of(center), radius, amount, F3.Of(travel));

    /// <summary>
    /// Ink lands at a world point: a blot of radius <paramref name="radius"/> (world units) with a ragged edge.
    /// <paramref name="travel"/> (the way the ink was moving; zero = from all round) favours the side facing it.
    /// </summary>
    public static void Deposit(StainPart p, F3 center, float radius, float amount, F3 travel)
    {
        if (!Usable(p) || radius <= 0) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var map = p.Map;
        var wv = p.World();
        var facing = travel.Len2 > 1e-12f;
        var wn = facing ? p.WorldNormals() : null;
        var from = facing ? -travel.Norm : default;
        var tris = p.Tris;
        var reach = radius * 1.25f;
        int y0 = int.MaxValue, y1 = -1;
        var added = 0;
        for (var t = 0; t < tris.Length / 3; t++)
        {
            int ia = tris[t * 3], ib = tris[t * 3 + 1], ic = tris[t * 3 + 2];
            F3 a = wv[ia], b = wv[ib], c = wv[ic];
            var centroid = (a + b + c) * (1f / 3f);
            var size = Math.Max((a - centroid).Len2, Math.Max((b - centroid).Len2, (c - centroid).Len2));
            var lim = reach + MathF.Sqrt(size);
            if ((centroid - center).Len2 > lim * lim) continue;
            for (var e = map.TriStart[t]; e < map.TriStart[t + 1]; e++)
            {
                float w0 = map.B0[e] * (1f / 255f), w1 = map.B1[e] * (1f / 255f), w2 = 1f - w0 - w1;
                var idx = map.Texel[e];
                var edge = radius * (0.7f + p.Noise[idx] * (0.6f / 255f));
                var d = (F3.Bary(a, b, c, w0, w1, w2) - center).Len;
                if (d > edge) continue;
                var k = amount * Math.Min(1f, (1f - d / edge) * 3f);
                if (facing) k *= FM.Clamp01(F3.Dot(F3.Bary(wn[ia], wn[ib], wn[ic], w0, w1, w2), from) * 1.6f + 0.35f);
                Add(p, idx, k, ref y0, ref y1);
                added++;
            }
        }

        Finish(p, y0, y1, added * amount * 0.002f);
        SplatCount++;
        LastDepositTexels = added;
        InkWorld.Prof.Add(11, sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Everything of the piece under an ink surface (world y) gets <paramref name="amount"/> more ink, with a ragged
    /// line <paramref name="edge"/> high. <paramref name="near"/>/<paramref name="within"/> limit it to around a
    /// point (one foot in a puddle).
    /// </summary>
    public static void DepositBelow(StainPart p, float surfaceY, float edge, float amount, Vector3? near = null, float within = 0)
    {
        if (!Usable(p)) return;
        var map = p.Map;
        var wv = p.World();
        var tris = p.Tris;
        var within2 = within * within;
        var nx = near?.x ?? 0;
        var nz = near?.z ?? 0;
        var limitXZ = near.HasValue;
        int y0 = int.MaxValue, y1 = -1;
        var added = 0;
        for (var t = 0; t < tris.Length / 3; t++)
        {
            int ia = tris[t * 3], ib = tris[t * 3 + 1], ic = tris[t * 3 + 2];
            F3 a = wv[ia], b = wv[ib], c = wv[ic];
            if (Math.Min(a.Y, Math.Min(b.Y, c.Y)) > surfaceY + edge) continue;
            if (limitXZ)
            {
                var dx = (a.X + b.X + c.X) * (1f / 3f) - nx;
                var dz = (a.Z + b.Z + c.Z) * (1f / 3f) - nz;
                if (dx * dx + dz * dz > within2) continue;
            }

            for (var e = map.TriStart[t]; e < map.TriStart[t + 1]; e++)
            {
                var idx = map.Texel[e];
                if (p.Cov[idx] == 255 && p.Wet[idx] == 255) continue; // already soaked
                float w0 = map.B0[e] * (1f / 255f), w1 = map.B1[e] * (1f / 255f);
                var h = a.Y * w0 + b.Y * w1 + c.Y * (1f - w0 - w1);
                if (h > surfaceY + (p.Noise[idx] * (1f / 255f) - 0.5f) * edge * 2f) continue;
                Add(p, idx, amount, ref y0, ref y1);
                added++;
            }
        }

        Finish(p, y0, y1, added * amount * 0.002f);
    }

    /// <summary>Whatever of the piece is inside a box (in a pouring stream), favouring the side facing <paramref name="travel"/>.</summary>
    public static void DepositInside(StainPart p, Bounds bounds, float amount, Vector3 travel)
    {
        if (!Usable(p)) return;
        var box = Box3.Of(bounds);
        var map = p.Map;
        var wv = p.World();
        var wn = p.WorldNormals();
        var from = -F3.Of(travel).Norm;
        var tris = p.Tris;
        int y0 = int.MaxValue, y1 = -1;
        var added = 0;
        for (var t = 0; t < tris.Length / 3; t++)
        {
            int ia = tris[t * 3], ib = tris[t * 3 + 1], ic = tris[t * 3 + 2];
            F3 a = wv[ia], b = wv[ib], c = wv[ic];
            var tb = Box3.Around(a, 0);
            tb.Encapsulate(b, 0);
            tb.Encapsulate(c, 0);
            if (!tb.Intersects(box)) continue;
            for (var e = map.TriStart[t]; e < map.TriStart[t + 1]; e++)
            {
                float w0 = map.B0[e] * (1f / 255f), w1 = map.B1[e] * (1f / 255f), w2 = 1f - w0 - w1;
                if (!box.Contains(F3.Bary(a, b, c, w0, w1, w2))) continue;
                var k = amount * FM.Clamp01(F3.Dot(F3.Bary(wn[ia], wn[ib], wn[ic], w0, w1, w2), from) * 1.5f + 0.25f);
                Add(p, map.Texel[e], k, ref y0, ref y1);
                added++;
            }
        }

        Finish(p, y0, y1, added * amount * 0.002f);
    }

    /// <summary>The whole piece, soaked (a little thinner here and there).</summary>
    public static void Soak(StainPart p, float strength)
    {
        if (!Usable(p)) return;
        var map = p.Map;
        int y0 = int.MaxValue, y1 = -1;
        for (var e = 0; e < map.Count; e++)
        {
            var idx = map.Texel[e];
            Add(p, idx, FM.Clamp01(0.75f + p.Noise[idx] * (0.4f / 255f)) * strength, ref y0, ref y1);
        }

        Finish(p, y0, y1, map.Count * strength * 0.002f);
    }

    /// <summary>Compatibility: a blot at a world point (radius in metres of Henry).</summary>
    public static void Splat(StainPart p, Vector3 worldPoint, float radius, float strength, bool runs = true) =>
        Deposit(p, F3.Of(worldPoint), radius * VRRig.ActiveScale, strength, default);

    // =====================================================================================================
    // Taking it off: a sharp flick shakes the liquid off
    // =====================================================================================================

    /// <summary>
    /// Shake a fraction of the wet ink off the piece. What was still liquid mostly goes (a faint stain stays);
    /// dried ink stays put. Returns how much came off, and adds world points it flew from to <paramref name="from"/>.
    /// </summary>
    public static float ShakeOff(StainPart p, float fraction, List<Vector3> from, int maxPoints)
    {
        if (!Usable(p) || p.WetAmount < 0.01f) return 0;
        var map = p.Map;
        for (var tries = 0; tries < maxPoints * 6 && from.Count < maxPoints; tries++)
        {
            var e = Rng.Next(map.Count);
            if (p.Wet[map.Texel[e]] < 80) continue;
            from.Add(p.EntryWorld(e, out _).V);
        }

        var removed = 0f;
        int y0 = int.MaxValue, y1 = -1;
        for (var i = 0; i < p.Wet.Length; i++)
        {
            int w = p.Wet[i];
            if (w == 0) continue;
            var off = (int)(w * fraction + NextRand());
            if (off <= 0) continue;
            var nw = w - off;
            p.Wet[i] = (byte)nw;
            p.Cov[i] = (byte)Math.Max(p.Cov[i] - (int)(off * 0.85f), nw);
            removed += off;
            var y = i / p.W;
            if (y < y0) y0 = y;
            if (y > y1) y1 = y;
        }

        if (y1 >= y0) p.MarkRows(y0, y1);
        p.WetAmount *= 1f - fraction;
        return removed / 255f;
    }

    // =====================================================================================================
    // Runs: wet ink trickles downhill, and drips off where the surface turns under
    // =====================================================================================================

    private static float _nextRuns;

    private static void UpdateRuns()
    {
        var now = InkClock.Now;
        if (now < _nextRuns) return;
        const float dt = 1f / 30f;
        _nextRuns = now + dt;
        var scale = VRRig.ActiveScale;
        foreach (var p in Parts)
        {
            if (!p.Ready || !p.Visible) continue;
            if (now < p.InInkUntil)
            {
                p.Runs.Clear();
                continue;
            }

            // New runs start where the ink is wettest, more often the wetter the piece is.
            if (p.WetAmount > 0.03f && p.Runs.Count < 6 && now >= p.NextRun)
            {
                p.NextRun = now + (1.8f + (0.25f - 1.8f) * FM.Clamp01(p.WetAmount / 4f)) * (0.6f + NextRand() * 0.8f);
                var map = p.Map;
                for (var tries = 0; tries < 24; tries++)
                {
                    var e = (int)(NextRand() * map.Count);
                    var idx = map.Texel[e];
                    if (p.Wet[idx] < 170) continue;
                    p.Runs.Add(new InkRun { U = idx % p.W + 0.5f, V = idx / p.W + 0.5f, Volume = 0.5f + NextRand() * 0.8f });
                    break;
                }
            }

            for (var r = p.Runs.Count - 1; r >= 0; r--)
                if (!StepRun(p, p.Runs[r], dt, scale))
                    p.Runs.RemoveAt(r);
        }
    }

    /// <summary>One step of a run; false when it has ended (dried up, pooled, or dripped off).</summary>
    private static bool StepRun(StainPart p, InkRun run, float dt, float scale)
    {
        var map = p.Map;
        var x = ((int)MathF.Floor(run.U) % p.W + p.W) % p.W;
        var y = (int)MathF.Floor(run.V);
        if (y < 0 || y >= p.H)
        {
            Drip(p, run);
            return false;
        }

        var e = map.EntryOf[y * p.W + x];
        if (e < 0)
        {
            Drip(p, run);
            return false;
        }

        run.LastWorld = p.EntryWorld(e, out var vi).V;
        var m = p.VertexMatrix(vi);
        var n = p.WorldNormals()[vi];
        var gt = F3.Down - n * F3.Dot(F3.Down, n);
        if (gt.Len < 0.3f)
        {
            // Facing down: it hangs and drips. Facing up: it pools where it is.
            if (F3.Dot(n, F3.Down) > 0.3f) Drip(p, run);
            return false;
        }

        var tu = m.Vec(p.TanU[vi]);
        var tv = m.Vec(p.TanV[vi]);
        if (tu.Len2 < 1e-14f || tv.Len2 < 1e-14f) return false;
        var du = F3.Dot(gt, tu) / tu.Len2;
        var dv = F3.Dot(gt, tv) / tv.Len2;
        var per = (tu * du + tv * dv).Len;
        if (per < 1e-9f) return false;
        var step = 0.05f * scale * dt * (0.6f + run.Volume * 0.5f); // a few centimetres a second
        var k = step / per;
        var width = FM.Clamp(0.0022f * scale / Math.Max(1e-6f, tu.Len), 0.9f, 4f); // a couple of millimetres
        int y0 = int.MaxValue, y1 = -1;
        var steps = Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(du * du + dv * dv) * k), 1, 12);
        for (var s = 0; s < steps; s++)
        {
            run.U += du * k / steps;
            run.V += dv * k / steps;
            Line(p, run.U, run.V, width, ref y0, ref y1);
        }

        if (y1 >= y0) p.MarkRows(y0, y1);
        run.Volume -= step / scale * 6f;
        return run.Volume > 0;
    }

    private static void Line(StainPart p, float u, float v, float r, ref int y0, ref int y1)
    {
        var ri = (int)MathF.Ceiling(r);
        for (var dy = -ri; dy <= ri; dy++)
        {
            var y = (int)v + dy;
            if (y < 0 || y >= p.H) continue;
            for (var dx = -ri; dx <= ri; dx++)
            {
                if (dx * dx + dy * dy > r * r) continue;
                var x = (((int)u + dx) % p.W + p.W) % p.W;
                var idx = y * p.W + x;
                if (p.Map.EntryOf[idx] < 0) continue;
                Add(p, idx, 0.85f, ref y0, ref y1);
            }
        }
    }

    private static void Drip(StainPart p, InkRun run)
    {
        if (run.LastWorld == default) return;
        DropFalls?.Invoke(run.LastWorld, FM.Clamp(run.Volume, 0.3f, 1.2f), p);
    }

    // =====================================================================================================
    // Drying, composing, uploading
    // =====================================================================================================

    /// <summary>Per frame: runs, a slice of drying, compose and upload changed textures (within a budget).</summary>
    /// <summary>Dev: skip one stage of the stains' tick ("runs", "dry", "upload") to compare frame times.</summary>
    public static string DevSkip = "";

    public static void Tick()
    {
        if (Parts.Count == 0 || InkWorld.DevOff || InkWorld.DevStainsOff) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (DevSkip != "runs") UpdateRuns();
        InkWorld.Prof.Add(8, sw.Elapsed.TotalMilliseconds);
        sw.Restart();
        if (DevSkip != "dry") DryPass(24000);
        if (DevSkip == "upload") return;
        InkWorld.Prof.Add(9, sw.Elapsed.TotalMilliseconds);
        sw.Restart();

        // Only the rows that changed go to the graphics card, a few hundred kilobytes a frame at most: sending a
        // whole big texture at once stalls the frame (a quarter of a second for the largest).
        var budget = 384 * 1024;
        var now = InkClock.Now;
        for (var tries = 0; tries < Parts.Count && budget > 0; tries++)
        {
            _uploadCursor = (_uploadCursor + 1) % Parts.Count;
            var p = Parts[_uploadCursor];
            // Fresh ink shows at up to 30 frames a second (a big change carries on over the next frames).
            // (drying is slow and subtle: sending it more often only loads the graphics card)
            var interval = now - p.LastInk < 1f || p.UpY1 >= p.UpY0 ? 1f / 30f : 1.5f;
            if (!p.Dirty || now - p.LastUpload < interval) continue;
            Compose(p);
            budget -= UploadRows(p, budget);
        }

        InkWorld.Prof.Add(10, sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>Wet ink dries (glossy black → matte), and dry ink fades back to the chapter's base, a slice at a time.</summary>
    private static void DryPass(int texelBudget)
    {
        var now = InkClock.Now;
        var fadeMinutes = Math.Max(0.5f, ModConfig.InkFadeMinutes.Value);
        for (var tries = 0; tries < Parts.Count && texelBudget > 0; tries++)
        {
            var p = Parts[_passCursor % Parts.Count];
            if (!p.Inked)
            {
                _passCursor++;
                continue;
            }

            if (p.LastPass <= 0) p.LastPass = now;
            if (now < p.InInkUntil)
            {
                p.LastPass = now;
                p.PassRow = 0;
                _passCursor++;
                continue;
            }

            var dt = FM.Clamp(now - p.LastPass, 0f, 10f);
            var kw = MathF.Exp(-dt / 40f); // liquid for about a minute
            var kc = MathF.Pow(0.1f, dt / (fadeMinutes * 60f)); // a tenth left after the fade time
            var rows = Math.Max(1, texelBudget / p.W);
            var end = Math.Min(p.H, p.PassRow + rows);
            int y0 = int.MaxValue, y1 = -1;
            var cov = p.Cov;
            var wet = p.Wet;
            for (var y = p.PassRow; y < end; y++)
            {
                var row = y * p.W;
                for (var i = row; i < row + p.W; i++)
                {
                    int c = cov[i];
                    if (c == 0) continue;
                    p.PassAny = true;
                    int w = wet[i];
                    // Liquid that dries becomes dry ink (still there); dry ink slowly fades.
                    var nw = Math.Min(w, (int)(w * kw + NextRand()));
                    var nc = Math.Max(nw, Math.Min(c, (int)(nw + (c - nw) * kc + NextRand())));
                    p.PassWet += nw;
                    if (nw == w && nc == c) continue;
                    wet[i] = (byte)nw;
                    cov[i] = (byte)nc;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
            }

            texelBudget -= (end - p.PassRow) * p.W;
            if (y1 >= y0)
            {
                if (y0 < p.DirtyY0) p.DirtyY0 = y0;
                if (y1 > p.DirtyY1) p.DirtyY1 = y1;
                p.Dirty = true;
            }

            p.PassRow = end;
            if (end < p.H) break;

            // A whole pass done: what's left of the wet ink, and whether any ink is left at all.
            p.PassRow = 0;
            p.LastPass = now;
            p.WetAmount = p.PassWet * (0.002f / 255f);
            p.Inked = p.PassAny;
            p.PassWet = 0;
            p.PassAny = false;
            _passCursor++;
        }
    }

    private static void Compose(StainPart p)
    {
        if (p.DirtyY1 < p.DirtyY0) return;
        var y0 = Math.Max(0, p.DirtyY0);
        var y1 = Math.Min(p.H - 1, p.DirtyY1);
        var px = p.Px;
        var bs = p.Base;
        for (var i = y0 * p.W; i < (y1 + 1) * p.W; i++)
        {
            int c = p.Cov[i];
            var b = bs[i];
            if (c == 0)
            {
                px[i] = b;
                continue;
            }

            var wf = p.Wet[i] * 256 / c; // how much of it is still liquid, 0..256
            var ir = DryInk.r + ((WetInk.r - DryInk.r) * wf >> 8);
            var ig = DryInk.g + ((WetInk.g - DryInk.g) * wf >> 8);
            var ib = DryInk.b + ((WetInk.b - DryInk.b) * wf >> 8);
            Color32 o = default;
            o.r = (byte)(b.r + ((ir - b.r) * c >> 8));
            o.g = (byte)(b.g + ((ig - b.g) * c >> 8));
            o.b = (byte)(b.b + ((ib - b.b) * c >> 8));
            o.a = 255;
            px[i] = o;
        }

        if (y0 < p.UpY0) p.UpY0 = y0;
        if (y1 > p.UpY1) p.UpY1 = y1;
        p.DirtyY0 = int.MaxValue;
        p.DirtyY1 = -1;
    }

    private static readonly Dictionary<long, Texture2D> Staging = new();
    private static int _stagingTurn;
    private static readonly string[] DevUploads = new string[6];
    private static int _devUp;

    /// <summary>Dev: the last few uploads (for chasing frame hitches).</summary>
    public static string DevLastUploads() => $"frame {Time.frameCount}: " + string.Join(" | ", DevUploads);

    /// <summary>Sends the next band of changed rows (within a byte budget) to the GPU; returns the bytes sent.</summary>
    private static int UploadRows(StainPart p, int budget)
    {
        if (p.Tex == null || p.UpY1 < p.UpY0)
        {
            p.Dirty = false;
            return 0;
        }

        if (SystemInfo.copyTextureSupport == UnityEngine.Rendering.CopyTextureSupport.None)
        {
            Upload(p);
            p.UpY0 = int.MaxValue;
            p.UpY1 = -1;
            return p.W * p.H * 4;
        }

        var rowBytes = p.W * 4;
        var rows = Math.Min(p.UpY1 - p.UpY0 + 1, Math.Max(16, budget / rowBytes));
        var band = 16;
        while (band < rows) band <<= 1;
        if (band > p.H) band = p.H;
        var y0 = Math.Min(p.UpY0, p.H - band);
        // A few staging textures of each size, used in turn: refilling one the GPU is still copying from makes
        // the driver hold on to more and more copies, and every so often it stops to catch up.
        var key = ((long)p.W << 24) | ((long)band << 4) | (uint)(_stagingTurn++ & 3);
        if (!Staging.TryGetValue(key, out var st) || st == null)
        {
            st = new Texture2D(p.W, band, TextureFormat.RGBA32, false) { name = "InkVR_StainRows" };
            st.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            Object.DontDestroyOnLoad(st);
            Staging[key] = st;
        }

        var handle = GCHandle.Alloc(p.Px, GCHandleType.Pinned);
        try
        {
            st.LoadRawTextureData(handle.AddrOfPinnedObject() + y0 * rowBytes, band * rowBytes);
        }
        finally
        {
            handle.Free();
        }

        st.Apply(false, false);
        Graphics.CopyTexture(st, 0, 0, 0, 0, p.W, band, p.Tex, 0, 0, 0, y0);
        DevUploads[_devUp++ % DevUploads.Length] = $"{Time.frameCount}:{p.T.name} {p.W}x{p.H} rows {y0}+{band}";
        p.UpY0 = y0 + band;
        if (p.UpY0 > p.UpY1)
        {
            p.UpY0 = int.MaxValue;
            p.UpY1 = -1;
            p.Dirty = false;
            p.LastUpload = InkClock.Now;
        }

        return band * rowBytes;
    }

    private static void Upload(StainPart p)
    {
        if (p.Tex == null) return;
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
        p.LastUpload = InkClock.Now;
    }

    /// <summary>Smooth blotchy noise over a texture (0..255): ragged edges and lines.</summary>
    private static byte[] NoiseMap(int w, int h)
    {
        const int N = 64;
        var r = new System.Random(77 + w * 31 + h);
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
                var top = lattice[r0 + c0] + (lattice[r0 + c1] - lattice[r0 + c0]) * tx;
                var bottom = lattice[r1 + c0] + (lattice[r1 + c1] - lattice[r1 + c0]) * tx;
                map[y * w + x] = (byte)(FM.Clamp01(top + (bottom - top) * ty) * 255f);
            }
        }

        return map;
    }
}
