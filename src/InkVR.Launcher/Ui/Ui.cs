using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using InkVR.Launcher.Effects;
using InkVR.Launcher.Game;

namespace InkVR.Launcher.Ui
{
    /// <summary>Shared look: fonts, text, the game's art as images, and positioning on the 1920x1080 stage.</summary>
    public static class Look
    {
        public static Animator Anim;

        /// <summary>Bangers (SIL OFL): the comic-title lettering.</summary>
        public static FontFamily Title => (FontFamily)Application.Current.Resources["Brush"];

        /// <summary>Typewriter lettering, like the studio's memos.</summary>
        public static readonly FontFamily MemoFont = new FontFamily("Courier New");

        public static TextBlock Text(string s, double size, Color color, FontFamily font = null, TextAlignment align = TextAlignment.Left, double opacity = 1)
        {
            return new TextBlock
            {
                Text = s,
                FontSize = size,
                FontFamily = font ?? Title,
                Foreground = Paint.Brush(color, opacity),
                TextAlignment = align,
                TextWrapping = TextWrapping.NoWrap,
                SnapsToDevicePixels = false,
            };
        }

        public static TextBlock MemoText(string s, double size, Color color, double width, double opacity = 1)
        {
            var t = Text(s, size, color, MemoFont, TextAlignment.Left, opacity);
            t.FontWeight = FontWeights.Bold;
            t.TextWrapping = TextWrapping.Wrap;
            t.Width = width;
            return t;
        }

        public static double Measure(string s, double size, FontFamily font = null)
        {
            var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(font ?? Title, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Brushes.Black, 1.0);
            return ft.WidthIncludingTrailingWhitespace;
        }

        public static BitmapSource Art(string name) => ArtLibrary.Get(name);

        public static Image Img(string art, double w, double h, Stretch stretch = Stretch.Fill)
        {
            var img = new Image { Width = w, Height = h, Stretch = stretch, Source = Art(art) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }

        /// <summary>The art's shape filled with one colour (e.g. the white BATIM logo printed in ink).</summary>
        public static Rectangle Tinted(string art, Color c, double w, double h)
        {
            var r = new Rectangle { Width = w, Height = h, Fill = Paint.Brush(c) };
            var src = Art(art);
            if (src != null) r.OpacityMask = new ImageBrush(src) { Stretch = Stretch.Fill };
            return r;
        }

        /// <summary>
        /// Makes an element clickable. The press is claimed (so the window-drag on the background never sees it)
        /// and the click fires on release over the element, like a normal button.
        /// </summary>
        /// <summary>Test harness only: synthetic events carry the real cursor position, so skip the "released over it" check.</summary>
        public static bool TestIgnorePosition;

        public static void OnClick(this FrameworkElement e, System.Action click)
        {
            var pressed = false;
            e.MouseLeftButtonDown += (s, a) =>
            {
                a.Handled = true;
                pressed = true;
                e.CaptureMouse(); // so a release outside the element cancels, like a normal button
            };
            e.MouseLeave += (s, a) =>
            {
                if (!e.IsMouseCaptured) pressed = false;
            };
            e.MouseLeftButtonUp += (s, a) =>
            {
                if (!pressed) return;
                pressed = false;
                if (e.IsMouseCaptured) e.ReleaseMouseCapture();
                a.Handled = true;
                var p = a.GetPosition(e);
                if (TestIgnorePosition || p.X >= -2 && p.Y >= -2 && p.X <= e.ActualWidth + 2 && p.Y <= e.ActualHeight + 2) click();
            };
        }

        public static T At<T>(this T e, double x, double y) where T : UIElement
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            return e;
        }
    }

    /// <summary>An element with scale/rotate/translate transforms (about a chosen origin) for animating.</summary>
    public sealed class Sprite
    {
        public readonly FrameworkElement E;
        private readonly ScaleTransform _s = new ScaleTransform();
        private readonly RotateTransform _r = new RotateTransform();
        private readonly TranslateTransform _t = new TranslateTransform();

        public Sprite(FrameworkElement e, double originX = 0.5, double originY = 0.5)
        {
            E = e;
            var g = new TransformGroup();
            g.Children.Add(_s);
            g.Children.Add(_r);
            g.Children.Add(_t);
            e.RenderTransform = g;
            e.RenderTransformOrigin = new Point(originX, originY);
        }

        public double Scale { set { _s.ScaleX = value; _s.ScaleY = value; } }
        public double ScaleX { get => _s.ScaleX; set => _s.ScaleX = value; }
        public double ScaleY { get => _s.ScaleY; set => _s.ScaleY = value; }
        public double Rot { get => _r.Angle; set => _r.Angle = value; }
        public double X { get => _t.X; set => _t.X = value; }
        public double Y { get => _t.Y; set => _t.Y = value; }
        public double Opacity { get => E.Opacity; set => E.Opacity = value; }
        public bool Visible { set => E.Visibility = value ? Visibility.Visible : Visibility.Hidden; }
    }
}
