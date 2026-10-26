using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace InkVR.UI;

/// <summary>
/// The theatre's own sounds, synthesised at startup (nothing recorded, nothing shipped): piano notes, the desk
/// bell, knocking, a door rattling and creaking, tin cans, a squeaky doll, ink seeping. Played flat (not 3D, the
/// game's listener isn't in the theatre) but panned and faded by where they are relative to your head.
/// </summary>
public static class TheatreSounds
{
    private const int Rate = 22050;
    private static readonly AudioSource[] Sources = new AudioSource[8];
    private static int _next;
    private static bool _lookedForMixer;
    private static readonly Dictionary<string, AudioClip> Clips = new();
    private static readonly AudioClip[] Notes = new AudioClip[88];

    public static AudioClip Bell => Get("bell", MakeBell);
    public static AudioClip Knock => Get("knock", MakeKnock);
    public static AudioClip Rattle => Get("rattle", MakeRattle);
    public static AudioClip Clank => Get("clank", MakeClank);
    public static AudioClip Squeak => Get("squeak", MakeSqueak);
    public static AudioClip Creak => Get("creak", MakeCreak);
    public static AudioClip Thud => Get("thud", MakeThud);
    public static AudioClip Seep => Get("seep", MakeSeep);

    private static readonly float[][] NoteData = new float[88][];
    private static System.Threading.Tasks.Task _prewarm;

    /// <summary>Piano key n (0 = A0 ... 87 = C8).</summary>
    public static AudioClip Note(int n)
    {
        n = Mathf.Clamp(n, 0, 87);
        if (Notes[n] != null) return Notes[n];
        var data = System.Threading.Volatile.Read(ref NoteData[n]) ?? MakeNote(n);
        return Notes[n] = ToClip("InkVR_Theatre_Note" + n, (float[])data.Clone());
    }

    /// <summary>Work out every piano note in the background, so the first press of a key doesn't hitch.</summary>
    public static void Prewarm()
    {
        if (_prewarm != null) return;
        _prewarm = System.Threading.Tasks.Task.Run(() =>
        {
            for (var n = 0; n < 88; n++) System.Threading.Volatile.Write(ref NoteData[n], MakeNote(n));
        });
    }

    /// <summary>Play a sound at a theatre position, heard from your head there.</summary>
    public static void Play(AudioClip clip, Vector3 at, float volume, float pitch = 1f)
    {
        if (clip == null) return;
        var src = Source();
        if (src == null) return;
        var rel = at - TheatreWalk.Eye;
        var d = rel.magnitude;
        var pan = d > 1e-3f ? Vector3.Dot(rel / d, TheatreWalk.Right) : 0f;
        src.panStereo = Mathf.Clamp(pan * 0.85f, -0.85f, 0.85f);
        src.pitch = pitch;
        src.volume = Mathf.Clamp01(volume / (1f + d * d / 9f)) * 0.9f;
        src.clip = clip;
        src.Play();
    }

