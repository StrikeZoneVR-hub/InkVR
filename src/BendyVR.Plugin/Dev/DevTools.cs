using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BendyVR.Dev;

/// <summary>
/// Development helpers driven by a command file (BepInEx/BendyVR/cmd.txt), so the game
/// can be inspected and screenshotted while it runs: one command per line, the file is
/// deleted once read. Results go to the BepInEx log and BepInEx/BendyVR/{dumps,shots}.
/// </summary>
public class DevTools : MonoBehaviour
{
    public DevTools(IntPtr ptr) : base(ptr) { }

    private string _cmdPath;
    private float _nextPoll;
    private string _lastSceneSummary = "";
    private bool _perf;
    private float _perfStart;
    private int _perfFrames;
    private float _perfWorst;

    private void Awake()
    {
        _cmdPath = Path.Combine(Plugin.DataDir, "cmd.txt");
        Plugin.L.LogInfo($"DevTools ready. Graphics: {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceVersion} on {SystemInfo.graphicsDeviceName}");
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextPoll)
        {
            _nextPoll = Time.unscaledTime + 0.25f;
            LogSceneChanges();
            PollCommands();
        }

        if (_perf) TrackPerf();
    }

    private void LogSceneChanges()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            sb.Append(s.name).Append(s.isLoaded ? "" : "(loading)").Append(';');
        }

        var summary = sb.ToString();
        if (summary == _lastSceneSummary) return;
        _lastSceneSummary = summary;
        Plugin.L.LogInfo($"Scenes: {summary} active={SceneManager.GetActiveScene().name}");
    }

    private void TrackPerf()
    {
        var dt = Time.unscaledDeltaTime;
        _perfFrames++;
        _perfWorst = Mathf.Max(_perfWorst, dt);
        var elapsed = Time.unscaledTime - _perfStart;
        if (elapsed < 2f) return;
        Plugin.L.LogInfo($"PERF avg {elapsed / _perfFrames * 1000f:F2} ms ({_perfFrames / elapsed:F1} fps), worst {_perfWorst * 1000f:F2} ms");
        _perfStart = Time.unscaledTime;
        _perfFrames = 0;
        _perfWorst = 0;
    }

    private void PollCommands()
    {
        if (!File.Exists(_cmdPath)) return;
        string[] lines;
        try
        {
            lines = File.ReadAllLines(_cmdPath);
            File.Delete(_cmdPath);
        }
        catch (IOException)
        {
            return; // still being written
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            try
            {
                Run(line);
            }
            catch (Exception e)
            {
                Plugin.L.LogError($"Command '{line}' failed: {e}");
            }
        }
    }

    private void Run(string line)
    {
        var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var arg = parts.Length > 1 ? parts[1].Trim() : "";
        Plugin.L.LogInfo($"> {line}");
        switch (parts[0].ToLowerInvariant())
        {
            case "dump": DumpHierarchy(arg); break;
            case "cams": DumpCameras(); break;
            case "shot": Screenshot(arg); break;
            case "find": Find(arg); break;
            case "inspect": Inspect(arg); break;
            case "active": SetActive(arg); break;
            case "perf":
                _perf = arg != "off";
                _perfStart = Time.unscaledTime;
                _perfFrames = 0;
                break;
            case "ts": Time.timeScale = float.Parse(arg); break;
            case "quit": Application.Quit(); break;
            default: Plugin.L.LogWarning($"Unknown command '{parts[0]}'"); break;
        }
    }

    // ---------- hierarchy ----------

    internal static IEnumerable<GameObject> AllRoots()
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (!s.isLoaded) continue;
            foreach (var go in s.GetRootGameObjects()) yield return go;
        }

        // DontDestroyOnLoad scene: reach it through a temporary object.
        var probe = new GameObject("BendyVR_DDOLProbe");
        DontDestroyOnLoad(probe);
        var ddol = probe.scene.GetRootGameObjects();
        Destroy(probe);
        foreach (var go in ddol)
            if (go.name != "BendyVR_DDOLProbe")
                yield return go;
    }

    internal static string PathOf(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    internal static Transform Resolve(string path)
    {
        var names = path.Split('/');
        foreach (var root in AllRoots())
        {
            if (root.name != names[0]) continue;
            var t = root.transform;
            for (var i = 1; i < names.Length && t != null; i++) t = FindChild(t, names[i]);
            if (t != null) return t;
        }

        return null;
    }

    private static Transform FindChild(Transform t, string name)
    {
        for (var i = 0; i < t.childCount; i++)
        {
            var c = t.GetChild(i);
            if (c.name == name) return c;
        }

        return null;
    }

    private static string ComponentList(GameObject go)
    {
        var comps = go.GetComponents<Component>();
        var names = new List<string>();
        foreach (var c in comps)
        {
            if (c == null) continue;
            var n = c.GetIl2CppType().Name;
            if (n == "Transform") continue;
            if (c.TryCast<Behaviour>() is { } b && !b.enabled) n += "(off)";
            names.Add(n);
        }

        return string.Join(", ", names);
    }

    private void DumpHierarchy(string name)
    {
        var dir = Path.Combine(Plugin.DataDir, "dumps");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{(name.Length > 0 ? name : SceneManager.GetActiveScene().name)}_{DateTime.Now:HHmmss}.txt");
        using var w = new StreamWriter(file);
        foreach (var root in AllRoots())
        {
            w.WriteLine($"## [{root.scene.name}] {root.name}");
            DumpRecursive(w, root.transform, 0);
        }

        Plugin.L.LogInfo($"Hierarchy dumped to {file}");
    }

    private static void DumpRecursive(StreamWriter w, Transform t, int depth)
    {
        var go = t.gameObject;
        var p = t.localPosition;
        w.WriteLine($"{new string(' ', depth * 2)}{(go.activeSelf ? "" : "[x] ")}{go.name}  L{go.layer} pos({p.x:F2},{p.y:F2},{p.z:F2})  {{{ComponentList(go)}}}");
        for (var i = 0; i < t.childCount; i++) DumpRecursive(w, t.GetChild(i), depth + 1);
    }

    private void Find(string needle)
    {
        var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Transform>());
        var n = 0;
        foreach (var o in all)
        {
            var t = o.Cast<Transform>();
            if (!t.gameObject.scene.IsValid()) continue; // skip prefabs/assets
            if (t.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
            Plugin.L.LogInfo($"  {(t.gameObject.activeInHierarchy ? "" : "[x] ")}{PathOf(t)}  {{{ComponentList(t.gameObject)}}}");
            if (++n >= 200) break;
        }

        Plugin.L.LogInfo($"find '{needle}': {n} match(es)");
    }

    private void SetActive(string arg)
    {
        var i = arg.LastIndexOf(' ');
        var t = Resolve(arg[..i]);
        if (t == null)
        {
            Plugin.L.LogWarning("Not found");
            return;
        }

        t.gameObject.SetActive(arg[(i + 1)..] == "1");
    }

    // ---------- cameras ----------

    private void DumpCameras()
    {
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Camera>()))
        {
            var c = o.Cast<Camera>();
            if (!c.gameObject.scene.IsValid()) continue;
            Plugin.L.LogInfo(
                $"CAM {PathOf(c.transform)} enabled={c.enabled}/{c.gameObject.activeInHierarchy} depth={c.depth} fov={c.fieldOfView:F1} " +
                $"near={c.nearClipPlane} far={c.farClipPlane} clear={c.clearFlags} mask=0x{c.cullingMask:X8} path={c.actualRenderingPath} " +
                $"rt={(c.targetTexture != null ? c.targetTexture.name : "-")} hdr={c.allowHDR} msaa={c.allowMSAA}\n      {{{ComponentList(c.gameObject)}}}");
        }
    }

    // ---------- inspection ----------

    private static readonly Dictionary<string, Type> TypeCache = new();

    private static Type InteropType(string il2CppFullName)
    {
        if (TypeCache.TryGetValue(il2CppFullName, out var t)) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            t = asm.GetType(il2CppFullName) ?? asm.GetType("Il2Cpp" + il2CppFullName);
            if (t != null) break;
        }

        TypeCache[il2CppFullName] = t;
        return t;
    }

    private void Inspect(string path)
    {
        var tr = Resolve(path);
        if (tr == null)
        {
            Plugin.L.LogWarning($"Not found: {path}");
            return;
        }

        var sb = new StringBuilder($"INSPECT {PathOf(tr)} active={tr.gameObject.activeInHierarchy} layer={tr.gameObject.layer}\n");
        sb.AppendLine($"  local pos={tr.localPosition} rot={tr.localEulerAngles} scale={tr.localScale}; world pos={tr.position} rot={tr.eulerAngles}");
        foreach (var c in tr.GetComponents<Component>())
        {
            if (c == null) continue;
            var il2 = c.GetIl2CppType();
            sb.AppendLine($"  [{il2.FullName}]");
            var type = InteropType(il2.FullName);
            if (type == null) continue;
            object typed;
            try
            {
                typed = Activator.CreateInstance(type, c.Pointer);
            }
            catch
            {
                continue;
            }

            for (var ty = type; ty != null && ty.Assembly == type.Assembly; ty = ty.BaseType)
            {
                foreach (var p in ty.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                    string val;
                    try
                    {
                        var v = p.GetValue(typed);
                        val = v switch
                        {
                            null => "null",
                            Object uo => uo == null ? "null(destroyed)" : $"{uo.name} ({uo.GetIl2CppType().Name})",
                            _ => v.ToString()
                        };
                    }
                    catch (Exception e)
                    {
                        val = "<" + (e.InnerException ?? e).GetType().Name + ">";
                    }

                    sb.AppendLine($"    {p.Name} = {val}");
                }
            }
        }

        Plugin.L.LogInfo(sb.ToString());
    }

    // ---------- screenshots ----------

    private void Screenshot(string arg)
    {
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name = parts.Length > 0 ? parts[0] : "shot";
        var scale = parts.Length > 1 ? int.Parse(parts[1]) : 1;
        var dir = Path.Combine(Plugin.DataDir, "shots");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{name}_{DateTime.Now:HHmmss}.png");
        ScreenCapture.CaptureScreenshot(file, scale);
        Plugin.L.LogInfo($"Screenshot -> {file}");
    }
}
