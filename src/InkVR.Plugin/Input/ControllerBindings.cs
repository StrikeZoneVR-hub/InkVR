using HarmonyLib;
using InkVR.Core;
using InkVR.UI;
using UnityEngine;

namespace InkVR.Input;

/// <summary>
/// Button-press control scheme (M2), following the game's own gamepad layout so its button prompts match:
///   off-hand stick      move (and menu navigation)       dominant stick   snap/smooth turn
///   A                   jump                             B                back
///   X / either grip     interact                         dominant trigger weapon (click in menus)
///   off-hand trigger / off-hand stick click   run        menu button      pause
/// Motion controls come later; every game action is reachable with buttons now.
/// </summary>
public static class ControllerBindings
{
    private static bool _snapReady = true;
    private static int _sampledFrame = -1;

    public static Hand Dominant => ModConfig.LeftHanded.Value ? Hand.Left : Hand.Right;
    public static Hand OffHand => ModConfig.LeftHanded.Value ? Hand.Right : Hand.Left;
    public static ControllerState Dom => XrInput.Get(Dominant);
    public static ControllerState Off => XrInput.Get(OffHand);

    /// <summary>Runs once per frame, as early as possible (InControl's manager update, else ours).</summary>
    public static void Sample()
    {
        if (_sampledFrame == Time.frameCount) return;
        _sampledFrame = Time.frameCount;

        XrInput.Update();
        if (!XrInput.Ready) return;

        var dom = Dom;
        var off = Off;
        VirtualGamepad.Ensure();
        Turn(dom.Stick.x);

        // While the laser points at a menu, the dominant trigger is a mouse click, not the weapon.
        var uiHasPointer = LaserPointer.IsOverInteractive;
        // A grip taken by a hand gesture (valve, lever, soup), the gun's front or the holster isn't "interact".
        var (claimL, claimR) = PhysicalInteract.ClaimGrips();
        var domGrip = dom.GripPressed && !(Dominant == Hand.Left ? claimL : claimR);
        var offGrip = off.GripPressed && !(OffHand == Hand.Left ? claimL : claimR);
        VirtualGamepad.Current = new VirtualGamepad.State
        {
            LeftStick = Deadzone(off.Stick, 0.15f),
            A = dom.Primary,
            B = dom.Secondary,
            X = off.Primary || (!uiHasPointer && (domGrip || offGrip)),
            Y = off.Secondary,
            LeftTrigger = off.Trigger,
            RightTrigger = uiHasPointer ? 0f : dom.Trigger,
            LeftStickButton = off.StickClick,
            // Quest has a menu button on the left controller only (the right one's is Meta's, reserved).
            Menu = XrInput.Get(Hand.Left).Menu || XrInput.Get(Hand.Right).Menu
        };
    }

    private static void Turn(float x)
    {
        if (ModConfig.SnapTurn.Value)
        {
            if (_snapReady && Mathf.Abs(x) > 0.7f)
            {
                _snapReady = false;
                VRRig.Turn(Mathf.Sign(x) * ModConfig.SnapTurnAngle.Value);
                XrInput.Haptic(Dominant, 0.15f, 0.02f);
            }
            else if (Mathf.Abs(x) < 0.35f)
            {
                _snapReady = true;
            }

            VRRig.SmoothTurnInput = 0;
        }
        else
        {
            VRRig.SmoothTurnInput = Mathf.Abs(x) > 0.2f ? x : 0;
        }
    }

    private static Vector2 Deadzone(Vector2 v, float dz)
    {
        var m = v.magnitude;
        if (m < dz) return Vector2.zero;
        return v / m * Mathf.InverseLerp(dz, 1f, Mathf.Min(m, 1f));
    }
}

[HarmonyPatch(typeof(InControl.InControlManager), nameof(InControl.InControlManager.Update))]
internal static class InControlManagerUpdatePatch
{
    // InControl updates before gameplay scripts, so input sampled here is seen by them this frame.
    [HarmonyPrefix]
    private static void Prefix() => ControllerBindings.Sample();
}
