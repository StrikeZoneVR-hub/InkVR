using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InkVR.Core;
using InkVR.Input;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InkVR.Body;

/// <summary>
/// The ink you're standing in, as a liquid. A patch of its surface around you (three metres across, moving with
/// you) is simulated as waves: whatever of Henry's is in the ink pushes it (a hand dragged through leaves a wake,
/// a plunge sends out rings, wading legs leave a trail), and the waves spread, bounce off walls and whatever
/// stands in the ink, and settle (ink is heavy). Plunging in or slapping the surface throws up real drops
/// (<see cref="InkDrops"/>: they land on you, on the floor, or back in the ink with a ring of their own), and a
/// fast swish sprays. The game's own drips and rain ring the surface where they fall in. The patch is drawn with
/// the pool's own material and mapping, so it's the same ink, moving.
/// </summary>
public static class InkPool
{
    private const int N = 112;            // cells a side
    private const int Edge = 14;          // cells of soft edge where the waves fade out into the still pool
    private const float CellM = 0.028f;   // metres a cell (the patch is about 3 m across)
    private const float SpeedM = 0.34f;   // wave speed, m/s
    private const float Step = 1f / 72f;
    private const float MaxUp = 0.035f, LiftM = 0.010f, LiftEdgeM = 0.0015f;

    // Heights and vertical speeds in metres (of Henry), sources in m/s².
    private static readonly float[] H = new float[N * N], V = new float[N * N], Src = new float[N * N];
    private static readonly float[] Tmp = new float[N * N], Tmp2 = new float[N * N];
    private static readonly byte[] Wall = new byte[N * N], WallTmp = new byte[N * N]; // 0 open, 1 solid, 2 not looked at yet
    private static readonly byte[] Obst = new byte[N * N]; // Henry is standing in it this frame
    private static readonly float[] Damp = new float[N * N], Lift = new float[N * N];

    private static Renderer _pool;
    private static M34 _toLocal;
    private static Box3 _box;
    private static float _top, _dx, _scale, _acc, _nextFind, _sleepy, _spray, _srcMax;
    private static int _ox, _oz, _probe;
    private static bool _on, _uvDirty, _awake;

    // Henry's capsules last frame (for how fast each part moves through the ink).
    private static F3[] _pa = new F3[64], _pb = new F3[64];
    private static int _prevN = -1;
    private static readonly F3[] _legs = new F3[2];
    private static readonly float[] LastSplash = new float[4];
    private static float _legT;
    private static float _prevT;

    /// <summary>Dev: how strongly Henry pushes the ink (1 = normal), and counters.</summary>
    public static float Gain = 1f;

    public static int Splashes, Caught, Probed, Walls;
    public static double MsPush, MsSim, MsDraw;
    public static string Name => _pool != null ? _pool.name : "-";
    public static bool Active => _on;
    public static float Top => _top;

    public static float MaxHeight
    {
        get
        {
            var m = 0f;
            for (var i = 0; i < H.Length; i++) m = Math.Max(m, Math.Abs(H[i]));
            return m;
        }
    }

    static InkPool()
    {
        for (var j = 0; j < N; j++)
        for (var i = 0; i < N; i++)
        {
            var e = Math.Min(Math.Min(i, j), Math.Min(N - 1 - i, N - 1 - j));
            var x = FM.Clamp01(e / (float)Edge);
            var s = x * x * (3 - 2 * x);
            // waves die out towards the edge of the patch, so it meets the still pool without a seam
            Damp[j * N + i] = (1f - 2.2f * Step) * (1f - 0.22f * (1 - x) * (1 - x));
            Lift[j * N + i] = LiftEdgeM + (LiftM - LiftEdgeM) * s;
        }
    }

    /// <summary>Dev: switch the pool simulation off (to compare).</summary>
    public static bool DevOff;

