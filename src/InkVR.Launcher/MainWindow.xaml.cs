using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using InkVR.Launcher.Effects;
using InkVR.Launcher.Game;
using InkVR.Launcher.Ui;

namespace InkVR.Launcher
{
    /// <summary>
    /// The InkVR Launcher. Opening: a film-leader countdown, a title card, then the game's own menu art
    /// revealed by ink splats, the logo dropping in and a "VR" rubber stamp. Closing: the menu clears away,
    /// Bendy fills up like the game's loader, and an old-cartoon iris closes on him in the shape of his head.
    /// </summary>
    public partial class MainWindow : Window
    {
        private enum Mode { Intro, Home, Closing }

        private readonly Animator A = new Animator();
        private readonly Random _rng = new Random();
        private Mode _mode = Mode.Intro;
        private double _t0 = -1, _t, _lastT;
        private int _frame24 = -1;
        private bool _reduce;

        private GameInstall _game;
        private ModSettingsFile _cfg;
        private volatile bool _artReady;

        // home layers
        private Grid _menu;
        private Image _bg, _light, _frame;
        private Canvas _scene, _drips, _pageLayer, _chrome;
        private Sprite _menuS, _logoS, _stampS, _subS, _circleGroupS, _circleS, _bendyS, _markerS, _statusS, _chromeS;
        private Image _bendy;
        private readonly List<InkButton> _buttons = new List<InkButton>();
        private readonly List<Sprite> _buttonS = new List<Sprite>();
        private int _selected = -1;
        private TextBlock _status;
        private double _lightTarget = 0.6, _shake;
        private double _nextDrip = 3;
        private readonly List<Drip> _dripList = new List<Drip>();

        // overlays
        private Rectangle _grain, _flash, _dim;
        private ImageBrush _grainBrush;
        private BitmapSource[] _grains;
        private Canvas _scratches;
        private Grid _leader, _card, _end;
        private Sprite _leaderS, _cardS, _cardCircleS, _endS;
        private Path _wedge, _iris;
        private TextBlock _count, _cardWait, _endText;
        private Rectangle _leaderDim, _cardRing, _endRing;
        private int _lastCount = -1;

        // reveal mask
        private DrawingGroup _mask;
        private bool _revealStarted, _skipRequested;

        // iris
        private Point _irisCenter;
        private double _irisR = -1;

        // capture mode (developer aid for screenshots / teaser frames)
        private readonly string _captureDir;
        private readonly double _captureFps, _captureSeconds;
        private readonly List<(double T, string Action)> _script = new List<(double, string)>();
        private int _captureFrame;

        private const double CircleX = 1350, CircleY = 545;
        public static readonly string Version = typeof(MainWindow).Assembly.GetName().Version.ToString(3);

        public MainWindow() : this(null, 0, 0, null)
        {
        }

        private readonly bool _smoke;

        public MainWindow(string captureDir, double fps, double seconds, string script, bool smoke = false)
        {
            _smoke = smoke;
            InitializeComponent();
            Look.Anim = A;
            _captureDir = captureDir;
            _captureFps = fps;
            _captureSeconds = seconds;
            if (script != null)
                foreach (var part in script.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var i = part.IndexOf('=');
                    if (i > 0) _script.Add((double.Parse(part.Substring(0, i), System.Globalization.CultureInfo.InvariantCulture), part.Substring(i + 1)));
                    else _script.Add((0, part));
                }

            RenderOptions.SetBitmapScalingMode(Root, BitmapScalingMode.HighQuality);
            Loaded += OnLoaded;
            KeyDown += OnKey;
            Root.MouseLeftButtonDown += OnRootMouseDown;
        }

        private bool Capturing => _captureDir != null;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var area = SystemParameters.WorkArea;
            Width = Math.Min(1600, area.Width * 0.75);
            Height = Width * 9 / 16;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
            if (Capturing || _smoke)
            {
                Left = -10000;
                ShowInTaskbar = false;
            }
            else if (LauncherSettings.Fullscreen)
            {
                WindowState = WindowState.Maximized;
            }

            _game = GameLocator.Find();
            _cfg = _game != null ? new ModSettingsFile(_game.ConfigFile) : null;
            _reduce = _cfg != null && _cfg.GetBool("Comfort", "ReduceFlashes", false);
            Sfx.Muted = Capturing || !LauncherSettings.Sounds;
            if (_smoke) Sfx.Master = 0; // exercise the sound code silently
            try
            {
                Sfx.Prepare();
            }
            catch (Exception ex)
            {
                LauncherLog.Write("Sounds unavailable: " + ex.Message);
                Sfx.Muted = true;
            }

            _grains = Enumerable.Range(0, 6).Select(i => Paint.Grain(512, 11 + i)).ToArray();
            BuildOverlays();

