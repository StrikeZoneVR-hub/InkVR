using UnityEngine;

namespace InkVR.UI;

/// <summary>Textures for the rooms added in 1.0, pale with ink linework like the rest (the colour comes from the light).</summary>
public static partial class TheatreArt
{
    private static Texture2D Done(InkCanvas c, string name, float grain = 0.08f, bool wrap = true)
    {
        c.Grain(grain);
        return c.ToTexture("InkVR_Theatre_" + name, wrap);
    }

    /// <summary>Old brick, with ink runs down it here and there (tiles both ways: 4 courses, 2 bricks a course).</summary>
    public static Texture2D Brick()
    {
        var c = new InkCanvas(256, 256, new Color(0.93f, 0.86f, 0.8f), 61);
        const int rows = 6;
        var rh = c.H / rows;
        for (var r = 0; r < rows; r++)
        {
            var off = r % 2 == 0 ? 0 : 64;
            for (var b = -1; b < 3; b++)
            {
                var x0 = b * 128 + off;
                var tone = c.Rand(0.82f, 1.02f);
                for (var y = r * rh + 2; y < (r + 1) * rh - 2; y++)
                for (var x = x0 + 2; x < x0 + 126; x++)
                {
                    if (x < 0 || x >= c.W) continue;
                    var p = c.Px[y * c.W + x];
                    c.Mul(y * c.W + x, tone, tone, tone);
                }

                c.Stroke(x0, r * rh, x0, (r + 1) * rh, 2f, Ink, 0.3f, 0.8f);
                for (var k = 0; k < 3; k++) c.Dot(x0 + c.Rand(10, 118), r * rh + c.Rand(6, rh - 6), c.Rand(0.8f, 1.8f), Ink, 0.3f);
            }

            c.Stroke(0, r * rh, c.W, r * rh, 2.6f, Ink, 0.4f, 0.85f);
        }

        for (var i = 0; i < 3; i++)
        {
            var x = c.Rand(0, c.W);
            c.Stroke(x, c.H, x + c.Rand(-3, 3), c.H - c.Rand(60, 200), c.Rand(2, 4), Ink, 0.3f, 0.6f);
        }

        return Done(c, "Brick");
    }

    /// <summary>Riveted iron plate (the ink machine, the projectors, pipes' flanges).</summary>
    public static Texture2D Plate()
    {
        var c = new InkCanvas(128, 128, new Color(0.9f, 0.88f, 0.84f), 62);
        Shade(c, 0.86f, 1.02f);
        Frame(c, 2.5f, 3f, 1f);
        for (var i = 0; i < 4; i++)
        {
            var t = 10f + i * 36f;
            foreach (var p in new[] { new Vector2(t, 9), new Vector2(t, 119), new Vector2(9, t), new Vector2(119, t) })
            {
                c.Dot(p.x, p.y, 2.6f, Ink, 0.75f);
                c.Dot(p.x - 0.8f, p.y + 0.8f, 0.9f, PaperWhite, 0.8f);
            }
        }

        for (var i = 0; i < 14; i++)
        {
            var y = c.Rand(14, 114);
            c.Stroke(14, y, 114, y + c.Rand(-2, 2), 0.6f, Ink, 0.2f, 0.12f);
        }

        return Done(c, "Plate", 0.1f, false);
    }

    /// <summary>Black and cream tiles in a checkerboard (tiles both ways, 4 by 4).</summary>
    public static Texture2D Checker()
    {
        var c = new InkCanvas(256, 256, new Color(0.95f, 0.91f, 0.84f), 63);
        const int n = 4;
        var s = c.W / n;
        for (var j = 0; j < n; j++)
        for (var i = 0; i < n; i++)
        {
            if ((i + j) % 2 == 0) continue;
            for (var y = j * s; y < (j + 1) * s; y++)
            for (var x = i * s; x < (i + 1) * s; x++)
                c.Set(y * c.W + x, 0.16f, 0.13f, 0.11f);
        }

        for (var k = 0; k <= n; k++)
        {
            c.Stroke(k * s, 0, k * s, c.H, 1.6f, Ink, 0.2f, 0.7f);
            c.Stroke(0, k * s, c.W, k * s, 1.6f, Ink, 0.2f, 0.7f);
        }

        for (var i = 0; i < 40; i++) c.Stroke(c.Rand(0, c.W), c.Rand(0, c.H), c.Rand(0, c.W), c.Rand(0, c.H), 0.5f, Ink, 0.6f, 0.06f);
        return Done(c, "Checker");
    }

