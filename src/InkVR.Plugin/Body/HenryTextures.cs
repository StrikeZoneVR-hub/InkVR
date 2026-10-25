using InkVR.UI;
using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// Henry Stein's hands, painted in the game's sepia-and-ink style. Lore notes:
/// - Henry was one of the studio's animators: ink-stained fingertips, shirt sleeves rolled for desk work.
/// - He went home to Linda: a plain wedding band (ring texture is a flat warm gold).
/// - The deeper into the studio he goes, the more ink clings to him (<paramref name="inkLevel"/> grows by chapter).
/// UV layout (from MeshBuilder.Tube): u wraps around the limb (u≈0.25 = back of the hand/top), v runs along it.
/// </summary>
public static class HenryTextures
{
    public static readonly Color Ink = new(0.045f, 0.03f, 0.022f, 1f);

    /// <summary>The pixels of each generated texture by name (the textures themselves are GPU-only), so ink can be painted onto copies.</summary>
    public static readonly System.Collections.Generic.Dictionary<string, (int W, int H, Color32[] Px)> Pixels = new();

    internal static Texture2D Keep(InkCanvas c, string name)
    {
        var px = new Color32[c.Px.Length];
        for (var i = 0; i < px.Length; i++) px[i] = c.Px[i];
        Pixels[name] = (c.W, c.H, px);
        return c.ToTexture(name);
    }
    private static readonly Color Skin = new(0.82f, 0.58f, 0.33f, 1f);
    private static readonly Color SkinShade = new(0.60f, 0.38f, 0.19f, 1f);
    private static readonly Color Shirt = new(0.94f, 0.87f, 0.66f, 1f);
    private static readonly Color Leather = new(0.27f, 0.17f, 0.10f, 1f);

    /// <summary>Finger segments. <paramref name="tip"/> adds the fingernail and fingertip ink.</summary>
    public static Texture2D Finger(bool tip, float inkLevel, int seed)
    {
        var c = new InkCanvas(256, 128, Skin, seed);
        // Underside a touch darker, for a drawn, two-tone look.
        ShadeUnderside(c, SkinShade);

        if (!tip)
        {
            // Knuckle wrinkles on the back near the joint, crease on the palm side.
            for (var i = 0; i < 3; i++)
                c.Stroke(c.W * (0.19f + i * 0.03f), 6, c.W * (0.21f + i * 0.03f), 22, 1.4f, Ink, 0.4f, 0.8f);
            c.Stroke(c.W * 0.62f, 10, c.W * 0.88f, 12, 1.6f, Ink, 0.5f, 0.9f);
        }
        else
        {
            // Fingernail on the back of the tip.
            var nx = c.W * 0.25f;
            c.Stroke(nx - 18, c.H * 0.55f, nx - 18, c.H * 0.92f, 1.8f, Ink, 0.3f);
            c.Stroke(nx + 18, c.H * 0.55f, nx + 18, c.H * 0.92f, 1.8f, Ink, 0.3f);
            c.Stroke(nx - 18, c.H * 0.55f, nx + 18, c.H * 0.55f, 1.8f, Ink, 0.8f);
            c.Stroke(nx - 12, c.H * 0.62f, nx + 12, c.H * 0.62f, 1.0f, Ink, 0.4f, 0.5f);
            c.Stroke(c.W * 0.62f, 10, c.W * 0.88f, 12, 1.6f, Ink, 0.5f, 0.9f);
        }

        // An animator's ink: fingertips first, more of it every chapter.
        var blots = Mathf.RoundToInt(inkLevel * (tip ? 9 : 4));
        for (var i = 0; i < blots; i++)
        {
            var y = tip ? c.Rand(c.H * 0.45f, c.H * 0.98f) : c.Rand(0, c.H);
            c.InkBlot(c.Rand(0, c.W), y, c.Rand(4, 9 + inkLevel * 10), Ink);
        }

        c.Grain(0.08f);
        return Keep(c, tip ? "InkVR_HenryFingerTip" : "InkVR_HenryFinger");
    }

