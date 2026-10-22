using System;
using System.Globalization;
using System.IO;
using System.Windows;

namespace InkVR.Launcher
{
    public partial class App : Application
    {
        private static System.Threading.Mutex _single;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var args = e.Args;
            var quiet = args.Length > 0 && args[0].StartsWith("--") && args[0] != "--play";
            DispatcherUnhandledException += (s, ex) =>
            {
                Game.LauncherLog.Write("Unhandled: " + ex.Exception);
                if (!quiet) MessageBox.Show(ex.Exception.Message, "InkVR Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            // Developer aid: "InkVR Launcher.exe --export-art <folder>" writes the extracted artwork as PNGs.
            if (args.Length >= 2 && args[0] == "--export-art")
            {
                try
                {
                    Game.ArtLibrary.Export(Game.GameLocator.Find(), args[1]);
                }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(args[1]);
                    File.WriteAllText(Path.Combine(args[1], "error.txt"), ex.ToString());
                }

                Shutdown();
                return;
            }

            // Developer aid: "--capture <folder> <fps> <seconds> [script]" renders the animation to numbered PNGs.
            // script: "time=action;..." with actions skip, nointro, page:controls|check|options|about, back, play, quit, select:N, note
            if (args.Length >= 4 && args[0] == "--capture")
            {
                var w = new MainWindow(args[1], double.Parse(args[2], CultureInfo.InvariantCulture), double.Parse(args[3], CultureInfo.InvariantCulture), args.Length >= 5 ? args[4] : null);
                MainWindow = w;
                w.Show();
                return;
            }

            // Developer aid: "--uninstall-from <game folder>" (results in launcher.log).
            if (args.Length >= 2 && args[0] == "--uninstall-from")
            {
                var g = new Game.GameInstall { Dir = args[1] };
                Game.ModInstaller.Uninstall(g);
                Game.LauncherLog.Write($"uninstall check: bepinex={Game.ModInstaller.BepInExInstalled(g)} provider={Game.ModInstaller.ProviderInstalled(g)} inkvr={Game.ModInstaller.InstalledVersion(g)}");
                Shutdown();
                return;
            }

            // Developer aid: "--install-to <game folder>" runs the installer against a folder (results in launcher.log).
            if (args.Length >= 2 && args[0] == "--install-to")
            {
                try
                {
                    var g = new Game.GameInstall { Dir = args[1] };
                    Game.ModInstaller.Install(g, s => Game.LauncherLog.Write("install: " + s));
                    Game.LauncherLog.Write($"install check: bepinex={Game.ModInstaller.BepInExInstalled(g)} provider={Game.ModInstaller.ProviderInstalled(g)} inkvr={Game.ModInstaller.InstalledVersion(g)} needs={Game.ModInstaller.NeedsInstall(g)}");
                }
                catch (Exception ex)
                {
                    Game.LauncherLog.Write("install failed: " + ex);
                }

                Shutdown();
                return;
            }

            // "--play vr" / "--play flat": start the game straight away with the saved options (for shortcuts).
            if (args.Length >= 2 && args[0] == "--play")
            {
                var game = Game.GameLocator.Find();
                if (game == null || Game.ModInstaller.NeedsInstall(game))
                {
                    MessageBox.Show("Open InkVR Launcher normally first: the game or the mod needs setting up (see VR CHECK).", "InkVR Launcher");
                }
                else
                {
                    try
                    {
                        Game.GameLaunch.Start(game, args[1] != "flat", Game.LauncherSettings.Runtime, null);
                    }
                    catch (Exception ex)
                    {
                        Game.LauncherLog.Write("Quick launch failed: " + ex);
                        MessageBox.Show(ex.Message, "InkVR Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                Shutdown();
                return;
            }

            // Developer aid: "--smoke <seconds> [script]" runs the real launcher off-screen and muted, then quits.
            if (args.Length >= 2 && args[0] == "--smoke")
            {
                var w = new MainWindow(null, 0, double.Parse(args[1], CultureInfo.InvariantCulture), args.Length >= 3 ? args[2] : null, smoke: true);
                MainWindow = w;
                w.Show();
                return;
            }

            // One launcher at a time: a second start just brings the first one forward.
            _single = new System.Threading.Mutex(true, "InkVR.Launcher.SingleInstance", out var first);
            if (!first)
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(System.Diagnostics.Process.GetCurrentProcess().ProcessName))
                    if (p.Id != System.Diagnostics.Process.GetCurrentProcess().Id && p.MainWindowHandle != IntPtr.Zero)
                        NativeMethods.SetForegroundWindow(p.MainWindowHandle);
                Shutdown();
                return;
            }

            var main = new MainWindow();
            MainWindow = main;
            main.Show();
        }
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
