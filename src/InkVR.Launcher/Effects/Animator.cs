using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace InkVR.Launcher.Effects
{
    /// <summary>
    /// Time source for every animation. Normally wall-clock; the --capture mode steps it frame by frame
    /// so recorded frames are evenly spaced however slow the capture is.
    /// </summary>
    public static class Clock
    {
        private static readonly Stopwatch Watch = Stopwatch.StartNew();
        public static double? Fixed;
        public static double Now => Fixed ?? Watch.Elapsed.TotalSeconds;
    }

    /// <summary>A tiny tween runner driven once per rendered frame.</summary>
    public sealed class Animator
    {
        private sealed class Tween
        {
            public double Start, Duration;
            public Func<double, double> Ease;
            public Action<double> Apply;
            public Action Done;
            public string Tag;
        }

        private readonly List<Tween> _tweens = new List<Tween>();
        public double Now { get; private set; }

        public void Run(double delay, double duration, Func<double, double> ease, Action<double> apply, Action done = null, string tag = null)
        {
            _tweens.Add(new Tween { Start = Now + delay, Duration = Math.Max(0, duration), Ease = ease ?? Effects.Ease.Linear, Apply = apply, Done = done, Tag = tag });
        }

        public void At(double delay, Action action, string tag = null) => Run(delay, 0, null, null, action, tag);

        public bool Busy(string tag) => _tweens.Exists(t => t.Tag == tag);

        public void Cancel(string tag) => _tweens.RemoveAll(t => t.Tag == tag);

        public void Update(double now)
        {
            Now = now;
            var snapshot = _tweens.ToArray();
            foreach (var t in snapshot)
            {
                if (now < t.Start || !_tweens.Contains(t)) continue;
                var p = t.Duration <= 0 ? 1 : Math.Min(1, (now - t.Start) / t.Duration);
                t.Apply?.Invoke(t.Ease(p));
                if (p >= 1)
                {
                    _tweens.Remove(t);
                    t.Done?.Invoke();
                }
            }
        }

        /// <summary>Jumps every pending tween with this tag (all when null) to its end, in start order.</summary>
        public void Finish(string tag = null)
        {
            for (var guard = 0; guard < 1000; guard++)
            {
                Tween next = null;
                foreach (var t in _tweens)
                    if ((tag == null || t.Tag == tag) && (next == null || t.Start < next.Start))
                        next = t;
                if (next == null) return;
                _tweens.Remove(next);
                next.Apply?.Invoke(next.Ease(1));
                next.Done?.Invoke();
            }
        }
    }
}
