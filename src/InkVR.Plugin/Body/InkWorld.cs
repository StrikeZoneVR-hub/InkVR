using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InkVR.Core;
using InkVR.Input;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace InkVR.Body;

/// <summary>
/// The ink in each level, and how it gets onto Henry, in real time:
///   ink pools and ink floors   → whatever goes under the surface is coated, every frame (the line follows your
///                                hand as it goes in); wading coats shoes and trousers; head under = soaked
///   the game's drips and rain   → each drop is checked against Henry as it falls; one that meets him stops there
///                                and leaves a blot, with a little splash
///   gushing pipes              → whatever is in the stream is coated, from the side it pours onto
///   ink splats on floors/walls  → a hand pressed into one picks it up; a foot in one inks the sole
///   the game's ink bursts       → a spray of drops at Henry that lands where it lands
///   a sharp flick              → shakes the wet ink off (it flies, and splats on the world)
/// Inky soles leave footprints (<see cref="Footprints"/>).
/// </summary>
public static class InkWorld
{
    /// <summary>A projected decal splat: its box (local unit cube, projecting along local Z).</summary>
    private sealed class BoxSplat
    {
        public Renderer R;
        public Bounds B;
        public M34 ToLocal;
        public bool Floor;
    }

    private sealed class Pool
    {
        public Renderer R;
        public Bounds B;
        public float Top;
    }

    /// <summary>Ink running down a wall: a thin vertical quad.</summary>
    private sealed class Wall
    {
        public Renderer R;
        public Bounds B;
        public M34 ToLocal;
        public float DepthScale; // world units per local unit across the quad
    }

    private static readonly List<BoxSplat> Splats = new();
    private static readonly List<Pool> Pools = new();
    private static readonly List<Wall> Walls = new();
    private static readonly List<Renderer> Spouts = new();
    private static readonly List<ParticleSystem> Drippers = new();

    /// <summary>Each emitter's renderer and simulation space, looked up once.</summary>
    private static readonly Dictionary<IntPtr, (Renderer r, bool local)> EmitterInfo = new();
    private static readonly List<ParticleSystem> Puddlers = new();
    private static readonly List<ParticleSystem> Bursts = new();
    private static readonly Dictionary<IntPtr, int> BurstCounts = new();
    private static readonly List<(Vector3 pos, float r)> PuddleSpots = new();
    private static Il2CppStructArray<ParticleSystem.Particle> _buf;
    private static string _scene;
    private static float _nextScan, _next30, _next10, _nextPuddles;
    private static int _scans;

    public static int DecalCount => Splats.Count;
    public static int PoolCount => Pools.Count;
    public static int ParticleCount => Drippers.Count;
    public static int SpoutCount => Spouts.Count;
    public static int WallCount => Walls.Count;
    public static int PuddleCount => PuddleSpots.Count;

    /// <summary>Dev: the ink surface under your feet (world y), why, and fake ink to test with.</summary>
    public static float? WadeSurface { get; private set; }

    public static string WadeWhy = "";
    public static float? DevWadeDepth;
    public static (Bounds box, float top)? DevPool;
    public static float DevShake, DevDripUntil, DevDripRate;
    private static float _devDripAcc;
    public static int DripHits, DropHits;
    public static string LastDripHit = "";

    private static float BodyK => VRRig.ActiveScale * Mathf.Clamp(VRRig.StandingEyeHeight / HenryBody.EyeHeight, 0.75f, 1.35f);

    /// <summary>Dev: the whole ink system paused (to compare frame times).</summary>
    public static bool DevOff, DevStainsOff;

