using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using InkVR.Input;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace InkVR.Dev;

/// <summary>
/// Development helpers driven by a command file (BepInEx/InkVR/cmd.txt), so the game
/// can be inspected and screenshotted while it runs: one command per line, the file is
/// deleted once read. Results go to the BepInEx log and BepInEx/InkVR/{dumps,shots}.
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
    private Vector2 _devMove, _devLook;
    private float _devMoveUntil, _devLookUntil;

    private void Awake()
    {
        _cmdPath = Path.Combine(Plugin.DataDir, "cmd.txt");
        if (File.Exists(_cmdPath)) File.Delete(_cmdPath); // never replay commands from a previous run
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

        if (_devMoveUntil > 0)
        {
            var on = Time.unscaledTime < _devMoveUntil;
            VirtualInput.Move = on ? _devMove : Vector2.zero;
            if (!on) _devMoveUntil = 0;
        }

        if (_devLookUntil > 0)
        {
            var on = Time.unscaledTime < _devLookUntil;
            VirtualInput.Look = on ? _devLook : Vector2.zero;
            if (!on) _devLookUntil = 0;
        }
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
            case "click": Click(arg); break;
            case "findc": FindComponent(arg); break;
            case "call": Call(arg); break;
            case "rscale":
                UnityEngine.XR.XRSettings.eyeTextureResolutionScale = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                Plugin.L.LogInfo($"eye texture {UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight}");
                break;
            case "enable":
            {
                // enable <path> <ComponentType> 0|1
                var a = arg.Split(' ');
                var t = Resolve(string.Join(' ', a[..^2]));
                foreach (var c in t.GetComponents<Behaviour>())
                    if (c.GetIl2CppType().Name == a[^2]) c.enabled = a[^1] == "1";
                break;
            }
            case "bounds":
            {
                var n = 0;
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Renderer>()))
                {
                    var r = o.Cast<Renderer>();
                    if (!r.gameObject.scene.IsValid() || !r.gameObject.activeInHierarchy || r.name.IndexOf(arg, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Plugin.L.LogInfo($"  {PathOf(r.transform)} size={r.bounds.size} center={r.bounds.center}");
                    if (++n > 15) break;
                }
                break;
            }
            case "pp":
            {
                var cam = InkVR.Core.VRRig.GameCamera ?? GameManager.Instance?.GameCamera?.Camera;
                if (cam == null) break;
                if (arg.Length > 0)
                {
                    var a = arg.Split(' ');
                    InkVR.Rendering.VRGraphics.Set(cam, a[0], a[1] == "1");
                }

                Plugin.L.LogInfo("PP " + InkVR.Rendering.VRGraphics.Describe(cam));
                break;
            }
            case "eyes":
            {
                var cam = InkVR.Core.VRRig.GameCamera ?? Camera.main;
                var l = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
                var r = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);
                Plugin.L.LogInfo($"EYES {cam.name}: left={(Vector3)l} right={(Vector3)r} separation={Vector3.Distance(l, r):F3} units, camera at {cam.transform.position}, lossyScale={cam.transform.lossyScale.x:F2}, stereoSeparation={cam.stereoSeparation}");
                break;
            }
            case "shaders":
            {
                var used = new Dictionary<string, int>();
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Renderer>()))
                {
                    var r = o.Cast<Renderer>();
                    if (!r.gameObject.scene.IsValid()) continue;
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || m.shader == null) continue;
                        used.TryGetValue(m.shader.name, out var c);
                        used[m.shader.name] = c + 1;
                    }
                }

                Plugin.L.LogInfo("SHADERS in use: " + string.Join(", ", used.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}({kv.Value})")));
                var all = new List<string>();
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Shader>())) all.Add(o.Cast<Shader>().name);
                Plugin.L.LogInfo("SHADERS loaded: " + string.Join(", ", all.OrderBy(n => n)));
                foreach (var n in new[] { "Standard", "Hidden/Internal-Colored", "Unlit/Color", "Unlit/Texture", "Unlit/Transparent", "Legacy Shaders/Diffuse", "Particles/Standard Unlit", "Sprites/Default" })
                    Plugin.L.LogInfo($"  Shader.Find('{n}') = {(Shader.Find(n) != null ? "yes" : "no")}");
                break;
            }
            case "shaderprops":
            {
                var sh = Shader.Find(arg);
                if (sh == null) { Plugin.L.LogWarning("shader not found"); break; }
                var sb = new StringBuilder($"SHADERPROPS {arg} queue={sh.renderQueue}:");
                for (var i = 0; i < sh.GetPropertyCount(); i++)
                    sb.Append($" {sh.GetPropertyName(i)}:{sh.GetPropertyType(i)}");
                // A material that uses it, for default values.
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>()))
                {
                    var m = o.Cast<Material>();
                    if (m.shader != sh) continue;
                    sb.Append($"\n  example material '{m.name}':");
                    for (var i = 0; i < sh.GetPropertyCount(); i++)
                    {
                        var n = sh.GetPropertyName(i);
                        var t = sh.GetPropertyType(i);
                        sb.Append(t switch
                        {
                            UnityEngine.Rendering.ShaderPropertyType.Texture => $" {n}={(m.GetTexture(n) != null ? m.GetTexture(n).name : "-")}",
                            UnityEngine.Rendering.ShaderPropertyType.Color => $" {n}={m.GetColor(n)}",
                            UnityEngine.Rendering.ShaderPropertyType.Vector => $" {n}={m.GetVector(n)}",
                            _ => $" {n}={m.GetFloat(n)}"
                        });
                    }

                    sb.Append($" keywords=[{string.Join(",", m.shaderKeywords)}]");
                    break;
                }

                Plugin.L.LogInfo(sb.ToString());
                break;
            }
            case "hands":
            {
                // hands demo [pitch] | hands off | hands x y z pitch yaw (right hand; left mirrored)
                if (arg == "off")
                {
                    InkVR.Body.Hands.DevLeft = InkVR.Body.Hands.DevRight = null;
                    break;
                }

                var a = arg == "demo" || arg.Length == 0 ? new float[] { 0.17f, 1.38f, 0.32f, -35f, 0f }
                    : arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (a.Length == 1) a = new[] { 0.17f, 1.38f, 0.32f, a[0], 0f };
                if (a.Length < 5) a = a.Concat(new float[5 - a.Length]).ToArray();
                InkVR.Body.Hands.DevRight = (new Vector3(a[0], a[1], a[2]), Quaternion.Euler(a[3], a[4], 0));
                InkVR.Body.Hands.DevLeft = (new Vector3(-a[0], a[1], a[2]), Quaternion.Euler(a[3], -a[4], 0));
                break;
            }
            case "mirror":
                UnityEngine.XR.XRSettings.gameViewRenderMode = arg switch
                {
                    "both" => UnityEngine.XR.GameViewRenderMode.BothEyes,
                    "right" => UnityEngine.XR.GameViewRenderMode.RightEye,
                    "occlusion" => UnityEngine.XR.GameViewRenderMode.OcclusionMesh,
                    _ => UnityEngine.XR.GameViewRenderMode.LeftEye
                };
                break;
            case "head":
            {
                // head x y z yaw pitch  (metres / degrees, overrides tracking)  |  head off
                if (arg == "off")
                {
                    InkVR.Core.VRRig.DevHeadOverride = null;
                    break;
                }

                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                InkVR.Core.VRRig.DevHeadOverride = (new Vector3(a[0], a[1], a[2]), Quaternion.Euler(a.Length > 4 ? a[4] : 0, a.Length > 3 ? a[3] : 0, 0));
                break;
            }
            case "xr":
                if (arg == "start") InkVR.Core.XrManager.Initialize();
                else if (arg == "stop") InkVR.Core.XrManager.Shutdown();
                Plugin.L.LogInfo($"XR state={InkVR.Core.XrManager.Current} session={InkVR.Core.XrManager.SessionState} runtime='{InkVR.Core.XrManager.RuntimeName}' running={InkVR.Core.XrManager.IsRunning} XRSettings.enabled={UnityEngine.XR.XRSettings.enabled} device='{UnityEngine.XR.XRSettings.loadedDeviceName}' eye={UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight}");
                break;
            case "press":
            {
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                VirtualInput.Tap(Enum.Parse<GameAction>(a[0], true), a.Length > 1 ? float.Parse(a[1]) : 0f);
                break;
            }
            case "move":
            case "look":
            {
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(float.Parse).ToArray();
                if (parts[0] == "move") { _devMove = new Vector2(a[0], a[1]); _devMoveUntil = Time.unscaledTime + a[2]; }
                else { _devLook = new Vector2(a[0], a[1]); _devLookUntil = Time.unscaledTime + a[2]; }
                break;
            }
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
        var probe = new GameObject("InkVR_DDOLProbe");
        DontDestroyOnLoad(probe);
        var ddol = probe.scene.GetRootGameObjects();
        Destroy(probe);
        foreach (var go in ddol)
            if (go.name != "InkVR_DDOLProbe")
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

    private void FindComponent(string typeName)
    {
        var n = 0;
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Component>()))
        {
            var c = o.Cast<Component>();
            if (!c.gameObject.scene.IsValid()) continue;
            if (!string.Equals(c.GetIl2CppType().Name, typeName, StringComparison.OrdinalIgnoreCase)) continue;
            Plugin.L.LogInfo($"  {(c.gameObject.activeInHierarchy ? "" : "[x] ")}{PathOf(c.transform)}");
            if (++n >= 100) break;
        }

        Plugin.L.LogInfo($"findc '{typeName}': {n} match(es)");
    }

    private void Click(string path)
    {
        var t = Resolve(path);
        if (t == null)
        {
            Plugin.L.LogWarning($"Not found: {path}");
            return;
        }

        var go = t.gameObject;
        var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
        {
            button = UnityEngine.EventSystems.PointerEventData.InputButton.Left
        };
        UnityEngine.EventSystems.ExecuteEvents.Execute(go, data, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
        UnityEngine.EventSystems.ExecuteEvents.Execute(go, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
        UnityEngine.EventSystems.ExecuteEvents.Execute(go, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
        UnityEngine.EventSystems.ExecuteEvents.Execute(go, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
    }

    /// <summary>call TypeName.Method [args...] on the first live component of that type (or a static method).</summary>
    private void Call(string arg)
    {
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var dot = parts[0].LastIndexOf('.');
        var typeName = parts[0][..dot];
        var methodName = parts[0][(dot + 1)..];
        var args = parts.Skip(1).ToArray();

        var type = InteropType(typeName);
        if (type == null)
        {
            Plugin.L.LogWarning($"Type not found: {typeName}");
            return;
        }

        var method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == args.Length);
        if (method == null)
        {
            Plugin.L.LogWarning($"Method not found: {methodName}/{args.Length}");
            return;
        }

        object target = null;
        if (!method.IsStatic)
        {
            var il2Type = Il2CppType.From(type);
            var found = Object.FindObjectOfType(il2Type);
            if (found == null)
            {
                Plugin.L.LogWarning($"No live instance of {typeName}");
                return;
            }

            target = Activator.CreateInstance(type, found.Pointer);
        }

        var ps = method.GetParameters();
        var values = new object[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            var pt = ps[i].ParameterType;
            values[i] = pt == typeof(string) ? args[i]
                : pt.IsEnum ? Enum.Parse(pt, args[i], true)
                : Convert.ChangeType(args[i], pt, System.Globalization.CultureInfo.InvariantCulture);
        }

        var result = method.Invoke(target, values);
        Plugin.L.LogInfo($"call {typeName}.{methodName} -> {result ?? "void"}");
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
                PropertyInfo[] props;
                try
                {
                    props = ty.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                }
                catch
                {
                    continue;
                }

                foreach (var p in props)
                {
                    try
                    {
                        if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                    }
                    catch
                    {
                        continue;
                    }

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
        if (UnityEngine.XR.XRSettings.enabled)
            ScreenCapture.CaptureScreenshot(file, ScreenCapture.StereoScreenCaptureMode.BothEyes);
        else
            ScreenCapture.CaptureScreenshot(file, scale);
        Plugin.L.LogInfo($"Screenshot -> {file}");
    }
}
