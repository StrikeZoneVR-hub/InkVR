using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InkVR.Core;
using InkVR.Input;
using InkVR.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace InkVR.Body;

/// <summary>
/// Finds the ink in each level and gets it onto Henry when he touches it:
///   splat decals on floors and walls  → a blot where a finger or palm brushes them
///   ink pools / flooded floors        → whatever dips under the surface is coated
///   ink drip and splash particles     → drops that land on a hand or sleeve
///   the game's own ink burst on the player (scares, ink attacks) → a splatter over both arms
/// </summary>
public static class InkWorld
{
    private sealed class Decal
    {
        public Renderer R;
        public Bounds B;
        public Vector3[] Tris; // world space, 3 per triangle
        public Vector3 Centre;
        public float Radius;
        public float LastHit;
    }

    private sealed class Pool
    {
        public Renderer R;
    }

    private static readonly List<Decal> Decals = new();
    private static readonly List<Pool> Pools = new();
    private static readonly List<ParticleSystem> Particles = new();
    private static readonly List<ParticleSystem> Bursts = new();
    private static readonly Dictionary<IntPtr, int> BurstCounts = new();
    private static Il2CppStructArray<ParticleSystem.Particle> _particleBuf;
    private static string _scene;
    private static float _nextScan, _nextContact, _nextDip, _nextParticles;
    private static int _scans;

    public static int DecalCount => Decals.Count;

    /// <summary>Dev: a point on the surface of the splat nearest to the player (and the surface normal).</summary>
    public static bool NearestDecalPoint(Vector3 from, out Vector3 point, out Vector3 normal)
    {
        point = normal = default;
        // Prefer splats on the floor near the player (easy to reach down to in a test).
        Decal best = null;
        foreach (var d in Decals)
        {
            var floor = d.B.extents.y < 0.6f && d.B.center.y < from.y;
            if (!floor) continue;
            if (best == null || (d.Centre - from).sqrMagnitude < (best.Centre - from).sqrMagnitude) best = d;
        }

        if (best == null)
            foreach (var d in Decals)
                if (best == null || (d.Centre - from).sqrMagnitude < (best.Centre - from).sqrMagnitude)
                    best = d;
        if (best == null) return false;
        if (best.Tris == null || best.Tris.Length < 3)
        {
            // No surface data: the box's thinnest axis is the surface normal.
            var e = best.B.extents;
            normal = e.y <= e.x && e.y <= e.z ? Vector3.up : e.x <= e.z ? Vector3.right : Vector3.forward;
            point = best.B.center;
            return true;
        }

        var bestT = 0;
        for (var t = 0; t < best.Tris.Length; t += 3)
        {
            var c = (best.Tris[t] + best.Tris[t + 1] + best.Tris[t + 2]) / 3f;
            var cb = (best.Tris[bestT] + best.Tris[bestT + 1] + best.Tris[bestT + 2]) / 3f;
            if ((c - best.Centre).sqrMagnitude < (cb - best.Centre).sqrMagnitude) bestT = t;
        }

        point = (best.Tris[bestT] + best.Tris[bestT + 1] + best.Tris[bestT + 2]) / 3f;
        normal = Vector3.Cross(best.Tris[bestT + 1] - best.Tris[bestT], best.Tris[bestT + 2] - best.Tris[bestT]).normalized;
        return true;
    }
    public static int PoolCount => Pools.Count;
    public static int ParticleCount => Particles.Count;

    public static void Update()
    {
        if (!ModConfig.InkOnHands.Value || !XrManager.IsRunning || !VRRig.InGame || VRRig.CinemaActive)
        {
            Droplets.Update();
            return;
        }

        var scene = SceneManager.GetActiveScene().name;
        if (scene != _scene)
        {
            _scene = scene;
            _scans = 0;
            _nextScan = Time.unscaledTime + 2f;
        }

        if (Time.unscaledTime >= _nextScan)
        {
            Scan();
            _scans++;
            _nextScan = Time.unscaledTime + (_scans < 3 ? 6f : 120f);
        }

        if (Time.unscaledTime >= _nextContact)
        {
            _nextContact = Time.unscaledTime + 1f / 30f;
            TouchDecals();
            WatchBursts();
        }

        if (Time.unscaledTime >= _nextDip)
        {
            _nextDip = Time.unscaledTime + 0.2f;
            DipInPools();
        }

        if (Time.unscaledTime >= _nextParticles)
        {
            _nextParticles = Time.unscaledTime + 0.1f;
            CatchDrops();
        }

        Droplets.Update();
    }

