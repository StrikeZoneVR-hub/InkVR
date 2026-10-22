using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace InkVR.Launcher.Game
{
    /// <summary>
    /// Reads and edits the mod's BepInEx config (the same file the in-game VR SETTINGS page uses),
    /// changing only the value on each line so comments and layout survive.
    /// </summary>
    public sealed class ModSettingsFile
    {
        private readonly string _path;
        private readonly List<string> _lines = new List<string>();

        public ModSettingsFile(string path)
        {
            _path = path;
            if (File.Exists(path)) _lines.AddRange(File.ReadAllLines(path));
        }

        public bool Exists => File.Exists(_path);

        public string Get(string section, string key)
        {
            var i = Find(section, key);
            return i < 0 ? null : _lines[i].Substring(_lines[i].IndexOf('=') + 1).Trim();
        }

        public bool GetBool(string section, string key, bool fallback) =>
            bool.TryParse(Get(section, key), out var b) ? b : fallback;

        public float GetFloat(string section, string key, float fallback) =>
            float.TryParse(Get(section, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;

        public void Set(string section, string key, string value)
        {
            var i = Find(section, key);
            if (i >= 0)
            {
                _lines[i] = key + " = " + value;
            }
            else
            {
                var s = SectionLine(section);
                if (s < 0)
                {
                    if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length > 0) _lines.Add("");
                    _lines.Add("[" + section + "]");
                    _lines.Add("");
                    _lines.Add(key + " = " + value);
                }
                else
                {
                    var end = s + 1;
                    while (end < _lines.Count && !_lines[end].TrimStart().StartsWith("[")) end++;
                    while (end > s + 1 && _lines[end - 1].Trim().Length == 0) end--;
                    _lines.Insert(end, key + " = " + value);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            File.WriteAllLines(_path, _lines);
        }

        public void Set(string section, string key, bool value) => Set(section, key, value ? "true" : "false");
        public void Set(string section, string key, float value) => Set(section, key, value.ToString("0.###", CultureInfo.InvariantCulture));

        private int SectionLine(string section)
        {
            for (var i = 0; i < _lines.Count; i++)
                if (_lines[i].Trim() == "[" + section + "]")
                    return i;
            return -1;
        }

        private int Find(string section, string key)
        {
            var s = SectionLine(section);
            if (s < 0) return -1;
            for (var i = s + 1; i < _lines.Count; i++)
            {
                var t = _lines[i].Trim();
                if (t.StartsWith("[")) break;
                if (t.StartsWith("#") || !t.Contains("=")) continue;
                if (string.Equals(t.Substring(0, t.IndexOf('=')).Trim(), key, StringComparison.Ordinal)) return i;
            }

            return -1;
        }
    }

    /// <summary>The launcher's own preferences (%LOCALAPPDATA%\InkVR\launcher.ini).</summary>
    public static class LauncherSettings
    {
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string FilePath => Path.Combine(LauncherPaths.AppData, "launcher.ini");

        /// <summary>"" = the PC's active OpenXR runtime, otherwise a runtime manifest (.json) path.</summary>
        public static string Runtime { get => Str("runtime", ""); set => Put("runtime", value); }
        public static bool PlayIntro { get => Str("intro", "true") == "true"; set => Put("intro", value ? "true" : "false"); }
        public static bool Sounds { get => Str("sounds", "true") == "true"; set => Put("sounds", value ? "true" : "false"); }
        public static bool Fullscreen { get => Str("fullscreen", "false") == "true"; set => Put("fullscreen", value ? "true" : "false"); }
        public static bool CloseOnLaunch { get => Str("close_on_launch", "true") == "true"; set => Put("close_on_launch", value ? "true" : "false"); }

        static LauncherSettings()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var i = line.IndexOf('=');
                    if (i > 0) Values[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch
            {
                // defaults
            }
        }

        private static string Str(string key, string fallback) => Values.TryGetValue(key, out var v) ? v : fallback;

        private static void Put(string key, string value)
        {
            Values[key] = value;
            try
            {
                Directory.CreateDirectory(LauncherPaths.AppData);
                var lines = new List<string> { "; InkVR Launcher preferences" };
                foreach (var kv in Values) lines.Add(kv.Key + " = " + kv.Value);
                File.WriteAllLines(FilePath, lines);
            }
            catch (Exception e)
            {
                LauncherLog.Write("Could not save launcher.ini: " + e.Message);
            }
        }
    }
}