    private static AudioSource Source()
    {
        if (Sources[0] == null)
        {
            var go = new GameObject("InkVR_TheatreAudio");
            Object.DontDestroyOnLoad(go);
            for (var i = 0; i < Sources.Length; i++)
            {
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.ignoreListenerPause = true; // the pause menu's theatre still has its sounds
                Sources[i] = s;
            }
        }

        if (!_lookedForMixer)
        {
            // through the game's sound-effects volume, if a source in the scene shows us where that is
            _lookedForMixer = true;
            try
            {
                foreach (var o in Object.FindObjectsOfType<AudioSource>())
                {
                    var g = o.outputAudioMixerGroup;
                    if (g == null) continue;
                    var name = g.name.ToLowerInvariant();
                    if (!name.Contains("sfx") && !name.Contains("effect") && !name.Contains("sound")) continue;
                    foreach (var s in Sources) s.outputAudioMixerGroup = g;
                    Plugin.L.LogInfo("Theatre sounds through the game's '" + g.name + "' mixer group");
                    break;
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Theatre sounds: no mixer group (" + e.Message + ")");
            }
        }

        // the quietest free source, else the oldest
        for (var k = 0; k < Sources.Length; k++)
        {
            var s = Sources[(_next + k) % Sources.Length];
            if (s.isPlaying) continue;
            _next = (_next + k + 1) % Sources.Length;
            return s;
        }

        var oldest = Sources[_next];
        _next = (_next + 1) % Sources.Length;
        return oldest;
    }

    /// <summary>Forget the mixer (a scene change may have replaced it).</summary>
    public static void SceneChanged() => _lookedForMixer = false;

    private static AudioClip Get(string name, Func<float[]> make)
    {
        if (Clips.TryGetValue(name, out var c) && c != null) return c;
        c = ToClip("InkVR_Theatre_" + name, make());
        Clips[name] = c;
        return c;
    }

    private static AudioClip ToClip(string name, float[] data)
    {
        var peak = 1e-4f;
        foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
        var k = 0.85f / peak;
        for (var i = 0; i < data.Length; i++) data[i] *= k;
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(clip);
        return clip;
    }

    private static float[] Buffer(float seconds) => new float[(int)MathF.Ceiling(seconds * Rate)];

    private static void Partial(float[] buf, float start, float freq, float amp, float decay, float phase = 0f)
    {
        // a decaying sine by recurrence (a rotating phasor and a shrinking envelope: no trig per sample)
        var s0 = (int)(start * Rate);
        var w = 2 * Math.PI * freq / Rate;
        double c = Math.Cos(w), sn = Math.Sin(w), x = Math.Cos(phase), y = Math.Sin(phase), env = amp, m = Math.Exp(-decay / Rate);
        for (var i = s0; i < buf.Length; i++)
        {
            if (env < 1e-4) break;
            buf[i] += (float)(env * y);
            var nx = x * c - y * sn;
            y = x * sn + y * c;
            x = nx;
            env *= m;
        }
    }

    private static void Noise(float[] buf, float start, float amp, float decay, float smooth, Random rng)
    {
        var s0 = (int)(start * Rate);
        var y = 0f;
        for (var i = s0; i < buf.Length; i++)
        {
            var t = (i - s0) / (float)Rate;
            var env = amp * MathF.Exp(-t * decay);
            if (env < 1e-4f) break;
            y += ((float)rng.NextDouble() * 2 - 1 - y) * smooth;
            buf[i] += env * y;
        }
    }

    // ---------------- instruments ----------------

    private static float[] MakeNote(int n)
    {
        var f = 27.5f * MathF.Pow(2f, n / 12f);
        var len = 2.8f + (1.1f - 2.8f) * (n / 87f);
        var buf = Buffer(len);
        var inharm = 0.00012f * (1f + n / 30f);
        for (var h = 1; h <= 12; h++)
        {
            var fh = f * h * MathF.Sqrt(1 + inharm * h * h);
            if (fh > Rate * 0.45f) break;
            var amp = 1f / MathF.Pow(h, 1.15f) * (h == 1 ? 1f : 0.85f) * (n < 20 && h == 1 ? 0.6f : 1f);
            var decay = 0.9f + 0.45f * h + f / 500f;
            // two strings a hair apart, a fast fall then a long ring
            Partial(buf, 0, fh, amp * 0.65f, decay * 2.4f, h);
            Partial(buf, 0, fh * 1.0007f, amp * 0.35f, decay * 0.55f, h * 2f);
        }

        Noise(buf, 0, 0.12f, 180f, 0.35f, new Random(n)); // the hammer
        Fade(buf, 0.003f, 0.08f);
        return buf;
    }

    private static float[] MakeBell()
    {
        var buf = Buffer(2.6f);
        var f = 2150f;
        var ratios = new[] { 1f, 2.32f, 4.25f, 6.63f, 9.1f };
        var amps = new[] { 1f, 0.55f, 0.28f, 0.14f, 0.07f };
        var decays = new[] { 1.1f, 1.7f, 2.8f, 4.2f, 6f };
        for (var i = 0; i < ratios.Length; i++)
        {
            Partial(buf, 0, f * ratios[i], amps[i], decays[i]);
            Partial(buf, 0, f * ratios[i] * 1.0025f, amps[i] * 0.4f, decays[i] * 1.1f, 1f); // a slow shimmer
        }

        Noise(buf, 0, 0.25f, 400f, 0.6f, new Random(7));
        Fade(buf, 0.001f, 0.1f);
        return buf;
    }

    private static float[] MakeKnock()
    {
        var buf = Buffer(1.3f);
        var rng = new Random(11);
        foreach (var t in new[] { 0f, 0.29f, 0.62f })
        {
            Partial(buf, t, 92f, 1f, 26f);
            Partial(buf, t, 178f, 0.55f, 38f, 1f);
            Partial(buf, t, 410f, 0.2f, 60f, 2f);
            Noise(buf, t, 0.35f, 110f, 0.2f, rng);
        }

        Fade(buf, 0.002f, 0.05f);
        return buf;
    }

    private static float[] MakeRattle()
    {
        var buf = Buffer(0.6f);
        var rng = new Random(12);
        var t = 0f;
        for (var k = 0; k < 8; k++)
        {
            var a = 0.5f + 0.5f * (float)rng.NextDouble();
            Noise(buf, t, a * 0.6f, 260f, 0.7f, rng);
            Partial(buf, t, 1250f + (float)rng.NextDouble() * 300f, a * 0.3f, 90f);
            Partial(buf, t, 140f, a * 0.4f, 50f);
            t += 0.035f + (float)rng.NextDouble() * 0.04f;
        }

        Fade(buf, 0.001f, 0.05f);
        return buf;
    }

    private static float[] MakeClank()
    {
        var buf = Buffer(0.5f);
        var freqs = new[] { 640f, 1610f, 2700f, 3950f, 5200f };
        var amps = new[] { 1f, 0.75f, 0.5f, 0.32f, 0.2f };
        for (var i = 0; i < freqs.Length; i++) Partial(buf, 0, freqs[i], amps[i], 16f + i * 7f, i);
        Noise(buf, 0, 0.5f, 220f, 0.8f, new Random(13));
        Fade(buf, 0.001f, 0.05f);
        return buf;
    }

    private static float[] MakeSqueak()
    {
        var buf = Buffer(0.42f);
        double phase = 0;
        for (var i = 0; i < buf.Length; i++)
        {
            var t = i / (float)Rate;
            var f = 950f + 520f * MathF.Sin(MathF.PI * t / 0.42f) + 25f * MathF.Sin(t * 2 * MathF.PI * 22f);
            phase += 2 * Math.PI * f / Rate;
            var env = Math.Clamp(t / 0.03f, 0f, 1f) * Math.Clamp((0.42f - t) / 0.08f, 0f, 1f);
            buf[i] = env * (float)(Math.Sin(phase) + 0.35 * Math.Sin(2 * phase) + 0.15 * Math.Sin(3 * phase));
        }

        return buf;
    }

    private static float[] MakeCreak()
    {
        // stick-slip: a run of little ticks, each ringing the wood, at a wavering rate
        var buf = Buffer(1.0f);
        var rng = new Random(14);
        var t = 0f;
        while (t < 0.9f)
        {
            var rate = 55f + 30f * MathF.Sin(t * 7f) + (float)rng.NextDouble() * 12f;
            var a = MathF.Sin(MathF.PI * t / 0.9f) * (0.6f + 0.4f * (float)rng.NextDouble());
            Partial(buf, t, 520f, a * 0.6f, 160f);
            Partial(buf, t, 1340f, a * 0.35f, 220f, 1f);
            Partial(buf, t, 210f, a * 0.3f, 90f, 2f);
            t += 1f / rate;
        }

        Fade(buf, 0.005f, 0.08f);
        return buf;
    }

    private static float[] MakeThud()
    {
        var buf = Buffer(0.6f);
        Partial(buf, 0, 68f, 1f, 18f);
        Partial(buf, 0, 130f, 0.45f, 28f, 1f);
        Noise(buf, 0, 0.4f, 70f, 0.12f, new Random(15));
        Fade(buf, 0.002f, 0.08f);
        return buf;
    }

    private static float[] MakeSeep()
    {
        var buf = Buffer(3.2f);
        var rng = new Random(16);
        var y = 0f;
        for (var i = 0; i < buf.Length; i++)
        {
            var t = i / (float)Rate;
            y += ((float)rng.NextDouble() * 2 - 1 - y) * 0.04f;
            buf[i] = y * MathF.Sin(MathF.PI * t / 3.2f) * 0.8f;
        }

        for (var k = 0; k < 18; k++)
        {
            var t = (float)rng.NextDouble() * 2.9f;
            Partial(buf, t, 160f + (float)rng.NextDouble() * 260f, 0.25f, 30f);
        }

        Fade(buf, 0.05f, 0.3f);
        return buf;
    }

    private static void Fade(float[] buf, float inSec, float outSec)
    {
        var a = (int)(inSec * Rate);
        var b = (int)(outSec * Rate);
        for (var i = 0; i < a && i < buf.Length; i++) buf[i] *= i / (float)a;
        for (var i = 0; i < b && i < buf.Length; i++) buf[buf.Length - 1 - i] *= i / (float)b;
    }
}
