using System.Collections.Generic;
using InkVR.Core;
using UnityEngine;

namespace InkVR.Input;

public enum Hand
{
    Left,
    Right
}

/// <summary>Per-hand controller state for the current frame (poses in tracking space, metres).</summary>
public class ControllerState
{
    public bool Active;
    public bool PoseValid;
    public Vector3 GripPosition, AimPosition;
    public Quaternion GripRotation = Quaternion.identity, AimRotation = Quaternion.identity;
    public Vector2 Stick;
    public float Trigger, Grip;
    public bool StickClick, Primary, Secondary, Menu, ThumbTouch, TriggerTouch;

    // Previous-frame copies for edge detection.
    public bool PrevStickClick, PrevPrimary, PrevSecondary, PrevMenu, PrevTriggerPressed, PrevGripPressed;

    public bool TriggerPressed => Trigger > 0.6f;
    public bool GripPressed => Grip > 0.6f;

    public void Roll()
    {
        PrevStickClick = StickClick;
        PrevPrimary = Primary;
        PrevSecondary = Secondary;
        PrevMenu = Menu;
        PrevTriggerPressed = TriggerPressed;
        PrevGripPressed = GripPressed;
    }
}

/// <summary>
/// InkVR's OpenXR action set: Quest Touch (plus Index and generic controllers), attached to the
/// session owned by Unity's OpenXR provider. Synced every frame; poses located at the predicted
/// display time captured from xrWaitFrame, in Unity's tracking space (same space as the head).
/// </summary>
public static unsafe class XrInput
{
    public static readonly ControllerState Left = new(), Right = new();
    public static string InteractionProfile { get; private set; } = "";
    public static bool Ready => _attached;

    private static ulong _instance, _session, _actionSet;
    private static ulong _handLeft, _handRight;
    private static ulong _gripPose, _aimPose, _stick, _stickClick, _trigger, _triggerTouch, _grip, _primary, _secondary, _menu, _thumbTouch, _haptic;
    private static ulong _gripSpaceL, _gripSpaceR, _aimSpaceL, _aimSpaceR;
    private static bool _created, _attached, _failed;
    private static float _profileCheckAt;

    public static ControllerState Get(Hand h) => h == Hand.Left ? Left : Right;

    public static void Update()
    {
        if (_failed || !XrManager.IsRunning) return;
        if (!_created && XrManager.XrInstance != 0) Create(XrManager.XrInstance);
        if (_created && !_attached) Attach();
        if (!_attached) return;

        Left.Roll();
        Right.Roll();

        var active = new XrActiveActionSet { actionSet = _actionSet, subactionPath = 0 };
        var sync = new XrActionsSyncInfo { type = XrType.ActionsSyncInfo, countActiveActionSets = 1, activeActionSets = &active };
        var r = OpenXRApi.xrSyncActions(_session, &sync);
        if (r < 0) return; // e.g. session not focused (XR_SESSION_NOT_FOCUSED is a success code)

        Read(Left, _handLeft);
        Read(Right, _handRight);
        LocatePoses();

        if (Time.unscaledTime > _profileCheckAt)
        {
            _profileCheckAt = Time.unscaledTime + 2f;
            var state = new XrInteractionProfileState { type = XrType.InteractionProfileState };
            if (OpenXRApi.xrGetCurrentInteractionProfile(_session, _handRight, &state) == 0)
            {
                var name = OpenXRApi.PathString(_instance, state.interactionProfile);
                if (name != InteractionProfile)
                {
                    InteractionProfile = name;
                    Plugin.L.LogInfo($"Controller profile: '{name}'");
                }
            }
        }
    }

    /// <summary>Re-locate the hand poses right before the rig is posed (later = less latency).</summary>
    public static void LocatePoses()
    {
        if (!_attached) return;
        var time = OpenXRApi.PredictedDisplayTime;
        if (time == 0) return;
        Locate(Left, _gripSpaceL, _aimSpaceL, time);
        Locate(Right, _gripSpaceR, _aimSpaceR, time);
    }

