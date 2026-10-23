using HarmonyLib;
using UnityEngine;

namespace InkVR.Core;

/// <summary>
/// The game moves the player in FixedUpdate (50 Hz) while the headset shows 72–120 frames a second, so
/// walking moves your view on some frames and not others: judder. Like Unity's Rigidbody interpolation,
/// the rig is drawn between the last two physics positions according to how far the frame is into the
/// current physics step. Applied just before rendering and removed again before the game's next update,
/// so game logic only ever sees the real positions.
/// </summary>
public static class MotionSmoothing
{
    private static Vector3 _lastStep;
    private static Vector3 _applied;
    private static Transform _appliedTo;

    internal static void OnFixedStep(Vector3 delta)
    {
        // A scripted teleport is not motion to smooth.
        _lastStep = delta.sqrMagnitude > 16f ? Vector3.zero : delta;
    }

    /// <summary>Take last frame's visual offset back off (start of the frame).</summary>
    public static void Remove()
    {
        if (_appliedTo != null) _appliedTo.position -= _applied;
        _applied = Vector3.zero;
        _appliedTo = null;
    }

    /// <summary>Draw the rig where the player is between physics steps (after the game's updates).</summary>
    public static void Apply(Transform origin)
    {
        Remove();
        if (origin == null || Time.fixedDeltaTime <= 0) return;
        var alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
        var offset = -_lastStep * (1f - alpha);
        if (offset.sqrMagnitude < 1e-10f) return;
        origin.position += offset;
        _applied = offset;
        _appliedTo = origin;
    }

    public static void Reset()
    {
        Remove();
        _lastStep = Vector3.zero;
    }
}

[HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
internal static class PlayerFixedUpdatePatch
{
    private static Vector3 _before;

    [HarmonyPrefix]
    private static void Prefix(PlayerController __instance) => _before = __instance.transform.position;

    [HarmonyPostfix]
    private static void Postfix(PlayerController __instance) => MotionSmoothing.OnFixedStep(__instance.transform.position - _before);
}
