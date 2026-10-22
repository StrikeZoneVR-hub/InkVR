using System;
using UnityEngine;

namespace InkVR.Core;

/// <summary>Per-frame driver for the VR systems. Lives on the plugin's persistent manager object.</summary>
public class VRRuntime : MonoBehaviour
{
    public VRRuntime(IntPtr ptr) : base(ptr) { }

    public static VRRuntime Instance { get; private set; }

    private float _startAt = -1;

    private void Awake()
    {
        Instance = this;
        if (ModConfig.StartInVR) _startAt = Time.realtimeSinceStartup + 1f;
    }

    private void Update()
    {
        if (_startAt > 0 && Time.realtimeSinceStartup >= _startAt)
        {
            _startAt = -1;
            XrManager.Initialize();
        }

        XrManager.Update();
        Input.ControllerBindings.Sample(); // normally already done from InControl's early update
        VRRig.Update();
        PerfMonitor.Update();

        if (XrManager.IsRunning && !Mathf.Approximately(UnityEngine.XR.XRSettings.eyeTextureResolutionScale, ModConfig.RenderScale.Value))
            UnityEngine.XR.XRSettings.eyeTextureResolutionScale = ModConfig.RenderScale.Value;
    }

    private void LateUpdate() => VRRig.LateUpdate();

    private void OnApplicationQuit() => XrManager.Shutdown();
}
