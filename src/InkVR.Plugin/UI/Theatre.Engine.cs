using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using SV = System.Numerics.Vector3;

namespace InkVR.UI;

/// <summary>
/// The theatre's light, baked once at startup on worker threads (plain .NET maths, never Unity's): every lamp's light
/// with soft shadows cast by the theatre's own boxes and walls, ambient occlusion in corners, under seats and along
/// skirting, an ambient level per room, and each room lit only by its own lamps. The result goes into the vertex
/// colours (the theatre's surfaces are unlit, so they cost almost nothing to draw).
///
/// Two layers stay live in the hall: the house lights (sconces, chandelier and house lamps) dim and rise, and the light
/// from the screen follows the picture on it. Each is a darkening pass over the hall drawn with the hall's own
/// geometry (dst * (1 - share * (1 - level))), so the textures underneath keep all their detail.
/// </summary>
public sealed partial class Theatre
{
    public enum Room
    {
        Hall,
        Lobby,
        Backstage,
        Dressing,
        Basement,
        Booth,
        Arcade,
        Office
    }

    public const int RoomCount = 8;

    private enum LampKind
    {
        Other,
        House,
        Screen
    }

    private LampKind _lampKind = LampKind.Other;

    // =====================================================================================================
    // Rooms
    // =====================================================================================================

    /// <summary>
    /// Which room a surface (or a lamp, with no normal) is in. A surface on a dividing wall belongs to the side it faces.
    /// Pure arithmetic, so the bake's worker threads can call it.
    /// </summary>
    public static Room RoomOf(float x, float y, float z, float nx, float ny, float nz)
    {
        // the projection booth hangs over the lobby doors, behind the hall's back wall
        if (z < BackZ - 0.2f && z > ProjBackZ - 0.06f && x > ProjX0 - 0.06f && x < ProjX1 + 0.06f && y > ProjFloor - 0.08f && y < ProjCeiling + 0.06f) return Room.Booth;
        if (z < BackZ - 0.12f || (z < BackZ - 0.01f && nz < -0.5f))
        {
            if (x > LobbyHalf + 0.12f || (x > LobbyHalf + 0.01f && nx > 0.5f)) return Room.Arcade;
            if (x < -LobbyHalf - 0.12f || (x < -LobbyHalf - 0.01f && nx < -0.5f)) return Room.Office;
            return Room.Lobby;
        }

        if (z > BackstageZ - 0.05f && y < BasementTop + 0.05f) return Room.Basement;
        if (x > DressingX - 0.05f && z > DressingZ - 0.05f && (x > DressingX + 0.04f || nx > 0.5f) && (z > DressingZ + 0.04f || nz > 0.5f)) return Room.Dressing;
        if (z > BackstageZ || (z > ProsZ + 0.02f && (System.Math.Abs(x) > OpenHalf + 0.06f || y > OpenTop + 0.4f)) || (z > ProsZ - 0.01f && nz > 0.5f && System.Math.Abs(x) > OpenHalf)) return Room.Backstage;
        return Room.Hall;
    }

    public static Room RoomOf(Vector3 p, Vector3 n) => RoomOf(p.x, p.y, p.z, n.x, n.y, n.z);

    private static readonly SV[] RoomAmbient =
    {
        new(0.16f, 0.13f, 0.1f), // the hall (less of it up in the dark under the ceiling)
        new(0.13f, 0.105f, 0.08f),
        new(0.085f, 0.07f, 0.058f),
        new(0.13f, 0.105f, 0.09f),
        new(0.055f, 0.05f, 0.048f),
        new(0.1f, 0.08f, 0.06f),
        new(0.12f, 0.095f, 0.1f),
        new(0.11f, 0.09f, 0.07f)
    };

    // =====================================================================================================
    // What the bake is asked for
    // =====================================================================================================

    private struct LightReq
    {
        public SV P, N;
        public float R, G, B, A, Ao;
        public byte Mode; // 0 lit, 1 unlit (the colour as given), 2 the same as another vertex's (Ref)
        public int Ref;
    }

    private static LightReq Req(Vector3 p, Vector3 n, Color tint, float ao) => new() { P = new SV(p.x, p.y, p.z), N = new SV(n.x, n.y, n.z), R = tint.r, G = tint.g, B = tint.b, A = tint.a, Ao = ao };

