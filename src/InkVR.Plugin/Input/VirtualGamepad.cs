using System;
using Il2CppInterop.Runtime;
using InControl;
using UnityEngine;

namespace InkVR.Input;

/// <summary>
/// The Quest controllers, presented to the game as an InControl gamepad. The game reads its gamepad
/// through InControl everywhere (gameplay, menus, prompts), so this is the one input path that every
/// screen honours. Values are written during InControl's own device update (OnUpdateDevices).
/// </summary>
public static class VirtualGamepad
{
    public struct State
    {
        public Vector2 LeftStick;
        public bool A, B, X, Y;
        public bool LeftStickButton;
        public float LeftTrigger, RightTrigger;
        public bool Menu;
    }

    public static State Current;

    // Dev/testing: hold a button for a moment (pad a|b|x|y|menu|lt|rt|ls [seconds]).
    private static readonly System.Collections.Generic.Dictionary<string, float> DevUntil = new();

    public static void DevHold(string button, float seconds) => DevUntil[button.ToLowerInvariant()] = Time.unscaledTime + seconds;

    // Dev/testing: hold the left stick (stick x y seconds).
    private static Vector2 _devStick;
    private static float _devStickUntil;
    public static void DevStick(Vector2 v, float seconds)
    {
        _devStick = v;
        _devStickUntil = Time.unscaledTime + seconds;
    }

    private static bool Dev(string b) => DevUntil.TryGetValue(b, out var t) && Time.unscaledTime < t;
    private static InputDevice _device;
    private static Il2CppSystem.Action<ulong, float> _callback;

    public static bool Attached => _device != null;

    public static void Ensure()
    {
        if (_device != null) return;
        try
        {
            _device = new InputDevice("InkVR Quest Controllers");
            _device.DeviceClass = InputDeviceClass.Controller;
            _device.DeviceStyle = InputDeviceStyle.XboxOne;
            foreach (var t in new[]
                     {
                         InputControlType.LeftStickUp, InputControlType.LeftStickDown, InputControlType.LeftStickLeft, InputControlType.LeftStickRight,
                         InputControlType.RightStickUp, InputControlType.RightStickDown, InputControlType.RightStickLeft, InputControlType.RightStickRight,
                         InputControlType.Action1, InputControlType.Action2, InputControlType.Action3, InputControlType.Action4,
                         InputControlType.LeftTrigger, InputControlType.RightTrigger, InputControlType.LeftBumper, InputControlType.RightBumper,
                         InputControlType.LeftStickButton, InputControlType.RightStickButton,
                         InputControlType.Menu, InputControlType.Start, InputControlType.Command, InputControlType.View, InputControlType.Back
                     })
                _device.AddControl(t, t.ToString());

            InputManager.AttachDevice(_device);
            _callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ulong, float>>(new Action<ulong, float>(OnUpdateDevices));
            InputManager.add_OnUpdateDevices(_callback);
            Plugin.L.LogInfo("Quest controllers attached to the game as an InControl gamepad");
        }
        catch (Exception e)
        {
            Plugin.L.LogError("Could not attach the virtual gamepad: " + e);
            _device = null;
        }
    }

    /// <summary>The state the game sees this frame: the controllers plus any dev/test holds.</summary>
    public static State Effective
    {
        get
        {
            var s = Current;
            s.A |= Dev("a");
            s.B |= Dev("b");
            s.X |= Dev("x");
            s.Y |= Dev("y");
            s.Menu |= Dev("menu");
            s.LeftStickButton |= Dev("ls");
            if (Dev("lt")) s.LeftTrigger = 1;
            if (Dev("rt")) s.RightTrigger = 1;
            if (Time.unscaledTime < _devStickUntil) s.LeftStick = _devStick;
            return s;
        }
    }

    private static void OnUpdateDevices(ulong tick, float dt)
    {
        if (_device == null) return;
        var s = Effective;
        _device.UpdateLeftStickWithValue(s.LeftStick, tick, dt);
        _device.UpdateRightStickWithValue(Vector2.zero, tick, dt); // VR turning is handled by InkVR, never the game's look
        _device.UpdateWithState(InputControlType.Action1, s.A, tick, dt);
        _device.UpdateWithState(InputControlType.Action2, s.B, tick, dt);
        _device.UpdateWithState(InputControlType.Action3, s.X, tick, dt);
        _device.UpdateWithState(InputControlType.Action4, s.Y, tick, dt);
        _device.UpdateWithValue(InputControlType.LeftTrigger, s.LeftTrigger, tick, dt);
        _device.UpdateWithValue(InputControlType.RightTrigger, s.RightTrigger, tick, dt);
        _device.UpdateWithState(InputControlType.LeftStickButton, s.LeftStickButton, tick, dt);
        _device.UpdateWithState(InputControlType.Menu, s.Menu, tick, dt);
        _device.UpdateWithState(InputControlType.Start, s.Menu, tick, dt);
        _device.UpdateWithState(InputControlType.Command, s.Menu, tick, dt);
    }
}
