using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// Where every texel of a piece's texture lies on the piece: the triangle it's on and its barycentric weights.
/// With it, ink can be put exactly where something touches in the world (a drop, a pool's surface, a stream),
/// whatever the texture's layout. Built once per layout on a worker thread (left and right hands share one).
/// </summary>
public sealed class TexelMap
{
    public int W, H, Count;

    /// <summary>Entries of triangle t are [TriStart[t], TriStart[t + 1]).</summary>
    public int[] TriStart;

    /// <summary>Per entry: the texel (y * W + x) and the weights of the triangle's first two corners (0..255).</summary>
    public int[] Texel;

    public byte[] B0, B1;

    /// <summary>Per texel: its entry, or -1 where the texture isn't on the surface.</summary>
    public int[] EntryOf;

    public volatile bool Ready;

    private static readonly Dictionary<string, TexelMap> Cache = new();

    public static TexelMap For(int w, int h, Vector2[] uvs, int[] tris)
    {
        var key = Key(w, h, uvs, tris);
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var have)) return have;
            var map = new TexelMap { W = w, H = h };
            Cache[key] = map;
            var u = (Vector2[])uvs.Clone();
            var t = (int[])tris.Clone();
            Task.Run(() =>
            {
                try
                {
                    map.Build(u, t);
                }
                catch (System.Exception e)
                {
                    Plugin.L.LogWarning("Ink texel map failed: " + e.Message);
                }
            });
            return map;
        }
    }

    private static string Key(int w, int h, Vector2[] uvs, int[] tris)
    {
        unchecked
        {
            var hash = 17;
            for (var i = 0; i < uvs.Length; i++) hash = hash * 31 + uvs[i].GetHashCode();
            for (var i = 0; i < tris.Length; i += 7) hash = hash * 31 + tris[i];
            return $"{w}x{h}:{uvs.Length}:{tris.Length}:{hash}";
        }
    }

    private static float Wrap(float d, int w)
    {
        if (d > w * 0.5f) d -= w;
        else if (d < -w * 0.5f) d += w;
        return d;
    }

    // Runs on a worker thread: only plain .NET here (Unity's own methods call into the game's runtime, which
    // crashes on a thread it doesn't know).
    private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

    private void Build(Vector2[] uvs, int[] tris)
    {
        int w = W, h = H;
        var entryOf = new int[w * h];
        for (var i = 0; i < entryOf.Length; i++) entryOf[i] = -1;
        var nt = tris.Length / 3;
        var start = new int[nt + 1];
        var texel = new List<int>(w * h / 2);
        var b0 = new List<byte>(w * h / 2);
        var b1 = new List<byte>(w * h / 2);
        for (var t = 0; t < nt; t++)
        {
            start[t] = texel.Count;
            int ia = tris[t * 3], ib = tris[t * 3 + 1], ic = tris[t * 3 + 2];
            var ax = uvs[ia].x * w;
            var ay = Clamp01(uvs[ia].y) * (h - 1);
            var bx = ax + Wrap((uvs[ib].x - uvs[ia].x) * w, w);
            var by = Clamp01(uvs[ib].y) * (h - 1);
            var cx = ax + Wrap((uvs[ic].x - uvs[ia].x) * w, w);
            var cy = Clamp01(uvs[ic].y) * (h - 1);
            var area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
            if (Math.Abs(area) < 1e-6f) continue;
            var inv = 1f / area;
            float w0x = (by - cy) * inv, w0y = (cx - bx) * inv, w0c = (bx * cy - cx * by) * inv;
            float w1x = (cy - ay) * inv, w1y = (ax - cx) * inv, w1c = (cx * ay - ax * cy) * inv;
            var x0 = (int)MathF.Floor(Math.Min(ax, Math.Min(bx, cx))) - 1;
            var x1 = (int)MathF.Ceiling(Math.Max(ax, Math.Max(bx, cx))) + 1;
            var y0 = Math.Max(0, (int)MathF.Floor(Math.Min(ay, Math.Min(by, cy))) - 1);
            var y1 = Math.Min(h - 1, (int)MathF.Ceiling(Math.Max(ay, Math.Max(by, cy))) + 1);
            // Texel centres just outside a triangle's edge still belong to it, so seams have no gaps.
            const float eps = -0.08f;
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
                    var idx = y * w + ((x % w) + w) % w;
                    if (entryOf[idx] != -1) continue;
                    w0 = Math.Max(0, w0);
                    w1 = Math.Max(0, w1);
                    w2 = Math.Max(0, w2);
                    var s = w0 + w1 + w2;
                    entryOf[idx] = texel.Count;
                    texel.Add(idx);
                    b0.Add((byte)(int)MathF.Round(w0 / s * 255f));
                    b1.Add((byte)(int)MathF.Round(w1 / s * 255f));
                }
            }
        }

        start[nt] = texel.Count;
        TriStart = start;
        Texel = texel.ToArray();
        B0 = b0.ToArray();
        B1 = b1.ToArray();
        EntryOf = entryOf;
        Count = Texel.Length;
        Ready = true;
    }

    /// <summary>The triangle an entry belongs to.</summary>
    public int TriangleOf(int entry)
    {
        int lo = 0, hi = TriStart.Length - 2;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (TriStart[mid] <= entry) lo = mid;
            else hi = mid - 1;
        }

        return lo;
    }
}
