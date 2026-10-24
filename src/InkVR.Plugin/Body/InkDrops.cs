using System.Collections.Generic;
using InkVR.Core;
using InkVR.Input;
using InkVR.Rendering;
using InkVR.UI;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InkVR.Body;

/// <summary>Henry's body this frame as capsules (hands, arms, and with the full body his clothes), for anything falling on him.</summary>
public static class InkBody
{
    public struct Cap
    {
        public StainPart P;
        public F3 A, B;
        public float R;
        public int Group; // 1 left hand/arm, 2 right, 3 body
    }

    public static readonly List<Cap> Caps = new();
    public static Box3 Box;
    public static bool Any;
    private static int _frame = -1;

    public static Bounds Bounds => new((Box.Min + Box.Max).V * 0.5f, (Box.Max - Box.Min).V);

    public static void Refresh()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;
        Caps.Clear();
        Any = false;
        Add(Hands.StainParts(Hand.Left), 1);
        Add(Hands.StainParts(Hand.Right), 2);
        Add(Hands.Body?.Stains, 3);
    }

    private static void Add(List<StainPart> parts, int group)
    {
        if (parts == null) return;
        foreach (var p in parts)
        {
            if (!p.Visible || !p.Ready) continue;
            foreach (var c in p.Caps)
            {
                InkStains.CapsuleWorld(p, c, out var a, out var b, out var r);
                Caps.Add(new Cap { P = p, A = a, B = b, R = r, Group = group });
                if (!Any) Box = Box3.Around(a, r);
                else Box.Encapsulate(a, r);
                Box.Encapsulate(b, r);
                Any = true;
            }
        }
    }

    public static int GroupOf(StainPart p)
    {
        var l = Hands.StainParts(Hand.Left);
        if (l != null && l.Contains(p)) return 1;
        var r = Hands.StainParts(Hand.Right);
        if (r != null && r.Contains(p)) return 2;
        return 3;
    }

    /// <summary>The first capsule a ball of radius <paramref name="r"/> moving a → b touches (skipping one group).</summary>
    public static bool Hit(F3 a, F3 b, float r, int skipGroup, out Cap cap, out F3 contact, out F3 normal)
    {
        cap = default;
        contact = normal = default;
        if (!Any) return false;
        var seg = Box3.Around(a, r);
        seg.Encapsulate(b, r);
        if (!seg.Intersects(Box)) return false;
        var best = float.MaxValue;
        var found = false;
        foreach (var c in Caps)
        {
            if (c.Group == skipGroup) continue;
            Closest(a, b, c.A, c.B, out var s, out _, out var onSeg, out var onAxis);
            var d = (onSeg - onAxis).Len;
            if (d > c.R + r || s >= best) continue;
            best = s;
            cap = c;
            normal = d > 1e-6f ? (onSeg - onAxis) / d : (a - b).Norm;
            contact = onAxis + normal * c.R;
            found = true;
        }

        return found;
    }

    /// <summary>Closest points between segments p1q1 and p2q2 (Ericson, Real-Time Collision Detection 5.1.9).</summary>
    public static void Closest(F3 p1, F3 q1, F3 p2, F3 q2, out float s, out float t, out F3 c1, out F3 c2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var a = F3.Dot(d1, d1);
        var e = F3.Dot(d2, d2);
        var f = F3.Dot(d2, r);
        if (a <= 1e-12f && e <= 1e-12f)
        {
            s = t = 0;
            c1 = p1;
            c2 = p2;
            return;
        }

        if (a <= 1e-12f)
        {
            s = 0;
            t = FM.Clamp01(f / e);
        }
        else
        {
            var c = F3.Dot(d1, r);
            if (e <= 1e-12f)
            {
                t = 0;
                s = FM.Clamp01(-c / a);
            }
            else
            {
                var b = F3.Dot(d1, d2);
                var denom = a * e - b * b;
                s = denom != 0 ? FM.Clamp01((b * f - c * e) / denom) : 0;
                t = (b * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = FM.Clamp01(-c / a);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = FM.Clamp01((b - c) / a);
                }
            }
        }

        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
    }
}

