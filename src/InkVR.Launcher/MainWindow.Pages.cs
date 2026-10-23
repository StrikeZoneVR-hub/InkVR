using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using InkVR.Launcher.Effects;
using InkVR.Launcher.Game;
using InkVR.Launcher.Ui;

namespace InkVR.Launcher
{
    /// <summary>The paper sheets that slide over the menu: CONTROLS, VR CHECK, OPTIONS, ABOUT, and notes.</summary>
    public partial class MainWindow
    {
        private Canvas _page;
        private Sprite _pageS;
        private Canvas _note;
        private Sprite _noteS;

        private const double SheetX = 110, SheetY = 22, SheetW = 1700, SheetH = 1036;

        // ------------------------------------------------------------------ sheet plumbing

        private void OpenPage(string name)
        {
            LauncherLog.Write("page: " + name);
            if (_page != null) ClosePage(true);
            var root = new Canvas { Width = 1920, Height = 1080, Background = Brushes.Transparent };
            var sheet = new Canvas { Width = SheetW, Height = SheetH };
            if (Look.Art("menu_page") != null) sheet.Children.Add(Look.Img("menu_page", SheetW, SheetH));
            else if (Look.Art("Options Background") != null) sheet.Children.Add(Look.Img("Options Background", SheetW, SheetH));
            else sheet.Children.Add(new Rectangle { Width = SheetW, Height = SheetH, Fill = Paint.Brush(Paint.Paper), RadiusX = 30, RadiusY = 30 });
            root.Children.Add(sheet.At(SheetX, SheetY));

            string title;
            switch (name)
            {
                case "controls":
                    title = "QUEST 3 CONTROLS";
                    BuildControls(sheet);
                    break;
                case "check":
                    title = "VR CHECK";
                    BuildCheck(sheet);
                    break;
                case "options":
                    title = "OPTIONS";
                    BuildOptions(sheet);
                    break;
                default:
                    title = "ABOUT INKVR";
                    BuildAbout(sheet);
                    break;
            }

            var heading = Look.Text(title, 84, Paint.Ink, null, TextAlignment.Center);
            heading.Width = SheetW;
            sheet.Children.Add(heading.At(0, 44));

            var back = new InkButton("BACK", 52);
            back.Click += () => ClosePage();
            back.Hovered += b => b.Lit = true;
            back.MouseLeave += (s, e) => back.Lit = false;
            sheet.Children.Add(back.At(120, SheetH - 128));

            _page = root;
            _pageS = new Sprite(sheet, 0.5, 1);
            _markerS.Opacity = 0;
            _pageLayer.Children.Add(root);
            Sfx.Play("paper", 0.8);
            A.Run(0, 0.25, Ease.Linear, p => _dimTarget(0.45 * p));
            _pageS.Y = 1150;
            _pageS.Rot = 6;
            A.Run(0, 0.5, Ease.OutCubic, p =>
            {
                _pageS.Y = 1150 * (1 - p);
                _pageS.Rot = 6 - 6.6 * p;
            });
        }

        private void _dimTarget(double v) => _dim.Opacity = v;

        private void ClosePage(bool instant = false)
        {
            if (_page == null) return;
            LauncherLog.Write("page closed");
            var page = _page;
            var s = _pageS;
            _page = null;
            if (instant)
            {
                _pageLayer.Children.Remove(page);
                _dim.Opacity = 0;
                return;
            }

            Sfx.Play("paper", 0.6);
            var startDim = _dim.Opacity;
            A.Run(0, 0.32, Ease.InCubic, p =>
            {
                s.Y = 1150 * p;
                s.Rot = -0.6 - 5 * p;
                _dim.Opacity = startDim * (1 - p);
            }, () =>
            {
                _pageLayer.Children.Remove(page);
                if (_mode == Mode.Home && _selected >= 0) _markerS.Opacity = 1;
            });
        }

