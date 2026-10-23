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

    private static Texture2D Keep(InkCanvas c, string name)
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