    public static void Update()
    {
        if (DevOff) return;
        var dt = Mathf.Min(InkClock.Dt, 0.05f);
        InkStains.DropFalls ??= (at, size, part) =>
        {
            var s = VRRig.ActiveScale;
            InkDrops.Spawn(at + Vector3.down * (0.002f * s), Vector3.down * (0.15f * s), Mathf.Lerp(0.0018f, 0.0035f, size) * s, InkBody.GroupOf(part));
        };

        if (!ModConfig.InkOnHands.Value || !XrManager.IsRunning || !VRRig.InGame || VRRig.CinemaActive)
        {
            InkPool.Hide();
            InkDrops.Update(dt);
            WorldInk.Update();
            return;
        }

        var scene = SceneManager.GetActiveScene().name;
        if (scene != _scene)
        {
            _scene = scene;
            _scans = 0;
            _nextScan = InkClock.Now + 2f;
            ForgetLevel();
            Footprints.Clear();
            WorldInk.Clear();
            WorldInk.Prewarm();
            InkDrops.Clear();
        }

        if (InkClock.Now >= _nextScan && _pending == null)
        {
            BeginScan();
            _scans++;
            // the pool's splash sounds are looked up while the chapter is still fading in, not at the first splash
            if (_scans == 1) InkPool.PrefetchSounds();
            _nextScan = InkClock.Now + (_scans < 4 ? 4f : 10f);
        }

        ContinueScan(300);

        Prof.Start();
        InkBody.Refresh();
        DevDrip(dt);
        Prof.Lap(0);
        Drips(dt);
        Prof.Lap(1);
        Shake(dt);
        Prof.Lap(2);
        InkPool.Update(dt);
        Prof.Lap(12);
        Dipping();
        Prof.Lap(3);

        var now = InkClock.Now;
        if (now >= _next30)
        {
            var dt30 = Mathf.Clamp(now - _next30 + 1f / 30f, 1f / 90f, 0.1f);
            _next30 = now + 1f / 30f;
            Pours(dt30);
            Touch();
            WatchBursts();
            Prof.Lap(4);
        }

        if (now >= _next10)
        {
            _next10 = now + 0.1f;
            Feet();
            Prof.Lap(5);
        }

        InkDrops.Update(dt);
        Prof.Lap(6);
        WorldInk.Update();
        Prof.Lap(7);
        Prof.Report();
    }

    /// <summary>Dev: where the ink's time goes (logged every few seconds while anything costs over 0.5 ms a frame).</summary>
    public static class Prof
    {
        private static readonly System.Diagnostics.Stopwatch Sw = new();
        public static readonly double[] Ms = new double[13];
        private static readonly string[] Names = { "body", "drips", "shake", "dip", "pours/touch", "feet", "drops", "splats", "runs", "dry", "upload", "deposit", "pool" };
        private static int _frames;
        private static float _next;

        public static void Start() => Sw.Restart();

        public static void Lap(int i)
        {
            Add(i, Sw.Elapsed.TotalMilliseconds);
            Sw.Restart();
        }

        public static void Add(int i, double ms)
        {
            Ms[i] += ms;
            if (ms > 15) Plugin.L.LogWarning($"INKHITCH {Names[i]} {ms:F1} ms");
        }

        public static void Report()
        {
            _frames++;
            if (InkClock.Now < _next) return;
            _next = InkClock.Now + 3f;
            var total = 0.0;
            foreach (var m in Ms) total += m;
            if (total / _frames > 0.5)
            {
                var sb = new System.Text.StringBuilder($"INKPROF {total / _frames:F2} ms/frame:");
                for (var i = 0; i < Ms.Length; i++)
                    if (Ms[i] / _frames > 0.02)
                        sb.Append($" {Names[i]}={Ms[i] / _frames:F2}");
                Plugin.L.LogInfo(sb.ToString());
            }

            System.Array.Clear(Ms, 0, Ms.Length);
            _frames = 0;
        }
    }

    // =====================================================================================================
    // Finding the ink
    // =====================================================================================================

    // The level is read a few hundred renderers a frame, and each renderer only once: sections of a level switch
    // on as you walk, so it's re-read every ten seconds, without ever stalling a frame.
    private static Il2CppReferenceArray<Object> _pending;
    private static int _pendingAt;
    private static float _scanT0, _findMs;
    private static readonly HashSet<IntPtr> Known = new();
    private static string _lastSummary = "";

    private static void ForgetLevel()
    {
        Splats.Clear();
        Pools.Clear();
        Walls.Clear();
        Spouts.Clear();
        Drippers.Clear();
        EmitterInfo.Clear();
        Puddlers.Clear();
        Bursts.Clear();
        Known.Clear();
        _pending = null;
        InkPool.Hide();
        InkPool.ForgetSounds();
    }

    /// <summary>The ink pool you're in or standing over (the highest surface below your eyes).</summary>
    internal static bool FindPool(Vector3 head, out Renderer renderer, out float top)
    {
        renderer = null;
        top = float.MinValue;
        var k = BodyK;
        foreach (var pool in Pools)
        {
            if (pool.R == null || !pool.R.enabled || !pool.R.gameObject.activeInHierarchy) continue;
            // the game raises and drains its ink: keep up with where it is now
            var b = pool.B = pool.R.bounds;
            pool.Top = b.max.y;
            if (head.x < b.min.x || head.x > b.max.x || head.z < b.min.z || head.z > b.max.z) continue;
            if (pool.Top > head.y - 0.15f * k || pool.Top < head.y - 2.6f * k || pool.Top <= top) continue;
            renderer = pool.R;
            top = pool.Top;
        }

        return renderer != null;
    }