    // ---------------- finding the ink ----------------

    private static void Scan()
    {
        Decals.Clear();
        Pools.Clear();
        Particles.Clear();
        Bursts.Clear();
        var player = GameManager.Instance?.Player;
        var t0 = Time.realtimeSinceStartup;

        foreach (var o in Object.FindObjectsOfType(Il2CppType.Of<Renderer>()))
        {
            var r = o.Cast<Renderer>();
            if (r == null || !r.enabled) continue;
            var name = r.name.ToLowerInvariant();
            var mat = r.sharedMaterial;
            var shader = mat?.shader?.name ?? "";

            if (r.GetIl2CppType().Name == "ParticleSystemRenderer")
            {
                if (shader.IndexOf("Particle Ink", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var ps = r.GetComponent<ParticleSystem>();
                if (ps == null) continue;
                if (player != null && r.transform.IsChildOf(player.transform)) Bursts.Add(ps);
                else Particles.Add(ps);
                continue;
            }

            if (shader.IndexOf("Main Ink", StringComparison.OrdinalIgnoreCase) >= 0 && r.bounds.size.y < 0.6f && r.bounds.size.x * r.bounds.size.z > 4f)
            {
                Pools.Add(new Pool { R = r });
                continue;
            }

            if (name.Contains("ink_splat") || name.Contains("inksplat") || name.Contains("ink_puddle") || name.Contains("inkpuddle") || name.Contains("ink_spill"))
            {
                var d = BuildDecal(r);
                if (d != null) Decals.Add(d);
            }
        }

        var meshed = 0;
        foreach (var d in Decals)
            if (d.Tris != null)
                meshed++;
        Plugin.L.LogInfo($"Ink in this level: {Decals.Count} splats ({meshed} with surface data), {Pools.Count} pools, {Particles.Count} drip/splash emitters, {Bursts.Count} player bursts ({(Time.realtimeSinceStartup - t0) * 1000f:F0} ms)");
    }

    private static Decal BuildDecal(Renderer r)
    {
        var mf = r.GetComponent<MeshFilter>();
        var mesh = mf?.sharedMesh;
        var d = new Decal { R = r, B = r.bounds, Centre = r.bounds.center, Radius = r.bounds.extents.magnitude };
        if (mesh != null && mesh.isReadable)
        {
            var verts = mesh.vertices;
            var tris = mesh.triangles;
            var m = r.transform.localToWorldMatrix;
            d.Tris = new Vector3[tris.Length];
            var sum = Vector3.zero;
            for (var i = 0; i < tris.Length; i++)
            {
                d.Tris[i] = m.MultiplyPoint3x4(verts[tris[i]]);
                sum += d.Tris[i];
            }

            if (tris.Length > 0) d.Centre = sum / tris.Length;
            var far = 0f;
            foreach (var v in d.Tris) far = Mathf.Max(far, (v - d.Centre).magnitude);
            d.Radius = far;
        }

        return d;
    }

    // ---------------- contact ----------------

    private static IEnumerable<(Hand hand, List<StainPart> parts)> HandsParts()
    {
        yield return (Hand.Left, Hands.StainParts(Hand.Left));
        yield return (Hand.Right, Hands.StainParts(Hand.Right));
    }

    private static readonly float[] LastHandContact = new float[2];

    private static void TouchDecals()
    {
        if (Decals.Count == 0) return;
        var scale = VRRig.ActiveScale;
        var margin = 0.012f * scale;
        foreach (var (hand, parts) in HandsParts())
        {
            if (parts == null) continue;
            var h = (int)hand;
            if (Time.unscaledTime - LastHandContact[h] < 0.2f) continue; // a held touch smears 5x a second, no more

            // Find the parts pressed into ink this tick (at most two get painted).
            var painted = 0;
            Decal touched = null;
            foreach (var p in parts)
            {
                if (painted >= 2) break;
                if (p.R == null || !p.R.gameObject.activeInHierarchy) continue;
                var pb = p.R.bounds;
                pb.Expand(margin * 2f);
                foreach (var d in Decals)
                {
                    if (d.R == null || !pb.Intersects(d.B)) continue;
                    if (!Touching(p, d, margin, out var point)) continue;
                    var fresh = Time.unscaledTime - d.LastHit > 1f;
                    InkStains.Splat(p, point, fresh ? 0.011f : 0.007f, fresh ? 0.95f : 0.6f);
                    if (fresh) Droplets.Spawn(point, 1 + UnityEngine.Random.Range(0, 2));
                    touched = d;
                    painted++;
                    break;
                }
            }

            if (touched == null) continue;
            if (Time.unscaledTime - touched.LastHit > 1f) XrInput.Haptic(hand, 0.25f, 0.04f);
            touched.LastHit = Time.unscaledTime;
            LastHandContact[h] = Time.unscaledTime;
        }
    }

    /// <summary>Is any vertex of the part against the decal's surface, within the inky middle of the splat?</summary>
    private static bool Touching(StainPart p, Decal d, float margin, out Vector3 point)
    {
        point = default;
        var m = p.T.localToWorldMatrix;
        var core = d.Radius * 0.62f;
        for (var i = 0; i < p.Verts.Length; i += 3)
        {
            var v = m.MultiplyPoint3x4(p.Verts[i]);
            if (!d.B.Contains(v) && d.B.SqrDistance(v) > margin * margin) continue;
            if ((v - d.Centre).magnitude > core + margin) continue;
            if (d.Tris == null)
            {
                // Baked decal (mesh not readable): it lies on the wall/floor under it, so the hand has to be
                // inside the splat's area and pressed against real geometry there.
                if (Physics.CheckSphere(v, margin, SurfaceMask(), QueryTriggerInteraction.Ignore))
                {
                    point = v;
                    return true;
                }

                continue;
            }

            for (var t = 0; t < d.Tris.Length; t += 3)
            {
                var c = ClosestOnTriangle(v, d.Tris[t], d.Tris[t + 1], d.Tris[t + 2]);
                if ((c - v).sqrMagnitude <= margin * margin)
                {
                    point = v;
                    return true;
                }
            }
        }

        return false;
    }

    private static void DipInPools()
    {
        if (Pools.Count == 0) return;
        foreach (var (hand, parts) in HandsParts())
        {
            if (parts == null) continue;
            var dipped = 0;
            foreach (var pool in Pools)
            {
                if (pool.R == null || !pool.R.enabled || !pool.R.gameObject.activeInHierarchy) continue;
                var b = pool.R.bounds;
                var surface = b.max.y;
                var deepest = b.min.y - 0.9f * VRRig.ActiveScale; // a hand can only reach so far into the ink
                foreach (var p in parts)
                {
                    if (p.R == null || !p.R.gameObject.activeInHierarchy) continue;
                    var pb = p.R.bounds;
                    if (pb.min.y > surface || pb.max.y < deepest || pb.max.x < b.min.x || pb.min.x > b.max.x || pb.max.z < b.min.z || pb.min.z > b.max.z) continue;
                    dipped += InkStains.Coat(p, surface, 0.85f);
                }
            }

            if (dipped > 0)
            {
                XrInput.Haptic(hand, 0.12f, 0.05f);
                if (UnityEngine.Random.value < 0.3f) Droplets.Spawn(Hands.PalmPosition(hand), 1);
            }
        }
    }

    private static void CatchDrops()
    {
        if (Particles.Count == 0) return;
        var reach = 0.05f * VRRig.ActiveScale;
        foreach (var ps in Particles)
        {
            if (ps == null || !ps.isPlaying) continue;
            var psr = ps.GetComponent<ParticleSystemRenderer>();
            if (psr == null) continue;
            var area = psr.bounds;
            area.Expand(reach * 2f);
            foreach (var (hand, parts) in HandsParts())
            {
                if (parts == null) continue;
                var handBounds = Hands.Bounds(hand);
                if (!area.Intersects(handBounds)) continue;

                var max = ps.main.maxParticles;
                if (_particleBuf == null || _particleBuf.Length < max) _particleBuf = new Il2CppStructArray<ParticleSystem.Particle>(Mathf.Max(max, 256));
                var n = ps.GetParticles(_particleBuf);
                var local = IsLocalSpace(ps, psr);
                for (var i = 0; i < n; i++)
                {
                    var pos = _particleBuf[i].m_Position;
                    if (local) pos = ps.transform.TransformPoint(pos);
                    if (!handBounds.Contains(pos) && handBounds.SqrDistance(pos) > reach * reach) continue;
                    var hit = NearestPart(parts, pos, reach * 1.5f);
                    if (hit == null || Time.unscaledTime - hit.LastSplat < 0.12f) continue;
                    InkStains.Splat(hit, pos, 0.007f, 0.9f);
                    XrInput.Haptic(hand, 0.15f, 0.02f);
                }
            }
        }
    }

    /// <summary>Particles simulated in the emitter's local space need moving into the world (the setting isn't readable in this build: infer it).</summary>
    private static bool IsLocalSpace(ParticleSystem ps, ParticleSystemRenderer psr)
    {
        try
        {
#pragma warning disable CS0618
            return ps.simulationSpace == ParticleSystemSimulationSpace.Local;
#pragma warning restore CS0618
        }
        catch
        {
            return !psr.bounds.Contains(ps.transform.position);
        }
    }

    /// <summary>The game's ink burst at the player: splatter over both hands and arms.</summary>
    private static void WatchBursts()
    {
        foreach (var ps in Bursts)
        {
            if (ps == null) continue;
            var count = ps.particleCount;
            BurstCounts.TryGetValue(ps.Pointer, out var before);
            BurstCounts[ps.Pointer] = count;
            if (count > before + 4 && before == 0) Splatter(14);
        }
    }

    /// <summary>A splash over both arms (also used by the dev "inkburst" test).</summary>
    public static void Splatter(int perHand)
    {
        foreach (var (hand, parts) in HandsParts())
        {
            if (parts == null || parts.Count == 0) continue;
            // Bigger surfaces (forearm, sleeves, palm) catch more of the splash: pick by vertex, not by part.
            var total = 0;
            foreach (var part in parts) total += part.Verts.Length;
            for (var i = 0; i < perHand; i++)
            {
                var pick = UnityEngine.Random.Range(0, total);
                StainPart p = null;
                foreach (var part in parts)
                {
                    if (pick < part.Verts.Length)
                    {
                        p = part;
                        break;
                    }

                    pick -= part.Verts.Length;
                }

                if (p?.R == null || !p.R.gameObject.activeInHierarchy) continue;

                // The burst comes at you: land on a side of the hand/arm that faces your eyes.
                var head = VRRig.Head != null ? VRRig.Head.position : p.T.position + Vector3.up;
                var world = p.T.position;
                for (var tries = 0; tries < 6; tries++)
                {
                    var k = UnityEngine.Random.Range(0, p.Verts.Length);
                    world = p.T.TransformPoint(p.Verts[k]);
                    if (Vector3.Dot(p.T.TransformDirection(p.Normals[k]), head - world) > 0) break;
                }

                InkStains.Splat(p, world, UnityEngine.Random.Range(0.007f, 0.017f), UnityEngine.Random.Range(0.75f, 1f));
            }

            XrInput.Haptic(hand, 0.6f, 0.15f);
            Droplets.Spawn(Hands.PalmPosition(hand), 3);
        }
    }

    private static int _surfaceMask = -1;
    private static int _maskPlayerLayer = -1;

    /// <summary>Everything solid except the player's own capsule and InkVR's layer.</summary>
    private static int SurfaceMask()
    {
        var player = GameManager.Instance?.Player;
        var layer = player != null ? player.gameObject.layer : 0;
        if (_surfaceMask == -1 || layer != _maskPlayerLayer)
        {
            _maskPlayerLayer = layer;
            _surfaceMask = ~((1 << layer) | (1 << UI.VRLayers.UI));
        }

        return _surfaceMask;
    }

    private static StainPart NearestPart(List<StainPart> parts, Vector3 world, float within)
    {
        StainPart best = null;
        var bestD = within * within;
        foreach (var p in parts)
        {
            if (p.R == null || !p.R.gameObject.activeInHierarchy) continue;
            var d = p.R.bounds.SqrDistance(world);
            if (d <= bestD)
            {
                bestD = d;
                best = p;
            }
        }

        return best;
    }

    private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        var bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        var cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));
        var denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }
}

