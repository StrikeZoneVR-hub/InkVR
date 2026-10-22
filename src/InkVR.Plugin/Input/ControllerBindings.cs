using HarmonyLib;
using InkVR.Core;
using InkVR.UI;
using UnityEngine;

namespace InkVR.Input;

/// <summary>
/// Button-press control scheme (M2). Motion controls come later; every game action is reachable here:
///   off-hand stick  move         dominant stick  snap/smooth turn
///   A (dominant)    jump         B (dominant)    back
///   grips / off-hand trigger     interact
///   dominant trigger             attack · click in menus
///   off-hand stick click         sprint          menu button     pause
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

        VirtualInput.Move = Deadzone(off.Stick, 0.15f);
        Turn(dom.Stick.x);

        var uiHasPointer = LaserPointer.IsOverInteractive;
        VirtualInput.SetHeld(GameAction.Jump, dom.Primary && !uiHasPointer);
        VirtualInput.SetHeld(GameAction.Back, dom.Secondary);
        VirtualInput.SetHeld(GameAction.Pause, off.Menu);
        VirtualInput.SetHeld(GameAction.Run, off.StickClick);
        VirtualInput.SetHeld(GameAction.Interact, !uiHasPointer && (dom.GripPressed || off.GripPressed || off.TriggerPressed));
        VirtualInput.SetHeld(GameAction.Attack, !uiHasPointer && dom.TriggerPressed);
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