    private static void BeginScan()
    {
        if (_pending != null) return;
        _scanT0 = Time.realtimeSinceStartup;
        _pending = Object.FindObjectsOfType(Il2CppType.Of<Renderer>());
        _findMs = (Time.realtimeSinceStartup - _scanT0) * 1000f;
        _pendingAt = 0;
    }

    private static void ContinueScan(int budget)
    {
        if (_pending == null) return;
        var player = GameManager.Instance?.Player;
        var end = Math.Min(_pending.Length, _pendingAt + budget);
        for (var i = _pendingAt; i < end; i++)
        {
            var o = _pending[i];
            if (o == null) continue;
            Classify(o.Cast<Renderer>(), player);
        }

        _pendingAt = end;
        if (end < _pending.Length) return;
        _pending = null;

        // Pipes that gush when the story turns them on: their spout is hidden until then.
        foreach (var o in Object.FindObjectsOfType(Il2CppType.Of<InkPipeController>()))
        foreach (var r in o.Cast<Component>().GetComponentsInChildren<Renderer>(true))
            if (r.name.IndexOf("spout", StringComparison.OrdinalIgnoreCase) >= 0 && !Spouts.Contains(r))
                Spouts.Add(r);

        Splats.RemoveAll(x => x.R == null);
        Pools.RemoveAll(x => x.R == null);
        Walls.RemoveAll(x => x.R == null);
        Spouts.RemoveAll(x => x == null);
        Drippers.RemoveAll(x => x == null);
        Puddlers.RemoveAll(x => x == null);
        Bursts.RemoveAll(x => x == null);
        var summary = $"{Splats.Count} splats, {Pools.Count} pools/ink floors, {Walls.Count} inky walls, {Spouts.Count} spouts, {Drippers.Count} drip/rain/splash emitters, {Puddlers.Count} puddle emitters, {Bursts.Count} player bursts";
        if (summary != _lastSummary)
            Plugin.L.LogInfo($"Ink in this level: {summary} (find {_findMs:F1} ms, read over {(Time.realtimeSinceStartup - _scanT0) * 1000f:F0} ms)");
        _lastSummary = summary;
    }

    private static void Classify(Renderer r, PlayerController player)
    {
        if (r == null || !r.enabled) return; // switched off: looked at again next time
        if (!Known.Add(r.Pointer)) return;
        var name = r.name.ToLowerInvariant();
        if (name.StartsWith("inkvr")) return;
        var mat = r.sharedMaterial;
        var shader = mat?.shader?.name ?? "";
        var onPlayer = player != null && r.transform.IsChildOf(player.transform);

        if (r.GetIl2CppType().Name == "ParticleSystemRenderer")
        {
            if (shader.IndexOf("Ink", StringComparison.OrdinalIgnoreCase) < 0 || shader.IndexOf("Secret", StringComparison.OrdinalIgnoreCase) >= 0) return;
            var ps = r.GetComponent<ParticleSystem>();
            if (ps == null) return;
            if (onPlayer) Bursts.Add(ps);
            else if (name.Contains("puddle")) Puddlers.Add(ps);
            else
            {
                Drippers.Add(ps);
                EmitterInfo[ps.Pointer] = (r, IsLocalSpace(ps, r.Cast<ParticleSystemRenderer>()));
            }
            return;
        }

        if (onPlayer) return;
        if (name.Contains("ink_splat") || name.Contains("inksplat") || name.Contains("ink_puddle") || name.Contains("inkpuddle") || name.Contains("ink_spill"))
        {
            var tr = r.transform;
            Splats.Add(new BoxSplat { R = r, B = r.bounds, ToLocal = M34.Of(tr.worldToLocalMatrix), Floor = Mathf.Abs(tr.forward.y) > 0.5f });
            return;
        }

        if (shader.IndexOf("Main Ink", StringComparison.OrdinalIgnoreCase) < 0) return;
        var b = r.bounds;
        var size = b.size;
        if (name.Contains("spout") || name.Contains("gush") || name.Contains("pour") || name.Contains("stream"))
        {
            if (!Spouts.Contains(r)) Spouts.Add(r);
            return;
        }

        if (size.y < 0.6f && size.x * size.z > 1f)
        {
            Pools.Add(new Pool { R = r, B = b, Top = b.max.y });
            return;
        }

        if (Mathf.Min(size.x, size.z) < 0.08f && size.y > 0.4f && Mathf.Max(size.x, size.z) > 0.4f)
            Walls.Add(new Wall { R = r, B = b, ToLocal = M34.Of(r.transform.worldToLocalMatrix), DepthScale = r.transform.lossyScale.z });
    }

