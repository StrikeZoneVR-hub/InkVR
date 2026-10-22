using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InkVR.Launcher.Effects
{
    /// <summary>Procedural textures: film grain, vignette, rubber-stamp wear, and a sepia stand-in background.</summary>
    public static class Paint
    {
        public static readonly Color Ink = Color.FromRgb(0x14, 0x0D, 0x09);
        public static readonly Color Paper = Color.FromRgb(0xF0, 0xD6, 0x96);
        public static readonly Color Sepia = Color.FromRgb(0xC9, 0x9A, 0x4E);
        public static readonly Color StampRed = Color.FromRgb(0x9A, 0x1E, 0x14);
        public static readonly Color OkGreen = Color.FromRgb(0x2E, 0x5B, 0x1F);
        public static readonly Color WarnAmber = Color.FromRgb(0xA8, 0x6A, 0x05);

        public static SolidColorBrush Brush(Color c, double opacity = 1)
        {
            var b = new SolidColorBrush(c) { Opacity = opacity };
            b.Freeze();
            return b;
        }

        /// <summary>Film grain frame: speckles of dark and light dust over transparency.</summary>
        public static BitmapSource Grain(int size, int seed)
        {
            var rng = new Random(seed);
            var px = new byte[size * size * 4];
            for (var i = 0; i < size * size; i++)
            {
                var r = rng.NextDouble();
                byte a, v;
                if (r < 0.006) { v = 10; a = (byte)rng.Next(120, 230); }        // dust
                else if (r < 0.010) { v = 250; a = (byte)rng.Next(60, 140); }  // bright fleck
                else { v = (byte)(rng.NextDouble() < 0.5 ? 0 : 255); a = (byte)rng.Next(0, 34); }
                px[i * 4] = v;
                px[i * 4 + 1] = v;
                px[i * 4 + 2] = v;
                px[i * 4 + 3] = a;
            }

            // a few hairs
            for (var h = 0; h < 2; h++)
            {
                double x = rng.Next(size), y = rng.Next(size), ang = rng.NextDouble() * Math.PI * 2;
                var len = rng.Next(20, 60);
                for (var k = 0; k < len; k++)
                {
                    ang += (rng.NextDouble() - 0.5) * 0.5;
                    x += Math.Cos(ang);
                    y += Math.Sin(ang);
                    var ix = ((int)x % size + size) % size;
                    var iy = ((int)y % size + size) % size;
                    var o = (iy * size + ix) * 4;
                    px[o] = px[o + 1] = px[o + 2] = 12;
                    px[o + 3] = 200;
                }
            }

            var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, px, size * 4);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>Uneven ink coverage for the rubber-stamped "VR" (used as an opacity mask).</summary>
        public static BitmapSource StampWear(int w, int h, int seed)
        {
            var rng = new Random(seed);
            var px = new byte[w * h * 4];
            var blobs = new (double x, double y, double r)[40];
            for (var i = 0; i < blobs.Length; i++) blobs[i] = (rng.NextDouble() * w, rng.NextDouble() * h, 3 + rng.NextDouble() * 14);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var a = 0.88 + rng.NextDouble() * 0.12;
                foreach (var b in blobs)
                {
                    var d = Math.Sqrt((x - b.x) * (x - b.x) + (y - b.y) * (y - b.y));
                    if (d < b.r) a *= 0.25 + 0.75 * d / b.r;
                }

                if (rng.NextDouble() < 0.04) a *= 0.2;
                var o = (y * w + x) * 4;
                px[o] = px[o + 1] = px[o + 2] = 255;
                px[o + 3] = (byte)(Math.Max(0, Math.Min(1, a)) * 255);
            }

            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            bmp.Freeze();
            return bmp;
        }

        public static Brush Vignette()
        {
            var b = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.48),
                Center = new Point(0.5, 0.48),
                RadiusX = 0.75,
                RadiusY = 0.78,
            };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(110, 10, 5, 0), 0.85));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(220, 0, 0, 0), 1.0));
            b.Freeze();
            return b;
        }

        /// <summary>Stand-in for the game's menu background when the game can't be read.</summary>
        public static Brush SepiaStage()
        {
            var b = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.25), Center = new Point(0.5, 0.35), RadiusX = 0.8, RadiusY = 0.9 };
            b.GradientStops.Add(new GradientStop(Color.FromRgb(0xE2, 0xB8, 0x6A), 0));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(0xA8, 0x74, 0x30), 0.6));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x22, 0x0C), 1));
            b.Freeze();
            return b;
        }
    }

    public static class Ease
    {
        public static double Linear(double t) => t;
        public static double OutCubic(double t) => 1 - Math.Pow(1 - t, 3);
        public static double InCubic(double t) => t * t * t;
        public static double InOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        public static double InQuad(double t) => t * t;

        public static double OutBack(double t)
        {
            const double c1 = 1.70158, c3 = c1 + 1;
            return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
        }

        public static double OutElastic(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            return Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * (2 * Math.PI / 3)) + 1;
        }

        public static double OutBounce(double t)
        {
            const double n1 = 7.5625, d1 = 2.75;
            if (t < 1 / d1) return n1 * t * t;
            if (t < 2 / d1) return n1 * (t -= 1.5 / d1) * t + 0.75;
            if (t < 2.5 / d1) return n1 * (t -= 2.25 / d1) * t + 0.9375;
            return n1 * (t -= 2.625 / d1) * t + 0.984375;
        }

        public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
