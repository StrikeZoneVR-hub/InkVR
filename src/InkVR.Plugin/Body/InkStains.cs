using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using InkVR.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace InkVR.Body;

/// <summary>One paintable piece of Henry (a finger segment, the palm, a sleeve...) with its own texture.</summary>
public sealed class StainPart
{
    public Transform T;
    public Renderer R;
    public Material Mat;
    public Texture2D Tex;
    public string BaseName;
    public bool Sleeve;
    public int W, H;
    public Color32[] Base, Px;
    public Vector3[] Verts, Normals;
    public Vector2[] Uvs;
    public float UPerMetre, VPerMetre;
    public bool Dirty, Stained;
    public float LastSplat = -10;
}

/// <summary>
/// Ink that gets onto Henry from the world: each touch paints an irregular wet blot (with a run of ink
/// down towards the floor) into that part's own copy of its texture. Stains slowly dry and fade back to
/// the chapter's base level, so the hands tell the story of the last few minutes rather than going black.
/// </summary>
public static class InkStains
{
    public static readonly List<StainPart> Parts = new();
    public static int SplatCount;
    private static readonly Random Rng = new(4242);
    private static readonly Color32 Ink = new(11, 8, 6, 255);
    private static int _fadeCursor, _uploadCursor;
    private static float _nextFade;