    /// <summary>Dev: the n-th nearest drip emitter that's dripping right now.</summary>
    public static ParticleSystem NearestDripping(Vector3 from, int skip, string named = null)
    {
        var list = new List<(float d, ParticleSystem ps)>();
        foreach (var ps in Drippers)
        {
            if (ps == null) continue;
            var n = ps.name.ToLowerInvariant();
            if (named != null)
            {
                if (!n.Contains(named)) continue;
            }
            else if (!ps.isPlaying || ps.particleCount == 0 || (!n.Contains("drip") && !n.Contains("drop") && !n.Contains("rain"))) continue;
            list.Add(((ps.transform.position - from).sqrMagnitude, ps));
        }

        list.Sort((a, b) => a.d.CompareTo(b.d));
        return skip < list.Count ? list[skip].ps : null;
    }

    private static bool InSplat(BoxSplat s, F3 p, float margin)
    {
        var l = s.ToLocal.Point(p);
        if (Math.Abs(l.Z) > 0.5f + margin) return false;
        // The inky middle of the splat (its edges are ragged and thin).
        return l.X * l.X + l.Y * l.Y < 0.36f * 0.36f;
    }

    private static int SurfaceMask() => InkDrops.Mask();

    // =====================================================================================================
    // Dipping: pools and ink floors, every 30th of a second
    // =====================================================================================================

    private static readonly float[] NextBuzz = new float[2];

    // Dipping is the costliest thing the ink does (every submerged piece, every texel under the line), so it's
    // spread over frames: the left hand, the right hand, and two halves of the body take turns.
    private static int _dipPhase;
    private static readonly float[] DipLast = new float[4];

    private static void Dipping()
    {
        var scale = VRRig.ActiveScale;
        var now = InkClock.Now;
        var phase = _dipPhase = (_dipPhase + 1) % 4;
        var dt = Mathf.Clamp(now - DipLast[phase], 1f / 240f, 0.15f);
        DipLast[phase] = now;
        var rate = 7f * dt; // a submerged surface is fully inked in about a seventh of a second
        var edge = 0.012f * scale;

        void Dip(StainPart p, int group)
        {
            if (!p.Visible || !p.Ready) return;
            var pb = p.R.bounds;
            foreach (var pool in Pools)
            {
                if (pool.R == null || !pool.R.enabled || !pool.R.gameObject.activeInHierarchy) continue;
                if (Under(pb, pool.B, pool.Top)) Coat(p, pool.Top, group);
            }

            if (DevPool.HasValue && Under(pb, DevPool.Value.box, DevPool.Value.top)) Coat(p, DevPool.Value.top, group);
        }

        void Coat(StainPart p, float top, int group)
        {
            InkStains.DepositBelow(p, top, edge, rate);
            p.InInkUntil = now + 0.25f;
            if (group is 1 or 2)
            {
                var hand = group == 1 ? Hand.Left : Hand.Right;
                var h = (int)hand;
                if (now < NextBuzz[h]) return;
                NextBuzz[h] = now + 0.1f;
                // Thick ink drags on the hand: the faster you move through it, the more you feel it.
                var speed = WeaponHandling.Velocity(hand).magnitude / scale;
                XrInput.Haptic(hand, Mathf.Clamp(0.04f + speed * 0.12f, 0.04f, 0.35f), 0.1f);
            }
        }

        switch (phase)
        {
            case 0:
                var l = Hands.StainParts(Hand.Left);
                if (l != null) foreach (var p in l) Dip(p, 1);
                break;
            case 1:
                var r = Hands.StainParts(Hand.Right);
                if (r != null) foreach (var p in r) Dip(p, 2);
                break;
            default:
                var body = Hands.Body?.Stains;
                if (body != null)
                    for (var i = phase - 2; i < body.Count; i += 2)
                        Dip(body[i], 3);
                break;
        }
    }

    private static bool Under(Bounds part, Bounds pool, float top) =>
        part.min.y < top && part.max.x > pool.min.x && part.min.x < pool.max.x && part.max.z > pool.min.z && part.min.z < pool.max.z
        && part.max.y > top - 3f * VRRig.ActiveScale; // not a pool on a floor far below

    // =====================================================================================================
    // The game's drips, rain and splashes: every drop, every frame
    // =====================================================================================================