    /// <summary>Palm and back of the hand (flattened tube, v from wrist to knuckles).</summary>
    public static Texture2D Palm(float inkLevel, int seed)
    {
        var c = new InkCanvas(512, 256, Skin, seed);
        ShadeUnderside(c, SkinShade);
        // Back of the hand: tendons running to the knuckles, knuckle bumps.
        for (var i = 0; i < 4; i++)
        {
            var u = c.W * (0.16f + i * 0.03f);
            c.Stroke(u, 30, u + c.Rand(-4, 4), 200, 1.1f, Ink, 0.8f, 0.45f);
            c.Stroke(u - 6, 222, u + 6, 222, 1.4f, Ink, 0.4f, 0.8f);
        }

        // Palm side: heart, head and life lines.
        c.Stroke(c.W * 0.62f, 190, c.W * 0.88f, 170, 1.8f, Ink, 1.2f);
        c.Stroke(c.W * 0.60f, 150, c.W * 0.86f, 140, 1.6f, Ink, 1.2f);
        c.Stroke(c.W * 0.80f, 150, c.W * 0.72f, 50, 1.6f, Ink, 1.5f);
        c.Hatch(c.W * 0.55f, 20, c.W * 0.4f, 40, 35, 6, 0.9f, Ink, 0.3f);

        var blots = Mathf.RoundToInt(inkLevel * 6);
        for (var i = 0; i < blots; i++) c.InkBlot(c.Rand(0, c.W), c.Rand(120, c.H), c.Rand(5, 10 + inkLevel * 12), Ink);
        c.Grain(0.08f);
        return Keep(c, "InkVR_HenryPalm");
    }

    // ---------------- Toon: white rubber-hose cartoon gloves ----------------

    private static readonly Color Glove = new(0.96f, 0.93f, 0.85f, 1f);
    private static readonly Color GloveShade = new(0.78f, 0.72f, 0.62f, 1f);
    private static readonly Color CartoonSkin = new(0.93f, 0.71f, 0.5f, 1f);

    /// <summary>A glove's finger: clean cream-white with a drawn shading band underneath; ink gets on it chapter by chapter.</summary>
    public static Texture2D GloveFinger(float inkLevel, int seed)
    {
        var c = new InkCanvas(256, 128, Glove, seed);
        ShadeUnderside(c, GloveShade);
        // a single drawn crease on the palm side at the joint, as on a cartoon glove
        c.Stroke(c.W * 0.66f, 8, c.W * 0.84f, 9, 1.6f, Ink, 0.4f, 0.75f);
        var blots = Mathf.RoundToInt(inkLevel * 5);
        for (var i = 0; i < blots; i++) c.InkBlot(c.Rand(0, c.W), c.Rand(c.H * 0.4f, c.H), c.Rand(4, 8 + inkLevel * 8), Ink);
        c.Grain(0.04f);
        return Keep(c, "InkVR_GloveFinger");
    }

    /// <summary>The glove's hand: the three stitch lines down the back, as on every cartoon glove of the period.</summary>
    public static Texture2D GlovePalm(float inkLevel, int seed)
    {
        var c = new InkCanvas(512, 256, Glove, seed);
        ShadeUnderside(c, GloveShade);
        for (var i = 0; i < 3; i++)
        {
            var u = c.W * (0.19f + i * 0.06f);
            c.Stroke(u, c.H * 0.22f, u + c.Rand(-3, 3), c.H * 0.86f, 2.6f, Ink, 0.5f);
        }

        var blots = Mathf.RoundToInt(inkLevel * 5);
        for (var i = 0; i < blots; i++) c.InkBlot(c.Rand(0, c.W), c.Rand(c.H * 0.3f, c.H), c.Rand(5, 9 + inkLevel * 10), Ink);
        c.Grain(0.04f);
        return Keep(c, "InkVR_GlovePalm");
    }

    /// <summary>The glove's flared cuff: white with a bold inked rim at the open end (v = 1).</summary>
    public static Texture2D GloveCuff(int seed)
    {
        var c = new InkCanvas(256, 64, Glove, seed);
        ShadeUnderside(c, GloveShade);
        c.Stroke(0, c.H * 0.86f, c.W, c.H * 0.86f, 3.2f, Ink, 0.6f);
        c.Grain(0.04f);
        return Keep(c, "InkVR_GloveCuff");
    }

