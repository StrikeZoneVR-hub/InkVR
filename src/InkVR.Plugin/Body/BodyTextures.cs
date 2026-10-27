using InkVR.UI;
using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// Henry's clothes, painted in the game's sepia-and-ink style. Tube UVs: u wraps around the body part
/// (u = 0.25 is the front, 0.75 the back), v runs along it (torso: waist to collar; legs: hip to ankle).
/// Ellipsoid UVs (shoes): u = 0.25 at the front, v = 1 on top. Ink creeps further up him each chapter.
/// </summary>
public static class BodyTextures
{
    private static readonly Color Ink = HenryTextures.Ink;

    public static readonly Color ToonShirt = new(0.94f, 0.87f, 0.66f);
    public static readonly Color ToonTrousers = new(0.47f, 0.32f, 0.2f);
    public static readonly Color ToonBraces = new(0.5f, 0.15f, 0.09f);
    public static readonly Color ToonShoe = new(0.1f, 0.075f, 0.06f); // glossy cartoon black
    public static readonly Color StudioShirt = new(0.93f, 0.87f, 0.72f);
    public static readonly Color StudioVest = new(0.3f, 0.21f, 0.14f);
    public static readonly Color StudioTrousers = new(0.36f, 0.32f, 0.27f);
    public static readonly Color StudioBoot = new(0.15f, 0.095f, 0.065f);
    public static readonly Color Belt = new(0.17f, 0.1f, 0.06f);

    private static Texture2D Done(InkCanvas c, string name, float grain = 0.08f)
    {
        c.Grain(grain);
        return HenryTextures.Keep(c, name); // keeps the pixels too, so ink can be painted onto Henry's clothes
    }

    /// <summary>Darken towards the sides and back, for a drawn, two-tone look.</summary>
    private static void Shade(InkCanvas c, float strength)
    {
        for (var x = 0; x < c.W; x++)
        {
            var u = (float)x / c.W;
            var front = Mathf.Cos((u - 0.25f) * Mathf.PI * 2) * 0.5f + 0.5f; // 1 at the front, 0 at the back
            var k = 1f - strength * (1f - front);
            for (var y = 0; y < c.H; y++)
            {
                var p = c.Px[y * c.W + x];
                c.Mul(y * c.W + x, k, k, k);
            }
        }
    }

    /// <summary>Cartoon two-tone: flat colour across the front, one hard step of shadow round the back, as the cartoons paint it.</summary>
    private static void CelShade(InkCanvas c, float strength)
    {
        for (var x = 0; x < c.W; x++)
        {
            var u = (float)x / c.W;
            var front = Mathf.Cos((u - 0.25f) * Mathf.PI * 2) * 0.5f + 0.5f;
            var k = front > 0.36f ? 1f : front > 0.3f ? 1f - strength * 0.5f : 1f - strength;
            for (var y = 0; y < c.H; y++)
            {
                var p = c.Px[y * c.W + x];
                c.Mul(y * c.W + x, k, k, k);
            }
        }
    }

    private static void Spatter(InkCanvas c, float inkLevel, float vMin, float vMax, int count, float size)
    {
        var n = Mathf.RoundToInt(count * inkLevel);
        for (var i = 0; i < n; i++) c.InkBlot(c.Rand(0, c.W), c.Rand(c.H * vMin, c.H * vMax), c.Rand(size * 0.5f, size * (1f + inkLevel)), Ink);
    }