    public static void Haptic(Hand hand, float amplitude, float seconds, float frequency = 0)
    {
        if (!_attached) return;
        var info = new XrHapticActionInfo { type = XrType.HapticActionInfo, action = _haptic, subactionPath = hand == Hand.Left ? _handLeft : _handRight };
        var vib = new XrHapticVibration
        {
            type = XrType.HapticVibration, duration = (long)(seconds * 1e9), frequency = frequency, amplitude = Mathf.Clamp01(amplitude)
        };
        OpenXRApi.xrApplyHapticFeedback(_session, &info, &vib);
    }

    // ------------------------------------------------------------------

    private static void Create(ulong instance)
    {
        _instance = instance;
        if (!OpenXRApi.Loaded && !OpenXRApi.Load(instance))
        {
            _failed = true;
            return;
        }

        _handLeft = OpenXRApi.Path(instance, "/user/hand/left");
        _handRight = OpenXRApi.Path(instance, "/user/hand/right");

        var setInfo = new XrActionSetCreateInfo { type = XrType.ActionSetCreateInfo, priority = 0 };
        OpenXRApi.CopyName(setInfo.actionSetName, 64, "inkvr");
        OpenXRApi.CopyName(setInfo.localizedActionSetName, 128, "InkVR");
        ulong set;
        if (OpenXRApi.Check(OpenXRApi.xrCreateActionSet(instance, &setInfo, &set), "xrCreateActionSet") < 0)
        {
            _failed = true;
            return;
        }

        _actionSet = set;
        _gripPose = Action("grip_pose", "Grip pose", XrActionType.Pose);
        _aimPose = Action("aim_pose", "Aim pose", XrActionType.Pose);
        _stick = Action("thumbstick", "Thumbstick", XrActionType.Vector2f);
        _stickClick = Action("thumbstick_click", "Thumbstick click", XrActionType.Boolean);
        _trigger = Action("trigger", "Trigger", XrActionType.Float);
        _triggerTouch = Action("trigger_touch", "Trigger touch", XrActionType.Boolean);
        _grip = Action("grip", "Grip", XrActionType.Float);
        _primary = Action("primary", "A / X", XrActionType.Boolean);
        _secondary = Action("secondary", "B / Y", XrActionType.Boolean);
        _menu = Action("menu", "Menu", XrActionType.Boolean);
        _thumbTouch = Action("thumb_touch", "Thumb rest touch", XrActionType.Boolean);
        _haptic = Action("haptic", "Haptics", XrActionType.Vibration);

        SuggestTouch();
        SuggestIndex();
        SuggestSimple();
        _created = true;
        Plugin.L.LogInfo("InkVR controller actions created");
    }

    private static ulong Action(string name, string localized, XrActionType type)
    {
        var subactions = stackalloc ulong[2] { _handLeft, _handRight };
        var info = new XrActionCreateInfo { type = XrType.ActionCreateInfo, actionType = (int)type, countSubactionPaths = 2, subactionPaths = subactions };
        OpenXRApi.CopyName(info.actionName, 64, name);
        OpenXRApi.CopyName(info.localizedActionName, 128, localized);
        ulong action;
        OpenXRApi.Check(OpenXRApi.xrCreateAction(_actionSet, &info, &action), "xrCreateAction " + name);
        return action;
    }

