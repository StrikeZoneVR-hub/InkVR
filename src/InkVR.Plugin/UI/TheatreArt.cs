using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The theatre's textures, painted at startup in the game's hand-inked style. They are pale with ink
/// linework; the colour and the light come from each surface's vertex colours.
/// </summary>
public static class TheatreArt
{
    private static readonly Color Ink = new(0.05f, 0.035f, 0.025f, 1f);
    private static readonly Color PaperWhite = new(0.95f, 0.92f, 0.86f, 1f);

    private static void Frame(InkCanvas c, float inset, float width, float alpha, float wobble = 0.5f)
    {
        float x0 = inset, y0 = inset, x1 = c.W - 1 - inset, y1 = c.H - 1 - inset;
        c.Stroke(x0, y0, x1, y0, width, Ink, wobble, alpha);
        c.Stroke(x1, y0, x1, y1, width, Ink, wobble, alpha);
        c.Stroke(x1, y1, x0, y1, width, Ink, wobble, alpha);
        c.Stroke(x0, y1, x0, y0, width, Ink, wobble, alpha);
    }

    private static void Shade(InkCanvas c, float bottom, float top)
    {
        for (var y = 0; y < c.H; y++)
        {
            var k = Mathf.Lerp(bottom, top, y / (float)(c.H - 1));
            for (var x = 0; x < c.W; x++)
            {
                var p = c.Px[y * c.W + x];
                c.Px[y * c.W + x] = new Color(p.r * k, p.g * k, p.b * k, p.a);
            }
        }
    }

    private static void Arc(InkCanvas c, float cx, float cy, float r, float a0, float a1, float width, float alpha)
    {
        var steps = Mathf.Max(6, Mathf.CeilToInt(r * Mathf.Abs(a1 - a0) / 3f));
        for (var i = 0; i < steps; i++)
        {
            var t0 = Mathf.Lerp(a0, a1, (float)i / steps);
            var t1 = Mathf.Lerp(a0, a1, (float)(i + 1) / steps);
            c.Stroke(cx + Mathf.Cos(t0) * r, cy + Mathf.Sin(t0) * r, cx + Mathf.Cos(t1) * r, cy + Mathf.Sin(t1) * r, width, Ink, 0.15f, alpha);
        }
    }

    /// <summary>Generic trim: each face of a box gets an ink outline and a faint bevel line.</summary>
    public static Texture2D Box()
    {
        var c = new InkCanvas(128, 128, PaperWhite, 31);
        Shade(c, 0.85f, 1f);
        Frame(c, 2.5f, 3.4f, 1f);
        Frame(c, 11f, 1.1f, 0.3f);
        c.Hatch(4, 4, 120, 10, 60, 5, 0.8f, Ink, 0.12f);
        c.Grain(0.1f);
        return c.ToTexture("InkVR_Theatre_Box", false);
    }

    /// <summary>Tufted velvet: three stitched channels, outlined.</summary>
    public static Texture2D Fabric()
    {
        var c = new InkCanvas(128, 128, new Color(0.96f, 0.93f, 0.9f), 32);
        Shade(c, 0.8f, 1.02f);
        foreach (var x in new[] { 32f, 64f, 96f })
        {
            c.Stroke(x, 8, x, 120, 1.6f, Ink, 0.4f, 0.45f);
            c.Hatch(x - 9, 8, 7, 112, 80, 4, 0.7f, Ink, 0.12f);
        }

        for (var i = 0; i < 120; i++) c.Dot(c.Rand(6, 122), c.Rand(6, 122), 0.7f, Ink, 0.1f);
        Frame(c, 2.5f, 3.6f, 1f);
        c.Grain(0.08f);
        return c.ToTexture("InkVR_Theatre_Velvet", false);
    }