    /// <summary>Toon forearm: flat cartoon skin, a drawn shading band, nothing else.</summary>
    public static Texture2D CartoonForearm(float inkLevel, int seed)
    {
        var c = new InkCanvas(256, 128, CartoonSkin, seed);
        ShadeUnderside(c, new Color(0.76f, 0.52f, 0.33f, 1f));
        var drips = Mathf.RoundToInt(inkLevel * 3);
        for (var i = 0; i < drips; i++) c.InkBlot(c.Rand(0, c.W), c.Rand(c.H * 0.5f, c.H), c.Rand(4, 9), Ink);
        c.Grain(0.03f);
        return Keep(c, "InkVR_CartoonForearm");
    }

    /// <summary>Forearm skin (v from elbow to wrist), with a few drawn hairs.</summary>
    public static Texture2D Forearm(float inkLevel, int seed)
    {
        var c = new InkCanvas(512, 256, Skin, seed);
        ShadeUnderside(c, SkinShade);
        for (var i = 0; i < 26; i++)
        {
            var u = c.Rand(c.W * 0.05f, c.W * 0.45f);
            var v = c.Rand(20, c.H - 20);
            c.Stroke(u, v, u + c.Rand(-3, 3), v + c.Rand(6, 12), 0.8f, Ink, 0.2f, 0.45f);
        }

        var drips = Mathf.RoundToInt(inkLevel * 4);
        for (var i = 0; i < drips; i++) c.InkBlot(c.Rand(0, c.W), c.Rand(c.H * 0.6f, c.H), c.Rand(5, 12), Ink);
        c.Grain(0.08f);
        return Keep(c, "InkVR_HenryForearm");
    }

    /// <summary>Shirt sleeve: folds, seam, and the rolled cuff band at the wrist end (v = 1).</summary>
    public static Texture2D Sleeve(bool rolledEnd, int seed)
    {
        var c = new InkCanvas(512, 256, Shirt, seed);
        ShadeUnderside(c, new Color(0.74f, 0.69f, 0.58f));
        // Seam along the underside.
        c.Stroke(c.W * 0.75f, 0, c.W * 0.75f, c.H, 1.2f, Ink, 0.6f, 0.7f);
        for (var i = 0; i < 14; i++)
        {
            // Fold creases.
            var u = c.Rand(0, c.W);
            var v = c.Rand(10, c.H * 0.8f);
            c.Stroke(u, v, u + c.Rand(-30, 30), v + c.Rand(18, 50), c.Rand(1.0f, 1.8f), Ink, 1.2f, 0.85f);
        }

        if (rolledEnd)
        {
            // The rolled cuff: a band with doubled ink lines and a little hatching in the roll's shadow.
            c.Stroke(0, c.H * 0.80f, c.W, c.H * 0.80f, 2.4f, Ink, 0.8f);
            c.Stroke(0, c.H * 0.97f, c.W, c.H * 0.97f, 2.4f, Ink, 0.8f);
            c.Hatch(0, c.H * 0.80f, c.W, c.H * 0.05f, 60, 7, 1.0f, Ink, 0.4f);
        }

        c.Grain(0.07f);
        return Keep(c, rolledEnd ? "InkVR_HenrySleeveRolled" : "InkVR_HenrySleeve");
    }

    public static Texture2D Strap(int seed)
    {
        var c = new InkCanvas(256, 64, Leather, seed);
        c.Stroke(0, 6, c.W, 6, 1.0f, new Color(0.6f, 0.5f, 0.35f), 0.2f, 0.7f); // stitching
        c.Stroke(0, c.H - 6, c.W, c.H - 6, 1.0f, new Color(0.6f, 0.5f, 0.35f), 0.2f, 0.7f);
        for (var x = 0; x < c.W; x += 64) c.Stroke(x, 0, x, c.H, 1.6f, Ink, 0.2f); // band holes/links
        c.Grain(0.1f);
        return Keep(c, "InkVR_HenryStrap");
    }

