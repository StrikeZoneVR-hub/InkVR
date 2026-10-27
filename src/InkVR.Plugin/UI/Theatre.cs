using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InkVR.UI;

/// <summary>
/// The 1930s picture palace the cinema screen hangs in: a stage with velvet drapes and footlights under a
/// gilded proscenium (Bendy's face in the medallion, organ grilles either side), rows of velvet seats
/// stepping up behind you, sconce-lit walls with framed cartoon posters and chapter lobby cards, a balcony
/// with a marquee and the projection booth at the back, and a coffered ceiling with a chandelier.
///
/// Everything is built from code at startup and painted in the game's ink style. The lighting is baked
/// into the vertex colours (no realtime lights, so it costs almost nothing to draw): each surface is lit
/// by the screen, the sconces, the footlights, the chandelier and the little lamps over the posters.
/// Opaque parts draw a depth-only pass first, then their colour with an equal-depth test.
/// </summary>
public sealed partial class Theatre
{
    // ---- layout, in metres: +z towards the screen, +x to the right; (0, 0, 0) is where you stand, in the centre aisle ----

    public const float ScreenZ = 6.0f, ScreenWidth = 6.6f, ScreenCenterY = 2.32f;

    /// <summary>The projector lens in the booth window at the back.</summary>
    public static readonly Vector3 Projector = new(0f, 8.6f, -13.9f);

    private const float Half = 9f, BackZ = -14f, CeilingY = 11f, ProsZ = 5f, CorniceY = 9.6f;
    private const float OpenHalf = 5.4f, OpenTop = 6.4f, StageY = 0.4f, ApronZ = 4.2f, ApronHalf = 6.2f;
    private const float RowGap = 0.95f, FirstRowZ = 3.0f, RakeFromZ = -1.3f, Riser = 0.12f;
    private const int Rows = 15, SeatsPerSide = 12, BalconyRows = 5;
    private const float AisleHalf = 1.0f, SeatW = 0.56f, SeatStart = 1.1f;
    // Layers laid over another surface stand at least this far off it: the headset's depth can't tell 5 mm apart
    // at the far end of the theatre, and the two flicker in and out of each other as you move.
    private const float FriezeOut = 0.28f; // the arch's frieze: 4 cm clear of the beam's face (z - 0.24)
    private const float BalconyZ = -7.5f, BalconyY = 4.2f, SoffitY = 3.9f, BalconyRise = 0.38f, ParapetTop = 5.15f;
    private static readonly float[] PilasterZ = { 3.7f, 0.25f, -3.2f, -6.65f, -10.1f, -13.55f };

    // ---- palette (multiplied by the baked light) ----

    private static readonly Color Velvet = new(0.66f, 0.17f, 0.09f);
    private static readonly Color VelvetDeep = new(0.52f, 0.11f, 0.06f);
    private static readonly Color Wood = new(0.66f, 0.45f, 0.26f);
    private static readonly Color WoodDark = new(0.38f, 0.24f, 0.13f);
    private static readonly Color Iron = new(0.22f, 0.17f, 0.13f);
    private static readonly Color Gold = new(0.96f, 0.74f, 0.38f);
    private static readonly Color GoldDark = new(0.66f, 0.47f, 0.24f);
    private static readonly Color Paper = new(0.88f, 0.73f, 0.5f);
    private static readonly Color Plaster = new(0.8f, 0.64f, 0.42f);
    private static readonly Color Carpet = new(0.5f, 0.15f, 0.09f);
    private static readonly Color Cream = new(0.97f, 0.88f, 0.66f);
    private static readonly Color Dark = new(0.08f, 0.055f, 0.04f);

    public struct SignSpec
    {
        public string Text;
        public Vector3 Pos;
        public Quaternion Rot;
        public float Height, Width;
        public Color Color;

        /// <summary>The moving piece it's fixed to (null: the theatre), and a name to find it by.</summary>
        public string Group, Id;
    }

    /// <summary>Lettering for <see cref="Cinema"/> to set in the game's own font.</summary>
    public readonly List<SignSpec> Signs = new();

    private readonly Transform _root;

    public Theatre(Transform root)
    {
        _root = root;
        var t0 = Time.realtimeSinceStartup;
        var timing = new System.Text.StringBuilder();
        var last = t0;
        void Step(string name, System.Action build)
        {
            build();
            var now = Time.realtimeSinceStartup;
            timing.Append($" {name} {(now - last) * 1000f:F0}");
            last = now;
        }

        Step("art", LoadArt);
        Step("lamps", PlanLamps);
        Step("floor", BuildFloor);
        Step("seats", BuildSeats);
        Step("walls", BuildWalls);
        Step("proscenium", BuildProscenium);
        Step("stage", BuildStage);
        Step("drapes", BuildCurtains);
        Step("ceiling", BuildCeiling);
        Step("chandelier", BuildChandelier);
        Step("balcony", BuildBalcony);
        Step("back", BuildBackWall);
        Step("posters", BuildPosters);
        Step("lobby", BuildLobby);
        Step("ink", BuildInk);
        Step("meshes", Finish);
        Step("glows", BuildGlows);
        Step("motes", BuildMotes);
        Plugin.L.LogInfo("Theatre timing (ms):" + timing);
        var verts = 0;
        foreach (var p in _parts) verts += p.V.Count;
        Plugin.L.LogInfo($"Theatre built: {verts} vertices, {_lamps.Count} lamps, {_glows.Count} glows, art {(_bendyFace != null ? "from your game" : "painted stand-ins")} ({(Time.realtimeSinceStartup - t0) * 1000f:F0} ms)");
    }

    // =====================================================================================================
    // Lighting (baked into vertex colours)
    // =====================================================================================================

    private struct Lamp
    {
        public Vector3 Pos;
        public Color Color;
        public float Range;
        public Vector3 Aim;
        public float Spread;
        public bool Under, Lobby;
        public float Reach2;
    }

    private readonly List<Lamp> _lamps = new();
    private Lamp[] _lampArray;

    private void AddLamp(Vector3 pos, Color color, float range, Vector3 aim = default, float spread = 0f)
    {
        _lamps.Add(new Lamp { Pos = pos, Color = color, Range = range, Aim = aim.normalized, Spread = spread, Under = UnderBalcony(pos), Lobby = InLobby(pos, Vector3.zero), Reach2 = range * range * 25f });
        _lampArray = _lamps.ToArray();
    }

    private static bool UnderBalcony(Vector3 p) => p.z < BalconyZ - 0.02f && p.y < SoffitY + 0.02f;

    private Color Lit(Vector3 p, Vector3 n, Color tint, float ao)
    {
        var height = Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(3f, CeilingY, p.y));
        var r = 0.16f * height;
        var g = 0.13f * height;
        var b = 0.1f * height;
        var under = UnderBalcony(p);
        var lobby = InLobby(p, n);
        if (lobby)
        {
            // the lobby's a lower room: less of the dark up top
            r = 0.13f;
            g = 0.105f;
            b = 0.08f;
        }

        for (var i = 0; i < _lampArray.Length; i++)
        {
            ref var l = ref _lampArray[i];
            if (l.Lobby != lobby) continue;
            var d = l.Pos - p;
            var d2 = d.sqrMagnitude;
            if (d2 > l.Reach2) continue;
            var dist = Mathf.Sqrt(d2);
            var dir = d / Mathf.Max(dist, 1e-4f);
            var ndl = Mathf.Clamp01((Vector3.Dot(n, dir) + 0.3f) / 1.3f);
            var att = 1f / (1f + dist * dist / (l.Range * l.Range)) * Mathf.Clamp01(2.5f - dist / (l.Range * 2f));
            if (l.Spread > 0) att *= Mathf.Lerp(1f - l.Spread, 1f, Mathf.Clamp01(Vector3.Dot(-dir, l.Aim)));
            if (under != l.Under) att *= 0.25f;
            var k = att * ndl;
            r += l.Color.r * k;
            g += l.Color.g * k;
            b += l.Color.b * k;
        }