    /// <summary>Gives a hand part its own material and paintable texture (null if ink is off or the base is unknown).</summary>
    public static StainPart Register(Transform t, Renderer r, Material shared, MeshBuilder mb, bool sleeve)
    {
        var tex = shared.mainTexture;
        if (tex == null || !HenryTextures.Pixels.TryGetValue(tex.name, out var basePx)) return null;

        var p = new StainPart
        {
            T = t, R = r, BaseName = tex.name, Sleeve = sleeve, W = basePx.W, H = basePx.H,
            Base = basePx.Px, Px = (Color32[])basePx.Px.Clone(), Verts = mb.VertexArray, Normals = mb.NormalArray, Uvs = mb.UvArray,
        };

        // How many texels a metre of surface covers, around (u) and along (v) the part.
        var min = Vector3.one * float.MaxValue;
        var max = Vector3.one * float.MinValue;
        foreach (var v in p.Verts)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        var size = max - min;
        var diameter = Mathf.Max(0.01f, (size.x + size.y) * 0.5f);
        p.UPerMetre = 1f / (Mathf.PI * diameter);
        p.VPerMetre = 1f / Mathf.Max(0.01f, size.z);

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
            p.Dirty = true;
        }
    }

    /// <summary>Match a shader swap on the shared materials, keeping each part's own texture.</summary>
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

    /// <summary>Paint a blot where <paramref name="worldPoint"/> touches the part (radius in metres of real hand).</summary>
    public static void Splat(StainPart p, Vector3 worldPoint, float radius, float strength, bool runs = true)
    {
        if (p?.Mat == null || !ModConfig.InkOnHands.Value) return;
        var local = p.T.InverseTransformPoint(worldPoint);
        var best = -1;
        var bestD = float.MaxValue;
        for (var i = 0; i < p.Verts.Length; i++)
        {
            var d = (p.Verts[i] - local).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        if (best < 0) return;
        SplatAtUv(p, p.Uvs[best], radius, strength, runs);
    }

    /// <summary>Everything of the part below the ink's surface gets coated (dipping a hand in).</summary>
    public static int Coat(StainPart p, float surfaceWorldY, float strength)
    {
        if (p?.Mat == null || !ModConfig.InkOnHands.Value) return 0;
        var n = 0;
        var m = p.T.localToWorldMatrix;
        for (var i = 0; i < p.Verts.Length; i += 2)
        {
            if (m.MultiplyPoint3x4(p.Verts[i]).y > surfaceWorldY) continue;
            SplatAtUv(p, p.Uvs[i], 0.006f, strength, false, blobs: 1);
            n++;
        }

        return n;
    }

    private static void SplatAtUv(StainPart p, Vector2 uv, float radius, float strength, bool runs, int blobs = -1)
    {
        var cx = Frac(uv.x) * p.W;
        var cy = Mathf.Clamp01(uv.y) * (p.H - 1);
        var rx = Mathf.Max(1.5f, radius * p.UPerMetre * p.W);
        var ry = Mathf.Max(1.5f, radius * p.VPerMetre * p.H);

        // A main blob, a few lobes and flecks: the same hand-drawn splat the game's ink decals use.
        Blob(p, cx, cy, rx, ry, strength);
        var lobes = blobs >= 0 ? blobs : 3 + Rng.Next(3);
        for (var i = 0; i < lobes; i++)
        {
            var a = Rng.NextDouble() * Math.PI * 2;
            var d = 0.55f + (float)Rng.NextDouble() * 0.5f;
            var s = 0.35f + (float)Rng.NextDouble() * 0.35f;
            Blob(p, cx + (float)Math.Cos(a) * rx * d, cy + (float)Math.Sin(a) * ry * d, rx * s, ry * s, strength);
        }

        if (blobs < 0)
            for (var i = 0; i < 3; i++)
            {
                var a = Rng.NextDouble() * Math.PI * 2;
                var d = 1.4f + (float)Rng.NextDouble();
                Blob(p, cx + (float)Math.Cos(a) * rx * d, cy + (float)Math.Sin(a) * ry * d, rx * 0.15f, ry * 0.15f, strength);
            }

        // Wet ink runs towards the floor: find which way along the part (v) is down right now.
        if (runs)
        {
            var downLocal = p.T.InverseTransformDirection(Vector3.down);
            var along = downLocal.z; // tubes run along local +Z, and v grows along it
            if (Mathf.Abs(along) > 0.25f)
            {
                var len = ry * (1.5f + (float)Rng.NextDouble() * 2.5f);
                var dir = Mathf.Sign(along);
                var steps = Mathf.CeilToInt(len);
                var w = rx * 0.32f;
                for (var s = 0; s <= steps; s++)
                {
                    var t = (float)s / Mathf.Max(1, steps);
                    Blob(p, cx + (float)Math.Sin(t * 3f) * w * 0.4f, cy + dir * t * len, w * (1f - 0.4f * t), w * (1f - 0.4f * t), strength);
                }

                Blob(p, cx, cy + dir * len, w * 0.9f, w * 0.9f, strength);
            }
        }

        p.Stained = true;
        p.Dirty = true;
        p.LastSplat = Time.unscaledTime;
        SplatCount++;
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
                var a = Mathf.Clamp01((1.15f - d) / 0.3f) * strength;
                var x = ((xx % p.W) + p.W) % p.W;
                var i = y * p.W + x;
                var c = p.Px[i];
                p.Px[i] = new Color32(
                    (byte)(c.r + (Ink.r - c.r) * a),
                    (byte)(c.g + (Ink.g - c.g) * a),
                    (byte)(c.b + (Ink.b - c.b) * a), 255);
            }
        }
    }

    private static float Frac(float v) => v - Mathf.Floor(v);

    /// <summary>Per frame: upload changed textures (a couple per frame) and let old ink dry and fade.</summary>
    public static void Tick()
    {
        if (Parts.Count == 0) return;

        // Drying: every half second one stained part fades a little towards its base.
        var minutes = Mathf.Max(0.5f, ModConfig.InkFadeMinutes.Value);
        if (Time.unscaledTime >= _nextFade)
        {
            _nextFade = Time.unscaledTime + 0.5f / Mathf.Max(1, CountStained());
            for (var tries = 0; tries < Parts.Count; tries++)
            {
                _fadeCursor = (_fadeCursor + 1) % Parts.Count;
                var p = Parts[_fadeCursor];
                if (!p.Stained || Time.unscaledTime - p.LastSplat < 8f) continue;
                // Each stained part is visited about twice a second: after `minutes` a tenth of the stain is left.
                Fade(p, 1f - Mathf.Pow(0.1f, 1f / (minutes * 120f)));
                break;
            }
        }

        for (var n = 0; n < 2; n++)
        {
            for (var tries = 0; tries < Parts.Count; tries++)
            {
                _uploadCursor = (_uploadCursor + 1) % Parts.Count;
                var p = Parts[_uploadCursor];
                if (!p.Dirty) continue;
                Upload(p);
                break;
            }
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

    private static void Fade(StainPart p, float k)
    {
        var any = false;
        for (var i = 0; i < p.Px.Length; i++)
        {
            var c = p.Px[i];
            var b = p.Base[i];
            if (c.r == b.r && c.g == b.g && c.b == b.b) continue;
            any = true;
            p.Px[i] = new Color32(Step(c.r, b.r, k), Step(c.g, b.g, k), Step(c.b, b.b, k), 255);
        }

        p.Stained = any;
        p.Dirty = true;
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
    }
}