    /// <summary>Watch dial on the top cap of an ellipsoid (v=1 is the dial centre, v=0.5 its rim).</summary>
    public static Texture2D Dial(int seed)
    {
        var c = new InkCanvas(256, 128, new Color(0.93f, 0.88f, 0.74f), seed);
        for (var y = 0; y < c.H; y++)
        {
            var v = (float)y / (c.H - 1);
            if (v < 0.5f)
            {
                // Lower half of the ellipsoid: the brass case.
                for (var x = 0; x < c.W; x++) c.Px[y * c.W + x] = new Color(0.55f, 0.42f, 0.2f);
            }
        }

        c.Stroke(0, c.H * 0.5f, c.W, c.H * 0.5f, 3f, Ink, 0.1f); // bezel
        for (var h = 0; h < 12; h++)
        {
            var u = h / 12f * c.W;
            c.Stroke(u, c.H * 0.56f, u, c.H * (h % 3 == 0 ? 0.68f : 0.62f), h % 3 == 0 ? 2.2f : 1.4f, Ink, 0.1f);
        }

        c.Grain(0.06f);
        return Keep(c, "InkVR_HenryDial");
    }

    // ---------------- lifelike hands ----------------

    private static readonly Color LifeSkin = new(0.87f, 0.67f, 0.53f, 1f);
    private static readonly Color LifePalmSide = new(0.93f, 0.76f, 0.64f, 1f);
    private static readonly Color LifeShade = new(0.7f, 0.49f, 0.38f, 1f);
    private static readonly Color Crease = new(0.42f, 0.27f, 0.2f, 1f);

    /// <summary>Soft, uneven colour in the skin (a few big faint blotches), so it doesn't read as painted wood.</summary>
    private static void Mottle(InkCanvas c, int count, float size)
    {
        for (var i = 0; i < count; i++)
        {
            var warm = c.Rand() < 0.5f;
            c.Dot(c.Rand(0, c.W), c.Rand(0, c.H), c.Rand(size * 0.5f, size), warm ? new Color(0.9f, 0.55f, 0.45f) : LifeShade, 0.06f);
        }
    }

    /// <summary>Lifelike finger skin (tube: u around, back of the finger at u = 0.25; v from inside the palm to the tip).</summary>
    public static Texture2D LifelikeFinger(float inkLevel, int seed)
    {
        var c = new InkCanvas(256, 256, LifeSkin, seed);
        ShadeUnderside(c, LifePalmSide);
        Mottle(c, 40, 16);
        // Knuckle creases on the back at the middle and end joints, flexion creases on the palm side.
        var back = c.W * 0.25f;
        foreach (var (v, lines) in new[] { (0.545f, 3), (0.785f, 2) })
            for (var i = 0; i < lines; i++)
            {
                var y = c.H * v + (i - (lines - 1) * 0.5f) * 5f;
                c.Stroke(back - 22 + c.Rand(-3, 3), y + 2, back, y - 2, 1.1f, Crease, 0.6f, 0.45f);
                c.Stroke(back, y - 2, back + 22 + c.Rand(-3, 3), y + 2, 1.1f, Crease, 0.6f, 0.45f);
            }

        foreach (var v in new[] { 0.5f, 0.75f })
            c.Stroke(c.W * 0.62f, c.H * v, c.W * 0.88f, c.H * v + c.Rand(-2, 2), 1.4f, Crease, 0.4f, 0.55f);

        // Rosier fingertips.
        for (var y = (int)(c.H * 0.86f); y < c.H; y++)
        for (var x = 0; x < c.W; x++)
            c.Px[y * c.W + x] = Color.Lerp(c.Px[y * c.W + x], new Color(0.93f, 0.66f, 0.56f), 0.25f);

        var blots = Mathf.RoundToInt(inkLevel * 7);
        for (var i = 0; i < blots; i++) c.InkBlot(c.Rand(0, c.W), c.Rand(c.H * 0.8f, c.H), c.Rand(4, 8 + inkLevel * 8), Ink);
        c.Grain(0.05f);
        return Keep(c, "InkVR_LifeFinger");
    }

