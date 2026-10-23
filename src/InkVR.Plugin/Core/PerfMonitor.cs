using UnityEngine;
using UnityEngine.XR;

namespace InkVR.Core;

/// <summary>Logs VR frame timing every few seconds (frame time, GPU time from the runtime, dropped frames).</summary>
public static class PerfMonitor
{
    private const float Interval = 5f;
    private static float _start;
    private static int _frames;
    private static float _worst, _gpuSum;
    private static int _gpuSamples, _droppedAtStart = -1;

    public static void Update()
    {
        if (!ModConfig.PerfLog.Value || !XrManager.IsRunning) return;
        var dt = Time.unscaledDeltaTime;
        _frames++;
        _worst = Mathf.Max(_worst, dt);
        var display = XrManager.Display;
        if (display.TryGetAppGPUTimeLastFrame(out var gpu) && gpu > 0)
        {
            _gpuSum += gpu;
            _gpuSamples++;
        }

        if (_droppedAtStart < 0 && display.TryGetDroppedFrameCount(out var d0)) _droppedAtStart = d0;

        var elapsed = Time.unscaledTime - _start;
        if (elapsed < Interval) return;

        display.TryGetDisplayRefreshRate(out var hz);
        var dropped = display.TryGetDroppedFrameCount(out var d1) && _droppedAtStart >= 0 ? d1 - _droppedAtStart : -1;
        var gpuAvg = _gpuSamples > 0 ? _gpuSum / _gpuSamples : -1;
        var head = InputTracking.GetLocalPosition(XRNode.CenterEye);
        Plugin.L.LogInfo(
            $"VRPERF frame {elapsed / _frames * 1000f:F2} ms avg ({_frames / elapsed:F1} fps), worst {_worst * 1000f:F1} ms | " +
            $"GPU {gpuAvg:F2} ms | dropped {dropped} | {hz:F0} Hz | eye {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} ({XRSettings.eyeTextureResolutionScale:P0}) | " +
            $"head {head.y:F2} m, IPD {StereoScale.MeasuredIpd * 1000f:F1} mm, eyes {StereoScale.RenderedSeparation:F3} u (prescaled {StereoScale.PreScaled}), scale {VRRig.ActiveScale:F2} | " +
            $"scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");

        _start = Time.unscaledTime;
        _frames = 0;
        _worst = 0;
        _gpuSum = 0;
        _gpuSamples = 0;
        _droppedAtStart = d1;
    }
}
