using System;
using Random = System.Random;

namespace InkVR.UI;

/// <summary>
/// Your footsteps in the theatre, soft (they're yours), on each kind of floor; and the mirror's buttons.
/// </summary>
public static partial class TheatreSounds
{
    public enum Floor
    {
        Wood,
        Carpet,
        Concrete,
        Tile
    }

    private const int StepVariants = 4;

    /// <summary>A footstep on this floor (one of a few takes, so a walk doesn't sound like a loop).</summary>
    public static UnityEngine.AudioClip Step(Floor floor, int take) => Get("step" + (int)floor + "_" + take % StepVariants, () => MakeStep(floor, take % StepVariants));

    public static UnityEngine.AudioClip Click => Get("click", MakeClick);
    public static UnityEngine.AudioClip Ding => Get("ding", MakeDing);

    private static float[] MakeStep(Floor floor, int take)
    {
        var rng = new Random(400 + (int)floor * 10 + take);
        var pitch = 0.92f + (float)rng.NextDouble() * 0.16f;
        float[] buf;
        switch (floor)
        {
            case Floor.Carpet:
                // a soft pad: muffled, almost all low end
                buf = Buffer(0.22f);
                Noise(buf, 0, 0.5f, 30f, 0.06f, rng);
                Partial(buf, 0, 75f * pitch, 0.35f, 40f);
                break;
            case Floor.Concrete:
                // heel and a little grit
                buf = Buffer(0.25f);
                Partial(buf, 0, 95f * pitch, 0.6f, 45f);
                Noise(buf, 0, 0.4f, 60f, 0.55f, rng);
                Noise(buf, 0.03f, 0.2f, 35f, 0.8f, rng); // the scuff
                break;
            case Floor.Tile:
                buf = Buffer(0.2f);
                Partial(buf, 0, 120f * pitch, 0.5f, 50f);
                Partial(buf, 0, 1150f * pitch, 0.25f, 90f, 1f);
                Noise(buf, 0, 0.35f, 140f, 0.85f, rng);
                break;
            default:
                // boards: a low knock with the board's ring, a heel then the toe
                buf = Buffer(0.3f);
                Partial(buf, 0, 105f * pitch, 0.7f, 32f);
                Partial(buf, 0, 360f * pitch, 0.3f, 55f, 1f);
                Noise(buf, 0, 0.3f, 90f, 0.35f, rng);
                Partial(buf, 0.045f, 140f * pitch, 0.25f, 45f, 2f);
                if (rng.NextDouble() < 0.5) Partial(buf, 0.06f, 610f * pitch, 0.08f, 25f, 3f); // a creak in the board
                break;
        }

        Fade(buf, 0.002f, 0.04f);
        return buf;
    }

    private static float[] MakeClick()
    {
        // a knife switch thrown: a snap and a thunk
        var buf = Buffer(0.25f);
        Noise(buf, 0, 0.6f, 300f, 0.9f, new Random(23));
        Partial(buf, 0, 2200f, 0.3f, 120f);
        Partial(buf, 0.008f, 160f, 0.5f, 40f, 1f);
        Fade(buf, 0.001f, 0.05f);
        return buf;
    }

    private static float[] MakeDing()
    {
        var buf = Buffer(1.2f);
        Partial(buf, 0, 1568f, 0.8f, 4f);
        Partial(buf, 0, 1568f * 2.76f, 0.3f, 7f, 1f);
        Partial(buf, 0, 1568f * 5.4f, 0.1f, 12f, 2f);
        Fade(buf, 0.001f, 0.1f);
        return buf;
    }

}