/// <summary>
/// Drops of ink in flight, simulated every frame: gravity, a little air drag, and collisions. A drop that meets
/// Henry leaves a blot where it hit (and a few smaller splashes bounce off); one that meets the world leaves a
/// splat there. Drops come from runs dripping off him, ink shaken off, ink bursts, splashes, and the game's own
/// drips once they've touched him.
/// </summary>
public static class InkDrops
{
    private struct Drop
    {
        public F3 P, V;
        public float R, Life;
        public int Frames, SkipGroup;
    }

    private const int Max = 600;
    private static readonly Drop[] D = new Drop[Max];
    private static int _n;
    private static Mesh _mesh;
    private static GameObject _go;
    private static Vector3[] _verts;
    private static int[] _tris;
    private static float _nextTick;
    private static readonly System.Random Rng = new(31337);

    public static int Alive => _n;

    private static F3 RandomInSphere()
    {
        while (true)
        {
            var v = new F3((float)Rng.NextDouble() * 2 - 1, (float)Rng.NextDouble() * 2 - 1, (float)Rng.NextDouble() * 2 - 1);
            if (v.Len2 <= 1f) return v;
        }
    }

    public static void Spawn(Vector3 at, Vector3 velocity, float radius, int skipGroup = 0) => Spawn(F3.Of(at), F3.Of(velocity), radius, skipGroup);

    /// <summary>A drop at <paramref name="at"/> moving at <paramref name="velocity"/> (world units); radius in world units.</summary>
    public static void Spawn(F3 at, F3 velocity, float radius, int skipGroup = 0)
    {
        if (!ModConfig.InkOnHands.Value || _n >= Max) return;
        D[_n++] = new Drop { P = at, V = velocity, R = radius, Life = 5f, SkipGroup = skipGroup };
    }

    /// <summary>A few smaller drops bouncing off where a drop hit.</summary>
    public static void Splash(F3 at, F3 normal, F3 velocity, float radius, int skipGroup)
    {
        var scale = VRRig.ActiveScale;
        var speed = velocity.Len / scale;
        if (speed < 2f || radius < 0.0015f * scale) return;
        // Under a heavy pour the air is already full of drops: splashes thin out rather than multiply.
        if (_n > Max * 3 / 4) return;
        var n = _n > Max / 2 ? 1 : Mathf.Clamp((int)(speed * 0.6f), 1, 4);
        var bounce = (velocity - normal * (2f * F3.Dot(velocity, normal))) * 0.22f;
        for (var i = 0; i < n; i++)
        {
            var v = bounce + (RandomInSphere() + normal * 0.6f) * (0.7f * scale);
            Spawn(at + normal * (radius * 1.2f), v, radius * (0.25f + (float)Rng.NextDouble() * 0.2f), skipGroup);
        }
    }

    public static void Clear() => _n = 0;

    public static void Update(float dt)
    {
        if (_n == 0)
        {
            if (_go != null && _go.activeSelf) _go.SetActive(false);
            return;
        }

        dt = FM.Min(dt, 0.05f);
        var scale = VRRig.ActiveScale;
        var g = new F3(0, -9.81f * scale * dt, 0);
        var drag = 1f - 0.15f * dt;
        var mask = Mask();
        InkBody.Refresh();
        for (var i = _n - 1; i >= 0; i--)
        {
            ref var d = ref D[i];
            d.Life -= dt;
            if (d.Life <= 0)
            {
                Remove(i);
                continue;
            }

            d.V = (d.V + g) * drag;
            var a = d.P;
            var b = a + d.V * dt;
            d.Frames++;
            var skip = d.Frames < 12 ? d.SkipGroup : 0;
            if (InkBody.Hit(a, b, d.R, skip, out var cap, out var contact, out var normal))
            {
                HitBody(cap, contact, normal, d.V, d.R);
                Remove(i);
                continue;
            }

            if (InkPool.Catch(a, b, d.R, d.V))
            {
                Remove(i);
                continue;
            }

            var move = b - a;
            var len = move.Len;
            if (len > 1e-6f && Physics.Raycast(a.V, (move / len).V, out var hit, len + d.R, mask, QueryTriggerInteraction.Ignore))
            {
                var hp = F3.Of(hit.point);
                var hn = F3.Of(hit.normal);
                WorldInk.Splat(hp, hn, d.R, d.V);
                if (d.R > 0.002f * scale) Splash(hp, hn, d.V * 0.6f, d.R * 0.7f, 0);
                Remove(i);
                continue;
            }

            d.P = b;
        }

        Render();
    }

