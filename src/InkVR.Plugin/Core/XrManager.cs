using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SubsystemsImplementation;
using UnityEngine.XR;

namespace InkVR.Core;

/// <summary>
/// Brings up OpenXR inside the (non-XR) game build: loads Unity's native OpenXR provider, creates the
/// XR display + input subsystems from their registered descriptors and walks the session lifecycle,
/// mirroring com.unity.xr.openxr's OpenXRLoader. Once the display subsystem runs, Unity renders every
/// camera that targets the screen in stereo to the headset (multi-pass).
/// </summary>
public static class XrManager
{
    public enum State
    {
        Off,
        WaitingForSession,
        Running,
        Failed
    }

    public static State Current { get; private set; } = State.Off;
    public static string RuntimeName { get; private set; } = "";
    public static XRDisplaySubsystem Display { get; private set; }
    public static XRInputSubsystem Input { get; private set; }
    public static ulong XrInstance { get; private set; }
    public static IntPtr XrGetInstanceProcAddr { get; private set; }
    public static ulong XrSession { get; private set; }
    public static OpenXRNative.NativeEvent SessionState { get; private set; }

    public static bool IsRunning => Current == State.Running && Display != null && Display.running;

    public static event Action Started;
    public static event Action Stopped;

    private static readonly ConcurrentQueue<(OpenXRNative.NativeEvent e, ulong payload)> Events = new();
    private static OpenXRNative.ReceiveNativeEventDelegate _callback; // must stay referenced

    public static string PluginDir => Path.Combine(Application.dataPath, "Plugins", "x86_64");

    public static bool Initialize()
    {
        if (Current != State.Off) return Current != State.Failed;

        try
        {
            if (!File.Exists(Path.Combine(PluginDir, "UnityOpenXR.dll")) || !File.Exists(Path.Combine(PluginDir, "openxr_loader.dll")))
                return Fail("UnityOpenXR.dll / openxr_loader.dll missing from the game's Plugins folder. Run the InkVR installer.");

            OpenXRNative.Resolve(PluginDir);
            OpenXRNative.SetSuccessfullyInitialized(false);

            var loader = Path.Combine(PluginDir, "openxr_loader.dll");
            if (!OpenXRNative.LoadOpenXRLibrary(Encoding.Unicode.GetBytes(loader + '\0')))
                return Fail("Could not load the OpenXR loader.");

            // OpenXRFeature.HookGetInstanceProcAddr with no features: hand the loader's
            // xrGetInstanceProcAddr to the provider so it can resolve the OpenXR API.
            XrGetInstanceProcAddr = OpenXRNative.GetProcAddressPtr(true);
            OpenXRNative.SetProcAddressPtrAndLoadStage1(OpenXRApi.InstallHook(XrGetInstanceProcAddr));

            if (!OpenXRNative.InitializeSession())
                return Fail("session_InitializeSession failed: " + OpenXRNative.LastError());

            _callback = OnNativeEvent;
            OpenXRNative.SetCallbacks(_callback);

            // The game's shaders have no single-pass-instanced variants, so render each eye separately.
            OpenXRNative.SetRenderMode(OpenXRNative.RenderMode.MultiPass);
            OpenXRNative.SetSymmetricProjection(false);
            OpenXRNative.SetLatencyOptimization(0);
            OpenXRNative.SetDepthSubmissionMode(0);
            OpenXRNative.SetUseOpenXRPredictedTime(true);
            SetApplicationInfo();

            Display = CreateSubsystem<XRDisplaySubsystemDescriptor, XRDisplaySubsystem>("OpenXR Display");
            Input = CreateSubsystem<XRInputSubsystemDescriptor, XRInputSubsystem>("OpenXR Input");
            if (Display == null || Input == null)
                return Fail("OpenXR subsystems were not registered. Is UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json installed?");

            if (OpenXRNative.GetRuntimeName(out var namePtr)) RuntimeName = Marshal.PtrToStringAnsi(namePtr) ?? "";
            Plugin.L.LogInfo($"OpenXR initialized. Runtime: '{RuntimeName}'");

            Current = State.WaitingForSession;
            if (!OpenXRNative.CreateSessionIfNeeded())
                return Fail("session_CreateSessionIfNeeded failed: " + OpenXRNative.LastError());

            return true;
        }
        catch (Exception e)
        {
            return Fail(e.ToString());
        }
    }

    /// <summary>Call every frame: pumps OpenXR events and reacts to session state changes on the main thread.</summary>
    public static void Update()
    {
        if (Current is State.Off or State.Failed) return;
        OpenXRNative.PumpMessageLoop();
        while (Events.TryDequeue(out var ev)) Handle(ev.e, ev.payload);
    }

    public static void Shutdown()
    {
        if (Current is State.Off or State.Failed) return;
        try
        {
            Input?.Stop();
            Display?.Stop();
            OpenXRNative.EndSession();
            OpenXRNative.DestroySession();
            Input?.Destroy();
            Display?.Destroy();
            OpenXRNative.UnloadOpenXRLibrary();
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("OpenXR shutdown: " + e.Message);
        }

        Input = null;
        Display = null;
        Current = State.Off;
        Stopped?.Invoke();
    }

    private static void OnNativeEvent(OpenXRNative.NativeEvent e, ulong payload) => Events.Enqueue((e, payload));

