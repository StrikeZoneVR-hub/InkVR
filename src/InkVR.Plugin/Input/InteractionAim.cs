using HarmonyLib;
using InkVR.Core;
using UnityEngine;

namespace InkVR.Input;

/// <summary>
/// The game finds what you can interact with by casting from the camera. In VR you point with your
/// dominant hand instead (then press grip or X), unless "interact aim" is set to head.
/// </summary>
[HarmonyPatch(typeof(InteractableInputController))]
internal static class InteractionAim
{
    private static bool TryHandRay(ref Vector3 origin, ref Vector3 direction)
    {
        if (!ModConfig.InteractWithHand.Value || !XrManager.IsRunning || !VRRig.InGame || VRRig.CinemaActive) return false;
        var c = ControllerBindings.Dom;
        if (!XrInput.Ready || !c.PoseValid) return false;
        VRRig.TrackingToWorld(c.AimPosition, c.AimRotation, out var pos, out var rot);
        origin = pos;
        direction = rot * Vector3.forward;
        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(InteractableInputController.UpdateInteraction), typeof(Vector3), typeof(Vector3))]
    private static void Aim2(ref Vector3 origin, ref Vector3 direction) => TryHandRay(ref origin, ref direction);

    [HarmonyPrefix]
    [HarmonyPatch(nameof(InteractableInputController.UpdateInteraction), typeof(Vector3), typeof(Vector3), typeof(float))]
    private static void Aim3(ref Vector3 origin, ref Vector3 direction) => TryHandRay(ref origin, ref direction);
}