    /// <summary>
    /// Each blot is a pass over the part's triangles, so under a heavy pour only the first few a frame are painted
    /// one by one; the rest of that frame's hits on each part are pooled into one bigger blot (FlushPooled).
    /// </summary>
    private const int BlotsPerFrame = 12;

    private static int _blotFrame = -1, _blots;
    private static readonly Dictionary<StainPart, (F3 sum, F3 vel, float r, int n, InkBody.Cap cap)> Pooled = new();

    /// <summary>A drop (or one of the game's drips) meets Henry.</summary>
    public static void HitBody(InkBody.Cap cap, F3 contact, F3 normal, F3 velocity, float radius)
    {
        if (_blotFrame != Time.frameCount)
        {
            FlushPooled();
            _blotFrame = Time.frameCount;
            _blots = 0;
        }

        if (_blots++ >= BlotsPerFrame)
        {
            Pooled.TryGetValue(cap.P, out var q);
            Pooled[cap.P] = (q.sum + contact, q.vel + velocity, FM.Max(q.r, radius), q.n + 1, cap);
            return;
        }

        var scale = VRRig.ActiveScale;
        var blot = FM.Clamp(radius * 2.6f, 0.005f * scale, 0.035f * scale);
        var amount = FM.Clamp(radius / (0.004f * scale), 0.45f, 1f);
        // The capsule is only a stand-in: the ink lands on the real surface nearest where it hit.
        InkStains.Deposit(cap.P, cap.P.NearestSurface(contact), blot, amount, velocity);
        Splash(contact, normal, velocity, radius, cap.Group);
        if (cap.Group is 1 or 2 && InkClock.Now >= _nextTick)
        {
            _nextTick = InkClock.Now + 0.05f;
            XrInput.Haptic(cap.Group == 1 ? Hand.Left : Hand.Right, FM.Clamp(radius / (0.006f * scale), 0.08f, 0.3f), 0.012f);
        }
    }

    /// <summary>This frame's pooled hits: one blot per part (and one splash), for all of them.</summary>
    public static void FlushPooled()
    {
        if (Pooled.Count == 0) return;
        foreach (var kv in Pooled)
        {
            var (sum, vel, r, n, cap) = kv.Value;
            HitPooled(cap, sum * (1f / n), vel * (1f / n), r, n);
        }

        Pooled.Clear();
    }

    private static void HitPooled(InkBody.Cap cap, F3 contact, F3 velocity, float radius, int count)
    {
        var scale = VRRig.ActiveScale;
        var blot = FM.Clamp(radius * 2.6f * System.MathF.Sqrt(count), 0.008f * scale, 0.06f * scale);
        InkStains.Deposit(cap.P, cap.P.NearestSurface(contact), blot, 1f, velocity);
        var normal = (contact - (cap.A + cap.B) * 0.5f).Norm;
        Splash(contact, normal, velocity, radius, cap.Group);
    }

    private static void Remove(int i) => D[i] = D[--_n];

    private static int _mask = -1, _maskLayer = -1;