        private void ShowNote(string title, string text, string action = null, Action onAction = null)
        {
            CloseNote(true);
            var root = new Canvas { Width = 1920, Height = 1080, Background = Paint.Brush(Colors.Black, 0.45) };
            const double w = 1060, h = 620;
            var card = new Canvas { Width = w, Height = h };
            if (Look.Art("menu_page") != null) card.Children.Add(Look.Img("menu_page", w, h));
            else card.Children.Add(new Rectangle { Width = w, Height = h, Fill = Paint.Brush(Paint.Paper), RadiusX = 24, RadiusY = 24 });
            var t = Look.Text(title, 64, Paint.Ink, null, TextAlignment.Center);
            t.Width = w;
            card.Children.Add(t.At(0, 70));
            card.Children.Add(Look.MemoText(text, 30, Paint.Ink, w - 220).At(110, 180));

            var x = w - 150.0;
            var ok = new InkButton(action == null ? "OK" : "BACK", 52);
            ok.Click += () => CloseNote();
            ok.Hovered += b => b.Lit = true;
            ok.MouseLeave += (s, e) => ok.Lit = false;
            card.Children.Add(ok.At(action == null ? x - ok.TextWidth : 130, h - 140));
            if (action != null)
            {
                var go = new InkButton(action, 52);
                go.Click += () =>
                {
                    CloseNote(true);
                    onAction?.Invoke();
                };
                go.Hovered += b => b.Lit = true;
                go.MouseLeave += (s, e) => go.Lit = false;
                card.Children.Add(go.At(x - go.TextWidth, h - 140));
            }

            root.Children.Add(card.At((1920 - w) / 2, (1080 - h) / 2));
            _note = root;
            _noteS = new Sprite(card);
            (_pageLayer ?? (Panel)Root).Children.Add(root);
            Sfx.Play("paper", 0.6);
            _noteS.Scale = 0.6;
            _noteS.Rot = -8;
            A.Run(0, 0.4, Ease.OutBack, p =>
            {
                _noteS.Scale = 0.6 + 0.4 * p;
                _noteS.Rot = -8 + 7 * p;
            });
        }

        private void CloseNote(bool instant = false)
        {
            if (_note == null) return;
            var note = _note;
            var s = _noteS;
            _note = null;
            var host = (Panel)note.Parent;
            if (instant)
            {
                host?.Children.Remove(note);
                return;
            }

            A.Run(0, 0.2, Ease.InCubic, p =>
            {
                s.Scale = 1 - 0.3 * p;
                note.Opacity = 1 - p;
            }, () => host?.Children.Remove(note));
        }

        private static InkButton SmallButton(string label, Action click, double size = 42)
        {
            var b = new InkButton(label, size);
            b.Click += () =>
            {
                Sfx.Play("bloop", 0.5);
                click();
            };
            b.Hovered += x => x.Lit = true;
            b.MouseLeave += (s, e) => b.Lit = false;
            return b;
        }

        // ------------------------------------------------------------------ CONTROLS