    /// <summary>The shirt over the torso: button placket down the front, folds at the waist, a chest pocket.</summary>
    public static Texture2D Shirt(bool studio, float inkLevel)
    {
        var c = new InkCanvas(512, 512, studio ? StudioShirt : ToonShirt, studio ? 201 : 101);
        if (studio) Shade(c, 0.28f);
        else CelShade(c, 0.3f);
        var front = c.W * 0.25f;
        c.Stroke(front - 7, 0, front - 7, c.H * 0.97f, 1.6f, Ink, 0.4f, 0.85f);
        c.Stroke(front + 7, 0, front + 7, c.H * 0.97f, 1.6f, Ink, 0.4f, 0.85f);
        for (var y = 40f; y < c.H * 0.92f; y += 62f)
        {
            c.Dot(front, y, 4.2f, new Color(0.98f, 0.95f, 0.88f));
            c.Dot(front, y, 4.6f, Ink, 0.35f);
            c.Dot(front, y, 1.2f, Ink, 0.7f);
        }

        if (!studio)
        {
            // Chest pocket on his left, with a pencil in it (he's an animator).
            float x0 = c.W * 0.1f, x1 = c.W * 0.19f, y0 = c.H * 0.56f, y1 = c.H * 0.74f;
            c.Stroke(x0, y0, x1, y0, 1.6f, Ink, 0.4f);
            c.Stroke(x0, y0, x0, y1, 1.6f, Ink, 0.4f);
            c.Stroke(x1, y0, x1, y1, 1.6f, Ink, 0.4f);
            c.Stroke(x0, y1 - 14, x1, y1 - 14, 1.4f, Ink, 0.4f, 0.8f);
            c.Stroke(x0 + 14, y1 - 30, x0 + 18, y1 + 26, 6f, new Color(0.85f, 0.62f, 0.2f), 0.1f);
            c.Stroke(x0 + 14, y1 - 30, x0 + 18, y1 + 26, 1f, Ink, 0.1f, 0.6f);
        }

        // Creases: gathered at the waist, folds under the arms. The cartoon gets a few bold, inked ones.
        for (var i = 0; i < (studio ? 18 : 9); i++)
        {
            var u = c.Rand(0, c.W);
            c.Stroke(u, c.Rand(4, 30), u + c.Rand(-20, 20), c.Rand(50, 110), studio ? c.Rand(1.0f, 1.6f) : c.Rand(2.4f, 3.4f), Ink, studio ? 1f : 0.5f, studio ? 0.6f : 0.85f);
        }

        foreach (var side in new[] { 0f, 0.5f })
            for (var i = 0; i < (studio ? 5 : 3); i++)
            {
                var u = c.W * side + c.Rand(-25, 25);
                var y = c.H * c.Rand(0.72f, 0.9f);
                c.Stroke(u, y, u + c.Rand(-35, 35), y - c.Rand(30, 60), studio ? 1.3f : 2.8f, Ink, studio ? 0.8f : 0.5f, studio ? 0.6f : 0.85f);
            }

        if (studio) c.Hatch(c.W * 0.6f, 0, c.W * 0.3f, c.H, 70, 8, 0.8f, Ink, 0.18f);
        else c.Hatch(c.W * 0.6f, 0, c.W * 0.3f, c.H, 70, 14, 2.2f, Ink, 0.3f);
        Spatter(c, inkLevel, 0.0f, 0.5f, 5, 7f);
        return Done(c, studio ? "InkVR_StudioShirt" : "InkVR_ToonShirt");
    }

    /// <summary>A waistcoat: buttoned below a V that shows the shirt, welt pockets, a satin back.</summary>
    public static Texture2D Vest(float inkLevel)
    {
        var c = new InkCanvas(512, 512, StudioVest, 301);
        Shade(c, 0.3f);
        var front = c.W * 0.25f;
        // Satin back panel, lighter, with its buckle strap.
        for (var y = 0; y < c.H; y++)
        for (var x = (int)(c.W * 0.6f); x < (int)(c.W * 0.9f); x++)
        {
            var p = c.Px[y * c.W + x];
            c.Mul(y * c.W + x, 1.35f, 1.3f, 1.25f);
        }

        c.Stroke(c.W * 0.6f, 0, c.W * 0.6f, c.H, 1.6f, Ink, 0.3f);
        c.Stroke(c.W * 0.9f, 0, c.W * 0.9f, c.H, 1.6f, Ink, 0.3f);
        c.Stroke(c.W * 0.68f, c.H * 0.3f, c.W * 0.82f, c.H * 0.3f, 3f, Ink, 0.3f, 0.8f);

        // The V: shirt colour above the top button.
        const float vTop = 1f, vBottom = 0.5f;
        for (var y = (int)(c.H * vBottom); y < c.H; y++)
        {
            var t = (y / (float)c.H - vBottom) / (vTop - vBottom);
            var half = c.W * 0.085f * t;
            for (var x = (int)(front - half); x <= (int)(front + half); x++)
                c.Px[y * c.W + ((x % c.W) + c.W) % c.W] = StudioShirt;
        }

        c.Stroke(front, c.H * vBottom, front - c.W * 0.085f, c.H, 2.2f, Ink, 0.3f);
        c.Stroke(front, c.H * vBottom, front + c.W * 0.085f, c.H, 2.2f, Ink, 0.3f);
        c.Stroke(front, 6, front, c.H * vBottom, 1.8f, Ink, 0.3f);
        for (var k = 0; k < 4; k++)
        {
            var y = c.H * (0.08f + k * 0.12f);
            c.Dot(front + 9, y, 5f, new Color(0.75f, 0.58f, 0.3f));
            c.Dot(front + 9, y, 5.6f, Ink, 0.4f);
        }

        // Welt pockets and the pointed hem at the front.
        foreach (var u in new[] { 0.15f, 0.35f })
            c.Stroke(c.W * u - 26, c.H * 0.3f, c.W * u + 26, c.H * 0.32f, 2.4f, Ink, 0.4f);
        c.Stroke(front - 60, 22, front, 2, 2f, Ink, 0.3f);
        c.Stroke(front + 60, 22, front, 2, 2f, Ink, 0.3f);
        c.Hatch(0, 0, c.W * 0.1f, c.H, 75, 7, 0.9f, Ink, 0.25f);
        c.Hatch(c.W * 0.4f, 0, c.W * 0.18f, c.H, 105, 7, 0.9f, Ink, 0.25f);
        Spatter(c, inkLevel, 0f, 0.6f, 4, 7f);
        return Done(c, "InkVR_StudioVest");
    }