    public static int Mask()
    {
        var player = GameManager.Instance?.Player;
        var layer = player != null ? player.gameObject.layer : 0;
        if (_mask == -1 || layer != _maskLayer)
        {
            _maskLayer = layer;
            _mask = Physics.DefaultRaycastLayers & ~((1 << layer) | VRLayers.UIMask);
        }

        return _mask;
    }

    // ---------------- drawing the drops: one mesh, a stretched droplet each ----------------

    private static int _drawn;

    private static void Render()
    {
        if (_go == null)
        {
            _go = new GameObject("InkVR_InkDrops");
            Object.DontDestroyOnLoad(_go);
            _mesh = new Mesh { name = "InkVR_InkDrops" };
            _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            r.sharedMaterial = InkMaterials.GameSurface(HenryTextures.Flat(HenryTextures.Ink, "InkVR_DropsInk"), "InkVR_InkDrops");
            r.shadowCastingMode = ShadowCastingMode.Off;
            _verts = new Vector3[Max * 6];
            _tris = new int[Max * 24];
            int[] t = { 0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 2, 1, 3, 2, 1, 4, 3, 1, 5, 4, 1, 2, 5 };
            for (var i = 0; i < Max; i++)
            for (var k = 0; k < 24; k++)
                _tris[i * 24 + k] = i * 6 + t[k];
        }

        if (!_go.activeSelf) _go.SetActive(true);
        var count = System.Math.Max(_n, _drawn);
        for (var i = 0; i < count; i++)
        {
            var o = i * 6;
            if (i >= _n)
            {
                for (var k = 0; k < 6; k++) _verts[o + k] = default;
                continue;
            }

            var d = D[i];
            var speed = d.V.Len;
            var dir = speed > 1e-5f ? d.V / speed : F3.Down;
            var side = F3.Cross(dir, System.Math.Abs(dir.Y) < 0.9f ? F3.Up : new F3(1, 0, 0)).Norm;
            var up = F3.Cross(side, dir);
            var stretch = FM.Clamp(speed * 0.012f, d.R * 1.2f, d.R * 6f);
            _verts[o] = (d.P + dir * d.R).V;
            _verts[o + 1] = (d.P - dir * stretch).V;
            _verts[o + 2] = (d.P + side * d.R).V;
            _verts[o + 3] = (d.P + up * d.R).V;
            _verts[o + 4] = (d.P - side * d.R).V;
            _verts[o + 5] = (d.P - up * d.R).V;
        }

        _drawn = _n;
        _mesh.vertices = _verts;
        if (_mesh.GetIndexCount(0) == 0) _mesh.SetTriangles(_tris, 0);
        _mesh.RecalculateNormals();
        var head = VRRig.Head;
        _mesh.bounds = new Bounds(head != null ? head.position : Vector3.zero, Vector3.one * 200f);
    }
}

/// <summary>
/// Ink splats Henry leaves on the world: drops that land, ink shaken off a hand. Fresh ones on the floor are
/// wet enough to step in (and print footprints from). They fade after a few minutes.
/// </summary>
public static class WorldInk
{
    private sealed class Mark
    {
        public Transform T;
        public MeshRenderer R;
        public float Born, Size, Alpha;
        public Vector3 Pos, Normal;
        public bool On; // kept here: asking the GameObject costs an interop call per splat per drop
    }

    private const int NewPerFrame = 8;
    private static int _madeFrame = -1, _made;
    private static float _nextFade;

    private const int Max = 160;
    private const float Life = 180f, FadeTime = 60f;
    private static readonly List<Mark> Splats = new();
    private static Material[] _mats;
    private static Mesh _quad;
    private static MaterialPropertyBlock _mpb;

    public static int Count
    {
        get
        {
            var n = 0;
            foreach (var s in Splats)
                if (s.On)
                    n++;
            return n;
        }
    }

    public static void Splat(F3 point, F3 normal, float dropRadius, F3 velocity) => Splat(point.V, normal.V, dropRadius, velocity.V);