    /// <summary>Fairground stripes, cream and colour (the colour from the light), with a scalloped edge band at the top.</summary>
    public static Texture2D Tent()
    {
        var c = new InkCanvas(256, 256, new Color(0.96f, 0.93f, 0.88f), 64);
        for (var x = 0; x < c.W; x++)
        {
            if (x / 32 % 2 == 0) continue;
            for (var y = 0; y < c.H; y++)
            {
                var p = c.Px[y * c.W + x];
                c.Mul(y * c.W + x, 0.86f, 0.42f, 0.36f);
            }
        }

        for (var x = 0; x <= c.W; x += 32) c.Stroke(x, 0, x, c.H, 1.4f, Ink, 0.3f, 0.6f);
        return Done(c, "Tent");
    }

    /// <summary>A crate's side: planks, cross braces, stencilled ink.</summary>
    public static Texture2D Crate()
    {
        var c = new InkCanvas(128, 128, new Color(0.92f, 0.84f, 0.7f), 65);
        for (var y = 0; y < 128; y += 32) c.Stroke(4, y, 124, y, 1.6f, Ink, 0.4f, 0.7f);
        c.Stroke(8, 8, 120, 120, 5f, Ink, 0.3f, 0.35f);
        c.Stroke(8, 120, 120, 8, 5f, Ink, 0.3f, 0.35f);
        TheatreArt.InkText(c, "JDS", 44, 52, 20, 2.6f, Ink, 0.55f);
        Frame(c, 3f, 5f, 1f);
        return Done(c, "Crate", 0.1f, false);
    }

    /// <summary>A cellar's concrete floor: cracks, ink stains.</summary>
    public static Texture2D Concrete()
    {
        var c = new InkCanvas(256, 256, new Color(0.88f, 0.86f, 0.82f), 66);
        for (var i = 0; i < 300; i++) c.Dot(c.Rand(0, c.W), c.Rand(0, c.H), c.Rand(0.5f, 1.4f), Ink, 0.12f);
        for (var i = 0; i < 6; i++)
        {
            float x = c.Rand(0, c.W), y = c.Rand(0, c.H);
            for (var k = 0; k < 6; k++)
            {
                float nx = x + c.Rand(-30, 30), ny = y + c.Rand(-30, 30);
                c.Stroke(x, y, nx, ny, 0.9f, Ink, 0.8f, 0.45f);
                x = nx;
                y = ny;
            }
        }

        for (var i = 0; i < 4; i++) c.InkBlot(c.Rand(20, 236), c.Rand(20, 236), c.Rand(8, 20), Ink);
        for (var k = 0; k <= 2; k++) c.Stroke(0, k * 128, c.W, k * 128, 1.2f, Ink, 0.3f, 0.35f);
        return Done(c, "Concrete");
    }

    /// <summary>Paper notes and scribbles (pinned to boards), each a random scrawl of lines.</summary>
    public static Texture2D Notes()
    {
        var c = new InkCanvas(256, 256, new Color(0.96f, 0.93f, 0.84f), 67);
        for (var y = 30; y < 236; y += 18)
        {
            var x = 18f;
            while (x < 230)
            {
                var w = c.Rand(8, 36);
                c.Stroke(x, y + c.Rand(-1, 1), x + w, y + c.Rand(-1, 1), 1.2f, Ink, 0.9f, 0.6f);
                x += w + c.Rand(5, 10);
            }
        }

        Frame(c, 3f, 2.4f, 0.8f);
        return Done(c, "Notes", 0.06f, false);
    }

    /// <summary>A plain painted surface: a soft ink frame, no pattern (cabinets, signs, machines).</summary>
    public static Texture2D Painted()
    {
        var c = new InkCanvas(128, 128, new Color(0.95f, 0.93f, 0.89f), 68);
        Shade(c, 0.88f, 1.02f);
        Frame(c, 2.5f, 3f, 1f);
        return Done(c, "Painted", 0.06f, false);
    }

    /// <summary>A rope's twist (tiles along v).</summary>
    public static Texture2D RopeTwist()
    {
        var c = new InkCanvas(32, 128, new Color(0.9f, 0.8f, 0.62f), 69);
        for (var y = -32f; y < 160; y += 10) c.Stroke(0, y, 32, y + 16, 2f, Ink, 0.2f, 0.55f);
        return Done(c, "Rope", 0.05f);
    }
}