    /// <summary>Floorboards (tiles both ways).</summary>
    public static Texture2D Wood()
    {
        var c = new InkCanvas(256, 256, new Color(0.94f, 0.89f, 0.81f), 21);
        const int planks = 8;
        var pw = c.H / planks;
        for (var k = 0; k < planks; k++)
        {
            var tone = c.Rand(0.88f, 1.04f);
            for (var y = k * pw; y < (k + 1) * pw; y++)
            for (var x = 0; x < c.W; x++)
            {
                var p = c.Px[y * c.W + x];
                c.Px[y * c.W + x] = new Color(p.r * tone, p.g * tone, p.b * tone, 1);
            }

            for (var g = 0; g < 4; g++)
            {
                var gy = k * pw + c.Rand(5, pw - 5);
                var gx = c.Rand(0, c.W);
                c.Stroke(gx, gy, gx + c.Rand(60, 160), gy + c.Rand(-2, 2), 0.8f, Ink, 0.8f, 0.28f);
            }

            var joint = c.Rand(0, c.W);
            c.Stroke(joint, k * pw + 1, joint + c.Rand(-1, 1), (k + 1) * pw - 1, 1.6f, Ink, 0.3f, 0.75f);
            if (c.Rand() < 0.4f)
            {
                var kx = c.Rand(0, c.W);
                var ky = k * pw + pw * 0.5f;
                c.Stroke(kx - 5, ky, kx + 5, ky, 1f, Ink, 1.5f, 0.4f);
                c.Dot(kx, ky, 1.5f, Ink, 0.5f);
            }
        }

        for (var k = 0; k <= planks; k++) c.Stroke(0, k * pw, c.W, k * pw, 2.2f, Ink, 0.4f, 0.85f);
        c.Grain(0.08f);
        return c.ToTexture("InkVR_Theatre_Wood");
    }

    /// <summary>Art deco wallpaper: fans between pinstripes (tiles both ways).</summary>
    public static Texture2D Wallpaper()
    {
        var c = new InkCanvas(256, 256, new Color(0.96f, 0.91f, 0.81f), 41);
        const int cell = 64;
        for (var row = 0; row < 4; row++)
        for (var col = 0; col < 4; col++)
        {
            var cx = col * cell + (row % 2 == 0 ? cell * 0.5f : 0f);
            var cy = row * cell + 10f;
            Arc(c, cx, cy, 24, 0, Mathf.PI, 1.3f, 0.5f);
            Arc(c, cx, cy, 16, 0, Mathf.PI, 1.1f, 0.4f);
            for (var r = 0; r < 7; r++)
            {
                var a = Mathf.PI * (r + 0.5f) / 7f;
                c.Stroke(cx + Mathf.Cos(a) * 16, cy + Mathf.Sin(a) * 16, cx + Mathf.Cos(a) * 24, cy + Mathf.Sin(a) * 24, 1f, Ink, 0.1f, 0.4f);
            }

            c.Dot(cx, cy + 36, 2f, Ink, 0.4f);
        }

        for (var x = 0; x < c.W; x += cell)
        {
            c.Stroke(x, 0, x, c.H, 1.1f, Ink, 0.2f, 0.35f);
            c.Stroke(x + 4, 0, x + 4, c.H, 0.8f, Ink, 0.2f, 0.25f);
        }

        c.Grain(0.08f);
        return c.ToTexture("InkVR_Theatre_Wallpaper");
    }

