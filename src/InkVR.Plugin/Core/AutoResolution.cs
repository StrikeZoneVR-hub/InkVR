using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace InkVR.Core;

/// <summary>
/// Holds the headset's frame rate by trading resolution. Missing frames (the frame takes longer than the
/// display's refresh) lowers the eye resolution a step; plenty of spare time (measured as how long the
/// runtime holds us in xrWaitFrame) raises it back towards Graphics.RenderScale. Steps are rare and
/// spaced out because each one reallocates the eye textures. Never changes in the theatre (paused or
/// menus), and goes back to full resolution behind each loading screen.
/// </summary>
public static class AutoResolution
{
    private const float Step = 0.05f, Window = 1f, MinGap = 6f, NoRiseAfterDrop = 30f;

    public static float Current { get; private set; } = -1;

    private static float _windowStart, _lastChange = -100;
    private static int _frames, _missed, _calmWindows, _hotWindows;
    private static float _lastDrop = -100;
    private static float _frameSum;
    private static string _scene;

    public static void Update()
    {
        if (!XrManager.IsRunning) return;
        var max = Mathf.Clamp(ModConfig.RenderScale.Value, 0.5f, 2f);
        var min = Mathf.Clamp(ModConfig.MinRenderScale.Value, 0.5f, max);

        if (!ModConfig.DynamicResolution.Value || Current < 0)
        {
            Set(max, null);
            return;
        }

        if (Current > max + 1e-3f) Set(max, "lowered RenderScale");

        // A new scene: start it at full quality (the change hides behind the load).
        var scene = SceneManager.GetActiveScene().name;
        if (scene != _scene)
        {
            _scene = scene;
            if (Current < max - 1e-3f) Set(max, "new scene");
            ResetWindow();
            return;
        }

        if (VRRig.CinemaActive || !VRRig.InGame)
        {
            ResetWindow();
            return;
        }

        XrManager.Display.TryGetDisplayRefreshRate(out var hz);
        if (hz < 30) hz = 90;
        var budget = 1f / hz;
        var dt = Time.unscaledDeltaTime;
        if (dt > 0.25f) // a hitch (loading, shader compile) says nothing about steady load
        {
            ResetWindow();
            return;
        }

        _frames++;
        _frameSum += dt;
        if (dt > budget * 1.35f) _missed++;
        if (Time.unscaledTime - _windowStart < Window) return;

        var avg = _frameSum / Mathf.Max(1, _frames);
        var (waitMs, waits) = OpenXRApi.TakeWaitStats();
        var missedShare = (float)_missed / Mathf.Max(1, _frames);
        var canChange = Time.unscaledTime - _lastChange > MinGap;

        // Lower after two slow seconds in a row (or one very slow one); raise only after a long calm spell and
        // never soon after lowering, so it settles instead of see-sawing (each change costs a small hitch).
        var hot = avg > budget * 1.06f || missedShare > 0.08f;
        _hotWindows = hot ? _hotWindows + 1 : 0;
        if (hot && Current > min + 1e-3f)
        {
            _calmWindows = 0;
            if (canChange && (_hotWindows >= 2 || missedShare > 0.25f))
            {
                _hotWindows = 0;
                _lastDrop = Time.unscaledTime;
                Set(Mathf.Max(min, Current - Step), $"frames over budget ({avg * 1000f:F1} ms vs {budget * 1000f:F1} ms, {missedShare:P0} late)");
            }
        }
        else if (!hot && waits > 0 && waitMs > budget * 1000f * 0.32f && Current < max - 1e-3f)
        {
            if (++_calmWindows >= 6 && canChange && Time.unscaledTime - _lastDrop > NoRiseAfterDrop)
            {
                _calmWindows = 0;
                Set(Mathf.Min(max, Current + Step), $"headroom ({waitMs:F1} ms spare per frame)");
            }
        }
        else
        {
            _calmWindows = 0;
        }

        ResetWindow();
    }

    private static void ResetWindow()
    {
        _windowStart = Time.unscaledTime;
        _frames = 0;
        _missed = 0;
        _frameSum = 0;
        OpenXRApi.TakeWaitStats();
    }

    private static void Set(float scale, string why)
    {
        if (Mathf.Abs(scale - Current) < 1e-3f && Mathf.Approximately(XRSettings.eyeTextureResolutionScale, scale)) return;
        Current = scale;
        XRSettings.eyeTextureResolutionScale = scale;
        _lastChange = Time.unscaledTime;
        if (why != null) Plugin.L.LogInfo($"Resolution {scale:P0} ({XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} per eye): {why}");
    }
}
