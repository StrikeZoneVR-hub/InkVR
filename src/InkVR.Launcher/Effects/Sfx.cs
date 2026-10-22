using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using InkVR.Launcher.Game;

namespace InkVR.Launcher.Effects
{
    /// <summary>
    /// The launcher's sounds, synthesised at start-up (projector clatter, leader beep, rubber-stamp thump,
    /// ink bloop, iris whoosh). All original; written as small WAVs to %LOCALAPPDATA%\InkVR\sfx.
    /// </summary>
    public static class Sfx
    {
        private const int Rate = 44100;
        private static readonly Dictionary<string, Uri> Files = new Dictionary<string, Uri>();
        private static readonly List<MediaPlayer> Playing = new List<MediaPlayer>();
        private static MediaPlayer _loop;
        private static readonly Random Rng = new Random(7);

        public static bool Muted;
        public static double Master = 1.0;

        public static void Prepare()
        {
            var dir = Path.Combine(LauncherPaths.AppData, "sfx");
            Directory.CreateDirectory(dir);
            void Make(string name, float[] samples)
            {
                var path = Path.Combine(dir, name + ".wav");
                WriteWav(path, samples);
                Files[name] = new Uri(path);
            }

            Make("projector", Projector());
            Make("beep", Beep());
            Make("stamp", Stamp());
            Make("bloop", Bloop());
            Make("hover", Hover());
            Make("whoosh", Whoosh());
            Make("paper", Paper());
            Make("runout", Runout());
        }

        public static void Play(string name, double volume = 1.0)
        {
            if (Muted || !Files.TryGetValue(name, out var uri)) return;
            var p = new MediaPlayer { Volume = volume * Master };
            p.MediaEnded += (s, e) =>
            {
                p.Close();
                Playing.Remove(p);
            };
            p.Open(uri);
            p.Play();
            Playing.Add(p);
        }

        public static void StartLoop(string name, double volume)
        {
            StopLoop();
            if (Muted || !Files.TryGetValue(name, out var uri)) return;
            _loop = new MediaPlayer { Volume = volume * Master };
            _loop.MediaEnded += (s, e) =>
            {
                _loop.Position = TimeSpan.Zero;
                _loop.Play();
            };
            _loop.Open(uri);
            _loop.Play();
        }

        public static void LoopVolume(double v)
        {
            if (_loop != null) _loop.Volume = v * Master;
        }

        public static void StopLoop()
        {
            _loop?.Stop();
            _loop?.Close();
            _loop = null;
        }

        // ---- synthesis ----

        private static float Noise() => (float)(Rng.NextDouble() * 2 - 1);

        private static float[] Projector()
        {
            // 2 s loop: motor hum + shutter clicks at 24 fps (whole cycles, so it loops seamlessly).
            var n = Rate * 2;
            var s = new float[n];
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / Rate;
                s[i] = (float)(0.05 * Math.Sin(2 * Math.PI * 60 * t) + 0.025 * Math.Sin(2 * Math.PI * 120 * t) + 0.012 * Math.Sin(2 * Math.PI * 180 * t));
            }

            var lp = 0f;
            for (var k = 0; k < 48; k++)
            {
                var start = k * n / 48;
                var amp = k % 2 == 0 ? 0.55f : 0.35f;
                for (var j = 0; j < Rate / 90 && start + j < n; j++)
                {
                    var env = (float)Math.Exp(-j / (Rate * 0.0025));
                    lp += (Noise() - lp) * 0.35f;
                    s[start + j] += lp * amp * env;
                }

                // the claw's softer return stroke
                var mid = start + n / 96;
                for (var j = 0; j < Rate / 200 && mid + j < n; j++)
                    s[mid + j] += Noise() * 0.08f * (float)Math.Exp(-j / (Rate * 0.002));
            }

            return s;
        }

        private static float[] Beep()
        {
            var n = (int)(Rate * 0.11);
            var s = new float[n];
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / Rate;
                var env = Math.Min(1, i / (Rate * 0.004)) * Math.Min(1, (n - i) / (Rate * 0.01));
                s[i] = (float)(0.35 * env * Math.Sin(2 * Math.PI * 1000 * t));
            }

            return s;
        }

        private static float[] Stamp()
        {
            var n = (int)(Rate * 0.45);
            var s = new float[n];
            double phase = 0;
            var lp = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / Rate;
                var f = 45 + 110 * Math.Exp(-t * 18);
                phase += 2 * Math.PI * f / Rate;
                var body = Math.Sin(phase) * Math.Exp(-t * 9) * 0.9;
                lp += (Noise() - lp) * 0.5f;
                var slap = lp * Math.Exp(-t * 90) * 0.7;
                s[i] = (float)(body + slap);
            }

            return s;
        }

        private static float[] Bloop()
        {
            var n = (int)(Rate * 0.16);
            var s = new float[n];
            double phase = 0;
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / Rate;
                var f = 260 + 700 * Math.Exp(-t * 30);
                phase += 2 * Math.PI * f / Rate;
                var env = Math.Min(1, t / 0.004) * Math.Exp(-t * 22);
                s[i] = (float)(0.5 * env * Math.Sin(phase));
            }

            return s;
        }

        private static float[] Hover()
        {
            var n = (int)(Rate * 0.05);
            var s = new float[n];
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / Rate;
                s[i] = (float)((Noise() * 0.25 * Math.Exp(-t * 900)) + 0.12 * Math.Sin(2 * Math.PI * 1800 * t) * Math.Exp(-t * 120));
            }

            return s;
        }

        private static float[] Whoosh()
        {
            var n = (int)(Rate * 1.0);
            var s = new float[n];
            float lp1 = 0, lp2 = 0;
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / n;
                var cutoff = (float)(0.25 * Math.Pow(1 - t, 2) + 0.01);
                lp1 += (Noise() - lp1) * cutoff;
                lp2 += (lp1 - lp2) * cutoff;
                var env = Math.Sin(Math.PI * Math.Pow(t, 0.6));
                s[i] = (float)(lp2 * env * 2.2);
            }

            return s;
        }

        private static float[] Paper()
        {
            var n = (int)(Rate * 0.3);
            var s = new float[n];
            float hp = 0, prev = 0;
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / n;
                var x = Noise();
                hp = 0.92f * (hp + x - prev);
                prev = x;
                var env = Math.Sin(Math.PI * t) * (0.6 + 0.4 * Math.Sin(t * 40));
                s[i] = (float)(hp * env * 0.18);
            }

            return s;
        }

        private static float[] Runout()
        {
            // The film's tail slapping the reel: clicks that slow down.
            var n = (int)(Rate * 1.6);
            var s = new float[n];
            var pos = 0.0;
            var gap = 0.022;
            while (pos < 1.5)
            {
                var start = (int)(pos * Rate);
                for (var j = 0; j < Rate / 120 && start + j < n; j++)
                    s[start + j] += Noise() * 0.5f * (float)Math.Exp(-j / (Rate * 0.002)) * (float)(1 - pos / 1.6);
                pos += gap;
                gap *= 1.09;
            }

            return s;
        }

        private static void WriteWav(string path, float[] samples)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                var bytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(Rate);
                w.Write(Rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(bytes);
                foreach (var f in samples) w.Write((short)(Math.Max(-1, Math.Min(1, f)) * 30000));
            }
        }
    }
}