    /// <summary>Aisle runner: bordered edges, a diamond lattice down the middle (tiles along v).</summary>
    public static Texture2D Carpet()
    {
        var c = new InkCanvas(128, 256, new Color(0.94f, 0.88f, 0.8f), 51);
        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
            if (x < 14 || x >= c.W - 14)
            {
                var p = c.Px[y * c.W + x];
                c.Px[y * c.W + x] = new Color(p.r * 0.62f, p.g * 0.62f, p.b * 0.62f, 1);
            }

        foreach (var x in new[] { 14f, 113f }) c.Stroke(x, 0, x, c.H, 1.8f, Ink, 0.2f, 0.8f);
        foreach (var x in new[] { 7f, 120f })
            for (var y = 4; y < c.H; y += 16)
                c.Dot(x, y, 2f, new Color(0.98f, 0.85f, 0.5f), 0.8f);
        for (var k = -8; k <= 12; k++)
        {
            var y0 = k * 32f;
            c.Stroke(18, y0, 110, y0 + 92, 1.1f, Ink, 0.2f, 0.45f);
            c.Stroke(110, y0, 18, y0 + 92, 1.1f, Ink, 0.2f, 0.45f);
        }

        for (var k = 0; k < 8; k++) c.Dot(64, k * 32 + 16 + 30, 2.4f, new Color(0.98f, 0.85f, 0.5f), 0.9f);
        c.Grain(0.1f);
        return c.ToTexture("InkVR_Theatre_Carpet");
    }

    /// <summary>A ceiling coffer: stepped frames and a rosette.</summary>
    public static Texture2D Coffer()
    {
        var c = new InkCanvas(256, 256, new Color(0.95f, 0.9f, 0.8f), 61);
        Frame(c, 5, 5f, 1f);
        Frame(c, 26, 2f, 0.8f);
        Frame(c, 44, 1.4f, 0.6f);
        c.Hatch(8, 8, 18, 240, 70, 5, 0.8f, Ink, 0.3f);
        c.Hatch(8, 230, 240, 18, 20, 5, 0.8f, Ink, 0.3f);
        Arc(c, 128, 128, 38, 0, Mathf.PI * 2, 2f, 0.85f);
        Arc(c, 128, 128, 24, 0, Mathf.PI * 2, 1.5f, 0.7f);
        for (var k = 0; k < 12; k++)
        {
            var a = k / 12f * Mathf.PI * 2;
            c.Stroke(128 + Mathf.Cos(a) * 24, 128 + Mathf.Sin(a) * 24, 128 + Mathf.Cos(a) * 38, 128 + Mathf.Sin(a) * 38, 1.4f, Ink, 0.1f, 0.6f);
        }

        c.Dot(128, 128, 7, Ink, 0.8f);
        c.Grain(0.08f);
        return c.ToTexture("InkVR_Theatre_Coffer", false);
    }

    /// <summary>A gilded deco frieze: banded edges, fans and chevrons (tiles along u).</summary>
    public static Texture2D Ornament()
    {
        var c = new InkCanvas(512, 64, new Color(0.97f, 0.92f, 0.8f), 71);
        Shade(c, 0.82f, 1f);
        foreach (var (y, w) in new[] { (3f, 3f), (10f, 1.4f), (53f, 1.4f), (60f, 3f) }) c.Stroke(0, y, c.W, y, w, Ink, 0.2f, 0.9f);
        for (var k = 0; k < 8; k++)
        {
            var cx = k * 64f + 32f;
            if (k % 2 == 0)
            {
                Arc(c, cx, 13, 24, 0, Mathf.PI, 1.6f, 0.8f);
                for (var r = 0; r < 5; r++)
                {
                    var a = Mathf.PI * (r + 0.5f) / 5f;
                    c.Stroke(cx, 13, cx + Mathf.Cos(a) * 24, 13 + Mathf.Sin(a) * 24, 1.1f, Ink, 0.1f, 0.6f);
                }
            }
            else
            {
                for (var v = 0; v < 3; v++)
                {
                    var y = 18 + v * 9f;
                    c.Stroke(cx - 22, y, cx, y + 12, 1.4f, Ink, 0.1f, 0.75f);
                    c.Stroke(cx, y + 12, cx + 22, y, 1.4f, Ink, 0.1f, 0.75f);
                }
            }
        }

        c.Grain(0.08f);
        return c.ToTexture("InkVR_Theatre_Frieze");
    }

