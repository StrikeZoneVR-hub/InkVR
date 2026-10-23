using HarmonyLib;
using InkVR.Core;
using InkVR.Input;
using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// Guns fire where your hand points. The game checks a gun's hit with a ray from the camera; in VR that ray
/// comes from the dominant controller's aim instead (the gun model is already in that hand, so its muzzle
/// flash and tracers line up). Everything after the ray (damage, impacts, sounds) is the game's own.
/// GUN AIM: HEAD keeps the flat game's look-to-aim.
/// </summary>
[HarmonyPatch(typeof(BaseWeapon), nameof(BaseWeapon.CheckHitRaycast))]
public static class GunAim
{
    public static string LastShot = "";

    [HarmonyPrefix]
    private static bool Prefix(BaseWeapon __instance)
    {
        if (!ModConfig.AimGunsWithHand.Value || !XrManager.IsRunning || !VRRig.InGame || VRRig.CinemaActive) return true;
        if (__instance.TryCast<GunWeapon>() == null) return true;
        var c = ControllerBindings.Dom;
        if (!c.PoseValid) return true;

        VRRig.TrackingToWorld(c.AimPosition, c.AimRotation, out var from, out var rot);
        if (WeaponHandling.TwoHandAim(out var twoFrom, out var twoRot))
        {
            from = twoFrom;
            rot = twoRot;
        }
        var dir = rot * Vector3.forward;
        var range = __instance.m_WeaponRange > 0 ? __instance.m_WeaponRange : 100f;
        var mask = ~__instance.m_IgnoreLayers.value;
        if (Physics.Raycast(from, dir, out var hit, range, mask, QueryTriggerInteraction.Ignore))
        {
            LastShot = $"{Dev.DevTools.PathOf(hit.collider.transform)} at {hit.point}";
            __instance.OnRaycastHit(hit);
        }
        else
        {
            LastShot = "nothing";
        }

        return false;
    }
}
