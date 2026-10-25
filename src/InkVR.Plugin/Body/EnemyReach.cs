using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// Hitting an enemy anywhere on it. While an enemy is alive the game keeps only a narrow capsule round its middle
/// (and a wide trigger) for hits; the collision shapes on its skeleton (head, arms, legs: the ragdoll it falls as)
/// stay switched off until it dies. Those bones still move with every animation, so a swing is measured against
/// their shapes here, in plain maths, along with a slightly fuller version of the body capsule: touch an arm, a
/// head or a leg with any part of the weapon and it's a hit on that enemy.
/// </summary>
internal static class EnemyReach
{
    private enum Kind { Capsule, Sphere, Box }

    private struct Shape
    {
        public Transform T;
        public Kind K;
        public F3 Centre, Axis, Size;
        public float Height, Radius;
    }

    private sealed class Enemy
    {
        public Ai.BaseAiController Ai;
        public Collider Body;
        public Shape[] Shapes;
    }

    private struct World
    {
        public Kind K;
        public F3 A, B;     // capsule ends (sphere: A = B)
        public float R;
        public M34 ToLocal, ToWorld;
        public F3 Centre, Half; // box, in its own space
    }

    private const float BodyFullness = 1.35f; // the body capsule, a little fuller: the arms swing just outside it

    private static readonly Dictionary<System.IntPtr, Enemy> Cache = new();
    private static readonly List<World> Shapes = new();
    private static readonly HashSet<System.IntPtr> Seen = new();
    private static Il2CppReferenceArray<Collider> _near;
    private static string _scene = "";

    /// <summary>
    /// The weapon's points moved from <paramref name="from"/> to <paramref name="to"/> this frame. The first living
    /// enemy any of them touched (earliest along the swing), the collider that takes its hits, and where.
    /// </summary>
    public static bool Touch(F3[] from, F3[] to, float radius, Vector3 around, float searchRadius, int mask,
        out Ai.BaseAiController hitAi, out Collider body, out Vector3 at)
    {
        hitAi = null;
        body = null;
        at = default;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != _scene)
        {
            _scene = scene;
            Cache.Clear();
        }

        _near ??= new Il2CppReferenceArray<Collider>(48);
        var n = Physics.OverlapSphereNonAlloc(around, searchRadius, _near, mask, QueryTriggerInteraction.Collide);
        Seen.Clear();
        var bestT = float.MaxValue;
        for (var i = 0; i < n; i++)
        {
            var col = _near[i];
            if (col == null) continue;
            var ai = col.GetComponentInParent<Ai.BaseAiController>();
            if (ai == null || !Seen.Add(ai.Pointer) || !ai.isActiveAndEnabled || ai.m_CurrentHealth <= 0) continue;
            var e = Get(ai);
            if (e.Body == null) continue;
            if (!FirstContact(e, from, to, radius, out var t, out var p) || t >= bestT) continue;
            bestT = t;
            hitAi = ai;
            body = e.Body;
            at = p.V;
        }