    /// <summary>An organ grille: an arched panel of slats with a sunburst.</summary>
    public static Texture2D Grille()
    {
        var c = new InkCanvas(256, 512, new Color(0.95f, 0.88f, 0.72f), 81);
        const float cx = 128, top = 380, r = 104, x0 = 24, x1 = 232, y0 = 24;
        bool Inside(float x, float y) => x > x0 && x < x1 && y > y0 && (y < top || (x - cx) * (x - cx) + (y - top) * (y - top) < r * r);
        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
        {
            if (!Inside(x, y)) continue;
            var slot = (x - x0) % 14 < 5;
            var p = c.Px[y * c.W + x];
            var k = slot ? 0.12f : 0.78f;
            c.Px[y * c.W + x] = new Color(p.r * k, p.g * k, p.b * k, 1);
        }

        for (var k = 0; k < 13; k++)
        {
            var a = Mathf.Lerp(0.12f, Mathf.PI - 0.12f, k / 12f);
            var ex = cx + Mathf.Cos(a) * 400;
            var ey = y0 + Mathf.Sin(a) * 400;
            var steps = 200;
            for (var i = 0; i < steps; i++)
            {
                var t = (float)i / steps;
                var px = Mathf.Lerp(cx, ex, t);
                var py = Mathf.Lerp(y0, ey, t);
                if (Inside(px, py)) c.Dot(px, py, 2.6f, new Color(0.98f, 0.9f, 0.68f), 0.95f);
            }
        }

        c.Stroke(x0, y0, x1, y0, 5f, Ink, 0.3f);
        c.Stroke(x0, y0, x0, top, 5f, Ink, 0.3f);
        c.Stroke(x1, y0, x1, top, 5f, Ink, 0.3f);
        Arc(c, cx, top, r, 0, Mathf.PI, 5f, 1f);
        Frame(c, 3, 4f, 1f);
        c.Hatch(6, 400, 60, 100, 45, 6, 0.8f, Ink, 0.25f);
        c.Hatch(196, 400, 54, 100, 135, 6, 0.8f, Ink, 0.25f);
        c.Grain(0.08f);
        return c.ToTexture("InkVR_Theatre_Grille", false);
    }

    /// <summary>One leaf of a panelled door with a porthole.</summary>
    public static Texture2D Door()
    {
        var c = new InkCanvas(128, 256, new Color(0.93f, 0.87f, 0.78f), 91);
        Shade(c, 0.85f, 1f);
        Frame(c, 3, 4f, 1f);
        foreach (var (y0, y1) in new[] { (18f, 104f), (126f, 238f) })
        {
            c.Stroke(18, y0, 110, y0, 1.6f, Ink, 0.3f, 0.8f);
            c.Stroke(110, y0, 110, y1, 1.6f, Ink, 0.3f, 0.8f);
            c.Stroke(110, y1, 18, y1, 1.6f, Ink, 0.3f, 0.8f);
            c.Stroke(18, y1, 18, y0, 1.6f, Ink, 0.3f, 0.8f);
        }

        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
        {
            var d = Vector2.Distance(new Vector2(x, y), new Vector2(64, 184));
            if (d < 24) c.Px[y * c.W + x] = new Color(0.98f, 0.9f, 0.7f, 1);
        }

        Arc(c, 64, 184, 25, 0, Mathf.PI * 2, 4f, 1f);
        c.Stroke(96, 112, 96, 140, 3f, Ink, 0.2f, 0.9f);
        c.Grain(0.08f);
        return c.ToTexture("InkVR_Theatre_Door", false);
    }