        private void BuildControls(Canvas sheet)
        {
            var leftHanded = _cfg != null && _cfg.GetBool("Controls", "LeftHanded", false);
            var snap = _cfg == null || _cfg.GetBool("Comfort", "SnapTurn", true);
            var angle = _cfg?.GetFloat("Comfort", "SnapTurnAngle", 45) ?? 45;
            var turn = snap ? $"SNAP TURN {angle:0}°" : "SMOOTH TURN";

            // Sheet coordinates. Controllers sit side by side; labels hang off their outer sides.
            const double lx = 430, rx = 930, cy = 165;
            sheet.Children.Add(QuestArt.Draw(true).At(lx, cy));
            sheet.Children.Add(QuestArt.Draw(false).At(rx, cy));

            // (action, detail) for a part, given which hand holds the controller
            (string, string) Role(bool leftController, QuestPart part)
            {
                var dominant = leftController == leftHanded;
                switch (part)
                {
                    case QuestPart.Trigger: return dominant ? ("WEAPON", "OR SWING YOUR ARM TO HIT") : ("RUN", null);
                    case QuestPart.Stick: return dominant ? (turn, snap ? "OR SMOOTH TURN IN OPTIONS" : "OR SNAP TURN IN OPTIONS") : ("MOVE", "CLICK IT TO RUN");
                    case QuestPart.Primary: return dominant ? ("JUMP", null) : ("INTERACT", null);
                    case QuestPart.Secondary: return dominant ? ("BACK", "IN MENUS") : (null, null);
                    case QuestPart.Menu: return ("PAUSE", null);
                    case QuestPart.Grip: return ("INTERACT", "EITHER HAND");
                    default: return (null, null);
                }
            }

            string PartName(bool leftController, QuestPart part)
            {
                switch (part)
                {
                    case QuestPart.Trigger: return "TRIGGER";
                    case QuestPart.Stick: return "THUMBSTICK";
                    case QuestPart.Primary: return leftController ? "X BUTTON" : "A BUTTON";
                    case QuestPart.Secondary: return leftController ? "Y BUTTON" : "B BUTTON";
                    case QuestPart.Menu: return "MENU BUTTON";
                    case QuestPart.Grip: return "GRIP";
                    default: return "";
                }
            }

            var lines = new Canvas();
            sheet.Children.Add(lines);

            void Column(bool leftController, QuestPart[] parts)
            {
                var used = parts.Where(p => Role(leftController, p).Item1 != null).ToArray();
                var spacing = used.Length > 5 ? 96 : 112;
                var labelY = 118.0;
                const double colW = 340;
                var ox = leftController ? lx : rx;
                foreach (var part in used)
                {
                    var (action, detail) = Role(leftController, part);
                    var align = leftController ? TextAlignment.Right : TextAlignment.Left;
                    var tx = leftController ? lx - 50 - colW : rx + QuestArt.W + 50;
                    var name = Look.Text(PartName(leftController, part), 28, Paint.Ink, null, align, 0.6);
                    var act = Look.Text(action, 46, Paint.Ink, null, align);
                    name.Width = act.Width = colW;
                    sheet.Children.Add(name.At(tx, labelY));
                    sheet.Children.Add(act.At(tx, labelY + 24));
                    if (detail != null)
                    {
                        var d = Look.Text(detail, 26, Paint.Ink, null, align, 0.75);
                        d.Width = colW;
                        sheet.Children.Add(d.At(tx, labelY + 72));
                    }

                    var pts = new PointCollection { new Point(leftController ? lx - 36 : rx + QuestArt.W + 36, labelY + 52) };
                    foreach (var p in QuestArt.Route(leftController, part)) pts.Add(new Point(ox + p.X, cy + p.Y));
                    // stop at the button's rim so the line never covers its letter
                    var rim = part == QuestPart.Primary || part == QuestPart.Secondary ? 28 : part == QuestPart.Stick ? 32 : part == QuestPart.Menu ? 20 : 0;
                    if (rim > 0)
                    {
                        var a = pts[pts.Count - 2];
                        var b = pts[pts.Count - 1];
                        var v = b - a;
                        v.Normalize();
                        pts[pts.Count - 1] = b - v * rim;
                    }

                    lines.Children.Add(new Polyline { Points = pts, Stroke = Paint.Brush(Paint.Ink), StrokeThickness = 3.5, StrokeLineJoin = PenLineJoin.Round });
                    var end = pts[pts.Count - 1];
                    lines.Children.Add(new Ellipse { Width = 11, Height = 11, Fill = Paint.Brush(Paint.Ink) }.At(end.X - 5.5, end.Y - 5.5));
                    labelY += spacing;
                }
            }

            // ordered top to bottom by where each leader enters the controller
            Column(true, new[] { QuestPart.Trigger, QuestPart.Secondary, QuestPart.Stick, QuestPart.Menu, QuestPart.Primary, QuestPart.Grip });
            Column(false, new[] { QuestPart.Trigger, QuestPart.Secondary, QuestPart.Stick, QuestPart.Primary, QuestPart.Grip });

            var hand = Look.Text("LEFT HAND", 46, Paint.Ink, null, TextAlignment.Center);
            hand.Width = QuestArt.W;
            sheet.Children.Add(hand.At(lx, cy + QuestArt.H + 6));
            var hand2 = Look.Text("RIGHT HAND", 46, Paint.Ink, null, TextAlignment.Center);
            hand2.Width = QuestArt.W;
            sheet.Children.Add(hand2.At(rx, cy + QuestArt.H + 6));

            var notes = string.Join("\n", new[]
            {
                "LOOK AROUND WITH YOUR HEAD. TURNING (SNAP OR SMOOTH) AND WORLD SIZE ARE IN OPTIONS.",
                "MENUS: THUMBSTICK TO MOVE, A TO SELECT, B TO GO BACK (A LASER POINTER IS IN OPTIONS).",
                "SWING THE AXE WITH YOUR ARM TO HIT. GUNS FIRE WHERE YOUR HAND POINTS. HOLD A HAND OVER THINGS TO USE THEM.",
            });
            var memo = Look.MemoText(notes, 26, Paint.Ink, 1180, 0.9);
            memo.TextAlignment = TextAlignment.Center;
            sheet.Children.Add(memo.At((SheetW - 1180) / 2, 760));
        }