    /// <summary>Trouser legs: a pressed crease, side seams, knee wrinkles, a turned-up cuff, ink splashed up from the floor.</summary>
    public static Texture2D Trousers(bool studio, float inkLevel)
    {
        var c = new InkCanvas(512, 512, studio ? StudioTrousers : ToonTrousers, studio ? 202 : 102);
        if (studio) Shade(c, 0.32f);
        else CelShade(c, 0.34f);
        if (studio)
            for (var x = 0; x < c.W; x += 22)
                c.Stroke(x, 0, x, c.H, 0.8f, new Color(0.75f, 0.7f, 0.62f), 0.1f, 0.25f);

        foreach (var u in new[] { 0.25f, 0.75f })
        {
            c.Stroke(c.W * u, 0, c.W * u, c.H * 0.92f, 1.2f, Ink, 0.5f, 0.55f);
            c.Stroke(c.W * u + 3, 0, c.W * u + 3, c.H * 0.92f, 1.2f, new Color(1, 1, 1, 1), 0.5f, 0.15f);
        }

        foreach (var u in new[] { 0f, 0.5f }) c.Stroke(c.W * u, 0, c.W * u, c.H, studio ? 1.6f : 2.6f, Ink, 0.4f, 0.8f);
        for (var i = 0; i < (studio ? 10 : 5); i++)
        {
            var u = c.W * c.Rand(0.12f, 0.38f);
            var v = c.H * c.Rand(0.4f, 0.56f);
            c.Stroke(u, v, u + c.Rand(18, 40), v + c.Rand(-8, 8), studio ? 1.3f : 2.8f, Ink, studio ? 0.8f : 0.5f, studio ? 0.6f : 0.85f);
        }

        // Turned-up cuff at the ankle (v = 1).
        c.Stroke(0, c.H * 0.92f, c.W, c.H * 0.92f, 2.4f, Ink, 0.6f);
        c.Stroke(0, c.H * 0.985f, c.W, c.H * 0.985f, 2.2f, Ink, 0.6f);
        c.Hatch(0, c.H * 0.92f, c.W, c.H * 0.03f, 60, 6, 0.9f, Ink, 0.35f);
        Spatter(c, inkLevel, 0.7f, 1f, 9, 8f);
        return Done(c, studio ? "InkVR_StudioTrousers" : "InkVR_ToonTrousers");
    }

