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
    private float _devMoveUntil, _devLookUntil, _slideUntil;
    private Vector2 _slide;
    private bool _slideWorld;

    private void Awake()
    {
        _cmdPath = Path.Combine(Plugin.DataDir, "cmd.txt");
        if (File.Exists(_cmdPath)) File.Delete(_cmdPath); // never replay commands from a previous run
        Plugin.L.LogInfo($"DevTools ready. Graphics: {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceVersion} on {SystemInfo.graphicsDeviceName}");
    }

    private void LateUpdate() => DevRecorder.Capture();

    private void Update()
    {
        if (EyeWatchFrames > 0) EyeWatchFrames--;
        if (Time.unscaledTime >= _nextPoll)
        {
            _nextPoll = Time.unscaledTime + 0.25f;
            LogSceneChanges();
            PollCommands();
            ApplyPendingHand();
        }

        DevRecorder.Tick(Run);
        if (_slideUntil > 0)
        {
            var player = GameManager.Instance?.Player;
            var head = InkVR.Core.VRRig.Head;
            if (player == null || head == null || InkVR.Body.InkClock.Now >= _slideUntil) _slideUntil = 0;
            else
            {
                var fwd = _slideWorld ? Vector3.forward : Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                var right = _slideWorld ? Vector3.right : Vector3.Cross(Vector3.up, fwd);
                var k = InkVR.Core.VRRig.ActiveScale;
                var v = (fwd * _slide.y + right * _slide.x) * k + Vector3.down * (2f * k);
                DevRecorder.MoveDir = (fwd * _slide.y + right * _slide.x).normalized;
                player.CharacterController.Move(v * InkVR.Body.InkClock.Dt);
            }
        }

        if (_perf) TrackPerf();

        if (_devMoveUntil > 0)
        {
            var on = InkVR.Body.InkClock.Now < _devMoveUntil;
            VirtualInput.Move = on ? _devMove : Vector2.zero;
            if (!on) _devMoveUntil = 0;
        }

        if (_devLookUntil > 0)
        {
            var on = InkVR.Body.InkClock.Now < _devLookUntil;
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
            case "rinfo":
            {
                // rinfo <path>: every renderer under it (static batching, mesh, readability, materials)
                var root = Resolve(arg);
                if (root == null) break;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    var mesh = mf != null ? mf.sharedMesh : null;
                    var mr = r.TryCast<MeshRenderer>();
                    var mats = string.Join(",", r.sharedMaterials.Select(m => m != null ? m.name + "/" + m.shader.name : "-"));
                    Plugin.L.LogInfo($"RINFO {PathOf(r.transform)} {r.GetIl2CppType().Name} on={r.enabled}/{r.gameObject.activeInHierarchy} static={r.isPartOfStaticBatch} " +
                                     $"mesh={(mesh != null ? mesh.name + " v" + mesh.vertexCount + " sub" + mesh.subMeshCount + " read=" + mesh.isReadable : "-")} " +
                                     $"start={(mr != null ? mr.subMeshStartIndex : -1)} size={r.bounds.size} mats={mats}");
                }

                break;
            }
            case "laseraim":
            {
                // laseraim <ui path> [dx dy] | off : the dev right hand points the laser at a menu element (dx, dy: pixels off it)
                if (arg == "off")
                {
                    InkVR.UI.LaserPointer.DevForce = false;
                    XrInput.DevRightAim = null;
                    XrInput.DevRightTrigger = null;
                    break;
                }

                var la = arg.Split(' ');
                var nums = Enumerable.Reverse(Enumerable.Reverse(la).TakeWhile(v => v.Length > 0 && (char.IsDigit(v[0]) || v[0] == '-')).ToArray()).ToArray();
                var path = string.Join(' ', la.Take(la.Length - nums.Length));
                var t = Resolve(path);
                var rt = t != null ? t.GetComponent<RectTransform>() : null;
                var panel = InkVR.Core.VRRig.PointerPanel;
                Camera uiCam = null;
                foreach (var cam in Camera.allCameras)
                    if (cam.name == "UICamera")
                        uiCam = cam;
                if (rt == null || panel == null || uiCam == null || InkVR.Core.VRRig.ActiveOrigin == null)
                {
                    Plugin.L.LogWarning($"laseraim: rect={rt != null} panel={panel != null} uiCam={uiCam != null}");
                    break;
                }

                var px = (Vector2)RectTransformUtility.WorldToScreenPoint(uiCam, rt.TransformPoint(rt.rect.center));
                if (nums.Length >= 2) px += new Vector2(float.Parse(nums[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(nums[1], System.Globalization.CultureInfo.InvariantCulture));
                var world = panel.Transform.TransformPoint(new Vector3(px.x / InkVR.UI.UICapture.Width - 0.5f, px.y / InkVR.UI.UICapture.Height - 0.5f, 0));
                var o = InkVR.Core.VRRig.ActiveOrigin;
                var target = o.InverseTransformPoint(world) - Vector3.up * InkVR.Core.VRRig.HeightLift;
                var hand = new Vector3(0.2f, 1.25f, 0.25f);
                XrInput.DevRightAim = (hand, Quaternion.LookRotation(target - hand));
                InkVR.UI.LaserPointer.DevForce = true;
                XrInput.DevRightTrigger ??= 0f;
                Plugin.L.LogInfo($"LASERAIM {PathOf(t)} pixel {px}");
                break;
            }
            case "laserstate":
                Plugin.L.LogInfo($"LASER onScreen={InkVR.UI.LaserPointer.OnScreen} last=[{InkVR.UI.LaserPointer.LastClick}]");
                break;
            case "eyewatch":
            {
                // eyewatch <frames>: log every camera pre-render (name, eye) for a few frames: is each eye its own pass?
                EyeWatchFrames = int.Parse(arg);
                if (_eyeWatch == null)
                {
                    _eyeWatch = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new System.Action<Camera>(cam =>
                    {
                        if (EyeWatchFrames <= 0) return;
                        Plugin.L.LogInfo($"EYEPASS f={Time.frameCount} {cam.name} eye={cam.stereoActiveEye} stereo={cam.stereoEnabled} target={(cam.targetTexture != null ? cam.targetTexture.name : "-")}");
                    }));
                    var existing = Camera.onPreRender;
                    Camera.onPreRender = existing == null ? _eyeWatch : Il2CppSystem.Delegate.Combine(existing, _eyeWatch).Cast<Camera.CameraCallback>();
                }

                break;
            }
            case "rb":
            {
                // rb <path>: the physics body on it (or above it): kinematic or loose, where, how fast
                var t = Resolve(arg);
                var rbd = t != null ? t.GetComponentInParent<Rigidbody>() : null;
                Plugin.L.LogInfo(rbd == null ? $"RB {arg}: none" : $"RB {PathOf(rbd.transform)} kinematic={rbd.isKinematic} sleeping={rbd.IsSleeping()} pos={rbd.position} vel={rbd.velocity} mass={rbd.mass}");
                break;
            }
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
            case "optstate":
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<OptionsMenuController>()))
                {
                    var c = o.Cast<OptionsMenuController>();
                    if (!c.gameObject.activeInHierarchy) continue;
                    var labels = new List<string>();
                    if (c.m_ActiveMenuItemButtons != null)
                        foreach (var b in c.m_ActiveMenuItemButtons) labels.Add(b == null ? "null" : b.name);
                    Plugin.L.LogInfo($"OPTSTATE item={c.m_CurrentMenuItem} selected={c.m_SelectedIndex} max={c.m_IndexMax} active=[{string.Join(", ", labels)}] vrOpen={InkVR.UI.VRSettingsMenu.AnyOpen(c)} vrNav={InkVR.UI.VRSettingsMenu.NavSelectedFor(c)} esSelected={UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject?.name} advArrows={c.m_AdvancedBtn.m_Arrows?.name}:{c.m_AdvancedBtn.m_Arrows?.activeSelf}");
                }

                break;
            }
            case "inkscan":
            {
                // inkscan [radius]: renderers/particles that look like ink, near the player
                var radius = arg.Length > 0 ? float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) : 60f;
                var center = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                var groups = new Dictionary<string, (int n, Vector3 size, string example)>();
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Renderer>()))
                {
                    var r = o.Cast<Renderer>();
                    if (!r.gameObject.scene.IsValid() || !r.gameObject.activeInHierarchy || !r.enabled) continue;
                    if (Vector3.Distance(r.bounds.center, center) > radius) continue;
                    var m = r.sharedMaterial;
                    var tex = m != null && m.HasProperty("_MainTex") && m.mainTexture != null ? m.mainTexture.name : "-";
                    var key = $"{r.GetIl2CppType().Name} | go={r.name} | mat={m?.name} | shader={m?.shader?.name} | tex={tex}";
                    var hay = key.ToLowerInvariant();
                    if (!(hay.Contains("ink") || hay.Contains("drip") || hay.Contains("puddle") || hay.Contains("splat") || hay.Contains("spill") || hay.Contains("decal") || hay.Contains("goo") || hay.Contains("flow"))) continue;
                    var gk = $"{r.GetIl2CppType().Name} | mat={m?.name} | shader={m?.shader?.name} | tex={tex}";
                    groups.TryGetValue(gk, out var g);
                    groups[gk] = (g.n + 1, r.bounds.size, g.example ?? PathOf(r.transform));
                }

                foreach (var kv in groups.OrderByDescending(k => k.Value.n))
                    Plugin.L.LogInfo($"INK x{kv.Value.n} {kv.Key} | size~{kv.Value.size} | e.g. {kv.Value.example}");
                Plugin.L.LogInfo($"INK scan done ({groups.Count} groups within {radius})");
                break;
            }
            case "handat":
            {
                // handat <path> [seconds] [dx dy dz metres]: the dev right hand (its grip) to an object's position, plus an offset
                var ha = arg.Split(' ');
                var nums = ha.Skip(1).Where(v => v.Length > 0 && (char.IsDigit(v[0]) || v[0] == '-' || v[0] == '.')).ToArray();
                var path = string.Join(' ', ha.Take(ha.Length - nums.Length));
                var t = Resolve(path);
                if (t == null || InkVR.Core.VRRig.ActiveOrigin == null) break;
                var k = InkVR.Core.VRRig.ActiveScale;
                var off = nums.Length >= 4 ? new Vector3(float.Parse(nums[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(nums[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(nums[3], System.Globalization.CultureInfo.InvariantCulture)) * k : Vector3.zero;
                var local = InkVR.Core.VRRig.ActiveOrigin.InverseTransformPoint(t.position + off) - Vector3.up * ModConfig.HeightOffset.Value;
                DevRecorder.HandGoTo(local, nums.Length > 0 ? float.Parse(nums[0], System.Globalization.CultureInfo.InvariantCulture) : 0f);
                Plugin.L.LogInfo($"HANDAT {PathOf(t)} {t.position} + {off}");
                break;
            }
            case "touch":
            {
                // touch [n] [metres out] [seconds]: the n-th nearest pressable thing; the dev right hand just in front of it, then pushed in
                var pa = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var n = pa.Length > 0 ? int.Parse(pa[0]) : 0;
                var info = Input.PhysicalTouch.DevNearest(n, out var at, out var normal);
                Plugin.L.LogInfo("PRESS " + info);
                if (pa.Length > 1 && InkVR.Core.VRRig.ActiveOrigin != null)
                {
                    var k = InkVR.Core.VRRig.ActiveScale;
                    var off = float.Parse(pa[1], System.Globalization.CultureInfo.InvariantCulture);
                    // grip 12 cm behind the fingertip
                    var grip = at + normal * ((off + 0.12f) * k);
                    var local = InkVR.Core.VRRig.ActiveOrigin.InverseTransformPoint(grip) - Vector3.up * ModConfig.HeightOffset.Value;
                    var look = Quaternion.LookRotation(InkVR.Core.VRRig.ActiveOrigin.InverseTransformDirection(-normal), Vector3.up);
                    if (InkVR.Body.Hands.DevRight.HasValue) InkVR.Body.Hands.DevRight = (InkVR.Body.Hands.DevRight.Value.pos, look);
                    DevRecorder.HandGoTo(local, pa.Length > 2 ? float.Parse(pa[2], System.Globalization.CultureInfo.InvariantCulture) : 0f);
                }

                break;
            }
            case "door":
            {
                // door: the nearest hinged door and its physical state | door look: turn to face it
                if (arg.StartsWith("stand"))
                {
                    // door stand <metres> [side]
                    var sa = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    Plugin.L.LogInfo("DOOR " + Input.PhysicalDoors.DevStand(sa.Length > 1 ? float.Parse(sa[1], System.Globalization.CultureInfo.InvariantCulture) : 0.6f, sa.Length > 2 ? int.Parse(sa[2]) : 1));
                    break;
                }

                if (arg.StartsWith("cam"))
                {
                    // door cam <side> <metres out> <u along> <up>
                    var ca = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    Plugin.L.LogInfo("DOOR " + Input.PhysicalDoors.DevCam((int)ca[0], ca[1], ca[2], ca[3]));
                    break;
                }

                if (arg.StartsWith("hand"))
                {
                    // door hand <u> <h> <off> [seconds]
                    var ha = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    Plugin.L.LogInfo("DOOR " + Input.PhysicalDoors.DevHand(ha[0], ha[1], ha[2], ha.Length > 3 ? ha[3] : 0f));
                    break;
                }

                if (arg.StartsWith("game"))
                {
                    // door game open <speed> <angle> | door game x | door game close <speed>
                    var ga = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    Input.PhysicalDoors.DevGameOpen(ga.Length > 1 ? ga[1] : "open", ga.Length > 2 ? float.Parse(ga[2], System.Globalization.CultureInfo.InvariantCulture) : 1f, ga.Length > 3 ? float.Parse(ga[3], System.Globalization.CultureInfo.InvariantCulture) : 90f);
                    break;
                }

                if (arg == "list")
                {
                    Plugin.L.LogInfo("DOORS" + Input.PhysicalDoors.DevList());
                    break;
                }

                Plugin.L.LogInfo("DOOR " + Input.PhysicalDoors.DevNearest(out var hinge));
                if (arg == "look" && hinge != null) InkVR.Core.VRRig.DevLookAt = hinge.position;
                break;
            }
            case "intersurvey":
            {
                // intersurvey: every Interactable, grouped by its own type and the scripts on it and above it -> dumps/intersurvey_<scene>.txt
                var dir = Path.Combine(Plugin.DataDir, "dumps");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"intersurvey_{SceneManager.GetActiveScene().name}_{DateTime.Now:HHmmss}.txt");
                using var w = new StreamWriter(file);
                var groups = new Dictionary<string, List<Interactable>>();
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Interactable>()))
                {
                    var it = o.Cast<Interactable>();
                    if (!it.gameObject.scene.IsValid()) continue;
                    var owners = new List<string>();
                    var t = it.transform;
                    for (var up = 0; up < 3 && t != null; up++, t = t.parent)
                        foreach (var c in t.GetComponents<MonoBehaviour>())
                        {
                            var n = c.GetIl2CppType().Name;
                            if (n != "Interactable" && !owners.Contains(n)) owners.Add(n);
                        }

                    var key = $"{it.GetIl2CppType().Name} <- {string.Join(",", owners.Take(4))}";
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Interactable>();
                    list.Add(it);
                }

                foreach (var kv in groups.OrderByDescending(g => g.Value.Count))
                {
                    w.WriteLine($"== x{kv.Value.Count} {kv.Key}");
                    foreach (var it in kv.Value.Take(4))
                    {
                        var col = it.GetComponent<Collider>();
                        var r = it.GetComponentInChildren<Renderer>();
                        w.WriteLine($"   {PathOf(it.transform)} active={it.gameObject.activeInHierarchy} col={(col != null ? col.bounds.size.ToString() : "-")} rend={(r != null ? r.bounds.size.ToString() : "-")} single={it.m_EnableSingleInteraction}");
                    }
                }

                Plugin.L.LogInfo($"INTERSURVEY -> {file}");
                break;
            }
            case "compsurvey":
            {
                // compsurvey Type1,Type2,... [n]: every live instance of each game component type, with its fields
                // (transforms with local rotation, colliders with sizes) -> dumps/compsurvey_<scene>.txt
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var per = a.Length > 1 ? int.Parse(a[1]) : 10;
                var dir = Path.Combine(Plugin.DataDir, "dumps");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"compsurvey_{SceneManager.GetActiveScene().name}_{DateTime.Now:HHmmss}.txt");
                using var w = new StreamWriter(file);
                string Val(object v)
                {
                    switch (v)
                    {
                        case null: return "null";
                        case Transform tr: return $"T:{tr.name} lrot={tr.localEulerAngles} lpos={tr.localPosition} kids={tr.childCount}";
                        case Collider col: return $"C:{col.GetIl2CppType().Name}:{col.name} size={col.bounds.size} trig={col.isTrigger} en={col.enabled}";
                        case Component comp: return $"{comp.GetIl2CppType().Name}:{comp.name}";
                        case Object uo: return $"{uo.GetIl2CppType().Name}:{uo.name}";
                        case Il2CppSystem.Object io: return io.ToString();
                        default: return v.ToString();
                    }
                }

                foreach (var tn in a[0].Split(','))
                {
                    var type = InteropType(tn);
                    if (type == null)
                    {
                        w.WriteLine($"== {tn}: type not found");
                        continue;
                    }

                    var all = Resources.FindObjectsOfTypeAll(Il2CppType.From(type)).Select(o => o.Cast<Component>()).Where(c => c.gameObject.scene.IsValid()).ToList();
                    w.WriteLine($"== {tn}: {all.Count} ({all.Count(c => c.gameObject.activeInHierarchy)} active)");
                    var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(pi => pi.GetIndexParameters().Length == 0 && !pi.Name.StartsWith("Native")).ToList();
                    foreach (var c in all.Take(per))
                    {
                        var o = Activator.CreateInstance(type, c.Pointer);
                        w.WriteLine($"   {PathOf(c.transform)} active={c.gameObject.activeInHierarchy} pos={c.transform.position}");
                        foreach (var pi in props)
                        {
                            string v;
                            try { v = Val(pi.GetValue(o)); }
                            catch (Exception e) { v = "!" + e.GetType().Name; }
                            w.WriteLine($"      {pi.Name} = {v}");
                        }

                        foreach (var col in c.GetComponentsInChildren<Collider>(true).Take(6))
                            w.WriteLine($"      collider {PathOf(col.transform).Replace(PathOf(c.transform), "~")} {col.GetIl2CppType().Name} size={col.bounds.size} trig={col.isTrigger} layer={col.gameObject.layer}");
                    }
                }

                Plugin.L.LogInfo($"COMPSURVEY -> {file}");
                break;
            }
            case "inksurvey":
            {
                // inksurvey: everything ink-like in the level, grouped, with components and shapes -> dumps/inksurvey_<scene>.txt
                var dir = Path.Combine(Plugin.DataDir, "dumps");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"inksurvey_{SceneManager.GetActiveScene().name}_{DateTime.Now:HHmmss}.txt");
                using var w = new StreamWriter(file);
                string Comps(GameObject go) => string.Join(",", go.GetComponents<Component>().Select(c => c.GetIl2CppType().Name).Where(n => n != "Transform"));
                var groups = new Dictionary<string, List<Renderer>>();
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Renderer>()))
                {
                    var r = o.Cast<Renderer>();
                    if (!r.gameObject.scene.IsValid()) continue;
                    var m = r.sharedMaterial;
                    var tex = m != null && m.HasProperty("_MainTex") && m.mainTexture != null ? m.mainTexture.name : "-";
                    var mf = r.GetComponent<MeshFilter>();
                    var mesh = mf?.sharedMesh;
                    var hay = $"{r.name} {m?.name} {m?.shader?.name} {tex} {mesh?.name}".ToLowerInvariant();
                    if (!(hay.Contains("ink") || hay.Contains("drip") || hay.Contains("puddle") || hay.Contains("splat") || hay.Contains("spill") || hay.Contains("decal") || hay.Contains("goo") || hay.Contains("flow") || hay.Contains("pour") || hay.Contains("fall") || hay.Contains("stream") || hay.Contains("leak"))) continue;
                    var key = $"{r.GetIl2CppType().Name} | mat={m?.name} | shader={m?.shader?.name} | tex={tex} | mesh={mesh?.name} readable={mesh?.isReadable}";
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Renderer>();
                    list.Add(r);
                }

                foreach (var kv in groups.OrderByDescending(k => k.Value.Count))
                {
                    w.WriteLine($"== x{kv.Value.Count} {kv.Key}");
                    foreach (var r in kv.Value.Take(6))
                        w.WriteLine($"   {PathOf(r.transform)} active={r.gameObject.activeInHierarchy}/{r.enabled} bounds={r.bounds.center}/{r.bounds.size} lossy={r.transform.lossyScale} rot={r.transform.eulerAngles} comps=[{Comps(r.gameObject)}]");
                }

                w.WriteLine("\n######## particle systems");
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<ParticleSystem>()))
                {
                    var ps = o.Cast<ParticleSystem>();
                    if (!ps.gameObject.scene.IsValid()) continue;
                    var psr = ps.GetComponent<ParticleSystemRenderer>();
                    var m = psr?.sharedMaterial;
                    var main = ps.main;
                    w.WriteLine($"PS {PathOf(ps.transform)} active={ps.gameObject.activeInHierarchy} playing={ps.isPlaying} mat={m?.name} shader={m?.shader?.name} max={main.maxParticles} mode={psr?.renderMode} mesh={psr?.mesh?.name} at={ps.transform.position} bounds={psr?.bounds.size}");
                }

                w.WriteLine("\n######## decal and ink components");
                foreach (var tn in new[] { "Decal", "InkWaterController", "CH1BarrelInk", "InkPipeController", "CH3InkWaterController", "OldDecalLinker", "EventTrigger" })
                {
                    var t = InteropType(tn);
                    if (t == null) { w.WriteLine($"type {tn}: not found"); continue; }
                    var n = 0;
                    foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.From(t)))
                    {
                        var c = o.Cast<Component>();
                        if (!c.gameObject.scene.IsValid()) continue;
                        n++;
                        if (tn == "EventTrigger" && !PathOf(c.transform).ToLowerInvariant().Contains("ink")) continue;
                        if (n > 40) continue;
                        var col = c.GetComponent<Collider>();
                        w.WriteLine($"{tn} {PathOf(c.transform)} active={c.gameObject.activeInHierarchy} pos={c.transform.position} lossy={c.transform.lossyScale} rot={c.transform.eulerAngles} collider={(col != null ? col.GetIl2CppType().Name + " " + col.bounds.center + "/" + col.bounds.size + " trigger=" + col.isTrigger : "-")} comps=[{Comps(c.gameObject)}]");
                    }

                    w.WriteLine($"type {tn}: {n} total");
                }

                Plugin.L.LogInfo($"INKSURVEY -> {file}");
                break;
            }
            case "fxscan":
            {
                // Components on every camera, plus lens flares and flare-ish objects (stereo troublemakers).
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Camera>()))
                {
                    var cam = o.Cast<Camera>();
                    if (!cam.gameObject.scene.IsValid() && cam.gameObject.scene.name != "DontDestroyOnLoad") continue;
                    var comps = cam.GetComponents<Component>().Select(c => c.GetIl2CppType().Name);
                    Plugin.L.LogInfo($"FX cam {PathOf(cam.transform)} enabled={cam.enabled} depth={cam.depth} target={(cam.targetTexture != null ? cam.targetTexture.name : "-")}: {string.Join(", ", comps)}");
                }

                foreach (var t in new[] { "LensFlare", "FlareLayer", "Projector" })
                {
                    var type = Il2CppInterop.Runtime.Il2CppClassPointerStore.GetNativeClassPointer(typeof(Camera)) != IntPtr.Zero ? Il2CppSystem.Type.GetType("UnityEngine." + t + ", UnityEngine.CoreModule") : null;
                    if (type == null) continue;
                    var n = 0;
                    foreach (var o in Object.FindObjectsOfType(type))
                    {
                        if (n++ < 6) Plugin.L.LogInfo($"FX {t}: {PathOf(o.Cast<Component>().transform)}");
                    }

                    Plugin.L.LogInfo($"FX {t}: {n} total");
                }

                var groups = new Dictionary<string, int>();
                foreach (var o in Object.FindObjectsOfType(Il2CppType.Of<Renderer>()))
                {
                    var r = o.Cast<Renderer>();
                    var key = $"{r.GetIl2CppType().Name} mat={r.sharedMaterial?.name} shader={r.sharedMaterial?.shader?.name}";
                    var hay = (r.name + " " + key).ToLowerInvariant();
                    if (!(hay.Contains("beam") || hay.Contains("shaft") || hay.Contains("flare") || hay.Contains("glow") || hay.Contains("ray") || hay.Contains("volum") || hay.Contains("halo") || hay.Contains("cone") || hay.Contains("light"))) continue;
                    groups.TryGetValue(key + " e.g. " + r.name, out var c);
                    groups[key + " e.g. " + r.name] = c + 1;
                }

                foreach (var kv in groups.OrderByDescending(k => k.Value).Take(25)) Plugin.L.LogInfo($"FX x{kv.Value} {kv.Key}");
                break;
            }
            case "interhere":
            {
                // Walk up to the nearest interactable (optionally by name) and hold the right hand over it.
                var player = GameManager.Instance.Player;
                Interactable best = null;
                var bestD = float.MaxValue;
                foreach (var o in Object.FindObjectsOfType(Il2CppType.Of<Interactable>()))
                {
                    var it = o.Cast<Interactable>();
                    if (!it.gameObject.activeInHierarchy || (arg.Length > 0 && PathOf(it.transform).IndexOf(arg, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    var d = (it.transform.position - player.transform.position).sqrMagnitude;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = it;
                    }
                }

                if (best == null)
                {
                    Plugin.L.LogInfo("interhere: none");
                    break;
                }

                var col = best.GetComponentInChildren<Collider>();
                var target = col != null ? col.bounds.center : best.transform.position;
                var away = Vector3.ProjectOnPlane(player.transform.position - target, Vector3.up);
                if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
                var stand = target + away.normalized * 2.2f;
                var cc = player.CharacterController;
                cc.enabled = false;
                player.transform.position = new Vector3(stand.x, player.transform.position.y, stand.z);
                cc.enabled = true;
                InkVR.Core.VRRig.DevPendingTouch = target;
                Plugin.L.LogInfo($"interhere: {PathOf(best.transform)} at {target}");
                break;
            }
            case "interstate":
            {
                var ic = GameManager.Instance.Player?.m_Interaction;
                var it = ic?.Interactable;
                Plugin.L.LogInfo($"INTERSTATE target={(it != null ? PathOf(it.transform) : "none")} hoverHand={InkVR.Input.InteractionAim.HoverHand?.ToString() ?? "none"} found=[{InkVR.Input.InteractionAim.LastFind}] ignore={ic?.m_IgnoreLayers.value} player={GameManager.Instance.Player.transform.position}");
                break;
            }
            case "beams":
                // beams fix|nofix|off|on
                if (arg == "nofix") InkVR.Rendering.VolumetricStereo.DevNoFix = true;
                else if (arg == "fix") InkVR.Rendering.VolumetricStereo.DevNoFix = false;
                else if (arg == "off" || arg == "on") ModConfig.VolumetricLightVR.Value = arg == "on";
                Plugin.L.LogInfo($"beams: on={ModConfig.VolumetricLightVR.Value} perEyeFix={!InkVR.Rendering.VolumetricStereo.DevNoFix}");
                break;
            case "lookat":
            {
                // lookat <path> [dropUnits]: stand 1.7 m tall and look at an object (or below it)
                var lookArgs = arg.Split('|');
                var t = Resolve(lookArgs[0]);
                var drop = lookArgs.Length > 1 ? float.Parse(lookArgs[1], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                InkVR.Core.VRRig.DevLookAt = t.position - Vector3.up * drop;
                break;
            }
            case "callall":
            {
                // callall Type.Method: a no-argument method on every live instance (dev: switch on a chapter's puzzle)
                var dot = arg.LastIndexOf('.');
                var type = InteropType(arg[..dot]);
                var method = type?.GetMethod(arg[(dot + 1)..], BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (method == null) { Plugin.L.LogWarning($"callall: {arg} not found"); break; }
                var n = 0;
                foreach (var o in Object.FindObjectsOfType(Il2CppType.From(type)))
                {
                    method.Invoke(Activator.CreateInstance(type, o.Pointer), null);
                    n++;
                }

                Plugin.L.LogInfo($"callall {arg}: {n} instances");
                break;
            }
            case "goto2":
            {
                // goto2 <path>: stand on the floor 2 units from an object (any height: basements, upper floors), facing it
                var t = Resolve(arg);
                var player = GameManager.Instance.Player;
                var mask = ~(1 << player.gameObject.layer);
                var offset = Physics.Raycast(player.transform.position, Vector3.down, out var under, 50f, mask, QueryTriggerInteraction.Ignore) ? player.transform.position.y - under.point.y : 1f;
                var flat = Vector3.ProjectOnPlane(player.transform.position - t.position, Vector3.up);
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
                var stand = t.position + flat.normalized * 2f;
                var y = Physics.Raycast(new Vector3(stand.x, t.position.y + 2f, stand.z), Vector3.down, out var floor, 30f, mask, QueryTriggerInteraction.Ignore) ? floor.point.y + offset : t.position.y;
                var cc = player.CharacterController;
                cc.enabled = false;
                player.transform.position = new Vector3(stand.x, y, stand.z);
                cc.enabled = true;
                Plugin.L.LogInfo($"goto2 {PathOf(t)} at {t.position}, standing at {player.transform.position}");
                break;
            }
            case "goto":
            {
                // goto <path>: stand 3 units from an object, facing it
                var t = Resolve(arg);
                var player = GameManager.Instance.Player;
                var flat = Vector3.ProjectOnPlane(player.transform.position - t.position, Vector3.up);
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
                var stand = t.position + flat.normalized * 3f;
                var cc = player.CharacterController;
                cc.enabled = false;
                player.transform.position = new Vector3(stand.x, player.transform.position.y, stand.z);
                cc.enabled = true;
                Plugin.L.LogInfo($"goto {PathOf(t)} at {t.position}, standing at {stand}");
                break;
            }
            case "wheel":
            {
                // wheel open | wheel <x> <y> (tilt the stick while it's open) | wheel close (let go: pick) | wheel off
                var wa = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (wa.Length > 0 && wa[0] == "open") InkVR.UI.WeaponWheel.DevClick = true;
                else if (wa.Length > 0 && wa[0] == "close") InkVR.UI.WeaponWheel.DevClick = false;
                else if (wa.Length > 0 && wa[0] == "off") { InkVR.UI.WeaponWheel.DevClick = null; InkVR.UI.WeaponWheel.DevStick = null; }
                else if (wa.Length > 1) InkVR.UI.WeaponWheel.DevStick = new Vector2(float.Parse(wa[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(wa[1], System.Globalization.CultureInfo.InvariantCulture));
                Plugin.L.LogInfo($"WHEEL open={InkVR.UI.WeaponWheel.IsOpen} weapon={InkVR.Body.WeaponHandling.WeaponName} holstered={InkVR.Body.WeaponHandling.Holstered}");
                break;
            }
            case "holster":
                if (!InkVR.Body.WeaponHandling.PutAway("(dev)")) InkVR.Body.WeaponHandling.Draw();
                break;
            case "inventory":
                InkVR.UI.InventoryButton.Show();
                break;
            case "pull":
            {
                // pull <path>: point the right hand at it, grip and hold (gravity pull) | pull state
                if (arg == "state")
                {
                    Plugin.L.LogInfo($"PULL {InkVR.Input.GravityPull.Targets} last=[{InkVR.Input.GravityPull.LastPull}]");
                    break;
                }

                var pt = Resolve(arg);
                if (pt == null) { Plugin.L.LogWarning("pull: not found"); break; }
                var pr = pt.GetComponentInChildren<Renderer>();
                InkVR.Input.GravityPull.DevPull(pr != null ? pr.bounds.center : pt.position);
                Plugin.L.LogInfo($"PULL pointing at {PathOf(pt)}");
                break;
            }
            case "inkburst":
                InkVR.Body.InkWorld.Splatter(arg.Length > 0 ? int.Parse(arg) : 14);
                break;
            case "hurt":
            {
                // hurt: the game's hurt border, the way a hit shows it (silent; it makes the controller on first use);
                // hurt hide; hurt state: what the VR visor draws
                var gm = GameManager.Instance;
                if (gm == null) break;
                if (arg.StartsWith("fov "))
                {
                    // hurt fov <half yaw> <half pitch> (degrees; 0 0 = the headset's own view)
                    var f = arg.Split(' ');
                    InkVR.UI.HurtVisor.HalfYaw = float.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture);
                    InkVR.UI.HurtVisor.HalfPitch = float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (arg != "state" && arg != "hide") gm.ShowHurtBorder(true);
                var hb = gm.m_HurtBordersController;
                if (hb == null) { Plugin.L.LogInfo("HURT no controller"); break; }
                if (arg == "hide") hb.HideBorder();
                var vc = InkVR.Core.VRRig.GameCamera;
                Plugin.L.LogInfo($"HURT hits={hb.m_HitCount}/{hb.m_HitMax} isHit={hb.m_IsHit} visor showing={InkVR.UI.HurtVisor.Showing}" +
                                 (vc != null ? $" | eye view {vc.fieldOfView:F0} deg tall, aspect {vc.aspect:F2}" : ""));
                break;
            }
            case "pool":
            {
                // pool [gain <x> | poke [depth m] [metres ahead]]: the ink pool's surface simulation
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (a.Length > 1 && a[0] == "gain") InkVR.Body.InkPool.Gain = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                if (a.Length > 0 && a[0] == "poke")
                {
                    var h = InkVR.Core.VRRig.Head;
                    var k = InkVR.Core.VRRig.ActiveScale;
                    var ahead = (a.Length > 2 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : 0.8f) * k;
                    var at = h.position + Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized * ahead;
                    InkVR.Body.InkPool.Poke(at.x, at.z, a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 0.02f, 2.5f);
                }

                if (a.Length > 0 && a[0] == "ridges") InkVR.Body.InkPool.DevRidges(a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 0.03f);
                if (a.Length > 1 && a[0] == "shine") InkVR.Body.InkPool.ShineGain = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                if (a.Length > 2 && a[0] == "crisp")
                {
                    InkVR.Body.InkPool.Threshold = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                    InkVR.Body.InkPool.Crisp = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
                }

                if (a.Length > 2 && a[0] == "studio")
                {
                    InkVR.Body.InkPool.Studio = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                    InkVR.Body.InkPool.CrestK = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
                    if (a.Length > 3) InkVR.Body.InkPool.StudioSharp = int.Parse(a[3]);
                }

                if (a.Length > 1 && a[0] == "tilt") InkVR.Body.InkPool.Exaggerate = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                if (a.Length > 1 && a[0] == "mat") InkVR.Body.InkPool.DevMaterial(a[1]);
                if (a.Length > 0 && a[0] == "off") InkVR.Body.InkPool.DevOff = true;
                if (a.Length > 0 && a[0] == "on") InkVR.Body.InkPool.DevOff = false;
                if (a.Length > 0 && a[0] == "peak") Plugin.L.LogInfo("POOLPEAK " + InkVR.Body.InkPool.DevPeak());
                if (a.Length > 0 && a[0] == "state") Plugin.L.LogInfo("POOLSTATE " + InkVR.Body.InkPool.DevState());
                Plugin.L.LogInfo($"POOL active={InkVR.Body.InkPool.Active} {InkVR.Body.InkPool.Name} top={InkVR.Body.InkPool.Top:F2} maxH={InkVR.Body.InkPool.MaxHeight * 1000f:F1} mm gain={InkVR.Body.InkPool.Gain} splashes={InkVR.Body.InkPool.Splashes} caught={InkVR.Body.InkPool.Caught} probed={InkVR.Body.InkPool.Probed} walls={InkVR.Body.InkPool.Walls} drops={InkVR.Body.InkDrops.Alive} ms push/sim/draw={InkVR.Body.InkPool.MsPush:F2}/{InkVR.Body.InkPool.MsSim:F2}/{InkVR.Body.InkPool.MsDraw:F2}");
                break;
            }
            case "inkpause":
                // inkpause | inkpause off | inkpause stains (only the stains' runs, drying and uploads)
                InkVR.Body.InkWorld.DevOff = arg == "" || arg == "on";
                InkVR.Body.InkWorld.DevStainsOff = arg == "stains";
                InkVR.Body.InkStains.DevSkip = arg is "runs" or "dry" or "upload" ? arg : "";
                if (InkVR.Body.InkStains.DevSkip != "") InkVR.Body.InkWorld.DevOff = false;
                break;
            case "inkclear":
                // Henry, the floor and the drops in the air all clean again (between takes).
                InkVR.Body.InkStains.WashAll();
                InkVR.Body.InkDrops.Clear();
                InkVR.Body.WorldInk.Clear();
                InkVR.Body.Footprints.Clear();
                break;
            case "drench":
                InkVR.Body.InkWorld.Soak("dev");
                break;
            case "wade":
                // wade <metres of Henry above the floor> | wade off
                InkVR.Body.InkWorld.DevWadeDepth = arg == "off" ? null : float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "inkbody":
            {
                var sb = new System.Text.StringBuilder("INKBODY ");
                var body = InkVR.Body.Hands.Body;
                sb.Append($"body={(body != null ? body.Style.ToString() : "none")} wade={InkVR.Body.InkWorld.WadeSurface?.ToString("F2") ?? "-"} ({InkVR.Body.InkWorld.WadeWhy}) ");
                sb.Append($"footstep={GameManager.Instance?.Player?.CurrentFootstepType} prints={InkVR.Body.Footprints.Count} soles={InkVR.Body.Footprints.LoadLeft:F2}/{InkVR.Body.Footprints.LoadRight:F2} inInk={InkVR.Body.Footprints.InInk} ");
                sb.Append($"drops={InkVR.Body.InkDrops.Alive} splats={InkVR.Body.WorldInk.Count} dripHits={InkVR.Body.InkWorld.DripHits} ({InkVR.Body.InkWorld.LastDripHit}) ");
                var rb = InkVR.Body.Hands.Bounds(Hand.Right);
                sb.Append($"rightHand y={rb.min.y:F2}..{rb.max.y:F2} devPool={(InkVR.Body.InkWorld.DevPool.HasValue ? InkVR.Body.InkWorld.DevPool.Value.top.ToString("F2") : "-")}; ");
                foreach (var part in InkVR.Body.InkStains.Parts)
                {
                    if (!part.Visible) continue;
                    var cov = 0;
                    var wet = 0;
                    for (var i = 0; i < part.Cov.Length; i++)
                    {
                        if (part.Cov[i] > 128) cov++;
                        if (part.Wet[i] > 128) wet++;
                    }

                    if (cov > 0) sb.Append($"{part.T.name}[{part.Region}] ink={cov * 100 / Math.Max(1, part.Map?.Count ?? part.Cov.Length)}% wet={wet * 100 / Math.Max(1, part.Map?.Count ?? part.Cov.Length)}% runs={part.Runs.Count} ");
                }

                Plugin.L.LogInfo(sb.ToString());
                break;
            }
            case "grip":
            {
                // grip r|l <0..1> | grip r|l off
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                float? v = a[1] == "off" ? null : float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                if (a[0].StartsWith("l")) InkVR.Input.XrInput.DevLeftGrip = v;
                else InkVR.Input.XrInput.DevRightGrip = v;
                break;
            }
            case "handto":
            {
                // handto <Component> [n]: put the dev right hand on the n-th nearest one (palm towards it, from the head)
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var type = InteropType(a[0]);
                var nth = a.Length > 1 ? int.Parse(a[1]) : 0;
                var from = InkVR.Core.VRRig.Head.position;
                var targets = new List<(float d, Vector3 at, string name)>();
                foreach (var o in Object.FindObjectsOfType(Il2CppType.From(type)))
                {
                    var c = o.Cast<Component>();
                    // the part you'd actually take hold of: its interactable's collider, else its mesh
                    var inter = c.GetComponentInChildren<Interactable>();
                    var col = inter != null ? inter.GetComponent<Collider>() : null;
                    var r = c.GetComponentInChildren<Renderer>();
                    var at = col != null ? col.bounds.center : r != null ? r.bounds.center : c.transform.position;
                    targets.Add(((at - from).magnitude, at, PathOf(inter != null ? inter.transform : c.transform)));
                }

                var pick = targets.OrderBy(x => x.d).Skip(nth).FirstOrDefault();
                if (pick.name == null) { Plugin.L.LogInfo("HANDTO none"); break; }
                var dir = (pick.at - from).normalized;
                SetDevRightWorld(pick.at - dir * (0.03f * InkVR.Core.VRRig.ActiveScale), Quaternion.LookRotation(dir));
                Plugin.L.LogInfo($"HANDTO {pick.name} at {pick.at} ({pick.d:F2} from the head)");
                break;
            }
            case "handmouth":
            {
                var h = InkVR.Core.VRRig.Head;
                SetDevRightWorld(h.position + h.rotation * new Vector3(0.02f, -0.09f, 0.08f) * InkVR.Core.VRRig.ActiveScale, h.rotation);
                break;
            }
            case "handshoulder":
            {
                var h = InkVR.Core.VRRig.Head;
                var s2 = InkVR.Core.VRRig.ActiveScale;
                var fwd = Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized;
                var right = Vector3.Cross(Vector3.up, fwd);
                SetDevRightWorld(h.position + right * (0.14f * s2) - fwd * (0.12f * s2) - Vector3.up * (0.12f * s2), Quaternion.LookRotation(-fwd));
                break;
            }
            case "handturn":
            {
                // twist the dev right hand about its pointing axis
                if (!InkVR.Body.Hands.DevRight.HasValue) break;
                var d = InkVR.Body.Hands.DevRight.Value;
                InkVR.Body.Hands.DevRight = (d.pos, d.rot * Quaternion.AngleAxis(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), Vector3.forward));
                break;
            }
            case "hittest":
            {
                // hittest <path>: the old interface lookup against the type-system check, for each collider under it
                var root = Resolve(arg);
                if (root == null) break;
                foreach (var c in root.GetComponentsInChildren<Collider>(true))
                {
                    var old = c.GetComponent<IHittable>();
                    Plugin.L.LogInfo($"HITTEST {PathOf(c.transform)}: GetComponent<IHittable>={(old != null ? "yes" : "no")} parent={(c.GetComponentInParent<IHittable>() != null ? "yes" : "no")} real={InkVR.Body.Hittable.Is(c)} trigger={c.isTrigger}");
                }

                break;
            }
            case "lhandhandle":
            {
                // lhandhandle <t>: the dev left hand on the held axe's handle (0 the dominant hand's end, 1 the head)
                var origin = InkVR.Core.VRRig.ActiveOrigin;
                if (origin == null || !InkVR.Body.MeleeSwing.HandleLine(out var near, out var far) || !InkVR.Body.Hands.DevRight.HasValue) { Plugin.L.LogInfo("LHANDHANDLE no handle"); break; }
                var at = Vector3.Lerp(near, far, float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
                InkVR.Body.Hands.DevLeft = (origin.InverseTransformPoint(at), InkVR.Body.Hands.DevRight.Value.rot);
                Plugin.L.LogInfo($"LHANDHANDLE at {at} (handle {near} -> {far})");
                break;
            }
            case "valvewind":
            {
                // valvewind deg: wind the dev right hand round the valve being turned, about its axis
                var v = Input.PhysicalInteract.DevValve;
                var origin = InkVR.Core.VRRig.ActiveOrigin;
                if (!v.HasValue || origin == null || !InkVR.Body.Hands.DevRight.HasValue) { Plugin.L.LogInfo("VALVEWIND no valve"); break; }
                var d = InkVR.Body.Hands.DevRight.Value;
                var q = Quaternion.AngleAxis(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), v.Value.axis);
                var world = origin.TransformPoint(d.pos);
                var to = v.Value.centre + q * (world - v.Value.centre);
                var lq = Quaternion.Inverse(origin.rotation) * q * origin.rotation;
                InkVR.Body.Hands.DevRight = (origin.InverseTransformPoint(to), lq * d.rot);
                Plugin.L.LogInfo($"VALVEWIND turned {v.Value.turned:F0}");
                break;
            }
            case "lhand":
            {
                // lhand dx dy dz: the dev left hand where the right hand is, plus an offset (real metres, world axes)
                if (!InkVR.Body.Hands.DevRight.HasValue) break;
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var d = InkVR.Body.Hands.DevRight.Value;
                var origin = InkVR.Core.VRRig.ActiveOrigin;
                var delta = origin != null ? origin.InverseTransformVector(new Vector3(a[0], a[1], a[2]) * InkVR.Core.VRRig.ActiveScale) : Vector3.zero;
                InkVR.Body.Hands.DevLeft = (d.pos + delta, d.rot);
                break;
            }
            case "lhandlid":
            {
                // the dev left hand's palm on the held can's lid
                var lid = Input.PhysicalInteract.DevLid;
                var origin = InkVR.Core.VRRig.ActiveOrigin;
                if (!lid.HasValue || origin == null || !InkVR.Body.Hands.DevRight.HasValue) { Plugin.L.LogInfo("LHANDLID no lid"); break; }
                var rot = InkVR.Body.Hands.DevRight.Value.rot;
                var worldRot = origin.rotation * rot;
                var grip = lid.Value - worldRot * new Vector3(0.03f, -0.01f, 0.04f) * InkVR.Core.VRRig.ActiveScale;
                InkVR.Body.Hands.DevLeft = (origin.InverseTransformPoint(grip) - Vector3.up * ModConfig.HeightOffset.Value, rot);
                break;
            }
            case "lhandmove":
            {
                if (!InkVR.Body.Hands.DevLeft.HasValue) break;
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var d = InkVR.Body.Hands.DevLeft.Value;
                var origin = InkVR.Core.VRRig.ActiveOrigin;
                var delta = origin != null ? origin.InverseTransformVector(new Vector3(a[0], a[1], a[2]) * InkVR.Core.VRRig.ActiveScale) : Vector3.zero;
                InkVR.Body.Hands.DevLeft = (d.pos + delta, d.rot);
                break;
            }
            case "manual":
                Plugin.L.LogInfo($"MANUAL gesture={Input.PhysicalInteract.Active} last='{Input.PhysicalInteract.LastResult}' darts={Input.MinigameThrow.Owns} '{Input.MinigameThrow.Last}' twoHandAxe={Body.WeaponHandling.OffHandOnMelee}");
                break;
            case "handmove":
            {
                // handmove dx dy dz (real metres, world axes)
                if (!InkVR.Body.Hands.DevRight.HasValue) break;
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var d = InkVR.Body.Hands.DevRight.Value;
                var origin = InkVR.Core.VRRig.ActiveOrigin;
                var worldDelta = new Vector3(a[0], a[1], a[2]);
                var localDelta = origin != null ? origin.InverseTransformVector(worldDelta * InkVR.Core.VRRig.ActiveScale) : worldDelta;
                InkVR.Body.Hands.DevRight = (d.pos + localDelta, d.rot);
                break;
            }
            case "attackheld":
            {
                // the held weapon's own attack (a directly loaded chapter ignores the attack button)
                var w = GameManager.Instance?.Player?.WeaponGameObject?.GetComponent<BaseWeapon>();
                if (w == null) { Plugin.L.LogInfo("ATTACKHELD nothing held"); break; }
                w.OnAttack();
                Plugin.L.LogInfo($"ATTACKHELD {w.name}");
                break;
            }
            case "devvel":
                InkVR.Body.WeaponHandling.DevVelocity = arg == "off" ? null : (Vector3?)ParseV3(arg);
                break;
            case "gesture":
                Plugin.L.LogInfo($"GESTURE active={InkVR.Input.PhysicalInteract.Active} last='{InkVR.Input.PhysicalInteract.LastResult}' hover={InkVR.Input.InteractionAim.Hovered?.name} ({(InkVR.Input.InteractionAim.Hovered != null ? InkVR.Input.PhysicalInteract.Classify(InkVR.Input.InteractionAim.Hovered).ToString() : "-")})");
                break;
            case "throwstate":
                Plugin.L.LogInfo($"THROW last='{InkVR.Body.WeaponHandling.LastThrow}' twoHands={InkVR.Body.WeaponHandling.OffHandOnGun} holstered={InkVR.Body.WeaponHandling.Holstered} held={GameManager.Instance?.Player?.WeaponGameObject?.name} inactive={GameManager.Instance?.Player?.InactiveWeapon?.name}");
                break;
            case "inkstate":
                Plugin.L.LogInfo($"INKSTATE splats={InkVR.Body.InkWorld.DecalCount} pools={InkVR.Body.InkWorld.PoolCount} walls={InkVR.Body.InkWorld.WallCount} spouts={InkVR.Body.InkWorld.SpoutCount} emitters={InkVR.Body.InkWorld.ParticleCount} puddles={InkVR.Body.InkWorld.PuddleCount} parts={InkVR.Body.InkStains.Parts.Count} ready={InkVR.Body.InkStains.Parts.Count(p => p.Ready)} deposits={InkVR.Body.InkStains.SplatCount} caps={InkVR.Body.InkBody.Caps.Count}");
                break;
            case "inkdebug":
            {
                foreach (var part in InkVR.Body.InkStains.Parts.Take(6))
                {
                    var r = part.R;
                    var used = r.sharedMaterial;
                    var tex = used?.GetTexture("_MainTex");
                    var dark = 0;
                    for (var i = 0; i < part.Px.Length; i++) if (part.Px[i].r < 40) dark++;
                    var gpu = part.Tex.GetPixel(part.W / 2, part.H / 2);
                    Plugin.L.LogInfo($"INKDBG {part.T.name}: mat={used?.name} isOurs={(used != null && used.Pointer == part.Mat.Pointer)} tex={tex?.name} isOursTex={(tex != null && tex.Pointer == part.Tex.Pointer)} darkPx={dark}/{part.Px.Length} inked={part.Inked} ready={part.Ready} dirty={part.Dirty} texFormat={part.Tex.format} readable={part.Tex.isReadable} gpuMid={gpu}");
                }

                break;
            }
            case "devpool":
            {
                // devpool <depth m> | off: a fake ink pool around you, <depth> metres of Henry deep
                if (arg == "off") { InkVR.Body.InkWorld.DevPool = null; break; }
                var k = InkVR.Core.VRRig.ActiveScale;
                var h = InkVR.Core.VRRig.Head.position;
                var ground = InkVR.Body.Hands.GroundAt(h);
                var depth = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) * k;
                InkVR.Body.InkWorld.DevPool = (new Bounds(new Vector3(h.x, ground + depth * 0.5f, h.z), new Vector3(3f * k, depth + 0.01f, 3f * k)), ground + depth);
                Plugin.L.LogInfo($"DEVPOOL top={ground + depth:F2} (ground {ground:F2})");
                break;
            }
            case "dripon":
            {
                // dripon <seconds> [drops per second]: ink dripping onto the right hand from 40 cm above it
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                InkVR.Body.InkWorld.DevDripUntil = InkVR.Body.InkClock.Now + float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture);
                InkVR.Body.InkWorld.DevDripRate = a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 25f;
                break;
            }
            case "underdrip":
            {
                // underdrip [n]: stand by the n-th nearest dripping emitter and hold the right hand under its drops
                // underdrip name:<part of a name> [n] goes to one even before it's dripping (it starts once it's on screen)
                var player = GameManager.Instance.Player;
                var ua = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string named = ua.Length > 0 && ua[0].StartsWith("name:") ? ua[0].Substring(5).ToLowerInvariant() : null;
                var nth = ua.Length > (named != null ? 1 : 0) ? int.Parse(ua[named != null ? 1 : 0]) : 0;
                var ps = InkVR.Body.InkWorld.NearestDripping(player.transform.position, nth, named);
                if (ps == null) { Plugin.L.LogInfo("UNDERDRIP none dripping"); break; }
                var max = ps.main.maxParticles;
                var buf = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<ParticleSystem.Particle>(Math.Max(max, 16));
                var n = ps.GetParticles(buf);
                var sum = Vector3.zero;
                for (var i = 0; i < n; i++) sum += buf[i].m_Position;
                var at = n > 0 ? sum / n : ps.transform.position;
                if (n > 0 && !ps.GetComponent<ParticleSystemRenderer>().bounds.Contains(at)) at = ps.transform.TransformPoint(at);
                var k = InkVR.Core.VRRig.ActiveScale;
                var mask = ~(1 << player.gameObject.layer);
                var ground = Physics.Raycast(new Vector3(at.x, at.y + 0.5f, at.z), Vector3.down, out var hit, 40f, mask, QueryTriggerInteraction.Ignore) ? hit.point.y : player.transform.position.y;
                var feetOffset = Physics.Raycast(player.transform.position, Vector3.down, out var under, 50f, mask, QueryTriggerInteraction.Ignore) ? player.transform.position.y - under.point.y : 1f;
                var away = Vector3.ProjectOnPlane(player.transform.position - at, Vector3.up);
                if (away.sqrMagnitude < 1e-4f) away = Vector3.back;
                var stand = new Vector3(at.x, ground + feetOffset, at.z) + away.normalized * (0.45f * k);
                var cc = player.CharacterController;
                cc.enabled = false;
                player.transform.position = stand;
                cc.enabled = true;
                PendingHand = new Vector3(at.x, ground + 1.15f * k, at.z);
                Plugin.L.LogInfo($"UNDERDRIP {PathOf(ps.transform)} particles={n} centre={at} ground={ground:F2}, standing at {stand}");
                break;
            }
            case "mathbench":
            {
                // mathbench: what Unity's math costs per call in this build (interop) vs plain .NET
                const int N = 200000;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var v = new Vector3(1, 2, 3);
                var acc = Vector3.zero;
                for (var i = 0; i < N; i++) acc = acc + v;
                var tAdd = sw.Elapsed.TotalMilliseconds; sw.Restart();
                for (var i = 0; i < N; i++) acc = acc * 0.999f;
                var tMul = sw.Elapsed.TotalMilliseconds; sw.Restart();
                float f = 0;
                for (var i = 0; i < N; i++) f += Mathf.Min(i, 5f);
                var tMin = sw.Elapsed.TotalMilliseconds; sw.Restart();
                for (var i = 0; i < N; i++) f += Math.Min(i, 5f);
                var tMath = sw.Elapsed.TotalMilliseconds; sw.Restart();
                var m = Matrix4x4.TRS(Vector3.one, Quaternion.identity, Vector3.one);
                for (var i = 0; i < N; i++) acc = m.MultiplyPoint3x4(v);
                var tMat = sw.Elapsed.TotalMilliseconds; sw.Restart();
                for (var i = 0; i < N; i++) acc = new Vector3(i, 1, 2);
                var tNew = sw.Elapsed.TotalMilliseconds; sw.Restart();
                Color32 c = default;
                for (var i = 0; i < N; i++) c = new Color32((byte)i, 1, 2, 255);
                var tCol = sw.Elapsed.TotalMilliseconds; sw.Restart();
                for (var i = 0; i < N; i++) f += Vector3.Dot(acc, v);
                var tDot = sw.Elapsed.TotalMilliseconds; sw.Restart();
                for (var i = 0; i < N; i++) f += (acc - v).sqrMagnitude;
                var tSq = sw.Elapsed.TotalMilliseconds; sw.Restart();
                Plugin.L.LogInfo($"MATHBENCH ns/call: v+v {tAdd * 1e6 / N:F0}, v*f {tMul * 1e6 / N:F0}, Mathf.Min {tMin * 1e6 / N:F0}, Math.Min {tMath * 1e6 / N:F0}, MultiplyPoint3x4 {tMat * 1e6 / N:F0}, new Vector3 {tNew * 1e6 / N:F0}, new Color32 {tCol * 1e6 / N:F0}, Dot {tDot * 1e6 / N:F0}, (a-b).sqrMagnitude {tSq * 1e6 / N:F0} ({f}{c.r}{acc.x})");
                break;
            }
            case "slide":
            {
                // slide <forward m/s> <right m/s> <seconds>: move Henry through his character controller (works where the
                // game ignores the move input, e.g. a directly loaded chapter)
                // slide w <x m/s> <z m/s> <seconds>: in world directions
                var parts2 = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                _slideWorld = parts2[0] == "w";
                if (_slideWorld) parts2.RemoveAt(0);
                var a = parts2.Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                _slide = _slideWorld ? new Vector2(a[0], a[1]) : new Vector2(a[1], a[0]);
                _slideUntil = InkVR.Body.InkClock.Now + a[2];
                break;
            }
            case "rec":
                DevRecorder.Command(arg);
                break;
            case "at":
                DevRecorder.At(arg, Run);
                break;
            case "handgo":
                DevRecorder.HandGo(arg);
                break;
            case "shake":
                InkVR.Body.InkWorld.DevShake = arg.Length > 0 ? float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) : 130f;
                break;
            case "vpscale":
            {
                var v = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                var d = InkVR.Core.XrManager.Display;
                d.scaleOfAllViewports = v;
                Plugin.L.LogInfo($"scaleOfAllViewports={d.scaleOfAllViewports} supportsViewportScaling={d.supportedTextureLayouts}");
            }
                break;
            case "cfg":
            {
                // cfg Section.Key value  (session only, not saved)
                var a = arg.Split(' ');
                var dot = a[0].IndexOf('.');
                foreach (var kv in ModConfig.File)
                {
                    if (kv.Key.Section != a[0][..dot] || kv.Key.Key != a[0][(dot + 1)..]) continue;
                    var saveOld = ModConfig.File.SaveOnConfigSet;
                    ModConfig.File.SaveOnConfigSet = false;
                    kv.Value.SetSerializedValue(a[1]);
                    ModConfig.File.SaveOnConfigSet = saveOld;
                    Plugin.L.LogInfo($"cfg {a[0]} = {kv.Value.BoxedValue}");
                }

                break;
            }
            case "sway":
                InkVR.Core.VRRig.DevSwayAmount = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "groundcheck":
                InkVR.Core.VRRig.DevUngrounded = 0;
                InkVR.Core.VRRig.DevGroundCount = int.Parse(arg);
                break;
            case "stick":
            {
                var a = arg.Split(' ').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                InkVR.Input.VirtualGamepad.DevStick(new Vector2(a[0], a[1]), a.Length > 2 ? a[2] : 1f);
                break;
            }
            case "trace":
                InkVR.Core.VRRig.DevTraceFrames = int.Parse(arg);
                break;
            case "enemies":
            {
                // enemies [n]: every enemy in the level (asleep too), nearest first, with its health and colliders
                var from = GameManager.Instance.Player.transform.position;
                var list = new List<(float d, string text)>();
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Ai.BaseAiController>()))
                {
                    var ai = o.Cast<Ai.BaseAiController>();
                    if (!ai.gameObject.scene.IsValid()) continue;
                    var cols = ai.GetComponentsInChildren<Collider>(true);
                    var cs = string.Join(", ", cols.Take(6).Select(c => $"{c.name}{(c.isTrigger ? "(trig)" : "")}/L{c.gameObject.layer}{(c.enabled ? "" : "(off)")}"));
                    list.Add(((ai.transform.position - from).magnitude,
                        $"{ai.GetIl2CppType().Name} {PathOf(ai.transform)} active={ai.gameObject.activeInHierarchy} enabled={ai.enabled} health={ai.m_CurrentHealth}/{ai.m_Health} colliders[{cols.Length}]: {cs}"));
                }

                foreach (var (d, text) in list.OrderBy(x => x.d).Take(arg.Length > 0 ? int.Parse(arg) : 12)) Plugin.L.LogInfo($"ENEMY {d:F1} {text}");
                Plugin.L.LogInfo($"ENEMIES {list.Count}");
                break;
            }
            case "invoke":
            {
                // invoke Type.Method: call a method (private too) on the first live instance, every argument null
                // (dev: fire a scripted moment's trigger handler, e.g. CH2SammyOfficeController.HandleKnockoutEventTriggerOnEnter)
                var dot = arg.LastIndexOf('.');
                var type = InteropType(arg[..dot]);
                var name = arg[(dot + 1)..];
                var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(m => m.Name == name);
                if (method == null) { Plugin.L.LogWarning($"invoke: {arg} not found"); break; }
                var found = Object.FindObjectOfType(Il2CppType.From(type));
                if (found == null) { Plugin.L.LogWarning($"invoke: no live {arg[..dot]}"); break; }
                method.Invoke(Activator.CreateInstance(type, found.Pointer), new object[method.GetParameters().Length]);
                Plugin.L.LogInfo($"invoke {arg}: done");
                break;
            }
            case "turn":
                // turn <degrees>: a snap turn, the same path as the stick
                InkVR.Core.VRRig.Turn(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "devtilt":
            {
                // devtilt <degrees> <seconds>: roll and pitch the player object like a scripted knockout does
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                InkVR.Core.VRRig.DevTiltRoll = float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture);
                InkVR.Core.VRRig.DevTiltUntil = Time.unscaledTime + float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                break;
            }
            case "camwatch":
                // camwatch <frames>: what the game does to its camera during scripted moments
                InkVR.Core.VRRig.DevCamWatch = int.Parse(arg);
                break;
            case "chapter":
            {
                // chapter CH2: load a chapter directly (test runs only: saving is disabled there)
                if (!DevSaveGuard.Active)
                {
                    Plugin.L.LogWarning("chapter: refused, only in dev test sessions (INKVR_DEV_NOSAVE=1)");
                    break;
                }

                var cc = Object.FindObjectOfType(Il2CppType.Of<ChapterController>())?.Cast<ChapterController>();
                if (cc == null)
                {
                    Plugin.L.LogInfo("chapter: no ChapterController in this scene");
                    break;
                }

                Plugin.L.LogInfo($"chapter: loading {arg}");
                cc.LoadChapter(arg);
                break;
            }
            case "weapons":
            {
                var player = GameManager.Instance.Player;
                Plugin.L.LogInfo($"WEAPONS held={(player?.WeaponGameObject != null ? PathOf(player.WeaponGameObject.transform) + " active=" + player.WeaponGameObject.activeInHierarchy : "none")} inactive={(player?.InactiveWeapon != null ? player.InactiveWeapon.name : "none")} player={player?.transform.position}");
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<BaseWeapon>()))
                {
                    var w = o.Cast<BaseWeapon>();
                    if (!w.gameObject.scene.IsValid()) continue;
                    Plugin.L.LogInfo($"WEAPON {w.GetIl2CppType().Name} {PathOf(w.transform)} at {w.transform.position} active={w.gameObject.activeInHierarchy} equipped={w.m_IsEquipped} range={w.m_WeaponRange} interaction={(w.m_Interaction != null ? w.m_Interaction.name : "-")}");
                }

                break;
            }
            case "swing":
            {
                // swing [x<n>] <path>: stand within reach of an object and swing the held melee weapon down through it
                // (x3: chop-chop three times, straight back up between, no pause)
                var chops = 1;
                if (arg.Length > 3 && arg[0] == 'x' && char.IsDigit(arg[1]))
                {
                    var sp = arg.IndexOf(' ');
                    chops = int.Parse(arg[1..sp]);
                    arg = arg[(sp + 1)..];
                }

                var t = Resolve(arg);
                var col = t.GetComponentsInChildren<Collider>().FirstOrDefault(c => !c.isTrigger);
                var target = col != null && col.enabled ? col.bounds.center : t.position; // a switched-off (ragdoll) collider has no bounds
                var player = GameManager.Instance.Player;
                var scale = InkVR.Core.VRRig.ActiveScale;
                var reach = (InkVR.Body.MeleeSwing.HeadInAim?.magnitude ?? 0.8f) + 0.15f;
                var away = Vector3.ProjectOnPlane(player.transform.position - target, Vector3.up);
                if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
                var stand = target + away.normalized * reach * scale;
                var cc = player.CharacterController;
                cc.enabled = false;
                player.transform.position = new Vector3(stand.x, player.transform.position.y, stand.z);
                cc.enabled = true;
                InkVR.Body.Hands.DevRight = null;
                InkVR.Input.XrInput.DevRightAim ??= (new Vector3(0.25f, 1.2f, 0.3f), Quaternion.identity);
                InkVR.Body.MeleeSwing.DevSwing(target, chops);
                Plugin.L.LogInfo($"swing: {PathOf(t)} ({(col != null ? col.name : "no collider")}) at {target}, standing {reach:F2} m away");
                break;
            }
            case "gamestate":
            {
                // gamestate PLAY: after a direct chapter load (test runs), let the chapter play and lift the black fade
                var gm = GameManager.Instance;
                gm.m_GameState = Enum.Parse<GameState>(arg, true);
                gm.m_ScreenBlockerController?.DOFade(0f, 0.3f, 0f);
                Plugin.L.LogInfo($"gamestate -> {gm.m_GameState}");
                break;
            }
            case "near":
            {
                // near <Component> [count]: the closest live objects with this component, by distance from the player
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var type = InteropType(a[0]);
                var count = a.Length > 1 ? int.Parse(a[1]) : 8;
                var from = GameManager.Instance.Player.transform.position;
                var list = new List<(float d, string text)>();
                foreach (var o in Object.FindObjectsOfType(Il2CppType.From(type)))
                {
                    var c = o.Cast<Component>();
                    var col = c.GetComponent<Collider>();
                    var at = col != null ? col.bounds.center : c.transform.position;
                    list.Add(((at - from).magnitude, $"{PathOf(c.transform)} at {at}"));
                }

                foreach (var (d, text) in list.OrderBy(x => x.d).Take(count)) Plugin.L.LogInfo($"NEAR {d:F1} {text}");
                break;
            }
            case "equip":
            {
                // equip <name>: pick up a weapon as if interacted with, even from a closed weapon station (test runs)
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<BaseWeapon>()))
                {
                    var w = o.Cast<BaseWeapon>();
                    if (!w.gameObject.scene.IsValid() || w.m_IsEquipped || w.name.IndexOf(arg, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    for (var t = w.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
                    w.OnInteracted();
                    Plugin.L.LogInfo($"equip: {PathOf(w.transform)}");
                    break;
                }

                break;
            }
            case "body":
            {
                // body full|arms|hands|none (what you see) or a style's key, any case (lifelike, toon, bendy, inkdemon...)
                var style = Array.Find(InkVR.Body.Styles.Keys, k => k.Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (style != null) ModConfig.BodyStyle.Value = style;
                else ModConfig.BodyMode.Value = char.ToUpperInvariant(arg[0]) + arg.Substring(1).ToLowerInvariant();
                Plugin.L.LogInfo($"body: {ModConfig.BodyMode.Value}, {ModConfig.BodyStyle.Value}");
                break;
            }
            case "watch":
            {
                var kind = Array.Find(new[] { "Tank", "Bendy", "Studio", "Gauge", "Angel", "Demon", "None" }, k => k.Equals(arg, StringComparison.OrdinalIgnoreCase));
                if (kind != null) ModConfig.Watch.Value = kind;
                Plugin.L.LogInfo($"watch: {ModConfig.Watch.Value}");
                break;
            }
            case "arcade":
            {
                // arcade state | arcade throw <name> vx vy vz [x y z] | arcade progress
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                float N(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                if (a[0] == "throw") Plugin.L.LogInfo("ARCADE " + InkVR.UI.Arcade.DevThrow(a[1], new Vector3(N(2), N(3), N(4)), a.Length > 7 ? new Vector3(N(5), N(6), N(7)) : null));
                else if (a[0] == "progress") Plugin.L.LogInfo("PROGRESS " + InkVR.UI.Progress.Describe());
                else Plugin.L.LogInfo("ARCADE " + InkVR.UI.Arcade.Describe());
                break;
            }
            case "show":
                // show: start the theatre's opening again (house lights, follow spots, the curtain)
                InkVR.Core.VRRig.Cinema?.StartShow(true);
                break;
            case "glass":
            {
                // glass x y z yaw w h: a test mirror in the theatre (theatre metres, facing yaw), or "glass state"
                if (arg == "state")
                {
                    Plugin.L.LogInfo("MIRROR " + InkVR.UI.Mirror.Describe());
                    break;
                }

                if (arg == "dump" || arg == "render")
                {
                    InkVR.UI.Mirror.DevDump(Path.Combine(Plugin.DataDir, "shots"), arg == "render");
                    Plugin.L.LogInfo("MIRROR dumped");
                    break;
                }

                var m = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                float N(int i) => float.Parse(m[i], System.Globalization.CultureInfo.InvariantCulture);
                var root = InkVR.Core.VRRig.Cinema?.Root;
                if (root == null) break;
                new InkVR.UI.Mirror(root, new Vector3(N(0), N(1), N(2)), Quaternion.Euler(0, N(3), 0), N(4), N(5), new Color(0.9f, 0.9f, 0.92f));
                Plugin.L.LogInfo("mirror placed");
                break;
            }
            case "headshow":
                // headshow 1|0: draw your head as a mirror would see it (for checking each style's head)
                InkVR.Body.Hands.Body?.ShowHead(arg == "1");
                Plugin.L.LogInfo($"head shown: {arg == "1"}");
                break;
            case "bodystate":
            {
                var b = InkVR.Body.Hands.Body;
                Plugin.L.LogInfo(b == null ? "BODY none" : $"BODY {b.Style} pelvis={b.Pelvis} feet L={b.FootLeft} R={b.FootRight} shoulders L={b.ShoulderLeft} R={b.ShoulderRight} head={InkVR.Core.VRRig.Head?.position}");
                break;
            }
            case "curl":
                InkVR.Body.Hands.DevCurl = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "twalk":
            {
                // twalk x z yaw [floor] : stand in the theatre there
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                InkVR.UI.TheatreWalk.DevPlace(a[0], a[1], a.Length > 2 ? a[2] : 0f, a.Length > 3 ? a[3] : 0f);
                break;
            }
            case "tmove":
            {
                // tmove x y seconds : walk with the stick
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                InkVR.UI.TheatreWalk.DevStick(new Vector2(a[0], a[1]), a[2]);
                break;
            }
            case "tturn":
                InkVR.Core.VRRig.Turn(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "tstate":
                Plugin.L.LogInfo("TWALK " + InkVR.UI.TheatreWalk.Describe());
                Plugin.L.LogInfo("TTOYS " + InkVR.UI.TheatreToys.Describe());
                break;
            case "thand":
            {
                // thand r|l x y z [yaw pitch] : a dev hand at a theatre point (grip pose)
                var tp = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var a = tp.Skip(1).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var root = InkVR.Core.VRRig.Cinema?.Root;
                var o = InkVR.Core.VRRig.ActiveOrigin;
                if (root == null || o == null) break;
                var world = root.TransformPoint(new Vector3(a[0], a[1], a[2]));
                var wrot = root.rotation * Quaternion.Euler(a.Length > 4 ? a[4] : 0f, a.Length > 3 ? a[3] : 0f, 0);
                var lp = o.InverseTransformPoint(world) - Vector3.up * InkVR.Core.VRRig.HeightLift;
                var lr = Quaternion.Inverse(o.rotation) * wrot;
                if (tp[0].StartsWith("l")) InkVR.Body.Hands.DevLeft = (lp, lr);
                else InkVR.Body.Hands.DevRight = (lp, lr);
                break;
            }
            case "thandgo":
            {
                // thandgo x y z seconds : the dev right hand (grip pose) moves smoothly to a theatre point
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var root = InkVR.Core.VRRig.Cinema?.Root;
                var o = InkVR.Core.VRRig.ActiveOrigin;
                if (root == null || o == null) break;
                var lp = o.InverseTransformPoint(root.TransformPoint(new Vector3(a[0], a[1], a[2]))) - Vector3.up * InkVR.Core.VRRig.HeightLift;
                DevRecorder.HandGoTo(lp, a[3]);
                break;
            }
            case "tgrip":
            {
                // tgrip r|l on|off
                var tp = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                InkVR.UI.TheatreToys.DevGrip[tp[0].StartsWith("l") ? 0 : 1] = tp.Length < 2 || tp[1] == "on";
                break;
            }
            case "tshot":
            {
                // tshot name x y z yaw pitch [fov] : a picture from a point in the theatre
                var tp = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var a = tp.Skip(1).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var root = InkVR.Core.VRRig.Cinema?.Root;
                if (root == null) break;
                var at = root.TransformPoint(new Vector3(a[0], a[1], a[2]));
                var dir = root.rotation * (Quaternion.Euler(a[4], a[3], 0) * Vector3.forward);
                ShootFrom(tp[0], at, at + dir, root.up, true, a.Length > 5 ? a[5] : 80f);
                break;
            }
            case "pack":
                Plugin.L.LogInfo("PACK " + InkVR.Input.Backpack.Describe());
                break;
            case "packshoulder":
            {
                // packshoulder r|l : that dev hand over the left shoulder, where the satchel hangs
                var h = InkVR.Core.VRRig.Head;
                var s2 = InkVR.Core.VRRig.ActiveScale;
                var fwd = Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized;
                var right = Vector3.Cross(Vector3.up, fwd);
                var spot = h.position - right * (0.15f * s2) - fwd * (0.13f * s2) - Vector3.up * (0.12f * s2);
                var o = InkVR.Core.VRRig.ActiveOrigin;
                var lp = o.InverseTransformPoint(spot);
                var lr = Quaternion.Inverse(o.rotation) * Quaternion.LookRotation(-fwd);
                if (arg.StartsWith("l")) InkVR.Body.Hands.DevLeft = (lp, lr);
                else SetDevRightWorld(spot, Quaternion.LookRotation(-fwd));
                break;
            }
            case "kinds":
            {
                // kinds: every Interactable in the level and what a grip does with it (Take, Valve, Lever...)
                var n = 0;
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Interactable>()))
                {
                    var it = o.Cast<Interactable>();
                    if (!it.gameObject.scene.IsValid()) continue;
                    var kind = InkVR.Input.PhysicalInteract.Classify(it);
                    if (kind == InkVR.Input.PhysicalInteract.Kind.None && arg != "all") continue;
                    var r = it.GetComponentInChildren<Renderer>();
                    var owners = new List<string>();
                    var up = it.transform;
                    for (var u = 0; u < 3 && up != null; u++, up = up.parent)
                        foreach (var c in up.GetComponents<MonoBehaviour>())
                        {
                            var tn = c.GetIl2CppType().Name;
                            if (tn != "Interactable" && !owners.Contains(tn)) owners.Add(tn);
                        }

                    Plugin.L.LogInfo($"KIND {kind} {it.GetIl2CppType().Name}<-{string.Join(",", owners)} {PathOf(it.transform)} single={it.m_EnableSingleInteraction} size={(r != null ? (r.bounds.size / InkVR.Core.VRRig.ActiveScale).ToString() : "-")} active={it.gameObject.activeInHierarchy} enabled={it.enabled}");
                    if (++n > 3000) break;
                }

                break;
            }
            case "shotweapons":
                ShotWeapons = arg != "off";
                break;
            case "gripstate":
            {
                // where the weapon's handle runs relative to each hand's fist (the grip pose's +Z line through its origin)
                var have = InkVR.Body.Grips.WeaponLine(out var near, out var far);
                var sc = InkVR.Core.VRRig.ActiveScale;
                foreach (var h in new[] { Hand.Left, Hand.Right })
                {
                    var w = InkVR.Body.Hands.WristOf(h);
                    if (w == null || !have) continue;
                    var root = w.parent;
                    var dir = (far - near).normalized;
                    var off = Vector3.ProjectOnPlane(root.position - near, dir).magnitude / sc;
                    var along = Vector3.Dot(root.position - near, dir) / sc;
                    var ang = Vector3.Angle(root.forward, dir);
                    Plugin.L.LogInfo($"GRIP {h}: handle {(off * 100):F1} cm from the fist's line, {along * 100:F0} cm up from the knob, angle {ang:F0} deg (len {(far - near).magnitude / sc:F2} m) override={InkVR.Body.Grips.HandPose(h, out _, out _)} maxCurl={InkVR.Body.Grips.MaxCurl(h):F2}");
                }

                Plugin.L.LogInfo($"GRIP wall push {(InkVR.Body.Hands.WallPush.magnitude / sc * 100):F1} cm, tipped {InkVR.Body.Hands.WallTurn:F0} deg");

                break;
            }
            case "handshot":
            {
                // handshot <name> left|right back|palm|side|thumb [distance m]
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var hand = a[1] == "left" ? Hand.Left : Hand.Right;
                var w = InkVR.Body.Hands.WristOf(hand);
                if (w == null) break;
                var scale = InkVR.Core.VRRig.ActiveScale;
                var d = (a.Length > 3 ? float.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture) : 0.24f) * scale;
                var fingers = w.forward;
                var back = w.up;
                var thumbSide = w.right * (hand == Hand.Left ? 1f : -1f);
                var center = w.position + fingers * (0.07f * scale);
                var dir = a[2] switch { "palm" => -back, "side" => -thumbSide, "thumb" => thumbSide, _ => back };
                ShootFrom(a[0], center + dir * d, center, a[2] == "side" || a[2] == "thumb" ? back : fingers, InkVR.Core.VRRig.CinemaActive || !InkVR.Core.VRRig.InGame, 40f);
                break;
            }
            case "armshot":
            {
                // armshot <name> right|left [yaw deg] [distance m]: the whole hand and forearm from above, with a flash
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var hand = a.Length > 1 && a[1] == "left" ? Hand.Left : Hand.Right;
                var scale = InkVR.Core.VRRig.ActiveScale;
                var yaw = a.Length > 2 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                var d = (a.Length > 3 ? float.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture) : 0.35f) * scale;
                var wrist = InkVR.Body.Hands.WristOf(hand);
                if (wrist == null) break;
                // The hand and the end of the forearm, from above and in front.
                var center = wrist.position + wrist.forward * (0.02f * scale);
                var fwd = Vector3.ProjectOnPlane(InkVR.Core.VRRig.Head.forward, Vector3.up).normalized;
                var dir = Quaternion.Euler(0, yaw, 0) * (fwd * 0.6f + Vector3.up).normalized;
                ShootFrom(a[0], center + dir * d, center, fwd, false, 45f);
                break;
            }
            case "shotflash":
                ShotFlash = arg != "off";
                break;
            case "oshot":
            {
                // oshot <name> <object name> <distance m> <yaw> <pitch> [fov] : orbit-camera picture of an object (world yaw/pitch)
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Transform t = null;
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Transform>()))
                {
                    var tr = o.Cast<Transform>();
                    if (tr.name == a[1] && tr.gameObject.activeInHierarchy) { t = tr; break; }
                }

                if (t == null) { Plugin.L.LogWarning("oshot: not found " + a[1]); break; }
                float N(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                var d = N(2) * InkVR.Core.VRRig.ActiveScale;
                var r = t.GetComponentInChildren<Renderer>();
                var c = r != null ? r.bounds.center : t.position;
                var dir = Quaternion.Euler(N(4), N(3), 0) * Vector3.back;
                ShootFrom(a[0], c + dir * d, c, Vector3.up, InkVR.Core.VRRig.CinemaActive || !InkVR.Core.VRRig.InGame, a.Length > 5 ? N(5) : 40f);
                break;
            }
            case "closeshot":
            {
                // closeshot <name> <object path or name> <distance m> : look straight down the object's +Y from that far,
                // with the object's +X pointing up in the picture (for the watch: 3 o'clock up, so 12 should be on the left)
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Transform t = null;
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Transform>()))
                {
                    var tr = o.Cast<Transform>();
                    if (tr.name == a[1] && tr.gameObject.activeInHierarchy) { t = tr; break; }
                }

                if (t == null) { Plugin.L.LogWarning("closeshot: not found " + a[1]); break; }
                var d = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) * InkVR.Core.VRRig.ActiveScale;
                ShootFrom(a[0], t.position + t.up * d, t.position, t.right, InkVR.Core.VRRig.CinemaActive || !InkVR.Core.VRRig.InGame, 30f);
                break;
            }
            case "devlight":
            {
                // devlight on|off: a shadow-casting spotlight 2.5 m over the player, pointing down (to see Henry's shadow)
                var existing = GameObject.Find("InkVR_DevLight");
                if (existing != null) Object.Destroy(existing);
                if (arg == "off") break;
                var player = GameManager.Instance?.Player;
                if (player == null) break;
                var k = InkVR.Core.VRRig.ActiveScale;
                var go = new GameObject("InkVR_DevLight");
                go.transform.SetPositionAndRotation(player.transform.position + Vector3.up * 2.5f * k + player.transform.forward * 0.6f * k, Quaternion.LookRotation(Vector3.down, player.transform.forward));
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 80f;
                l.range = 6f * k;
                l.intensity = 4f;
                l.shadows = LightShadows.Soft;
                Plugin.L.LogInfo("DEVLIGHT on");
                break;
            }
            case "bodyshot":
                BodyShot(arg);
                break;
            case "swingstate":
                Plugin.L.LogInfo($"SWINGSTATE swings={InkVR.Body.MeleeSwing.Swings} hits={InkVR.Body.MeleeSwing.Hits} gameSwings={InkVR.Body.MeleeSwing.GameSwings} speed={InkVR.Body.MeleeSwing.Speed:F2} peak={InkVR.Body.MeleeSwing.PeakSpeed:F2} last=[{InkVR.Body.MeleeSwing.LastHit}] gunShot=[{InkVR.Body.GunAim.LastShot}]");
                break;
            case "findmethod":
            {
                // findmethod <name>: which loaded game types declare a method with this name
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.GetName().Name.StartsWith("Assembly-CSharp")) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); } catch { continue; }
                    foreach (var t in types)
                        foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                            if (arg.StartsWith("~") ? m.Name.IndexOf(arg.Substring(1), StringComparison.OrdinalIgnoreCase) >= 0 : m.Name == arg)
                                Plugin.L.LogInfo($"FOUND {t.FullName}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))}) static={m.IsStatic}");
                }

                break;
            }
            case "methods":
            {
                // methods <TypeName> [filter]: list a game type's methods (interop reflection)
                var a = arg.Split(' ');
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t;
                    try { t = asm.GetTypes().FirstOrDefault(x => x.Name == a[0]); } catch { continue; }
                    if (t == null) continue;
                    var names = t.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)
                        .Select(m => m.Name).Where(n => a.Length < 2 || n.IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0).Distinct().OrderBy(n => n);
                    var all = names.ToList();
                    for (var i = 0; i < all.Count; i += 40)
                        Plugin.L.LogInfo($"METHODS {t.FullName} : {t.BaseType?.FullName} [{i}]: {string.Join(", ", all.Skip(i).Take(40))}");
                    if (t.IsEnum) Plugin.L.LogInfo($"ENUM {t.Name}: {string.Join(", ", Enum.GetNames(t))}");
                    var props = t.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly)
                        .Select(p => $"{p.Name}:{p.PropertyType.Name}");
                    Plugin.L.LogInfo($"PROPS {t.Name}: {string.Join(", ", props)}");
                }

                break;
            }
            case "stereo":
                if (arg == "noscale") InkVR.Core.VRRig.DevNoStereoScale = true;
                else if (arg == "scale") InkVR.Core.VRRig.DevNoStereoScale = false;
                else InkVR.Core.VRRig.DevStereoReport = true;
                break;
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
                    InkVR.Core.VRRig.DevPendingTouch = null;
                    break;
                }

                var a = arg == "demo" || arg.Length == 0 ? new float[] { 0.17f, 1.38f, 0.32f, -35f, 0f }
                    : arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (a.Length == 1) a = new[] { 0.17f, 1.38f, 0.32f, a[0], 0f };
                if (a.Length < 6) a = a.Concat(new float[6 - a.Length]).ToArray();
                InkVR.Body.Hands.DevRight = (new Vector3(a[0], a[1], a[2]), Quaternion.Euler(a[3], a[4], a[5]));
                InkVR.Body.Hands.DevLeft = (new Vector3(-a[0], a[1], a[2]), Quaternion.Euler(a[3], -a[4], -a[5]));
                break;
            }
            case "aim":
            {
                // aim x y z pitch yaw | aim off   (right controller aim, tracking space)
                if (arg == "off") { InkVR.Input.XrInput.DevRightAim = null; break; }
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                InkVR.Input.XrInput.DevRightAim = (new Vector3(a[0], a[1], a[2]), Quaternion.Euler(a[3], a[4], 0));
                break;
            }
            case "trigger":
                InkVR.Input.XrInput.DevRightTrigger = arg == "off" ? null : float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "pad":
            {
                var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                InkVR.Input.VirtualGamepad.DevHold(a[0], a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 0.15f);
                break;
            }
            case "inputstats":
                Plugin.L.LogInfo("INPUT CALLS " + string.Join(", ", InkVR.Input.PlayerInputPatches.Calls.Select(kv => $"{kv.Key}={kv.Value}")));
                InkVR.Input.PlayerInputPatches.Calls.Clear();
                break;
            case "state":
            {
                var gm = GameManager.Instance;
                Plugin.L.LogInfo($"STATE isPaused={gm.isPaused} isPauseReady={gm.isPauseReady} state={gm.m_GameState} loaded={gm.isGameLoaded} timeScale={Time.timeScale} cursor={Cursor.lockState}/{Cursor.visible}");
                break;
            }
            case "pausegame":
            {
                var gm = GameManager.Instance;
                Plugin.L.LogInfo($"isPaused={gm.isPaused} isPauseReady={gm.isPauseReady} state={gm.m_GameState} dead={gm.isDead} loaded={gm.isGameLoaded} playerLocked={gm.Player?.isLocked} timeScale={Time.timeScale} menu={(gm.m_PauseMenu != null)}");
                if (arg == "off") gm.UnpauseGame(); else gm.PauseGame();
                Plugin.L.LogInfo($"-> isPaused={gm.isPaused}");
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
                if (parts[0] == "move") { _devMove = new Vector2(a[0], a[1]); _devMoveUntil = InkVR.Body.InkClock.Now + a[2]; }
                else { _devLook = new Vector2(a[0], a[1]); _devLookUntil = InkVR.Body.InkClock.Now + a[2]; }
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

    /// <summary>
    /// bodyshot name [orbit degrees] [distance m] [height m] [look-at height m]: a picture of Henry from outside
    /// (the headset only ever sees him from inside): orbit 0 is in front of him, 180 behind.
    /// </summary>
    private void BodyShot(string arg)
    {
        var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float F(int i, float d) => a.Length > i ? float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture) : d;
        var name = a.Length > 0 ? a[0] : "body";
        var orbit = F(1, 20f);
        var dist = F(2, 2.4f);
        var height = F(3, 1.1f);
        var lookHeight = F(4, 0.9f);
        var head = InkVR.Core.VRRig.Head;
        var origin = InkVR.Core.VRRig.ActiveOrigin;
        if (head == null || origin == null) return;
        var scale = InkVR.Core.VRRig.ActiveScale;
        var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 1e-4f) fwd = origin.forward;
        var floorY = origin.position.y;
        var body = InkVR.Body.Hands.Body;
        var center = body != null ? body.Pelvis : head.position;
        var target = new Vector3(center.x, floorY + lookHeight * scale, center.z);
        var dir = Quaternion.Euler(0, orbit, 0) * fwd;
        var camPos = new Vector3(center.x, floorY + height * scale, center.z) + dir * (dist * scale);

        ShootFrom(name, camPos, target, Vector3.up, InkVR.Core.VRRig.CinemaActive || !InkVR.Core.VRRig.InGame, 45f);
    }

    /// <summary>Render one picture from a temporary camera (not the headset's) and save it to shots/.</summary>
    /// <summary>Dev: shots include layer 10 (the held weapon).</summary>
    private static bool ShotWeapons;

    private static int EyeWatchFrames;
    private static Camera.CameraCallback _eyeWatch;

    /// <summary>Dev: the fill flash on dev pictures (off to judge shadows).</summary>
    private static bool ShotFlash = true;

    private void ShootFrom(string name, Vector3 camPos, Vector3 target, Vector3 up, bool cinema, float fov)
    {
        var scale = InkVR.Core.VRRig.ActiveScale;
        var go = new GameObject("InkVR_BodyShotCam");
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.cullingMask = cinema ? InkVR.UI.VRLayers.UIMask : ~InkVR.UI.VRLayers.UIMask & (ShotWeapons ? ~0 : ~(1 << 10));
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.04f, 0.03f);
        cam.fieldOfView = fov;
        cam.nearClipPlane = Mathf.Min(0.05f * scale, Vector3.Distance(camPos, target) * 0.3f);
        cam.farClipPlane = 60f * scale;
        go.transform.position = camPos;
        go.transform.rotation = Quaternion.LookRotation(target - camPos, up);
        var rt = new RenderTexture(1024, 1024, 24);
        cam.targetTexture = rt;
        // A flash at the camera, so dark corners of the studio don't hide what's being checked.
        var flash = cinema || !ShotFlash ? null : new GameObject("InkVR_ShotFlash").AddComponent<Light>();
        if (flash != null)
        {
            flash.type = LightType.Point;
            flash.range = Vector3.Distance(camPos, target) * 4f;
            flash.intensity = 1.4f;
            flash.transform.position = camPos;
        }

        cam.Render();
        if (flash != null) Object.Destroy(flash.gameObject);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        var dirPath = Path.Combine(Plugin.DataDir, "shots");
        Directory.CreateDirectory(dirPath);
        var file = Path.Combine(dirPath, $"{name}_{DateTime.Now:HHmmss}.png");
        File.WriteAllBytes(file, ImageConversion.EncodeToPNG(tex));
        cam.targetTexture = null;
        Object.Destroy(go);
        Object.Destroy(rt);
        Object.Destroy(tex);
        Plugin.L.LogInfo($"Body shot -> {file}");
    }

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

    /// <summary>Put the dev right hand at a world pose (converted into tracking space).</summary>
    private static void SetDevRightWorld(Vector3 worldPos, Quaternion worldRot)
    {
        var origin = InkVR.Core.VRRig.ActiveOrigin;
        if (origin == null) return;
        var local = origin.InverseTransformPoint(worldPos) - Vector3.up * ModConfig.HeightOffset.Value;
        InkVR.Body.Hands.DevRight = (local, Quaternion.Inverse(origin.rotation) * worldRot);
    }

    /// <summary>Dev: a world point for the right hand, applied once the rig has followed a teleport.</summary>
    private static Vector3? PendingHand;

    private static int _pendingHandFrames;

    private static void ApplyPendingHand()
    {
        if (!PendingHand.HasValue) return;
        if (++_pendingHandFrames < 5) return;
        _pendingHandFrames = 0;
        var head = InkVR.Core.VRRig.Head;
        var fwd = Vector3.ProjectOnPlane(PendingHand.Value - head.position, Vector3.up).normalized;
        SetDevRightWorld(PendingHand.Value, Quaternion.LookRotation(fwd) * Quaternion.Euler(10, 0, 0));
        PendingHand = null;
    }

    private static Vector3 ParseV3(string s)
    {
        var a = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return new Vector3(a[0], a[1], a[2]);
    }
}