    private static void Handle(OpenXRNative.NativeEvent e, ulong payload)
    {
        switch (e)
        {
            case OpenXRNative.NativeEvent.XrInstanceChanged:
                XrInstance = payload;
                Plugin.L.LogInfo($"OpenXR instance 0x{payload:X}");
                break;
            case OpenXRNative.NativeEvent.XrSessionChanged:
                XrSession = payload;
                Plugin.L.LogInfo($"OpenXR session 0x{payload:X}");
                break;
            case OpenXRNative.NativeEvent.XrSessionStateChanged:
            case OpenXRNative.NativeEvent.XrSetupConfigValues:
            case OpenXRNative.NativeEvent.XrSystemIdChanged:
            case OpenXRNative.NativeEvent.XrChangedSpaceApp:
                break;
            default:
                if (e >= OpenXRNative.NativeEvent.XrIdle && e <= OpenXRNative.NativeEvent.XrInstanceLossPending)
                    SessionState = e;
                Plugin.L.LogInfo($"OpenXR event {e}");
                break;
        }

        switch (e)
        {
            case OpenXRNative.NativeEvent.XrReady:
                BeginSession();
                break;
            case OpenXRNative.NativeEvent.XrStopping:
                Input?.Stop();
                Display?.Stop();
                OpenXRNative.EndSession();
                Current = State.WaitingForSession;
                Stopped?.Invoke();
                break;
            case OpenXRNative.NativeEvent.XrExiting:
            case OpenXRNative.NativeEvent.XrLossPending:
            case OpenXRNative.NativeEvent.XrInstanceLossPending:
                Shutdown();
                break;
            case OpenXRNative.NativeEvent.XrRequestRestartLoop:
            case OpenXRNative.NativeEvent.XrRequestGetSystemLoop:
                Plugin.L.LogWarning("OpenXR: headset not available yet (is Virtual Desktop connected?)");
                break;
        }
    }

    private static void BeginSession()
    {
        // Display has to start before input so input can see the session (same order as OpenXRLoader).
        Display.Start();
        OpenXRNative.BeginSession();
        if (!Display.running) Display.Start();
        if (!Input.running) Input.Start();

        if (!Input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
            Plugin.L.LogWarning("Floor tracking origin not supported; using device origin.");

        if (Display.running && Input.running)
        {
            Current = State.Running;
            Plugin.L.LogInfo($"OpenXR session running. Origin={Input.GetTrackingOriginMode()} passes={Display.GetRenderPassCount()}");
            Started?.Invoke();
        }
        else
        {
            Fail($"Subsystems failed to start (display={Display.running}, input={Input.running})");
        }
    }

    private static TSubsystem CreateSubsystem<TDescriptor, TSubsystem>(string id)
        where TDescriptor : IntegratedSubsystemDescriptor<TSubsystem>
        where TSubsystem : IntegratedSubsystem
    {
        EnsureManagedDescriptors();
        var descriptors = SubsystemDescriptorStore.s_IntegratedDescriptors;
        if (descriptors == null) return null;
        for (var i = 0; i < descriptors.Count; i++)
        {
            var d = descriptors[i];
            if (d == null || d.id != id) continue;
            // Same steps as IntegratedSubsystemDescriptor<T>.Create(), whose interop body is unusable here.
            var nativePtr = SubsystemDescriptorBindings.Create(d.m_Ptr);
            if (nativePtr == IntPtr.Zero)
            {
                Plugin.L.LogWarning($"Native create of '{id}' returned null");
                return null;
            }

            var managed = SubsystemManager.GetIntegratedSubsystemByPtr(nativePtr);
            if (managed == null)
            {
                // Native side didn't build the managed wrapper; build and register it ourselves.
                managed = (IntegratedSubsystem)Activator.CreateInstance(typeof(TSubsystem));
                SubsystemManager.InitializeIntegratedSubsystem(nativePtr, managed);
            }

            managed.m_SubsystemDescriptor = d.Cast<ISubsystemDescriptor>();
            var s = managed.TryCast<TSubsystem>();
            Plugin.L.LogInfo($"Created subsystem '{id}': {(s != null ? "ok" : "null")} (wrapper {(SubsystemManager.GetIntegratedSubsystemByPtr(nativePtr) != null ? "native" : "manual")})");
            return s;
        }

        var ids = new StringBuilder();
        for (var i = 0; i < descriptors.Count; i++) ids.Append(descriptors[i]?.id).Append(", ");
        Plugin.L.LogWarning($"Descriptor '{id}' not found. Registered: {ids}");
        return null;
    }

    /// <summary>
    /// Unity creates the managed wrappers for natively discovered descriptors (UnitySubsystems manifests)
    /// from SubsystemManager's static constructor (StaticConstructScriptingClassMap). A non-XR game never
    /// touches SubsystemManager, so run it ourselves.
    /// </summary>
    private static void EnsureManagedDescriptors()
    {
        if (SubsystemDescriptorStore.s_IntegratedDescriptors is { Count: > 0 }) return;
        _ = SubsystemManager.s_IntegratedSubsystems; // touching the class runs its static constructor
        if (SubsystemDescriptorStore.s_IntegratedDescriptors is { Count: > 0 }) return;
        SubsystemManager.StaticConstructScriptingClassMap();
        Plugin.L.LogInfo($"Managed subsystem descriptors: {SubsystemDescriptorStore.s_IntegratedDescriptors?.Count ?? -1}");
    }

    private static void SetApplicationInfo()
    {
        var data = MD5.HashData(Encoding.UTF8.GetBytes(Application.version));
        if (BitConverter.IsLittleEndian) Array.Reverse(data);
        OpenXRNative.SetApplicationInfo(Application.productName, Application.version, BitConverter.ToUInt32(data, 0), Application.unityVersion);
    }

    private static bool Fail(string why)
    {
        Plugin.L.LogError("VR init failed: " + why);
        Current = State.Failed;
        return false;
    }
}