    /// <summary>Lifelike palm and back of the hand (loft: u around, back at u = 0.25; v from the wrist to the knuckles).</summary>
    public static Texture2D LifelikePalm(float inkLevel, int seed)
    {
        var c = new InkCanvas(512, 256, LifeSkin, seed);
        ShadeUnderside(c, LifePalmSide);
        Mottle(c, 60, 24);
        // Back: tendons fanning out to the knuckles, a couple of veins.
        for (var i = 0; i < 4; i++)
        {
            var u0 = c.W * (0.235f + (i - 1.5f) * 0.012f);
            var u1 = c.W * (0.25f + (i - 1.5f) * 0.045f);
            c.Stroke(u0, c.H * 0.12f, u1, c.H * 0.9f, 1.0f, Crease, 1.2f, 0.25f);
        }

        for (var k = 0; k < 2; k++)
        {
            var u = c.W * (0.2f + k * 0.08f);
            var prev = new Vector2(u, c.H * 0.08f);
            for (var y = 0.12f; y < 0.8f; y += 0.08f)
            {
                var next = new Vector2(u + Mathf.Sin(y * 9f + k) * 10f, c.H * y);
                c.Stroke(prev.x, prev.y, next.x, next.y, 2.2f, new Color(0.55f, 0.45f, 0.45f), 0.3f, 0.22f);
                prev = next;
            }
        }

        // Palm: heart, head and life lines, and the crease at the wrist.
        c.Stroke(c.W * 0.62f, c.H * 0.82f, c.W * 0.86f, c.H * 0.74f, 1.6f, Crease, 1.2f, 0.65f);
        c.Stroke(c.W * 0.6f, c.H * 0.68f, c.W * 0.84f, c.H * 0.6f, 1.5f, Crease, 1.2f, 0.6f);
        c.Stroke(c.W * 0.82f, c.H * 0.62f, c.W * 0.74f, c.H * 0.2f, 1.5f, Crease, 1.6f, 0.6f);
        c.Stroke(c.W * 0.58f, c.H * 0.08f, c.W * 0.92f, c.H * 0.08f, 1.2f, Crease, 1.0f, 0.4f);

        var blots = Mathf.RoundToInt(inkLevel * 5);
        for (var i = 0; i < blots; i++) c.InkBlot(c.Rand(0, c.W), c.Rand(c.H * 0.5f, c.H), c.Rand(5, 9 + inkLevel * 10), Ink);
        c.Grain(0.05f);
        return Keep(c, "InkVR_LifePalm");
    }

    /// <summary>A fingernail (ellipsoid: u = 0.25 points to the fingertip): pale pink, a lighter free edge, a shine.</summary>
    public static Texture2D LifelikeNail(int seed)
    {
        var c = new InkCanvas(64, 64, new Color(0.95f, 0.8f, 0.74f), seed);
        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
        {
            var du = Mathf.Abs(Mathf.DeltaAngle(x / (float)c.W * 360f, 90f)) / 180f; // 0 at the fingertip end
            if (du < 0.22f && y > c.H * 0.35f) c.Px[y * c.W + x] = new Color(0.99f, 0.94f, 0.88f);
        }

        c.Stroke(c.W * 0.7f, c.H * 0.7f, c.W * 0.8f, c.H * 0.85f, 2f, new Color(1, 1, 1), 0.2f, 0.6f);
        return Keep(c, "InkVR_LifeNail");
    }

