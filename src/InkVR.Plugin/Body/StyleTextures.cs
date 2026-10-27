using System;
using InkVR.UI;
using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// The new styles' textures, painted at startup in the game's hand-inked look: clothes, fur, ink skins, gloves and
/// faces. Tubes wrap u once round (the front at u = 0.25) with v up their length; heads wrap the same way with the
/// top at v = 1. Every one is kept with its pixels (HenryTextures.Keep), so ink from the world stains it.
/// </summary>
public static class StyleTextures
{
    private static readonly Color Ink = HenryTextures.Ink;
    private static readonly Color White = new(0.96f, 0.93f, 0.86f);

    private static InkCanvas Canvas(int w, int h, Color fill, int seed) => new(w, h, fill, seed);

    private static Texture2D Done(InkCanvas c, string name, float grain = 0.07f)
    {
        c.Grain(grain);
        return HenryTextures.Keep(c, name);
    }

    // ---------------- painting helpers ----------------

    private static float Front(float u) => Mathf.Cos((u - 0.25f) * Mathf.PI * 2) * 0.5f + 0.5f; // 1 at the front, 0 at the back

    /// <summary>Cartoon two-tone: flat colour across the front, a hard step of shadow round the back.</summary>
    private static void Cel(InkCanvas c, float strength, float rim = 0f)
    {
        for (var x = 0; x < c.W; x++)
        {
            var f = Front((float)x / c.W);
            var k = f > 0.36f ? 1f : f > 0.3f ? 1f - strength * 0.5f : 1f - strength;
            // a glossy rim light on the lit side (for the shiny cartoon blacks)
            var lit = rim > 0 && f > 0.62f && f < 0.7f ? rim : 0f;
            for (var y = 0; y < c.H; y++)
            {
                var p = c.Px[y * c.W + x];
                c.Px[y * c.W + x] = new Color(p.r * k + lit, p.g * k + lit, p.b * k + lit, 1);
            }
        }
    }

    /// <summary>A filled, soft-edged ellipse (rotated by angle, radians).</summary>
    private static void Ellipse(InkCanvas c, float cx, float cy, float rx, float ry, Color col, float alpha = 1f, float angle = 0f)
    {
        var r = Mathf.Max(rx, ry) + 2;
        var ca = Mathf.Cos(angle);
        var sa = Mathf.Sin(angle);
        for (var y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
        for (var x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
        {
            float dx = x - cx, dy = y - cy;
            var lx = dx * ca + dy * sa;
            var ly = -dx * sa + dy * ca;
            var d = Mathf.Sqrt(lx * lx / (rx * rx) + ly * ly / (ry * ry));
            var a = Mathf.Clamp01((1f - d) * Mathf.Min(rx, ry) + 0.5f);
            if (a > 0) c.Blend(x, y, col, a * alpha);
        }
    }

    /// <summary>A filled polygon (even-odd), for pie-cuts, bows and teeth.</summary>
    private static void Poly(InkCanvas c, Vector2[] pts, Color col, float alpha = 1f)
    {
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in pts)
        {
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }

        var xs = new System.Collections.Generic.List<float>();
        for (var y = Mathf.FloorToInt(minY); y <= Mathf.CeilToInt(maxY); y++)
        {
            xs.Clear();
            var sy = y + 0.5f;
            for (var i = 0; i < pts.Length; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Length];
                if ((a.y <= sy && b.y > sy) || (b.y <= sy && a.y > sy)) xs.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x));
            }

