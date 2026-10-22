using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace InkVR.Launcher.Game
{
    public enum CheckLevel { Ok, Warn, Fail, Info }

    public sealed class Check
    {
        public string Title;
        public string Detail;
        public CheckLevel Level;
    }

    /// <summary>The VR CHECK page: everything that has to be right before pressing Play.</summary>
    public static class Diagnostics
    {
        public const string TestedBuild = "23764460";

        public static List<Check> Run(GameInstall g)
        {
            var list = new List<Check>();
            void Add(string title, CheckLevel level, string detail) => list.Add(new Check { Title = title, Level = level, Detail = detail });

            if (g == null)
            {
                Add("Bendy and the Ink Machine", CheckLevel.Fail, "Not found. Put InkVR Launcher in the game folder, or use LOCATE GAME.");
                return list;
            }

            Add("Bendy and the Ink Machine", g.SteamBuildId == null || g.SteamBuildId == TestedBuild ? CheckLevel.Ok : CheckLevel.Warn,
                g.SteamBuildId == null ? g.Dir :
                g.SteamBuildId == TestedBuild ? $"Steam build {g.SteamBuildId} (the build InkVR is made for)" :
                $"Steam build {g.SteamBuildId}. InkVR was made for build {TestedBuild}; a game update may need a mod update.");

            if (!ModInstaller.BepInExInstalled(g))
                Add("BepInEx 6 mod loader", CheckLevel.Fail, "Not installed. Press INSTALL / REPAIR.");
            else
                Add("BepInEx 6 mod loader", ModInstaller.InteropGenerated(g) ? CheckLevel.Ok : CheckLevel.Info,
                    ModInstaller.InteropGenerated(g) ? "Installed" : "Installed. The first start prepares the game for mods (one time, a few minutes).");

            var installed = ModInstaller.InstalledVersion(g);
            var payload = ModInstaller.PayloadVersion;
            if (installed == null)
                Add("InkVR", CheckLevel.Fail, "Not installed. Press INSTALL / REPAIR.");
            else if (payload != null && payload > installed)
                Add("InkVR", CheckLevel.Warn, $"Version {installed} installed; {payload} is ready. Press INSTALL / REPAIR to update.");
            else
                Add("InkVR", CheckLevel.Ok, $"Version {installed}");

            Add("Unity OpenXR plugin", ModInstaller.ProviderInstalled(g) ? CheckLevel.Ok : CheckLevel.Fail,
                ModInstaller.ProviderInstalled(g) ? "Installed (1.17.1)" : "Missing. INSTALL / REPAIR downloads it from Unity.");

            var chosen = LauncherSettings.Runtime;
            var active = GameLocator.ActiveRuntimeJson();
            var runtimeJson = string.IsNullOrEmpty(chosen) ? active : chosen;
            if (string.IsNullOrEmpty(runtimeJson) || !File.Exists(runtimeJson))
                Add("OpenXR runtime", CheckLevel.Fail, "No OpenXR runtime found. Install Virtual Desktop's streamer, SteamVR or Meta Horizon Link.");
            else
                Add("OpenXR runtime", CheckLevel.Ok, GameLocator.FriendlyRuntime(runtimeJson) + (string.IsNullOrEmpty(chosen) ? " (PC default)" : " (chosen in OPTIONS)"));

            if (runtimeJson != null && runtimeJson.IndexOf("virtualdesktop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var vd = GameLaunch.VirtualDesktopStreamerRunning();
                Add("Virtual Desktop Streamer", vd ? CheckLevel.Ok : CheckLevel.Warn,
                    vd ? "Running. Connect from your Quest, then press PLAY IN VR." : "Not running. Start it on the PC and connect from your Quest first.");
            }

            Add("Steam", GameLaunch.SteamRunning() ? CheckLevel.Ok : CheckLevel.Info,
                GameLaunch.SteamRunning() ? "Running" : "Not running. The launcher will start it for you.");

            foreach (var gpu in Gpus())
                Add("Graphics", gpu.Level, gpu.Detail);

            var plan = PowerPlan();
            if (plan != null)
                Add("Power plan", plan.IndexOf("saver", StringComparison.OrdinalIgnoreCase) >= 0 ? CheckLevel.Warn : CheckLevel.Ok,
                    plan.IndexOf("saver", StringComparison.OrdinalIgnoreCase) >= 0 ? plan + ". Switch to Balanced or High performance for 90 fps." : plan);

            if (GameLaunch.GameRunning())
                Add("Game", CheckLevel.Info, "Already running.");

            return list;
        }

        private static IEnumerable<Check> Gpus()
        {
            var found = new List<Check>();
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController"))
                    foreach (ManagementObject o in q.Get())
                    {
                        var name = o["Name"] as string ?? "Unknown GPU";
                        if (name.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        var discrete = Regex.IsMatch(name, "NVIDIA|GeForce|RTX|GTX|Radeon RX|Radeon Pro|Arc", RegexOptions.IgnoreCase);
                        found.Add(new Check { Level = discrete ? CheckLevel.Ok : CheckLevel.Warn, Detail = $"{name} (driver {o["DriverVersion"]})" + (discrete ? "" : ". A dedicated GPU is recommended for VR.") });
                    }
            }
            catch (Exception e)
            {
                LauncherLog.Write("GPU query failed: " + e.Message);
            }

            // Laptops list the integrated GPU too; show the dedicated one when there is one.
            if (found.Any(c => c.Level == CheckLevel.Ok)) return found.Where(c => c.Level == CheckLevel.Ok);
            return found;
        }

        private static string PowerPlan()
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", "/getactivescheme") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    var m = Regex.Match(o, @"\(([^)]+)\)");
                    return m.Success ? m.Groups[1].Value : null;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