            if (Capturing)
            {
                ArtLibrary.Load(_game);
                ArtReady();
                var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1) };
                timer.Tick += (s, a) => CaptureStep(timer);
                timer.Start();
            }
            else
            {
                Task.Run(() =>
                {
                    try
                    {
                        ArtLibrary.Load(_game);
                    }
                    catch (Exception ex)
                    {
                        LauncherLog.Write("Art load failed: " + ex);
                    }
                }).ContinueWith(_ => Dispatcher.Invoke(ArtReady));
                CompositionTarget.Rendering += (s, a) => Frame(Clock.Now);
                Sfx.StartLoop("projector", 0.0);
                if (_smoke)
                {
                    var start = Clock.Now;
                    var done = new HashSet<int>();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    timer.Tick += (s, a) =>
                    {
                        var t = Clock.Now - start;
                        for (var i = 0; i < _script.Count; i++)
                            if (_script[i].T <= t && done.Add(i))
                            {
                                LauncherLog.Write($"smoke {t:0.0}s: {_script[i].Action}");
                                RunScript(_script[i].Action);
                            }

                        if (t >= _captureSeconds)
                        {
                            timer.Stop();
                            LauncherLog.Write($"smoke done: mode={_mode} art={_artReady} rendered {_frames} frames in {t:0.0}s ({_frames / t:0} fps)");
                            Application.Current.Shutdown();
                        }
                    };
                    timer.Start();
                }
            }
        }

        private void ArtReady()
        {
            // the rings on the title and end cards are the game's menu ring, printed in paper colour
            foreach (var ring in new[] { _cardRing, _endRing })
            {
                var art = Look.Art("menu_circle");
                if (art != null) ring.OpacityMask = new ImageBrush(art) { Stretch = Stretch.Fill };
                else ring.Visibility = Visibility.Collapsed;
            }

            BuildMenu();
            _artReady = true;
        }

        // ------------------------------------------------------------------ building

        private void BuildOverlays()
        {
            // Film leader: sepia stock, crosshair, rings, sweeping hand, big number.
            _leader = new Grid { Width = 1920, Height = 1080 };
            var stock = new RadialGradientBrush(Color.FromRgb(0xDC, 0xB2, 0x70), Color.FromRgb(0x6E, 0x48, 0x1C)) { RadiusX = 0.7, RadiusY = 0.8 };
            _leader.Children.Add(new Rectangle { Fill = stock });
            var lc = new Canvas();
            lc.Children.Add(new Line { X1 = 0, X2 = 1920, Y1 = 540, Y2 = 540, Stroke = Paint.Brush(Paint.Ink, 0.75), StrokeThickness = 5 });
            lc.Children.Add(new Line { X1 = 960, X2 = 960, Y1 = 0, Y2 = 1080, Stroke = Paint.Brush(Paint.Ink, 0.75), StrokeThickness = 5 });
            _wedge = new Path { Fill = Paint.Brush(Paint.Ink, 0.32) };
            lc.Children.Add(_wedge);
            lc.Children.Add(new Ellipse { Width = 760, Height = 760, Stroke = Paint.Brush(Paint.Ink), StrokeThickness = 10 }.At(960 - 380, 540 - 380));
            lc.Children.Add(new Ellipse { Width = 640, Height = 640, Stroke = Paint.Brush(Paint.Ink), StrokeThickness = 5 }.At(960 - 320, 540 - 320));
            _leader.Children.Add(lc);
            _count = Look.Text("3", 470, Paint.Ink, null, TextAlignment.Center);
            _count.HorizontalAlignment = HorizontalAlignment.Center;
            _count.VerticalAlignment = VerticalAlignment.Center;
            _count.Margin = new Thickness(0, 40, 0, 0);
            _leader.Children.Add(_count);
            _leaderDim = new Rectangle { Fill = Brushes.Black, Opacity = 0 };
            _leader.Children.Add(_leaderDim);
            _leaderS = new Sprite(_leader);
            _leader.Visibility = Visibility.Hidden;

            // Title card.
            _card = new Grid { Width = 1920, Height = 1080, Background = Paint.Brush(Color.FromRgb(0x12, 0x0B, 0x06)), Visibility = Visibility.Hidden };
            var cardCircle = _cardRing = new Rectangle { Width = 760, Height = 760, Fill = Paint.Brush(Paint.Paper), OpacityMask = Brushes.Transparent };
            cardCircle.HorizontalAlignment = HorizontalAlignment.Center;
            cardCircle.VerticalAlignment = VerticalAlignment.Center;
            cardCircle.Opacity = 0.85;
            _cardCircleS = new Sprite(cardCircle);
            _card.Children.Add(cardCircle);
            var names = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
            var who = Look.Text("STRIKEZONEVR", 150, Paint.Paper, null, TextAlignment.Center);
            names.Children.Add(who);
            names.Children.Add(Look.Text("PRESENTS", 60, Paint.Paper, null, TextAlignment.Center, 0.85));
            _card.Children.Add(names);
            _cardWait = Look.Text("DEVELOPING THE FILM...", 36, Paint.Paper, null, TextAlignment.Center, 0.6);
            _cardWait.HorizontalAlignment = HorizontalAlignment.Center;
            _cardWait.VerticalAlignment = VerticalAlignment.Bottom;
            _cardWait.Margin = new Thickness(0, 0, 0, 90);
            _cardWait.Visibility = Visibility.Hidden;
            _card.Children.Add(_cardWait);
            _cardS = new Sprite(_card);

            _dim = new Rectangle { Fill = Brushes.Black, Opacity = 0, IsHitTestVisible = false };
            _flash = new Rectangle { Fill = Paint.Brush(Color.FromRgb(0xFF, 0xF4, 0xD8)), Opacity = 0, IsHitTestVisible = false };
            _iris = new Path { Fill = Brushes.Black, IsHitTestVisible = false, Visibility = Visibility.Hidden };

            _end = new Grid { Width = 1920, Height = 1080, Visibility = Visibility.Hidden, IsHitTestVisible = false };
            var endCircle = _endRing = new Rectangle { Width = 700, Height = 700, Fill = Paint.Brush(Paint.Paper), OpacityMask = Brushes.Transparent };
            endCircle.HorizontalAlignment = HorizontalAlignment.Center;
            endCircle.VerticalAlignment = VerticalAlignment.Center;
            endCircle.Opacity = 0.8;
            _end.Children.Add(endCircle);
            _endText = Look.Text("THE END", 190, Paint.Paper, null, TextAlignment.Center);
            _endText.HorizontalAlignment = HorizontalAlignment.Center;
            _endText.VerticalAlignment = VerticalAlignment.Center;
            _endText.Margin = new Thickness(0, 30, 0, 0);
            _end.Children.Add(_endText);
            _endS = new Sprite(_end);

            _grainBrush = new ImageBrush(_grains[0]) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 512, 512) };
            _grain = new Rectangle { Fill = _grainBrush, IsHitTestVisible = false, Opacity = _reduce ? 0.45 : 0.8 };
            _scratches = new Canvas { IsHitTestVisible = false };

            Root.Children.Add(_leader);
            Root.Children.Add(_card);
            // the menu goes in at index 0 when the art is ready (under the leader and card)
            Root.Children.Add(_dim);
            Root.Children.Add(_iris);
            Root.Children.Add(_end);
            Root.Children.Add(_flash);
            Root.Children.Add(_grain);
            Root.Children.Add(_scratches);
            Root.Children.Add(new Rectangle { Fill = Paint.Vignette(), IsHitTestVisible = false });
        }

        private void BuildMenu()
        {
            _menu = new Grid { Width = 1920, Height = 1080, Visibility = Visibility.Hidden };
            _menuS = new Sprite(_menu);

            if (Look.Art("menu_bg") != null) _bg = Look.Img("menu_bg", 1920, 1080);
            _menu.Children.Add(_bg ?? (UIElement)new Rectangle { Fill = Paint.SepiaStage() });
            _light = Look.Img("menu_lightcookie", 1920, 1080);
            _light.Opacity = 0.6;
            _menu.Children.Add(_light);

            _scene = new Canvas();
            _menu.Children.Add(_scene);

            // Bendy in the menu's ring
            var group = new Grid { Width = 800, Height = 800 };
            var circle = Look.Img("menu_circle", 800, 800);
            _circleS = new Sprite(circle);
            group.Children.Add(circle);
            _bendy = Look.Img("bendy_pose_14", 560, 560);
            _bendy.HorizontalAlignment = HorizontalAlignment.Center;
            _bendy.VerticalAlignment = VerticalAlignment.Center;
            _bendyS = new Sprite(_bendy, 0.5, 0.95);
            group.Children.Add(_bendy);
            _circleGroupS = new Sprite(group);
            _scene.Children.Add(group.At(CircleX - 400, CircleY - 400));

            // logo printed in ink, with the VR stamp
            FrameworkElement logo;
            if (Look.Art("batim_logo") != null) logo = Look.Tinted("batim_logo", Paint.Ink, 640, 276);
            else logo = Look.Text("BENDY AND THE INK MACHINE", 64, Paint.Ink);
            _logoS = new Sprite(logo);
            _scene.Children.Add(logo.At(140, 78));

            var stamp = new Grid { Width = 236, Height = 142 };
            stamp.Children.Add(new Border { BorderBrush = Paint.Brush(Paint.StampRed), BorderThickness = new Thickness(10), CornerRadius = new CornerRadius(18) });
            var vr = Look.Text("VR", 128, Paint.StampRed, null, TextAlignment.Center);
            vr.HorizontalAlignment = HorizontalAlignment.Center;
            vr.VerticalAlignment = VerticalAlignment.Center;
            vr.Margin = new Thickness(0, 14, 0, 0);
            stamp.Children.Add(vr);
            stamp.OpacityMask = new ImageBrush(Paint.StampWear(236, 142, 5));
            _stampS = new Sprite(stamp);
            _stampS.Rot = -11;
            _scene.Children.Add(stamp.At(778, 196));

            var sub = Look.Text("INKVR  ·  VR MOD MADE BY STRIKEZONEVR", 38, Paint.Ink);
            _subS = new Sprite(sub, 0, 0.5);
            _scene.Children.Add(sub.At(152, 372));

            // Bendy-head marker that hops to the selected entry
            var marker = Look.Img("BendyIcon", 66, 66);
            _markerS = new Sprite(marker);
            _markerS.Opacity = 0;
            _scene.Children.Add(marker.At(70, 0));

            var entries = new (string Label, double Size, Action Act)[]
            {
                ("PLAY IN VR", 86, () => Play(true)),
                ("PLAY WITHOUT VR", 50, () => Play(false)),
                ("CONTROLS", 50, () => OpenPage("controls")),
                ("VR CHECK", 50, () => OpenPage("check")),
                ("OPTIONS", 50, () => OpenPage("options")),
                ("ABOUT", 50, () => OpenPage("about")),
                ("QUIT", 50, Quit),
            };
            var y = 462.0;
            for (var i = 0; i < entries.Length; i++)
            {
                var (label, size, act) = entries[i];
                var b = new InkButton(label, size);
                var index = i;
                b.Click += () =>
                {
                    if (_mode != Mode.Home || _page != null || _note != null) return;
                    LauncherLog.Write("menu: " + label);
                    Sfx.Play("bloop", 0.7);
                    act();
                };
                b.Hovered += _ => Select(index, true);
                _buttons.Add(b);
                _buttonS.Add(new Sprite(b, 0, 0.5));
                _scene.Children.Add(b.At(160, y));
                y += b.Height + (i == 0 ? 6 : 9);
            }

            _frame = Look.Img("menu_frame", 1920, 1080);
            _frame.IsHitTestVisible = false;
            _menu.Children.Add(_frame);

            _drips = new Canvas { IsHitTestVisible = false };
            _menu.Children.Add(_drips);

            _status = Look.Text("", 32, Paint.Ink, null, TextAlignment.Center, 0.8);
            _status.Width = 1200;
            _statusS = new Sprite(_status);
            var statusHost = new Canvas { IsHitTestVisible = false };
            statusHost.Children.Add(_status.At(360, 992));
            _menu.Children.Add(statusHost);
            SetIdleStatus();

            _chrome = new Canvas();
            var min = Look.Text("—", 54, Paint.Ink);
            var close = Look.Text("X", 54, Paint.Ink);
            foreach (var (t, x, act) in new (TextBlock, double, Action)[] { (min, 1712, () => WindowState = WindowState.Minimized), (close, 1786, Quit) })
            {
                t.Cursor = Cursors.Hand;
                t.Background = Brushes.Transparent;
                t.Padding = new Thickness(10, 0, 10, 0);
                t.MouseEnter += (s, e) => t.Foreground = Paint.Brush(Paint.StampRed);
                t.MouseLeave += (s, e) => t.Foreground = Paint.Brush(Paint.Ink);
                t.OnClick(() =>
                {
                    if (_mode == Mode.Home) act();
                });
                _chrome.Children.Add(t.At(x, 44));
            }

            _chromeS = new Sprite(_chrome);
            _menu.Children.Add(_chrome);

            // the dim goes under the sheets, over the menu
            Root.Children.Remove(_dim);
            _menu.Children.Add(_dim);
            _pageLayer = new Canvas();
            _menu.Children.Add(_pageLayer);

            Root.Children.Insert(0, _menu);
        }

        private void SetIdleStatus()
        {
            if (_status == null) return;
            if (_game == null)
            {
                _status.Text = "GAME NOT FOUND  ·  SEE VR CHECK";
                return;
            }

            var runtime = string.IsNullOrEmpty(LauncherSettings.Runtime) ? GameLocator.ActiveRuntimeJson() : LauncherSettings.Runtime;
            var mod = ModInstaller.InstalledVersion(_game);
            _status.Text = (ModInstaller.NeedsInstall(_game) ? "INKVR NEEDS INSTALLING  ·  SEE VR CHECK" : $"READY  ·  INKVR {mod}") +
                           "  ·  " + GameLocator.FriendlyRuntime(runtime).ToUpperInvariant();
        }

        // ------------------------------------------------------------------ per-frame

        private int _frames;

        private void Frame(double clock)
        {
            _frames++;
            if (_t0 < 0) _t0 = clock;
            _t = clock - _t0;
            var dt = Math.Max(0, Math.Min(0.1, _t - _lastT));
            _lastT = _t;
            A.Update(_t);

            var step = (int)Math.Floor(_t * 24);
            var tick24 = step != _frame24;
            _frame24 = step;

            if (_mode == Mode.Intro) Intro(_t, tick24);
            if (_menu != null && _menu.Visibility == Visibility.Visible) Idle(_t, dt, tick24);
            if (tick24) Film();
            if (_irisR >= 0) DrawIris();
        }

        private void Intro(double t, bool tick)
        {
            var play = Capturing || LauncherSettings.PlayIntro;
            if (!play || _skipRequested)
            {
                _leader.Visibility = Visibility.Hidden;
                if (_artReady && !_revealStarted)
                {
                    _card.Visibility = Visibility.Hidden;
                    BeginReveal();
                }
                else if (!_artReady)
                {
                    _card.Visibility = Visibility.Visible;
                    _cardWait.Visibility = Visibility.Visible;
                }

                return;
            }

            if (t < 0.25)
            {
                Sfx.LoopVolume(t / 0.25 * 0.35);
                return;
            }

            if (t < 3.25)
            {
                _leader.Visibility = Visibility.Visible;
                var lt = t - 0.25;
                var n = 3 - (int)Math.Floor(lt);
                if (n != _lastCount)
                {
                    _lastCount = n;
                    _count.Text = n.ToString();
                    Sfx.Play("beep", 0.45);
                }

                var frac = lt - Math.Floor(lt);
                _wedge.Data = Wedge(960, 540, 320, frac * 360);
                if (tick)
                {
                    _leaderS.X = (_rng.NextDouble() - 0.5) * (_reduce ? 2 : 8);
                    _leaderS.Y = (_rng.NextDouble() - 0.5) * (_reduce ? 2 : 8);
                    _leaderDim.Opacity = _reduce ? 0.04 : _rng.NextDouble() * 0.16;
                }

                return;
            }

            if (_leader.Visibility == Visibility.Visible)
            {
                _leader.Visibility = Visibility.Hidden;
                if (!_reduce) A.Run(0, 0.14, Ease.Linear, p => _flash.Opacity = 1 - p);
                _card.Visibility = Visibility.Visible;
                _cardS.Opacity = 0;
                A.Run(0.05, 0.3, Ease.OutCubic, p => _cardS.Opacity = p, null, "intro");
                Sfx.Play("bloop", 0.5);
            }

            _cardCircleS.Rot = t * 14;
            _cardS.Scale = 1 + (t - 3.25) * 0.012;
            if (tick && !_reduce && t > 3.6) _cardS.Opacity = 0.9 + _rng.NextDouble() * 0.1;

            if (t >= 4.7)
            {
                if (_artReady) BeginReveal();
                else _cardWait.Visibility = Visibility.Visible;
            }
        }

        private static Geometry Wedge(double cx, double cy, double r, double degrees)
        {
            if (degrees <= 0.5) return Geometry.Empty;
            if (degrees >= 359.5) return new EllipseGeometry(new Point(cx, cy), r, r);
            var a = (degrees - 90) * Math.PI / 180;
            var end = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            var fig = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(cx, cy - r), true));
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, degrees > 180, SweepDirection.Clockwise, true));
            return new PathGeometry(new[] { fig });
        }

        private void BeginReveal()
        {
            if (_revealStarted) return;
            _revealStarted = true;
            _leader.Visibility = Visibility.Hidden;
            _menu.Visibility = Visibility.Visible;
            Sfx.LoopVolume(0.3);

            if (_card.Visibility == Visibility.Visible)
                A.Run(0.1, 0.35, Ease.Linear, p => _cardS.Opacity = 1 - p, () => _card.Visibility = Visibility.Hidden, "reveal");

            // ink splats eat through to the menu
            _mask = new DrawingGroup();
            _mask.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 1920, 1080))));
            var splat = Look.Art("ink_splat_decal");
            var spots = new[] { (960.0, 520.0, 0.0), (520.0, 330.0, 0.08), (1450.0, 700.0, 0.14), (380.0, 860.0, 0.22), (1500.0, 250.0, 0.27), (980.0, 980.0, 0.33) };
            Sfx.Play("bloop", 0.8);
            foreach (var (cx, cy, delay) in spots)
            {
                Drawing d;
                Action<double> grow;
                if (splat != null)
                {
                    var img = new ImageDrawing(splat, new Rect(cx, cy, 0, 0));
                    d = img;
                    grow = s => img.Rect = new Rect(cx - 900 * s, cy - 900 * s, 1800 * s, 1800 * s);
                }
                else
                {
                    var eg = new EllipseGeometry(new Point(cx, cy), 0, 0);
                    d = new GeometryDrawing(Brushes.Black, null, eg);
                    grow = s => eg.RadiusX = eg.RadiusY = 700 * s;
                }

                _mask.Children.Add(d);
                A.Run(delay, 0.75, Ease.OutCubic, grow, null, "reveal");
            }

            var fill = new SolidColorBrush(Colors.Black) { Opacity = 0 };
            _mask.Children.Add(new GeometryDrawing(fill, null, new RectangleGeometry(new Rect(0, 0, 1920, 1080))));
            A.Run(0.55, 0.45, Ease.InCubic, p => fill.Opacity = p, () => _menu.OpacityMask = null, "reveal");
            _menu.OpacityMask = new DrawingBrush(_mask) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 1920, 1080) };

            // logo drops in
            _logoS.Y = -460;
            A.Run(0.3, 0.75, Ease.OutBounce, p => _logoS.Y = -460 * (1 - p), null, "reveal");

            // VR stamp slams down
            _stampS.Opacity = 0;
            A.Run(1.0, 0.2, Ease.InQuad, p =>
            {
                _stampS.Opacity = Math.Min(1, p * 2);
                _stampS.Scale = 3.4 - 2.4 * p;
                _stampS.Rot = -32 + 21 * p;
            }, () =>
            {
                Sfx.Play("stamp", 0.9);
                if (!_reduce) _shake = 1;
            }, "reveal");

            _subS.Opacity = 0;
            A.Run(1.25, 0.4, Ease.OutCubic, p =>
            {
                _subS.Opacity = p;
                _subS.Y = 16 * (1 - p);
            }, null, "reveal");

            for (var i = 0; i < _buttonS.Count; i++)
            {
                var s = _buttonS[i];
                s.X = -620;
                s.Opacity = 0;
                A.Run(1.05 + i * 0.07, 0.55, Ease.OutBack, p =>
                {
                    s.X = -620 * (1 - p);
                    s.Opacity = Math.Min(1, p * 1.5);
                }, null, "reveal");
            }

            _circleGroupS.Opacity = 0;
            A.Run(0.6, 0.8, Ease.OutBack, p =>
            {
                _circleGroupS.Opacity = Math.Min(1, p * 1.4);
                _circleGroupS.Scale = 0.45 + 0.55 * p;
            }, null, "reveal");
            _bendyS.Scale = 0;
            A.Run(1.2, 1.0, Ease.OutElastic, p => _bendyS.Scale = p, null, "reveal");

            _statusS.Opacity = 0;
            _chromeS.Opacity = 0;
            A.Run(1.5, 0.5, Ease.Linear, p =>
            {
                _statusS.Opacity = p;
                _chromeS.Opacity = p;
            }, null, "reveal");

            A.At(1.7, () =>
            {
                _mode = Mode.Home;
                if (_selected < 0) Select(0, false);
                RunScriptDefaults();
            }, "reveal");
        }

        private void Idle(double t, double dt, bool tick)
        {
            if (tick)
            {
                if (_reduce) _lightTarget = 0.62;
                else _lightTarget = _rng.NextDouble() < 0.03 ? 0.3 : 0.56 + _rng.NextDouble() * 0.14;
            }

            if (_light != null) _light.Opacity += (_lightTarget - _light.Opacity) * Math.Min(1, dt * 18);

            // gate weave + stamp shake
            double wx = 0, wy = 0;
            if (!_reduce && tick && (_frame24 & 1) == 0)
            {
                wx = (_rng.NextDouble() - 0.5) * 1.6;
                wy = (_rng.NextDouble() - 0.5) * 1.6;
            }

            if (_shake > 0)
            {
                _shake = Math.Max(0, _shake - dt * 3.2);
                wx += Math.Sin(t * 95) * 12 * _shake;
                wy += Math.Cos(t * 83) * 9 * _shake;
            }

            if (tick || _shake > 0)
            {
                _menuS.X = wx;
                _menuS.Y = wy;
            }

            if (_mode != Mode.Closing)
            {
                _circleS.Rot = t * 7;
                var bounce = Math.Abs(Math.Sin(t * Math.PI * 1.15));
                _bendyS.Y = -18 * bounce;
                if (_bendyS.ScaleX > 0.98 && !A.Busy("reveal"))
                {
                    var squash = Math.Max(0, 0.25 - bounce) * 0.16;
                    _bendyS.ScaleX = 1 + squash;
                    _bendyS.ScaleY = 1 - squash;
                }

                _bendyS.Rot = 3 * Math.Sin(t * Math.PI * 0.575);
                if (_markerS.Opacity > 0) _markerS.Rot = 8 * Math.Sin(t * 5);
            }

            if (_mode == Mode.Home)
            {
                if (t > _nextDrip)
                {
                    _nextDrip = t + 2.5 + _rng.NextDouble() * 3.5;
                    SpawnDrip();
                }
            }

            UpdateDrips(dt);
        }

        private void Film()
        {
            _grainBrush.ImageSource = _grains[_rng.Next(_grains.Length)];
            _grainBrush.Viewport = new Rect(-_rng.Next(512), -_rng.Next(512), 512, 512);

            for (var i = _scratches.Children.Count - 1; i >= 0; i--)
            {
                var l = (Line)_scratches.Children[i];
                var life = (int)l.Tag - 1;
                if (life <= 0) _scratches.Children.RemoveAt(i);
                else l.Tag = life;
            }

            if (_rng.NextDouble() < (_reduce ? 0.015 : 0.07))
            {
                var x = 60 + _rng.NextDouble() * 1800;
                var slant = (_rng.NextDouble() - 0.5) * 30;
                var light = _rng.NextDouble() < 0.6;
                _scratches.Children.Add(new Line
                {
                    X1 = x, X2 = x + slant, Y1 = -10, Y2 = 1090,
                    Stroke = Paint.Brush(light ? Paint.Paper : Paint.Ink, light ? 0.3 : 0.35),
                    StrokeThickness = 1 + _rng.NextDouble() * 2, Tag = 2 + _rng.Next(5),
                });
            }
        }

        // ------------------------------------------------------------------ ink drips from the frame

        private sealed class Drip
        {
            public Ellipse Head;
            public Rectangle Neck;
            public double X, Top, Y, V, Age;
        }

        private void SpawnDrip()
        {
            var d = new Drip { X = 130 + _rng.NextDouble() * 1660, Top = 40 + _rng.NextDouble() * 10 };
            d.Y = d.Top;
            d.Head = new Ellipse { Width = 16, Height = 20, Fill = Paint.Brush(Paint.Ink) };
            d.Neck = new Rectangle { Width = 6, Fill = Paint.Brush(Paint.Ink), RadiusX = 3, RadiusY = 3 };
            _drips.Children.Add(d.Neck);
            _drips.Children.Add(d.Head);
            _dripList.Add(d);
        }

        private void UpdateDrips(double dt)
        {
            for (var i = _dripList.Count - 1; i >= 0; i--)
            {
                var d = _dripList[i];
                d.Age += dt;
                if (d.Age < 1.4)
                {
                    d.Y = d.Top + 26 * Ease.OutCubic(d.Age / 1.4);
                    d.Head.Width = 10 + 6 * d.Age / 1.4;
                }
                else
                {
                    d.V += 1400 * dt;
                    d.Y += d.V * dt;
                }

                var neckTop = Math.Max(d.Top, d.Y - 150);
                d.Neck.Height = Math.Max(0, d.Y - neckTop);
                d.Neck.At(d.X - 3, neckTop);
                d.Head.At(d.X - d.Head.Width / 2, d.Y - 6);
                if (d.Y > 1150)
                {
                    _drips.Children.Remove(d.Head);
                    _drips.Children.Remove(d.Neck);
                    _dripList.RemoveAt(i);
                }
            }
        }

        // ------------------------------------------------------------------ menu selection & input

        private void Select(int index, bool sound)
        {
            if (index < 0 || index >= _buttons.Count || index == _selected) return;
            if (_mode == Mode.Closing) return;
            _selected = index;
            for (var i = 0; i < _buttons.Count; i++) _buttons[i].Lit = i == index;
            var b = _buttons[index];
            var targetY = Canvas.GetTop(b) + b.Height / 2 - 33;
            var fromY = _markerS.Opacity > 0 ? _markerS.Y : targetY;
            A.Cancel("marker");
            A.Run(0, 0.22, Ease.OutBack, p =>
            {
                _markerS.Y = fromY + (targetY - fromY) * p;
                _markerS.Opacity = Math.Max(_markerS.Opacity, p);
            }, null, "marker");
            if (sound) Sfx.Play("hover", 0.35);
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }

            if (_mode == Mode.Intro)
            {
                SkipIntro();
                return;
            }

            if (_mode != Mode.Home) return;
            if (_note != null)
            {
                if (e.Key == Key.Escape) CloseNote();
                return;
            }

            if (_page != null)
            {
                if (e.Key == Key.Escape || e.Key == Key.Back) ClosePage();
                return;
            }

            switch (e.Key)
            {
                case Key.Up:
                case Key.W:
                    Select((_selected - 1 + _buttons.Count) % _buttons.Count, true);
                    break;
                case Key.Down:
                case Key.S:
                    Select((_selected + 1) % _buttons.Count, true);
                    break;
                case Key.Enter:
                case Key.Space:
                    if (_selected >= 0) _buttons[_selected].Invoke();
                    break;
                case Key.Escape:
                    Select(_buttons.Count - 1, true);
                    break;
            }
        }

        private void OnRootMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_mode == Mode.Intro)
            {
                SkipIntro();
                return;
            }

            if (_smoke)
            {
                LauncherLog.Write("test: background press reached the window (drag)");
                return;
            }

            if (_mode == Mode.Home && WindowState == WindowState.Normal && e.ButtonState == MouseButtonState.Pressed && !Capturing)
            {

                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // released before the drag started
                }
            }
        }

        private void SkipIntro()
        {
            if (_revealStarted)
            {
                A.Finish("reveal");
                return;
            }

            _skipRequested = true;
        }

        // ------------------------------------------------------------------ play / quit (closing animation)

        private void Play(bool vr)
        {
            if (_game == null)
            {
                ShowNote("GAME NOT FOUND", "InkVR couldn't find Bendy and the Ink Machine. Put InkVR Launcher in the game's folder, or locate the game.",
                    "LOCATE GAME", LocateGame);
                return;
            }

            if (GameLaunch.GameRunning())
            {
                ShowNote("ALREADY RUNNING", "Bendy and the Ink Machine is already running. Close it first to start it again from here.");
                return;
            }

            if (ModInstaller.NeedsInstall(_game))
            {
                if (ModInstaller.PayloadDir == null && (!ModInstaller.BepInExInstalled(_game) || ModInstaller.InstalledVersion(_game) == null))
                {
                    ShowNote("INKVR ISN'T INSTALLED", "The InkVR-Payload folder from the release zip must sit next to InkVR Launcher.exe. Re-extract the zip and try again.");
                    return;
                }

                RunInstall(() => Play(vr));
                return;
            }

            var runtime = LauncherSettings.Runtime;
            var effective = string.IsNullOrEmpty(runtime) ? GameLocator.ActiveRuntimeJson() : runtime;
            if (vr && effective != null && effective.IndexOf("virtualdesktop", StringComparison.OrdinalIgnoreCase) >= 0 &&
                !GameLaunch.VirtualDesktopStreamerRunning() && !Capturing)
            {
                ShowNote("VIRTUAL DESKTOP ISN'T RUNNING", "Start the Virtual Desktop Streamer on this PC and connect from your Quest 3, then press PLAY IN VR. Or play anyway and connect while the game loads.",
                    "PLAY ANYWAY", () => BeginClose(true, true));
                return;
            }

            BeginClose(true, vr);
        }

        private void Quit()
        {
            if (_mode != Mode.Home) return;
            BeginClose(false, false);
        }

        private void BeginClose(bool launch, bool vr)
        {
            if (_mode == Mode.Closing) return;
            _mode = Mode.Closing;
            CloseNote(true);
            ClosePage(true);
            foreach (var b in _buttons) b.Lit = false;
            A.Cancel("marker");
            A.Run(0, 0.2, Ease.Linear, p => _markerS.Opacity = Math.Min(_markerS.Opacity, 1 - p));

            for (var i = 0; i < _buttonS.Count; i++)
            {
                var s = _buttonS[i];
                A.Run(i * 0.035, 0.4, Ease.InCubic, p =>
                {
                    s.X = -700 * p;
                    s.Opacity = 1 - p;
                });
            }

            A.Run(0.1, 0.45, Ease.InCubic, p =>
            {
                _logoS.Y = -520 * p;
                _stampS.Y = -520 * p;
                _subS.Opacity = 1 - p;
                _chromeS.Opacity = 1 - p;
            });

            // Bendy's ring glides to centre stage
            var startRot = _circleS.Rot;
            A.Run(0.15, 0.7, Ease.InOutCubic, p =>
            {
                _circleGroupS.X = (960 - CircleX) * p;
                _circleGroupS.Y = (540 - CircleY) * p;
                _bendyS.Y *= 1 - p;
                _bendyS.Rot *= 1 - p;
                _circleS.Rot = startRot + 120 * p;
            });

            var launched = false;
            string error = null;
            if (launch)
            {
                _status.Text = vr ? "STARTING BENDY AND THE INK MACHINE IN VR..." : "STARTING BENDY AND THE INK MACHINE...";
                // Bendy fills up with ink like the game's own loading face
                var frames = new[] { "Bendy Loader - 01 Blank", "Bendy Loader - 02", "Bendy Loader - 03", "Bendy Loader - 04", "Bendy Loader - 05 Face" };
                A.Run(0.35, 0.12, Ease.InCubic, p => _bendyS.Scale = 1 - 0.2 * p, () =>
                {
                    _bendyS.ScaleX = _bendyS.ScaleY = 1;
                    _bendy.Width = 560;
                    _bendy.Height = 538;
                    _bendyS.E.RenderTransformOrigin = new Point(0.5, 0.5);
                });
                for (var i = 0; i < frames.Length; i++)
                {
                    var f = frames[i];
                    A.At(0.47 + i * 0.24, () =>
                    {
                        _bendy.Source = Look.Art(f) ?? _bendy.Source;
                        Sfx.Play("bloop", 0.3);
                    });
                }

                var runtime = LauncherSettings.Runtime;
                var game = _game;
                if (Capturing || _smoke)
                {
                    A.At(0.6, () => launched = true);
                }
                else
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            GameLaunch.Start(game, vr, runtime, s => Dispatcher.Invoke(() => _status.Text = s.ToUpperInvariant()));
                            launched = true;
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;
                            LauncherLog.Write("Launch failed: " + ex);
                        }
                    });
                }
            }
            else
            {
                _status.Text = "SO LONG, PAL!";
                _bendy.Source = Look.Art("bendy_pose_13") ?? _bendy.Source;
                launched = true;
            }

            // wait for the game process (and the loader animation) before the iris closes
            void WaitThenIris()
            {
                if (error != null)
                {
                    Reopen("COULDN'T START THE GAME", error);
                    return;
                }

                if (!launched || A.Now < 1.9 + _closeStart)
                {
                    A.At(0.1, WaitThenIris);
                    return;
                }

                IrisOut(launch);
            }

            _closeStart = A.Now;
            A.At(0.2, WaitThenIris);
        }

        private double _closeStart;

        private void IrisOut(bool launched)
        {
            Sfx.Play("whoosh", 0.8);
            Sfx.Play("runout", 0.6);
            _iris.Visibility = Visibility.Visible;
            // centre on Bendy's face (the loader art's face sits a little right of and below the image centre)
            _irisCenter = launched ? new Point(960 + 17, 540 + 6) : new Point(960, 520);
            var hold = launched ? 242 : 300;
            _irisR = 1500;
            A.Run(0, 0.85, Ease.InOutCubic, p => _irisR = 1500 + (hold - 1500) * p);
            A.Run(0, 1.2, Ease.Linear, p => Sfx.LoopVolume(0.3 * (1 - p)));
            A.Run(1.45, 0.38, Ease.InCubic, p => _irisR = hold * (1 - p), () =>
            {
                _irisR = 0;
                DrawIris();
                Sfx.StopLoop();
                EndCard(launched);
            });
        }

        private void DrawIris()
        {
            var r = _irisR;
            var all = new RectangleGeometry(new Rect(-50, -50, 2020, 1180));
            if (r <= 0.5)
            {
                _iris.Data = all;
                return;
            }

            // Bendy's head: a circle with a round bite out of the top (the horns either side)
            var head = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(_irisCenter, r, r),
                new EllipseGeometry(new Point(_irisCenter.X, _irisCenter.Y - r * 1.074), r * 0.513, r * 0.513));
            _iris.Data = new CombinedGeometry(GeometryCombineMode.Exclude, all, head);
        }

        private void EndCard(bool launched)
        {
            _menu.Visibility = Visibility.Hidden;
            _end.Visibility = Visibility.Visible;
            _endText.Text = launched ? "DREAMS COME TRUE" : "THE END";
            _endText.FontSize = launched ? 120 : 190;
            _endS.Opacity = 0;
            A.Run(0.15, 0.35, Ease.OutCubic, p =>
            {
                _endS.Opacity = p;
                _endS.Scale = 0.92 + 0.08 * p;
            });
            A.Run(launched ? 1.7 : 1.5, 0.45, Ease.Linear, p => _endS.Opacity = 1 - p, () =>
            {
                if (!Capturing && !_smoke) Application.Current.Shutdown();
            });
        }

        /// <summary>Launch failed: bring the menu back and explain.</summary>
        private void Reopen(string title, string message)
        {
            _irisR = -1;
            _iris.Visibility = Visibility.Hidden;
            _end.Visibility = Visibility.Hidden;
            _menu.Visibility = Visibility.Visible;
            _bendy.Source = Look.Art("bendy_pose_14");
            _bendy.Width = _bendy.Height = 560;
            _bendyS.E.RenderTransformOrigin = new Point(0.5, 0.95);
            A.Run(0, 0.5, Ease.OutCubic, p =>
            {
                for (var i = 0; i < _buttonS.Count; i++)
                {
                    _buttonS[i].X = -700 * (1 - p);
                    _buttonS[i].Opacity = p;
                }

                _logoS.Y = _stampS.Y = -520 * (1 - p);
                _subS.Opacity = _chromeS.Opacity = p;
                _circleGroupS.X = (960 - CircleX) * (1 - p);
                _circleGroupS.Y = (540 - CircleY) * (1 - p);
            }, () =>
            {
                _mode = Mode.Home;
                _selected = -1;
                Select(0, false);
                SetIdleStatus();
                ShowNote(title, message);
            });
            Sfx.StartLoop("projector", 0.3);
        }

        // ------------------------------------------------------------------ capture (developer aid)

        private void RunScriptDefaults()
        {
        }

        private void CaptureStep(DispatcherTimer timer)
        {
            var t = _captureFrame / _captureFps;
            Clock.Fixed = t;
            foreach (var s in _script.Where(s => s.T <= t && s.T > t - 1 / _captureFps - 1e-9).ToList())
                RunScript(s.Action);
            Frame(t);
            Root.UpdateLayout();

            var rtb = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                dc.DrawRectangle(new VisualBrush(Root), null, new Rect(0, 0, 1920, 1080));
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            Directory.CreateDirectory(_captureDir);
            using (var fs = File.Create(System.IO.Path.Combine(_captureDir, $"frame_{_captureFrame:0000}.png"))) enc.Save(fs);

            _captureFrame++;
            if (t >= _captureSeconds)
            {
                timer.Stop();
                Application.Current.Shutdown();
            }
        }

        /// <summary>Test aid: sends real mouse messages to this window at a stage point ("x,y" in 1920x1080 units).</summary>
        private void PostClick(string xy)
        {
            var parts = xy.Split(',');
            var stage = new Point(double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
            var dip = Root.TransformToAncestor(this).Transform(stage);
            var dev = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.Transform(dip);
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var lp = (IntPtr)(((int)dev.Y << 16) | ((int)dev.X & 0xFFFF));
            NativeMethods.PostMessage(hwnd, 0x0200, IntPtr.Zero, lp);       // WM_MOUSEMOVE
            NativeMethods.PostMessage(hwnd, 0x0201, (IntPtr)1, lp);         // WM_LBUTTONDOWN
            NativeMethods.PostMessage(hwnd, 0x0202, IntPtr.Zero, lp);       // WM_LBUTTONUP
            LauncherLog.Write($"test click at stage {stage} -> client {dev}");
        }

        /// <summary>
        /// Test aid: drives WPF's own mouse-down/up events through an element ("menu:N", "back", "chrome:N", "bg"),
        /// the same route a real click takes once the system has found the element under the cursor.
        /// </summary>
        private void PressButton(string target)
        {
            FrameworkElement el;
            if (target.StartsWith("menu:")) el = _buttons[int.Parse(target.Substring(5))];
            else if (target == "back") el = FindBack(_pageLayer);
            else if (target.StartsWith("chrome:")) el = (FrameworkElement)_chrome.Children[int.Parse(target.Substring(7))];
            else el = _bg;
            if (el == null)
            {
                LauncherLog.Write("test: no element for " + target);
                return;
            }

            Look.TestIgnorePosition = true;
            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseDownEvent };
            el.RaiseEvent(down);
            var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseUpEvent };
            el.RaiseEvent(up);
            Look.TestIgnorePosition = false;
            LauncherLog.Write($"test: pressed {target} (down handled={down.Handled}, up handled={up.Handled})");
        }

        private static FrameworkElement FindBack(DependencyObject root)
        {
            if (root == null) return null;
            if (root is InkButton b && b.Label == "BACK") return b;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var f = FindBack(VisualTreeHelper.GetChild(root, i));
                if (f != null) return f;
            }

            return null;
        }

        private void RunScript(string action)
        {
            if (action == "skip") SkipIntro();
            else if (action == "nointro") _skipRequested = true;
            else if (action.StartsWith("page:")) OpenPage(action.Substring(5));
            else if (action == "back") ClosePage();
            else if (action == "play") BeginClose(true, true);
            else if (action == "quit") BeginClose(false, false);
            else if (action.StartsWith("select:")) Select(int.Parse(action.Substring(7)), false);
            else if (action.StartsWith("click:")) PostClick(action.Substring(6));
            else if (action.StartsWith("press:")) PressButton(action.Substring(6));
            else if (action == "note") ShowNote("VIRTUAL DESKTOP ISN'T RUNNING", "Start the Virtual Desktop Streamer on this PC and connect from your Quest 3, then press PLAY IN VR.", "PLAY ANYWAY", () => { });
        }
    }
}
