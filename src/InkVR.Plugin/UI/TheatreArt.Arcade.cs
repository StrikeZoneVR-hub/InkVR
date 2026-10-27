using UnityEngine;

namespace InkVR.UI;

/// <summary>The Fun Hall's painted pieces: the toss board, the gallery's backdrop, the dartboard, the ball targets...</summary>
public static partial class TheatreArt
{
    private static readonly Color Cream = new(0.97f, 0.92f, 0.8f, 1f);
    private static readonly Color Black = new(0.07f, 0.06f, 0.055f, 1f);

    private static void Disc(InkCanvas c, float cx, float cy, float r, Color col)
    {
        for (var y = (int)(cy - r - 1); y <= (int)(cy + r + 1); y++)
        for (var x = (int)(cx - r - 1); x <= (int)(cx + r + 1); x++)
        {
            if (x < 0 || y < 0 || x >= c.W || y >= c.H) continue;
            var d = System.MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            var a = r - d + 0.5f;
            if (a > 0) c.Blend(x, y, col, a > 1 ? 1 : a);
        }
    }

    private static void Ring(InkCanvas c, float cx, float cy, float r, float w, float alpha = 1f) => Arc(c, cx, cy, r, 0, Mathf.PI * 2, w, alpha);

    /// <summary>Ink Toss: Bendy's face, his grin the hole to throw into (the grin's painted black; the hole is cut in the board).</summary>
    public static Texture2D TossBoard()
    {
        var c = new InkCanvas(512, 512, new Color(0.85f, 0.25f, 0.18f), 71);
        // a sunburst behind him
        for (var k = 0; k < 24; k++)
        {
            if (k % 2 == 0) continue;
            var a0 = k / 24f * Mathf.PI * 2;
            var a1 = (k + 1) / 24f * Mathf.PI * 2;
            for (var r = 0f; r < 360f; r += 2f)
                c.Stroke(256 + System.MathF.Cos(a0) * r, 256 + System.MathF.Sin(a0) * r, 256 + System.MathF.Cos(a1) * r, 256 + System.MathF.Sin(a1) * r, 2.2f, Cream, 0f, 0.9f);
        }

        Disc(c, 256, 250, 190, Black);
        // horns
        for (var s = -1; s <= 1; s += 2)
            for (var t = 0f; t < 1f; t += 0.01f)
            {
                var x = 256 + s * (110 + 60 * t + 20 * t * t);
                var y = 380 + 110 * t;
                Disc(c, x, y, 50 * (1 - t) + 3, Black);
            }

        // the face: cream from the cheeks down, a widow's peak, pie-cut eyes
        for (var y = 80; y < 330; y++)
        for (var x = 90; x < 422; x++)
        {
            var dx = (x - 256) / 160f;
            var dy = (y - 220) / 150f;
            var inFace = dx * dx + dy * dy < 1f && y < 300 - System.MathF.Abs(x - 256) * 0.25f + (System.MathF.Abs(x - 256) < 40 ? -System.MathF.Abs(x - 256) * 0.5f - 20 : 0);
            if (inFace) c.Px[y * c.W + x] = Cream;
        }

        for (var s = -1; s <= 1; s += 2)
        {
            Disc(c, 256 + s * 62, 250, 34, Black);
            for (var y = 250; y < 300; y++)
            for (var x = 256 + s * 62 - 40; x < 256 + s * 62 + 40; x++)
            {
                var px = (x - (256 + s * 62)) * -s;
                if (px > 0 && y - 250 > -px * 0.3f && x >= 0 && x < c.W) c.Px[y * c.W + x] = Cream;
            }
        }

        // the grin (this part is the hole: the board's mesh is cut away here)
        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
        {
            var dx = (x - 256) / 150f;
            var dy = (y - 140) / 62f;
            if (dx * dx + dy * dy < 1f && y < 140 + 30 * (1 - dx * dx)) c.Px[y * c.W + x] = new Color(0.02f, 0.015f, 0.01f, 1);
        }

        TheatreArt.InkText(c, "INK TOSS", 150, 470, 36, 4f, Black);
        Ring(c, 256, 250, 190, 6f);
        return Done(c, "TossBoard", 0.05f, false);
    }