    public static void Update(float dt)
    {
        var head = VRRig.Head;
        if (head == null || DevOff)
        {
            Off();
            return;
        }

        var now = InkClock.Now;
        if (now >= _nextFind)
        {
            _nextFind = now + 0.25f;
            if (!InkWorld.FindPool(head.position, out var r, out var top)) Off();
            else if (r != _pool || Math.Abs(VRRig.ActiveScale - _scale) > 1e-4f) Begin(r, top);
            else
            {
                // the same ink, risen or drained a little: the waves ride on it
                _toLocal = M34.Of(r.transform.worldToLocalMatrix);
                _box = Box3.Of(r.bounds);
                if (Math.Abs(top - _top) > 1e-5f)
                {
                    _top = top;
                    _uvDirty = true;
                }
            }
        }

        if (!_on) return;
        if (_pool == null || !_pool.gameObject.activeInHierarchy)
        {
            Off();
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Recentre(head.position);
        ProbeWalls(220);
        Push(now);
        var t1 = sw.Elapsed.TotalMilliseconds;
        _acc = Math.Min(_acc + dt, Step * 4);
        if (_awake)
            while (_acc >= Step)
            {
                _acc -= Step;
                Simulate();
            }
        else _acc = 0;

        var t2 = sw.Elapsed.TotalMilliseconds;
        Draw();
        var t3 = sw.Elapsed.TotalMilliseconds;
        MsPush = MsPush * 0.95 + t1 * 0.05;
        MsSim = MsSim * 0.95 + (t2 - t1) * 0.05;
        MsDraw = MsDraw * 0.95 + (t3 - t2) * 0.05;
    }

    // ---------------- the patch ----------------

    private static void Begin(Renderer r, float top)
    {
        _pool = r;
        _top = top;
        _scale = VRRig.ActiveScale;
        _dx = CellM * _scale;
        _toLocal = M34.Of(r.transform.worldToLocalMatrix);
        _box = Box3.Of(r.bounds);
        Array.Clear(H, 0, H.Length);
        Array.Clear(V, 0, V.Length);
        for (var i = 0; i < Wall.Length; i++) Wall[i] = 2;
        _probe = 0;
        Walls = 0;
        _prevN = -1;
        var head = VRRig.Head.position;
        _ox = (int)Math.Floor(head.x / _dx) - N / 2;
        _oz = (int)Math.Floor(head.z / _dx) - N / 2;
        _on = true;
        _uvDirty = true;
        _awake = true;
        Build();
        _go.layer = r.gameObject.layer;
        var mr = _go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = r.sharedMaterial;
        _shineRenderer.gameObject.layer = r.gameObject.layer;
        CopyLook(mr, true);
        mr.receiveShadows = r.receiveShadows;
        mr.lightmapIndex = r.lightmapIndex;
        mr.lightmapScaleOffset = r.lightmapScaleOffset;
        mr.reflectionProbeUsage = r.reflectionProbeUsage;
        var right = r.transform.right.normalized;
        var tan = new Vector4(right.x, right.y, right.z, -1f);
        for (var i = 0; i < N * N; i++) _tans[i] = tan;
        _mesh.tangents = _tans;
        _go.SetActive(true);
        Plugin.L.LogInfo($"Ink pool: {r.name} (top {top:F2}, lightmap {r.lightmapIndex}, {r.sharedMaterial?.shader?.name})");
    }

    public static void Hide() => Off();

    private static MaterialPropertyBlock _block;

    /// <summary>
    /// Many of the game's ink floors get their own settings on top of the shared material (its MaterialInstanceController
    /// puts them in a property block on the pool's renderer). Without them the patch drew as a different ink, one
    /// that dissolved to nothing: a clear square round your hands where the floor showed through the pool.
    /// </summary>
    private static void CopyLook(MeshRenderer mr, bool log = false)
    {
        if (_pool == null || mr == null) return;
        _block ??= new MaterialPropertyBlock();
        _pool.GetPropertyBlock(_block);
        mr.SetPropertyBlock(_block);
        if (log && !_block.isEmpty) Plugin.L.LogInfo($"Ink pool: {_pool.name} has its own ink settings; the moving surface uses them too");
    }

    /// <summary>Dev: ridges across the whole patch (to see that it draws, and how the ink's light plays on it).</summary>
    public static void DevRidges(float amp)
    {
        for (var j = 0; j < N; j++)
        for (var i = 0; i < N; i++)
            if (Wall[j * N + i] != 1)
                H[j * N + i] = amp * MathF.Sin(i * 0.45f) * MathF.Sin(j * 0.3f);
        _awake = true;
    }

    /// <summary>Dev: draw the patch with the pool's material (orig), a copy without edge fading (nofade), or flat white (debug).</summary>
    public static void DevMaterial(string which)
    {
        if (_go == null || _pool == null) return;
        var mr = _go.GetComponent<MeshRenderer>();
        switch (which)
        {
            case "nofade":
            {
                var m = new Material(_pool.sharedMaterial) { name = "InkVR_PoolNoFade" };
                m.DisableKeyword("_FADEEDGES_ON");
                m.SetFloat("_FadeEdges", 0f);
                mr.sharedMaterial = m;
                break;
            }
            case "debug":
            {
                var m = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "InkVR_PoolDebug" };
                m.SetColor("_Color", new Color(0.9f, 0.9f, 0.85f, 1f));
                mr.sharedMaterial = m;
                break;
            }
            default:
                mr.sharedMaterial = _pool.sharedMaterial;
                break;
        }
    }

    public static string DevPeak()
    {
        var best = 0;
        for (var i = 0; i < H.Length; i++)
            if (Math.Abs(H[i]) > Math.Abs(H[best])) best = i;
        var bi = best % N;
        var bj = best / N;
        var head = VRRig.Head.position;
        var x = (_ox + bi + 0.5f) * _dx - head.x;
        var z = (_oz + bj + 0.5f) * _dx - head.z;
        var sb = new System.Text.StringBuilder($"peak {H[best] * 1000f:F1} mm at cell {bi},{bj} ({x / _scale:F2}, {z / _scale:F2} m from head) wall={Wall[best]} obst={Obst[best]} v={V[best]:F3} src={Src[best]:F2} | ");
        for (var dj = -2; dj <= 2; dj++)
        {
            for (var di = -2; di <= 2; di++)
            {
                var i = bi + di;
                var j = bj + dj;
                if (i < 0 || j < 0 || i >= N || j >= N) continue;
                sb.Append($"{H[j * N + i] * 1000f:F0}{(Wall[j * N + i] == 1 ? "w" : Obst[j * N + i] == 1 ? "o" : "")} ");
            }

            sb.Append("/ ");
        }

        return sb.ToString();
    }

