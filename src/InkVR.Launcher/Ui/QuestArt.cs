using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using InkVR.Launcher.Effects;

namespace InkVR.Launcher.Ui
{
    public enum QuestPart { Trigger, Grip, Stick, Primary, Secondary, Menu, Meta }

    /// <summary>
    /// A Meta Quest 3 Touch Plus controller, drawn in the look of the game's own controls card:
    /// painted gold body, heavy ink outline, dark ink buttons with gold lettering. Seen from above,
    /// trigger at the top, handle toward you. Original artwork for InkVR.
    /// </summary>
    public static class QuestArt
    {
        public const double W = 340, H = 520;

        private static readonly Color Gold = Color.FromRgb(0xE6, 0xB0, 0x48);
        private static readonly Color GoldDeep = Color.FromRgb(0xBE, 0x84, 0x22);
        private static readonly Color GoldShade = Color.FromRgb(0x96, 0x62, 0x14);
        private static readonly Color InkBtn = Color.FromRgb(0x24, 0x17, 0x0D);
        private static readonly Color Letter = Color.FromRgb(0xF2, 0xC2, 0x5A);

        // Right controller as you see it in your hand (from above, trigger at the far edge): round face with the
        // stick on the outer side, B and A toward the inside, Meta button lower-outer; grip paddle on the inner
        // side of the handle. The left controller is the mirror image (stick outer, Y/X inner, menu lower-outer).
        private const string Disc =
            "M 170,32 C 252,32 310,82 310,150 C 310,218 252,268 170,268 C 88,268 30,218 30,150 C 30,82 88,32 170,32 Z";
        private const string Face =
            "M 170,52 C 240,52 290,94 290,150 C 290,206 240,248 170,248 C 100,248 50,206 50,150 C 50,94 100,52 170,52 Z";
        private const string Handle =
            "M 112,236 C 116,300 120,400 138,468 C 146,500 196,508 210,482 C 226,440 224,330 230,236 Z";
        private const string TriggerShape = "M 118,48 C 124,6 222,2 236,44 Z";
        private const string GripShape = "M 116,282 C 92,292 86,350 102,372 L 122,366 C 118,340 117,304 122,286 Z";
        private const string Shade = "M 230,236 C 224,330 226,440 210,482 C 200,500 176,506 162,500 C 196,470 204,380 206,290 Z";

        private static readonly Dictionary<QuestPart, Point> RightAnchors = new Dictionary<QuestPart, Point>
        {
            { QuestPart.Trigger, new Point(178, 22) },
            { QuestPart.Grip, new Point(100, 326) },
            { QuestPart.Stick, new Point(222, 112) },
            { QuestPart.Secondary, new Point(132, 136) },
            { QuestPart.Primary, new Point(176, 178) },
            { QuestPart.Meta, new Point(250, 186) },
        };

        // Leader-line routes (right controller, local coordinates) from just outside the controller's outer side
        // to each part, chosen so no line passes over another button: inner buttons are reached from above/below.
        private static readonly Dictionary<QuestPart, Point[]> RightRoutes = new Dictionary<QuestPart, Point[]>
        {
            { QuestPart.Trigger, new[] { new Point(W + 12, 22), new Point(178, 22) } },
            { QuestPart.Secondary, new[] { new Point(W + 12, 56), new Point(132, 56), new Point(132, 136) } },
            { QuestPart.Stick, new[] { new Point(W + 12, 112), new Point(222, 112) } },
            { QuestPart.Meta, new[] { new Point(W + 12, 186), new Point(250, 186) } },
            { QuestPart.Primary, new[] { new Point(W + 12, 232), new Point(176, 232), new Point(176, 178) } },
            { QuestPart.Grip, new[] { new Point(W + 12, 330), new Point(100, 326) } },
        };

        public static Point[] Route(bool left, QuestPart part)
        {
            if (part == QuestPart.Menu) part = QuestPart.Meta;
            var r = RightRoutes[part];
            var o = new Point[r.Length];
            for (var i = 0; i < r.Length; i++) o[i] = left ? new Point(W - r[i].X, r[i].Y) : r[i];
            return o;
        }

        public static Point Anchor(bool left, QuestPart part)
        {
            if (part == QuestPart.Menu) part = QuestPart.Meta; // the left's menu button sits where the right's Meta button is
            var p = RightAnchors[part];
            return left ? new Point(W - p.X, p.Y) : p;
        }