    /// <summary>The shooting gallery's backdrop: a cartoon sky, rolling hills, a fence.</summary>
    public static Texture2D Gallery()
    {
        var c = new InkCanvas(512, 256, new Color(0.62f, 0.78f, 0.86f), 72);
        for (var x = 0; x < c.W; x++)
        {
            var h1 = 90 + System.MathF.Sin(x * 0.02f) * 22 + System.MathF.Sin(x * 0.051f) * 9;
            var h2 = 60 + System.MathF.Sin(x * 0.033f + 2f) * 18;
            for (var y = 0; y < c.H; y++)
            {
                if (y < h2) c.Px[y * c.W + x] = new Color(0.42f, 0.62f, 0.3f, 1);
                else if (y < h1) c.Px[y * c.W + x] = new Color(0.55f, 0.72f, 0.38f, 1);
            }
        }

        for (var k = 0; k < 5; k++)
        {
            var cx = c.Rand(30, 480);
            var cy = c.Rand(170, 230);
            for (var b = 0; b < 4; b++) Disc(c, cx + b * 18 - 27, cy + (b % 2) * 8, 16, Cream);
        }

        for (var x = 10; x < c.W; x += 30) c.Stroke(x, 0, x, 40, 4f, Black, 0.3f, 0.9f);
        c.Stroke(0, 30, c.W, 30, 3f, Black, 0.3f, 0.9f);
        return Done(c, "Gallery", 0.06f, false);
    }

    /// <summary>A gallery target: the Butcher Gang's faces and Bendy's, round, on a stick (one sheet, four faces across).</summary>
    public static Texture2D Targets()
    {
        var c = new InkCanvas(512, 128, Cream, 73);
        for (var k = 0; k < 4; k++)
        {
            var cx = 64 + k * 128;
            Disc(c, cx, 64, 56, k == 3 ? Black : new Color(0.25f, 0.22f, 0.2f));
            Disc(c, cx, 64, 44, Cream);
            Ring(c, cx, 64, 56, 4f);
            switch (k)
            {
                case 0: // Piper: one big eye, a grin
                    Disc(c, cx + 10, 72, 12, Black);
                    c.Stroke(cx - 25, 42, cx + 25, 40, 4f, Black, 0.3f);
                    break;
                case 1: // Fisher: a lamp on his head, a wide mouth
                    Disc(c, cx, 98, 10, new Color(0.95f, 0.8f, 0.3f));
                    Disc(c, cx - 14, 70, 8, Black);
                    Disc(c, cx + 14, 70, 8, Black);
                    c.Stroke(cx - 20, 45, cx + 20, 45, 6f, Black, 0.2f);
                    break;
                case 2: // Striker: a stitched grin
                    Disc(c, cx - 14, 72, 7, Black);
                    Disc(c, cx + 14, 72, 7, Black);
                    c.Stroke(cx - 24, 46, cx + 24, 46, 3f, Black, 0.2f);
                    for (var i = -2; i <= 2; i++) c.Stroke(cx + i * 9, 40, cx + i * 9, 52, 2f, Black, 0.1f);
                    break;
                default: // Bendy (don't shoot him: it costs you)
                    Disc(c, cx, 58, 40, Cream);
                    Disc(c, cx - 15, 70, 8, Black);
                    Disc(c, cx + 15, 70, 8, Black);
                    c.Stroke(cx - 22, 44, cx + 22, 44, 5f, Black, 0.2f);
                    break;
            }
        }

        return Done(c, "Targets", 0.05f, false);
    }

    /// <summary>A dartboard: twenty segments in cream and black, the doubles and trebles rings, the bull.</summary>
    public static Texture2D Dartboard()
    {
        var c = new InkCanvas(512, 512, Black, 74);
        const float cx = 256, cy = 256;
        var red = new Color(0.62f, 0.1f, 0.08f);
        var green = new Color(0.12f, 0.35f, 0.18f);
        for (var y = 0; y < 512; y++)
        for (var x = 0; x < 512; x++)
        {
            float dx = x - cx, dy = y - cy;
            var r = System.MathF.Sqrt(dx * dx + dy * dy) / 230f; // 1 = the doubles' outer edge
            if (r > 1.12f) continue;
            var a = (System.MathF.Atan2(dx, dy) / (System.MathF.PI * 2) + 1f + 0.025f) % 1f;
            var seg = (int)(a * 20);
            var dark = seg % 2 == 0;
            Color col;
            if (r < 0.037f) col = red;
            else if (r < 0.094f) col = green;
            else if (r > 1f) col = Black;
            else if (r > 0.953f || (r > 0.582f && r < 0.629f)) col = dark ? red : green;
            else col = dark ? Black : Cream;
            c.Px[y * 512 + x] = col;
        }

        Ring(c, cx, cy, 230, 2f);
        Ring(c, cx, cy, 145, 1.5f);
        var nums = new[] { 20, 1, 18, 4, 13, 6, 10, 15, 2, 17, 3, 19, 7, 16, 8, 11, 14, 9, 12, 5 };
        for (var i = 0; i < 20; i++)
        {
            var a = i / 20f * Mathf.PI * 2;
            var t = nums[i].ToString();
            var w = InkTextWidth(t, 22);
            TheatreArt.InkText(c, t, cx + System.MathF.Sin(a) * 245 - w * 0.5f, cy + System.MathF.Cos(a) * 245 - 11, 22, 3f, Cream);
        }

        return Done(c, "Dartboard", 0.04f, false);
    }

