using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InkVR.Body;
using InkVR.Core;
using InkVR.Input;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.Dev;

/// <summary>
/// Dev video recorder (teasers): a free camera rendered every frame into raw RGB frames on disk, at a fixed video
/// rate. While it records, the game steps a fixed amount of time per frame (Time.captureFramerate) and so does
/// the ink (<see cref="InkClock"/>), so the footage plays smoothly however slow capturing is. Commands:
///   rec start &lt;name&gt; [fps] [width] [height] | rec stop
///   rec cam hand &lt;side|front|back|top|palm&gt; &lt;metres&gt;    follow the right hand
///   rec cam orbit &lt;yaw&gt; &lt;metres&gt; &lt;height m&gt; &lt;look height m&gt;   around Henry (like bodyshot)
///   rec cam head [fov]                              Henry's own eyes
///   rec cam floor &lt;metres back&gt; &lt;height&gt;              looking down at the floor behind him
///   at &lt;seconds&gt; &lt;command&gt;                           run a command that many (video) seconds from now
///   handgo &lt;x y z&gt; &lt;seconds&gt;                         move the dev right hand there (tracking space), smoothly
/// </summary>
public static class DevRecorder
{
    /// <summary>The way the dev slide is moving Henry (world), for the floor camera.</summary>
    public static Vector3 MoveDir;

    private static Camera _cam;
    private static Light _fill;

    /// <summary>Fill light strength while recording (rec light &lt;0..2&gt;).</summary>
    public static float Fill = 0.8f;
    private static RenderTexture _rt;
    private static Texture2D _tex;
    private static FileStream _out;
    private static int _frames, _w, _h;
    private static byte[] _raw;
    private static string _file;
    private static string _mode = "orbit";
    private static float[] _args = { 20, 2.2f, 1.0f, 0.9f };
    private static Vector3 _pos, _look;
    private static bool _havePose;
    private static float _fov = 50f;

    private static readonly List<(float at, string cmd)> Schedule = new();
    private static (Vector3 from, Vector3 to, float t0, float t1)? _handMove;

    public static bool Recording => _out != null;

    public static void Command(string arg)
    {
        var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (a[0])
        {
            case "start":
            {
                Stop();
                var fps = a.Length > 2 ? int.Parse(a[2]) : 30;
                _w = a.Length > 3 ? int.Parse(a[3]) : 1280;
                _h = a.Length > 4 ? int.Parse(a[4]) : 720;
                var dir = Path.Combine(Plugin.DataDir, "rec");
                Directory.CreateDirectory(dir);
                _file = Path.Combine(dir, a[1] + ".raw");
                _out = new FileStream(_file, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);
                _frames = 0;
                _havePose = false;
                Time.captureFramerate = fps;
                InkClock.FixedStep = 1f / fps;
                Plugin.L.LogInfo($"REC start {_file} {_w}x{_h} @ {fps}");
                break;
            }
            case "stop":
                Stop();
                break;
            case "light":
                Fill = F(a[1]);
                break;
            case "cam":
                if (a.Contains("cut")) _havePose = false;
                a = a.Where(x => x != "cut").ToArray();
                _mode = a[1];
                if (_mode == "hand")
                {
                    _hSide = a.Length > 2 ? a[2] : "side";
                    _args = new[] { a.Length > 3 ? F(a[3]) : 0.35f };
                }
                else if (_mode == "head") _args = new[] { a.Length > 2 ? F(a[2]) : 80f };
                else _args = a.Skip(2).Select(F).ToArray();
                break;
        }
    }

    private static string _hSide = "side";

    private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    public static void At(string arg, Action<string> run)
    {
        var sp = arg.IndexOf(' ');
        Schedule.Add((InkClock.Now + F(arg[..sp]), arg[(sp + 1)..]));
    }

    public static void HandGo(string arg)
    {
        var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(F).ToArray();
        if (!Hands.DevRight.HasValue) return;
        _handMove = (Hands.DevRight.Value.pos, new Vector3(a[0], a[1], a[2]), InkClock.Now, InkClock.Now + Mathf.Max(0.01f, a[3]));
    }

    private static Vector3 _fixedPos, _fixedLook;

    /// <summary>A camera standing still in the world (rec cam fixed).</summary>
    public static void SetFixed(Vector3 pos, Vector3 look)
    {
        _fixedPos = pos;
        _fixedLook = look;
        _mode = "fixed";
        _havePose = false;
    }

    /// <summary>Moves the dev right hand to a tracking-space point over some seconds (0 = at once).</summary>
    public static void HandGoTo(Vector3 to, float seconds)
    {
        if (!Hands.DevRight.HasValue) return;
        if (seconds <= 0)
        {
            Hands.DevRight = (to, Hands.DevRight.Value.rot);
            _handMove = null;
            return;
        }

        _handMove = (Hands.DevRight.Value.pos, to, InkClock.Now, InkClock.Now + seconds);
    }

    /// <summary>Every frame (before rendering): scheduled commands and the moving hand.</summary>
    public static void Tick(Action<string> run)
    {
        var now = InkClock.Now;
        for (var i = 0; i < Schedule.Count; i++)
        {
            if (Schedule[i].at > now) continue;
            var cmd = Schedule[i].cmd;
            Schedule.RemoveAt(i--);
            Plugin.L.LogInfo($"> (at) {cmd}");
            run(cmd);
        }

        if (_handMove.HasValue && Hands.DevRight.HasValue)
        {
            var m = _handMove.Value;
            var p = Mathf.Clamp01((now - m.t0) / (m.t1 - m.t0));
            p = p * p * (3 - 2 * p);
            Hands.DevRight = (Vector3.Lerp(m.from, m.to, p), Hands.DevRight.Value.rot);
            if (p >= 1f) _handMove = null;
        }
    }

