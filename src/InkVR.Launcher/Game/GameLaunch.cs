using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace InkVR.Launcher.Game
{
    public static class GameLaunch
    {
        public static bool GameRunning() => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameInstall.ExeName)).Length > 0;
        public static bool SteamRunning() => Process.GetProcessesByName("steam").Length > 0;

        /// <summary>
        /// Starts the game directly (not through Steam's launch options) so the chosen OpenXR runtime and
        /// play mode reach it as environment variables. Steam still has to be running for the game's Steamworks.
        /// </summary>
        public static Process Start(GameInstall game, bool vr, string runtimeJson, Action<string> status)
        {
            if (!File.Exists(game.Exe)) throw new FileNotFoundException("Game executable not found", game.Exe);

            // Lets the exe start without Steam relaunching it through the Steam client (and dropping our environment).
            var appId = Path.Combine(game.Dir, "steam_appid.txt");
            if (!File.Exists(appId)) File.WriteAllText(appId, GameInstall.SteamAppId.ToString());

            if (!SteamRunning())
            {
                var root = GameLocator.SteamRoot();
                var steamExe = root == null ? null : Path.Combine(root, "steam.exe");
                if (steamExe != null && File.Exists(steamExe))
                {
                    status?.Invoke("Waking up Steam...");
                    Process.Start(new ProcessStartInfo(steamExe, "-silent") { UseShellExecute = true });
                    for (var i = 0; i < 40 && !SteamRunning(); i++) Thread.Sleep(500);
                    Thread.Sleep(4000); // let it sign in
                }
            }

            var psi = new ProcessStartInfo(game.Exe)
            {
                WorkingDirectory = game.Dir,
                UseShellExecute = false,
            };
            psi.EnvironmentVariables["INKVR_MODE"] = vr ? "vr" : "flat";
            psi.EnvironmentVariables.Remove("XR_RUNTIME_JSON");
            if (vr && !string.IsNullOrEmpty(runtimeJson) && File.Exists(runtimeJson))
                psi.EnvironmentVariables["XR_RUNTIME_JSON"] = runtimeJson;

            LauncherLog.Write($"Launching {(vr ? "VR" : "flat")} runtime={(string.IsNullOrEmpty(runtimeJson) ? "(system default)" : runtimeJson)}");
            status?.Invoke(vr ? "Starting Bendy and the Ink Machine in VR..." : "Starting Bendy and the Ink Machine...");
            return Process.Start(psi);
        }

        public static bool VirtualDesktopStreamerRunning() =>
            Process.GetProcesses().Any(p =>
            {
                try
                {
                    return p.ProcessName.StartsWith("VirtualDesktop.Streamer", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            });

        public static void OpenFolder(string path)
        {
            if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }

        public static void OpenFile(string path)
        {
            if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }
}