    /// <summary>Ink Ball's target face: rings worth 10 to 50, two 100 pockets in the top corners.</summary>
    public static Texture2D SkeeTarget()
    {
        var c = new InkCanvas(256, 512, new Color(0.15f, 0.12f, 0.1f), 75);
        var rings = new[] { (220f, 10), (165f, 20), (115f, 30), (70f, 40), (34f, 50) };
        foreach (var (r, _) in rings) Disc(c, 128, 200, r * 0.55f, new Color(0.25f, 0.2f, 0.17f));
        foreach (var (r, pts) in rings)
        {
            Ring(c, 128, 200, r * 0.55f, 3f);
            TheatreArt.InkText(c, pts.ToString(), 116, 200 + r * 0.55f - 22, 16, 2.4f, Cream);
        }

        Disc(c, 40, 470, 22, new Color(0.02f, 0.015f, 0.01f));
        Disc(c, 216, 470, 22, new Color(0.02f, 0.015f, 0.01f));
        TheatreArt.InkText(c, "100", 18, 430, 16, 2.4f, Cream);
        TheatreArt.InkText(c, "100", 194, 430, 16, 2.4f, Cream);
        return Done(c, "Skee", 0.05f, false);
    }

    /// <summary>The pachinko board: the ink machine drawn behind the pins, the slots at the bottom with their points.</summary>
    public static Texture2D Pachinko()
    {
        var c = new InkCanvas(256, 320, new Color(0.92f, 0.86f, 0.7f), 76);
        // the machine, faint
        for (var y = 80; y < 260; y++)
        for (var x = 70; x < 186; x++)
            if ((x - 128) * (x - 128) / 3364f + (y < 230 ? 0 : (y - 230) * (y - 230) / 900f) < 1f) c.Px[y * c.W + x] = new Color(0.8f, 0.74f, 0.6f, 1);
        Ring(c, 128, 150, 40, 3f, 0.5f);
        for (var y = 90; y < 250; y += 30) c.Stroke(70, y, 186, y, 2f, Ink, 0.3f, 0.35f);
        TheatreArt.InkText(c, "THE INK MACHINE", 40, 292, 16, 2.2f, Ink);
        // slots
        var slots = new[] { 10, 50, 20, 100, 20, 50, 10 };
        for (var i = 0; i < slots.Length; i++)
        {
            var x0 = i * 256f / slots.Length;
            c.Stroke(x0, 0, x0, 40, 2.4f, Ink, 0.2f);
            var t = slots[i].ToString();
            TheatreArt.InkText(c, t, x0 + 18 - t.Length * 4, 12, 13, 2f, Ink);
        }

        return Done(c, "Pachinko", 0.05f, false);
    }

    /// <summary>A strip of tickets: ADMIT ONE, perforated.</summary>
    public static Texture2D Ticket()
    {
        var c = new InkCanvas(128, 64, new Color(0.95f, 0.6f, 0.5f), 77);
        Frame(c, 3f, 2f, 0.8f);
        TheatreArt.InkText(c, "ADMIT ONE", 16, 24, 14, 2f, Ink);
        for (var y = 4; y < 60; y += 6) c.Dot(124, y, 1.4f, Ink, 0.6f);
        return Done(c, "Ticket", 0.05f, false);
    }

    /// <summary>The strong man's scale up the tower: WEAKLING to INK DEMON.</summary>
    public static Texture2D StrongScale()
    {
        var c = new InkCanvas(128, 1024, Cream, 78);
        var marks = new[] { "WEAKLING", "SAPLING", "SEARCHER", "BUTCHER", "BRUTE", "INK DEMON" };
        for (var i = 0; i < marks.Length; i++)
        {
            var y = 60 + i * 170;
            c.Stroke(0, y, 128, y, 3f, Ink, 0.2f);
            var w = InkTextWidth(marks[i], 16);
            TheatreArt.InkText(c, marks[i], 64 - w * 0.5f, y + 20, 16, 2.4f, Ink);
        }

        for (var y = 0; y < 1024; y += 34) c.Stroke(0, y, 24, y, 1.6f, Ink, 0.1f, 0.6f);
        return Done(c, "StrongScale", 0.05f, false);
    }
}
