using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// The ink's time. Normally real time; while the dev recorder captures video it steps a fixed amount each
/// frame, so the ink runs, drips and dries at the video's pace however long each frame takes to capture.
/// </summary>
public static class InkClock
{
    /// <summary>Seconds per frame while recording (0: real time).</summary>
    public static float FixedStep;

    private static float _now;
    private static int _frame = -1;

    private static void Tick()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;
        _now = FixedStep > 0 ? _now + FixedStep : Time.unscaledTime;
    }

    public static float Now
    {
        get
        {
            Tick();
            return _now;
        }
    }

    public static float Dt
    {
        get
        {
            Tick();
            return FixedStep > 0 ? FixedStep : Time.unscaledDeltaTime;
        }
    }
}