            xs.Sort();
            for (var i = 0; i + 1 < xs.Count; i += 2)
                for (var x = Mathf.RoundToInt(xs[i]); x <= Mathf.RoundToInt(xs[i + 1]); x++)
                    c.Blend(x, y, col, alpha);
        }
    }

    private static void Outline(InkCanvas c, Vector2[] pts, float width, float alpha = 1f)
    {
        for (var i = 0; i < pts.Length; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Length];
            c.Stroke(a.x, a.y, b.x, b.y, width, Ink, 0.3f, alpha);
        }
    }

    private static void Stripes(InkCanvas c, float every, float width, Color col, float alpha, bool vertical = true)
    {
        if (vertical)
            for (var x = 0f; x < c.W; x += every)
                c.Stroke(x, 0, x + c.Rand(-1, 1), c.H, width, col, 0.15f, alpha);
        else
            for (var y = 0f; y < c.H; y += every)
                c.Stroke(0, y, c.W, y + c.Rand(-1, 1), width, col, 0.15f, alpha);
    }

    private static void Spatter(InkCanvas c, float amount, float vMin = 0f, float vMax = 1f, float size = 6f)
    {
        var n = Mathf.RoundToInt(30 * amount);
        for (var i = 0; i < n; i++) c.InkBlot(c.Rand(0, c.W), c.Rand(c.H * vMin, c.H * vMax), c.Rand(size * 0.5f, size * 1.6f), Ink);
    }

    /// <summary>Ink runs down from the top (v = 1), as on the studio's walls.</summary>
    private static void RunsDown(InkCanvas c, int count, float maxLen, float width, Color col)
    {
        for (var i = 0; i < count; i++)
        {
            var x = c.Rand(0, c.W);
            var len = c.Rand(maxLen * 0.3f, maxLen);
            var w = c.Rand(width * 0.5f, width);
            c.Stroke(x, c.H, x + c.Rand(-2, 2), c.H - len, w, col, 0.2f);
            c.Dot(x, c.H - len, w * 0.7f, col);
        }
    }

    private static void Gloss(InkCanvas c, float u, float width, float strength, float vMin = 0f, float vMax = 1f)
    {
        var x0 = u * c.W;
        for (var y = Mathf.FloorToInt(c.H * vMin); y < Mathf.Min(c.H, Mathf.CeilToInt(c.H * vMax)); y++)
            c.Stroke(x0 - width * 2, y, x0 + width * 2, y, 1f, new Color(1, 1, 1, 1), 0f, strength * c.Rand(0.4f, 1f) / 4f);
    }

    // ---------------- skins and fur ----------------

    /// <summary>A cartoon's bare arm or body colour: flat, a hard shadow, an optional glossy rim (the blacks shine).</summary>
    public static Texture2D CartoonSkin(string name, Color col, float shade, float rim, int seed)
    {
        var c = Canvas(256, 128, col, seed);
        Cel(c, shade, rim);
        return Done(c, name, 0.04f);
    }

    /// <summary>Boris's fur: pale grey, a little tufted, the hard cartoon shadow.</summary>
    public static Texture2D Fur(string name, Color col, int seed)
    {
        var c = Canvas(256, 256, col, seed);
        for (var i = 0; i < 220; i++)
        {
            var x = c.Rand(0, c.W);
            var y = c.Rand(0, c.H);
            c.Stroke(x, y, x + c.Rand(-3, 3), y - c.Rand(4, 10), 0.8f, col * 0.7f, 0.2f, 0.4f);
        }

        Cel(c, 0.3f);
        return Done(c, name, 0.06f);
    }

    /// <summary>Living ink: near-black, wet highlights, runs and (for the goo) bubbles.</summary>
    public static Texture2D InkSkin(string name, Color tint, float wet, bool bubbles, int seed, int w = 256, int h = 256)
    {
        var c = Canvas(w, h, tint, seed);
        for (var i = 0; i < 18; i++)
        {
            var x = c.Rand(0, c.W);
            c.Stroke(x, c.H, x + c.Rand(-4, 4), c.Rand(0, c.H * 0.6f), c.Rand(2, 6), tint * 0.55f, 0.3f, 0.8f);
        }

        // wet highlights along the lit side
        for (var i = 0; i < 26 * wet; i++)
        {
            var x = c.W * 0.31f + c.Rand(-c.W * 0.05f, c.W * 0.05f);
            var y = c.Rand(0, c.H);
            c.Stroke(x, y, x + c.Rand(-1, 1), y - c.Rand(6, 22), c.Rand(0.8f, 2f), new Color(0.75f, 0.72f, 0.68f), 0.2f, c.Rand(0.15f, 0.45f));
        }

        if (bubbles)
            for (var i = 0; i < 40; i++)
            {
                var x = c.Rand(0, c.W);
                var y = c.Rand(0, c.H);
                var r = c.Rand(2, 6);
                c.Dot(x, y, r, tint * 0.6f);
                c.Dot(x - r * 0.3f, y + r * 0.3f, r * 0.3f, new Color(0.7f, 0.68f, 0.64f), 0.6f);
            }

        Cel(c, 0.35f, 0.05f * wet);
        return Done(c, name, 0.05f);
    }

    /// <summary>A work glove (Wally's): tan leather with seams and a darker, worn palm.</summary>
    public static Texture2D WorkGlove(string name, bool palm, int seed)
    {
        var tan = new Color(0.62f, 0.45f, 0.27f);
        var c = Canvas(256, 128, palm ? tan * 0.8f : tan, seed);
        Stripes(c, 64, 1.2f, Ink, 0.55f);
        for (var i = 0; i < 40; i++) c.Dot(c.Rand(0, c.W), c.Rand(0, c.H), c.Rand(1, 3), tan * 0.6f, 0.4f);
        if (palm) Spatter(c, 0.4f, 0.2f, 0.9f, 5f);
        Cel(c, 0.25f);
        return Done(c, name);
    }

    // ---------------- clothes ----------------

    public struct Cloth
    {
        public Color Base;
        public float Shade, Rim;
        public float Pinstripe; // stripe spacing in px (0: none)
        public Color StripeColor;
        public bool Placket, Pocket, Patches, Torn, Stitches, Buttons;
        public float Ink; // ink spatter/runs
        public int Seed;
    }

    public static Texture2D Fabric(string name, Cloth o, int w = 256, int h = 256)
    {
        var c = Canvas(w, h, o.Base, o.Seed);
        if (o.Pinstripe > 0) Stripes(c, o.Pinstripe, 0.7f, o.StripeColor, 0.5f);
        // weave
        for (var i = 0; i < 400; i++)
        {
            var x = c.Rand(0, c.W);
            var y = c.Rand(0, c.H);
            c.Stroke(x, y, x + c.Rand(2, 5), y, 0.5f, o.Base * 0.8f, 0.1f, 0.25f);
        }

        var front = c.W * 0.25f;
        if (o.Placket)
        {
            c.Stroke(front, 0, front, c.H, 1.4f, Ink, 0.2f, 0.7f);
            if (o.Buttons)
                for (var y = c.H * 0.15f; y < c.H * 0.95f; y += c.H * 0.14f)
                {
                    c.Dot(front + 4, y, 3.2f, new Color(0.82f, 0.66f, 0.3f));
                    c.Dot(front + 4, y, 1.2f, Ink, 0.7f);
                }
        }

        if (o.Pocket)
        {
            var px = front + c.W * 0.08f;
            var py = c.H * 0.68f;
            var pocket = new[] { new Vector2(px - 12, py - 16), new Vector2(px + 12, py - 16), new Vector2(px + 12, py + 10), new Vector2(px - 12, py + 10) };
            Poly(c, pocket, o.Base * 0.88f);
            Outline(c, pocket, 1.2f, 0.8f);
        }

        if (o.Patches)
            for (var i = 0; i < 3; i++)
            {
                var px = c.Rand(0, c.W);
                var py = c.Rand(c.H * 0.1f, c.H * 0.8f);
                var s = c.Rand(10, 18);
                var patch = new[] { new Vector2(px - s, py - s * 0.8f), new Vector2(px + s, py - s), new Vector2(px + s * 0.9f, py + s), new Vector2(px - s, py + s * 0.9f) };
                Poly(c, patch, o.Base * c.Rand(0.75f, 1.15f));
                for (var k = 0; k < patch.Length; k++)
                {
                    var a = patch[k];
                    var b = patch[(k + 1) % patch.Length];
                    for (var t = 0f; t < 1f; t += 0.12f)
                    {
                        var p = Vector2.Lerp(a, b, t);
                        c.Stroke(p.x - 1.5f, p.y - 1.5f, p.x + 1.5f, p.y + 1.5f, 0.6f, Ink, 0f, 0.8f);
                    }
                }
            }

        if (o.Stitches) Stripes(c, c.W / 4f, 0.6f, Ink, 0.35f);
        if (o.Torn)
            for (var i = 0; i < 6; i++)
            {
                var x = c.Rand(0, c.W);
                c.Stroke(x, 0, x + c.Rand(-6, 6), c.Rand(10, 34), c.Rand(2, 5), Ink, 0.6f, 0.9f);
            }

        if (o.Ink > 0)
        {
            Spatter(c, o.Ink, 0f, 1f, 6f);
            RunsDown(c, Mathf.RoundToInt(10 * o.Ink), c.H * 0.6f, 4f, Ink);
        }

        Cel(c, o.Shade, o.Rim);
        return Done(c, name);
    }

    public static Texture2D Shoe(string name, Color col, float gloss, int seed)
    {
        var c = Canvas(256, 128, col, seed);
        c.Stroke(0, c.H * 0.18f, c.W, c.H * 0.18f, 3f, Ink, 0.2f, 0.9f); // the sole's edge
        if (gloss > 0) Gloss(c, 0.3f, 3f, gloss, 0.45f, 0.9f);
        Cel(c, 0.3f, 0.04f * gloss);
        return Done(c, name, 0.04f);
    }

    /// <summary>A white cartoon bow (Bendy's tie, Alice's bow): flat white, inked folds.</summary>
    public static Texture2D Bow(string name, Color col, int seed)
    {
        var c = Canvas(64, 64, col, seed);
        c.Stroke(32, 4, 32, 60, 1.5f, Ink, 0.3f, 0.6f);
        c.Stroke(14, 20, 26, 32, 1f, Ink, 0.3f, 0.5f);
        c.Stroke(50, 20, 38, 32, 1f, Ink, 0.3f, 0.5f);
        return Done(c, name, 0.03f);
    }

    public static Texture2D Rope(string name, int seed)
    {
        var c = Canvas(64, 64, new Color(0.62f, 0.5f, 0.32f), seed);
        for (var x = -64f; x < 64; x += 8) c.Stroke(x, 0, x + 64, 64, 2.2f, Ink, 0.2f, 0.45f);
        return Done(c, name);
    }

    public static Texture2D Metal(string name, Color col, float gloss, int seed)
    {
        var c = Canvas(128, 128, col, seed);
        for (var i = 0; i < 30; i++)
        {
            var y = c.Rand(0, c.H);
            c.Stroke(0, y, c.W, y + c.Rand(-2, 2), 0.6f, col * 0.75f, 0.1f, 0.35f);
        }

        for (var i = 0; i < 8; i++) c.Dot(c.Rand(0, c.W), c.Rand(0, c.H), 2.2f, col * 0.55f, 0.8f); // rivets
        Gloss(c, 0.3f, 3f, gloss);
        Cel(c, 0.3f, 0.05f);
        return Done(c, name, 0.04f);
    }

    /// <summary>Flat, unshaded light (a lens, a halo, glowing eyes): the shader still shades it, so it's painted bright.</summary>
    public static Texture2D Glow(string name, Color col) => HenryTextures.Flat(col, name);

    // ---------------- faces (painted onto a head's ellipsoid: front at u = 0.25, eyes a little above the middle) ----------------

    private const int FW = 512, FH = 256;
    private static float FX(float u) => u * FW;
    private static float FY(float v) => v * FH;

    /// <summary>A pie-cut cartoon eye: a black oval with a wedge cut out of it, the wedge towards the face's middle.</summary>
    private static void PieEye(InkCanvas c, float u, float v, float rx, float ry, float side, Color eye, Color cut)
    {
        Ellipse(c, FX(u), FY(v), rx, ry, eye);
        var cx = FX(u);
        var cy = FY(v);
        var wedge = new[] { new Vector2(cx, cy), new Vector2(cx - side * rx * 1.4f, cy + ry * 0.55f), new Vector2(cx - side * rx * 0.55f, cy + ry * 1.4f) };
        Poly(c, wedge, cut);
    }

    /// <summary>The wide cartoon grin: a black band curving up at the ends, with teeth.</summary>
    private static void Grin(InkCanvas c, float u, float v, float halfWidth, float depth, Color mouth, Color teeth, bool teethShown)
    {
        var pts = new System.Collections.Generic.List<Vector2>();
        const int n = 16;
        for (var i = 0; i <= n; i++)
        {
            var t = i / (float)n * 2 - 1;
            pts.Add(new Vector2(FX(u) + t * halfWidth, FY(v) - depth * (1 - t * t) + Mathf.Abs(t) * depth * 0.15f));
        }

        for (var i = n; i >= 0; i--)
        {
            var t = i / (float)n * 2 - 1;
            pts.Add(new Vector2(FX(u) + t * halfWidth * 0.94f, FY(v) - depth * 0.35f * (1 - t * t) + depth * 0.25f));
        }

        var arr = pts.ToArray();
        Poly(c, arr, mouth);
        if (teethShown)
        {
            // a row of teeth along the top of the mouth
            for (var i = -3; i <= 3; i++)
            {
                var x = FX(u) + i * halfWidth * 0.24f;
                var tooth = new[] { new Vector2(x - halfWidth * 0.1f, FY(v) + depth * 0.2f), new Vector2(x + halfWidth * 0.1f, FY(v) + depth * 0.2f), new Vector2(x, FY(v) - depth * 0.35f) };
                Poly(c, tooth, teeth);
            }
        }

        Outline(c, arr, 1.6f, 0.9f);
    }

    /// <summary>Bendy's head: black, with the white face reaching up to a widow's peak, pie-cut eyes and the grin.</summary>
    public static Texture2D BendyHead(string name, bool demon, int seed)
    {
        var black = new Color(0.07f, 0.06f, 0.055f);
        var c = Canvas(FW, FH, black, seed);
        var face = demon ? new Color(0.07f, 0.06f, 0.055f) : White;
        // the face mask: wide at the cheeks, a peak at the top middle
        var mask = new System.Collections.Generic.List<Vector2>();
        for (var i = 0; i <= 24; i++)
        {
            var a = Mathf.PI * (1 + i / 24f); // the bottom half of an oval
            mask.Add(new Vector2(FX(0.25f) + Mathf.Cos(a) * FW * 0.105f, FY(0.5f) + Mathf.Sin(a) * FH * 0.3f));
        }

        mask.Add(new Vector2(FX(0.25f) + FW * 0.1f, FY(0.62f)));
        mask.Add(new Vector2(FX(0.25f) + FW * 0.035f, FY(0.66f)));
        mask.Add(new Vector2(FX(0.25f), FY(0.56f))); // the widow's peak
        mask.Add(new Vector2(FX(0.25f) - FW * 0.035f, FY(0.66f)));
        mask.Add(new Vector2(FX(0.25f) - FW * 0.1f, FY(0.62f)));
        if (!demon)
        {
            Poly(c, mask.ToArray(), face);
            PieEye(c, 0.225f, 0.53f, 10, 16, -1, black, face);
            PieEye(c, 0.275f, 0.53f, 10, 16, 1, black, face);
            Grin(c, 0.25f, 0.35f, FW * 0.07f, 22, black, White, true);
        }
        else
        {
            // the Ink Demon: no eyes (ink runs over them), the grin and its teeth
            Grin(c, 0.25f, 0.36f, FW * 0.075f, 24, new Color(0.03f, 0.02f, 0.02f), White, true);
            RunsDown(c, 30, FH * 0.7f, 7f, new Color(0.02f, 0.015f, 0.01f));
            Gloss(c, 0.31f, 2f, 0.5f, 0.3f, 0.95f);
        }

        Cel(c, 0.3f, 0.05f);
        return Done(c, name, 0.03f);
    }

    /// <summary>Boris: pale grey, pie-cut eyes, a black nose at the end of the muzzle (the muzzle is its own shape).</summary>
    public static Texture2D BorisHead(string name, bool brute, int seed)
    {
        var fur = brute ? new Color(0.24f, 0.22f, 0.21f) : new Color(0.78f, 0.77f, 0.74f);
        var c = Canvas(FW, FH, fur, seed);
        if (!brute)
        {
            PieEye(c, 0.228f, 0.58f, 9, 14, -1, Ink, fur);
            PieEye(c, 0.272f, 0.58f, 9, 14, 1, Ink, fur);
            c.Stroke(FX(0.21f), FY(0.68f), FX(0.235f), FY(0.7f), 2f, Ink, 0.2f); // brows
            c.Stroke(FX(0.29f), FY(0.68f), FX(0.265f), FY(0.7f), 2f, Ink, 0.2f);
        }
        else
        {
            // Brute Boris: one eye, the other stitched shut; ink running from the scalp
            Ellipse(c, FX(0.272f), FY(0.58f), 8, 10, new Color(0.9f, 0.88f, 0.8f));
            Ellipse(c, FX(0.272f), FY(0.57f), 3, 5, Ink);
            c.Stroke(FX(0.21f), FY(0.58f), FX(0.245f), FY(0.58f), 2.4f, Ink, 0.3f);
            for (var i = 0; i < 4; i++)
            {
                var x = FX(0.213f) + i * 9;
                c.Stroke(x, FY(0.54f), x + 3, FY(0.62f), 1.4f, new Color(0.85f, 0.8f, 0.7f), 0.2f);
            }

            RunsDown(c, 20, FH * 0.5f, 5f, Ink);
        }

        Cel(c, 0.3f);
        return Done(c, name, 0.05f);
    }

    /// <summary>Alice Angel: white face, pie-cut eyes with lashes, a small smile; her black hair is its own shape.</summary>
    public static Texture2D AliceHead(string name, int seed)
    {
        var c = Canvas(FW, FH, White, seed);
        foreach (var (u, side) in new[] { (0.226f, -1f), (0.274f, 1f) })
        {
            PieEye(c, u, 0.54f, 9, 15, side, Ink, White);
            for (var i = 0; i < 3; i++)
            {
                var x = FX(u) + side * (6 + i * 3);
                c.Stroke(x, FY(0.6f), x + side * 6, FY(0.66f) + i * 2, 1.6f, Ink, 0.2f); // lashes
            }
        }

        // a little smile, a beauty spot
        var smile = new System.Collections.Generic.List<Vector2>();
        for (var i = 0; i <= 10; i++)
        {
            var t = i / 10f * 2 - 1;
            smile.Add(new Vector2(FX(0.25f) + t * 18, FY(0.38f) - 6 * (1 - t * t)));
        }

        for (var i = 0; i < smile.Count - 1; i++) c.Stroke(smile[i].x, smile[i].y, smile[i + 1].x, smile[i + 1].y, 2.2f, Ink, 0.2f);
        c.Dot(FX(0.288f), FY(0.42f), 2f, Ink);
        Cel(c, 0.18f);
        return Done(c, name, 0.03f);
    }

    /// <summary>Sammy's mask: Bendy's face drawn on cardboard (flat, the edges torn), cream and ink.</summary>
    public static Texture2D SammyMask(string name, int seed)
    {
        var card = new Color(0.86f, 0.74f, 0.52f);
        var c = Canvas(256, 256, card, seed);
        // drawn like the studio's cut-outs, on a flat disc: face at the middle
        Ellipse(c, 128, 132, 70, 80, White);
        foreach (var side in new[] { -1f, 1f })
        {
            var ex = 128 + side * 30;
            Ellipse(c, ex, 150, 18, 28, Ink);
            Poly(c, new[] { new Vector2(ex, 150), new Vector2(ex - side * 25, 165), new Vector2(ex - side * 10, 180) }, White);
        }

        var mouth = new System.Collections.Generic.List<Vector2>();
        for (var i = 0; i <= 14; i++)
        {
            var t = i / 14f * 2 - 1;
            mouth.Add(new Vector2(128 + t * 52, 98 - 26 * (1 - t * t)));
        }

        for (var i = 14; i >= 0; i--)
        {
            var t = i / 14f * 2 - 1;
            mouth.Add(new Vector2(128 + t * 48, 104 - 8 * (1 - t * t)));
        }

        Poly(c, mouth.ToArray(), Ink);
        // horns at the top, the torn edge, a smear of ink
        Poly(c, new[] { new Vector2(70, 200), new Vector2(40, 250), new Vector2(92, 214) }, Ink);
        Poly(c, new[] { new Vector2(186, 200), new Vector2(216, 250), new Vector2(164, 214) }, Ink);
        for (var a = 0f; a < Mathf.PI * 2; a += 0.15f) c.Stroke(128 + Mathf.Cos(a) * 118, 128 + Mathf.Sin(a) * 118, 128 + Mathf.Cos(a + 0.15f) * 112, 128 + Mathf.Sin(a + 0.15f) * 112, 3f, Ink, 0.8f, 0.8f);
        RunsDown(c, 6, 90, 5f, Ink);
        return Done(c, name, 0.08f);
    }

    /// <summary>Joey Drew: a cartoon portrait's face, warm skin, the eyes and a moustache drawn on (the hair and the moustache's bulk are shapes).</summary>
    public static Texture2D JoeyHead(string name, bool wally, int seed)
    {
        var skin = wally ? new Color(0.86f, 0.66f, 0.46f) : new Color(0.88f, 0.7f, 0.52f);
        var c = Canvas(FW, FH, skin, seed);
        foreach (var side in new[] { -1f, 1f })
        {
            var u = 0.25f + side * 0.022f;
            Ellipse(c, FX(u), FY(0.56f), 6, 4, White);
            Ellipse(c, FX(u), FY(0.56f), 3, 3.6f, Ink);
            c.Stroke(FX(u) - 8, FY(0.63f) + (wally ? 2 : -1) * side, FX(u) + 8, FY(0.63f) - (wally ? 2 : -1) * side, 2.4f, wally ? new Color(0.45f, 0.3f, 0.15f) : new Color(0.55f, 0.55f, 0.55f), 0.2f); // brows
        }

        // a smile line, cheeks
        for (var i = 0; i < 8; i++)
        {
            var t = i / 7f * 2 - 1;
            var t2 = (i + 1) / 7f * 2 - 1;
            c.Stroke(FX(0.25f) + t * 16, FY(0.38f) - 5 * (1 - t * t), FX(0.25f) + t2 * 16, FY(0.38f) - 5 * (1 - t2 * t2), 1.6f, Ink, 0.2f);
        }

        Ellipse(c, FX(0.215f), FY(0.44f), 9, 5, new Color(0.9f, 0.55f, 0.45f), 0.35f);
        Ellipse(c, FX(0.285f), FY(0.44f), 9, 5, new Color(0.9f, 0.55f, 0.45f), 0.35f);
        if (wally)
            for (var i = 0; i < 14; i++)
                c.Dot(FX(0.25f) + c.Rand(-30, 30), FY(0.32f) + c.Rand(-10, 6), 1f, Ink, 0.4f); // stubble
        Cel(c, 0.22f);
        return Done(c, name, 0.04f);
    }

    /// <summary>A Searcher's head: lumpy black goo with two pale, empty eyes and a dark mouth that never closes.</summary>
    public static Texture2D SearcherHead(string name, int seed)
    {
        var c = Canvas(FW, FH, new Color(0.05f, 0.045f, 0.04f), seed);
        Ellipse(c, FX(0.232f), FY(0.56f), 7, 10, new Color(0.92f, 0.9f, 0.82f));
        Ellipse(c, FX(0.27f), FY(0.55f), 6, 9, new Color(0.92f, 0.9f, 0.82f));
        Ellipse(c, FX(0.25f), FY(0.38f), 16, 12, new Color(0.01f, 0.01f, 0.01f));
        RunsDown(c, 26, FH * 0.8f, 8f, new Color(0.02f, 0.02f, 0.015f));
        Gloss(c, 0.31f, 2f, 0.7f, 0.2f, 0.95f);
        Cel(c, 0.3f, 0.05f);
        return Done(c, name, 0.03f);
    }

    /// <summary>Hair (Alice's bob, Joey's slicked grey): a cap of colour with brushed ink strands.</summary>
    public static Texture2D Hair(string name, Color col, int seed)
    {
        var c = Canvas(256, 128, col, seed);
        for (var i = 0; i < 160; i++)
        {
            var x = c.Rand(0, c.W);
            c.Stroke(x, c.H, x + c.Rand(-6, 6), c.Rand(0, c.H * 0.6f), 0.8f, col * 0.6f, 0.4f, 0.5f);
        }

        Gloss(c, 0.3f, 2f, 0.4f, 0.4f, 0.9f);
        Cel(c, 0.3f, 0.03f);
        return Done(c, name, 0.04f);
    }

    /// <summary>The projector that is Norman's head: dark painted metal, rivets, a plate with the maker's name.</summary>
    public static Texture2D Projector(string name, int seed)
    {
        var c = Canvas(256, 256, new Color(0.2f, 0.19f, 0.17f), seed);
        Stripes(c, 32, 1f, Ink, 0.5f, false);
        for (var i = 0; i < 24; i++) c.Dot(c.Rand(0, c.W), c.Rand(0, c.H), 2.5f, new Color(0.45f, 0.42f, 0.36f), 0.9f);
        var plate = new[] { new Vector2(40, 100), new Vector2(110, 100), new Vector2(110, 130), new Vector2(40, 130) };
        Poly(c, plate, new Color(0.7f, 0.56f, 0.28f));
        Outline(c, plate, 1.2f);
        TheatreArt.InkText(c, "JDS", 52, 108, 14, 2f, Ink);
        RunsDown(c, 10, 120, 5f, Ink);
        Cel(c, 0.35f, 0.04f);
        return Done(c, name, 0.05f);
    }

    /// <summary>A cap's peak and crown (Wally's): blue-grey cloth with a stitched peak.</summary>
    public static Texture2D Cap(string name, int seed)
    {
        var c = Canvas(256, 128, new Color(0.3f, 0.36f, 0.42f), seed);
        Stripes(c, 32, 0.8f, Ink, 0.4f);
        Cel(c, 0.3f);
        return Done(c, name);
    }

    /// <summary>A brass key ring's keys, little studio keys.</summary>
    public static Texture2D Brass(string name, int seed) => Metal(name, new Color(0.78f, 0.6f, 0.28f), 0.6f, seed);

    // ---------------- the watches (dials are flat discs: the 12 at the top, the 3 to the right) ----------------

    /// <summary>A watch strap round the wrist (u round it, v across it): leather in a colour, stitched along both edges.</summary>
    public static Texture2D Strap(string name, Color col, Color stitch, int seed)
    {
        var c = Canvas(256, 64, col, seed);
        c.Stroke(0, 6, c.W, 6, 1.0f, stitch, 0.2f, 0.7f);
        c.Stroke(0, c.H - 6, c.W, c.H - 6, 1.0f, stitch, 0.2f, 0.7f);
        for (var x = 0; x < c.W; x += 64) c.Stroke(x, 0, x, c.H, 1.6f, Ink, 0.2f);
        Gloss(c, 0.3f, 2f, 0.3f, 0.2f, 0.8f);
        return Done(c, name, 0.08f);
    }

    /// <summary>A gold bracelet: links across the band, each with its own shine.</summary>
    public static Texture2D Links(string name, Color col, int seed)
    {
        var c = Canvas(256, 64, col, seed);
        for (var x = 0f; x < c.W; x += 12)
        {
            c.Stroke(x, 0, x, c.H, 1.4f, Ink, 0.1f, 0.8f);
            c.Stroke(x + 4, 8, x + 4, c.H - 8, 1.2f, Color.Lerp(col, Color.white, 0.5f), 0.1f, 0.6f);
        }

        c.Stroke(0, c.H * 0.5f, c.W, c.H * 0.5f, 1f, Ink, 0.1f, 0.4f);
        return Done(c, name, 0.04f);
    }

    private static void Centered(InkCanvas c, string text, float cx, float cy, float h, float stroke, Color col)
    {
        var w = text.Length * h * 0.62f + (text.Length - 1) * h * 0.22f;
        TheatreArt.InkText(c, text, cx - w * 0.5f, cy - h * 0.5f, h, stroke, col);
    }

    private static Vector2 Clock(float hours, float r) => new(128 + Mathf.Sin(hours / 12f * Mathf.PI * 2) * r, 128 + Mathf.Cos(hours / 12f * Mathf.PI * 2) * r);

    private static InkCanvas DialBase(Color face, Color ink, int seed, float rimWidth = 6f, bool minutes = true)
    {
        var c = Canvas(256, 256, face, seed);
        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
        {
            var dx = (x - 127.5f) / 128f;
            var dy = (y - 127.5f) / 128f;
            var k = 1f - 0.14f * Mathf.Clamp01(dx * dx + dy * dy);
            var p = c.Px[y * c.W + x];
            c.Px[y * c.W + x] = new Color(p.r * k, p.g * k, p.b * k, 1);
        }

        for (var a = 0f; a < Mathf.PI * 2; a += 0.04f)
            c.Stroke(128 + Mathf.Cos(a) * 121, 128 + Mathf.Sin(a) * 121, 128 + Mathf.Cos(a + 0.045f) * 121, 128 + Mathf.Sin(a + 0.045f) * 121, rimWidth, ink, 0.05f);
        if (minutes)
            for (var m = 0; m < 60; m++)
            {
                var o = Clock(m / 5f, 112);
                var i = Clock(m / 5f, m % 5 == 0 ? 96 : 105);
                c.Stroke(i.x, i.y, o.x, o.y, m % 5 == 0 ? 3.6f : 1.4f, ink, 0.05f);
            }

        return c;
    }

    private static void Quarters(InkCanvas c, string[] labels, float r, float h, float stroke, Color ink)
    {
        for (var q = 0; q < 4; q++)
        {
            var p = Clock(q * 3, r);
            Centered(c, labels[q], p.x, p.y, h, stroke, ink);
        }
    }

    /// <summary>Bendy's own watch, like the character watches sold with the cartoons: his face on the dial, his gloved arms for hands.</summary>
    public static Texture2D BendyDial(string name, int seed)
    {
        var c = DialBase(White, Ink, seed);
        Quarters(c, new[] { "12", "3", "6", "9" }, 80, 24, 3.4f, Ink);
        // his face in the middle, the hands' pin on his nose: the black head, the white mask with its peak, pie-cut eyes
        // above the pin and the grin below it
        const float cy = 130;
        Ellipse(c, 128, cy, 60, 58, Ink);
        Poly(c, new[] { new Vector2(94, cy + 34), new Vector2(76, cy + 82), new Vector2(110, cy + 48) }, Ink); // horns
        Poly(c, new[] { new Vector2(162, cy + 34), new Vector2(180, cy + 82), new Vector2(146, cy + 48) }, Ink);
        var mask = new System.Collections.Generic.List<Vector2>();
        for (var i = 0; i <= 24; i++)
        {
            var a = Mathf.PI * (1 + i / 24f);
            mask.Add(new Vector2(128 + Mathf.Cos(a) * 50, cy + 6 + Mathf.Sin(a) * 52));
        }

        mask.Add(new Vector2(176, cy + 26));
        mask.Add(new Vector2(150, cy + 40));
        mask.Add(new Vector2(128, cy + 24)); // the widow's peak
        mask.Add(new Vector2(106, cy + 40));
        mask.Add(new Vector2(80, cy + 26));
        Poly(c, mask.ToArray(), White);
        foreach (var side in new[] { -1f, 1f })
        {
            var ex = 128 + side * 17;
            Ellipse(c, ex, cy + 12, 9, 14, Ink);
            Poly(c, new[] { new Vector2(ex, cy + 12), new Vector2(ex - side * 12, cy + 18), new Vector2(ex - side * 5, cy + 28) }, White);
        }

        var grin = new System.Collections.Generic.List<Vector2>();
        for (var i = 0; i <= 14; i++)
        {
            var t = i / 14f * 2 - 1;
            grin.Add(new Vector2(128 + t * 34, cy - 14 - 20 * (1 - t * t)));
        }

        for (var i = 14; i >= 0; i--)
        {
            var t = i / 14f * 2 - 1;
            grin.Add(new Vector2(128 + t * 31, cy - 10 - 6 * (1 - t * t)));
        }

        Poly(c, grin.ToArray(), Ink);
        for (var i = -2; i <= 2; i++)
        {
            var x = 128 + i * 11f;
            Poly(c, new[] { new Vector2(x - 4, cy - 12), new Vector2(x + 4, cy - 12), new Vector2(x, cy - 20) }, White);
        }

        Centered(c, "BENDY", 128, 44, 13, 2f, Ink);
        return Done(c, name, 0.03f);
    }

    /// <summary>The studio's gold dress watch: Roman numerals, the JDS monogram and its motto under the hands.</summary>
    public static Texture2D StudioDial(string name, int seed)
    {
        var cream = new Color(0.95f, 0.89f, 0.72f);
        var gold = new Color(0.72f, 0.55f, 0.24f);
        var c = DialBase(cream, Ink, seed, 4f);
        for (var a = 0f; a < Mathf.PI * 2; a += 0.04f)
            c.Stroke(128 + Mathf.Cos(a) * 90, 128 + Mathf.Sin(a) * 90, 128 + Mathf.Cos(a + 0.045f) * 90, 128 + Mathf.Sin(a + 0.045f) * 90, 1.6f, gold, 0.05f);
        Quarters(c, new[] { "XII", "III", "VI", "IX" }, 76, 20, 3f, Ink);
        Centered(c, "JDS", 128, 164, 22, 3.2f, gold);
        Centered(c, "JOEY DREW", 128, 98, 11, 1.8f, Ink);
        Centered(c, "STUDIOS", 128, 84, 9, 1.5f, Ink);
        Centered(c, "DREAMS COME TRUE", 128, 58, 7, 1.2f, gold);
        return Done(c, name, 0.03f);
    }

    /// <summary>A gauge off the ink machine: a heavy ring of numbers, the INK LEVEL arc getting blacker towards full.</summary>
    public static Texture2D GaugeDial(string name, int seed)
    {
        var cream = new Color(0.93f, 0.87f, 0.7f);
        var c = DialBase(cream, Ink, seed, 9f);
        for (var h = 1; h <= 12; h++)
        {
            var p = Clock(h, 82);
            Centered(c, h.ToString(), p.x, p.y, 16, 2.6f, Ink);
        }

        for (var t = 0f; t < 1f; t += 0.01f)
        {
            var hrs = Mathf.Lerp(7.5f, 16.5f, t) % 12f;
            var a = Clock(hrs, 58);
            var b = Clock(hrs, 66);
            c.Stroke(a.x, a.y, b.x, b.y, 2.6f, Ink, 0.05f, Mathf.Lerp(0.25f, 1f, t));
        }

        Centered(c, "INK", 128, 160, 16, 2.6f, Ink);
        Centered(c, "LEVEL", 128, 142, 9, 1.5f, Ink);
        Centered(c, "JDS", 128, 70, 9, 1.5f, Ink);
        for (var i = 0; i < 4; i++) c.InkBlot(c.Rand(40, 216), c.Rand(40, 216), c.Rand(2, 4), Ink);
        return Done(c, name, 0.05f);
    }

    /// <summary>Alice's watch: white, a halo drawn over the middle, little wings at the sides, hearts for 12, 3, 6 and 9.</summary>
    public static Texture2D AngelDial(string name, int seed)
    {
        var c = DialBase(White, Ink, seed, 4f, false);
        var gold = new Color(0.82f, 0.64f, 0.28f);
        for (var h = 0; h < 12; h++)
        {
            var p = Clock(h, 100);
            if (h % 3 == 0)
            {
                Ellipse(c, p.x - 5, p.y + 3, 6, 6, Ink);
                Ellipse(c, p.x + 5, p.y + 3, 6, 6, Ink);
                Poly(c, new[] { new Vector2(p.x - 11, p.y + 2), new Vector2(p.x + 11, p.y + 2), new Vector2(p.x, p.y - 12) }, Ink);
            }
            else c.Dot(p.x, p.y, 3.4f, Ink);
        }

        for (var a = 0f; a < Mathf.PI * 2; a += 0.05f)
            c.Stroke(128 + Mathf.Cos(a) * 26, 172 + Mathf.Sin(a) * 8, 128 + Mathf.Cos(a + 0.06f) * 26, 172 + Mathf.Sin(a + 0.06f) * 8, 3f, gold, 0.05f);
        foreach (var side in new[] { -1f, 1f })
            for (var f = 0; f < 4; f++)
                Ellipse(c, 128 + side * (34 + f * 9), 128 + f * 5, 14 - f * 2, 5, Ink, 0.85f, side * (0.5f + f * 0.12f));
        Centered(c, "ANGEL", 128, 84, 13, 2f, Ink);
        return Done(c, name, 0.03f);
    }

    /// <summary>The Ink Demon's watch: a black face, scratched white numbers, his grin, ink running down over it all.</summary>
    public static Texture2D DemonDial(string name, int seed)
    {
        var black = new Color(0.06f, 0.05f, 0.045f);
        var bone = new Color(0.88f, 0.84f, 0.74f);
        var c = DialBase(black, bone, seed, 3f);
        Quarters(c, new[] { "12", "3", "6", "9" }, 80, 22, 3f, bone);
        var grin = new System.Collections.Generic.List<Vector2>();
        for (var i = 0; i <= 16; i++)
        {
            var t = i / 16f * 2 - 1;
            grin.Add(new Vector2(128 + t * 50, 112 - 26 * (1 - t * t)));
        }

        for (var i = 16; i >= 0; i--)
        {
            var t = i / 16f * 2 - 1;
            grin.Add(new Vector2(128 + t * 46, 118 - 6 * (1 - t * t)));
        }

        Poly(c, grin.ToArray(), new Color(0.01f, 0.01f, 0.01f));
        for (var i = -3; i <= 3; i++)
        {
            var x = 128 + i * 12f;
            Poly(c, new[] { new Vector2(x - 5, 116), new Vector2(x + 5, 116), new Vector2(x, 104) }, bone);
        }

        Outline(c, grin.ToArray(), 1.6f, 0.9f);
        RunsDown(c, 12, 110, 6f, new Color(0.01f, 0.008f, 0.006f));
        for (var i = 0; i < 5; i++)
        {
            var x = c.Rand(60, 196);
            var y = c.Rand(60, 196);
            for (var j = 0; j < 3; j++)
            {
                var nx = x + c.Rand(-18, 18);
                var ny = y + c.Rand(-18, 18);
                c.Stroke(x, y, nx, ny, 0.8f, bone, 0.5f, 0.5f);
                x = nx;
                y = ny;
            }
        }

        return Done(c, name, 0.04f);
    }

    // ---------------- every style's material, by key ----------------

    private static Cloth C(Color col, int seed, float shade = 0.3f, float rim = 0f) => new() { Base = col, Shade = shade, Rim = rim, Seed = seed };

    /// <summary>The texture for one of the styles' material keys (null: not a style's).</summary>
    public static Texture2D Paint(string key)
    {
        var n = "InkVR_Style_" + key;
        var black = new Color(0.075f, 0.065f, 0.06f);
        var inkTint = new Color(0.09f, 0.08f, 0.075f);
        var borisGrey = new Color(0.78f, 0.77f, 0.74f);
        var bruteGrey = new Color(0.25f, 0.23f, 0.22f);
        var overalls = new Color(0.36f, 0.45f, 0.56f);
        var denim = new Color(0.27f, 0.32f, 0.4f);
        var suit = new Color(0.22f, 0.2f, 0.22f);
        switch (key)
        {
            // Bendy
            case "bendyArm": return CartoonSkin(n, black, 0.35f, 0.08f, 101);
            case "bendyBody": return CartoonSkin(n, black, 0.35f, 0.1f, 102);
            case "bendyLegs": return CartoonSkin(n, black, 0.35f, 0.08f, 103);
            case "bendyBow": return Bow(n, White, 104);
            case "bendyShoe": return Shoe(n, black, 0.8f, 105);
            case "bendyHead": return BendyHead(n, false, 106);
            case "watchBlack": return CartoonSkin(n, black, 0.3f, 0.12f, 107);
            // Boris
            case "borisArm": return Fur(n, borisGrey, 111);
            case "borisFur": return Fur(n, borisGrey, 112);
            case "borisHead": return BorisHead(n, false, 113);
            case "borisMuzzle": return CartoonSkin(n, new Color(0.9f, 0.89f, 0.86f), 0.25f, 0f, 114);
            case "borisOveralls":
            {
                var o = C(overalls, 115);
                o.Stitches = true;
                o.Pocket = true;
                o.Patches = true;
                return Fabric(n, o);
            }
            case "borisOverallLegs":
            {
                var o = C(overalls, 116);
                o.Stitches = true;
                o.Patches = true;
                return Fabric(n, o);
            }
            case "borisShoe": return Shoe(n, new Color(0.45f, 0.3f, 0.18f), 0.4f, 117);
            // Alice Angel
            case "aliceArm": return CartoonSkin(n, White, 0.22f, 0f, 121);
            case "aliceArmBody": return CartoonSkin(n, White, 0.22f, 0f, 122);
            case "aliceDress": return Fabric(n, C(black, 123, 0.35f, 0.08f));
            case "aliceSkirt": return Fabric(n, C(black, 124, 0.35f, 0.08f));
            case "aliceBow": return Bow(n, White, 125);
            case "aliceLegs": return CartoonSkin(n, black, 0.3f, 0.06f, 126);
            case "aliceShoe": return Shoe(n, black, 0.9f, 127);
            case "aliceHead": return AliceHead(n, 128);
            case "aliceHair": return Hair(n, black, 129);
            case "halo": return Glow(n, new Color(1f, 0.86f, 0.5f));
            // the Ink Demon
            case "demonSkin": return InkSkin(n, inkTint, 1f, false, 131);
            case "demonPalm": return InkSkin(n, inkTint, 0.8f, false, 132);
            case "demonNail": return InkSkin(n, black * 0.6f, 1.2f, false, 133, 64, 64);
            case "demonForearm": return InkSkin(n, inkTint, 1f, false, 134);
            case "demonBody": return InkSkin(n, inkTint, 1f, false, 135);
            case "demonLegs": return InkSkin(n, inkTint, 0.9f, false, 136);
            case "demonFeet": return InkSkin(n, black * 0.7f, 1.2f, false, 137);
            case "demonBow": return Bow(n, new Color(0.82f, 0.78f, 0.7f), 138);
            case "demonHead": return BendyHead(n, true, 139);
            case "watchInk": return InkSkin(n, black, 1.2f, false, 140, 64, 64);
            // Sammy Lawrence
            case "sammySkin": return InkSkin(n, new Color(0.16f, 0.14f, 0.13f), 0.6f, false, 141);
            case "sammyPalm": return InkSkin(n, new Color(0.18f, 0.16f, 0.15f), 0.5f, false, 142);
            case "sammyNail": return InkSkin(n, new Color(0.12f, 0.1f, 0.1f), 0.8f, false, 143, 64, 64);
            case "sammyForearm": return InkSkin(n, new Color(0.16f, 0.14f, 0.13f), 0.6f, false, 144);
            case "sammySkinBody": return InkSkin(n, new Color(0.16f, 0.14f, 0.13f), 0.6f, false, 145);
            case "sammyOveralls":
            {
                var o = C(new Color(0.52f, 0.46f, 0.36f), 146);
                o.Stitches = true;
                o.Ink = 0.9f;
                return Fabric(n, o);
            }
            case "sammyOverallLegs":
            {
                var o = C(new Color(0.52f, 0.46f, 0.36f), 147);
                o.Stitches = true;
                o.Ink = 0.9f;
                return Fabric(n, o);
            }
            case "sammyBoot": return Shoe(n, new Color(0.3f, 0.22f, 0.15f), 0.2f, 148);
            case "sammyMask": return SammyMask(n, 149);
            case "rope": return Rope(n, 150);
            // the Projectionist
            case "normanSkin": return InkSkin(n, new Color(0.13f, 0.12f, 0.11f), 0.7f, false, 151);
            case "normanPalm": return InkSkin(n, new Color(0.15f, 0.13f, 0.12f), 0.6f, false, 152);
            case "normanNail": return InkSkin(n, new Color(0.1f, 0.09f, 0.08f), 0.8f, false, 153, 64, 64);
            case "normanForearm": return InkSkin(n, new Color(0.13f, 0.12f, 0.11f), 0.7f, false, 154);
            case "normanSkinBody": return InkSkin(n, new Color(0.13f, 0.12f, 0.11f), 0.7f, false, 155);
            case "normanSleeve":
            case "normanSleeveRolled":
            {
                var o = C(new Color(0.55f, 0.5f, 0.42f), key == "normanSleeve" ? 156 : 157);
                o.Torn = true;
                o.Ink = 0.7f;
                return Fabric(n, o, 512, 256);
            }
            case "normanVest":
            {
                var o = C(new Color(0.3f, 0.26f, 0.22f), 158);
                o.Torn = true;
                o.Placket = true;
                o.Buttons = true;
                o.Ink = 0.6f;
                return Fabric(n, o);
            }
            case "normanTrousers":
            {
                var o = C(new Color(0.2f, 0.19f, 0.18f), 159);
                o.Ink = 0.5f;
                return Fabric(n, o);
            }
            case "normanBoot": return Shoe(n, new Color(0.18f, 0.14f, 0.11f), 0.2f, 160);
            case "projector": return Projector(n, 161);
            case "lens": return Glow(n, new Color(1f, 0.96f, 0.85f));
            // Brute Boris
            case "bruteSkin": return InkSkin(n, bruteGrey, 0.5f, false, 171);
            case "brutePalm": return InkSkin(n, bruteGrey * 0.85f, 0.4f, false, 172);
            case "bruteNail": return InkSkin(n, black, 0.8f, false, 173, 64, 64);
            case "bruteForearm": return InkSkin(n, bruteGrey, 0.5f, false, 174);
            case "bruteBody": return InkSkin(n, bruteGrey, 0.5f, false, 175);
            case "bruteHead": return BorisHead(n, true, 176);
            case "bruteOverallLegs":
            {
                var o = C(denim * 0.8f, 177);
                o.Torn = true;
                o.Patches = true;
                o.Ink = 1f;
                return Fabric(n, o);
            }
            case "bruteFeet": return InkSkin(n, bruteGrey * 0.8f, 0.5f, false, 178);
            // a Searcher
            case "gooSkin": return InkSkin(n, inkTint, 1.2f, true, 181);
            case "gooPalm": return InkSkin(n, inkTint, 1.1f, true, 182);
            case "gooNail": return InkSkin(n, inkTint, 1.2f, true, 183, 64, 64);
            case "gooForearm": return InkSkin(n, inkTint, 1.2f, true, 184);
            case "gooBody": return InkSkin(n, inkTint, 1.2f, true, 185);
            case "gooLegs": return InkSkin(n, inkTint, 1.1f, true, 186);
            case "gooFeet": return InkSkin(n, black * 0.7f, 1.2f, true, 187);
            case "searcherHead": return SearcherHead(n, 188);
            // Joey Drew
            case "joeySleeve":
            {
                var o = C(suit, 191);
                o.Pinstripe = 18;
                o.StripeColor = new Color(0.55f, 0.52f, 0.5f);
                return Fabric(n, o, 512, 256);
            }
            case "joeySuit":
            {
                var o = C(suit, 192);
                o.Pinstripe = 14;
                o.StripeColor = new Color(0.55f, 0.52f, 0.5f);
                o.Placket = true;
                o.Buttons = true;
                o.Pocket = true;
                return Fabric(n, o);
            }
            case "joeySuitLegs":
            {
                var o = C(suit, 193);
                o.Pinstripe = 14;
                o.StripeColor = new Color(0.55f, 0.52f, 0.5f);
                return Fabric(n, o);
            }
            case "joeyLapel": return Fabric(n, C(suit * 0.8f, 194), 64, 64);
            case "joeyShirt":
            case "shirtCuff":
            case "joeyShirtBits":
            {
                var o = C(new Color(0.93f, 0.91f, 0.86f), key.Length + 195, 0.2f);
                o.Placket = key == "joeyShirt";
                o.Buttons = key == "joeyShirt";
                return Fabric(n, o, 128, 128);
            }
            case "joeyTie": return Bow(n, new Color(0.3f, 0.24f, 0.32f), 196);
            case "joeyShoe": return Shoe(n, new Color(0.2f, 0.12f, 0.08f), 0.9f, 197);
            case "joeyFace": return JoeyHead(n, false, 198);
            case "joeyHair": return Hair(n, new Color(0.62f, 0.62f, 0.6f), 199);
            // Wally Franks
            case "workGlove": return WorkGlove(n, false, 201);
            case "workGlovePalm": return WorkGlove(n, true, 202);
            case "wallySleeve":
            case "wallySleeveRolled":
            {
                var o = C(new Color(0.6f, 0.64f, 0.66f), key == "wallySleeve" ? 203 : 204);
                o.Stitches = true;
                return Fabric(n, o, 512, 256);
            }
            case "wallyShirt":
            {
                var o = C(new Color(0.6f, 0.64f, 0.66f), 205);
                o.Placket = true;
                o.Buttons = true;
                o.Pocket = true;
                return Fabric(n, o);
            }
            case "wallyOveralls":
            {
                var o = C(denim, 206);
                o.Stitches = true;
                o.Pocket = true;
                o.Ink = 0.3f;
                return Fabric(n, o);
            }
            case "wallyOverallLegs":
            {
                var o = C(denim, 207);
                o.Stitches = true;
                o.Patches = true;
                o.Ink = 0.3f;
                return Fabric(n, o);
            }
            case "wallyBoot": return Shoe(n, new Color(0.35f, 0.24f, 0.15f), 0.2f, 208);
            case "wallyKeys": return Brass(n, 209);
            case "wallyRag": return Fabric(n, C(new Color(0.75f, 0.7f, 0.58f), 210), 64, 64);
            case "wallyFace": return JoeyHead(n, true, 211);
            case "wallyCap": return Cap(n, 212);
            // Henry's own head (for mirrors and your shadow)
            case "henryFace": return JoeyHead(n, true, 221);
            case "toonHenryFace": return JoeyHead(n, true, 222);
            case "henryHair": return Hair(n, new Color(0.3f, 0.2f, 0.12f), 223);
            case "henrySkin": return CartoonSkin(n, new Color(0.88f, 0.7f, 0.52f), 0.2f, 0f, 224);
            // the watches
            case "strapBlack": return Strap(n, black, new Color(0.5f, 0.48f, 0.45f), 231);
            case "strapWhite": return Strap(n, White, new Color(0.7f, 0.6f, 0.4f), 232);
            case "strapInk": return InkSkin(n, black, 1f, false, 233, 256, 64);
            case "goldLinks": return Links(n, new Color(0.8f, 0.62f, 0.26f), 234);
            case "brass": return Brass(n, 235);
            case "vialGlass": return Glow(n, new Color(0.8f, 0.86f, 0.85f));
            case "bone": return Glow(n, new Color(0.9f, 0.87f, 0.78f));
            case "dialBendy": return BendyDial(n, 236);
            case "dialStudio": return StudioDial(n, 237);
            case "dialGauge": return GaugeDial(n, 238);
            case "dialAngel": return AngelDial(n, 239);
            case "dialDemon": return DemonDial(n, 240);
            default: return null;
        }
    }
}