    /// <summary>Lifelike forearm (v from the elbow to the wrist): fine hairs, a vein or two on the inside.</summary>
    public static Texture2D LifelikeForearm(float inkLevel, int seed)
    {
        var c = new InkCanvas(512, 256, LifeSkin, seed);
        ShadeUnderside(c, LifePalmSide);
        Mottle(c, 50, 30);
        for (var i = 0; i < 40; i++)
        {
            var u = c.Rand(c.W * 0.05f, c.W * 0.45f);
            var v = c.Rand(20, c.H - 20);
            c.Stroke(u, v, u + c.Rand(-2, 2), v + c.Rand(4, 8), 0.6f, Crease, 0.2f, 0.3f);
        }

        for (var k = 0; k < 2; k++)
        {
            var u = c.W * (0.7f + k * 0.07f);
            c.Stroke(u, c.H * 0.1f, u + c.Rand(-12, 12), c.H * 0.95f, 2.4f, new Color(0.55f, 0.45f, 0.45f), 2f, 0.18f);
        }

        var drips = Mathf.RoundToInt(inkLevel * 3);
        for (var i = 0; i < drips; i++) c.InkBlot(c.Rand(0, c.W), c.Rand(c.H * 0.6f, c.H), c.Rand(5, 10), Ink);
        c.Grain(0.05f);
        return Keep(c, "InkVR_LifeForearm");
    }

    /// <summary>
    /// The tank watch's dial (u from 9 to 3 o'clock, v from 6 up to 12): cream, an inked border and minute
    /// track, bold 12 / 3 / 6 / 9 numerals and a small "J.D.S." under the 12.
    /// </summary>
    public static Texture2D WatchFace(int seed)
    {
        var c = new InkCanvas(256, 352, new Color(0.96f, 0.91f, 0.77f), seed);
        // A soft vignette, like old enamel.
        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
        {
            var dx = (x - 127.5f) / 128f;
            var dy = (y - 175.5f) / 176f;
            var k = 1f - 0.12f * Mathf.Clamp01(dx * dx + dy * dy);
            var p = c.Px[y * c.W + x];
            c.Px[y * c.W + x] = new Color(p.r * k, p.g * k, p.b * k, 1);
        }

        Rect(c, 5, 6f);
        Rect(c, 30, 1.6f);

        // Minute track: a tick for every minute between the two frames, longer at each hour.
        var cx = c.W * 0.5f;
        var cy = c.H * 0.5f;
        for (var m = 0; m < 60; m++)
        {
            var a = m / 60f * Mathf.PI * 2;
            var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            var outer = ToEdge(dir, cx - 14, cy - 14);
            var inner = ToEdge(dir, cx - (m % 5 == 0 ? 40 : 31), cy - (m % 5 == 0 ? 40 : 31));
            c.Stroke(cx + inner.x, cy + inner.y, cx + outer.x, cy + outer.y, m % 5 == 0 ? 3.2f : 1.4f, Ink, 0.1f);
        }

        Number(c, "12", cx, c.H - 78, 52);
        Number(c, "6", cx, 78, 52);
        Number(c, "3", c.W - 70, cy, 52);
        Number(c, "9", 70, cy, 52);
        c.Dot(cx - 14, c.H - 120, 2f, Ink);
        c.Stroke(cx - 6, c.H - 112, cx - 6, c.H - 126, 1.4f, Ink, 0.1f);
        c.Stroke(cx + 2, c.H - 112, cx + 8, c.H - 126, 1.4f, Ink, 0.1f);
        c.Stroke(cx + 8, c.H - 126, cx + 14, c.H - 112, 1.4f, Ink, 0.1f);
        c.Grain(0.05f);
        return Keep(c, "InkVR_HenryWatchFace");
    }

    private static void Rect(InkCanvas c, float inset, float width)
    {
        float x0 = inset, y0 = inset, x1 = c.W - 1 - inset, y1 = c.H - 1 - inset;
        c.Stroke(x0, y0, x1, y0, width, Ink, 0.2f);
        c.Stroke(x1, y0, x1, y1, width, Ink, 0.2f);
        c.Stroke(x1, y1, x0, y1, width, Ink, 0.2f);
        c.Stroke(x0, y1, x0, y0, width, Ink, 0.2f);
    }

    /// <summary>Where a ray from the centre leaves a centred rectangle of these half-sizes.</summary>
    private static Vector2 ToEdge(Vector2 dir, float hw, float hh)
    {
        var t = Mathf.Min(Mathf.Abs(dir.x) > 1e-5f ? hw / Mathf.Abs(dir.x) : float.MaxValue, Mathf.Abs(dir.y) > 1e-5f ? hh / Mathf.Abs(dir.y) : float.MaxValue);
        return dir * t;
    }