        public static Canvas Draw(bool left)
        {
            var c = new Canvas { Width = W, Height = H };
            var mirror = left ? new ScaleTransform(-1, 1, W / 2, 0) : null;

            Path Shape(string data, Brush fill, double stroke, double opacity = 1)
            {
                var g = Geometry.Parse(data);
                if (mirror != null)
                {
                    g = g.Clone();
                    g.Transform = mirror;
                }

                var p = new Path
                {
                    Data = g, Fill = fill, Stroke = stroke > 0 ? Paint.Brush(Paint.Ink) : null, StrokeThickness = stroke,
                    StrokeLineJoin = PenLineJoin.Round, Opacity = opacity,
                };
                c.Children.Add(p);
                return p;
            }

            // soft ink shadow on the paper
            foreach (var part in new[] { Handle, Disc })
            {
                var shadow = Shape(part, Paint.Brush(Paint.Ink, 0.25), 0);
                shadow.RenderTransform = new TranslateTransform(12, 14);
                shadow.Effect = new BlurEffect { Radius = 14 };
            }

            var bodyFill = new LinearGradientBrush(Gold, GoldDeep, new Point(0.3, 0), new Point(0.7, 1));
            bodyFill.Freeze();
            Shape(TriggerShape, Paint.Brush(GoldShade), 6);
            Shape(Handle, bodyFill, 7);
            Shape(Shade, Paint.Brush(GoldShade, 0.45), 0);
            Shape(GripShape, Paint.Brush(GoldShade), 4);
            var handleSketch = Shape(Handle, null, 2.2, 0.5);
            handleSketch.RenderTransform = new TranslateTransform(-3, 2);

            Shape(Disc, bodyFill, 7);
            // the hand-inked second line that makes it look drawn rather than vector
            var sketch = Shape(Disc, null, 2.2, 0.55);
            sketch.RenderTransform = new TranslateTransform(-3, 3);

            var faceFill = new RadialGradientBrush(Color.FromRgb(0xD6, 0x9C, 0x34), Color.FromRgb(0xB8, 0x7E, 0x22)) { GradientOrigin = new Point(0.4, 0.3) };
            faceFill.Freeze();
            Shape(Face, faceFill, 4);

            // thumbstick: socket + cap
            var stick = Anchor(left, QuestPart.Stick);
            Circle(c, stick, 44, Paint.Brush(GoldShade), 5);
            Circle(c, stick, 30, new RadialGradientBrush(Gold, GoldDeep) { GradientOrigin = new Point(0.35, 0.3) }, 4);
            Circle(c, stick, 17, null, 2, 0.6);

            // face buttons
            Button(c, Anchor(left, QuestPart.Secondary), 25, left ? "Y" : "B");
            Button(c, Anchor(left, QuestPart.Primary), 25, left ? "X" : "A");

            // menu (left) / Meta button (right, reserved by the headset)
            var m = Anchor(left, QuestPart.Menu);
            var oval = new Ellipse { Width = 40, Height = 30, Fill = Paint.Brush(InkBtn), Stroke = Paint.Brush(Paint.Ink), StrokeThickness = 3 };
            c.Children.Add(oval.At(m.X - 20, m.Y - 15));
            if (left)
            {
                for (var i = -1; i <= 1; i++)
                    c.Children.Add(new Line { X1 = m.X - 9, X2 = m.X + 9, Y1 = m.Y + i * 6, Y2 = m.Y + i * 6, Stroke = Paint.Brush(Letter), StrokeThickness = 2.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
            else
            {
                c.Children.Add(new Ellipse { Width = 14, Height = 9, Stroke = Paint.Brush(Letter), StrokeThickness = 2 }.At(m.X - 7, m.Y - 4.5));
            }

            return c;
        }

        private static void Circle(Canvas c, Point p, double r, Brush fill, double stroke, double opacity = 1)
        {
            var e = new Ellipse
            {
                Width = r * 2, Height = r * 2, Fill = fill, Stroke = Paint.Brush(Paint.Ink), StrokeThickness = stroke, Opacity = opacity,
            };
            c.Children.Add(e.At(p.X - r, p.Y - r));
        }

        private static void Button(Canvas c, Point p, double r, string letter)
        {
            var e = new Ellipse
            {
                Width = r * 2, Height = r * 2, Fill = new RadialGradientBrush(Color.FromRgb(0x3A, 0x26, 0x15), InkBtn) { GradientOrigin = new Point(0.35, 0.3) },
                Stroke = Paint.Brush(Paint.Ink), StrokeThickness = 4,
            };
            c.Children.Add(e.At(p.X - r, p.Y - r));
            var t = Look.Text(letter, r * 1.35, Letter, null, TextAlignment.Center);
            t.Width = r * 2;
            c.Children.Add(t.At(p.X - r, p.Y - r * 0.82));
        }

        /// <summary>Small inline glyph of a button or stick (for text rows).</summary>
        public static FrameworkElement Glyph(string letter, double size)
        {
            var g = new Grid { Width = size, Height = size };
            g.Children.Add(new Ellipse { Fill = Paint.Brush(InkBtn), Stroke = Paint.Brush(Paint.Ink), StrokeThickness = 3 });
            var t = Look.Text(letter, size * 0.62, Letter, null, TextAlignment.Center);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(0, size * 0.04, 0, 0);
            g.Children.Add(t);
            return g;
        }
    }
}
