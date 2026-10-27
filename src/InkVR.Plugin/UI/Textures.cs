using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// A tiny software canvas for painting textures in the game's hand-inked style (wobbly ink lines,
/// hatching, ink blots) at runtime, so nothing has to ship as art files.
/// </summary>
public class InkCanvas
{
    public readonly int W, H;
    public readonly Color[] Px;
    private readonly System.Random _rng;

    public InkCanvas(int w, int h, Color fill, int seed = 1)
    {
        W = w;
        H = h;
        Px = new Color[w * h];
        for (var i = 0; i < Px.Length; i++) Px[i] = fill;
        _rng = new System.Random(seed);
    }

    public float Rand() => (float)_rng.NextDouble();
    public float Rand(float a, float b) => a + (b - a) * Rand();

    // (Plain .NET maths throughout: in this game build every Unity maths call goes through IL2CPP and costs ~100 ns,
    // and painting makes millions of them.)

    public void Blend(int x, int y, Color c, float a)
    {
        if (W == 0) return;
        x = ((x % W) + W) % W; // wrap horizontally (cylindrical UVs)
        if (y < 0 || y >= H || a <= 0) return;
        var i = y * W + x;
        var t = a * c.a;
        if (t > 1f) t = 1f;
        var p = Px[i];
        p.r += (c.r - p.r) * t;
        p.g += (c.g - p.g) * t;
        p.b += (c.b - p.b) * t;
        p.a += (c.a - p.a) * t;
        Px[i] = p;
    }

    /// <summary>A soft disc (anti-aliased).</summary>
    public void Dot(float cx, float cy, float r, Color c, float alpha = 1f)
    {
        var x0 = (int)System.MathF.Floor(cx - r - 1);
        var x1 = (int)System.MathF.Ceiling(cx + r + 1);
        var y0 = (int)System.MathF.Floor(cy - r - 1);
        var y1 = (int)System.MathF.Ceiling(cy + r + 1);
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var d = System.MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            var k = r - d + 0.5f;
            if (k <= 0) continue;
            Blend(x, y, c, alpha * (k > 1f ? 1f : k));
        }
    }

    /// <summary>A hand-drawn stroke: tapered ends, slight wobble.</summary>
    public void Stroke(float x0, float y0, float x1, float y1, float width, Color c, float wobble = 0.6f, float alpha = 1f)
    {
        var len = System.MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        var steps = System.Math.Max(2, (int)System.MathF.Ceiling(len * 1.5f));
        var nx = -(y1 - y0) / System.Math.Max(len, 1e-3f);
        var ny = (x1 - x0) / System.Math.Max(len, 1e-3f);
        var phase = Rand(0, 6.28f);
        var freq = Rand(0.05f, 0.12f);
        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            var taper = System.MathF.Sin(t * System.MathF.PI) * 0.75f + 0.25f;
            var off = System.MathF.Sin(phase + i * freq) * wobble;
            Dot(x0 + (x1 - x0) * t + nx * off, y0 + (y1 - y0) * t + ny * off, width * 0.5f * taper, c, alpha);
        }
    }

    /// <summary>Parallel hatching inside a rectangle, at an angle (degrees).</summary>
    public void Hatch(float x, float y, float w, float h, float angle, float spacing, float width, Color c, float alpha)
    {
        var rad = angle * (System.MathF.PI / 180f);
        float dx = System.MathF.Cos(rad), dy = System.MathF.Sin(rad);
        float nx = -dy, ny = dx;
        float cx = x + w / 2, cy = y + h / 2;
        var reach = System.MathF.Sqrt(w * w + h * h) / 2;
        for (var o = -reach; o <= reach; o += spacing * Rand(0.8f, 1.2f))
        {
            float px = cx + nx * o, py = cy + ny * o;
            // clip to the rectangle by sampling: cheap and good enough for textures
            ClippedStroke(px - dx * reach, py - dy * reach, px + dx * reach, py + dy * reach, x, y, w, h, width, c, alpha * Rand(0.6f, 1f));
        }
    }

    private void ClippedStroke(float ax, float ay, float bx, float by, float x, float y, float w, float h, float width, Color c, float alpha)
    {
        var steps = (int)System.MathF.Ceiling(System.MathF.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)));
        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            float px = ax + (bx - ax) * t, py = ay + (by - ay) * t;
            if (px < x || px > x + w || py < y || py > y + h) continue;
            Dot(px, py, width * 0.5f, c, alpha);
        }
    }

    /// <summary>An ink blot with a few satellite drops and a downward drip (texture y-up).</summary>
    public void InkBlot(float cx, float cy, float r, Color ink)
    {
        Dot(cx, cy, r, ink);
        var lobes = 3 + _rng.Next(4);
        for (var i = 0; i < lobes; i++)
        {
            var ang = Rand(0, 6.28f);
            var d = r * Rand(0.5f, 1.0f);
            Dot(cx + System.MathF.Cos(ang) * d, cy + System.MathF.Sin(ang) * d, r * Rand(0.35f, 0.7f), ink);
        }

        var drops = 2 + _rng.Next(4);
        for (var i = 0; i < drops; i++)
        {
            var ang = Rand(0, 6.28f);
            var d = r * Rand(1.4f, 2.4f);
            Dot(cx + System.MathF.Cos(ang) * d, cy + System.MathF.Sin(ang) * d, r * Rand(0.08f, 0.22f), ink);
        }

        if (Rand() < 0.6f)
        {
            var len = r * Rand(1.2f, 3f);
            Stroke(cx, cy, cx + Rand(-1f, 1f), cy - len, r * 0.5f, ink, 0.2f);
            Dot(cx, cy - len, r * 0.3f, ink);
        }
    }

    /// <summary>Pixel i's colour times (r, g, b), alpha set to a (field by field: no Unity calls).</summary>
    public void Mul(int i, float r, float g, float b, float a = 1f)
    {
        var p = Px[i];
        p.r *= r;
        p.g *= g;
        p.b *= b;
        p.a = a;
        Px[i] = p;
    }

    /// <summary>Set pixel i (field by field: no Unity calls).</summary>
    public void Set(int i, float r, float g, float b, float a = 1f)
    {
        var p = Px[i];
        p.r = r;
        p.g = g;
        p.b = b;
        p.a = a;
        Px[i] = p;
    }

    /// <summary>Paper-like value noise multiplied into the colours.</summary>
    public void Grain(float strength)
    {
        for (var i = 0; i < Px.Length; i++)
        {
            var n = 1f + (Rand() - 0.5f) * strength;
            var p = Px[i];
            p.r *= n;
            p.g *= n;
            p.b *= n;
            Px[i] = p;
        }
    }

    public Texture2D ToTexture(string name, bool wrap = true, bool mips = true)
    {
        var t = new Texture2D(W, H, TextureFormat.RGBA32, mips) { name = name, wrapMode = wrap ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        t.SetPixels(Px);
        t.Apply(mips, true);
        t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(t);
        return t;
    }
}