    private static void AddLit(Part p, Vector3 pos, Vector3 n, Color tint, float ao) => p.L.Add(Req(pos, n, tint, ao));

    private static void AddUnlit(Part p, Color c) => p.L.Add(new LightReq { R = c.r, G = c.g, B = c.b, A = c.a, Mode = 1 });

    private static void AddSame(Part p, int index) => p.L.Add(new LightReq { Mode = 2, Ref = index });

    // =====================================================================================================
    // Occluders: the boxes and walls that cast shadows and darken corners
    // =====================================================================================================

    private readonly List<SV> _occMin = new(), _occMax = new();

    /// <summary>Nothing built inside this casts shadows (glass, glows, paper laid flat on things).</summary>
    private bool _noShadows;

    private void Occlude(Vector3 center, Vector3 half)
    {
        if (_noShadows || _group != null) return; // (moving pieces don't: they move)
        _occMin.Add(new SV(center.x - half.x, center.y - half.y, center.z - half.z));
        _occMax.Add(new SV(center.x + half.x, center.y + half.y, center.z + half.z));
    }

    private void OccludeBox(Vector3 center, Vector3 size, Quaternion rot)
    {
        var x = rot * new Vector3(size.x * 0.5f, 0, 0);
        var y = rot * new Vector3(0, size.y * 0.5f, 0);
        var z = rot * new Vector3(0, 0, size.z * 0.5f);
        var half = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        var big = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        var small = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
        if (big < 0.12f || small < 0.012f) return;
        // a box turned well off the axes would shadow far more than itself: shrink it towards its true size
        var turned = Mathf.Abs(Quaternion.Angle(rot, Quaternion.identity) % 90f);
        if (turned > 20f && turned < 70f) half *= 0.75f;
        Occlude(center, half);
    }

