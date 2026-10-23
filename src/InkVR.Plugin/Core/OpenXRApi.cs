using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace InkVR.Core;

// Raw OpenXR types and entry points (openxr.h 1.x) used for controller input. The session itself
// belongs to Unity's OpenXR provider; InkVR adds its own action set to it.

[StructLayout(LayoutKind.Sequential)]
public struct XrQuaternionf { public float x, y, z, w; }

[StructLayout(LayoutKind.Sequential)]
public struct XrVector3f { public float x, y, z; }

[StructLayout(LayoutKind.Sequential)]
public struct XrPosef
{
    public XrQuaternionf orientation;
    public XrVector3f position;

    public static XrPosef Identity => new() { orientation = new XrQuaternionf { w = 1 } };

    // OpenXR is right-handed (-Z forward); Unity is left-handed (+Z forward).
    public Vector3 UnityPosition => new(position.x, position.y, -position.z);
    public Quaternion UnityRotation => new(-orientation.x, -orientation.y, orientation.z, orientation.w);
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrActionSetCreateInfo
{
    public int type;
    public void* next;
    public fixed byte actionSetName[64];
    public fixed byte localizedActionSetName[128];
    public uint priority;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrActionCreateInfo
{
    public int type;
    public void* next;
    public fixed byte actionName[64];
    public int actionType;
    public uint countSubactionPaths;
    public ulong* subactionPaths;
    public fixed byte localizedActionName[128];
}

[StructLayout(LayoutKind.Sequential)]
public struct XrActionSuggestedBinding { public ulong action, binding; }

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrInteractionProfileSuggestedBinding
{
    public int type;
    public void* next;
    public ulong interactionProfile;
    public uint countSuggestedBindings;
    public XrActionSuggestedBinding* suggestedBindings;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrActionSpaceCreateInfo
{
    public int type;
    public void* next;
    public ulong action;
    public ulong subactionPath;
    public XrPosef poseInActionSpace;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrSessionActionSetsAttachInfo
{
    public int type;
    public void* next;
    public uint countActionSets;
    public ulong* actionSets;
}

[StructLayout(LayoutKind.Sequential)]
public struct XrActiveActionSet { public ulong actionSet, subactionPath; }

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrActionsSyncInfo
{
    public int type;
    public void* next;
    public uint countActiveActionSets;
    public XrActiveActionSet* activeActionSets;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrActionStateGetInfo
{
    public int type;
    public void* next;
    public ulong action;
    public ulong subactionPath;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrActionStateBoolean
{
    public int type;
    public void* next;
    public uint currentState;
    public uint changedSinceLastSync;
    public long lastChangeTime;
    public uint isActive;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrActionStateFloat
{
    public int type;
    public void* next;
    public float currentState;
    public uint changedSinceLastSync;
    public long lastChangeTime;
    public uint isActive;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrActionStateVector2f
{
    public int type;
    public void* next;
    public float x, y;
    public uint changedSinceLastSync;
    public long lastChangeTime;
    public uint isActive;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrActionStatePose
{
    public int type;
    public void* next;
    public uint isActive;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrSpaceLocation
{
    public int type;
    public void* next;
    public ulong locationFlags;
    public XrPosef pose;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrHapticActionInfo
{
    public int type;
    public void* next;
    public ulong action;
    public ulong subactionPath;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrHapticVibration
{
    public int type;
    public void* next;
    public long duration;
    public float frequency;
    public float amplitude;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrFrameState
{
    public int type;
    public void* next;
    public long predictedDisplayTime;
    public long predictedDisplayPeriod;
    public uint shouldRender;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct XrInteractionProfileState
{
    public int type;
    public void* next;
    public ulong interactionProfile;
}

public static class XrType
{
    public const int HapticVibration = 13;
    public const int ActionStateBoolean = 23, ActionStateFloat = 24, ActionStateVector2f = 25, ActionStatePose = 27;
    public const int ActionSetCreateInfo = 28, ActionCreateInfo = 29;
    public const int ActionSpaceCreateInfo = 38, SpaceLocation = 42;
    public const int InteractionProfileSuggestedBinding = 51, InteractionProfileState = 53;
    public const int ActionStateGetInfo = 58, HapticActionInfo = 59, SessionActionSetsAttachInfo = 60, ActionsSyncInfo = 61;
}

public enum XrActionType
{
    Boolean = 1,
    Float = 2,
    Vector2f = 3,
    Pose = 4,
    Vibration = 100
}

/// <summary>
/// OpenXR function table, resolved through the loader's xrGetInstanceProcAddr. Also installs a
/// pass-through hook so InkVR can read each frame's predicted display time from xrWaitFrame.
/// </summary>
public static unsafe class OpenXRApi
{
    private static IntPtr _realGetInstanceProcAddr;
    private static IntPtr _realWaitFrame;
    private static long _predictedDisplayTime;
    private static long _predictedDisplayPeriod;

    public static long PredictedDisplayTime => System.Threading.Interlocked.Read(ref _predictedDisplayTime);
    public static long PredictedDisplayPeriod => System.Threading.Interlocked.Read(ref _predictedDisplayPeriod);

    public static delegate* unmanaged<ulong, byte*, ulong*, int> xrStringToPath;
    public static delegate* unmanaged<ulong, ulong, uint, uint*, byte*, int> xrPathToString;
    public static delegate* unmanaged<ulong, XrActionSetCreateInfo*, ulong*, int> xrCreateActionSet;
    public static delegate* unmanaged<ulong, XrActionCreateInfo*, ulong*, int> xrCreateAction;
    public static delegate* unmanaged<ulong, XrInteractionProfileSuggestedBinding*, int> xrSuggestInteractionProfileBindings;
    public static delegate* unmanaged<ulong, XrSessionActionSetsAttachInfo*, int> xrAttachSessionActionSets;
    public static delegate* unmanaged<ulong, XrActionSpaceCreateInfo*, ulong*, int> xrCreateActionSpace;
    public static delegate* unmanaged<ulong, XrActionsSyncInfo*, int> xrSyncActions;
    public static delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStateBoolean*, int> xrGetActionStateBoolean;
    public static delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStateFloat*, int> xrGetActionStateFloat;
    public static delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStateVector2f*, int> xrGetActionStateVector2f;
    public static delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStatePose*, int> xrGetActionStatePose;
    public static delegate* unmanaged<ulong, ulong, long, XrSpaceLocation*, int> xrLocateSpace;
    public static delegate* unmanaged<ulong, XrHapticActionInfo*, XrHapticVibration*, int> xrApplyHapticFeedback;
    public static delegate* unmanaged<ulong, ulong, XrInteractionProfileState*, int> xrGetCurrentInteractionProfile;

    public static bool Loaded { get; private set; }

    /// <summary>Returns the hook to hand to Unity's provider in place of the loader's xrGetInstanceProcAddr.</summary>
    public static IntPtr InstallHook(IntPtr realGetInstanceProcAddr)
    {
        _realGetInstanceProcAddr = realGetInstanceProcAddr;
        return (IntPtr)(delegate* unmanaged<ulong, byte*, IntPtr*, int>)&HookGetInstanceProcAddr;
    }

    [UnmanagedCallersOnly]
    private static int HookGetInstanceProcAddr(ulong instance, byte* name, IntPtr* function)
    {
        var result = ((delegate* unmanaged<ulong, byte*, IntPtr*, int>)_realGetInstanceProcAddr)(instance, name, function);
        if (result == 0 && function != null && *function != IntPtr.Zero && Matches(name, "xrWaitFrame"))
        {
            _realWaitFrame = *function;
            *function = (IntPtr)(delegate* unmanaged<ulong, void*, XrFrameState*, int>)&HookWaitFrame;
        }

        return result;
    }

    // Time spent blocked in xrWaitFrame: the runtime holds the app there when it is ahead of the display,
    // so it is the frame's spare time (the headroom automatic resolution works from).
    private static long _waitTicksSum, _waitCount;

    /// <summary>Average milliseconds spent waiting in xrWaitFrame since the last call (and the call count).</summary>
    public static (double avgMs, long count) TakeWaitStats()
    {
        var ticks = System.Threading.Interlocked.Exchange(ref _waitTicksSum, 0);
        var n = System.Threading.Interlocked.Exchange(ref _waitCount, 0);
        return (n > 0 ? ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / n : -1, n);
    }

    [UnmanagedCallersOnly]
    private static int HookWaitFrame(ulong session, void* info, XrFrameState* state)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = ((delegate* unmanaged<ulong, void*, XrFrameState*, int>)_realWaitFrame)(session, info, state);
        System.Threading.Interlocked.Add(ref _waitTicksSum, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
        System.Threading.Interlocked.Increment(ref _waitCount);
        if (result >= 0 && state != null)
        {
            System.Threading.Interlocked.Exchange(ref _predictedDisplayTime, state->predictedDisplayTime);
            System.Threading.Interlocked.Exchange(ref _predictedDisplayPeriod, state->predictedDisplayPeriod);
        }

        return result;
    }

    private static bool Matches(byte* s, string name)
    {
        for (var i = 0; i < name.Length; i++)
            if (s[i] != name[i])
                return false;
        return s[name.Length] == 0;
    }

    public static bool Load(ulong instance)
    {
        try
        {
            xrStringToPath = (delegate* unmanaged<ulong, byte*, ulong*, int>)Get(instance, "xrStringToPath");
            xrPathToString = (delegate* unmanaged<ulong, ulong, uint, uint*, byte*, int>)Get(instance, "xrPathToString");
            xrCreateActionSet = (delegate* unmanaged<ulong, XrActionSetCreateInfo*, ulong*, int>)Get(instance, "xrCreateActionSet");
            xrCreateAction = (delegate* unmanaged<ulong, XrActionCreateInfo*, ulong*, int>)Get(instance, "xrCreateAction");
            xrSuggestInteractionProfileBindings = (delegate* unmanaged<ulong, XrInteractionProfileSuggestedBinding*, int>)Get(instance, "xrSuggestInteractionProfileBindings");
            xrAttachSessionActionSets = (delegate* unmanaged<ulong, XrSessionActionSetsAttachInfo*, int>)Get(instance, "xrAttachSessionActionSets");
            xrCreateActionSpace = (delegate* unmanaged<ulong, XrActionSpaceCreateInfo*, ulong*, int>)Get(instance, "xrCreateActionSpace");
            xrSyncActions = (delegate* unmanaged<ulong, XrActionsSyncInfo*, int>)Get(instance, "xrSyncActions");
            xrGetActionStateBoolean = (delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStateBoolean*, int>)Get(instance, "xrGetActionStateBoolean");
            xrGetActionStateFloat = (delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStateFloat*, int>)Get(instance, "xrGetActionStateFloat");
            xrGetActionStateVector2f = (delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStateVector2f*, int>)Get(instance, "xrGetActionStateVector2f");
            xrGetActionStatePose = (delegate* unmanaged<ulong, XrActionStateGetInfo*, XrActionStatePose*, int>)Get(instance, "xrGetActionStatePose");
            xrLocateSpace = (delegate* unmanaged<ulong, ulong, long, XrSpaceLocation*, int>)Get(instance, "xrLocateSpace");
            xrApplyHapticFeedback = (delegate* unmanaged<ulong, XrHapticActionInfo*, XrHapticVibration*, int>)Get(instance, "xrApplyHapticFeedback");
            xrGetCurrentInteractionProfile = (delegate* unmanaged<ulong, ulong, XrInteractionProfileState*, int>)Get(instance, "xrGetCurrentInteractionProfile");
            Loaded = true;
        }
        catch (Exception e)
        {
            Plugin.L.LogError("OpenXR function lookup failed: " + e.Message);
            Loaded = false;
        }

        return Loaded;
    }

    private static IntPtr Get(ulong instance, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name + "\0");
        IntPtr fn;
        int r;
        fixed (byte* p = bytes)
            r = ((delegate* unmanaged<ulong, byte*, IntPtr*, int>)_realGetInstanceProcAddr)(instance, p, &fn);
        if (r != 0 || fn == IntPtr.Zero) throw new Exception($"{name} unavailable ({r})");
        return fn;
    }

    public static ulong Path(ulong instance, string path)
    {
        var bytes = Encoding.ASCII.GetBytes(path + "\0");
        ulong result;
        fixed (byte* p = bytes)
            Check(xrStringToPath(instance, p, &result), "xrStringToPath " + path);
        return result;
    }

    public static string PathString(ulong instance, ulong path)
    {
        if (path == 0) return "";
        var buffer = stackalloc byte[256];
        uint count;
        return xrPathToString(instance, path, 256, &count, buffer) == 0 ? Marshal.PtrToStringAnsi((IntPtr)buffer) : "?";
    }

    public static void CopyName(byte* dest, int capacity, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var n = Math.Min(bytes.Length, capacity - 1);
        for (var i = 0; i < n; i++) dest[i] = bytes[i];
        dest[n] = 0;
    }

    public static int Check(int result, string what)
    {
        if (result < 0) Plugin.L.LogWarning($"{what} failed: XrResult {result}");
        return result;
    }
}
