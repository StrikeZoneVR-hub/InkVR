using System.Collections.Generic;
using System.IO;

namespace InkVR.UI;

/// <summary>
/// InkVR's own progress: fan finds, Fun Hall tickets, high scores and prizes. Kept in its own small file
/// (BepInEx/InkVR/progress.txt, one "key=value" a line), never in the game's save. Dev test sessions
/// (INKVR_DEV_NOSAVE=1) keep it in memory only, so tests never touch the player's progress.
/// </summary>
public static class Progress
{
    private static Dictionary<string, int> _values;
    private static bool _dirty;
    private static float _saveAt;

    private static string FilePath => Path.Combine(Plugin.DataDir, "progress.txt");

    private static Dictionary<string, int> Values
    {
        get
        {
            if (_values != null) return _values;
            _values = new Dictionary<string, int>();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        var eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        if (int.TryParse(line[(eq + 1)..].Trim(), out var v)) _values[line[..eq].Trim()] = v;
                    }
            }
            catch (System.Exception e)
            {
                Plugin.L.LogWarning("Progress: couldn't read " + FilePath + ": " + e.Message);
            }

            return _values;
        }
    }

    public static int Get(string key, int fallback = 0) => Values.TryGetValue(key, out var v) ? v : fallback;

    public static bool Has(string key) => Get(key) != 0;

    public static void Set(string key, int value)
    {
        if (Values.TryGetValue(key, out var v) && v == value) return;
        Values[key] = value;
        _dirty = true;
        _saveAt = UnityEngine.Time.unscaledTime + 1.5f;
    }

    public static int Add(string key, int delta)
    {
        var v = Get(key) + delta;
        Set(key, v);
        return v;
    }

    /// <summary>Writes the file a moment after the last change (call every frame).</summary>
    public static void Tick()
    {
        if (!_dirty || UnityEngine.Time.unscaledTime < _saveAt) return;
        _dirty = false;
        if (Dev.DevSaveGuard.Active) return; // a test session: nothing written
        try
        {
            var lines = new List<string>();
            foreach (var kv in Values) lines.Add(kv.Key + "=" + kv.Value);
            lines.Sort();
            var tmp = FilePath + ".tmp";
            File.WriteAllLines(tmp, lines);
            File.Copy(tmp, FilePath, true);
            File.Delete(tmp);
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("Progress: couldn't write " + FilePath + ": " + e.Message);
        }
    }

    public static string Describe()
    {
        var parts = new List<string>();
        foreach (var kv in Values) parts.Add(kv.Key + "=" + kv.Value);
        parts.Sort();
        return string.Join(" ", parts);
    }
}