        // ------------------------------------------------------------------ VR CHECK

        private void BuildCheck(Canvas sheet)
        {
            var list = new Canvas();
            sheet.Children.Add(list.At(0, 0));
            var busy = Look.Text("CHECKING...", 44, Paint.Ink, null, TextAlignment.Center, 0.6);
            busy.Width = SheetW;
            list.Children.Add(busy.At(0, 420));

            void Fill(List<Check> checks)
            {
                list.Children.Clear();
                for (var i = 0; i < checks.Count; i++)
                {
                    var c = checks[i];
                    var col = i < 6 ? 0 : 1;
                    var row = col == 0 ? i : i - 6;
                    var x = 150 + col * 720.0;
                    var y = 175 + row * 112.0;
                    list.Children.Add(StatusMark(c.Level).At(x, y + 4));
                    list.Children.Add(Look.Text(c.Title.ToUpperInvariant(), 38, Paint.Ink).At(x + 74, y - 4));
                    list.Children.Add(Look.MemoText(c.Detail, 21, Paint.Ink, 600, 0.9).At(x + 76, y + 42));
                }
            }

            void Refresh()
            {
                var game = _game;
                Task.Run(() => Diagnostics.Run(game)).ContinueWith(t => Dispatcher.Invoke(() =>
                {
                    if (t.Exception != null) LauncherLog.Write("Diagnostics failed: " + t.Exception);
                    else Fill(t.Result);
                }));
            }

            var x0 = 420.0;
            foreach (var (label, act) in new (string, Action)[]
                     {
                         ("INSTALL / REPAIR", () => RunInstall(Refresh)),
                         ("GAME FOLDER", () => { if (_game != null) GameLaunch.OpenFolder(_game.Dir); }),
                         ("GAME LOG", () => { if (_game != null) GameLaunch.OpenFile(_game.LogFile); }),
                         ("CHECK AGAIN", Refresh),
                     })
            {
                var b = SmallButton(label, act);
                sheet.Children.Add(b.At(x0, SheetH - 124));
                x0 += b.TextWidth + 70;
            }

            Refresh();
        }

        private static FrameworkElement StatusMark(CheckLevel level)
        {
            var color = level == CheckLevel.Ok ? Paint.OkGreen : level == CheckLevel.Warn ? Paint.WarnAmber : level == CheckLevel.Fail ? Paint.StampRed : Paint.Ink;
            var g = new Grid { Width = 56, Height = 56 };
            g.Children.Add(new Ellipse { Stroke = Paint.Brush(color), StrokeThickness = 5 });
            var mark = new Path { Stroke = Paint.Brush(color), StrokeThickness = 7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            switch (level)
            {
                case CheckLevel.Ok:
                    mark.Data = Geometry.Parse("M 15,29 L 24,38 L 41,18");
                    break;
                case CheckLevel.Fail:
                    mark.Data = Geometry.Parse("M 18,18 L 38,38 M 38,18 L 18,38");
                    break;
                case CheckLevel.Warn:
                    mark.Data = Geometry.Parse("M 28,14 L 28,32 M 28,41 L 28,42");
                    break;
                default:
                    mark.Data = Geometry.Parse("M 28,25 L 28,40 M 28,15 L 28,16");
                    break;
            }

            g.Children.Add(mark);
            g.OpacityMask = new ImageBrush(Paint.StampWear(56, 56, (int)level + 3));
            return g;
        }

        private void RunInstall(Action after)
        {
            if (_game == null)
            {
                LocateGame();
                return;
            }

            if (GameLaunch.GameRunning())
            {
                ShowNote("CLOSE THE GAME FIRST", "Bendy and the Ink Machine is running. Close it, then install or repair InkVR.");
                return;
            }

            var game = _game;
            ShowNote("INSTALLING INKVR", "Setting things up in your game folder. This only takes a moment (the OpenXR plugin is downloaded from Unity the first time).");
            Task.Run(() => ModInstaller.Install(game, s => Dispatcher.Invoke(() => _status.Text = s.ToUpperInvariant()))).ContinueWith(t => Dispatcher.Invoke(() =>
            {
                CloseNote(true);
                SetIdleStatus();
                if (t.Exception != null)
                {
                    var msg = t.Exception.GetBaseException().Message;
                    LauncherLog.Write("Install failed: " + t.Exception);
                    ShowNote("INSTALL FAILED", msg);
                    return;
                }

                after?.Invoke();
            }));
        }

        private void LocateGame()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Find Bendy and the Ink Machine.exe",
                Filter = "Bendy and the Ink Machine|" + GameInstall.ExeName,
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(this) != true) return;
            GameLocator.Remember(System.IO.Path.GetDirectoryName(dlg.FileName));
            _game = GameLocator.Find();
            _cfg = _game != null ? new ModSettingsFile(_game.ConfigFile) : null;
            SetIdleStatus();
        }

