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

    public void Blend(int x, int y, Color c, float a)
    {
        if (W == 0) return;
        x = ((x % W) + W) % W; // wrap horizontally (cylindrical UVs)
        if (y < 0 || y >= H || a <= 0) return;
        var i = y * W + x;
        Px[i] = Color.Lerp(Px[i], c, Mathf.Clamp01(a * c.a));
    }

    /// <summary>A soft disc (anti-aliased).</summary>
    public void Dot(float cx, float cy, float r, Color c, float alpha = 1f)
    {
        var x0 = Mathf.FloorToInt(cx - r - 1);
        var x1 = Mathf.CeilToInt(cx + r + 1);
        var y0 = Mathf.FloorToInt(cy - r - 1);
        var y1 = Mathf.CeilToInt(cy + r + 1);
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            Blend(x, y, c, alpha * Mathf.Clamp01(r - d + 0.5f));
        }
    }

    /// <summary>A hand-drawn stroke: tapered ends, slight wobble.</summary>
    public void Stroke(float x0, float y0, float x1, float y1, float width, Color c, float wobble = 0.6f, float alpha = 1f)
    {
        var len = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        var steps = Mathf.Max(2, Mathf.CeilToInt(len * 1.5f));
        var nx = -(y1 - y0) / Mathf.Max(len, 1e-3f);
        var ny = (x1 - x0) / Mathf.Max(len, 1e-3f);
        var phase = Rand(0, 6.28f);
        var freq = Rand(0.05f, 0.12f);
        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            var taper = Mathf.Sin(t * Mathf.PI) * 0.75f + 0.25f;
            var off = Mathf.Sin(phase + i * freq) * wobble;
            Dot(Mathf.Lerp(x0, x1, t) + nx * off, Mathf.Lerp(y0, y1, t) + ny * off, width * 0.5f * taper, c, alpha);
        }
    }

    /// <summary>Parallel hatching inside a rectangle, at an angle (degrees).</summary>
    public void Hatch(float x, float y, float w, float h, float angle, float spacing, float width, Color c, float alpha)
    {
        var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        var n = new Vector2(-dir.y, dir.x);
        var center = new Vector2(x + w / 2, y + h / 2);
        var reach = Mathf.Sqrt(w * w + h * h) / 2;
        for (var o = -reach; o <= reach; o += spacing * Rand(0.8f, 1.2f))
        {
            var p = center + n * o;
            var a = p - dir * reach;
            var b = p + dir * reach;
            // Clip to the rectangle by sampling: cheap and good enough for textures.
            ClippedStroke(a, b, x, y, w, h, width, c, alpha * Rand(0.6f, 1f));
        }
    }

    private void ClippedStroke(Vector2 a, Vector2 b, float x, float y, float w, float h, float width, Color c, float alpha)
    {
        var steps = Mathf.CeilToInt(Vector2.Distance(a, b));
        for (var i = 0; i <= steps; i++)
        {
            var p = Vector2.Lerp(a, b, (float)i / steps);
            if (p.x < x || p.x > x + w || p.y < y || p.y > y + h) continue;
            Dot(p.x, p.y, width * 0.5f, c, alpha);
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
            Dot(cx + Mathf.Cos(ang) * d, cy + Mathf.Sin(ang) * d, r * Rand(0.35f, 0.7f), ink);
        }

        var drops = 2 + _rng.Next(4);
        for (var i = 0; i < drops; i++)
        {
            var ang = Rand(0, 6.28f);
            var d = r * Rand(1.4f, 2.4f);
            Dot(cx + Mathf.Cos(ang) * d, cy + Mathf.Sin(ang) * d, r * Rand(0.08f, 0.22f), ink);
        }

        if (Rand() < 0.6f)
        {
            var len = r * Rand(1.2f, 3f);
            Stroke(cx, cy, cx + Rand(-1f, 1f), cy - len, r * 0.5f, ink, 0.2f);
            Dot(cx, cy - len, r * 0.3f, ink);
        }
    }

    /// <summary>Paper-like value noise multiplied into the colours.</summary>
    public void Grain(float strength)
    {
        for (var i = 0; i < Px.Length; i++)
        {
            var n = 1f + (Rand() - 0.5f) * strength;
            var p = Px[i];
            Px[i] = new Color(p.r * n, p.g * n, p.b * n, p.a);
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
