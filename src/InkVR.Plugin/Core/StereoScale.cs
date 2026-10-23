using UnityEngine;

namespace InkVR.Core;

/// <summary>
/// Sets each eye so the two are exactly (your IPD × world scale) apart, centred on the head.
///
/// Unity derives the eye view matrices from the runtime's eye poses. Depending on the runtime and how the
/// camera's parent is scaled, those can come out in real metres (eyes a third as far apart as the scaled
/// world needs, so everything feels giant) or already scaled (scaling again would put the eyes ~57 cm
/// apart: a miniature world, tiny hands and double vision up close). Rather than assume either, measure:
/// a human IPD is 50–80 mm, so whichever reading of Unity's separation lands in that range is the IPD.
/// </summary>
public static class StereoScale
{
    private static readonly Vector3 FlipZ = new(1, 1, -1);
    private const float MinIpd = 0.045f, MaxIpd = 0.085f, FallbackIpd = 0.064f;

    /// <summary>Your IPD in metres, as last measured from the runtime.</summary>
    public static float MeasuredIpd { get; private set; } = FallbackIpd;

    /// <summary>Unity's default eye separation came pre-scaled by the rig (1) or in metres (0); -1 = not yet seen.</summary>
    public static int PreScaled { get; private set; } = -1;

    /// <summary>The eye separation actually rendered, in game units.</summary>
    public static float RenderedSeparation { get; private set; }

    public static void Apply(Camera cam, float scale)
    {
        if (cam == null || !cam.stereoEnabled) return;
        cam.ResetStereoViewMatrices();

        // Read both eyes before setting either: setting one eye resets the other to custom (zero).
        var lv = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
        var rv = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
        Vector3 lp = lv.inverse.GetColumn(3), rp = rv.inverse.GetColumn(3);
        var sep = Vector3.Distance(lp, rp);
        if (sep < 1e-5f) return;

        var lossy = Mathf.Max(1e-3f, cam.transform.lossyScale.x);
        int prescaled;
        float ipd;
        if (sep >= MinIpd && sep <= MaxIpd) (prescaled, ipd) = (0, sep);
        else if (sep / lossy >= MinIpd && sep / lossy <= MaxIpd) (prescaled, ipd) = (1, sep / lossy);
        else (prescaled, ipd) = (PreScaled < 0 ? 0 : PreScaled, MeasuredIpd);

        if (prescaled != PreScaled)
        {
            PreScaled = prescaled;
            Plugin.L.LogInfo($"Stereo: runtime IPD {ipd * 1000f:F1} mm; Unity's eye offsets arrive {(prescaled == 1 ? "already world-scaled" : "in metres")}; rendering eyes {ipd * scale:F3} units apart (scale {scale:F2})");
        }

        MeasuredIpd = ipd;
        var target = ipd * scale;
        RenderedSeparation = target;
        if (Mathf.Abs(target - sep) < 1e-4f) return;

        var factor = target / sep;
        var mid = (lp + rp) * 0.5f;
        cam.SetStereoViewMatrix(Camera.StereoscopicEye.Left, Scaled(lv, mid, factor));
        cam.SetStereoViewMatrix(Camera.StereoscopicEye.Right, Scaled(rv, mid, factor));
    }

    private static Matrix4x4 Scaled(Matrix4x4 view, Vector3 mid, float factor)
    {
        var eyeToWorld = view.inverse;
        Vector3 pos = eyeToWorld.GetColumn(3);
        var forward = -((Vector3)eyeToWorld.GetColumn(2)).normalized;
        var up = ((Vector3)eyeToWorld.GetColumn(1)).normalized;
        var scaledPos = mid + (pos - mid) * factor;
        return Matrix4x4.TRS(scaledPos, Quaternion.LookRotation(forward, up), FlipZ).inverse;
    }
}
