using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using InkVR.Launcher.Effects;

namespace InkVR.Launcher.Ui
{
    /// <summary>
    /// A menu entry lettered in ink. Highlighting brushes the game's dark ink splash in behind it and turns
    /// the lettering to paper, the way the game's own menu marks the selected item.
    /// </summary>
    public sealed class InkButton : Canvas
    {
        public event Action Click;
        public event Action<InkButton> Hovered;

        public readonly string Label;
        private readonly TextBlock _text;
        private readonly FrameworkElement _splash;
        private readonly ScaleTransform _splashScale = new ScaleTransform(0, 1);
        private readonly Color _normal;
        private bool _lit;
        private double _amount;
        public bool Enabled = true;

        public double TextWidth { get; }

        public InkButton(string label, double size, Color? normal = null)
        {
            Label = label;
            _normal = normal ?? Paint.Ink;
            TextWidth = Look.Measure(label, size);
            Width = TextWidth + 40;
            Height = size * 1.18;
            Background = Brushes.Transparent; // hit-testable
            Cursor = Cursors.Hand;

            var splashW = TextWidth + 110;
            var splashH = size * 1.55;
            if (Look.Art("Large Dark Horizontal Splash") != null)
                _splash = Look.Img("Large Dark Horizontal Splash", splashW, splashH);
            else
                _splash = new Rectangle { Width = splashW, Height = splashH * 0.7, Fill = Paint.Brush(Paint.Ink), RadiusX = 18, RadiusY = 18 };
            _splash.RenderTransform = _splashScale;
            _splash.RenderTransformOrigin = new Point(0, 0.5);
            _splash.IsHitTestVisible = false;
            Children.Add(_splash.At(-45, (Height - _splash.Height) / 2 + size * 0.02));

            _text = Look.Text(label, size, _normal);
            _text.IsHitTestVisible = false;
            Children.Add(_text.At(0, 0));

            MouseEnter += (s, e) => Hovered?.Invoke(this);
            MouseLeftButtonUp += (s, e) =>
            {
                if (Enabled) Click?.Invoke();
                e.Handled = true;
            };
        }

        public void Invoke()
        {
            if (Enabled) Click?.Invoke();
        }

        public bool Lit
        {
            get => _lit;
            set
            {
                if (_lit == value) return;
                _lit = value;
                var from = _amount;
                var to = value ? 1.0 : 0.0;
                Look.Anim.Run(0, value ? 0.2 : 0.14, value ? (Func<double, double>)Ease.OutCubic : Ease.InCubic, p => Apply(from + (to - from) * p));
            }
        }

        private void Apply(double a)
        {
            _amount = a;
            _splashScale.ScaleX = a;
            var c = a > 0.45 ? Paint.Paper : _normal;
            _text.Foreground = Paint.Brush(c);
        }
    }
}