    /// <summary>The seat of the trousers (v from the crotch up to the waistband): fly, pockets, belt loops.</summary>
    public static Texture2D Hips(bool studio)
    {
        var c = new InkCanvas(512, 256, studio ? StudioTrousers : ToonTrousers, studio ? 203 : 103);
        if (studio) Shade(c, 0.32f);
        else CelShade(c, 0.34f);
        if (studio)
            for (var x = 0; x < c.W; x += 22)
                c.Stroke(x, 0, x, c.H, 0.8f, new Color(0.75f, 0.7f, 0.62f), 0.1f, 0.25f);
        var front = c.W * 0.25f;
        c.Stroke(front, c.H * 0.25f, front, c.H, 1.6f, Ink, 0.3f);
        c.Stroke(front + 14, c.H * 0.35f, front + 14, c.H, 1.2f, Ink, 0.3f, 0.7f);
        foreach (var s in new[] { -1f, 1f })
        {
            c.Stroke(front + s * 70, c.H * 0.95f, front + s * 110, c.H * 0.55f, 1.6f, Ink, 0.4f);
            for (var k = 0; k < 3; k++)
            {
                var u = front + s * (50 + k * 75);
                c.Stroke(u, c.H * 0.86f, u, c.H, 3f, Ink, 0.2f, 0.7f);
            }
        }

        c.Stroke(c.W * 0.75f, 0, c.W * 0.75f, c.H, 1.4f, Ink, 0.4f, 0.7f);
        return Done(c, studio ? "InkVR_StudioHips" : "InkVR_ToonHips");
    }

    /// <summary>Shoes (toon: round cartoon toes) or work boots: sole, toe cap, laces, a shine.</summary>
    public static Texture2D Shoe(bool studio, float inkLevel)
    {
        var c = new InkCanvas(256, 128, studio ? StudioBoot : ToonShoe, studio ? 204 : 104);
        var front = c.W * 0.25f;
        c.Stroke(0, c.H * 0.24f, c.W, c.H * 0.24f, 2.4f, Ink, 0.3f);
        c.Stroke(0, c.H * 0.3f, c.W, c.H * 0.3f, 1.0f, new Color(0.6f, 0.45f, 0.3f), 0.3f, 0.7f);
        if (studio) c.Stroke(front - 30, c.H * 0.5f, front + 30, c.H * 0.5f, 1.6f, Ink, 1.2f, 0.8f);
        for (var k = 0; k < (studio ? 6 : 0); k++) // laces on the boots; the cartoon shoes are plain and glossy
        {
            var v = c.H * (0.6f + k * (studio ? 0.06f : 0.1f));
            c.Stroke(front - 12, v, front + 12, v + 6, 1.8f, new Color(0.85f, 0.75f, 0.55f), 0.2f);
            c.Stroke(front + 12, v, front - 12, v + 6, 1.8f, new Color(0.85f, 0.75f, 0.55f), 0.2f);
        }

        if (studio) c.Stroke(c.W * 0.15f, c.H * 0.7f, c.W * 0.2f, c.H * 0.85f, 3f, new Color(0.9f, 0.82f, 0.7f), 0.3f, 0.5f);
        else
        {
            // the cartoon shine: a fat white bean on the toe and a fleck beside it
            c.Stroke(c.W * 0.16f, c.H * 0.66f, c.W * 0.21f, c.H * 0.84f, 9f, new Color(0.97f, 0.95f, 0.9f), 0.2f);
            c.Dot(c.W * 0.235f, c.H * 0.9f, 3.4f, new Color(0.97f, 0.95f, 0.9f));
        }

        Spatter(c, inkLevel, 0.25f, 0.8f, 5, 5f);
        return Done(c, studio ? "InkVR_StudioBoot" : "InkVR_ToonShoe", 0.06f);
    }

    /// <summary>A strap (braces, belt) with stitching along both edges.</summary>
    public static Texture2D Strap(Color color, string name)
    {
        var c = new InkCanvas(64, 256, color, name.Length);
        for (var y = 4; y < c.H; y += 10)
        {
            c.Dot(10, y, 1f, new Color(0.85f, 0.75f, 0.55f), 0.8f);
            c.Dot(c.W - 10, y, 1f, new Color(0.85f, 0.75f, 0.55f), 0.8f);
        }

        c.Stroke(3, 0, 3, c.H, 1.6f, Ink, 0.2f);
        c.Stroke(c.W - 3, 0, c.W - 3, c.H, 1.6f, Ink, 0.2f);
        return Done(c, name);
    }

    /// <summary>A dark tie with diagonal stripes.</summary>
    public static Texture2D Tie()
    {
        var c = new InkCanvas(128, 256, new Color(0.1f, 0.06f, 0.05f), 401);
        for (var k = -8; k < 16; k++) c.Stroke(0, k * 22, c.W, k * 22 + 60, 5f, new Color(0.45f, 0.12f, 0.07f), 0.2f, 0.9f);
        return Done(c, "InkVR_StudioTie");
    }
}