        // ------------------------------------------------------------------ OPTIONS

        private void BuildOptions(Canvas sheet)
        {
            var cfg = _cfg;
            var y = 180.0;
            const double rowH = 49;
            var col = 0;

            void Header(string text, int c, double hy = 160)
            {
                var h = Look.Text(text, 44, Paint.StampRed);
                sheet.Children.Add(h.At(150 + c * 760, hy));
            }

            void Row(string label, string[] values, int index, Action<int> changed, bool enabled = true)
            {
                var x = 150 + col * 760.0;
                var rowY = y;
                y += rowH;
                sheet.Children.Add(Look.Text(label, 33, Paint.Ink, null, TextAlignment.Left, enabled ? 1 : 0.4).At(x, rowY));
                var valueText = Look.Text(values[Math.Max(0, Math.Min(values.Length - 1, index))], 33, Paint.Ink, null, TextAlignment.Center, enabled ? 1 : 0.4);
                valueText.Width = 320;
                sheet.Children.Add(valueText.At(x + 340, rowY));
                var i = index;
                if (!enabled) return;

                TextBlock Arrow(string glyph, double ax, int dir)
                {
                    var a = Look.Text(glyph, 40, Paint.Ink);
                    a.Cursor = System.Windows.Input.Cursors.Hand;
                    a.Background = Brushes.Transparent;
                    a.Padding = new Thickness(8, 0, 8, 0);
                    a.MouseEnter += (s, e) => a.Foreground = Paint.Brush(Paint.StampRed);
                    a.MouseLeave += (s, e) => a.Foreground = Paint.Brush(Paint.Ink);
                    a.OnClick(() =>
                    {
                        i = (i + dir + values.Length) % values.Length;
                        valueText.Text = values[i];
                        Sfx.Play("hover", 0.5);
                        changed(i);
                        SetIdleStatus();
                    });
                    sheet.Children.Add(a.At(ax, rowY - 3));
                    return a;
                }

                Arrow("<", x + 330, -1);
                Arrow(">", x + 650, +1);
            }

            Header("IN VR", 0);
            y = 222;

            var runtimes = GameLocator.OpenXRRuntimes();
            var active = GameLocator.ActiveRuntimeJson();
            var names = new List<string> { "PC DEFAULT (" + GameLocator.FriendlyRuntime(active).ToUpperInvariant() + ")" };
            var jsons = new List<string> { "" };
            foreach (var r in runtimes)
            {
                names.Add(r.Name.ToUpperInvariant());
                jsons.Add(r.Json);
            }

            var current = Math.Max(0, jsons.FindIndex(j => string.Equals(j, LauncherSettings.Runtime, StringComparison.OrdinalIgnoreCase)));
            Row("VR RUNTIME", names.Select(n => n.Length > 22 ? n.Substring(0, 21) + "…" : n).ToArray(), current, i => LauncherSettings.Runtime = jsons[i]);

            var has = cfg != null;
            Row("TURNING", new[] { "SNAP", "SMOOTH" }, has && !cfg.GetBool("Comfort", "SnapTurn", true) ? 1 : 0, i => cfg.Set("Comfort", "SnapTurn", i == 0), has);
            var angles = new[] { 30f, 45f, 60f, 90f };
            Row("SNAP ANGLE", angles.Select(a => a + "°").ToArray(), Nearest(angles, cfg?.GetFloat("Comfort", "SnapTurnAngle", 45) ?? 45), i => cfg.Set("Comfort", "SnapTurnAngle", angles[i]), has);
            var speeds = new[] { 60f, 90f, 120f, 150f, 180f, 240f };
            Row("SMOOTH TURN SPEED", speeds.Select(s => s + "°/S").ToArray(), Nearest(speeds, cfg?.GetFloat("Comfort", "SmoothTurnSpeed", 120) ?? 120), i => cfg.Set("Comfort", "SmoothTurnSpeed", speeds[i]), has);
            var worlds = new[] { 4.21f, 3.93f, 3.69f, 3.47f, 3.28f, 3.11f, 2.95f, 2.81f, 2.68f, 2.57f, 2.46f };
            var auto = !has || cfg.GetBool("VR", "AutoWorldScale", true);
            Row("WORLD SIZE", new[] { "AUTO (YOUR HEIGHT)" }.Concat(worlds.Select(w => Math.Round(2.95f / w * 100f) + "%")).ToArray(),
                auto ? 0 : 1 + Nearest(worlds, cfg?.GetFloat("VR", "WorldScale", 2.95f) ?? 2.95f), i =>
                {
                    cfg.Set("VR", "AutoWorldScale", i == 0);
                    if (i > 0) cfg.Set("VR", "WorldScale", worlds[i - 1]);
                }, has);
            Row("MAIN HAND", new[] { "RIGHT", "LEFT" }, has && cfg.GetBool("Controls", "LeftHanded", false) ? 1 : 0, i => cfg.Set("Controls", "LeftHanded", i == 1), has);
            Row("INTERACT", new[] { "HAND HOVER", "LOOK AT IT" }, has && !cfg.GetBool("Controls", "InteractWithHand", true) ? 1 : 0, i => cfg.Set("Controls", "InteractWithHand", i == 0), has);
            var heights = Enumerable.Range(-12, 25).Select(k => k * 0.05f).ToArray();
            Row("HEIGHT", heights.Select(h => h == 0 ? "AS TRACKED" : (h > 0 ? "UP " : "DOWN ") + Math.Abs(Math.Round(h * 100)) + " CM").ToArray(),
                Nearest(heights, cfg?.GetFloat("VR", "HeightOffset", 0) ?? 0), i => cfg.Set("VR", "HeightOffset", heights[i]), has);
            var scales = new[] { 0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f };
            Row("RENDER SCALE", scales.Select(s => Math.Round(s * 100) + "%").ToArray(), Nearest(scales, cfg?.GetFloat("Graphics", "RenderScale", 1) ?? 1), i => cfg.Set("Graphics", "RenderScale", scales[i]), has);
            Row("AUTO RESOLUTION", new[] { "ON", "OFF" }, has && !cfg.GetBool("Graphics", "DynamicResolution", true) ? 1 : 0, i => cfg.Set("Graphics", "DynamicResolution", i == 0), has);
            Row("LIGHT BEAMS", new[] { "ON", "OFF" }, has && !cfg.GetBool("Graphics", "VolumetricLightInVR", true) ? 1 : 0, i => cfg.Set("Graphics", "VolumetricLightInVR", i == 0), has);
            Row("INK ON HANDS", new[] { "ON", "OFF" }, has && !cfg.GetBool("Body", "InkOnHands", true) ? 1 : 0, i => cfg.Set("Body", "InkOnHands", i == 0), has);
            Row("LASER POINTER", new[] { "OFF", "ON" }, has && cfg.GetBool("Controls", "LaserPointer", false) ? 1 : 0, i => cfg.Set("Controls", "LaserPointer", i == 1), has);
            Row("REDUCE FLASHES", new[] { "OFF", "ON" }, _reduce ? 1 : 0, i =>
            {
                cfg.Set("Comfort", "ReduceFlashes", i == 1);
                _reduce = i == 1;
                _grain.Opacity = _reduce ? 0.45 : 0.8;
            }, has);

            col = 1;
            Header("LAUNCHER", 1);
            y = 230;
            Row("OPENING ANIMATION", new[] { "ON", "OFF" }, LauncherSettings.PlayIntro ? 0 : 1, i => LauncherSettings.PlayIntro = i == 0);
            Row("SOUNDS", new[] { "ON", "OFF" }, LauncherSettings.Sounds ? 0 : 1, i =>
            {
                LauncherSettings.Sounds = i == 0;
                Sfx.Muted = i != 0;
                if (Sfx.Muted) Sfx.StopLoop();
                else Sfx.StartLoop("projector", 0.3);
            });
            Row("START FULLSCREEN", new[] { "OFF", "ON" }, LauncherSettings.Fullscreen ? 1 : 0, i =>
            {
                LauncherSettings.Fullscreen = i == 1;
                WindowState = i == 1 ? WindowState.Maximized : WindowState.Normal;
            });

            Header("HENRY", 1, 410);
            y = 480;
            var bodies = new[] { "Full", "Arms", "Hands", "None" };
            Row("BODY", new[] { "FULL BODY", "ARMS & HANDS", "HANDS ONLY", "NONE" }, Math.Max(0, Array.IndexOf(bodies, cfg?.Get("Body", "Mode") ?? "Full")), i => cfg.Set("Body", "Mode", bodies[i]), has);
            var styles = new[] { "Lifelike", "Toon" };
            Row("STYLE", new[] { "LIFELIKE", "TOON" }, Math.Max(0, Array.IndexOf(styles, cfg?.Get("Body", "Style") ?? "Lifelike")), i => cfg.Set("Body", "Style", styles[i]), has);
            Row("MELEE", new[] { "SWING YOUR ARM", "BUTTON ONLY" }, has && !cfg.GetBool("Controls", "PhysicalMelee", true) ? 1 : 0, i => cfg.Set("Controls", "PhysicalMelee", i == 0), has);
            Row("GUN AIM", new[] { "HAND", "HEAD" }, has && !cfg.GetBool("Controls", "AimGunsWithHand", true) ? 1 : 0, i => cfg.Set("Controls", "AimGunsWithHand", i == 0), has);

            var memo = Look.MemoText(has
                ? "The IN VR settings are the same ones as VR SETTINGS in the game's Options menu. Changes here apply the next time the game starts."
                : "Find the game first (VR CHECK) to change the IN VR settings.", 22, Paint.Ink, 640, 0.85);
            sheet.Children.Add(memo.At(910, 700));

            var x0 = 910.0;
            var locate = SmallButton("LOCATE GAME", LocateGame, 38);
            sheet.Children.Add(locate.At(x0, 830));
            if (_game != null && ModInstaller.InstalledVersion(_game) != null)
            {
                var uninstall = SmallButton("UNINSTALL INKVR", () => ShowNote("UNINSTALL INKVR?",
                    "This removes InkVR, the OpenXR plugin and BepInEx's loader from the game folder. The game itself is not touched, and you can reinstall from VR CHECK.",
                    "UNINSTALL", () =>
                    {
                        try
                        {
                            ModInstaller.Uninstall(_game);
                            SetIdleStatus();
                            ShowNote("INKVR REMOVED", "The game will now start without VR. Use INSTALL / REPAIR in VR CHECK to put InkVR back.");
                        }
                        catch (Exception ex)
                        {
                            ShowNote("COULDN'T UNINSTALL", ex.Message);
                        }
                    }), 38);
                sheet.Children.Add(uninstall.At(x0 + locate.TextWidth + 70, 830));
            }
        }