    private static void SuggestTouch()
    {
        var b = new List<(ulong, string)>();
        foreach (var h in new[] { "left", "right" })
        {
            var p = $"/user/hand/{h}/";
            b.Add((_gripPose, p + "input/grip/pose"));
            b.Add((_aimPose, p + "input/aim/pose"));
            b.Add((_stick, p + "input/thumbstick"));
            b.Add((_stickClick, p + "input/thumbstick/click"));
            b.Add((_trigger, p + "input/trigger/value"));
            b.Add((_triggerTouch, p + "input/trigger/touch"));
            b.Add((_grip, p + "input/squeeze/value"));
            b.Add((_thumbTouch, p + "input/thumbstick/touch"));
            b.Add((_haptic, p + "output/haptic"));
        }

        b.Add((_primary, "/user/hand/left/input/x/click"));
        b.Add((_secondary, "/user/hand/left/input/y/click"));
        b.Add((_thumbTouch, "/user/hand/left/input/x/touch"));
        b.Add((_thumbTouch, "/user/hand/left/input/y/touch"));
        b.Add((_menu, "/user/hand/left/input/menu/click"));
        b.Add((_primary, "/user/hand/right/input/a/click"));
        b.Add((_secondary, "/user/hand/right/input/b/click"));
        b.Add((_thumbTouch, "/user/hand/right/input/a/touch"));
        b.Add((_thumbTouch, "/user/hand/right/input/b/touch"));
        Suggest("/interaction_profiles/oculus/touch_controller", b);
    }

    private static void SuggestIndex()
    {
        var b = new List<(ulong, string)>();
        foreach (var h in new[] { "left", "right" })
        {
            var p = $"/user/hand/{h}/";
            b.Add((_gripPose, p + "input/grip/pose"));
            b.Add((_aimPose, p + "input/aim/pose"));
            b.Add((_stick, p + "input/thumbstick"));
            b.Add((_stickClick, p + "input/thumbstick/click"));
            b.Add((_trigger, p + "input/trigger/value"));
            b.Add((_triggerTouch, p + "input/trigger/touch"));
            b.Add((_grip, p + "input/squeeze/value"));
            b.Add((_primary, p + "input/a/click"));
            b.Add((_secondary, p + "input/b/click"));
            b.Add((_thumbTouch, p + "input/thumbstick/touch"));
            b.Add((_haptic, p + "output/haptic"));
        }

        Suggest("/interaction_profiles/valve/index_controller", b);
    }

    private static void SuggestSimple()
    {
        var b = new List<(ulong, string)>();
        foreach (var h in new[] { "left", "right" })
        {
            var p = $"/user/hand/{h}/";
            b.Add((_gripPose, p + "input/grip/pose"));
            b.Add((_aimPose, p + "input/aim/pose"));
            b.Add((_primary, p + "input/select/click"));
            b.Add((_menu, p + "input/menu/click"));
            b.Add((_haptic, p + "output/haptic"));
        }

        Suggest("/interaction_profiles/khr/simple_controller", b);
    }

    private static void Suggest(string profile, List<(ulong action, string path)> bindings)
    {
        var arr = stackalloc XrActionSuggestedBinding[bindings.Count];
        for (var i = 0; i < bindings.Count; i++)
            arr[i] = new XrActionSuggestedBinding { action = bindings[i].action, binding = OpenXRApi.Path(_instance, bindings[i].path) };
        var info = new XrInteractionProfileSuggestedBinding
        {
            type = XrType.InteractionProfileSuggestedBinding, interactionProfile = OpenXRApi.Path(_instance, profile),
            countSuggestedBindings = (uint)bindings.Count, suggestedBindings = arr
        };
        OpenXRApi.Check(OpenXRApi.xrSuggestInteractionProfileBindings(_instance, &info), "suggest " + profile);
    }

    private static void Attach()
    {
        _session = XrManager.XrSession;
        if (_session == 0 && OpenXRNative.GetXRSession(out var s)) _session = s;
        if (_session == 0 || !OpenXRNative.GetAppSpace(out var appSpace) || appSpace == 0) return;

        var set = _actionSet;
        var info = new XrSessionActionSetsAttachInfo { type = XrType.SessionActionSetsAttachInfo, countActionSets = 1, actionSets = &set };
        var r = OpenXRApi.xrAttachSessionActionSets(_session, &info);
        if (r < 0)
        {
            Plugin.L.LogError($"Could not attach InkVR controls to the OpenXR session (XrResult {r}). Controllers disabled.");
            _failed = true;
            return;
        }

        _gripSpaceL = Space(_gripPose, _handLeft);
        _gripSpaceR = Space(_gripPose, _handRight);
        _aimSpaceL = Space(_aimPose, _handLeft);
        _aimSpaceR = Space(_aimPose, _handRight);
        _attached = true;
        Plugin.L.LogInfo("InkVR controller actions attached");
    }