        return new Color(Mathf.Clamp01(tint.r * r * ao), Mathf.Clamp01(tint.g * g * ao), Mathf.Clamp01(tint.b * b * ao), tint.a);
    }

    private void PlanLamps()
    {
        // The screen: a broad warm key light, only towards the audience.
        foreach (var x in new[] { -2.2f, 0f, 2.2f })
        foreach (var y in new[] { 1.4f, 3.2f })
            AddLamp(new Vector3(x, y, ScreenZ - 0.3f), new Color(1f, 0.88f, 0.66f) * 0.6f, 4.5f, Vector3.back, 1f);

        // House lights at half: the booth and the open hall behind you, so the seat backs read.
        AddLamp(new Vector3(0, 6.5f, -6f), new Color(1f, 0.8f, 0.52f) * 0.45f, 9f);
        AddLamp(new Vector3(0, 8.2f, -3.5f), new Color(1f, 0.8f, 0.5f) * 1.0f, 4.5f); // chandelier

        // Wall sconces on every pilaster (plus a row at balcony level behind the balcony front).
        foreach (var s in new[] { -1f, 1f })
        foreach (var z in PilasterZ)
        {
            AddLamp(new Vector3(s * (Half - 0.5f), 3.45f, z), new Color(1f, 0.66f, 0.32f) * 1.5f, 1.8f, Vector3.up, 0.5f);
            if (z < BalconyZ) AddLamp(new Vector3(s * (Half - 0.5f), 7.3f, z), new Color(1f, 0.66f, 0.32f) * 1.5f, 1.8f, Vector3.up, 0.5f);
        }

        // Footlights along the apron, lighting the drapes and proscenium from below.
        for (var x = -4.5f; x <= 4.51f; x += 1.8f)
            AddLamp(new Vector3(x, StageY + 0.15f, ApronZ + 0.15f), new Color(1f, 0.76f, 0.42f) * 0.9f, 1.5f, new Vector3(0, 1f, 0.35f), 0.6f);

        // Exit signs, the booth window, the marquee, the lobby doors, the downlights under the balcony.
        foreach (var s in new[] { -1f, 1f })
        {
            AddLamp(new Vector3(s * (Half - 0.3f), 2.95f, 2.0f), new Color(1f, 0.32f, 0.15f) * 0.9f, 1.1f);
            AddLamp(new Vector3(s * 6f, 3.45f, BackZ + 0.3f), new Color(1f, 0.32f, 0.15f) * 0.8f, 1.0f);
        }

        AddLamp(Projector + Vector3.forward * 0.3f, new Color(1f, 0.93f, 0.78f) * 1.2f, 1.6f);
        AddLamp(new Vector3(0, 4.5f, BalconyZ + 0.6f), new Color(1f, 0.8f, 0.45f) * 1.0f, 1.9f);
        AddLamp(new Vector3(0, 2.4f, BackZ + 0.35f), new Color(1f, 0.82f, 0.55f) * 0.9f, 1.6f);
        foreach (var p in new[] { new Vector3(-5f, SoffitY - 0.05f, -9.6f), new Vector3(5f, SoffitY - 0.05f, -9.6f), new Vector3(-1.8f, SoffitY - 0.05f, -12f), new Vector3(1.8f, SoffitY - 0.05f, -12f) })
            AddLamp(p, new Color(1f, 0.8f, 0.5f) * 0.9f, 1.7f, Vector3.down, 0.6f);

        // A little picture lamp over every poster.
        foreach (var slot in PosterSlots())
            AddLamp(slot.Center + slot.Normal * 0.45f + Vector3.up * (slot.Portrait ? 0.95f : 0.75f), new Color(1f, 0.85f, 0.6f) * 1.2f, 0.95f, -slot.Normal + Vector3.down, 0.4f);

        PlanLobbyLamps();
    }

    // =====================================================================================================
    // Mesh parts (one per texture)
    // =====================================================================================================

    private sealed class Part
    {
        public string Group;
        public readonly string Name;
        public readonly Texture Tex;
        public readonly bool Transparent;
        public readonly int Queue;
        public readonly List<Vector3> V = new();
        public readonly List<Vector2> UV = new();
        public readonly List<Color> C = new();
        public readonly List<int> T = new();

        public Part(string name, Texture tex, bool transparent, int queue)
        {
            Name = name;
            Tex = tex;
            Transparent = transparent;
            Queue = queue;
        }
    }

    private readonly List<Part> _parts = new();
    private Part _wood, _box, _fabric, _wall, _carpet, _coffer, _ornament, _grille, _door, _curtain, _posterBg, _ink, _medal;

    private Part NewPart(string name, Texture tex, bool transparent = false, int queue = 2001)
    {
        var p = new Part(name, tex, transparent, queue) { Group = _group };
        _parts.Add(p);
        return p;
    }

    // ---- moving pieces (doors, cans, keys...): their own meshes, under a transform at their pivot ----

    private string _group;
    private readonly Dictionary<string, Vector3> _pivots = new();
    private readonly Dictionary<(Part, string), Part> _groupParts = new();
    private readonly Dictionary<string, Transform> _groupTransforms = new();

    /// <summary>Everything built inside <paramref name="build"/> becomes the piece <paramref name="name"/>, pivoting at <paramref name="pivot"/>.</summary>
    private void InGroup(string name, Vector3 pivot, System.Action build)
    {
        var was = _group;
        _group = name;
        _pivots[name] = pivot;
        build();
        _group = was;
    }

    private Part Route(Part p)
    {
        if (_group == null || p.Group != null) return p;
        if (!_groupParts.TryGetValue((p, _group), out var gp))
        {
            gp = new Part(p.Name + "_" + _group, p.Tex, p.Transparent, p.Queue) { Group = _group };
            _parts.Add(gp);
            _groupParts[(p, _group)] = gp;
        }

        return gp;
    }

    /// <summary>A moving piece's transform (child of the theatre; at rest it sits at its pivot, unrotated).</summary>
    public Transform Piece(string name) => _groupTransforms.TryGetValue(name, out var t) ? t : null;

    public Vector3 PivotOf(string name) => _pivots.TryGetValue(name, out var p) ? p : Vector3.zero;

    private Transform GroupTransform(string name)
    {
        if (_groupTransforms.TryGetValue(name, out var t)) return t;
        var go = new GameObject("InkVR_Theatre_" + name) { layer = VRLayers.UI };
        go.transform.SetParent(_root, false);
        go.transform.localPosition = PivotOf(name);
        _groupTransforms[name] = go.transform;
        return go.transform;
    }

    /// <summary>Where a piece's vertices start and end in its part (for pieces animated vertex by vertex, like the keys).</summary>
    private int VertexCount(Part p) => Route(p).V.Count;

    private static int Sub(float len, float step = 0.9f) => Mathf.Clamp(Mathf.CeilToInt(len / step - 0.01f), 1, 48);

    /// <summary>
    /// A quad patch: a-b along the bottom, d-c along the top (as seen from the front), subdivided for lighting.
    /// UVs run 0..1 over the patch (the textures' ink borders outline it), or tile every tileU/tileV metres.
    /// </summary>
    private void Patch(Part p, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color tint, int su = 1, int sv = 1,
        float tileU = 0, float tileV = 0, float ao = 1f, bool unlit = false, Vector4? uvRect = null, bool flat = false)
    {
        p = Route(p);
        su = Mathf.Max(1, su);
        sv = Mathf.Max(1, sv);
        var flatColor = flat && !unlit ? Lit((a + b + c + d) * 0.25f, n, tint, ao) : tint;
        var ua = (b - a).normalized;
        var va = (d - a).normalized;
        var start = p.V.Count;
        for (var j = 0; j <= sv; j++)
        for (var i = 0; i <= su; i++)
        {
            var fu = (float)i / su;
            var fv = (float)j / sv;
            var pos = Vector3.Lerp(Vector3.Lerp(a, b, fu), Vector3.Lerp(d, c, fu), fv);
            float u, v;
            if (uvRect.HasValue)
            {
                var r = uvRect.Value;
                u = Mathf.Lerp(r.x, r.z, fu);
                v = Mathf.Lerp(r.y, r.w, fv);
            }
            else
            {
                u = tileU > 0 ? Vector3.Dot(pos, ua) / tileU : fu;
                v = tileV > 0 ? Vector3.Dot(pos, va) / tileV : fv;
            }

            p.V.Add(pos);
            p.UV.Add(new Vector2(u, v));
            p.C.Add(unlit || flat ? flatColor : Lit(pos, n, tint, ao));
        }

        for (var j = 0; j < sv; j++)
        for (var i = 0; i < su; i++)
        {
            var k = start + j * (su + 1) + i;
            p.T.Add(k);
            p.T.Add(k + su + 1);
            p.T.Add(k + su + 2);
            p.T.Add(k);
            p.T.Add(k + su + 2);
            p.T.Add(k + 1);
        }
    }

    /// <summary>A box; each face carries the whole texture, so its ink border outlines every edge.</summary>
    private void Box(Part p, Vector3 center, Vector3 size, Quaternion rot, Color tint, float ao = 1f, bool bottom = false, bool unlit = false)
    {
        var x = rot * new Vector3(size.x, 0, 0);
        var y = rot * new Vector3(0, size.y, 0);
        var z = rot * new Vector3(0, 0, size.z);
        var o = center - (x + y + z) * 0.5f;
        var nx = rot * Vector3.right;
        var ny = rot * Vector3.up;
        var nz = rot * Vector3.forward;
        int sx = Sub(size.x), sy = Sub(size.y), sz = Sub(size.z);
        var flat = size.x < 0.8f && size.y < 0.8f && size.z < 0.8f; // small parts (seats, trim): light each face once
        Patch(p, o + x, o + x + z, o + x + z + y, o + x + y, nx, tint, sz, sy, ao: ao, unlit: unlit, flat: flat);
        Patch(p, o + z, o, o + y, o + z + y, -nx, tint, sz, sy, ao: ao, unlit: unlit, flat: flat);
        Patch(p, o + z + x, o + z, o + z + y, o + z + x + y, nz, tint, sx, sy, ao: ao, unlit: unlit, flat: flat);
        Patch(p, o, o + x, o + x + y, o + y, -nz, tint, sx, sy, ao: ao, unlit: unlit, flat: flat);
        Patch(p, o + y, o + y + x, o + y + x + z, o + y + z, ny, tint, sx, sz, ao: ao, unlit: unlit, flat: flat);
        if (bottom) Patch(p, o, o + x, o + x + z, o + z, -ny, tint, sx, sz, ao: ao, unlit: unlit, flat: flat);
    }

    private void Box(Part p, Vector3 center, Vector3 size, Color tint, float ao = 1f, bool bottom = false) =>
        Box(p, center, size, Quaternion.identity, tint, ao, bottom);

    /// <summary>An ornament strip from start to end, width wide towards across; the frieze tiles along its length.</summary>
    private void Band(Part p, Vector3 start, Vector3 end, Vector3 across, float width, Vector3 n, Color tint, float tile, float ao = 1f)
    {
        var len = (end - start).magnitude;
        var w = across.normalized * width;
        Patch(p, start, end, end + w, start + w, n, tint, Sub(len, 0.7f), 1, ao: ao, uvRect: new Vector4(0, 0, len / tile, 1));
    }

    /// <summary>A horizontal ring (tube) around a vertical axis.</summary>
    private void Ring(Part p, Vector3 center, float radius, float tube, Color tint, int seg = 28, int sides = 6, float ao = 1f)
    {
        p = Route(p);
        var start = p.V.Count;
        for (var i = 0; i <= seg; i++)
        {
            var a = (float)i / seg * Mathf.PI * 2;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            for (var s = 0; s <= sides; s++)
            {
                var b = (float)s / sides * Mathf.PI * 2;
                var n = dir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                var pos = center + dir * radius + n * tube;
                p.V.Add(pos);
                p.UV.Add(new Vector2((float)i / seg * 6, (float)s / sides));
                p.C.Add(Lit(pos, n, tint, ao));
            }
        }

        Grid(p, start, seg, sides);
    }

    /// <summary>A surface of revolution around a vertical axis; profile points are (radius, height).</summary>
    private void Lathe(Part p, Vector3 center, Vector2[] profile, Color tint, int sides = 20, bool unlit = false, float uRepeat = 4f, float vFrom = 0f, float vTo = 1f)
    {
        p = Route(p);
        var start = p.V.Count;
        for (var k = 0; k < profile.Length; k++)
        {
            var prev = profile[Mathf.Max(0, k - 1)];
            var next = profile[Mathf.Min(profile.Length - 1, k + 1)];
            var t = next - prev;
            var nr = new Vector2(t.y, -t.x).normalized;
            if (nr.x < 0) nr = -nr;
            for (var s = 0; s <= sides; s++)
            {
                var a = (float)s / sides * Mathf.PI * 2;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var pos = center + dir * profile[k].x + Vector3.up * profile[k].y;
                var n = (dir * nr.x + Vector3.up * nr.y).normalized;
                p.V.Add(pos);
                p.UV.Add(new Vector2((float)s / sides * uRepeat, Mathf.Lerp(vFrom, vTo, (float)k / (profile.Length - 1))));
                p.C.Add(unlit ? tint : Lit(pos, n, tint, 1f));
            }
        }

        Grid(p, start, profile.Length - 1, sides);
    }

    /// <summary>A flat gilded ring facing the audience (-z).</summary>
    private void Annulus(Part p, Vector3 center, float r0, float r1, Color tint, int seg)
    {
        for (var k = 0; k < seg; k++)
        {
            var a0 = (float)k / seg * Mathf.PI * 2;
            var a1 = (float)(k + 1) / seg * Mathf.PI * 2;
            var d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0);
            var d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0);
            Patch(p, center + d0 * r0, center + d1 * r0, center + d1 * r1, center + d0 * r1, Vector3.back, tint, 1, 1, uvRect: new Vector4(0.3f, 0.02f, 0.7f, 0.98f));
        }
    }

    private static void Grid(Part p, int start, int rows, int cols)
    {
        for (var i = 0; i < rows; i++)
        for (var s = 0; s < cols; s++)
        {
            var k = start + i * (cols + 1) + s;
            p.T.Add(k);
            p.T.Add(k + cols + 1);
            p.T.Add(k + 1);
            p.T.Add(k + 1);
            p.T.Add(k + cols + 1);
            p.T.Add(k + cols + 2);
        }
    }

    /// <summary>A glowing deco fan (a sconce's glass), upright, facing along normal.</summary>
    private void Fan(Part p, Vector3 center, Vector3 right, float radius, int wedges, Color color)
    {
        p = Route(p);
        for (var k = 0; k < wedges; k++)
        {
            var a0 = Mathf.PI * k / wedges;
            var a1 = Mathf.PI * (k + 1) / wedges;
            var p0 = center + (right * Mathf.Cos(a0) + Vector3.up * Mathf.Sin(a0)) * radius;
            var p1 = center + (right * Mathf.Cos(a1) + Vector3.up * Mathf.Sin(a1)) * radius;
            var col = color * (k % 2 == 0 ? 1f : 0.8f);
            col.a = 1;
            var i0 = p.V.Count;
            p.V.Add(center);
            p.V.Add(p0);
            p.V.Add(p1);
            p.UV.Add(new Vector2(0.5f, 0.1f));
            p.UV.Add(new Vector2(0.03f, 0.97f));
            p.UV.Add(new Vector2(0.97f, 0.97f));
            p.C.Add(col);
            p.C.Add(col);
            p.C.Add(col);
            p.T.Add(i0);
            p.T.Add(i0 + 1);
            p.T.Add(i0 + 2);
        }
    }

    // =====================================================================================================
    // Art
    // =====================================================================================================

    private Texture2D _bendyFace, _splat;
    private readonly Dictionary<string, Texture2D> _images = new();

    private void LoadArt()
    {
        _bendyFace = GameArt.Load("BendyIcon");
        _splat = GameArt.Load("ink_splat_decal");
        foreach (var name in new[] { "bendy_pose_02", "bendy_pose_03", "bendy_pose_11", "bendy_pose_13", "bendy_pose_14", "loading_image_ch1", "loading_image_ch2", "loading_image_ch3", "loading_image_ch4", "loading_image_ch5" })
        {
            var t = GameArt.Load(name);
            if (t != null) _images[name] = t;
        }

        _wood = NewPart("Wood", TheatreArt.Wood());
        _box = NewPart("Trim", TheatreArt.Box());
        _fabric = NewPart("Velvet", TheatreArt.Fabric());
        _wall = NewPart("Wallpaper", TheatreArt.Wallpaper());
        _carpet = NewPart("Carpet", TheatreArt.Carpet());
        _coffer = NewPart("Coffers", TheatreArt.Coffer());
        _ornament = NewPart("Frieze", TheatreArt.Ornament());
        _grille = NewPart("Grilles", TheatreArt.Grille());
        _door = NewPart("Doors", TheatreArt.Door());
        _curtain = NewPart("Drapes", TheatreArt.Curtain());
        _posterBg = NewPart("PosterPaper", TheatreArt.PosterPaper());
        _ink = NewPart("Ink", _splat != null ? _splat : TheatreArt.Splat(), true, 2950);
        _medal = NewPart("BendyArt", _bendyFace != null ? _bendyFace : TheatreArt.Sunburst(), true, 2960);
    }

    // =====================================================================================================
    // Floor, rows and seats
    // =====================================================================================================

    private static float RowZ(int r) => FirstRowZ - r * RowGap;

    private static float RowFloor(int r)
    {
        var z = RowZ(r);
        return z >= RakeFromZ ? 0f : Mathf.Ceil((RakeFromZ - z) / RowGap - 1e-3f) * Riser;
    }

    /// <summary>The rake as a smooth slope (for the walls' panelling).</summary>
    private static float Rake(float z) => z >= RakeFromZ ? 0f : Mathf.Min((RakeFromZ - z) / RowGap * Riser + Riser * 0.5f, RowFloor(Rows - 1));

    /// <summary>The stepped floor height at z (main floor).</summary>
    private static float FloorAt(float z)
    {
        if (z > FirstRowZ + RowGap * 0.5f) return 0f;
        var r = Mathf.Clamp(Mathf.FloorToInt((FirstRowZ + RowGap * 0.5f - z) / RowGap), 0, Rows - 1);
        return RowFloor(r);
    }

    private static float BalconyRowZ(int b) => BalconyZ - 0.75f - b * RowGap;
    private static float BalconyFloor(int b) => BalconyY + b * BalconyRise;

    private void BuildFloor()
    {
        // In front of the first row, up to the apron.
        FloorBand(FirstRowZ + RowGap * 0.5f, ApronZ, 0f, 1f);

        var prev = 0f;
        for (var r = 0; r < Rows; r++)
        {
            var h = RowFloor(r);
            var z1 = RowZ(r) + RowGap * 0.5f;
            var z0 = r == Rows - 1 ? BackZ : RowZ(r) - RowGap * 0.5f;
            FloorBand(z0, z1, h, r == Rows - 1 ? 1f : 0.62f);
            if (h > prev + 1e-3f) Riser_(z1, prev, h);
            prev = h;
        }
    }

    /// <summary>One step of the floor: planks under the seats, carpet down the centre aisle (and across the back).</summary>
    private void FloorBand(float z0, float z1, float h, float seatAo)
    {
        var up = Vector3.up;
        foreach (var s in new[] { -1f, 1f })
        {
            var xa = s * AisleHalf;
            var xb = s * Half;
            var a = new Vector3(Mathf.Min(xa, xb), h, z0);
            var b = new Vector3(Mathf.Max(xa, xb), h, z0);
            Patch(_wood, a, b, new Vector3(b.x, h, z1), new Vector3(a.x, h, z1), up, Wood, Sub(Half - AisleHalf, 1.2f), Sub(z1 - z0, 1.2f), 2.2f, 2.2f, seatAo);
        }

        // The runner (u across the aisle, v along it).
        Patch(_carpet, new Vector3(-AisleHalf, h + 0.004f, z0), new Vector3(AisleHalf, h + 0.004f, z0), new Vector3(AisleHalf, h + 0.004f, z1), new Vector3(-AisleHalf, h + 0.004f, z1),
            up, Carpet, 2, Sub(z1 - z0, 0.8f), uvRect: new Vector4(0, z0 / 1.6f, 1, z1 / 1.6f));
    }

    private void Riser_(float z, float y0, float y1)
    {
        var n = Vector3.forward;
        Patch(_wood, new Vector3(-Half, y0, z), new Vector3(Half, y0, z), new Vector3(Half, y1, z), new Vector3(-Half, y1, z), n, WoodDark, 20, 1, 2.2f, 0.3f, 0.8f);
        Patch(_carpet, new Vector3(-AisleHalf, y0, z + 0.004f), new Vector3(AisleHalf, y0, z + 0.004f), new Vector3(AisleHalf, y1, z + 0.004f), new Vector3(-AisleHalf, y1, z + 0.004f),
            n, Carpet * 0.8f, 2, 1, uvRect: new Vector4(0, 0, 1, 0.1f));
    }

    private void BuildSeats()
    {
        var rng = new System.Random(1928);
        for (var r = 0; r < Rows; r++) SeatRow(RowFloor(r), RowZ(r), rng);
        for (var b = 0; b < BalconyRows; b++) SeatRow(BalconyFloor(b), BalconyRowZ(b), rng);
    }

    private void SeatRow(float h, float z, System.Random rng)
    {
        foreach (var s in new[] { -1f, 1f })
        {
            for (var i = 0; i <= SeatsPerSide; i++)
            {
                // Cast-iron standards with wooden armrests between the seats.
                var xb = s * (SeatStart + SeatW * i);
                Box(_box, new Vector3(xb, h + 0.33f, z - 0.06f), new Vector3(0.05f, 0.66f, 0.48f), Iron, 0.9f);
                Box(_box, new Vector3(xb, h + 0.665f, z - 0.03f), new Vector3(0.075f, 0.035f, 0.42f), WoodDark);
                if (i == SeatsPerSide) break;

                var x = s * (SeatStart + SeatW * (i + 0.5f));
                var folded = rng.NextDouble() < 0.1;
                var stained = rng.NextDouble() < 0.025;
                var cloth = stained ? new Color(0.2f, 0.07f, 0.05f) : (rng.NextDouble() < 0.5 ? Velvet : Velvet * 0.93f);
                cloth.a = 1;
                if (folded)
                    Box(_fabric, new Vector3(x, h + 0.62f, z - 0.11f), new Vector3(0.48f, 0.42f, 0.09f), Quaternion.Euler(-8, 0, 0), cloth);
                else
                    Box(_fabric, new Vector3(x, h + 0.43f, z + 0.03f), new Vector3(0.48f, 0.09f, 0.44f), Quaternion.Euler(-6, 0, 0), cloth);
                Box(_fabric, new Vector3(x, h + 0.8f, z - 0.21f), new Vector3(0.5f, 0.56f, 0.08f), Quaternion.Euler(-12, 0, 0), cloth);
                Box(_box, new Vector3(x, h + 0.79f, z - 0.262f), new Vector3(0.52f, 0.6f, 0.025f), Quaternion.Euler(-12, 0, 0), WoodDark);
            }
        }
    }

    // =====================================================================================================
    // Walls
    // =====================================================================================================

    private void BuildWalls()
    {
        foreach (var s in new[] { -1f, 1f })
        {
            var x = s * Half;
            var n = new Vector3(-s, 0, 0);
            var zs = new List<float>();
            for (var z = BackZ; z < ProsZ - 0.01f; z += 1.0f) zs.Add(z);
            zs.Add(ProsZ);
            for (var k = 0; k < zs.Count - 1; k++)
            {
                float za = zs[k], zb = zs[k + 1];
                var ha = Rake(za) + 1.2f;
                var hb = Rake(zb) + 1.2f;

                // Dado panelling, a chair rail, deco wallpaper up to the cornice.
                Patch(_wood, P(x, 0, za, s), P(x, 0, zb, s), P(x, hb, zb, s), P(x, ha, za, s), n, WoodDark, 1, 2, 1.6f, 1.6f);
                Patch(_box, P(x - s * 0.04f, ha, za, s), P(x - s * 0.04f, hb, zb, s), P(x - s * 0.04f, hb + 0.1f, zb, s), P(x - s * 0.04f, ha + 0.1f, za, s), n, GoldDark, 1, 1);
                Patch(_wall, P(x, ha + 0.1f, za, s), P(x, hb + 0.1f, zb, s), P(x, CorniceY, zb, s), P(x, CorniceY, za, s), n, Paper, 1, 9, 1.2f, 1.2f);

                // The cornice frieze and the plaster band above it.
                Band(_ornament, P(x - s * 0.12f, CorniceY, za, s), P(x - s * 0.12f, CorniceY, zb, s), Vector3.up, 0.6f, n, Gold, 1.6f);
                Patch(_box, P(x, CorniceY, za, s), P(x - s * 0.12f, CorniceY, za, s), P(x - s * 0.12f, CorniceY, zb, s), P(x, CorniceY, zb, s), Vector3.down, GoldDark);
                Patch(_wall, P(x, CorniceY + 0.6f, za, s), P(x, CorniceY + 0.6f, zb, s), P(x, CeilingY, zb, s), P(x, CeilingY, za, s), n, Plaster * 0.8f, 1, 1, 1.2f, 1.2f);
            }

            foreach (var z in PilasterZ) Pilaster(s, z);

            // Exit doors near the stage.
            Doorway(new Vector3(x - s * 0.02f, 0, 2.0f), new Vector3(0, 0, s), n, 1.5f, 2.4f);
            ExitSign(new Vector3(x - s * 0.05f, 2.92f, 2.0f), Quaternion.Euler(0, s > 0 ? 90 : -90, 0), n);
        }
    }

    /// <summary>Wall point, nudged into the room a hair so the layers don't fight.</summary>
    private static Vector3 P(float x, float y, float z, float s) => new(x, y, z);

    private void Pilaster(float s, float z)
    {
        var x = s * Half;
        var under = z < BalconyZ;
        var baseY = Rake(z);
        Box(_box, new Vector3(x - s * 0.13f, (baseY + CorniceY) * 0.5f, z), new Vector3(0.26f, CorniceY - baseY, 0.62f), Plaster * 0.95f);
        Box(_box, new Vector3(x - s * 0.18f, baseY + 0.3f, z), new Vector3(0.36f, 0.6f, 0.78f), WoodDark);
        Box(_box, new Vector3(x - s * 0.19f, CorniceY - 0.3f, z), new Vector3(0.38f, 0.6f, 0.82f), Gold);
        Box(_box, new Vector3(x - s * 0.22f, CorniceY - 0.66f, z), new Vector3(0.3f, 0.12f, 0.7f), GoldDark);

        // A brass bracket and a ribbed glass fan.
        Sconce(new Vector3(x - s * 0.27f, 3.2f, z), s);
        if (under) Sconce(new Vector3(x - s * 0.27f, 7.05f, z), s);
    }

    private void Sconce(Vector3 at, float s)
    {
        var n = new Vector3(-s, 0, 0);
        Box(_box, at + n * 0.06f + Vector3.down * 0.03f, new Vector3(0.12f, 0.06f, 0.2f), Gold);
        Fan(_box, at + n * 0.1f, new Vector3(0, 0, 1), 0.3f, 7, new Color(1f, 0.86f, 0.58f));
        Glow(at + n * 0.2f + Vector3.up * 0.14f, 1.1f, new Color(1f, 0.7f, 0.36f, 0.5f), GlowGroup.Flicker);
        Glow(at + n * 0.14f + Vector3.up * 0.1f, 0.38f, new Color(1f, 0.9f, 0.7f, 0.85f), GlowGroup.Flicker);
    }

    private void Doorway(Vector3 bottomCentre, Vector3 right, Vector3 n, float width, float height, bool leaves = true)
    {
        right = right.normalized;
        var half = width * 0.5f;
        for (var leaf = 0; leaf < (leaves ? 2 : 0); leaf++)
        {
            var a = bottomCentre + right * (leaf == 0 ? -half : 0f);
            var b = a + right * half;
            Patch(_door, a, b, b + Vector3.up * height, a + Vector3.up * height, n, Wood, 1, 3, uvRect: leaf == 0 ? new Vector4(0, 0, 1, 1) : new Vector4(1, 0, 0, 1));
            Glow(a + right * (half * 0.5f) + Vector3.up * (height * 0.72f) + n * 0.03f, 0.32f, new Color(1f, 0.82f, 0.55f, 0.55f), GlowGroup.Steady);
        }

        // Frame.
        var t = 0.12f;
        Box(_box, bottomCentre + right * (half + t * 0.5f) + Vector3.up * (height * 0.5f) + n * 0.04f, Abs(right * t + Vector3.up * height + n * 0.1f), GoldDark);
        Box(_box, bottomCentre - right * (half + t * 0.5f) + Vector3.up * (height * 0.5f) + n * 0.04f, Abs(right * t + Vector3.up * height + n * 0.1f), GoldDark);
        Box(_box, bottomCentre + Vector3.up * (height + t * 0.5f) + n * 0.04f, Abs(right * (width + t * 2) + Vector3.up * t + n * 0.1f), GoldDark);
    }

    private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    private void ExitSign(Vector3 at, Quaternion facing, Vector3 n)
    {
        Box(_box, at, Abs(facing * new Vector3(0.72f, 0.24f, 0.07f)), Dark);
        Signs.Add(new SignSpec { Text = "EXIT", Pos = at + n * 0.045f, Rot = facing, Height = 0.17f, Width = 0.7f, Color = new Color(1f, 0.42f, 0.22f) });
        Glow(at + n * 0.12f, 0.8f, new Color(1f, 0.3f, 0.12f, 0.35f), GlowGroup.Steady);
    }

    // =====================================================================================================
    // Proscenium, stage and drapes
    // =====================================================================================================

    private void BuildProscenium()
    {
        var n = Vector3.back;
        var z = ProsZ;

        // The wall either side of and above the opening.
        foreach (var s in new[] { -1f, 1f })
        {
            float x0 = s * OpenHalf, x1 = s * Half;
            Patch(_wood, new Vector3(x0, 0, z), new Vector3(x1, 0, z), new Vector3(x1, 1.2f, z), new Vector3(x0, 1.2f, z), n, WoodDark, 4, 1, 1.6f, 1.6f);
            Patch(_wall, new Vector3(x0, 1.2f, z), new Vector3(x1, 1.2f, z), new Vector3(x1, CorniceY, z), new Vector3(x0, CorniceY, z), n, Paper * 0.95f, 4, 9, 1.2f, 1.2f);

            // Organ grilles in gilded frames.
            var gx0 = s * 6.2f;
            var gx1 = s * 8.4f;
            Patch(_grille, new Vector3(Mathf.Min(gx0, gx1), 2.4f, z - 0.04f), new Vector3(Mathf.Max(gx0, gx1), 2.4f, z - 0.04f), new Vector3(Mathf.Max(gx0, gx1), 8.3f, z - 0.04f), new Vector3(Mathf.Min(gx0, gx1), 8.3f, z - 0.04f),
                n, Gold, 3, 8);
            var gc = (gx0 + gx1) * 0.5f;
            Box(_box, new Vector3(gc, 2.3f, z - 0.1f), new Vector3(2.5f, 0.2f, 0.2f), GoldDark);
            Box(_box, new Vector3(gc, 8.4f, z - 0.1f), new Vector3(2.5f, 0.2f, 0.2f), GoldDark);
            Box(_box, new Vector3(gx0 - s * 0.05f, 5.35f, z - 0.1f), new Vector3(0.18f, 6.1f, 0.2f), GoldDark);
            Box(_box, new Vector3(gx1 + s * 0.05f, 5.35f, z - 0.1f), new Vector3(0.18f, 6.1f, 0.2f), GoldDark);
            Box(_box, new Vector3(gc, 1.75f, z - 0.15f), new Vector3(1.6f, 0.5f, 0.3f), Gold); // console shelf under each grille
        }

        Patch(_wall, new Vector3(-OpenHalf, OpenTop, z), new Vector3(OpenHalf, OpenTop, z), new Vector3(OpenHalf, CorniceY, z), new Vector3(-OpenHalf, CorniceY, z), n, Paper * 0.95f, 10, 4, 1.2f, 1.2f);
        Band(_ornament, new Vector3(-Half, CorniceY, z - 0.12f), new Vector3(Half, CorniceY, z - 0.12f), Vector3.up, 0.6f, n, Gold, 1.6f);
        // its underside, back to the wall: without it you looked up past the frieze into the empty dark behind it,
        // a sliver that flickered as you moved
        Patch(_box, new Vector3(-Half, CorniceY, z), new Vector3(-Half, CorniceY, z - 0.12f), new Vector3(Half, CorniceY, z - 0.12f), new Vector3(Half, CorniceY, z), Vector3.down, GoldDark);
        Patch(_wall, new Vector3(-Half, CorniceY + 0.6f, z), new Vector3(Half, CorniceY + 0.6f, z), new Vector3(Half, CeilingY, z), new Vector3(-Half, CeilingY, z), n, Plaster * 0.8f, 12, 1, 1.2f, 1.2f);

        // The arch: an inner gilded frieze band and a stepped dark outer frame.
        foreach (var s in new[] { -1f, 1f })
        {
            var xi = s * (OpenHalf + 0.17f);
            Box(_box, new Vector3(xi, (OpenTop + 0.35f) * 0.5f, z - 0.12f), new Vector3(0.35f, OpenTop + 0.35f, 0.24f), GoldDark);
            Band(_ornament, new Vector3(xi, 0, z - FriezeOut) - Vector3.right * 0.17f, new Vector3(xi, OpenTop + 0.35f, z - FriezeOut) - Vector3.right * 0.17f, Vector3.right, 0.34f, n, Gold, 1.3f);
            var xo = s * (OpenHalf + 0.52f);
            Box(_box, new Vector3(xo, (OpenTop + 0.7f) * 0.5f, z - 0.07f), new Vector3(0.35f, OpenTop + 0.7f, 0.14f), WoodDark);
        }

        Box(_box, new Vector3(0, OpenTop + 0.175f, z - 0.12f), new Vector3(2 * (OpenHalf + 0.35f), 0.35f, 0.24f), GoldDark);
        Band(_ornament, new Vector3(-(OpenHalf + 0.35f), OpenTop + 0.005f, z - FriezeOut), new Vector3(OpenHalf + 0.35f, OpenTop + 0.005f, z - FriezeOut), Vector3.up, 0.34f, n, Gold, 1.3f);
        Box(_box, new Vector3(0, OpenTop + 0.525f, z - 0.07f), new Vector3(2 * (OpenHalf + 0.7f), 0.35f, 0.14f), WoodDark);

        // The studio's name on a plaque, Bendy's face in a gilded medallion, and a sunburst behind it.
        Box(_box, new Vector3(0, 7.5f, z - 0.1f), new Vector3(6.6f, 0.8f, 0.2f), Dark);
        Box(_box, new Vector3(0, 7.5f, z - 0.06f), new Vector3(6.9f, 1.0f, 0.1f), Gold);
        Signs.Add(new SignSpec { Text = "JOEY DREW STUDIOS", Pos = new Vector3(0, 7.48f, z - 0.24f), Rot = Quaternion.identity, Height = 0.55f, Width = 6.4f, Color = new Color(0.98f, 0.82f, 0.5f) });

        var medal = new Vector3(0, 8.75f, z - 0.16f);
        for (var k = 0; k < 13; k++)
        {
            var a = Mathf.Lerp(-80f, 80f, k / 12f);
            var rot = Quaternion.Euler(0, 0, a);
            var len = k % 2 == 0 ? 0.95f : 0.6f;
            Box(_box, medal + rot * Vector3.up * (0.78f + len * 0.5f) + Vector3.forward * 0.1f, new Vector3(0.12f, len, 0.05f), Quaternion.Euler(0, 0, a), Gold);
        }

        Annulus(_box, medal + Vector3.back * 0.04f, 0.64f, 0.8f, Gold, 40);
        Patch(_medal, medal + new Vector3(-0.66f, -0.66f, 0f), medal + new Vector3(0.66f, -0.66f, 0f), medal + new Vector3(0.66f, 0.66f, 0f), medal + new Vector3(-0.66f, 0.66f, 0f), n, new Color(1f, 0.95f, 0.85f), 1, 1);
    }

    private void BuildStage()
    {
        var up = Vector3.up;
        // Apron and stage floor.
        Patch(_wood, new Vector3(-ApronHalf, StageY, ApronZ), new Vector3(ApronHalf, StageY, ApronZ), new Vector3(ApronHalf, StageY, ProsZ), new Vector3(-ApronHalf, StageY, ProsZ), up, Wood * 1.05f, 12, 2, 2.2f, 2.2f);
        Patch(_wood, new Vector3(-OpenHalf, StageY, ProsZ), new Vector3(OpenHalf, StageY, ProsZ), new Vector3(OpenHalf, StageY, ScreenZ + 0.4f), new Vector3(-OpenHalf, StageY, ScreenZ + 0.4f), up, Wood, 10, 2, 2.2f, 2.2f);
        Patch(_box, new Vector3(-ApronHalf, 0, ApronZ), new Vector3(ApronHalf, 0, ApronZ), new Vector3(ApronHalf, StageY, ApronZ), new Vector3(-ApronHalf, StageY, ApronZ), Vector3.back, WoodDark, 14, 1, uvRect: new Vector4(0, 0, 14, 1));
        foreach (var s in new[] { -1f, 1f })
            Patch(_box, new Vector3(s * ApronHalf, 0, ApronZ), new Vector3(s * ApronHalf, 0, ProsZ), new Vector3(s * ApronHalf, StageY, ProsZ), new Vector3(s * ApronHalf, StageY, ApronZ), new Vector3(s, 0, 0), WoodDark, 1, 1);

        // Footlights: a brass trough with a row of bulbs.
        Box(_box, new Vector3(0, StageY + 0.04f, ApronZ + 0.1f), new Vector3(10.6f, 0.08f, 0.16f), GoldDark);
        for (var x = -5.1f; x <= 5.11f; x += 0.6f) Glow(new Vector3(x, StageY + 0.11f, ApronZ + 0.08f), 0.28f, new Color(1f, 0.85f, 0.55f, 0.85f), GlowGroup.Flicker);
        Signs.Add(new SignSpec { Text = "InkVR  -  VR mod made by StrikeZoneVR", Pos = new Vector3(0, 0.25f, ApronZ - 0.012f), Rot = Quaternion.identity, Height = 0.13f, Width = 6f, Color = new Color(0.95f, 0.82f, 0.56f) });
        Signs.Add(new SignSpec { Text = "click the move stick to explore the theatre", Pos = new Vector3(0, 0.1f, ApronZ - 0.012f), Rot = Quaternion.identity, Height = 0.065f, Width = 6f, Color = new Color(0.95f, 0.82f, 0.56f) * 0.85f });

        // Black masking behind the screen.
        Patch(_curtain, new Vector3(-OpenHalf, StageY, ScreenZ + 0.25f), new Vector3(OpenHalf, StageY, ScreenZ + 0.25f), new Vector3(OpenHalf, OpenTop, ScreenZ + 0.25f), new Vector3(-OpenHalf, OpenTop, ScreenZ + 0.25f),
            Vector3.back, Dark, 6, 4, uvRect: new Vector4(0, 0, 6, 1));

        // Two of Bendy's cardboard cut-outs keeping an eye on the audience.
        Standee(new Vector3(-ApronHalf + 0.75f, StageY, ApronZ + 0.45f), "bendy_pose_02", -12f);
        Standee(new Vector3(ApronHalf - 0.75f, StageY, ApronZ + 0.45f), "bendy_pose_13", 12f);
    }

    private void Standee(Vector3 foot, string pose, float yaw)
    {
        var rot = Quaternion.Euler(0, yaw, 0);
        var right = rot * Vector3.right;
        var n = rot * Vector3.back;
        var w = 1.05f;
        var h = 1.35f;
        Box(_box, foot + rot * new Vector3(0, 0.03f, 0.1f), Abs(rot * new Vector3(0.7f, 0.06f, 0.4f)), WoodDark);
        Patch(_posterBg, foot - right * (w * 0.5f) + Vector3.up * 0.06f, foot + right * (w * 0.5f) + Vector3.up * 0.06f, foot + right * (w * 0.5f) + Vector3.up * (0.06f + h), foot - right * (w * 0.5f) + Vector3.up * (0.06f + h), n, Cream, 1, 2);
        if (_images.TryGetValue(pose, out var tex)) AddArtQuad(tex, foot + Vector3.up * (0.06f + h * 0.5f) + n * 0.01f, right, w * 0.95f, w * 0.95f, n);
    }

    private void BuildCurtains()
    {
        // The main drapes, gathered and tied back either side of the screen.
        foreach (var s in new[] { -1f, 1f })
        {
            const int nu = 30, nv = 22;
            var pts = new Vector3[(nu + 1) * (nv + 1)];
            const float tieY = 1.75f;
            for (var j = 0; j <= nv; j++)
            {
                var fv = (float)j / nv;
                var y = Mathf.Lerp(StageY, OpenTop, fv);
                var inner = y >= tieY
                    ? Mathf.Lerp(4.75f, 3.55f, Mathf.SmoothStep(0, 1, (y - tieY) / (OpenTop - tieY)))
                    : Mathf.Lerp(4.75f, 4.35f, (tieY - y) / (tieY - StageY));
                var outer = OpenHalf + 0.05f;
                var gather = Mathf.InverseLerp(outer - 3.55f, outer - 4.75f, outer - inner); // 0 open .. 1 gathered
                for (var i = 0; i <= nu; i++)
                {
                    var fu = (float)i / nu;
                    var xa = Mathf.Lerp(outer, inner, fu);
                    var fold = Mathf.Sin(fu * Mathf.PI * 2 * 7.5f) * (0.07f + 0.08f * gather);
                    pts[j * (nu + 1) + i] = new Vector3(s * xa, y, ProsZ + 0.22f + fold - 0.06f * fu);
                }
            }

            Surface(_curtain, pts, nu, nv, Velvet, 4f, 1.6f);
            Box(_box, new Vector3(s * 4.75f, tieY, ProsZ + 0.16f), new Vector3(0.36f, 0.12f, 0.32f), Gold);
            Lathe(_box, new Vector3(s * 4.75f, tieY - 0.42f, ProsZ + 0.02f), new[] { new Vector2(0.01f, 0f), new Vector2(0.09f, 0.08f), new Vector2(0.05f, 0.3f), new Vector2(0.02f, 0.36f) }, Gold, 10);
        }

        // A swagged valance across the top with a gold fringe.
        {
            const int nu = 84, nv = 5;
            var pts = new Vector3[(nu + 1) * (nv + 1)];
            var x0 = -(OpenHalf + 0.05f);
            var x1 = OpenHalf + 0.05f;
            var bottoms = new float[nu + 1];
            for (var i = 0; i <= nu; i++)
            {
                var fu = (float)i / nu;
                var swag = Mathf.Sin(Mathf.Repeat(fu * 7f, 1f) * Mathf.PI);
                bottoms[i] = OpenTop - 0.55f - 0.32f * swag;
                for (var j = 0; j <= nv; j++)
                {
                    var fv = (float)j / nv;
                    var y = Mathf.Lerp(bottoms[i], OpenTop + 0.02f, fv);
                    var fold = Mathf.Sin(fu * Mathf.PI * 2 * 28f) * 0.035f + swag * 0.08f * (1 - fv);
                    pts[j * (nu + 1) + i] = new Vector3(Mathf.Lerp(x0, x1, fu), y, ProsZ + 0.08f - fold);
                }
            }

            Surface(_curtain, pts, nu, nv, VelvetDeep, 14f, 0.5f);
            for (var i = 0; i < nu; i++)
            {
                var a = pts[i] + Vector3.back * 0.01f;
                var b = pts[i + 1] + Vector3.back * 0.01f;
                Patch(_ornament, a + Vector3.down * 0.07f, b + Vector3.down * 0.07f, b + Vector3.up * 0.04f, a + Vector3.up * 0.04f, Vector3.back, Gold, 1, 1, uvRect: new Vector4(i * 0.25f, 0.05f, (i + 1) * 0.25f, 0.45f));
            }
        }
    }

    /// <summary>A grid surface (curtain cloth) with normals from its own shape, facing the audience.</summary>
    private void Surface(Part p, Vector3[] pts, int nu, int nv, Color tint, float uRepeat, float vRepeat)
    {
        var start = p.V.Count;
        for (var j = 0; j <= nv; j++)
        for (var i = 0; i <= nu; i++)
        {
            var pos = pts[j * (nu + 1) + i];
            var du = pts[j * (nu + 1) + Mathf.Min(nu, i + 1)] - pts[j * (nu + 1) + Mathf.Max(0, i - 1)];
            var dv = pts[Mathf.Min(nv, j + 1) * (nu + 1) + i] - pts[Mathf.Max(0, j - 1) * (nu + 1) + i];
            var n = Vector3.Cross(du, dv).normalized;
            if (n.z > 0) n = -n;
            p.V.Add(pos);
            p.UV.Add(new Vector2((float)i / nu * uRepeat, (float)j / nv * vRepeat));
            p.C.Add(Lit(pos, n, tint, 1f));
        }

        Grid(p, start, nv, nu);
    }

    // =====================================================================================================
    // Ceiling and chandelier
    // =====================================================================================================

    private void BuildCeiling()
    {
        var down = Vector3.down;
        var xs = new[] { -9f, -6f, -3f, 0f, 3f, 6f, 9f };
        var zs = new List<float> { BackZ };
        for (var z = -11f; z < ProsZ; z += 3f) zs.Add(z);
        zs.Add(ProsZ);
        for (var i = 0; i < xs.Length - 1; i++)
        for (var k = 0; k < zs.Count - 1; k++)
        {
            float x0 = xs[i], x1 = xs[i + 1], z0 = zs[k], z1 = zs[k + 1];
            Patch(_coffer, new Vector3(x0, CeilingY, z0), new Vector3(x1, CeilingY, z0), new Vector3(x1, CeilingY, z1), new Vector3(x0, CeilingY, z1), down, Plaster, 3, 3);
        }

        // Beams between the coffers, one piece per span so each is outlined like the rest of the trim.
        foreach (var x in xs)
            if (Mathf.Abs(x) < Half - 0.1f)
                for (var k = 0; k < zs.Count - 1; k++)
                    Box(_box, new Vector3(x, CeilingY - 0.2f, (zs[k] + zs[k + 1]) * 0.5f), new Vector3(0.3f, 0.4f, zs[k + 1] - zs[k]), GoldDark, 1f, true);
        foreach (var z in zs)
            if (z > BackZ + 0.1f && z < ProsZ - 0.1f)
                for (var i = 0; i < xs.Length - 1; i++)
                    Box(_box, new Vector3((xs[i] + xs[i + 1]) * 0.5f, CeilingY - 0.18f, z), new Vector3(xs[i + 1] - xs[i], 0.36f, 0.3f), GoldDark, 1f, true);
    }

    private void BuildChandelier() => Chandelier(new Vector3(0, 8.4f, -3.5f), CeilingY - 0.4f);

    private void Chandelier(Vector3 c, float hangFrom)
    {
        Box(_box, new Vector3(c.x, (c.y + 0.7f + hangFrom) * 0.5f, c.z), new Vector3(0.04f, hangFrom - (c.y + 0.7f), 0.04f), Gold, 1.6f);
        Ring(_box, c, 0.95f, 0.05f, Gold, 36, 6, 1.6f);
        Ring(_box, c + Vector3.up * 0.4f, 0.62f, 0.045f, Gold, 30, 6, 1.6f);
        Ring(_box, c + Vector3.up * 0.72f, 0.32f, 0.04f, Gold, 24, 6, 1.6f);
        Lathe(_box, c + Vector3.down * 0.05f, new[] { new Vector2(0.01f, -0.55f), new Vector2(0.22f, -0.46f), new Vector2(0.42f, -0.25f), new Vector2(0.5f, 0f), new Vector2(0.46f, 0.05f) }, new Color(1f, 0.82f, 0.55f), 24, true);
        for (var k = 0; k < 8; k++)
        {
            var a = k / 8f * Mathf.PI * 2;
            var top = c + Vector3.up * 0.72f + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.32f;
            var bottom = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.95f;
            var mid = (top + bottom) * 0.5f;
            Box(_box, mid, new Vector3(0.025f, (top - bottom).magnitude, 0.025f), Quaternion.FromToRotation(Vector3.up, top - bottom), Gold, 1.6f);
        }

        foreach (var (r, y, count) in new[] { (0.95f, 0.06f, 16), (0.62f, 0.46f, 12), (0.32f, 0.78f, 8) })
            for (var k = 0; k < count; k++)
            {
                var a = (k + 0.5f) / count * Mathf.PI * 2;
                Glow(c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), 0.22f, new Color(1f, 0.88f, 0.62f, 0.95f), GlowGroup.Flicker);
            }

        Glow(c + Vector3.up * 0.2f, 3.2f, new Color(1f, 0.75f, 0.42f, 0.2f), GlowGroup.Steady);
        Glow(c + Vector3.down * 0.3f, 0.9f, new Color(1f, 0.85f, 0.6f, 0.45f), GlowGroup.Steady);
    }

    // =====================================================================================================
    // Balcony and back wall
    // =====================================================================================================

    private void BuildBalcony()
    {
        // The soffit under the balcony, panelled, with a few downlights.
        for (var x = -Half; x < Half - 0.01f; x += 2.25f)
        for (var z = BackZ; z < BalconyZ - 0.01f; z += 2.17f)
        {
            var z1 = Mathf.Min(z + 2.17f, BalconyZ);
            Patch(_box, new Vector3(x, SoffitY, z), new Vector3(x + 2.25f, SoffitY, z), new Vector3(x + 2.25f, SoffitY, z1), new Vector3(x, SoffitY, z1), Vector3.down, Plaster * 0.9f, 2, 2);
        }

        foreach (var p in new[] { new Vector3(-5f, SoffitY - 0.02f, -9.6f), new Vector3(5f, SoffitY - 0.02f, -9.6f), new Vector3(-1.8f, SoffitY - 0.02f, -12f), new Vector3(1.8f, SoffitY - 0.02f, -12f) })
        {
            Lathe(_box, p, new[] { new Vector2(0.16f, 0f), new Vector2(0.12f, -0.06f) }, new Color(1f, 0.88f, 0.62f), 16, true);
            Glow(p + Vector3.down * 0.08f, 0.55f, new Color(1f, 0.85f, 0.6f, 0.6f), GlowGroup.Steady);
        }

        // The parapet: panelled, a gilded frieze, a rail on top, and the marquee in the middle.
        var front = BalconyZ;
        Box(_box, new Vector3(0, (SoffitY + ParapetTop) * 0.5f, front - 0.15f), new Vector3(2 * Half, ParapetTop - SoffitY, 0.3f), WoodDark);
        Band(_ornament, new Vector3(-Half, ParapetTop - 0.42f, front + 0.04f), new Vector3(Half, ParapetTop - 0.42f, front + 0.04f), Vector3.up, 0.36f, Vector3.forward, Gold, 1.4f);
        Band(_ornament, new Vector3(-Half, SoffitY + 0.02f, front + 0.04f), new Vector3(Half, SoffitY + 0.02f, front + 0.04f), Vector3.up, 0.22f, Vector3.forward, GoldDark, 1.0f);
        Box(_box, new Vector3(0, ParapetTop + 0.04f, front - 0.15f), new Vector3(2 * Half, 0.08f, 0.44f), Wood);

        var plaque = new Vector3(0, 4.45f, front + 0.08f);
        Box(_box, plaque, new Vector3(4.4f, 0.92f, 0.16f), Gold);
        Box(_box, plaque + Vector3.forward * 0.04f, new Vector3(4.1f, 0.7f, 0.12f), Dark);
        Signs.Add(new SignSpec { Text = "DREAMS COME TRUE", Pos = plaque + Vector3.forward * 0.14f, Rot = Quaternion.Euler(0, 180, 0), Height = 0.42f, Width = 4f, Color = new Color(1f, 0.84f, 0.5f) });
        var bulb = 0;
        void Bulb(Vector3 p) => Glow(p, 0.22f, new Color(1f, 0.88f, 0.6f, 1f), GlowGroup.Chase, bulb++);
        for (var x = -2.1f; x <= 2.11f; x += 0.21f) Bulb(plaque + new Vector3(x, 0.4f, 0.12f));
        for (var y = 0.2f; y >= -0.21f; y -= 0.2f) Bulb(plaque + new Vector3(2.1f, y, 0.12f));
        for (var x = 2.1f; x >= -2.11f; x -= 0.21f) Bulb(plaque + new Vector3(x, -0.4f, 0.12f));
        for (var y = -0.2f; y <= 0.21f; y += 0.2f) Bulb(plaque + new Vector3(-2.1f, y, 0.12f));

        // Stepped balcony tiers.
        var prev = BalconyY;
        for (var b = 0; b < BalconyRows; b++)
        {
            var h = BalconyFloor(b);
            var z1 = b == 0 ? front - 0.3f : BalconyRowZ(b) + RowGap * 0.5f;
            var z0 = b == BalconyRows - 1 ? BackZ : BalconyRowZ(b) - RowGap * 0.5f;
            FloorBand(z0, z1, h, b == BalconyRows - 1 ? 1f : 0.62f);
            if (h > prev + 1e-3f) Riser_(z1, prev, h);
            prev = h;
        }
    }

    private void BuildBackWall()
    {
        var n = Vector3.forward;
        var z = BackZ;
        var floor = RowFloor(Rows - 1);

        // Under the balcony: panelling, wallpaper, the lobby doors (they open into the lobby) and two exits.
        var door = new[] { new Vector4(-LobbyDoorHalf, floor, LobbyDoorHalf, floor + LobbyDoorHeight) };
        WallWithHoles(_wood, new Vector3(0, 0, z), Vector3.right, -Half, Half, floor, floor + 1.1f, n, WoodDark, door, 1.6f, 1.6f);
        WallWithHoles(_wall, new Vector3(0, 0, z), Vector3.right, -Half, Half, floor + 1.1f, SoffitY, n, Paper, door, 1.2f, 1.2f);
        Doorway(new Vector3(0, floor, z + 0.02f), Vector3.left, n, LobbyDoorHalf * 2, LobbyDoorHeight, false);
        foreach (var s in new[] { -1f, 1f })
        {
            Doorway(new Vector3(s * 6f, floor, z + 0.02f), Vector3.left, n, 1.3f, 2.1f);
            ExitSign(new Vector3(s * 6f, 3.6f, z + 0.05f), Quaternion.Euler(0, 180, 0), n);
        }

        Glow(new Vector3(0, floor + 2.8f, z + 0.25f), 1.6f, new Color(1f, 0.78f, 0.45f, 0.16f), GlowGroup.Steady);

        // Above the balcony: the projection booth.
        var top = BalconyFloor(BalconyRows - 1);
        var upper = BalconyDoors(top);
        WallWithHoles(_wood, new Vector3(0, 0, z), Vector3.right, -Half, Half, top, top + 1.1f, n, WoodDark, upper, 1.6f, 1.6f);
        WallWithHoles(_wall, new Vector3(0, 0, z), Vector3.right, -Half, Half, top + 1.1f, CorniceY, n, Paper * 0.9f, upper, 1.2f, 1.2f);
        foreach (var s in new[] { -1f, 1f })
        {
            Doorway(new Vector3(s * BalconyDoorX, top, z + 0.02f), Vector3.left, n, BalconyDoorHalf * 2, BalconyDoorHeight, false);
            Signs.Add(new SignSpec { Text = "STAIRS", Pos = new Vector3(s * BalconyDoorX, top + BalconyDoorHeight + 0.32f, z + 0.06f), Rot = Quaternion.Euler(0, 180, 0), Height = 0.16f, Width = 1.2f, Color = new Color(1f, 0.84f, 0.5f) });
        }
        Band(_ornament, new Vector3(-Half, CorniceY, z + 0.12f), new Vector3(Half, CorniceY, z + 0.12f), Vector3.up, 0.6f, n, Gold, 1.6f);
        Patch(_box, new Vector3(-Half, CorniceY, z), new Vector3(Half, CorniceY, z), new Vector3(Half, CorniceY, z + 0.12f), new Vector3(-Half, CorniceY, z + 0.12f), Vector3.down, GoldDark);
        Patch(_wall, new Vector3(-Half, CorniceY + 0.6f, z), new Vector3(Half, CorniceY + 0.6f, z), new Vector3(Half, CeilingY, z), new Vector3(-Half, CeilingY, z), n, Plaster * 0.8f, 12, 1, 1.2f, 1.2f);
        foreach (var x in new[] { -1.7f, 0f, 1.7f })
        {
            var w = new Vector3(x, Projector.y, z + 0.02f);
            Box(_box, w, new Vector3(0.86f, 0.62f, 0.1f), GoldDark);
            Patch(_box, w + new Vector3(-0.33f, -0.22f, 0.06f), w + new Vector3(0.33f, -0.22f, 0.06f), w + new Vector3(0.33f, 0.22f, 0.06f), w + new Vector3(-0.33f, 0.22f, 0.06f), n,
                x == 0 ? new Color(1f, 0.95f, 0.82f) : new Color(0.45f, 0.32f, 0.2f), 1, 1, unlit: true);
            Glow(w + Vector3.forward * 0.15f, x == 0 ? 1.4f : 0.7f, x == 0 ? new Color(1f, 0.95f, 0.8f, 0.9f) : new Color(1f, 0.75f, 0.45f, 0.22f), x == 0 ? GlowGroup.Projector : GlowGroup.Steady);
        }
    }

    // =====================================================================================================
    // Posters and lobby cards
    // =====================================================================================================

    private struct PosterSlot
    {
        public Vector3 Center, Normal, Right;
        public bool Portrait;
        public string Art, Title;
    }

    private static IEnumerable<PosterSlot> PosterSlots()
    {
        var mids = new[] { -1.475f, -4.925f, -8.375f, -11.825f };
        var left = new[] { ("bendy_pose_02", "SENT"), ("loading_image_ch1", "CHAPTER ONE: MOVING PICTURES"), ("bendy_pose_11", "TOMBSTONE PICNIC"), ("loading_image_ch3", "CHAPTER THREE: RISE AND FALL") };
        var right = new[] { ("bendy_pose_13", "THE DANCING DEMON"), ("loading_image_ch2", "CHAPTER TWO: THE OLD SONG"), ("bendy_pose_14", "HELL'S FIRE"), ("loading_image_ch4", "CHAPTER FOUR: COLOSSAL WONDERS") };
        foreach (var s in new[] { -1f, 1f })
            for (var k = 0; k < mids.Length; k++)
            {
                var (art, title) = s < 0 ? left[k] : right[k];
                var portrait = art.StartsWith("bendy");
                var z = mids[k];
                var y = Rake(z) + (portrait ? 2.0f : 1.85f);
                if (k == 3) y = Mathf.Min(y, SoffitY - 0.75f);
                yield return new PosterSlot
                {
                    Center = new Vector3(s * (Half - 0.06f), y, z), Normal = new Vector3(-s, 0, 0), Right = new Vector3(0, 0, -s), Portrait = portrait, Art = art, Title = title
                };
            }

        var floor = RowFloor(Rows - 1);
        yield return new PosterSlot { Center = new Vector3(-3.4f, floor + 1.45f, BackZ + 0.06f), Normal = Vector3.forward, Right = Vector3.left, Portrait = false, Art = "loading_image_ch5", Title = "CHAPTER FIVE: THE LAST REEL" };
        yield return new PosterSlot { Center = new Vector3(3.4f, floor + 1.4f, BackZ + 0.06f), Normal = Vector3.forward, Right = Vector3.left, Portrait = true, Art = "bendy_pose_03", Title = "TRAIN TROUBLE" };
    }

    private void BuildPosters()
    {
        foreach (var slot in PosterSlots())
        {
            var n = slot.Normal;
            var right = slot.Right;
            var w = slot.Portrait ? 0.95f : 1.76f;
            var h = slot.Portrait ? 1.4f : 0.99f;
            var c = slot.Center;
            var rot = Quaternion.LookRotation(-n);

            // Gilded frame.
            var f = 0.09f;
            Box(_box, c + Vector3.up * (h * 0.5f + f * 0.5f) + n * 0.03f, Abs(right * (w + 2 * f) + Vector3.up * f + n * 0.06f), Gold);
            Box(_box, c - Vector3.up * (h * 0.5f + f * 0.5f) + n * 0.03f, Abs(right * (w + 2 * f) + Vector3.up * f + n * 0.06f), Gold);
            Box(_box, c + right * (w * 0.5f + f * 0.5f) + n * 0.03f, Abs(right * f + Vector3.up * h + n * 0.06f), Gold);
            Box(_box, c - right * (w * 0.5f + f * 0.5f) + n * 0.03f, Abs(right * f + Vector3.up * h + n * 0.06f), Gold);

            var a = c - right * (w * 0.5f) - Vector3.up * (h * 0.5f) + n * 0.01f;
            var b = c + right * (w * 0.5f) - Vector3.up * (h * 0.5f) + n * 0.01f;
            if (slot.Portrait)
            {
                // A cartoon one-sheet: paper, Bendy, the studio's name and the cartoon's title.
                Patch(_posterBg, a, b, b + Vector3.up * h, a + Vector3.up * h, n, Cream, 2, 3);
                if (_images.TryGetValue(slot.Art, out var pose)) AddArtQuad(pose, c + Vector3.up * 0.1f + n * 0.02f, right, 0.82f, 0.82f, n);
                Signs.Add(new SignSpec { Text = "JOEY DREW STUDIOS PRESENTS", Pos = c + Vector3.up * (h * 0.5f - 0.12f) + n * 0.025f, Rot = rot, Height = 0.055f, Width = 0.9f, Color = new Color(0.12f, 0.07f, 0.04f) });
                Signs.Add(new SignSpec { Text = "BENDY in", Pos = c - Vector3.up * (h * 0.5f - 0.3f) + n * 0.025f, Rot = rot, Height = 0.07f, Width = 0.9f, Color = new Color(0.12f, 0.07f, 0.04f) });
                Signs.Add(new SignSpec { Text = slot.Title, Pos = c - Vector3.up * (h * 0.5f - 0.15f) + n * 0.025f, Rot = rot, Height = Mathf.Min(0.15f, 1.5f / slot.Title.Length), Width = 0.9f, Color = new Color(0.45f, 0.08f, 0.04f) });
            }
            else
            {
                if (_images.TryGetValue(slot.Art, out var still)) AddArtQuad(still, c + n * 0.012f, right, w, h, n, false);
                else Patch(_posterBg, a, b, b + Vector3.up * h, a + Vector3.up * h, n, Cream * 0.6f, 2, 2);
                Box(_box, c - Vector3.up * (h * 0.5f + 0.22f) + n * 0.03f, Abs(right * 1.5f + Vector3.up * 0.17f + n * 0.04f), GoldDark);
                Signs.Add(new SignSpec { Text = slot.Title, Pos = c - Vector3.up * (h * 0.5f + 0.22f) + n * 0.055f, Rot = rot, Height = 0.075f, Width = 1.45f, Color = new Color(0.12f, 0.07f, 0.04f) });
            }

            // The picture lamp.
            var lampPos = c + Vector3.up * (h * 0.5f + 0.2f) + n * 0.18f;
            Box(_box, lampPos, Abs(right * 0.5f + Vector3.up * 0.07f + n * 0.12f), Gold, 1.3f);
            Glow(lampPos - Vector3.up * 0.04f + n * 0.06f, 0.5f, new Color(1f, 0.85f, 0.6f, 0.5f), GlowGroup.Steady);
        }
    }

    /// <summary>One of the game's images on its own quad (art keeps its own texture; Bendy's poses are see-through).</summary>
    private void AddArtQuad(Texture2D tex, Vector3 center, Vector3 right, float w, float h, Vector3 n, bool transparent = true)
    {
        var p = NewPart("Art_" + tex.name, tex, transparent, transparent ? 2970 : 2001);
        var a = center - right * (w * 0.5f) - Vector3.up * (h * 0.5f);
        var b = center + right * (w * 0.5f) - Vector3.up * (h * 0.5f);
        Patch(p, a, b, b + Vector3.up * h, a + Vector3.up * h, n, transparent ? new Color(0.9f, 0.85f, 0.78f) : new Color(1.25f, 1.25f, 1.25f, 1f), 3, 3);
    }

    // =====================================================================================================
    // Ink: it's Joey Drew's studio, after all
    // =====================================================================================================

    private void BuildInk()
    {
        var black = new Color(0.035f, 0.025f, 0.02f, 0.92f);
        void Decal(Vector3 c, Vector3 right, Vector3 up, float w, float h, Vector3 n, bool flipV = false)
        {
            var a = c - right * (w * 0.5f) - up * (h * 0.5f);
            var b = c + right * (w * 0.5f) - up * (h * 0.5f);
            Patch(_ink, a, b, b + up * h, a + up * h, n, black, 2, 2, unlit: true, uvRect: flipV ? new Vector4(0, 1, 1, 0) : new Vector4(0, 0, 1, 1));
        }

        // Splats running down the walls from the cornice...
        Decal(new Vector3(-Half + 0.012f, 8.6f, -2.0f), Vector3.forward, Vector3.up, 1.6f, 2.2f, Vector3.right);
        Decal(new Vector3(Half - 0.012f, 8.2f, -5.4f), Vector3.back, Vector3.up, 1.8f, 2.6f, Vector3.left);
        Decal(new Vector3(-7.3f, 9.2f, ProsZ - 0.012f), Vector3.right, Vector3.up, 1.3f, 1.6f, Vector3.back);
        Decal(new Vector3(4.2f, 8.9f, BackZ + 0.012f), Vector3.left, Vector3.up, 1.5f, 1.9f, Vector3.forward);
        // ...and puddled on the floor.
        Decal(new Vector3(0.3f, 0.009f, 2.4f), Vector3.right, Vector3.forward, 1.2f, 1.2f, Vector3.up);
        Decal(new Vector3(-8.4f, FloorAt(RowZ(7)) + 0.009f, RowZ(7)), Vector3.forward, Vector3.right, 0.8f, 0.9f, Vector3.up);
        Decal(new Vector3(-4.2f, StageY + 0.009f, 4.65f), Vector3.right, Vector3.forward, 1.0f, 0.8f, Vector3.up);
        Decal(new Vector3(6.4f, RowFloor(Rows - 1) + 0.009f, -12.6f), Vector3.right, Vector3.forward, 1.4f, 1.1f, Vector3.up);
    }

    // =====================================================================================================
    // Building the meshes
    // =====================================================================================================

    private void Finish()
    {
        var depth = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "InkVR_TheatreDepth", renderQueue = 1999 };
        depth.SetInt("_SrcBlend", (int)BlendMode.Zero);
        depth.SetInt("_DstBlend", (int)BlendMode.One);
        depth.SetInt("_Cull", (int)CullMode.Off);
        depth.SetInt("_ZWrite", 1);
        depth.SetInt("_ZTest", (int)CompareFunction.LessEqual);
        Keep(depth);

        foreach (var p in _parts)
        {
            if (p.V.Count == 0) continue;
            var mesh = new Mesh { name = "InkVR_Theatre_" + p.Name };
            if (p.V.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            var verts = p.V.ToArray();
            if (p.Group != null)
            {
                var pivot = PivotOf(p.Group);
                for (var i = 0; i < verts.Length; i++) verts[i] -= pivot;
            }

            mesh.SetVertices(verts);
            mesh.SetUVs(0, p.UV.ToArray());
            mesh.SetColors(p.C.ToArray());
            mesh.SetTriangles(p.T.ToArray(), 0);
            mesh.RecalculateBounds();
            Keep(mesh);

            var mat = new Material(Shader.Find("UI/Default")) { name = "InkVR_Theatre_" + p.Name, mainTexture = p.Tex, renderQueue = p.Queue };
            mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
            Keep(mat);

            var go = new GameObject("InkVR_Theatre_" + p.Name) { layer = VRLayers.UI };
            go.transform.SetParent(p.Group != null ? GroupTransform(p.Group) : _root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            if (p.Group != null) _groupMeshes[p.Name] = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = p.Transparent ? new[] { mat } : new[] { depth, mat };
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    private static void Keep(Object o)
    {
        o.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(o);
    }

    // =====================================================================================================
    // Glows (bulbs, sconces, the booth window) and dust in the beam
    // =====================================================================================================

    private enum GlowGroup
    {
        Steady,
        Flicker,
        Chase,
        Projector
    }

    private struct GlowSpec
    {
        public Vector3 Pos;
        public float Size;
        public Color Color;
        public GlowGroup Group;
        public int Index;
    }

    private readonly List<GlowSpec> _glows = new();
    private Mesh _glowMesh;
    private Color[] _glowColors;
    private Vector3[] _glowVerts;
    private Vector3 _glowEye = new(0, 1.6f, 0);

    private void Glow(Vector3 pos, float size, Color color, GlowGroup group, int index = 0) =>
        _glows.Add(new GlowSpec { Pos = pos, Size = size, Color = color, Group = group, Index = index });

    private void BuildGlows()
    {
        var eye = _glowEye;
        var n = _glows.Count;
        var v = _glowVerts = new Vector3[n * 4];
        var uv = new Vector2[n * 4];
        _glowColors = new Color[n * 4];
        var t = new int[n * 6];
        for (var i = 0; i < n; i++)
        {
            var g = _glows[i];
            var f = (eye - g.Pos).normalized;
            var r = Vector3.Cross(Vector3.up, f);
            r = r.sqrMagnitude < 1e-4f ? Vector3.right : r.normalized;
            var u = Vector3.Cross(f, r);
            var h = g.Size * 0.5f;
            v[i * 4] = g.Pos - r * h - u * h;
            v[i * 4 + 1] = g.Pos + r * h - u * h;
            v[i * 4 + 2] = g.Pos + r * h + u * h;
            v[i * 4 + 3] = g.Pos - r * h + u * h;
            uv[i * 4] = new Vector2(0, 0);
            uv[i * 4 + 1] = new Vector2(1, 0);
            uv[i * 4 + 2] = new Vector2(1, 1);
            uv[i * 4 + 3] = new Vector2(0, 1);
            for (var k = 0; k < 4; k++) _glowColors[i * 4 + k] = g.Color;
            t[i * 6] = i * 4;
            t[i * 6 + 1] = i * 4 + 2;
            t[i * 6 + 2] = i * 4 + 1;
            t[i * 6 + 3] = i * 4;
            t[i * 6 + 4] = i * 4 + 3;
            t[i * 6 + 5] = i * 4 + 2;
        }

        _glowMesh = new Mesh { name = "InkVR_TheatreGlows" };
        _glowMesh.SetVertices(v);
        _glowMesh.SetUVs(0, uv);
        _glowMesh.MarkDynamic();
        _glowMesh.SetColors(_glowColors);
        _glowMesh.SetTriangles(t, 0);
        _glowMesh.bounds = new Bounds(new Vector3(0, 5, -9), new Vector3(20, 14, 34));
        Keep(_glowMesh);
        AddRenderer("InkVR_TheatreGlows", _glowMesh, Textures.SoftDot, 3050);
    }

    private void AddRenderer(string name, Mesh mesh, Texture tex, int queue)
    {
        var mat = new Material(Shader.Find("UI/Default")) { name = name, mainTexture = tex, renderQueue = queue };
        mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
        Keep(mat);
        var go = new GameObject(name) { layer = VRLayers.UI };
        go.transform.SetParent(_root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    private const int MoteCount = 70;
    private Mesh _moteMesh;
    private readonly Vector3[] _moteBase = new Vector3[MoteCount];
    private readonly Vector3[] _moteVerts = new Vector3[MoteCount * 4];
    private readonly float[] _motePhase = new float[MoteCount];

    private void BuildMotes()
    {
        var rng = new System.Random(3);
        var screen = new Vector3(0, ScreenCenterY, ScreenZ);
        var hw = ScreenWidth * 0.5f;
        var hh = ScreenWidth * 9f / 32f;
        var uv = new Vector2[MoteCount * 4];
        var col = new Color[MoteCount * 4];
        var t = new int[MoteCount * 6];
        for (var i = 0; i < MoteCount; i++)
        {
            // Mostly near you, where they're big enough to see.
            var k = Mathf.Lerp(0.45f, 0.95f, (float)rng.NextDouble());
            var p = Vector3.Lerp(Projector, screen, k);
            p += new Vector3(((float)rng.NextDouble() - 0.5f) * hw * 1.8f * k, ((float)rng.NextDouble() - 0.5f) * hh * 1.8f * k, 0);
            _moteBase[i] = p;
            _motePhase[i] = (float)rng.NextDouble() * 100f;
            for (var c = 0; c < 4; c++) col[i * 4 + c] = new Color(1f, 0.95f, 0.8f, 0.55f);
            uv[i * 4] = new Vector2(0, 0);
            uv[i * 4 + 1] = new Vector2(1, 0);
            uv[i * 4 + 2] = new Vector2(1, 1);
            uv[i * 4 + 3] = new Vector2(0, 1);
            t[i * 6] = i * 4;
            t[i * 6 + 1] = i * 4 + 2;
            t[i * 6 + 2] = i * 4 + 1;
            t[i * 6 + 3] = i * 4;
            t[i * 6 + 4] = i * 4 + 3;
            t[i * 6 + 5] = i * 4 + 2;
        }

        _moteMesh = new Mesh { name = "InkVR_TheatreMotes" };
        UpdateMotes(0, new Vector3(0, 1.6f, 0));
        _moteMesh.SetUVs(0, uv);
        _moteMesh.SetColors(col);
        _moteMesh.SetTriangles(t, 0);
        _moteMesh.bounds = new Bounds(new Vector3(0, 5, -4), new Vector3(20, 12, 24));
        Keep(_moteMesh);
        AddRenderer("InkVR_TheatreMotes", _moteMesh, Textures.SoftDot, 3920);
    }

    private void UpdateMotes(float time, Vector3 eye)
    {
        for (var i = 0; i < MoteCount; i++)
        {
            var ph = _motePhase[i];
            var p = _moteBase[i] + new Vector3(Mathf.Sin(time * 0.21f + ph) * 0.35f, Mathf.Sin(time * 0.17f + ph * 1.3f) * 0.25f, Mathf.Sin(time * 0.13f + ph * 0.7f) * 0.35f);
            var f = (eye - p).normalized;
            var r = Vector3.Cross(Vector3.up, f);
            r = r.sqrMagnitude < 1e-4f ? Vector3.right : r.normalized;
            var u = Vector3.Cross(f, r);
            const float h = 0.008f;
            _moteVerts[i * 4] = p - r * h - u * h;
            _moteVerts[i * 4 + 1] = p + r * h - u * h;
            _moteVerts[i * 4 + 2] = p + r * h + u * h;
            _moteVerts[i * 4 + 3] = p - r * h + u * h;
        }

        _moteMesh.SetVertices(_moteVerts);
    }

    // =====================================================================================================
    // Animation
    // =====================================================================================================

    /// <summary>Sconces breathe, the marquee chases, the booth window flickers with the projector, dust drifts.</summary>
    public void Animate(float time, float projector, bool calm, Vector3 eyeLocal)
    {
        if (_glowMesh == null) return;
        var step = Mathf.FloorToInt(time * 7f);
        var turn = (eyeLocal - _glowEye).sqrMagnitude > 0.02f * 0.02f;
        if (turn) _glowEye = eyeLocal;
        for (var i = 0; i < _glows.Count; i++)
        {
            var g = _glows[i];
            var k = 1f;
            var toEye = eyeLocal - g.Pos;
            var dist = toEye.magnitude;
            if (turn)
            {
                var f = toEye / Mathf.Max(dist, 1e-4f);
                var r = Vector3.Cross(Vector3.up, f);
                r = r.sqrMagnitude < 1e-4f ? Vector3.right : r.normalized;
                var u = Vector3.Cross(f, r);
                var h = g.Size * 0.5f;
                _glowVerts[i * 4] = g.Pos - r * h - u * h;
                _glowVerts[i * 4 + 1] = g.Pos + r * h - u * h;
                _glowVerts[i * 4 + 2] = g.Pos + r * h + u * h;
                _glowVerts[i * 4 + 3] = g.Pos - r * h + u * h;
            }

            // walked up to: a glow fades rather than filling your eyes
            k *= Mathf.Clamp01((dist - g.Size * 0.35f) / (g.Size * 0.65f + 0.1f));
            switch (g.Group)
            {
                case GlowGroup.Flicker:
                    k = calm ? 1f : 0.88f + 0.12f * Mathf.PerlinNoise(time * 2.5f + i * 0.37f, 0.5f);
                    break;
                case GlowGroup.Chase:
                    k = calm ? 0.8f : (((step - g.Index) % 4 + 4) % 4 == 0 ? 1f : 0.42f);
                    break;
                case GlowGroup.Projector:
                    k = projector;
                    break;
            }

            var c = new Color(g.Color.r, g.Color.g, g.Color.b, g.Color.a * k);
            _glowColors[i * 4] = _glowColors[i * 4 + 1] = _glowColors[i * 4 + 2] = _glowColors[i * 4 + 3] = c;
        }

        if (turn) _glowMesh.SetVertices(_glowVerts);
        _glowMesh.SetColors(_glowColors);
        UpdateMotes(time, eyeLocal);
    }
}
