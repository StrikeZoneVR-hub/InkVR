using UnityEngine;

namespace InkVR.Core;

/// <summary>
/// Eye separation in the scaled world. Unity already derives each eye's view from the runtime's eye
/// poses *through the rig's scale*: the default separation is your IPD × world scale, which is exactly
/// right, on every runtime. (Early InkVR builds scaled it a second time: the Unity mock runtime's eyes are
/// only 22 mm apart, so 22 mm × 2.95 looked like an unscaled 65 mm. On a real headset that double scaling
/// put the eyes ~57 cm apart: a miniature world, small hands, double vision up close.)
/// So InkVR leaves the eyes to Unity and only measures them: your IPD is the separation over the scale.
/// </summary>
public static class StereoScale
{
    private const float FallbackIpd = 0.064f;

    /// <summary>Your IPD in metres, as last measured from the runtime.</summary>
    public static float MeasuredIpd { get; private set; } = FallbackIpd;

    /// <summary>The eye separation actually rendered, in game units.</summary>
    public static float RenderedSeparation { get; private set; }

    /// <summary>1 once measured (kept for the log format).</summary>
    public static int PreScaled { get; private set; } = -1;

    private static bool _logged;

    public static void Apply(Camera cam, float scale)
    {
        if (cam == null || !cam.stereoEnabled) return;
        cam.ResetStereoViewMatrices();

        Vector3 l = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
        Vector3 r = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);
        var sep = Vector3.Distance(l, r);
        var lossy = Mathf.Max(1e-3f, cam.transform.lossyScale.x);
        if (sep < 1e-5f) return;
        RenderedSeparation = sep;
        MeasuredIpd = sep / lossy;
        PreScaled = 1;
        if (_logged) return;
        _logged = true;
        Plugin.L.LogInfo($"Stereo: runtime IPD {MeasuredIpd * 1000f:F1} mm; eyes rendered {sep:F3} units apart at {lossy:F2} units per metre (Unity's own scaled eyes)");
    }
}
