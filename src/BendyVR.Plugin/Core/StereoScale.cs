using UnityEngine;

namespace BendyVR.Core;

/// <summary>
/// Unity applies the runtime's eye offsets (real metres) relative to the camera without the rig's
/// scale, so in a world of ~3 units per metre the eyes would be a third as far apart as they should be
/// and everything would feel giant. Rebuild each eye's view matrix with the offset scaled to the world.
/// </summary>
public static class StereoScale
{
    private static readonly Vector3 FlipZ = new(1, 1, -1);

    public static void Apply(Camera cam, float scale)
    {
        if (cam == null || !cam.stereoEnabled) return;
        cam.ResetStereoViewMatrices();
        if (Mathf.Abs(scale - 1f) < 0.001f) return;

        // Read both eyes before setting either: setting one eye resets the other to custom (zero).
        var center = cam.transform.position;
        var left = Scaled(cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left), center, scale);
        var right = Scaled(cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right), center, scale);
        cam.SetStereoViewMatrix(Camera.StereoscopicEye.Left, left);
        cam.SetStereoViewMatrix(Camera.StereoscopicEye.Right, right);
    }

    private static Matrix4x4 Scaled(Matrix4x4 view, Vector3 center, float scale)
    {
        var eyeToWorld = view.inverse;
        Vector3 pos = eyeToWorld.GetColumn(3);
        var forward = -((Vector3)eyeToWorld.GetColumn(2)).normalized;
        var up = ((Vector3)eyeToWorld.GetColumn(1)).normalized;
        var scaledPos = center + (pos - center) * scale;
        return Matrix4x4.TRS(scaledPos, Quaternion.LookRotation(forward, up), FlipZ).inverse;
    }
}