    private void OccludePatch(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
    {
        if (Mathf.Max(Mathf.Abs(n.x), Mathf.Max(Mathf.Abs(n.y), Mathf.Abs(n.z))) < 0.97f) return; // only walls, floors and ceilings square to the axes
        var min = Vector3.Min(Vector3.Min(a, b), Vector3.Min(c, d));
        var max = Vector3.Max(Vector3.Max(a, b), Vector3.Max(c, d));
        var size = max - min;
        var area = Mathf.Abs(n.x) > 0.9f ? size.y * size.z : Mathf.Abs(n.y) > 0.9f ? size.x * size.z : size.x * size.y;
        if (area < 0.25f) return;
        var t = new Vector3(Mathf.Abs(n.x) > 0.9f ? 0.012f : 0, Mathf.Abs(n.y) > 0.9f ? 0.012f : 0, Mathf.Abs(n.z) > 0.9f ? 0.012f : 0);
        Occlude((min + max) * 0.5f - n * 0.012f, size * 0.5f + t);
    }

    // a grid over everything, each cell listing the occluders that touch it
    private const float Cell = 0.6f;
    private SV _gridMin;
    private int _gx, _gy, _gz;
    private int[] _cellStart, _cellItems;
    private SV[] _bMin, _bMax;

    private void BuildGrid()
    {
        _bMin = _occMin.ToArray();
        _bMax = _occMax.ToArray();
        if (_bMin.Length == 0)
        {
            _gx = _gy = _gz = 0;
            return;
        }

        var lo = _bMin[0];
        var hi = _bMax[0];
        for (var i = 1; i < _bMin.Length; i++)
        {
            lo = SV.Min(lo, _bMin[i]);
            hi = SV.Max(hi, _bMax[i]);
        }

        _gridMin = lo - new SV(0.01f);
        var span = hi - _gridMin + new SV(0.02f);
        _gx = System.Math.Max(1, (int)System.MathF.Ceiling(span.X / Cell));
        _gy = System.Math.Max(1, (int)System.MathF.Ceiling(span.Y / Cell));
        _gz = System.Math.Max(1, (int)System.MathF.Ceiling(span.Z / Cell));
        var counts = new int[_gx * _gy * _gz + 1];
        void Cells(int i, System.Action<int> each)
        {
            var a = (_bMin[i] - _gridMin) / Cell;
            var b = (_bMax[i] - _gridMin) / Cell;
            int x0 = Clamp((int)a.X, _gx), x1 = Clamp((int)b.X, _gx), y0 = Clamp((int)a.Y, _gy), y1 = Clamp((int)b.Y, _gy), z0 = Clamp((int)a.Z, _gz), z1 = Clamp((int)b.Z, _gz);
            for (var z = z0; z <= z1; z++)
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
                each((z * _gy + y) * _gx + x);
        }

        for (var i = 0; i < _bMin.Length; i++) Cells(i, c => counts[c + 1]++);
        for (var c = 1; c < counts.Length; c++) counts[c] += counts[c - 1];
        _cellStart = (int[])counts.Clone();
        _cellItems = new int[counts[counts.Length - 1]];
        var fill = (int[])counts.Clone();
        for (var i = 0; i < _bMin.Length; i++)
        {
            var item = i;
            Cells(i, c => _cellItems[fill[c]++] = item);
        }
    }

    private static int Clamp(int v, int n) => v < 0 ? 0 : v >= n ? n - 1 : v;

    private sealed class RayScratch
    {
        public int[] Stamp;
        public int Ray;
    }

    /// <summary>Is anything between o and o + d * maxT? (Boxes holding either end don't count: a lamp in its own fitting.)</summary>
    private bool Blocked(SV o, SV d, float maxT, SV end, RayScratch s)
    {
        if (_gx == 0) return false;
        s.Ray++;
        var p = (o - _gridMin) / Cell;
        int x = (int)System.MathF.Floor(p.X), y = (int)System.MathF.Floor(p.Y), z = (int)System.MathF.Floor(p.Z);
        int sx = d.X >= 0 ? 1 : -1, sy = d.Y >= 0 ? 1 : -1, sz = d.Z >= 0 ? 1 : -1;
        float Next(float pos, float dir, int cell) => dir > 0 ? (cell + 1 - pos) * Cell / dir : dir < 0 ? (pos - cell) * Cell / -dir : float.MaxValue;
        float Delta(float dir) => dir != 0 ? Cell / System.Math.Abs(dir) : float.MaxValue;
        float tx = Next(p.X, d.X, x), ty = Next(p.Y, d.Y, y), tz = Next(p.Z, d.Z, z);
        float dx = Delta(d.X), dy = Delta(d.Y), dz = Delta(d.Z);
        var t = 0f;
        var guard = 0;
        while (t <= maxT && guard++ < 400)
        {
            if (x >= 0 && y >= 0 && z >= 0 && x < _gx && y < _gy && z < _gz)
            {
                var c = (z * _gy + y) * _gx + x;
                for (var k = _cellStart[c]; k < _cellStart[c + 1]; k++)
                {
                    var i = _cellItems[k];
                    if (s.Stamp[i] == s.Ray) continue;
                    s.Stamp[i] = s.Ray;
                    if (Hits(_bMin[i], _bMax[i], o, d, maxT) && !Inside(_bMin[i], _bMax[i], o, 0.004f) && !Inside(_bMin[i], _bMax[i], end, 0.04f)) return true;
                }
            }
            else if ((x < 0 && sx < 0) || (y < 0 && sy < 0) || (z < 0 && sz < 0) || (x >= _gx && sx > 0) || (y >= _gy && sy > 0) || (z >= _gz && sz > 0)) return false;

            if (tx < ty && tx < tz)
            {
                t = tx;
                tx += dx;
                x += sx;
            }
            else if (ty < tz)
            {
                t = ty;
                ty += dy;
                y += sy;
            }
            else
            {
                t = tz;
                tz += dz;
                z += sz;
            }
        }

        return false;
    }

    private static bool Inside(SV min, SV max, SV p, float pad) =>
        p.X > min.X - pad && p.X < max.X + pad && p.Y > min.Y - pad && p.Y < max.Y + pad && p.Z > min.Z - pad && p.Z < max.Z + pad;

    private static bool Hits(SV min, SV max, SV o, SV d, float maxT)
    {
        float t0 = 0.0005f, t1 = maxT;
        for (var a = 0; a < 3; a++)
        {
            var oa = a == 0 ? o.X : a == 1 ? o.Y : o.Z;
            var da = a == 0 ? d.X : a == 1 ? d.Y : d.Z;
            var lo = a == 0 ? min.X : a == 1 ? min.Y : min.Z;
            var hi = a == 0 ? max.X : a == 1 ? max.Y : max.Z;
            if (System.Math.Abs(da) < 1e-8f)
            {
                if (oa < lo || oa > hi) return false;
                continue;
            }

            var inv = 1f / da;
            var ta = (lo - oa) * inv;
            var tb = (hi - oa) * inv;
            if (ta > tb) (ta, tb) = (tb, ta);
            if (ta > t0) t0 = ta;
            if (tb < t1) t1 = tb;
            if (t0 > t1) return false;
        }

        return true;
    }

    // =====================================================================================================
    // The bake
    // =====================================================================================================

    private struct BLamp
    {
        public SV P, Col, Aim;
        public float Range, Spread, Reach2, Size;
        public int Room, Kind;
        public bool Under;
    }

    private BLamp[] _bLamps;
    private bool _bakeNoAo, _bakeNoShadows; // dev: INKVR_BAKE=noao,noshadow to compare

    // per vertex: the light that came from the house lights and from the screen, as a share of all of it
    private float[] _houseShare, _screenShare;

    private static readonly SV[] Hemisphere = MakeHemisphere(12);

    private static SV[] MakeHemisphere(int n)
    {
        // cosine-weighted directions round +z (a golden-angle spiral)
        var dirs = new SV[n];
        for (var i = 0; i < n; i++)
        {
            var u = (i + 0.5f) / n;
            var r = System.MathF.Sqrt(u);
            var a = i * 2.39996323f;
            dirs[i] = new SV(r * System.MathF.Cos(a), r * System.MathF.Sin(a), System.MathF.Sqrt(1 - u));
        }

        return dirs;
    }

    private static readonly SV[] LampJitter = { new(0.6f, 0.3f, -0.4f), new(-0.5f, 0.55f, 0.3f), new(0.1f, -0.6f, 0.5f) };

    /// <summary>Light every part's vertices (the slow part of building the theatre, spread over every core).</summary>
    private void Bake()
    {
        var t0 = Time.realtimeSinceStartup;
        var flags = System.Environment.GetEnvironmentVariable("INKVR_BAKE") ?? "";
        _bakeNoAo = flags.Contains("noao");
        _bakeNoShadows = flags.Contains("noshadow");
        BuildGrid();
        _bLamps = new BLamp[_lamps.Count];
        for (var i = 0; i < _lamps.Count; i++)
        {
            var l = _lamps[i];
            _bLamps[i] = new BLamp
            {
                P = new SV(l.Pos.x, l.Pos.y, l.Pos.z), Col = new SV(l.Color.r, l.Color.g, l.Color.b), Aim = new SV(l.Aim.x, l.Aim.y, l.Aim.z), Range = l.Range, Spread = l.Spread, Reach2 = l.Reach2,
                Size = Mathf.Clamp(l.Range * 0.08f, 0.04f, 0.25f), Room = (int)RoomOf(l.Pos, Vector3.zero), Kind = (int)l.Kind, Under = l.Under
            };
        }

        // every request, flat, so the threads can share them out
        var parts = new List<Part>();
        var starts = new List<int>();
        var total = 0;
        foreach (var p in _parts)
        {
            parts.Add(p);
            starts.Add(total);
            total += p.L.Count;
        }

        var reqs = new LightReq[total];
        for (var k = 0; k < parts.Count; k++) parts[k].L.CopyTo(reqs, starts[k]);
        var outR = new float[total];
        var outG = new float[total];
        var outB = new float[total];
        _houseShare = new float[total];
        _screenShare = new float[total];
        var occluders = _bMin.Length;
        var threadScratch = new System.Threading.ThreadLocal<RayScratch>(() => new RayScratch { Stamp = new int[System.Math.Max(1, occluders)] });

        Parallel.For(0, (total + 255) / 256, chunk =>
        {
            var s = threadScratch.Value;
            var end = System.Math.Min(total, chunk * 256 + 256);
            for (var i = chunk * 256; i < end; i++)
            {
                ref var q = ref reqs[i];
                if (q.Mode != 0) continue;
                Shade(ref q, s, out outR[i], out outG[i], out outB[i], out _houseShare[i], out _screenShare[i]);
            }
        });

        // write the colours back (copies resolved within each part)
        for (var k = 0; k < parts.Count; k++)
        {
            var p = parts[k];
            var b = starts[k];
            p.C.Clear();
            for (var j = 0; j < p.L.Count; j++)
            {
                var q = p.L[j];
                var i = b + j;
                if (q.Mode == 2)
                {
                    var src = b + q.Ref;
                    var sq = reqs[src];
                    i = src;
                    q.A = sq.A;
                    q.Mode = sq.Mode;
                    q.R = sq.R;
                    q.G = sq.G;
                    q.B = sq.B;
                    _houseShare[b + j] = _houseShare[src];
                    _screenShare[b + j] = _screenShare[src];
                }

                p.C.Add(q.Mode == 1 ? new Color(q.R, q.G, q.B, q.A) : new Color(outR[i], outG[i], outB[i], q.A));
            }

            p.ShareStart = b;
        }

        threadScratch.Dispose();
        Plugin.L.LogInfo($"Theatre light baked: {total} vertices, {_lamps.Count} lamps, {occluders} shadow casters on a {_gx}x{_gy}x{_gz} grid ({(Time.realtimeSinceStartup - t0) * 1000f:F0} ms)");
    }

    private void Shade(ref LightReq q, RayScratch s, out float r, out float g, out float b, out float house, out float screen)
    {
        var p = q.P;
        var n = q.N;
        var room = (int)RoomOf(p.X, p.Y, p.Z, n.X, n.Y, n.Z);
        var amb = RoomAmbient[room];
        if (room == (int)Room.Hall)
        {
            var h = 1f - 0.45f * System.Math.Clamp((p.Y - 3f) / (CeilingY - 3f), 0f, 1f);
            amb *= h;
        }

        var under = p.Z < BalconyZ - 0.02f && p.Y < SoffitY + 0.02f && room == (int)Room.Hall;
        var origin = p + n * 0.025f;

        // ambient occlusion: how much of the hemisphere above the surface is closed in nearby
        var ao = 1f;
        if (n.LengthSquared() > 0.5f && !_bakeNoAo)
        {
            var tangent = System.Math.Abs(n.Y) < 0.9f ? SV.Normalize(SV.Cross(n, SV.UnitY)) : SV.Normalize(SV.Cross(n, SV.UnitX));
            var bit = SV.Cross(n, tangent);
            var spin = (System.Math.Abs(p.X * 12.9898f + p.Y * 78.233f + p.Z * 37.719f) % 6.2831f);
            float cs = System.MathF.Cos(spin), sn = System.MathF.Sin(spin);
            var closed = 0f;
            foreach (var h in Hemisphere)
            {
                var hx = h.X * cs - h.Y * sn;
                var hy = h.X * sn + h.Y * cs;
                var dir = tangent * hx + bit * hy + n * h.Z;
                if (Blocked(origin, dir, 0.9f, origin + dir * 0.9f, s)) closed += 1f;
            }

            ao = 1f - 0.55f * closed / Hemisphere.Length;
        }

        var lit = amb * ao;
        var fromHouse = SV.Zero;
        var fromScreen = SV.Zero;
        for (var i = 0; i < _bLamps.Length; i++)
        {
            ref var l = ref _bLamps[i];
            if (l.Room != room) continue;
            var d = l.P - p;
            var d2 = d.LengthSquared();
            if (d2 > l.Reach2) continue;
            var dist = System.MathF.Sqrt(d2);
            var dir = d / System.Math.Max(dist, 1e-4f);
            var ndl = n.LengthSquared() > 0.5f ? System.Math.Clamp((SV.Dot(n, dir) + 0.3f) / 1.3f, 0f, 1f) : 1f;
            if (ndl <= 0) continue;
            var att = 1f / (1f + d2 / (l.Range * l.Range)) * System.Math.Clamp(2.5f - dist / (l.Range * 2f), 0f, 1f);
            if (l.Spread > 0) att *= 1f - l.Spread + l.Spread * System.Math.Clamp(SV.Dot(-dir, l.Aim), 0f, 1f);
            if (under != l.Under) att *= 0.25f;
            var k = att * ndl;
            if (k < 0.004f) continue;

            // soft shadows: a few points on the lamp
            var seen = _bakeNoShadows ? LampJitter.Length : 0f;
            for (var j = 0; j < LampJitter.Length && !_bakeNoShadows; j++)
            {
                var target = l.P + LampJitter[j] * l.Size;
                var to = target - origin;
                var len = to.Length();
                if (!Blocked(origin, to / len, len - 0.05f, target, s)) seen += 1f;
            }

            k *= 0.15f + 0.85f * seen / LampJitter.Length;
            k *= 0.45f + 0.55f * ao;
            var c = l.Col * k;
            lit += c;
            if (l.Kind == (int)LampKind.House) fromHouse += c;
            else if (l.Kind == (int)LampKind.Screen) fromScreen += c;
        }

        var total = lit.X + lit.Y + lit.Z;
        house = total > 1e-4f ? (fromHouse.X + fromHouse.Y + fromHouse.Z) / total : 0f;
        screen = total > 1e-4f ? (fromScreen.X + fromScreen.Y + fromScreen.Z) / total : 0f;
        r = System.Math.Clamp(q.R * lit.X * q.Ao, 0f, 1f);
        g = System.Math.Clamp(q.G * lit.Y * q.Ao, 0f, 1f);
        b = System.Math.Clamp(q.B * lit.Z * q.Ao, 0f, 1f);
    }

    // =====================================================================================================
    // The live layers: house lights and the screen's light; whole rooms' tints
    // =====================================================================================================

    private readonly List<Material>[] _roomMaterials = MakeRoomLists();
    private Material _houseMat, _screenMat;
    private float _houseLevel = 1f;

    /// <summary>The house lights' level now (0..1).</summary>
    public float HouseLevel => _houseLevel;

    private static List<Material>[] MakeRoomLists()
    {
        var a = new List<Material>[RoomCount];
        for (var i = 0; i < a.Length; i++) a[i] = new List<Material>();
        return a;
    }

    /// <summary>A darkening pass over a hall part: share of the light (in the vertex colour) times (1 - level).</summary>
    private void AddDimmer(Part p, Mesh baseMesh, float[] share, ref Material mat, string name, int queue)
    {
        var any = false;
        var colors = new Color[p.V.Count];
        for (var i = 0; i < colors.Length; i++)
        {
            var f = share[p.ShareStart + i];
            if (f > 0.02f) any = true;
            colors[i] = new Color(f, f, f, 1f);
        }

        if (!any) return;
        if (mat == null)
        {
            mat = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "InkVR_Theatre_" + name, renderQueue = queue };
            mat.SetInt("_SrcBlend", (int)BlendMode.Zero);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcColor);
            mat.SetInt("_Cull", (int)CullMode.Off);
            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            mat.SetColor("_Color", Color.black); // (full light: nothing taken away)
            Keep(mat);
        }

