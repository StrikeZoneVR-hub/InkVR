using System.Text;
using UnityEngine;
using UnityEngine.PostProcessing;

namespace BendyVR.Rendering;

/// <summary>
/// VR adjustments to the game's PostProcessing (v1) stack. Effects that smear or break in a headset
/// (motion blur, depth of field, temporal AA) are switched off/over; everything else keeps the game's settings.
/// </summary>
public static class VRGraphics
{
    private static float _nextApply;

    public static void Update(Camera gameCam)
    {
        if (gameCam == null || Time.unscaledTime < _nextApply) return;
        _nextApply = Time.unscaledTime + 1f; // the game re-applies its own options on load/menu changes
        foreach (var ppb in gameCam.GetComponentsInChildren<PostProcessingBehaviour>(true))
            Apply(ppb.profile);
    }

    private static void Apply(PostProcessingProfile p)
    {
        if (p == null) return;
        if (ModConfig.VRDisableMotionBlur.Value && p.motionBlur.enabled) p.motionBlur.enabled = false;
        if (ModConfig.VRDisableDepthOfField.Value && p.depthOfField.enabled) p.depthOfField.enabled = false;
        if (ModConfig.VRAODownsample.Value && p.ambientOcclusion.enabled)
        {
            var ao = p.ambientOcclusion.settings;
            if (!ao.downsampling)
            {
                ao.downsampling = true; // half-resolution AO: about half the cost, nearly identical look
                p.ambientOcclusion.settings = ao;
            }
        }

        if (p.antialiasing.enabled)
        {
            var s = p.antialiasing.settings;
            if (s.method == AntialiasingModel.Method.Taa)
            {
                s.method = AntialiasingModel.Method.Fxaa;
                p.antialiasing.settings = s;
            }
        }
    }

    public static string Describe(Camera gameCam)
    {
        var sb = new StringBuilder();
        foreach (var ppb in gameCam.GetComponentsInChildren<PostProcessingBehaviour>(true))
        {
            var p = ppb.profile;
            if (p == null) continue;
            sb.Append($"[{ppb.name}:{p.name}] ");
            sb.Append($"aa={p.antialiasing.enabled}/{p.antialiasing.settings.method} ao={p.ambientOcclusion.enabled} ssr={p.screenSpaceReflection.enabled} ");
            sb.Append($"dof={p.depthOfField.enabled} mblur={p.motionBlur.enabled} eyeAdapt={p.eyeAdaptation.enabled} bloom={p.bloom.enabled} ");
            sb.Append($"grading={p.colorGrading.enabled} chroma={p.chromaticAberration.enabled} grain={p.grain.enabled} vignette={p.vignette.enabled} ");
            sb.Append($"dither={p.dithering.enabled} fog={p.fog.enabled} lut={p.userLut.enabled} ");
            var ao = p.ambientOcclusion.settings;
            sb.Append($"ao(samples={ao.sampleCount} down={ao.downsampling} radius={ao.radius} intensity={ao.intensity})\n");
        }

        return sb.ToString();
    }

    /// <summary>Dev: toggle one effect on every PPv1 profile under the camera.</summary>
    public static void Set(Camera gameCam, string effect, bool on)
    {
        foreach (var ppb in gameCam.GetComponentsInChildren<PostProcessingBehaviour>(true))
        {
            var p = ppb.profile;
            if (p == null) continue;
            PostProcessingModel m = effect switch
            {
                "aa" => p.antialiasing, "ao" => p.ambientOcclusion, "ssr" => p.screenSpaceReflection, "dof" => p.depthOfField,
                "mblur" => p.motionBlur, "eye" => p.eyeAdaptation, "bloom" => p.bloom, "grading" => p.colorGrading,
                "chroma" => p.chromaticAberration, "grain" => p.grain, "vignette" => p.vignette, "dither" => p.dithering,
                "fog" => p.fog, _ => null
            };
            if (m != null) m.enabled = on;
        }
    }
}