    /// <summary>Velvet drapes: deep folds with hatching (tiles across).</summary>
    public static Texture2D Curtain()
    {
        var c = new InkCanvas(256, 256, new Color(0.95f, 0.9f, 0.86f), 5);
        for (var x = 0; x < c.W; x++)
        {
            var s = Mathf.Sin(x / (float)c.W * Mathf.PI * 2 * 4);
            var shade = 0.62f + 0.38f * (s * 0.5f + 0.5f);
            for (var y = 0; y < c.H; y++)
            {
                var p = c.Px[y * c.W + x];
                c.Px[y * c.W + x] = new Color(p.r * shade, p.g * shade, p.b * shade, 1);
            }
        }

        for (var i = 0; i < 4; i++)
        {
            var x = (i + 0.75f) * c.W / 4f;
            c.Stroke(x, 0, x + c.Rand(-3, 3), c.H, 2f, Ink, 1f, 0.75f);
            c.Hatch(x - 22, 0, 16, c.H, 80, 6, 0.9f, Ink, 0.3f);
        }

        c.Grain(0.1f);
        return c.ToTexture("InkVR_Theatre_Drape");
    }

    /// <summary>Poster paper: warm sepia, a spotlight glow, a double ink border.</summary>
    public static Texture2D PosterPaper()
    {
        var c = new InkCanvas(256, 384, new Color(0.98f, 0.9f, 0.7f), 101);
        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
        {
            var d = Vector2.Distance(new Vector2(x, y), new Vector2(128, 230)) / 200f;
            var k = Mathf.Lerp(1.05f, 0.72f, Mathf.Clamp01(d));
            var p = c.Px[y * c.W + x];
            c.Px[y * c.W + x] = new Color(Mathf.Min(1, p.r * k), Mathf.Min(1, p.g * k), Mathf.Min(1, p.b * k), 1);
        }

        Frame(c, 6, 4f, 1f);
        Frame(c, 14, 1.5f, 0.7f);
        for (var k = 0; k < 18; k++)
        {
            var a = k / 18f * Mathf.PI * 2;
            c.Stroke(128 + Mathf.Cos(a) * 70, 230 + Mathf.Sin(a) * 70, 128 + Mathf.Cos(a) * 125, 230 + Mathf.Sin(a) * 125, 1f, Ink, 0.2f, 0.12f);
        }

        c.Grain(0.12f);
        return c.ToTexture("InkVR_Theatre_Poster", false);
    }

    /// <summary>A painted ink splat with drips, for when the game's own isn't available.</summary>
    public static Texture2D Splat()
    {
        var c = new InkCanvas(256, 256, new Color(0, 0, 0, 0), 111);
        c.InkBlot(128, 170, 46, Color.white);
        for (var i = 0; i < 6; i++)
        {
            var x = c.Rand(80, 176);
            var len = c.Rand(60, 160);
            c.Stroke(x, 160, x + c.Rand(-3, 3), 160 - len, c.Rand(5, 10), Color.white, 0.3f);
            c.Dot(x, 160 - len, 6, Color.white);
        }

        return c.ToTexture("InkVR_Theatre_Splat", false);
    }

    /// <summary>A gilded sunburst disc, for the medallion when Bendy's face isn't available.</summary>
    public static Texture2D Sunburst()
    {
        var c = new InkCanvas(256, 256, new Color(0, 0, 0, 0), 121);
        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
        {
            var d = Vector2.Distance(new Vector2(x, y), new Vector2(127.5f, 127.5f));
            if (d < 120) c.Px[y * c.W + x] = new Color(0.98f, 0.86f, 0.55f, Mathf.Clamp01(120 - d));
        }

        for (var k = 0; k < 24; k++)
        {
            var a = k / 24f * Mathf.PI * 2;
            c.Stroke(128 + Mathf.Cos(a) * 30, 128 + Mathf.Sin(a) * 30, 128 + Mathf.Cos(a) * 112, 128 + Mathf.Sin(a) * 112, 2f, Ink, 0.2f, 0.7f);
        }

        Arc(c, 128, 128, 118, 0, Mathf.PI * 2, 4f, 1f);
        c.Dot(128, 128, 28, Ink, 0.9f);
        return c.ToTexture("InkVR_Theatre_Sunburst", false);
    }
}