    public static string DevState()
    {
        if (_go == null) return "no mesh";
        var mr = _go.GetComponent<MeshRenderer>();
        var m = mr.sharedMaterial;
        var sb = new System.Text.StringBuilder($"go={_go.activeSelf} layer={_go.layer} mat={m?.name} queue={m?.renderQueue} verts={_mesh.vertexCount} bounds={_mesh.bounds.center}/{_mesh.bounds.size} awake={_awake}");
        if (m != null)
        {
            var sh = m.shader;
            for (var i = 0; i < sh.GetPropertyCount(); i++)
            {
                var n = sh.GetPropertyName(i);
                var t = sh.GetPropertyType(i);
                sb.Append($" | {n}:{t}");
                if (t == UnityEngine.Rendering.ShaderPropertyType.Float || t == UnityEngine.Rendering.ShaderPropertyType.Range) sb.Append($"={m.GetFloat(n):F3}");
                else if (t == UnityEngine.Rendering.ShaderPropertyType.Texture) sb.Append($"={m.GetTexture(n)?.name} st={m.GetTextureScale(n)}{m.GetTextureOffset(n)}");
                else if (t == UnityEngine.Rendering.ShaderPropertyType.Color) sb.Append($"={m.GetColor(n)}");
            }
            sb.Append($" | keywords={string.Join(",", m.shaderKeywords)}");
        }

        // the game's own pool mesh: what its vertex colours and UVs carry (the shader's edge fade reads one of them)
        var mf = _pool != null ? _pool.GetComponent<MeshFilter>() : null;
        var pm = mf != null ? mf.sharedMesh : null;
        if (pm != null)
        {
            sb.Append($" || pool mesh {pm.name} verts={pm.vertexCount} readable={pm.isReadable}");
            if (pm.isReadable)
            {
                var cols = pm.colors32;
                if (cols != null && cols.Length > 0)
                {
                    int r0 = 255, r1 = 0, a0 = 255, a1 = 0;
                    foreach (var c in cols)
                    {
                        r0 = Math.Min(r0, c.r);
                        r1 = Math.Max(r1, c.r);
                        a0 = Math.Min(a0, c.a);
                        a1 = Math.Max(a1, c.a);
                    }

                    sb.Append($" colors={cols.Length} r {r0}..{r1} a {a0}..{a1} first={cols[0]}");
                }
                else sb.Append(" colors=none");

                var uv = pm.uv;
                if (uv != null && uv.Length > 0)
                {
                    float u0 = 9e9f, u1 = -9e9f, v0 = 9e9f, v1 = -9e9f;
                    foreach (var p in uv)
                    {
                        u0 = Math.Min(u0, p.x);
                        u1 = Math.Max(u1, p.x);
                        v0 = Math.Min(v0, p.y);
                        v1 = Math.Max(v1, p.y);
                    }

                    sb.Append($" uv u {u0:F2}..{u1:F2} v {v0:F2}..{v1:F2}");
                }

                var b = pm.bounds;
                sb.Append($" localBounds={b.center}/{b.size}");
            }
        }

        return sb.ToString();
    }

    private static void Off()
    {
        if (!_on) return;
        _on = false;
        _pool = null;
        if (_go != null) _go.SetActive(false);
    }

    private static void Recentre(Vector3 head)
    {
        var ci = (int)Math.Floor(head.x / _dx) - N / 2;
        var cj = (int)Math.Floor(head.z / _dx) - N / 2;
        var di = ci - _ox;
        var dj = cj - _oz;
        if (Math.Abs(di) <= 6 && Math.Abs(dj) <= 6) return;
        for (var j = 0; j < N; j++)
        for (var i = 0; i < N; i++)
        {
            var si = i + di;
            var sj = j + dj;
            var idx = j * N + i;
            if (si < 0 || sj < 0 || si >= N || sj >= N)
            {
                Tmp[idx] = Tmp2[idx] = 0;
                WallTmp[idx] = 2;
                continue;
            }

            var s = sj * N + si;
            Tmp[idx] = H[s];
            Tmp2[idx] = V[s];
            WallTmp[idx] = Wall[s];
        }

        Array.Copy(Tmp, H, H.Length);
        Array.Copy(Tmp2, V, V.Length);
        Array.Copy(WallTmp, Wall, Wall.Length);
        _ox = ci;
        _oz = cj;
        _probe = 0;
        _uvDirty = true;
        _prevN = -1;
    }

    /// <summary>Walls, crates and pillars standing in the ink: waves bounce off them (a few cells a frame).</summary>
    private static void ProbeWalls(int budget)
    {
        if (_probe >= N * N) return;
        var mask = InkDrops.Mask();
        var y = _top + 0.05f * _scale;
        var r = 0.45f * _dx;
        for (; _probe < N * N && budget > 0; _probe++)
        {
            if (Wall[_probe] != 2) continue;
            budget--;
            var i = _probe % N;
            var j = _probe / N;
            var x = (_ox + i + 0.5f) * _dx;
            var z = (_oz + j + 0.5f) * _dx;
            var solid = x < _box.Min.X || x > _box.Max.X || z < _box.Min.Z || z > _box.Max.Z;
            if (!solid)
            {
                Vector3 c = default;
                c.x = x;
                c.y = y;
                c.z = z;
                solid = Physics.CheckSphere(c, r, mask, QueryTriggerInteraction.Ignore);
                Probed++;
            }

            Wall[_probe] = solid ? (byte)1 : (byte)0;
            if (solid)
            {
                H[_probe] = V[_probe] = 0;
                Walls++;
            }
        }
    }

    // ---------------- Henry in the ink ----------------

