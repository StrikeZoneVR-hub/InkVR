using HarmonyLib;
using UnityEngine;

namespace InkVR.Rendering;

/// <summary>
/// The game's volumetric light beams (VolumetricLightRenderer + VolumetricLight) are drawn as light
/// cones projected with one "mono" view-projection matrix, so in a headset both eyes put the beam in
/// the same place on screen and it doubles. Each light receives that matrix from the renderer while the
/// eye is being prepared; swap in the current eye's own view-projection there (the camera itself is not
/// touched, so the rest of the frame keeps its stereo).
/// </summary>
[HarmonyPatch(typeof(VolumetricLight), "VolumetricLightRenderer_PreRenderEvent")]
public static class VolumetricStereo
{
    /// <summary>Dev: leave the beams on but skip the per-eye correction (to compare).</summary>
    public static bool DevNoFix;

    [HarmonyPrefix]
    private static void Prefix(VolumetricLightRenderer __0, ref Matrix4x4 __1)
    {
        if (DevNoFix || __0 == null || !Core.XrManager.IsRunning) return;
        var cam = __0._camera ?? __0.GetComponent<Camera>();
        if (cam == null || !cam.stereoEnabled) return;
        var active = cam.stereoActiveEye;
        if (active == Camera.MonoOrStereoscopicEye.Mono) return;
        var eye = active == Camera.MonoOrStereoscopicEye.Left ? Camera.StereoscopicEye.Left : Camera.StereoscopicEye.Right;
        __1 = GL.GetGPUProjectionMatrix(cam.GetStereoProjectionMatrix(eye), true) * cam.GetStereoViewMatrix(eye);
    }
}

/// <summary>LIGHT BEAMS off in VR: don't build or composite the beams at all (also saves GPU time).</summary>
[HarmonyPatch(typeof(VolumetricLightRenderer))]
internal static class VolumetricBeamsOff
{
    private static bool Off(VolumetricLightRenderer r)
    {
        if (ModConfig.VolumetricLightVR.Value || !Core.XrManager.IsRunning) return false;
        var cam = r._camera ?? r.GetComponent<Camera>();
        return cam != null && cam.stereoEnabled;
    }

    [HarmonyPrefix, HarmonyPatch("OnPreRender")]
    private static bool BeforePreRender(VolumetricLightRenderer __instance) => !Off(__instance);

    [HarmonyPrefix, HarmonyPatch("OnRenderImage")]
    private static bool BeforeRenderImage(VolumetricLightRenderer __instance, RenderTexture __0, RenderTexture __1)
    {
        if (!Off(__instance)) return true;
        Graphics.Blit(__0, __1);
        return false;
    }
}