/// <summary>Shared procedural textures.</summary>
public static class Textures
{
    private static Texture2D _softDot, _grain, _vignette;

    /// <summary>White soft round dot (laser tip, dust motes).</summary>
    public static Texture2D SoftDot => _softDot ??= MakeSoftDot();

    /// <summary>Monochrome film grain, tileable.</summary>
    public static Texture2D Grain => _grain ??= MakeGrain();

    /// <summary>White with transparent centre fading to opaque edges.</summary>
    public static Texture2D Vignette => _vignette ??= MakeVignette();

    private static Texture2D MakeSoftDot()
    {
        var c = new InkCanvas(64, 64, new Color(1, 1, 1, 0));
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
        {
            var d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
            c.Px[y * 64 + x] = new Color(1, 1, 1, Mathf.Clamp01(1 - d * d) * Mathf.Clamp01((1 - d) * 3));
        }

        return c.ToTexture("InkVR_SoftDot", false);
    }

    private static Texture2D MakeGrain()
    {
        var c = new InkCanvas(256, 256, Color.clear, 7);
        for (var i = 0; i < c.Px.Length; i++)
        {
            var v = c.Rand();
            c.Px[i] = new Color(v, v * 0.95f, v * 0.85f, Mathf.Abs(v - 0.5f) * 2f);
        }

        return c.ToTexture("InkVR_Grain");
    }

    private static Texture2D MakeVignette()
    {
        var c = new InkCanvas(128, 128, Color.clear);
        for (var y = 0; y < 128; y++)
        for (var x = 0; x < 128; x++)
        {
            var d = Vector2.Distance(new Vector2(x, y), new Vector2(63.5f, 63.5f)) / 64f;
            c.Px[y * 128 + x] = new Color(0, 0, 0, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.55f, 1.0f, d)));
        }

        return c.ToTexture("InkVR_Vignette", false);
    }
}