    public static void Splat(Vector3 point, Vector3 normal, float dropRadius, Vector3 velocity)
    {
        if (!ModConfig.InkOnHands.Value) return;
        var scale = VRRig.ActiveScale;
        var size = Mathf.Clamp(dropRadius * 7f, 0.012f * scale, 0.12f * scale);
        if (_madeFrame != Time.frameCount)
        {
            _madeFrame = Time.frameCount;
            _made = 0;
        }

        // A heavy pour: past a few new splats a frame, the rest only join splats near them (wider), or are lost.
        var busy = _made >= NewPerFrame;
        var catchK = busy ? 1f : 0.16f;
        var now = InkClock.Now;
        // Landing on top of a fresh splat makes it bigger rather than piling up quads.
        foreach (var s in Splats)
        {
            if (!s.On || now - s.Born > 30f) continue;
            float dx = s.Pos.x - point.x, dy = s.Pos.y - point.y, dz = s.Pos.z - point.z;
            if (dx * dx + dy * dy + dz * dz > s.Size * s.Size * catchK) continue;
            if (s.Normal.x * normal.x + s.Normal.y * normal.y + s.Normal.z * normal.z < 0.8f) continue;
            // Drips landing on one spot pool a little, but a trail of drips must not snowball into one huge
            // splat (each merge widens the catch area), so a merged splat stays drip-sized.
            s.Size = Mathf.Min(Mathf.Sqrt(s.Size * s.Size + size * size * 0.5f), Mathf.Max(s.Size, 0.09f * scale));
            s.T.localScale = new Vector3(s.Size, 1f, s.Size * s.T.localScale.z / Mathf.Max(1e-6f, s.T.localScale.x));
            s.Born = InkClock.Now;
            SetAlpha(s, s.Alpha = 1f);
            return;
        }

        if (busy) return;
        _made++;
        var sp = Take();
        // A drop that lands at a slant smears along the way it was going.
        var along = Vector3.ProjectOnPlane(velocity, normal);
        var slant = Mathf.Clamp01(along.magnitude / Mathf.Max(1e-5f, velocity.magnitude));
        var fwd = along.sqrMagnitude > 1e-8f ? along.normalized : Vector3.ProjectOnPlane(Random.onUnitSphere, normal).normalized;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.Cross(normal, Vector3.right).normalized;
        sp.T.SetPositionAndRotation(point + normal * (0.002f * scale), Quaternion.LookRotation(fwd, normal) * Quaternion.Euler(0, 0, 0));
        sp.T.localScale = new Vector3(size, 1f, size * (1f + slant * 1.5f));
        sp.R.sharedMaterial = _mats[Random.Range(0, _mats.Length)];
        sp.Born = InkClock.Now;
        sp.Size = size;
        sp.Pos = point;
        sp.Normal = normal;
        sp.Alpha = 1f;
        sp.On = true;
        sp.T.gameObject.SetActive(true);
        SetAlpha(sp, 1f);
    }

    /// <summary>How much fresh, wet ink from splats is on the floor at a point (0..1).</summary>
    public static float WetAt(Vector3 point, float radius)
    {
        var best = 0f;
        var now = InkClock.Now;
        var scale = VRRig.ActiveScale;
        foreach (var s in Splats)
        {
            if (!s.On || s.Normal.y < 0.7f || now - s.Born > 90f) continue;
            var age = now - s.Born;
            var r = s.Size * 0.45f + radius;
            float dx = s.Pos.x - point.x, dy = s.Pos.y - point.y, dz = s.Pos.z - point.z;
            if (dx * dx + dy * dy + dz * dz > r * r) continue;
            best = FM.Max(best, (1f - age / 90f) * FM.Clamp01(s.Size / (0.06f * scale)));
        }

        return best;
    }

