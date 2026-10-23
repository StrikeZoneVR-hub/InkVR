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
            case "inkburst":
                InkVR.Body.InkWorld.Splatter(arg.Length > 0 ? int.Parse(arg) : 14);
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
                sb.Append($"footstep={GameManager.Instance?.Player?.CurrentFootstepType} prints={InkVR.Body.Footprints.Count} soles={InkVR.Body.Footprints.LoadLeft:F2}/{InkVR.Body.Footprints.LoadRight:F2} inInk={InkVR.Body.Footprints.InInk}; ");
                foreach (var part in InkVR.Body.InkStains.Parts)
                {
                    if (!part.Visible) continue;
                    var dark = 0;
                    for (var i = 0; i < part.Px.Length; i++) if (part.Px[i].r < 40 && part.Base[i].r >= 40) dark++;
                    if (dark > 0) sb.Append($"{part.T.name}[{part.Region}]={dark * 100 / part.Px.Length}% ");
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
                Plugin.L.LogInfo($"INKSTATE decals={InkVR.Body.InkWorld.DecalCount} pools={InkVR.Body.InkWorld.PoolCount} emitters={InkVR.Body.InkWorld.ParticleCount} parts={InkVR.Body.InkStains.Parts.Count} splats={InkVR.Body.InkStains.SplatCount}");
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
                    Plugin.L.LogInfo($"INKDBG {part.T.name}: mat={used?.name} isOurs={(used != null && used.Pointer == part.Mat.Pointer)} tex={tex?.name} isOursTex={(tex != null && tex.Pointer == part.Tex.Pointer)} darkPx={dark}/{part.Px.Length} stained={part.Stained} dirty={part.Dirty} texFormat={part.Tex.format} readable={part.Tex.isReadable} gpuMid={gpu}");
                }

                break;
            }
            case "inktouch":
            {
                // Teleport next to the nearest ink splat and press the right hand onto it.
                var player = GameManager.Instance.Player;
                if (!InkVR.Body.InkWorld.NearestDecalPoint(player.transform.position, out var point, out var normal))
                {
                    Plugin.L.LogInfo("inktouch: no splat with mesh data");
                    break;
                }

                var flat = Vector3.ProjectOnPlane(normal, Vector3.up);
                var stand = Mathf.Abs(normal.y) > 0.7f ? point + new Vector3(0.6f, 0, 0.6f) : point + flat.normalized * 1.8f;
                var cc = player.CharacterController;
                cc.enabled = false;
                player.transform.position = new Vector3(stand.x, player.transform.position.y + (Mathf.Abs(normal.y) > 0.7f ? point.y - (player.transform.position.y + cc.center.y - cc.height / 2) : 0), stand.z);
                cc.enabled = true;
                InkVR.Core.VRRig.DevPendingTouch = point;
                Plugin.L.LogInfo($"inktouch: splat at {point} normal {normal}, standing at {stand}");
                break;
            }
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
                // swing <path>: stand within reach of an object and swing the held melee weapon down through it
                var t = Resolve(arg);
                var col = t.GetComponentsInChildren<Collider>().FirstOrDefault(c => !c.isTrigger);
                var target = col != null ? col.bounds.center : t.position;
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
                InkVR.Body.MeleeSwing.DevSwing(target);
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
                // body full|arms|hands|none (what you see) or lifelike|toon (how it looks)
                var word = char.ToUpperInvariant(arg[0]) + arg.Substring(1).ToLowerInvariant();
                if (word == "Lifelike" || word == "Toon") ModConfig.BodyStyle.Value = word;
                else ModConfig.BodyMode.Value = word;
                Plugin.L.LogInfo($"body: {ModConfig.BodyMode.Value}, {ModConfig.BodyStyle.Value}");
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
    private void ShootFrom(string name, Vector3 camPos, Vector3 target, Vector3 up, bool cinema, float fov)
    {
        var scale = InkVR.Core.VRRig.ActiveScale;
        var go = new GameObject("InkVR_BodyShotCam");
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.cullingMask = cinema ? InkVR.UI.VRLayers.UIMask : ~InkVR.UI.VRLayers.UIMask & ~(1 << 10);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.04f, 0.03f);
        cam.fieldOfView = fov;
        cam.nearClipPlane = Mathf.Min(0.05f * scale, Vector3.Distance(camPos, target) * 0.3f);
        cam.farClipPlane = 60f * scale;
        go.transform.position = camPos;
        go.transform.rotation = Quaternion.LookRotation(target - camPos, up);
        var rt = new RenderTexture(1024, 1024, 24);
        cam.targetTexture = rt;
        cam.Render();
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

    private static Vector3 ParseV3(string s)
    {
        var a = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return new Vector3(a[0], a[1], a[2]);
    }
}