    private static void Drips(float dt)
    {
        if (!InkBody.Any || Drippers.Count == 0) return;
        var scale = VRRig.ActiveScale;
        var near3 = InkBody.Box.Expanded(0.2f * scale);
        var near = InkBody.Bounds;
        near.Expand(0.4f * scale);
        // drips and rain falling into the ink pool around Henry ring its surface
        var poolOn = InkPool.Active;
        var region = poolOn ? InkPool.Region : default;
        foreach (var ps in Drippers)
        {
            if (ps == null || !EmitterInfo.TryGetValue(ps.Pointer, out var info) || info.r == null) continue;
            if (ps.particleCount == 0) continue;
            var eb = info.r.bounds;
            if (!eb.Intersects(near) && !(poolOn && Box3.Of(eb).Intersects(region))) continue;
            var max = ps.main.maxParticles;
            if (_buf == null || _buf.Length < max) _buf = new Il2CppStructArray<ParticleSystem.Particle>(Mathf.Max(max, 256));
            var n = ps.GetParticles(_buf);
            var local = info.local;
            var m = M34.Of(ps.transform.localToWorldMatrix);
            var changed = false;
            for (var i = 0; i < n; i++)
            {
                var pt = _buf[i];
                var pos = F3.Of(pt.m_Position);
                if (local) pos = m.Point(pos);
                var inNear = near3.Contains(pos);
                if (!inNear && !(poolOn && region.Contains(pos))) continue;
                var vel = F3.Of(pt.m_Velocity) + F3.Of(pt.m_AnimatedVelocity);
                if (local) vel = m.Vec(vel);
                var radius = FM.Clamp(pt.m_StartSize.x * 0.25f, 0.0015f * scale, 0.02f * scale);
                if (poolOn) InkPool.GameDrop(pos, pos + vel * dt, radius);
                if (!inNear) continue;
                var from = pos - vel * (dt * 1.3f);
                if (!InkBody.Hit(from, pos, radius, 0, out var cap, out var contact, out var normal)) continue;
                InkDrops.HitBody(cap, contact, normal, vel, radius);
                LastDripHit = $"{ps.name} on {cap.P.T.name} r={radius / scale * 1000f:F1} mm, {InkStains.LastDepositTexels} texels, ready={cap.P.Ready}";
                pt.m_Lifetime = -1f; // it stopped on Henry: it doesn't fall on through him
                _buf[i] = pt;
                changed = true;
                DripHits++;
            }

            if (changed) ps.SetParticles(_buf, n);
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

    /// <summary>Dev: ink dripping onto the right hand from 40 cm above it (tests drops meeting Henry).</summary>
    private static void DevDrip(float dt)
    {
        if (InkClock.Now > DevDripUntil) return;
        var k = BodyK;
        var wrist = Hands.WristOf(Hand.Right);
        if (wrist == null) return;
        var at = wrist.position + wrist.forward * (0.07f * k) + Vector3.up * (0.4f * k); // above the palm
        _devDripAcc += DevDripRate * dt;
        while (_devDripAcc >= 1f)
        {
            _devDripAcc -= 1f;
            InkDrops.Spawn(at + UnityEngine.Random.insideUnitSphere * (0.03f * k), Vector3.down * (1f * k), UnityEngine.Random.Range(0.002f, 0.004f) * k);
        }
    }

    // =====================================================================================================
    // Gushing pipes
    // =====================================================================================================

    private static void Pours(float dt)
    {
        if (Spouts.Count == 0) return;
        var scale = VRRig.ActiveScale;
        foreach (var spout in Spouts)
        {
            if (spout == null || !spout.enabled || !spout.gameObject.activeInHierarchy) continue;
            var b = spout.bounds;
            // The visible stream is a little narrower than its box.
            b.extents = new Vector3(b.extents.x * 0.8f, b.extents.y, b.extents.z * 0.8f);
            foreach (var cap in InkBody.Caps)
            {
                if (!cap.P.R.bounds.Intersects(b)) continue;
                InkStains.DepositInside(cap.P, b, 5f * dt, Vector3.down);
                cap.P.InInkUntil = InkClock.Now + 0.2f;
                if (cap.Group is 1 or 2) XrInput.Haptic(cap.Group == 1 ? Hand.Left : Hand.Right, 0.25f, 0.04f);
                // Ink splashing off whatever is in the way.
                if (UnityEngine.Random.value < 0.4f)
                {
                    var top = new F3(FM.Clamp(cap.A.X, b.min.x, b.max.x), FM.Max(cap.A.Y, cap.B.Y) + cap.R, FM.Clamp(cap.A.Z, b.min.z, b.max.z));
                    InkDrops.Splash(top, F3.Up, F3.Down * (4f * scale), 0.004f * scale, cap.Group);
                }
            }
        }
    }

    // =====================================================================================================
    // Hands on ink splats and inky walls
    // =====================================================================================================

    private static void Touch()
    {
        var scale = VRRig.ActiveScale;
        var mask = SurfaceMask();
        foreach (var (hand, group) in new[] { (Hand.Left, 1), (Hand.Right, 2) })
        {
            var parts = Hands.StainParts(hand);
            if (parts == null) continue;
            var hb = Hands.Bounds(hand);
            hb.Expand(0.03f * scale);
            var touched = false;
            foreach (var s in Splats)
            {
                if (s.R == null || !hb.Intersects(s.B)) continue;
                touched |= Press(parts, v => InSplat(s, v, 0.02f) && Physics.CheckSphere(v.V, 0.012f * scale, mask, QueryTriggerInteraction.Ignore), scale);
            }

            foreach (var w in Walls)
            {
                if (w.R == null || !w.R.gameObject.activeInHierarchy || !hb.Intersects(w.B)) continue;
                touched |= Press(parts, v =>
                {
                    var l = w.ToLocal.Point(v);
                    if (Math.Abs(l.X) > 0.5f || Math.Abs(l.Y) > 0.5f) return false;
                    return Math.Abs(l.Z * w.DepthScale) < 0.015f * scale;
                }, scale);
            }

            if (touched && InkClock.Now >= NextBuzz[(int)hand])
            {
                NextBuzz[(int)hand] = InkClock.Now + 0.15f;
                XrInput.Haptic(hand, 0.18f, 0.03f);
            }
        }
    }

    /// <summary>Where the hand's surface is pressed into ink, ink comes off on it (a few contact points at most).</summary>
    private static bool Press(List<StainPart> parts, Func<F3, bool> inInk, float scale)
    {
        var any = false;
        foreach (var p in parts)
        {
            if (!p.Visible || !p.Ready) continue;
            var wv = p.World();
            var hits = 0;
            for (var i = 0; i < wv.Length && hits < 3; i += 3)
            {
                if (!inInk(wv[i])) continue;
                InkStains.Deposit(p, wv[i], 0.011f * scale, 0.55f, default);
                hits++;
                i += 24; // spread the contacts out
            }

            any |= hits > 0;
        }

        return any;
    }

    // =====================================================================================================
    // Feet: wading, puddles, splats on the floor; footprints
    // =====================================================================================================

    private static void Feet()
    {
        var player = GameManager.Instance?.Player;
        var headT = VRRig.Head;
        if (player == null || headT == null) return;
        var k = BodyK;
        var head = headT.position;
        var body = Hands.Body;
        var now = InkClock.Now;
        if (now >= _nextPuddles)
        {
            _nextPuddles = now + 0.5f;
            ReadPuddles(head);
        }

        // Head under the ink: everything is soaked.
        foreach (var pool in Pools)
        {
            if (pool.R == null || !pool.R.gameObject.activeInHierarchy) continue;
            var b = pool.B;
            if (head.x > b.min.x && head.x < b.max.x && head.z > b.min.z && head.z < b.max.z && pool.Top > head.y && pool.Top - head.y < 0.5f * k)
                Soak("went under the ink");
        }

        var game = player.CurrentFootstepType;
        float? lowest = null;
        WadeWhy = "";
        for (var f = 0; f < 2; f++)
        {
            var side = f == 0 ? -1f : 1f;
            Vector3 foot;
            if (body != null) foot = f == 0 ? body.FootLeft : body.FootRight;
            else
            {
                var fwd = Vector3.ProjectOnPlane(headT.forward, Vector3.up).normalized;
                var right = Vector3.Cross(Vector3.up, fwd);
                foot = head + right * (0.1f * side * k);
                foot.y = Hands.GroundAt(foot);
            }

            var ground = body != null ? foot.y : Hands.GroundAt(foot);
            float? surface = null;
            var wet = 0f;
            var why = "";
            foreach (var pool in Pools)
            {
                if (pool.R == null || !pool.R.gameObject.activeInHierarchy) continue;
                var b = pool.B;
                if (foot.x < b.min.x || foot.x > b.max.x || foot.z < b.min.z || foot.z > b.max.z) continue;
                if (pool.Top < ground - 0.08f * k || pool.Top > ground + 1.2f * k) continue;
                surface = Mathf.Max(surface ?? float.MinValue, Mathf.Max(pool.Top, ground + 0.008f * k));
                wet = 1f;
                why = "pool";
            }

            if (DevPool.HasValue && DevPool.Value.box.Contains(new Vector3(foot.x, DevPool.Value.box.center.y, foot.z)))
            {
                surface = Mathf.Max(surface ?? float.MinValue, DevPool.Value.top);
                wet = 1f;
                why = "dev pool";
            }

            if (game == FootstepTypes.INK || game == FootstepTypes.INK_STAIRS || game == FootstepTypes.INK_DEEP)
            {
                surface = Mathf.Max(surface ?? float.MinValue, ground + (game == FootstepTypes.INK_DEEP ? 0.24f : 0.035f) * k);
                wet = 1f;
                why += " game ink";
            }

            var probe = foot + Vector3.up * (0.03f * k);
            foreach (var s in Splats)
            {
                if (s.R == null || !s.B.Contains(probe) && s.B.SqrDistance(probe) > 0.01f * k * k) continue;
                if (!InSplat(s, F3.Of(probe), 0.05f)) continue;
                surface = Mathf.Max(surface ?? float.MinValue, ground + 0.01f * k);
                wet = Mathf.Max(wet, 0.7f);
                why += " splat";
                break;
            }

            foreach (var (pos, r) in PuddleSpots)
            {
                if (new Vector2(pos.x - foot.x, pos.z - foot.z).sqrMagnitude > r * r || Mathf.Abs(pos.y - ground) > 0.3f * k) continue;
                surface = Mathf.Max(surface ?? float.MinValue, ground + 0.01f * k);
                wet = Mathf.Max(wet, 0.8f);
                why += " puddle";
                break;
            }

            var ours = WorldInk.WetAt(foot, 0.06f * k);
            if (ours > 0.25f)
            {
                surface = Mathf.Max(surface ?? float.MinValue, ground + 0.006f * k);
                wet = Mathf.Max(wet, ours * 0.6f);
                why += " fresh splat";
            }

            if (DevWadeDepth.HasValue)
            {
                surface = ground + DevWadeDepth.Value * k;
                wet = 1f;
                why = "dev";
            }

            Footprints.SetInInk(f, surface.HasValue);
            if (!surface.HasValue) continue;
            if (!lowest.HasValue || surface.Value > lowest.Value) lowest = surface;
            WadeWhy += $"{(f == 0 ? "L" : "R")}:{why.Trim()} ";
            Footprints.Wet(f, wet);
            if (body == null) continue;
            // That foot's shoe (and trouser leg, if it's deep) up to the ink line.
            foreach (var p in body.Stains)
                if (p.Visible && (p.Region == InkRegion.Feet || p.Region == InkRegion.Legs))
                {
                    InkStains.DepositBelow(p, surface.Value, 0.015f * k, 0.7f, foot, 0.22f * k);
                    p.InInkUntil = now + 0.3f;
                }
        }

        WadeSurface = lowest;
    }

    private static void ReadPuddles(Vector3 around)
    {
        PuddleSpots.Clear();
        var reach = 6f * VRRig.ActiveScale;
        foreach (var ps in Puddlers)
        {
            if (ps == null || ps.particleCount == 0) continue;
            var psr = ps.GetComponent<ParticleSystemRenderer>();
            if (psr == null || psr.bounds.SqrDistance(around) > reach * reach) continue;
            var max = ps.main.maxParticles;
            if (_buf == null || _buf.Length < max) _buf = new Il2CppStructArray<ParticleSystem.Particle>(Mathf.Max(max, 256));
            var n = ps.GetParticles(_buf);
            var local = IsLocalSpace(ps, psr);
            var m = M34.Of(ps.transform.localToWorldMatrix);
            var k = local ? m.ScaleX : 1f;
            for (var i = 0; i < n && PuddleSpots.Count < 200; i++)
            {
                var pt = _buf[i];
                var pos = F3.Of(pt.m_Position);
                if (local) pos = m.Point(pos);
                PuddleSpots.Add((pos.V, FM.Max(pt.m_StartSize.x * k * 0.5f, 0.05f * VRRig.ActiveScale)));
            }
        }
    }

    // =====================================================================================================
    // Bursts, soaking, shaking off
    // =====================================================================================================

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

    /// <summary>
    /// An ink burst at Henry (the game's own, or the dev "inkburst" test): a spray of drops from just in front of
    /// him, aimed at his front and his hands. Each lands where it lands.
    /// </summary>
    public static void Splatter(int strength)
    {
        var head = VRRig.Head;
        if (head == null) return;
        var k = BodyK;
        var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        var origin = head.position + fwd * (0.75f * k) - Vector3.up * (0.1f * k);
        var targets = new List<Vector3> { head.position - Vector3.up * (0.35f * k), head.position - Vector3.up * (0.7f * k) };
        foreach (var hand in new[] { Hand.Left, Hand.Right })
            if (Hands.StainParts(hand) != null)
                targets.Add(Hands.Bounds(hand).center);
        var count = strength * 6;
        for (var i = 0; i < count; i++)
        {
            var target = targets[UnityEngine.Random.Range(0, targets.Count)] + UnityEngine.Random.insideUnitSphere * (0.25f * k);
            var from = origin + UnityEngine.Random.insideUnitSphere * (0.15f * k);
            var speed = UnityEngine.Random.Range(4.5f, 7.5f) * k;
            InkDrops.Spawn(from, (target - from).normalized * speed, UnityEngine.Random.Range(0.002f, 0.007f) * k);
        }

        foreach (var hand in new[] { Hand.Left, Hand.Right }) XrInput.Haptic(hand, 0.6f, 0.15f);
    }

    private static float _lastSoak = -10;

    /// <summary>Soaked from head to toe (fallen in, or the dev "drench" test).</summary>
    public static void Soak(string why)
    {
        if (InkClock.Now - _lastSoak < 2f) return;
        _lastSoak = InkClock.Now;
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var parts = Hands.StainParts(hand);
            if (parts == null) continue;
            foreach (var p in parts) InkStains.Soak(p, 0.95f);
            XrInput.Haptic(hand, 0.7f, 0.3f);
        }

        var body = Hands.Body?.Stains;
        if (body != null)
            foreach (var p in body)
                InkStains.Soak(p, 0.95f);
        Footprints.Wet(0, 1f);
        Footprints.Wet(1, 1f);
        Plugin.L.LogInfo($"Henry is soaked in ink ({why})");
    }

    private static readonly Vector3[] PrevPos = new Vector3[2], PrevVel = new Vector3[2], PeakVel = new Vector3[2];
    private static readonly float[] Accel = new float[2], NextShake = new float[2], PeakAt = new float[2];
    private static readonly bool[] HavePrev = new bool[2];
    private static readonly List<Vector3> ShakePoints = new();

    /// <summary>
    /// A sharp flick shakes the wet ink off: the hand's acceleration (in your own space, so walking and turning
    /// don't count) has to spike. The ink flies on the way the hand was going.
    /// </summary>
    private static void Shake(float dt)
    {
        var now = InkClock.Now;
        var scale = VRRig.ActiveScale;
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var h = (int)hand;
            var c = XrInput.Get(hand);
            var parts = Hands.StainParts(hand);
            if (parts == null || (!c.PoseValid && DevShake <= 0)) continue;
            var pos = c.GripPosition;
            if (!HavePrev[h] || dt <= 0)
            {
                PrevPos[h] = pos;
                PrevVel[h] = Vector3.zero;
                HavePrev[h] = true;
                continue;
            }

            var v = (pos - PrevPos[h]) / dt;
            var a = (v - PrevVel[h]).magnitude / dt;
            Accel[h] = Mathf.Lerp(Accel[h], a, 0.5f);
            if (v.sqrMagnitude > PeakVel[h].sqrMagnitude || now - PeakAt[h] > 0.15f)
            {
                PeakVel[h] = v;
                PeakAt[h] = now;
            }

            PrevPos[h] = pos;
            PrevVel[h] = v;
            var strength = Accel[h];
            var fling = PeakVel[h];
            if (DevShake > 0 && hand == ControllerBindings.Dominant)
            {
                strength = DevShake;
                fling = new Vector3(0, -1.5f, 2.5f);
                DevShake = 0;
            }

            if (strength < 45f || now < NextShake[h]) continue;
            NextShake[h] = now + 0.12f;
            var fraction = Mathf.Clamp((strength - 45f) / 120f, 0.12f, 0.5f);
            ShakePoints.Clear();
            var removed = 0f;
            foreach (var p in parts)
                if (p.Visible && now >= p.InInkUntil)
                    removed += InkStains.ShakeOff(p, p.Region == InkRegion.Hand ? fraction : fraction * 0.4f, ShakePoints, 6);
            if (removed < 0.002f || ShakePoints.Count == 0) continue;

            var origin = VRRig.ActiveOrigin;
            var worldVel = origin != null ? origin.TransformVector(fling) : fling * scale;
            var drops = Mathf.Clamp((int)(removed * 12f), 3, 36);
            for (var i = 0; i < drops; i++)
            {
                var from = ShakePoints[i % ShakePoints.Count];
                var vel = worldVel * UnityEngine.Random.Range(0.75f, 1.15f) + UnityEngine.Random.insideUnitSphere * (0.6f * scale);
                InkDrops.Spawn(from, vel, UnityEngine.Random.Range(0.0015f, 0.0045f) * scale, h + 1);
            }
        }
    }
}