        private static int Nearest(float[] values, float v)
        {
            var best = 0;
            for (var i = 1; i < values.Length; i++)
                if (Math.Abs(values[i] - v) < Math.Abs(values[best] - v))
                    best = i;
            return best;
        }

        // ------------------------------------------------------------------ ABOUT

        private void BuildAbout(Canvas sheet)
        {
            var bendy = Look.Img("bendy_pose_02", 520, 520);
            sheet.Children.Add(bendy.At(130, 230));

            var x = 700.0;
            sheet.Children.Add(Look.Text("INKVR  ·  VR MOD MADE BY STRIKEZONEVR", 50, Paint.Ink).At(x, 180));
            sheet.Children.Add(Look.Text("VERSION " + Version + "  ·  TEST BUILD", 34, Paint.StampRed).At(x, 246));
            var text = string.Join("\n\n", new[]
            {
                "A fan-made VR mod for Bendy and the Ink Machine: play every chapter in a Quest 3 (or any OpenXR headset) with Henry's own hands, and watch the menus on the big screen of a 1930s picture palace.",
                "Unofficial. Not made, endorsed or supported by Joey Drew Studios Inc. The Bendy artwork you see here is read from your own copy of the game and never leaves your PC.",
                "Built with BepInEx (LGPL 2.1), HarmonyX (MIT), Il2CppInterop (LGPL 3.0) and Unity's OpenXR plugin (downloaded from Unity). Lettering: Bangers by Vernon Adams (SIL Open Font License). Texture decoding after bcdec by Sergii Kudlai (MIT).",
            });
            sheet.Children.Add(Look.MemoText(text, 24, Paint.Ink, 880, 0.92).At(x, 320));
        }
    }
}