    /// <summary>Hand-lettered digits (polylines in a unit box, y up), centred at (x, y).</summary>
    private static void Number(InkCanvas c, string text, float x, float y, float height)
    {
        var w = height * 0.55f;
        var gap = height * 0.12f;
        var total = text.Length * w + (text.Length - 1) * gap;
        var left = x - total * 0.5f;
        foreach (var ch in text)
        {
            if (Glyphs.TryGetValue(ch, out var strokes))
                foreach (var line in strokes)
                    for (var i = 0; i < line.Length - 1; i++)
                        c.Stroke(left + line[i].x * w, y - height * 0.5f + line[i].y * height, left + line[i + 1].x * w, y - height * 0.5f + line[i + 1].y * height, height * 0.13f, Ink, 0.15f);
            left += w + gap;
        }
    }

    private static readonly System.Collections.Generic.Dictionary<char, Vector2[][]> Glyphs = new()
    {
        ['1'] = new[] { new[] { new Vector2(0.25f, 0.78f), new Vector2(0.6f, 1f), new Vector2(0.6f, 0f) }, new[] { new Vector2(0.25f, 0f), new Vector2(0.92f, 0f) } },
        ['2'] = new[]
        {
            new[]
            {
                new Vector2(0.08f, 0.76f), new Vector2(0.25f, 0.95f), new Vector2(0.5f, 1f), new Vector2(0.78f, 0.93f), new Vector2(0.9f, 0.74f), new Vector2(0.82f, 0.52f),
                new Vector2(0.08f, 0f), new Vector2(0.94f, 0f)
            }
        },
        ['3'] = new[]
        {
            new[]
            {
                new Vector2(0.1f, 0.9f), new Vector2(0.4f, 1f), new Vector2(0.75f, 0.95f), new Vector2(0.86f, 0.78f), new Vector2(0.72f, 0.58f), new Vector2(0.38f, 0.53f),
                new Vector2(0.76f, 0.47f), new Vector2(0.92f, 0.26f), new Vector2(0.76f, 0.05f), new Vector2(0.42f, 0f), new Vector2(0.08f, 0.1f)
            }
        },
        ['6'] = new[]
        {
            new[]
            {
                new Vector2(0.82f, 0.94f), new Vector2(0.52f, 1f), new Vector2(0.22f, 0.82f), new Vector2(0.1f, 0.45f), new Vector2(0.16f, 0.14f), new Vector2(0.46f, 0f),
                new Vector2(0.78f, 0.08f), new Vector2(0.9f, 0.32f), new Vector2(0.76f, 0.55f), new Vector2(0.46f, 0.6f), new Vector2(0.14f, 0.42f)
            }
        },
        ['9'] = new[]
        {
            new[]
            {
                new Vector2(0.18f, 0.06f), new Vector2(0.48f, 0f), new Vector2(0.78f, 0.18f), new Vector2(0.9f, 0.55f), new Vector2(0.84f, 0.86f), new Vector2(0.54f, 1f),
                new Vector2(0.22f, 0.92f), new Vector2(0.1f, 0.68f), new Vector2(0.24f, 0.45f), new Vector2(0.54f, 0.4f), new Vector2(0.86f, 0.58f)
            }
        },
    };

    public static Texture2D Flat(Color color, string name)
    {
        var c = new InkCanvas(16, 16, color);
        return Keep(c, name);
    }

    private static void ShadeUnderside(InkCanvas c, Color shade)
    {
        // u≈0.75 is the underside (palm side): blend towards the shade colour with a soft falloff.
        for (var x = 0; x < c.W; x++)
        {
            var u = (float)x / c.W;
            var d = Mathf.Abs(Mathf.DeltaAngle(u * 360f, 270f)) / 180f; // 0 at underside, 1 on top
            var k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(1 - d * 1.6f)) * 0.6f;
            for (var y = 0; y < c.H; y++)
            {
                var i = y * c.W + x;
                c.Px[i] = Color.Lerp(c.Px[i], shade, k);
            }
        }
    }
}
