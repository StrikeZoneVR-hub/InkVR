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
        // the weapon wheel takes the right stick while it's open
        if (!UI.WeaponWheel.IsOpen) Turn(dom.Stick.x);
        else VRRig.SmoothTurnInput = 0;

        // While the laser points at a menu, the dominant trigger is a mouse click, not the weapon.
        var uiHasPointer = LaserPointer.IsOverInteractive;
        // A grip taken by a hand gesture (valve, lever, soup), the gun's front or the holster isn't "interact".
        var (claimL, claimR) = PhysicalInteract.ClaimGrips();
        var domGrip = dom.GripPressed && !(Dominant == Hand.Left ? claimL : claimR);
        var offGrip = off.GripPressed && !(OffHand == Hand.Left ? claimL : claimR);
        VirtualGamepad.Current = new VirtualGamepad.State
        {
            // (a cutscene holds walking: you keep your body, arms and head, but the stick doesn't move Henry)
            // (in the theatre, a push of the stick while you're not pointing at the screen walks you instead)
            LeftStick = VRRig.MovementHeld || UI.TheatreWalk.WantsStick(off) ? Vector2.zero : Deadzone(off.Stick, 0.15f),
            A = dom.Primary,
            B = dom.Secondary,
            X = off.Primary || (!uiHasPointer && (domGrip || offGrip)),
            Y = UI.InventoryButton.FilterY(off.Secondary),
            LeftTrigger = off.Trigger,
            // (the trigger picks from the weapon wheel while it's open, rather than swinging)
            // (and in a throwing game it's the throw, made by your arm: see MinigameThrow)
            RightTrigger = uiHasPointer || UI.WeaponWheel.IsOpen || MinigameThrow.Owns ? 0f : Body.MeleeSwing.FilterAttack(dom.Trigger),
            LeftStickButton = off.StickClick && !VRRig.CinemaShowing, // (in the theatre it's walk / menu)
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

// The game's menus have two modes. With a mouse, what the pointer is over is what's selected, and a click acts on it;
// once it has seen a gamepad (the Quest controllers are one, so any push of the stick), hovering stops selecting, and a
// click acts on whatever the stick last picked: the laser would click OPTIONS and get QUIT. While the laser is on the
// screen the player is pointing, so the game is told it's a mouse.
[HarmonyPatch(typeof(GameManager), nameof(GameManager.IsUsingController), MethodType.Getter)]
internal static class UsingControllerPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result)
    {
        if (!UI.LaserPointer.OnScreen) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(InControl.InControlManager), nameof(InControl.InControlManager.Update))]
internal static class InControlManagerUpdatePatch
{
    // InControl updates before gameplay scripts, so input sampled here is seen by them this frame.
    [HarmonyPrefix]
    private static void Prefix() => ControllerBindings.Sample();
}