/// <summary>A few drops of ink that fall off a hand after it gets inky.</summary>
public static class Droplets
{
    private sealed class Drop
    {
        public Transform T;
        public Vector3 Velocity;
        public float Until, Size;
    }

    private static readonly List<Drop> Pool = new();
    private static Mesh _mesh;
    private static Material _material;

    public static void Spawn(Vector3 at, int count)
    {
        if (!ModConfig.InkOnHands.Value) return;
        var scale = VRRig.ActiveScale;
        for (var i = 0; i < count; i++)
        {
            var d = Take();
            if (d == null) return;
            d.T.position = at + UnityEngine.Random.insideUnitSphere * 0.01f * scale;
            d.Size = scale * UnityEngine.Random.Range(0.7f, 1.3f);
            d.T.localScale = Vector3.one * d.Size;
            d.Velocity = new Vector3(UnityEngine.Random.Range(-0.1f, 0.1f), UnityEngine.Random.Range(-0.2f, 0.05f), UnityEngine.Random.Range(-0.1f, 0.1f)) * scale;
            d.Until = Time.unscaledTime + UnityEngine.Random.Range(0.25f, 0.6f) + 1.2f;
            d.T.gameObject.SetActive(true);
        }
    }

    public static void Update()
    {
        var g = 9.81f * VRRig.ActiveScale;
        var dt = Time.unscaledDeltaTime;
        foreach (var d in Pool)
        {
            if (d.T == null || !d.T.gameObject.activeSelf) continue;
            if (Time.unscaledTime > d.Until)
            {
                d.T.gameObject.SetActive(false);
                continue;
            }

            d.Velocity += Vector3.down * g * dt;
            d.T.position += d.Velocity * dt;
            // Falling drops stretch into a teardrop along their path.
            var stretch = Mathf.Clamp(1f + d.Velocity.magnitude / VRRig.ActiveScale * 0.6f, 1f, 2.2f);
            d.T.rotation = Quaternion.LookRotation(d.Velocity.sqrMagnitude > 1e-6f ? d.Velocity : Vector3.down);
            d.T.localScale = new Vector3(d.Size, d.Size, d.Size * stretch);
        }
    }

    private static Drop Take()
    {
        foreach (var d in Pool)
            if (d.T != null && !d.T.gameObject.activeSelf)
                return d;
        if (Pool.Count >= 24) return null;

        if (_mesh == null)
        {
            var mb = new MeshBuilder();
            mb.Ellipsoid(Vector3.zero, new Vector3(0.0045f, 0.0045f, 0.006f), Quaternion.identity, 8, 6);
            _mesh = mb.ToMesh("InkVR_Drop");
            _material = InkMaterials.GameSurface(HenryTextures.Flat(HenryTextures.Ink, "InkVR_DropInk"), "InkVR_Drop");
        }

        var go = new GameObject("InkVR_Drop");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = _material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.SetActive(false);
        var drop = new Drop { T = go.transform };
        Pool.Add(drop);
        return drop;
    }
}
