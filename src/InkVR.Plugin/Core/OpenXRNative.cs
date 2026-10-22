using System;
using System.IO;
using System.Runtime.InteropServices;

namespace InkVR.Core;

/// <summary>
/// Entry points of Unity's native OpenXR provider (UnityOpenXR.dll from com.unity.xr.openxr 1.17.x),
/// the same calls the package's managed OpenXRLoader makes. The game was built without XR, so the
/// mod plays the role of that loader.
/// </summary>
public static class OpenXRNative
{
    private const string Lib = "UnityOpenXR";

    // Mirrors OpenXRFeature.NativeEvent.
    public enum NativeEvent
    {
        XrSetupConfigValues,
        XrSystemIdChanged,
        XrInstanceChanged,
        XrSessionChanged,
        XrBeginSession,
        XrSessionStateChanged,
        XrChangedSpaceApp,
        XrEndSession,
        XrDestroySession,
        XrDestroyInstance,
        XrIdle,
        XrReady,
        XrSynchronized,
        XrVisible,
        XrFocused,
        XrStopping,
        XrExiting,
        XrLossPending,
        XrInstanceLossPending,
        XrRestartRequested,
        XrRequestRestartLoop,
        XrRequestGetSystemLoop
    }

    public enum RenderMode
    {
        MultiPass,
        SinglePassInstanced
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ReceiveNativeEventDelegate(NativeEvent e, ulong payload);

    /// <summary>Point DllImport("UnityOpenXR") at the copy in the game's Plugins folder (the same module Unity loads).</summary>
    public static void Resolve(string pluginDir) => NativeLibs.Register(Lib, Path.Combine(pluginDir, Lib + ".dll"));

    [DllImport(Lib, EntryPoint = "main_LoadOpenXRLibrary")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool LoadOpenXRLibrary(byte[] loaderPathWide);

    [DllImport(Lib, EntryPoint = "main_UnloadOpenXRLibrary")]
    public static extern void UnloadOpenXRLibrary();

    [DllImport(Lib, EntryPoint = "NativeConfig_SetCallbacks")]
    public static extern void SetCallbacks(ReceiveNativeEventDelegate callback);

    [DllImport(Lib, EntryPoint = "NativeConfig_SetApplicationInfo", CharSet = CharSet.Ansi)]
    public static extern void SetApplicationInfo(string applicationName, string applicationVersion, uint applicationVersionHash, string engineVersion);

    [DllImport(Lib, EntryPoint = "NativeConfig_SetRenderMode")]
    public static extern void SetRenderMode(RenderMode mode);

    [DllImport(Lib, EntryPoint = "NativeConfig_SetLatencyOptimization")]
    public static extern void SetLatencyOptimization(int mode); // 0 = PrioritizeRendering

    [DllImport(Lib, EntryPoint = "NativeConfig_SetDepthSubmissionMode")]
    public static extern void SetDepthSubmissionMode(int mode); // 0 none, 1 16-bit, 2 24-bit

    [DllImport(Lib, EntryPoint = "NativeConfig_SetSymmetricProjection")]
    public static extern void SetSymmetricProjection([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Lib, EntryPoint = "NativeConfig_SetUseOpenXRPredictedTime")]
    public static extern void SetUseOpenXRPredictedTime([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Lib, EntryPoint = "NativeConfig_GetRuntimeName")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool GetRuntimeName(out IntPtr runtimeNamePtr);

    [DllImport(Lib, EntryPoint = "NativeConfig_GetProcAddressPtr")]
    public static extern IntPtr GetProcAddressPtr([MarshalAs(UnmanagedType.I1)] bool loaderDefault);

    [DllImport(Lib, EntryPoint = "NativeConfig_SetProcAddressPtrAndLoadStage1")]
    public static extern void SetProcAddressPtrAndLoadStage1(IntPtr func);

    [DllImport(Lib, EntryPoint = "session_InitializeSession")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool InitializeSession();

    [DllImport(Lib, EntryPoint = "session_CreateSessionIfNeeded")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool CreateSessionIfNeeded();

    [DllImport(Lib, EntryPoint = "session_BeginSession")]
    public static extern void BeginSession();

    [DllImport(Lib, EntryPoint = "session_EndSession")]
    public static extern void EndSession();

    [DllImport(Lib, EntryPoint = "session_DestroySession")]
    public static extern void DestroySession();

    [DllImport(Lib, EntryPoint = "session_RequestExitSession")]
    public static extern void RequestExitSession();

    [DllImport(Lib, EntryPoint = "session_SetSuccessfullyInitialized")]
    public static extern void SetSuccessfullyInitialized([MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(Lib, EntryPoint = "session_GetLastError")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool GetLastError(out IntPtr error);

    [DllImport(Lib, EntryPoint = "messagepump_PumpMessageLoop")]
    public static extern void PumpMessageLoop();

    [DllImport(Lib, EntryPoint = "unity_ext_RequestEnableExtensionString", CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool RequestEnableExtensionString(string extension);

    [DllImport(Lib, EntryPoint = "OpenXRInputProvider_GetXRSession")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool GetXRSession(out ulong session);

    [DllImport(Lib, EntryPoint = "OpenXRInputProvider_GetAppSpace")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool GetAppSpace(out ulong appSpace);

    public static string LastError()
    {
        try
        {
            return GetLastError(out var p) ? Marshal.PtrToStringAnsi(p) : "";
        }
        catch
        {
            return "";
        }
    }
}
