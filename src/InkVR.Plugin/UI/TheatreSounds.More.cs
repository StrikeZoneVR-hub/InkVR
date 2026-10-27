using System;
using Random = System.Random;

namespace InkVR.UI;

/// <summary>
/// More synthesised theatre sounds for 1.0: footsteps on each kind of floor (soft: they're yours), the ink machine's
/// thump and hiss, switches, a find's chime, and the Fun Hall's dings, buzzers, ticket chatter, pops and fanfares.
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

    public static UnityEngine.AudioClip Chime => Get("chime", MakeChime);
    public static UnityEngine.AudioClip Thump => Get("thump", MakeThump);
    public static UnityEngine.AudioClip Hiss => Get("hiss", MakeHiss);
    public static UnityEngine.AudioClip Click => Get("click", MakeClick);
    public static UnityEngine.AudioClip Ding => Get("ding", MakeDing);
    public static UnityEngine.AudioClip Buzzer => Get("buzzer", MakeBuzzer);
    public static UnityEngine.AudioClip Tickets => Get("tickets", MakeTickets);
    public static UnityEngine.AudioClip Pop => Get("pop", MakePop);
    public static UnityEngine.AudioClip Whack => Get("whack", MakeWhack);
    public static UnityEngine.AudioClip Fanfare => Get("fanfare", MakeFanfare);
    public static UnityEngine.AudioClip Shutter => Get("shutter", MakeShutter);
    public static UnityEngine.AudioClip Whirr => Get("whirr", MakeWhirr);
    public static UnityEngine.AudioClip Boing => Get("boing", MakeBoing);

    private static readonly System.Collections.Generic.Dictionary<string, UnityEngine.AudioSource> Loops = new();

    /// <summary>Keep a sound looping at a place (call every frame; volume 0 lets it go quiet).</summary>
    public static void Loop(string id, UnityEngine.AudioClip clip, UnityEngine.Vector3 at, float volume, float pitch = 1f)
    {
        Loops.TryGetValue(id, out var src);
        if (volume <= 0.001f)
        {
            if (src != null && src.isPlaying) src.Stop();
            return;
        }

        if (src == null)
        {
            var go = new UnityEngine.GameObject("InkVR_TheatreLoop_" + id);
            UnityEngine.Object.DontDestroyOnLoad(go);
            src = go.AddComponent<UnityEngine.AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;
            src.ignoreListenerPause = true;
            if (Sources[0] != null) src.outputAudioMixerGroup = Sources[0].outputAudioMixerGroup;
            Loops[id] = src;
        }

        var rel = at - TheatreWalk.Eye;
        var d = rel.magnitude;
        src.panStereo = UnityEngine.Mathf.Clamp(d > 1e-3f ? UnityEngine.Vector3.Dot(rel / d, TheatreWalk.Right) * 0.8f : 0f, -0.8f, 0.8f);
        src.volume = UnityEngine.Mathf.Clamp01(volume / (1f + d * d / 9f));
        src.pitch = pitch;
        if (src.clip != clip) src.clip = clip;
        if (!src.isPlaying) src.Play();
    }

    public static void StopLoops()
    {
        foreach (var s in Loops.Values)
            if (s != null && s.isPlaying)
                s.Stop();
    }

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

    private static float[] MakeChime()
    {
        // a little rising arpeggio on a celesta-ish tone: something found
        var buf = Buffer(1.8f);
        var notes = new[] { 0, 4, 7, 12, 16 };
        for (var i = 0; i < notes.Length; i++)
        {
            var f = 784f * MathF.Pow(2f, notes[i] / 12f);
            Partial(buf, i * 0.09f, f, 0.6f, 3.5f, i);
            Partial(buf, i * 0.09f, f * 2.01f, 0.18f, 6f, i + 1);
            Partial(buf, i * 0.09f, f * 3.98f, 0.06f, 9f, i + 2);
        }

        Fade(buf, 0.002f, 0.2f);
        return buf;
    }

    private static float[] MakeThump()
    {
        // the ink machine's pump: a heavy piston stroke
        var buf = Buffer(0.55f);
        Partial(buf, 0, 48f, 1f, 9f);
        Partial(buf, 0, 96f, 0.4f, 16f, 1f);
        Partial(buf, 0, 260f, 0.15f, 30f, 2f);
        Noise(buf, 0, 0.25f, 40f, 0.08f, new Random(21));
        Fade(buf, 0.004f, 0.1f);
        return buf;
    }

    private static float[] MakeHiss()
    {
        var buf = Buffer(0.9f);
        var rng = new Random(22);
        var y = 0f;
        for (var i = 0; i < buf.Length; i++)
        {
            var t = i / (float)Rate;
            y += ((float)rng.NextDouble() * 2 - 1 - y) * 0.75f;
            buf[i] = y * MathF.Min(1f, t / 0.04f) * MathF.Exp(-t * 3.2f);
        }

        Fade(buf, 0.002f, 0.15f);
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

    private static float[] MakeBuzzer()
    {
        var buf = Buffer(0.6f);
        for (var i = 0; i < buf.Length; i++)
        {
            var t = i / (float)Rate;
            var saw = (t * 110f % 1f) * 2f - 1f;
            var saw2 = (t * 113f % 1f) * 2f - 1f;
            buf[i] = (saw + saw2) * 0.5f * MathF.Min(1f, t / 0.01f);
        }

        Fade(buf, 0.002f, 0.08f);
        return buf;
    }

    private static float[] MakeTickets()
    {
        // the ticket machine chattering out a strip
        var buf = Buffer(1.0f);
        var rng = new Random(24);
        for (var t = 0f; t < 0.9f; t += 0.045f)
        {
            Noise(buf, t, 0.35f, 220f, 0.9f, rng);
            Partial(buf, t, 1800f + (float)rng.NextDouble() * 400f, 0.12f, 200f);
        }

        Fade(buf, 0.002f, 0.05f);
        return buf;
    }

    private static float[] MakePop()
    {
        // a cork gun
        var buf = Buffer(0.3f);
        Noise(buf, 0, 1f, 160f, 0.95f, new Random(25));
        Partial(buf, 0, 420f, 0.5f, 60f);
        Partial(buf, 0, 210f, 0.4f, 40f, 1f);
        Fade(buf, 0.0005f, 0.05f);
        return buf;
    }

    private static float[] MakeWhack()
    {
        // a mallet on a rubber head
        var buf = Buffer(0.35f);
        Partial(buf, 0, 150f, 1f, 30f);
        Partial(buf, 0, 320f, 0.5f, 45f, 1f);
        Noise(buf, 0, 0.6f, 120f, 0.5f, new Random(26));
        Fade(buf, 0.001f, 0.05f);
        return buf;
    }

    private static float[] MakeFanfare()
    {
        // a short brassy flourish (stacked saw-ish partials), for a prize or a high score
        var buf = Buffer(1.6f);
        var notes = new[] { (0f, 0), (0.14f, 4), (0.28f, 7), (0.42f, 12), (0.62f, 12) };
        foreach (var (at, semis) in notes)
        {
            var f = 392f * MathF.Pow(2f, semis / 12f);
            for (var h = 1; h <= 6; h++) Partial(buf, at, f * h, 0.5f / h, at > 0.6f ? 2.2f : 7f, h);
        }

        Fade(buf, 0.003f, 0.25f);
        return buf;
    }

    private static float[] MakeShutter()
    {
        var buf = Buffer(0.3f);
        var rng = new Random(27);
        Noise(buf, 0, 0.7f, 260f, 0.9f, rng);
        Noise(buf, 0.09f, 0.5f, 220f, 0.9f, rng);
        Partial(buf, 0, 900f, 0.2f, 80f);
        Fade(buf, 0.001f, 0.04f);
        return buf;
    }

    private static float[] MakeWhirr()
    {
        // a projector running: the claw's flutter over a motor hum (loops cleanly at 1 s)
        var buf = Buffer(1.0f);
        for (var i = 0; i < buf.Length; i++)
        {
            var t = i / (float)Rate;
            var flutter = MathF.Sin(t * 2 * MathF.PI * 24f);
            buf[i] = 0.35f * MathF.Sin(t * 2 * MathF.PI * 60f) + 0.25f * MathF.Sin(t * 2 * MathF.PI * 120f) + 0.3f * (flutter > 0.6f ? 1f : 0f) * MathF.Sin(t * 2 * MathF.PI * 1300f);
        }

        return buf;
    }

    private static float[] MakeBoing()
    {
        var buf = Buffer(0.7f);
        double phase = 0;
        for (var i = 0; i < buf.Length; i++)
        {
            var t = i / (float)Rate;
            var f = 180f + 140f * MathF.Exp(-t * 6f) * MathF.Sin(t * 2 * MathF.PI * 9f);
            phase += 2 * Math.PI * f / Rate;
            buf[i] = (float)Math.Sin(phase) * MathF.Exp(-t * 4f);
        }

        Fade(buf, 0.002f, 0.08f);
        return buf;
    }
}