    private static void Push(float now)
    {
        Array.Clear(Src, 0, Src.Length);
        Array.Clear(Obst, 0, Obst.Length);
        _srcMax = 0;
        InkBody.Refresh();
        var caps = InkBody.Caps;
        var n = caps.Count;
        var frameDt = now - _prevT;
        _prevT = now;
        var haveVel = n == _prevN && frameDt > 1e-4f && frameDt < 0.2f;
        if (_pa.Length < n)
        {
            _pa = new F3[n * 2];
            _pb = new F3[n * 2];
        }

        var k = _scale;
        var any = false;
        var budget = InkClock.Dt;
        for (var c = 0; c < n; c++)
        {
            var cap = caps[c];
            var a = cap.A;
            var b = cap.B;
            var r = cap.R;
            var low = Math.Min(a.Y, b.Y) - r;
            var high = Math.Max(a.Y, b.Y) + r;
            var va = haveVel ? (a - _pa[c]) / frameDt : default;
            var vb = haveVel ? (b - _pb[c]) / frameDt : default;
            var wasLow = haveVel ? Math.Min(_pa[c].Y, _pb[c].Y) - r : low;
            _pa[c] = a;
            _pb[c] = b;
            if (low > _top || high < _top) continue;

            // circles where the capsule cuts the surface
            var len = (b - a).Len;
            var samples = Math.Clamp((int)Math.Ceiling(len / Math.Max(r, 1e-5f)) + 1, 2, 6);
            var crossed = false;
            F3 at = default, vAt = default;
            var atR = 0f;
            for (var s = 0; s < samples; s++)
            {
                var t = s / (float)(samples - 1);
                var p = a + (b - a) * t;
                var dy = p.Y - _top;
                if (Math.Abs(dy) >= r) continue;
                var rr = MathF.Sqrt(r * r - dy * dy);
                var v = va + (vb - va) * t;
                Stamp(p.X, p.Z, rr, v);
                if (!crossed || rr > atR)
                {
                    at = p;
                    vAt = v;
                    atR = rr;
                }

                crossed = true;
            }

            if (!crossed || !haveVel) continue;
            any = true;
            // Going in fast: a splash, thrown up round where it went in.
            var down = -vAt.Y / k;
            // (one splash per hand or body at a time: a hand is several capsules going in together)
            if (wasLow > _top && down > 0.8f && now - LastSplash[cap.Group & 3] > 0.15f)
            {
                LastSplash[cap.Group & 3] = now;
                Splash(at, atR, vAt, down, cap.Group);
            }
            // Swishing: spray off the front.
            var flat = new F3(vAt.X, 0, vAt.Z);
            var fs = flat.Len / k;
            if (fs > 1.1f)
            {
                _spray += (fs - 1.1f) * (atR / k) * 520f * budget;
                while (_spray >= 1f)
                {
                    _spray -= 1f;
                    Spray(at, atR, flat, fs, cap.Group);
                }
            }
        }

        _prevN = n;

        // Hands only: no legs to see, but the ink still parts round where they'd be.
        if (Hands.Body == null && VRRig.Head != null)
        {
            var head = VRRig.Head;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, fwd);
            for (var f = 0; f < 2; f++)
            {
                var foot = head.position + right * (0.1f * k * (f == 0 ? -1f : 1f));
                var ground = Hands.GroundAt(foot);
                if (ground > _top || head.position.y < _top) continue;
                var at = new F3(foot.x, _top, foot.z);
                var v = haveVel || _legT > 0 ? (at - _legs[f]) / Math.Max(1e-4f, now - _legT) : default;
                if (now - _legT > 0.2f) v = default;
                _legs[f] = at;
                Stamp(at.X, at.Z, 0.065f * k, new F3(v.X, 0, v.Z));
            }

            _legT = now;
        }
        // standing still in it doesn't stir it
        if (any && _srcMax > 0.08f) _awake = true;
    }

    /// <summary>A part of Henry where it cuts the surface: a circle the ink can't be in, pushing the ink round it.</summary>
    private static void Stamp(float x, float z, float rr, F3 vel)
    {
        var gx = x / _dx - _ox - 0.5f;
        var gz = z / _dx - _oz - 0.5f;
        var rc = rr / _dx;
        var reach = rc + 3f;
        var i0 = Math.Max(1, (int)(gx - reach));
        var i1 = Math.Min(N - 2, (int)(gx + reach) + 1);
        var j0 = Math.Max(1, (int)(gz - reach));
        var j1 = Math.Min(N - 2, (int)(gz + reach) + 1);
        if (i0 > i1 || j0 > j1) return;
        var vx = vel.X / _scale;
        var vz = vel.Z / _scale;
        var vy = vel.Y / _scale;
        var g = Gain;
        for (var j = j0; j <= j1; j++)
        for (var i = i0; i <= i1; i++)
        {
            var ddx = i - gx;
            var ddz = j - gz;
            var d = MathF.Sqrt(ddx * ddx + ddz * ddz);
            var idx = j * N + i;
            if (d < rc * 0.85f)
            {
                Obst[idx] = 1;
                continue;
            }

            var w = 1f - (d - rc) / 3f;
            if (w <= 0) continue;
            if (w > 1) w = 1;
            // the ink in front rises and behind it falls (a wake); going down pushes it up round the edge,
            // pulling out drags it down after
            var un = d > 1e-4f ? (ddx * vx + ddz * vz) / d : 0f;
            var add = (un * 7f - vy * 5.5f) * w * g;
            Src[idx] += add;
            if (add > _srcMax) _srcMax = add;
            else if (-add > _srcMax) _srcMax = -add;
        }
    }

    private static readonly System.Random Rng = new(4242);

    private static float Rand(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

    private static void Splash(F3 at, float ringR, F3 vel, float speed, int group)
    {
        var k = _scale;
        var n = Math.Clamp((int)(speed * 9f * (ringR / k) / 0.035f), 5, 40);
        var flat = new F3(vel.X, 0, vel.Z) * 0.25f;
        var up = Math.Min(speed, 4.5f);
        for (var i = 0; i < n; i++)
        {
            var ang = Rand(0, MathF.PI * 2);
            var dir = new F3(MathF.Cos(ang), 0, MathF.Sin(ang));
            var p = new F3(at.X, _top + 0.006f * k, at.Z) + dir * (ringR * Rand(0.9f, 1.25f));
            var v = (dir * (Rand(0.25f, 0.9f) * up * 0.45f) + F3.Up * (Rand(0.6f, 1.5f) * up * 0.6f)) * k + flat;
            InkDrops.Spawn(p, v, Rand(0.0012f, 0.0042f) * k, group);
        }

        // and the crater it leaves
        Poke(at.X, at.Z, Math.Min(0.012f, 0.004f * speed), Math.Max(1.5f, ringR / _dx));
        Splashes++;
        if (group is 1 or 2) XrInput.Haptic(group == 1 ? Hand.Left : Hand.Right, FM.Clamp(0.15f + speed * 0.12f, 0.15f, 0.7f), 0.07f);
        SplashSound(at.V, Math.Min(1f, speed / 3f));
    }

    private static void Spray(F3 at, float ringR, F3 flat, float speed, int group)
    {
        var k = _scale;
        var dir = flat / Math.Max(1e-5f, flat.Len);
        var side = new F3(-dir.Z, 0, dir.X);
        var p = new F3(at.X, _top + 0.004f * k, at.Z) + dir * (ringR * 1.05f) + side * (ringR * Rand(-0.8f, 0.8f));
        var v = flat * Rand(0.5f, 0.95f) + (F3.Up * Rand(0.4f, 1.3f) + side * Rand(-0.35f, 0.35f)) * (Math.Min(speed, 4f) * 0.6f * k);
        InkDrops.Spawn(p, v, Rand(0.0010f, 0.0030f) * k, group);
    }

    /// <summary>Something fell in at (x, z): a little crater (metres deep, cells wide) that rings outwards.</summary>
    public static void Poke(float x, float z, float depth, float radiusCells)
    {
        if (!_on) return;
        var gx = x / _dx - _ox - 0.5f;
        var gz = z / _dx - _oz - 0.5f;
        var reach = radiusCells + 1;
        var i0 = Math.Max(1, (int)(gx - reach));
        var i1 = Math.Min(N - 2, (int)(gx + reach) + 1);
        var j0 = Math.Max(1, (int)(gz - reach));
        var j1 = Math.Min(N - 2, (int)(gz + reach) + 1);
        for (var j = j0; j <= j1; j++)
        for (var i = i0; i <= i1; i++)
        {
            var ddx = i - gx;
            var ddz = j - gz;
            var q = (ddx * ddx + ddz * ddz) / (radiusCells * radiusCells);
            if (q > 1.5f) continue;
            var idx = j * N + i;
            if (Wall[idx] == 1) continue;
            H[idx] -= depth * MathF.Exp(-2.5f * q) * Gain;
        }

        _awake = true;
    }

    /// <summary>A drop of ours falling a → b: if it falls into this ink it rings it and is gone.</summary>
    public static bool Catch(F3 a, F3 b, float radius, F3 vel)
    {
        if (!_on) return false;
        if (a.Y < _top || b.Y >= _top) return false;
        var t = (a.Y - _top) / Math.Max(1e-6f, a.Y - b.Y);
        var p = a + (b - a) * t;
        if (p.X < _box.Min.X || p.X > _box.Max.X || p.Z < _box.Min.Z || p.Z > _box.Max.Z) return false;
        var gi = (int)(p.X / _dx) - _ox;
        var gj = (int)(p.Z / _dx) - _oz;
        if (gi >= 0 && gj >= 0 && gi < N && gj < N && Wall[gj * N + gi] == 1) return false; // that's the top of something standing in it
        var speed = vel.Len / _scale;
        var r = radius / _scale;
        Poke(p.X, p.Z, Math.Min(0.004f, r * speed * 0.3f), Math.Max(2.6f, r * 3f / CellM));
        // a big, fast drop kicks up a droplet of its own
        if (r > 0.0025f && speed > 2.5f && Rng.NextDouble() < 0.5)
            InkDrops.Spawn(new F3(p.X, _top + 0.004f * _scale, p.Z), F3.Up * (Rand(0.5f, 1.1f) * _scale), radius * 0.4f);
        Caught++;
        return true;
    }

    /// <summary>The game's own drip or raindrop at a → b (falling into the ink makes a ring).</summary>
    public static void GameDrop(F3 a, F3 b, float radius)
    {
        if (!_on || a.Y < _top || b.Y >= _top) return;
        var t = (a.Y - _top) / Math.Max(1e-6f, a.Y - b.Y);
        var p = a + (b - a) * t;
        Poke(p.X, p.Z, Math.Min(0.003f, radius / _scale * 0.6f), 2.6f);
    }

    /// <summary>The space the patch covers (for which of the game's drips to watch).</summary>
    public static Box3 Region
    {
        get
        {
            var min = new F3(_ox * _dx, _top - 0.3f * _scale, _oz * _dx);
            return new Box3 { Min = min, Max = min + new F3(N * _dx, 4f * _scale, N * _dx) };
        }
    }

    // ---------------- the waves ----------------

    private static void Simulate()
    {
        var kk = SpeedM * SpeedM / (CellM * CellM) * Step;
        var active = 0f;
        for (var j = 1; j < N - 1; j++)
        {
            var row = j * N;
            for (var i = 1; i < N - 1; i++)
            {
                var idx = row + i;
                if (Wall[idx] == 1 || Obst[idx] == 1)
                {
                    V[idx] = 0;
                    H[idx] *= 0.85f;
                    continue;
                }

                var hc = H[idx];
                // a wall or Henry next to a cell reflects (no flow across)
                var l = Solid(idx - 1) ? hc : H[idx - 1];
                var r = Solid(idx + 1) ? hc : H[idx + 1];
                var d = Solid(idx - N) ? hc : H[idx - N];
                var u = Solid(idx + N) ? hc : H[idx + N];
                var v = (V[idx] + kk * (l + r + d + u - 4f * hc) + Src[idx] * Step) * Damp[idx];
                V[idx] = v;
                if (v > active) active = v;
                else if (-v > active) active = -v;
            }
        }

        // A little smoothing: ripples a cell or two across are the grid's, not the ink's (thick ink has none),
        // and they'd glint as specks.
        Array.Copy(H, Tmp, H.Length);
        for (var j = 1; j < N - 1; j++)
        {
            var row = j * N;
            for (var i = 1; i < N - 1; i++)
            {
                var idx = row + i;
                if (Wall[idx] == 1) continue;
                var hc = Tmp[idx];
                var l = Solid(idx - 1) ? hc : Tmp[idx - 1];
                var r = Solid(idx + 1) ? hc : Tmp[idx + 1];
                var d = Solid(idx - N) ? hc : Tmp[idx - N];
                var u = Solid(idx + N) ? hc : Tmp[idx + N];
                H[idx] = hc + 0.07f * (l + r + d + u - 4f * hc);
            }
        }

        var settle = 1f - 0.6f * Step;
        for (var idx = 0; idx < H.Length; idx++)
        {
            if (Wall[idx] == 1) continue;
            var h = (H[idx] + V[idx] * Step) * settle;
            var lo = -Lift[idx] + 0.0005f;
            h = h < lo ? lo : h > MaxUp ? MaxUp : h;
            H[idx] = h;
            // where anything is still moving (only that part of the patch is redrawn)
            if (h > 2e-5f || h < -2e-5f)
            {
                var i = idx % N;
                var j = idx / N;
                if (i < _ax0) _ax0 = i;
                if (i > _ax1) _ax1 = i;
                if (j < _az0) _az0 = j;
                if (j > _az1) _az1 = j;
            }
        }

        // still again: stop working until something disturbs it
        if (active < 2e-5f)
        {
            _sleepy += Step;
            if (_sleepy > 0.5f)
            {
                _awake = false;
                _sleepy = 0;
            }
        }
        else _sleepy = 0;
    }

    private static bool Solid(int idx) => Wall[idx] == 1 || Obst[idx] == 1;

    // ---------------- drawing ----------------

    private static GameObject _go;
    private static Mesh _mesh;
    private static Il2CppStructArray<Vector3> _verts, _norms;
    private static Il2CppStructArray<Vector2> _uvs;
    private static Il2CppStructArray<Vector4> _tans;
    private static bool _drawnFlat;
    private static int _ax0 = N, _ax1 = -1, _az0 = N, _az1 = -1;   // moving since the last draw
    private static int _dx0 = 0, _dx1 = N - 1, _dz0 = 0, _dz1 = N - 1; // drawn last time

    private static void Build()
    {
        if (_go != null) return;
        _go = new GameObject("InkVR_InkPoolSurface");
        Object.DontDestroyOnLoad(_go);
        _mesh = new Mesh { name = "InkVR_InkPoolSurface" };
        _mesh.MarkDynamic();
        _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var mr = _go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        _inkRenderer = mr;
        _verts = new Il2CppStructArray<Vector3>(N * N);
        _norms = new Il2CppStructArray<Vector3>(N * N);
        _uvs = new Il2CppStructArray<Vector2>(N * N);
        _tans = new Il2CppStructArray<Vector4>(N * N);
        _cols = new Il2CppStructArray<Color32>(N * N);
        _shine = new Material(Shader.Find("Sprites/Default")) { name = "InkVR_InkPoolShine" };
        _shine.SetTexture("_MainTex", Texture2D.whiteTexture);
        _shine.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(_shine);
        var tris = new Il2CppStructArray<int>((N - 1) * (N - 1) * 6);
        var t = 0;
        for (var j = 0; j < N - 1; j++)
        for (var i = 0; i < N - 1; i++)
        {
            var a = j * N + i;
            tris[t++] = a;
            tris[t++] = a + N;
            tris[t++] = a + 1;
            tris[t++] = a + 1;
            tris[t++] = a + N;
            tris[t++] = a + N + 1;
        }

        _mesh.vertices = _verts;
        _mesh.triangles = tris;

        // The glints are a second surface of their own. They're carried in vertex colours (clear where there's
        // no glint), and the pool's ink shader reads vertex colours as its edge fade: sharing one mesh, the ink
        // faded out wherever there was no glint, and you saw the floor through the pool round your hands.
        var shine = new GameObject("InkVR_InkPoolShine");
        shine.transform.SetParent(_go.transform, false);
        _shineMesh = new Mesh { name = "InkVR_InkPoolShine" };
        _shineMesh.MarkDynamic();
        shine.AddComponent<MeshFilter>().sharedMesh = _shineMesh;
        _shineRenderer = shine.AddComponent<MeshRenderer>();
        _shineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _shineRenderer.receiveShadows = false;
        _shineRenderer.sharedMaterial = _shine;
        _shineMesh.vertices = _verts;
        _shineMesh.triangles = tris;
    }

    private static Mesh _shineMesh;
    private static MeshRenderer _shineRenderer, _inkRenderer;
    private static float _nextLook;

    // Ink is black: what shows a moving surface is the light it reflects. The pool's own shader takes its shine
    // from its texture, not the shape of the surface, so the ripples' highlights are worked out here from the
    // lights round about (and the lit walls) and laid over the ink in a second pass, in the lights' colours.
    private static readonly List<(F3 pos, F3 col, float range)> Lamps = new();
    private static float _nextLamps;
    private static Material _shine;
    private static Il2CppStructArray<Color32> _cols;
    public static float ShineGain = 1.8f, Exaggerate = 2f, Threshold = 0.08f, Crisp = 2f, Studio = 0.8f, CrestK = 1300f;
    public static int StudioSharp = 6;
    private static F3 _studioOffset;

    private static void FindLamps(F3 centre)
    {
        Lamps.Clear();
        var all = new List<(float w, F3 pos, F3 col, float range)>();
        foreach (var o in Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<Light>()))
        {
            var l = o.Cast<Light>();
            if (!l.enabled || !l.gameObject.activeInHierarchy || l.type == LightType.Directional || l.intensity <= 0.01f) continue;
            var pos = F3.Of(l.transform.position);
            var d = (pos - centre).Len;
            var reach = l.range + N * _dx * 0.7f;
            if (d > reach) continue;
            var c = l.color;
            all.Add((l.intensity / (1f + d * d / (l.range * l.range)), pos, new F3(c.r, c.g, c.b) * l.intensity, l.range));
        }

        all.Sort((x, y) => y.w.CompareTo(x.w));
        for (var i = 0; i < all.Count && i < 4; i++) Lamps.Add((all[i].pos, all[i].col, all[i].range));
    }

    /// <summary>x to the power 2^n.</summary>
    private static float PowN(float x, int n)
    {
        if (x <= 0) return 0;
        for (var i = 0; i < n; i++) x *= x;
        return x;
    }

    private static float Pow32(float x)
    {
        if (x <= 0) return 0;
        x *= x;
        x *= x;
        x *= x;
        x *= x;
        return x * x;
    }

    private static float Pow256(float x)
    {
        if (x <= 0) return 0;
        x *= x;
        x *= x;
        x *= x;
        x *= x;
        x *= x;
        x *= x;
        x *= x;
        return x * x;
    }

    private static void Draw()
    {
        if (_go == null) return;
        if (InkClock.Now >= _nextLook)
        {
            // the game can change a pool's own settings as it plays (its ink rising, fading): keep up
            _nextLook = InkClock.Now + 0.25f;
            CopyLook(_inkRenderer);
        }

        var moved = _uvDirty;
        if (_uvDirty)
        {
            _uvDirty = false;
            var us = _uvs.AsSpan();
            for (var j = 0; j < N; j++)
            for (var i = 0; i < N; i++)
            {
                var l = _toLocal.Point(new F3((_ox + i + 0.5f) * _dx, _top, (_oz + j + 0.5f) * _dx));
                ref var uv = ref us[j * N + i];
                uv.x = l.X + 0.5f;
                uv.y = l.Y + 0.5f;
            }

            _mesh.uv = _uvs;
            _mesh.uv2 = _uvs; // the lightmap, if the pool has one, is laid out like the quad
        }

        if (!_awake && _drawnFlat && !moved) return;
        _drawnFlat = !_awake;
        var k = _scale;
        var inv = 1f / (2f * CellM);
        var now = InkClock.Now;
        var centre = new F3((_ox + N * 0.5f) * _dx, _top, (_oz + N * 0.5f) * _dx);
        if (now >= _nextLamps)
        {
            _nextLamps = now + 1.5f;
            var lw = System.Diagnostics.Stopwatch.StartNew();
            FindLamps(centre);
            if (lw.Elapsed.TotalMilliseconds > 5) Plugin.L.LogWarning($"INKHITCH lamps {lw.Elapsed.TotalMilliseconds:F1} ms");
        }

        var eye = VRRig.Head != null ? F3.Of(VRRig.Head.position) : centre + F3.Up * (1.6f * k);
        if (VRRig.Head != null)
        {
            var f = VRRig.Head.forward;
            var flat = new F3(f.x, 0, f.z).Norm;
            _studioOffset = new F3(flat.X * 1.2f * k + eye.X, 0, flat.Z * 1.2f * k + eye.Z) - new F3(eye.X, 0, eye.Z);
        }
        var vs = _verts.AsSpan();
        var ns = _norms.AsSpan();
        var cs = _cols.AsSpan();
        var ex = Exaggerate;
        var shine = ShineGain;
        var lampN = Lamps.Count;
        // Redraw what moved since last time and what moved the time before (now settling flat).
        int i0, i1, j0, j1;
        if (moved)
        {
            i0 = j0 = 0;
            i1 = j1 = N - 1;
        }
        else
        {
            i0 = Math.Max(0, Math.Min(_ax0, _dx0) - 2);
            i1 = Math.Min(N - 1, Math.Max(_ax1, _dx1) + 2);
            j0 = Math.Max(0, Math.Min(_az0, _dz0) - 2);
            j1 = Math.Min(N - 1, Math.Max(_az1, _dz1) + 2);
        }

        _dx0 = _ax0;
        _dx1 = _ax1;
        _dz0 = _az0;
        _dz1 = _az1;
        _ax0 = _az0 = N;
        _ax1 = _az1 = -1;
        for (var j = j0; j <= j1; j++)
        for (var i = i0; i <= i1; i++)
        {
            var idx = j * N + i;
            var hc = H[idx];
            ref var p = ref vs[idx];
            p.x = (_ox + i + 0.5f) * _dx;
            p.y = _top + (Lift[idx] + hc) * k;
            p.z = (_oz + j + 0.5f) * _dx;
            // walls and Henry don't slope the ink next to them
            var hl = i > 0 && !Solid(idx - 1) ? H[idx - 1] : hc;
            var hr = i < N - 1 && !Solid(idx + 1) ? H[idx + 1] : hc;
            var hd = j > 0 && !Solid(idx - N) ? H[idx - N] : hc;
            var hu = j < N - 1 && !Solid(idx + N) ? H[idx + N] : hc;
            var gx = (hr - hl) * inv;
            var gz = (hu - hd) * inv;
            var nl = 1f / MathF.Sqrt(gx * gx + 1f + gz * gz);
            ref var nn = ref ns[idx];
            nn.x = -gx * nl;
            nn.y = nl;
            nn.z = -gz * nl;

            ref var col = ref cs[idx];
            var slope = gx * gx + gz * gz;
            if (slope < 2e-6f || Wall[idx] == 1 || Obst[idx] == 1)
            {
                col.a = 0;
                continue;
            }

            // the shine of the tilted surface, less the shine the still ink already has
            var ax = -gx * ex;
            var az = -gz * ex;
            var el = 1f / MathF.Sqrt(ax * ax + 1f + az * az);
            var n = new F3(ax * el, el, az * el);
            var pos = new F3(p.x, p.y, p.z);
            var view = (eye - pos).Norm;
            float r = 0, g = 0, b = 0;
            // a lamp shows in the crests more than on the broad slopes between (or it reads as a glow, not a ripple)
            var crest = 0.3f + 0.7f * FM.Clamp01(MathF.Abs(hl + hr + hd + hu - 4f * hc) * CrestK);
            for (var li = 0; li < lampN; li++)
            {
                var lamp = Lamps[li];
                var to = lamp.pos - pos;
                var d = to.Len;
                var fall = 1f - d / Math.Max(lamp.range, 1e-3f);
                if (fall <= 0) continue;
                var hv = (to / d + view).Norm;
                var sp = Pow256(F3.Dot(n, hv)) - Pow256(hv.Y);
                if (sp <= 0) continue;
                sp *= fall * fall * crest;
                r += sp * lamp.col.X;
                g += sp * lamp.col.Y;
                b += sp * lamp.col.Z;
            }

            // the studio's light from overhead, caught along the crests: thin lines of light round each ring
            {
                var to = new F3(eye.X + _studioOffset.X, _top + 2.4f * k, eye.Z + _studioOffset.Z) - pos;
                var hv = (to.Norm + view).Norm;
                var sp = PowN(F3.Dot(n, hv), StudioSharp) - PowN(hv.Y, StudioSharp);
                if (sp > 0)
                {
                    var curve = MathF.Abs(hl + hr + hd + hu - 4f * hc);
                    sp *= FM.Clamp01(curve * CrestK) * Studio;
                    r += sp;
                    g += sp * 0.86f;
                    b += sp * 0.6f;
                }
            }

            r *= shine;
            g *= shine;
            b *= shine;
            // crisp-edged glints, like the game's own (soft blobs read as smoke, not liquid), in the studio's gold
            var m = Math.Max(r, Math.Max(g, b));
            var a = (m - Threshold) * Crisp;
            if (a <= 0)
            {
                col.a = 0;
                continue;
            }

            if (a > 1) a = 1;
            col.r = (byte)Math.Min(255f, r / m * 255f);
            col.g = (byte)Math.Min(255f, g / m * 232f);
            col.b = (byte)Math.Min(255f, b / m * 178f);
            col.a = (byte)(a * 255f);
        }

        // the ink itself has no vertex colours (read as solid, like the pool's own quad); the glints have them
        _mesh.vertices = _verts;
        _mesh.normals = _norms;
        _shineMesh.vertices = _verts;
        _shineMesh.colors32 = _cols;
        var size = N * _dx;
        Vector3 c = default, s = default;
        c.x = centre.X;
        c.y = _top;
        c.z = centre.Z;
        s.x = size;
        s.y = 0.2f * k;
        s.z = size;
        _mesh.bounds = new Bounds(c, s);
        _shineMesh.bounds = _mesh.bounds;
    }

    // ---------------- sound ----------------

    private static AudioSource _audio;
    private static AudioClip[] _clips;
    private static float _nextSound, _lookedForClips = -100f;

    /// <summary>The game's own ink splash sounds, if this chapter has any loaded.</summary>
    public static string[] ClipNames = { "ink_splash", "barrel_ink_splash", "ink_moves", "Ink_Squirt", "INK_DEEP", "ink_deep", "Ink_Step", "ink_step" };

    /// <summary>One of the game's ink sounds at a point (gravity pull's ink, too).</summary>
    internal static void InkSound(Vector3 at, float loud) => SplashSound(at, loud);

    private static void SplashSound(Vector3 at, float loud)
    {
        if (InkClock.Now < _nextSound) return;
        _nextSound = InkClock.Now + 0.12f;
        if (_clips == null && InkClock.Now - _lookedForClips > 20f)
        {
            _lookedForClips = InkClock.Now;
            _clips = FindClips();
        }

        if (_clips == null || _clips.Length == 0) return;
        if (_audio == null)
        {
            var go = new GameObject("InkVR_InkPoolAudio");
            Object.DontDestroyOnLoad(go);
            _audio = go.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
        }

        if (_audio.outputAudioMixerGroup == null)
            foreach (var s in GameManager.Instance.Player.GetComponentsInChildren<AudioSource>(true))
                if (s.outputAudioMixerGroup != null)
                {
                    _audio.outputAudioMixerGroup = s.outputAudioMixerGroup;
                    break;
                }

        _audio.minDistance = 1f * _scale;
        _audio.maxDistance = 15f * _scale;
        _audio.transform.position = at;
        _audio.pitch = Rand(0.9f, 1.12f);
        _audio.PlayOneShot(_clips[Rng.Next(_clips.Length)], 0.35f + 0.65f * loud);
    }

    private static AudioClip[] FindClips()
    {
        var found = new List<AudioClip>();
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<AudioClip>()))
        {
            var c = o.TryCast<AudioClip>();
            if (c == null) continue;
            foreach (var n in ClipNames)
                if (c.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found.Add(c);
                    break;
                }
        }

        Plugin.L.LogInfo($"Ink pool sounds: {(found.Count == 0 ? "none loaded" : string.Join(", ", found.ConvertAll(c => c.name)))}");
        return found.Count > 0 ? found.ToArray() : null;
    }

    public static void ForgetSounds()
    {
        _clips = null;
        _lookedForClips = -100f;
    }

    public static void PrefetchSounds()
    {
        _lookedForClips = InkClock.Now;
        _clips = FindClips();
    }
}