        return hitAi != null;
    }

    private static Enemy Get(Ai.BaseAiController ai)
    {
        if (Cache.TryGetValue(ai.Pointer, out var e) && e.Ai != null) return e;
        e = new Enemy { Ai = ai };
        var shapes = new List<Shape>();
        foreach (var c in ai.GetComponentsInChildren<Collider>(true))
        {
            var onRoot = c.gameObject == ai.gameObject;
            if (onRoot)
            {
                // the collider the game takes hits on: the body (not the wide trigger round it)
                if (!c.isTrigger && e.Body == null) e.Body = c;
                if (c.isTrigger) continue;
            }

            var s = new Shape { T = c.transform };
            var cc = c.TryCast<CharacterController>();
            var cap = c.TryCast<CapsuleCollider>();
            var sph = c.TryCast<SphereCollider>();
            var box = c.TryCast<BoxCollider>();
            if (cc != null)
            {
                s.K = Kind.Capsule;
                s.Centre = F3.Of(cc.center);
                s.Axis = F3.Up;
                s.Height = cc.height;
                s.Radius = cc.radius * BodyFullness;
            }
            else if (cap != null)
            {
                s.K = Kind.Capsule;
                s.Centre = F3.Of(cap.center);
                s.Axis = cap.direction == 0 ? new F3(1, 0, 0) : cap.direction == 2 ? new F3(0, 0, 1) : F3.Up;
                s.Height = cap.height;
                s.Radius = cap.radius * (onRoot ? BodyFullness : 1f);
            }
            else if (sph != null)
            {
                s.K = Kind.Sphere;
                s.Centre = F3.Of(sph.center);
                s.Radius = sph.radius;
            }
            else if (box != null)
            {
                s.K = Kind.Box;
                s.Centre = F3.Of(box.center);
                s.Size = F3.Of(box.size);
            }
            else continue;

            shapes.Add(s);
        }

        if (e.Body == null)
            foreach (var c in ai.GetComponents<Collider>())
            {
                e.Body = c;
                break;
            }

        e.Shapes = shapes.ToArray();
        Cache[ai.Pointer] = e;
        Plugin.L.LogInfo($"Enemy reach: {ai.GetIl2CppType().Name} '{ai.name}', {e.Shapes.Length} body shapes, hits taken on '{e.Body?.name}'");
        return e;
    }

    /// <summary>The earliest moment (0..1 along this frame's motion) any weapon point touches the enemy, and where.</summary>
    private static bool FirstContact(Enemy e, F3[] from, F3[] to, float radius, out float when, out F3 at)
    {
        when = float.MaxValue;
        at = default;
        Shapes.Clear();
        foreach (var s in e.Shapes)
        {
            if (s.T == null) continue;
            var m = M34.Of(s.T.localToWorldMatrix);
            var w = new World { K = s.K, ToWorld = m };
            var scale = MaxScale(m);
            switch (s.K)
            {
                case Kind.Capsule:
                {
                    var half = s.Height * 0.5f - s.Radius;
                    if (half < 0) half = 0;
                    w.A = m.Point(s.Centre + s.Axis * half);
                    w.B = m.Point(s.Centre - s.Axis * half);
                    w.R = s.Radius * scale;
                    break;
                }
                case Kind.Sphere:
                    w.A = w.B = m.Point(s.Centre);
                    w.R = s.Radius * scale;
                    break;
                default:
                    w.ToLocal = M34.Of(s.T.worldToLocalMatrix);
                    w.Centre = s.Centre;
                    w.Half = s.Size * 0.5f;
                    break;
            }

            Shapes.Add(w);
        }

        const int steps = 4;
        for (var k = 0; k <= steps; k++)
        {
            var t = k / (float)steps;
            for (var p = 0; p < from.Length; p++)
            {
                var q = from[p] + (to[p] - from[p]) * t;
                foreach (var w in Shapes)
                {
                    if (Distance(w, q) > radius) continue;
                    when = t;
                    at = q;
                    return true;
                }
            }
        }

        return false;
    }

    private static float MaxScale(in M34 m)
    {
        var x = m.A00 * m.A00 + m.A10 * m.A10 + m.A20 * m.A20;
        var y = m.A01 * m.A01 + m.A11 * m.A11 + m.A21 * m.A21;
        var z = m.A02 * m.A02 + m.A12 * m.A12 + m.A22 * m.A22;
        return System.MathF.Sqrt(FM.Max(x, FM.Max(y, z)));
    }

    /// <summary>How far a point is outside a shape (negative inside).</summary>
    private static float Distance(in World w, F3 q)
    {
        if (w.K == Kind.Box)
        {
            var l = w.ToLocal.Point(q) - w.Centre;
            var c = new F3(FM.Clamp(l.X, -w.Half.X, w.Half.X), FM.Clamp(l.Y, -w.Half.Y, w.Half.Y), FM.Clamp(l.Z, -w.Half.Z, w.Half.Z));
            return (w.ToWorld.Point(c + w.Centre) - q).Len;
        }

        var ab = w.B - w.A;
        var len2 = ab.Len2;
        var t = len2 > 1e-8f ? FM.Clamp01(F3.Dot(q - w.A, ab) / len2) : 0f;
        return (w.A + ab * t - q).Len - w.R;
    }
}