        var mesh = new Mesh { name = "InkVR_Theatre_" + p.Name + "_" + name, indexFormat = baseMesh.indexFormat };
        mesh.SetVertices(baseMesh.vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(baseMesh.triangles, 0);
        mesh.RecalculateBounds();
        Keep(mesh);
        var go = new GameObject("InkVR_Theatre_" + p.Name + "_" + name) { layer = VRLayers.UI };
        go.transform.SetParent(_root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    /// <summary>The house lights, 0 (out) to 1 (up).</summary>
    public void SetHouseLights(float level)
    {
        _houseLevel = Mathf.Clamp01(level);
        if (_houseMat == null) return;
        var k = 1f - Mathf.Clamp01(level);
        _houseMat.SetColor("_Color", new Color(k, k, k, 1f));
    }

    /// <summary>The light from the screen: how bright the picture is (0..1) and its colour.</summary>
    public void SetScreenLight(float level, Color hue)
    {
        if (_screenMat == null) return;
        level = Mathf.Clamp01(level);
        _screenMat.SetColor("_Color", new Color(Mathf.Clamp01(1f - level * hue.r), Mathf.Clamp01(1f - level * hue.g), Mathf.Clamp01(1f - level * hue.b), 1f));
    }

    /// <summary>Tint a whole room (flicker, a power cut, a cosy glow): every surface's colour times this.</summary>
    public void SetRoomTint(Room room, Color tint)
    {
        foreach (var m in _roomMaterials[(int)room])
            if (m != null)
                m.color = tint;
    }
}
