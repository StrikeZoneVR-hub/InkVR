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
        if (ModConfig.VREnabled.Value) _startAt = Time.realtimeSinceStartup + 1f;
    }

    private void Update()
    {
        if (_startAt > 0 && Time.realtimeSinceStartup >= _startAt)
        {
            _startAt = -1;
            XrManager.Initialize();
        }

        XrManager.Update();
        VRRig.Update();
        PerfMonitor.Update();
    }

    private void LateUpdate() => VRRig.LateUpdate();

    private void OnApplicationQuit() => XrManager.Shutdown();
}