    /// <summary>After everything has moved: render this frame's picture and append it.</summary>
    public static void Capture()
    {
        if (_out == null) return;
        var head = VRRig.Head;
        if (head == null) return;
        if (_cam == null)
        {
            var go = new GameObject("InkVR_RecCam");
            Object.DontDestroyOnLoad(go);
            _cam = go.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.04f, 0.03f);
            // A soft fill light riding on the camera, so dark corners of the studio don't swallow the ink.
            _fill = go.AddComponent<Light>();
            _fill.type = LightType.Point;
        }

        _fill.intensity = Fill;
        _fill.range = 4f * VRRig.ActiveScale;
        _fill.enabled = Fill > 0;

        if (_rt == null || _rt.width != _w || _rt.height != _h)
        {
            _rt = new RenderTexture(_w, _h, 24) { antiAliasing = 4 };
            _tex = new Texture2D(_w, _h, TextureFormat.RGB24, false);
        }

        var scale = VRRig.ActiveScale;
        _cam.cullingMask = ~UI.VRLayers.UIMask & ~(1 << 10);
        _cam.nearClipPlane = 0.02f * scale;
        _cam.farClipPlane = 80f * scale;
        Pose(head, scale, out var pos, out var look, out var fov);
        // Glide rather than jump between frames (a hand-held camera, not a security camera).
        if (!_havePose)
        {
            _pos = pos;
            _look = look;
            _havePose = true;
        }

        _pos = Vector3.Lerp(_pos, pos, 0.25f);
        _look = Vector3.Lerp(_look, look, 0.3f);
        _fov = Mathf.Lerp(_fov, fov, 0.3f);
        _cam.fieldOfView = _fov;
        _cam.transform.position = _pos;
        _cam.transform.rotation = Quaternion.LookRotation(_look - _pos, Vector3.up);
        _cam.targetTexture = _rt;
        _cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = _rt;
        _tex.ReadPixels(new Rect(0, 0, _w, _h), 0, 0);
        _tex.Apply(false);
        RenderTexture.active = prev;
        _cam.targetTexture = null;
        var px = _tex.GetPixels32();
        var n = px.Length;
        if (_raw == null || _raw.Length != n * 3) _raw = new byte[n * 3];
        for (var i = 0; i < n; i++)
        {
            var c = px[i];
            _raw[i * 3] = c.r;
            _raw[i * 3 + 1] = c.g;
            _raw[i * 3 + 2] = c.b;
        }

        _out.Write(_raw, 0, _raw.Length);
        _frames++;
    }

    private static void Pose(Transform head, float scale, out Vector3 pos, out Vector3 look, out float fov)
    {
        var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        var right = Vector3.Cross(Vector3.up, fwd);
        fov = 50f;
        switch (_mode)
        {
            case "hand":
            {
                var w = Hands.WristOf(Hand.Right);
                var c = w != null ? w.position + w.forward * (0.05f * scale) : head.position;
                var d = _args[0] * scale;
                var dir = _hSide switch
                {
                    "front" => (-fwd + Vector3.up * 0.25f).normalized,
                    "back" => (-fwd * 0.7f + Vector3.up * 0.6f + right * 0.25f).normalized,
                    "top" => (Vector3.up + fwd * 0.35f).normalized,
                    "palm" => (-Vector3.up + right * 0.2f).normalized,
                    _ => (right + Vector3.up * 0.35f - fwd * 0.15f).normalized
                };
                pos = c + dir.normalized * d;
                look = c;
                fov = 45f;
                return;
            }
            case "fixed":
                pos = _fixedPos;
                look = _fixedLook;
                fov = 60f;
                return;
            case "head":
                pos = head.position;
                look = head.position + head.forward;
                fov = _args.Length > 0 ? _args[0] : 80f;
                return;
            case "floor":
            {
                // Behind Henry along the way he's walking, looking down at the floor he's just walked over.
                if (MoveDir.sqrMagnitude > 0.5f) fwd = MoveDir;
                var back = (_args.Length > 0 ? _args[0] : 1f) * scale;
                var height = (_args.Length > 1 ? _args[1] : 2f) * scale;
                var feet = new Vector3(head.position.x, Hands.GroundAt(head.position), head.position.z);
                pos = feet - fwd * (back * 0.3f) + Vector3.up * height;
                look = feet - fwd * back;
                fov = 55f;
                return;
            }
            default:
            {
                var yaw = _args.Length > 0 ? _args[0] : 20f;
                var dist = (_args.Length > 1 ? _args[1] : 2.2f) * scale;
                var height = (_args.Length > 2 ? _args[2] : 1f) * scale;
                var lookH = (_args.Length > 3 ? _args[3] : 0.9f) * scale;
                var body = Hands.Body;
                var center = body != null ? body.Pelvis : head.position;
                var floor = Hands.GroundAt(head.position);
                var dir = Quaternion.Euler(0, yaw, 0) * fwd;
                pos = new Vector3(center.x, floor + height, center.z) + dir * dist;
                look = new Vector3(center.x, floor + lookH, center.z);
                return;
            }
        }
    }

    private static void Stop()
    {
        if (_out == null) return;
        _out.Flush();
        _out.Dispose();
        _out = null;
        if (_fill != null) _fill.enabled = false;
        Time.captureFramerate = 0;
        InkClock.FixedStep = 0;
        Plugin.L.LogInfo($"REC stop {_file}: {_frames} frames {_w}x{_h}");
    }
}
