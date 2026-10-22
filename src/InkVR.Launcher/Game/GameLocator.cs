using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace InkVR.Launcher.Game
{
    /// <summary>Paths of the installed game (Steam app 622650).</summary>
    public sealed class GameInstall
    {
        public const string ExeName = "Bendy and the Ink Machine.exe";
        public const int SteamAppId = 622650;

        public string Dir;
        public string Exe => Path.Combine(Dir, ExeName);
        public string DataDir => Path.Combine(Dir, "Bendy and the Ink Machine_Data");
        public string BepInExDir => Path.Combine(Dir, "BepInEx");
        public string PluginDll => Path.Combine(BepInExDir, "plugins", "InkVR", "InkVR.dll");
        public string ConfigFile => Path.Combine(BepInExDir, "config", "com.strikezonevr.inkvr.cfg");
        public string LogFile => Path.Combine(BepInExDir, "LogOutput.log");
        public string OpenXRProvider => Path.Combine(DataDir, "Plugins", "x86_64", "UnityOpenXR.dll");
        public string OpenXRLoader => Path.Combine(DataDir, "Plugins", "x86_64", "openxr_loader.dll");
        public string SubsystemManifest => Path.Combine(DataDir, "UnitySubsystems", "UnityOpenXR", "UnitySubsystemsManifest.json");
        public string SteamBuildId;
    }

    public static class GameLocator
    {
        private static string SettingsFile => Path.Combine(LauncherPaths.AppData, "game_path.txt");

        public static GameInstall Find()
        {
            foreach (var dir in Candidates())
            {
                if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, GameInstall.ExeName))) continue;
                var g = new GameInstall { Dir = dir };
                g.SteamBuildId = ReadBuildId(dir);
                Remember(dir);
                return g;
            }

            return null;
        }

        public static void Remember(string dir)
        {
            try
            {
                Directory.CreateDirectory(LauncherPaths.AppData);
                File.WriteAllText(SettingsFile, dir);
            }
            catch
            {
                // best effort
            }
        }

        private static IEnumerable<string> Candidates()
        {
            // 1. Next to the launcher (InkVR Launcher.exe dropped into the game folder).
            var here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            yield return here;
            yield return Path.GetDirectoryName(here);

            // 2. Last known location.
            if (File.Exists(SettingsFile)) yield return File.ReadAllText(SettingsFile).Trim();

            // 3. Steam libraries.
            foreach (var lib in SteamLibraries())
            {
                var manifest = Path.Combine(lib, "steamapps", $"appmanifest_{GameInstall.SteamAppId}.acf");
                var installDir = "Bendy and the Ink Machine";
                if (File.Exists(manifest))
                {
                    var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                    if (m.Success) installDir = m.Groups[1].Value;
                }

                yield return Path.Combine(lib, "steamapps", "common", installDir);
            }
        }

        public static string SteamRoot()
        {
            var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (string.IsNullOrEmpty(path)) path = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            return string.IsNullOrEmpty(path) ? null : path.Replace('/', '\\');
        }

        public static IEnumerable<string> SteamLibraries()
        {
            var root = SteamRoot();
            if (root == null) yield break;
            yield return root;
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) yield break;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                yield return m.Groups[1].Value.Replace(@"\\", @"\");
        }

        private static string ReadBuildId(string gameDir)
        {
            try
            {
                var steamapps = Path.GetDirectoryName(Path.GetDirectoryName(gameDir));
                var manifest = Path.Combine(steamapps ?? "", $"appmanifest_{GameInstall.SteamAppId}.acf");
                if (!File.Exists(manifest)) return null;
                var m = Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"(\\d+)\"");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>OpenXR runtimes on this PC: (display name, manifest path).</summary>
        public static List<(string Name, string Json)> OpenXRRuntimes()
        {
            var list = new List<(string, string)>();
            void Add(string name, string json)
            {
                if (!string.IsNullOrEmpty(json) && File.Exists(json) && list.All(r => !string.Equals(r.Item2, json, StringComparison.OrdinalIgnoreCase)))
                    list.Add((name, json));
            }

            Add("Virtual Desktop (VDXR)", @"C:\Program Files\Virtual Desktop Streamer\OpenXR\virtualdesktop-openxr.json");
            Add("Meta Horizon Link", @"C:\Program Files\Oculus\Support\oculus-runtime\oculus_openxr_64.json");
            Add("Meta Horizon Link", @"C:\Program Files\Meta Horizon\Support\oculus-runtime\oculus_openxr_64.json");
            foreach (var lib in SteamLibraries())
                Add("SteamVR", Path.Combine(lib, "steamapps", "common", "SteamVR", "steamxr_win64.json"));
            return list;
        }

        public static string ActiveRuntimeJson() =>
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Khronos\OpenXR\1", "ActiveRuntime", null) as string;

        public static string FriendlyRuntime(string json)
        {
            if (string.IsNullOrEmpty(json)) return "None set";
            var lower = json.ToLowerInvariant();
            if (lower.Contains("virtualdesktop")) return "Virtual Desktop (VDXR)";
            if (lower.Contains("oculus")) return "Meta Horizon Link";
            if (lower.Contains("steamxr") || lower.Contains("steamvr")) return "SteamVR";
            if (lower.Contains("mock")) return "Unity mock runtime";
            return Path.GetFileNameWithoutExtension(json);
        }
    }

    public static class LauncherPaths
    {
        public static string AppData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InkVR");
        public static string ArtCache => Path.Combine(AppData, "art");
        public static string Payload => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InkVR-Payload");
    }
}