    private static ulong Space(ulong action, ulong hand)
    {
        var info = new XrActionSpaceCreateInfo { type = XrType.ActionSpaceCreateInfo, action = action, subactionPath = hand, poseInActionSpace = XrPosef.Identity };
        ulong space;
        OpenXRApi.Check(OpenXRApi.xrCreateActionSpace(_session, &info, &space), "xrCreateActionSpace");
        return space;
    }

    private static void Read(ControllerState c, ulong hand)
    {
        c.Stick = Vec2(_stick, hand, out var stickActive);
        c.Trigger = Float(_trigger, hand, out var trigActive);
        c.Grip = Float(_grip, hand, out _);
        c.StickClick = Bool(_stickClick, hand);
        c.Primary = Bool(_primary, hand);
        c.Secondary = Bool(_secondary, hand);
        c.Menu = Bool(_menu, hand);
        c.ThumbTouch = Bool(_thumbTouch, hand);
        c.TriggerTouch = Bool(_triggerTouch, hand) || c.Trigger > 0.05f;
        var pose = new XrActionStatePose { type = XrType.ActionStatePose };
        var get = new XrActionStateGetInfo { type = XrType.ActionStateGetInfo, action = _gripPose, subactionPath = hand };
        OpenXRApi.xrGetActionStatePose(_session, &get, &pose);
        c.Active = pose.isActive != 0 || stickActive || trigActive;
    }

    private static void Locate(ControllerState c, ulong gripSpace, ulong aimSpace, long time)
    {
        if (!OpenXRNative.GetAppSpace(out var appSpace)) return;
        var loc = new XrSpaceLocation { type = XrType.SpaceLocation };
        if (OpenXRApi.xrLocateSpace(gripSpace, appSpace, time, &loc) == 0 && (loc.locationFlags & 0x3) == 0x3)
        {
            c.GripPosition = loc.pose.UnityPosition;
            c.GripRotation = loc.pose.UnityRotation;
            c.PoseValid = true;
        }
        else
        {
            c.PoseValid = false;
        }

        loc = new XrSpaceLocation { type = XrType.SpaceLocation };
        if (OpenXRApi.xrLocateSpace(aimSpace, appSpace, time, &loc) == 0 && (loc.locationFlags & 0x3) == 0x3)
        {
            c.AimPosition = loc.pose.UnityPosition;
            c.AimRotation = loc.pose.UnityRotation;
        }
    }

    private static bool Bool(ulong action, ulong hand)
    {
        var get = new XrActionStateGetInfo { type = XrType.ActionStateGetInfo, action = action, subactionPath = hand };
        var st = new XrActionStateBoolean { type = XrType.ActionStateBoolean };
        return OpenXRApi.xrGetActionStateBoolean(_session, &get, &st) == 0 && st.isActive != 0 && st.currentState != 0;
    }

    private static float Float(ulong action, ulong hand, out bool active)
    {
        var get = new XrActionStateGetInfo { type = XrType.ActionStateGetInfo, action = action, subactionPath = hand };
        var st = new XrActionStateFloat { type = XrType.ActionStateFloat };
        active = OpenXRApi.xrGetActionStateFloat(_session, &get, &st) == 0 && st.isActive != 0;
        return active ? st.currentState : 0;
    }

    private static Vector2 Vec2(ulong action, ulong hand, out bool active)
    {
        var get = new XrActionStateGetInfo { type = XrType.ActionStateGetInfo, action = action, subactionPath = hand };
        var st = new XrActionStateVector2f { type = XrType.ActionStateVector2f };
        active = OpenXRApi.xrGetActionStateVector2f(_session, &get, &st) == 0 && st.isActive != 0;
        return active ? new Vector2(st.x, st.y) : Vector2.zero;
    }
}