    public static void Update()
    {
        // Fading over a minute: a few steps a second is smooth enough.
        var now = InkClock.Now;
        if (now < _nextFade) return;
        _nextFade = now + 0.2f;
        foreach (var s in Splats)
        {
            if (!s.On) continue;
            var age = now - s.Born;
            if (age > Life) Off(s);
            else if (age > Life - FadeTime) SetAlpha(s, s.Alpha * (Life - age) / FadeTime);
        }
    }

    private static void Off(Mark s)
    {
        s.On = false;
        if (s.T != null) s.T.gameObject.SetActive(false);
    }

    /// <summary>Make the splat textures and quads now (a level is loading) rather than on the first drop.</summary>
    public static void Prewarm() => Build();

    public static void Clear()
    {
        foreach (var s in Splats) Off(s);
    }

    private static void SetAlpha(Mark s, float a)
    {
        _mpb ??= new MaterialPropertyBlock();
        _mpb.SetColor("_Color", new Color(1, 1, 1, a));
        s.R.SetPropertyBlock(_mpb);
    }

    private static Mark Take()
    {
        Build();
        foreach (var s in Splats)
            if (!s.On && s.T != null)
                return s;
        if (Splats.Count < Max)
        {
            var go = new GameObject("InkVR_WorldSplat");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var s = new Mark { T = go.transform, R = r };
            Splats.Add(s);
            return s;
        }

        var oldest = Splats[0];
        foreach (var s in Splats)
            if (s.Born < oldest.Born)
                oldest = s;
        return oldest;
    }

    private static void Build()
    {
        if (_mats != null) return;
        _quad = new Mesh { name = "InkVR_SplatQuad" };
        _quad.SetVertices(new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) });
        _quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
        _quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        _quad.RecalculateBounds();
        _quad.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(_quad);
        _mats = new Material[3];
        for (var i = 0; i < _mats.Length; i++)
        {
            var mat = new Material(Shader.Find("UI/Default")) { name = "InkVR_WorldSplat" + i, mainTexture = SplatTexture(700 + i), renderQueue = 2952 };
            mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
            mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            Object.DontDestroyOnLoad(mat);
            _mats[i] = mat;
        }
    }

    /// <summary>An ink splat: a round body, lobes, a few flung droplets (the drop's direction is towards +v).</summary>
    private static Texture2D SplatTexture(int seed)
    {
        const int N = 128;
        var rng = new System.Random(seed);
        var blobs = new List<(float x, float y, float r)> { (0.5f, 0.45f, 0.22f) };
        for (var i = 0; i < 7; i++)
        {
            var a = rng.NextDouble() * System.Math.PI * 2;
            var d = 0.12 + rng.NextDouble() * 0.12;
            blobs.Add(((float)(0.5 + System.Math.Cos(a) * d), (float)(0.45 + System.Math.Sin(a) * d * 1.2), (float)(0.06 + rng.NextDouble() * 0.07)));
        }

        for (var i = 0; i < 9; i++)
        {
            var a = rng.NextDouble() * System.Math.PI * 2;
            var d = 0.3 + rng.NextDouble() * 0.15;
            blobs.Add(((float)(0.5 + System.Math.Cos(a) * d * 0.8), (float)(0.5 + System.Math.Sin(a) * d), (float)(0.012 + rng.NextDouble() * 0.02)));
        }

        var px = new Color32[N * N];
        for (var y = 0; y < N; y++)
        for (var x = 0; x < N; x++)
        {
            float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
            var a = 0f;
            foreach (var (bx, by, br) in blobs)
            {
                var d = Mathf.Sqrt((u - bx) * (u - bx) + (v - by) * (v - by));
                a = Mathf.Max(a, Mathf.Clamp01((br - d) / 0.012f + 0.5f));
            }

            px[y * N + x] = new Color32(9, 7, 6, (byte)(a * 245));
        }

        var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "InkVR_Splat" + seed, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        tex.SetPixels32(px);
        tex.Apply(true, true);
        tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(tex);
        return tex;
    }
}
