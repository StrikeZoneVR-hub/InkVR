using System.Collections.Generic;
using InkVR.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace InkVR.UI;

/// <summary>
/// The hall comes alive: a red house curtain that parts for the show, two follow spots from the booth that sweep the
/// stage as it does, ink dripping from the ceiling into puddles that ripple. (The house lights and the screen's glow
/// are the bake's live layers: <see cref="SetHouseLights"/>, <see cref="SetScreenLight"/>.)
/// </summary>
public sealed partial class Theatre
{
    // =====================================================================================================
    // The house curtain
    // =====================================================================================================

    private const int CurtainU = 40, CurtainV = 16;
    private const float CurtainZ = ProsZ + 0.46f, CurtainEdge = OpenHalf + 0.05f;
    private float _curtainOpen = -1f;

    private void BuildHouseCurtain()
    {
        foreach (var s in new[] { -1f, 1f })
        {
            var name = s < 0 ? "CurtainL" : "CurtainR";
            InGroup(name, Vector3.zero, () =>
            {
                var pts = CurtainPoints(s, 0f);
                Surface(_curtain, pts, CurtainU, CurtainV, Velvet * 1.05f, 9f, 1.8f);
                // a gold fringe along the hem
                for (var i = 0; i < CurtainU; i++)
                {
                    var a = pts[i] + Vector3.back * 0.015f;
                    var b = pts[i + 1] + Vector3.back * 0.015f;
                    Patch(_ornament, a + Vector3.down * 0.02f, b + Vector3.down * 0.02f, b + Vector3.up * 0.1f, a + Vector3.up * 0.1f, Vector3.back, Gold, 1, 1, uvRect: new Vector4(i * 0.25f, 0.05f, (i + 1) * 0.25f, 0.45f));
                }
            });
        }
    }

    /// <summary>The curtain half's grid: closed (0) it meets its twin in the middle; open (1) it's gathered at the side.</summary>
    private static Vector3[] CurtainPoints(float s, float open)
    {
        var pts = new Vector3[(CurtainU + 1) * (CurtainV + 1)];
        var e = Mathf.SmoothStep(0, 1, open);
        var inner = Mathf.Lerp(-0.06f, OpenHalf - 0.95f, e); // its leading edge (overlaps its twin a little when closed)
        var depth = Mathf.Lerp(0.07f, 0.17f, e);
        for (var j = 0; j <= CurtainV; j++)
        {
            var fv = (float)j / CurtainV;
            var y = Mathf.Lerp(StageY + 0.015f, OpenTop + 0.25f, fv);
            for (var i = 0; i <= CurtainU; i++)
            {
                var fu = (float)i / CurtainU;
                var x = Mathf.Lerp(inner, CurtainEdge, fu);
                // the leading edge swings up and out a little as it's drawn back, the way a heavy traveller drags
                var lift = e * (1 - fu) * 0.25f * (1 - fv) * (1 - fv);
                var fold = Mathf.Sin(fu * Mathf.PI * 2 * 9f + s * 0.7f) * depth * (0.6f + 0.4f * fv);
                pts[j * (CurtainU + 1) + i] = new Vector3(s * x, y + lift, CurtainZ + fold);
            }
        }

        return pts;
    }

    /// <summary>Draw the house curtain to this far open (0 closed, 1 open); it reshapes only when it moves.</summary>
    public void SetCurtain(float open)
    {
        open = Mathf.Clamp01(open);
        if (Mathf.Abs(open - _curtainOpen) < 0.0005f) return;
        _curtainOpen = open;
        foreach (var s in new[] { -1f, 1f })
        {
            var name = s < 0 ? "CurtainL" : "CurtainR";
            var pts = CurtainPoints(s, open);
            if (_groupMeshes.TryGetValue("Drapes_" + name, out var cloth) && cloth != null)
            {
                var verts = cloth.vertices;
                for (var i = 0; i < Mathf.Min(pts.Length, verts.Length); i++) verts[i] = pts[i];
                cloth.vertices = verts;
                cloth.RecalculateBounds();
            }

            // the fringe along the hem: four corners a segment (bottom left, bottom right, top left, top right)
            if (_groupMeshes.TryGetValue("Frieze_" + name, out var fringe) && fringe != null)
            {
                var verts = fringe.vertices;
                for (var i = 0; i < CurtainU && i * 4 + 3 < verts.Length; i++)
                {
                    var a = pts[i] + Vector3.back * 0.015f;
                    var b = pts[i + 1] + Vector3.back * 0.015f;
                    verts[i * 4] = a + Vector3.down * 0.02f;
                    verts[i * 4 + 1] = b + Vector3.down * 0.02f;
                    verts[i * 4 + 2] = a + Vector3.up * 0.1f;
                    verts[i * 4 + 3] = b + Vector3.up * 0.1f;
                }

                fringe.vertices = verts;
                fringe.RecalculateBounds();
            }
        }
    }

    // =====================================================================================================
    // Follow spots
    // =====================================================================================================

    private sealed class Spot
    {
        public Vector3 From;
        public Mesh Pool, Beam;
        public Material PoolMat, BeamMat;
        public Transform PoolT, BeamT;
    }

    private readonly Spot[] _spots = new Spot[2];

    private void BuildSpots()
    {
        for (var i = 0; i < 2; i++)
        {
            var sp = new Spot { From = new Vector3(i == 0 ? -1.7f : 1.7f, Projector.y, BackZ + 0.2f) };
            // the pool: a soft disc of light (bright middle, fading to nothing at the rim), added to whatever it lands on
            var pool = new MeshBuilder();
            const int seg = 40;
            var hot = new Color(1f, 0.93f, 0.78f, 1f);
            for (var k = 0; k < seg; k++)
            {
                var a0 = k / (float)seg * Mathf.PI * 2;
                var a1 = (k + 1) / (float)seg * Mathf.PI * 2;
                var p0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0);
                var p1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0);
                pool.Quad(p0 * 0.6f, p1 * 0.6f, p1, p0, hot * 0.85f, hot * 0.85f, Color.black, Color.black);
                pool.Quad(Vector3.zero, Vector3.zero, p1 * 0.6f, p0 * 0.6f, hot, hot, hot * 0.85f, hot * 0.85f);
            }

            sp.Pool = pool.ToMesh("InkVR_SpotPool" + i);
            sp.PoolMat = Additive("InkVR_SpotPool", 3000, CompareFunction.LessEqual);
            sp.PoolT = AddMeshObject("InkVR_SpotPool" + i, sp.Pool, sp.PoolMat);

            // the beam: a long faint cone from the booth window
            var beam = new MeshBuilder();
            var near = new Color(1f, 0.92f, 0.75f, 1f) * 0.05f;
            var far = new Color(1f, 0.92f, 0.75f, 1f) * 0.025f;
            for (var k = 0; k < 24; k++)
            {
                var a0 = k / 24f * Mathf.PI * 2;
                var a1 = (k + 1) / 24f * Mathf.PI * 2;
                var r0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 1);
                var r1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 1);
                beam.Quad(new Vector3(r0.x * 0.02f, r0.y * 0.02f, 0), new Vector3(r1.x * 0.02f, r1.y * 0.02f, 0), r1, r0, near, near, far, far);
            }

            sp.Beam = beam.ToMesh("InkVR_SpotBeam" + i);
            sp.BeamMat = Additive("InkVR_SpotBeam", 3910, CompareFunction.LessEqual);
            sp.BeamT = AddMeshObject("InkVR_SpotBeam" + i, sp.Beam, sp.BeamMat);
            sp.PoolT.gameObject.SetActive(false);
            sp.BeamT.gameObject.SetActive(false);
            _spots[i] = sp;
        }
    }

    private Material Additive(string name, int queue, CompareFunction ztest)
    {
        var m = new Material(Shader.Find("Hidden/Internal-Colored")) { name = name, renderQueue = queue };
        m.SetInt("_SrcBlend", (int)BlendMode.One);
        m.SetInt("_DstBlend", (int)BlendMode.One);
        m.SetInt("_Cull", (int)CullMode.Off);
        m.SetInt("_ZWrite", 0);
        m.SetInt("_ZTest", (int)ztest);
        m.SetColor("_Color", Color.white);
        Keep(m);
        return m;
    }

    private Transform AddMeshObject(string name, Mesh mesh, Material mat)
    {
        Keep(mesh);
        var go = new GameObject(name) { layer = VRLayers.UI };
        go.transform.SetParent(_root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    /// <summary>
    /// Aim follow spot i at a point on the stage (it lands on the curtain if the point is behind its line, else on the
    /// boards), this bright (0 = off) and this wide (pool radius, metres).
    /// </summary>
    public void SetSpot(int i, Vector3 target, float level, float radius)
    {
        var sp = _spots[i];
        if (sp == null) return;
        var on = level > 0.01f;
        if (sp.PoolT.gameObject.activeSelf != on) sp.PoolT.gameObject.SetActive(on);
        if (sp.BeamT.gameObject.activeSelf != on) sp.BeamT.gameObject.SetActive(on);
        if (!on) return;

        // where it lands: the curtain (or whatever's at the curtain's line) or the stage floor
        var onCurtain = target.z > ProsZ + 0.2f;
        var at = onCurtain ? new Vector3(target.x, Mathf.Max(StageY + radius * 0.6f, target.y), _curtainOpen > 0.6f ? ScreenZ - 0.04f : CurtainZ - 0.2f) : new Vector3(target.x, StageY + 0.012f, target.z);
        var dir = (at - sp.From).normalized;
        sp.PoolT.localPosition = at;
        // stretched along the beam where it strikes at a slant
        var normal = onCurtain ? Vector3.back : Vector3.up;
        sp.PoolT.localRotation = Quaternion.LookRotation(-normal, onCurtain ? Vector3.up : Vector3.forward);
        var slant = 1f / Mathf.Max(0.35f, Mathf.Abs(Vector3.Dot(dir, normal)));
        sp.PoolT.localScale = new Vector3(radius, radius * (onCurtain ? 1f : slant), 1f);
        sp.PoolMat.SetColor("_Color", new Color(level, level, level, 1f) * 0.55f);

        var len = (at - sp.From).magnitude;
        sp.BeamT.localPosition = sp.From;
        sp.BeamT.localRotation = Quaternion.LookRotation(dir);
        sp.BeamT.localScale = new Vector3(radius, radius, len);
        sp.BeamMat.SetColor("_Color", new Color(level, level, level, 1f));
    }

    /// <summary>Aim follow spot i from its nose along dir: it lands on the screen (or curtain), the stage, or the floor.</summary>
    public void AimSpot(int i, Vector3 nose, Vector3 dir, float level)
    {
        var sp = _spots[i];
        if (sp == null) return;
        Vector3 at, normal;
        var t = dir.z > 0.01f ? (ScreenZ - 0.05f - nose.z) / dir.z : -1f;
        var hit = nose + dir * t;
        if (t > 0 && Mathf.Abs(hit.x) < OpenHalf && hit.y > StageY && hit.y < OpenTop)
        {
            at = new Vector3(hit.x, hit.y, _curtainOpen > 0.6f ? ScreenZ - 0.04f : CurtainZ - 0.2f);
            normal = Vector3.back;
        }
        else
        {
            // down onto the floor: the stage's boards, or the hall's (stepping up the rows)
            var y = StageY;
            for (var k = 0; k < 3; k++)
            {
                var tf = dir.y < -0.01f ? (y - nose.y) / dir.y : 50f;
                var p = nose + dir * tf;
                y = p.z > ApronZ ? StageY : FloorAt(p.z);
            }

            var tt = dir.y < -0.01f ? (y + 0.012f - nose.y) / dir.y : 50f;
            at = nose + dir * tt;
            normal = Vector3.up;
        }

        PlaceSpot(sp, at, normal, level, 0.9f);
    }

    private void PlaceSpot(Spot sp, Vector3 at, Vector3 normal, float level, float radius)
    {
        var on = level > 0.01f;
        if (sp.PoolT.gameObject.activeSelf != on) sp.PoolT.gameObject.SetActive(on);
        if (sp.BeamT.gameObject.activeSelf != on) sp.BeamT.gameObject.SetActive(on);
        if (!on) return;
        var dir = (at - sp.From).normalized;
        sp.PoolT.localPosition = at;
        var vertical = Mathf.Abs(normal.y) < 0.5f;
        sp.PoolT.localRotation = Quaternion.LookRotation(-normal, vertical ? Vector3.up : Vector3.forward);
        var slant = 1f / Mathf.Max(0.35f, Mathf.Abs(Vector3.Dot(dir, normal)));
        sp.PoolT.localScale = new Vector3(radius, radius * (vertical ? 1f : slant), 1f);
        sp.PoolMat.SetColor("_Color", new Color(level, level, level, 1f) * 0.55f);
        sp.BeamT.localPosition = sp.From;
        sp.BeamT.localRotation = Quaternion.LookRotation(dir);
        sp.BeamT.localScale = new Vector3(radius, radius, (at - sp.From).magnitude);
        sp.BeamMat.SetColor("_Color", new Color(level, level, level, 1f));
    }

    // =====================================================================================================
    // Ink dripping from the ceiling
    // =====================================================================================================

    private struct Drip
    {
        public Vector3 From;
        public float FloorY, Period, Phase;
    }

    private readonly List<Drip> _ceilingDrips = new();
    private Mesh _dripMesh;
    private Vector3[] _dripVerts;
    private Color[] _dripColors;
    private const int DripQuads = 3; // the drop, its splash ring, a fleck

    private void BuildDrips()
    {
        void Add(Vector3 from, float floor, float period, float phase) => _ceilingDrips.Add(new Drip { From = from, FloorY = floor, Period = period, Phase = phase });
        // under the splats running down from the cornice, and from the ceiling over the aisle
        Add(new Vector3(-Half + 0.2f, CorniceY - 0.4f, -2.0f), FloorAt(-2.0f) + 0.01f, 5.3f, 0.2f);
        Add(new Vector3(Half - 0.2f, CorniceY - 0.6f, -5.4f), FloorAt(-5.4f) + 0.01f, 6.1f, 2.1f);
        Add(new Vector3(0.3f, CeilingY - 0.4f, 2.4f), 0.01f, 7.7f, 1.3f);
        Add(new Vector3(6.4f, CeilingY - 0.4f, -12.6f), RowFloor(Rows - 1) + 0.01f, 8.9f, 4.0f);
        var n = _ceilingDrips.Count * DripQuads;
        _dripVerts = new Vector3[n * 4];
        _dripColors = new Color[n * 4];
        var uv = new Vector2[n * 4];
        var t = new int[n * 6];
        for (var i = 0; i < n; i++)
        {
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

        _dripMesh = new Mesh { name = "InkVR_TheatreDrips" };
        _dripMesh.SetVertices(_dripVerts);
        _dripMesh.SetUVs(0, uv);
        _dripMesh.SetColors(_dripColors);
        _dripMesh.SetTriangles(t, 0);
        _dripMesh.MarkDynamic();
        _dripMesh.bounds = new Bounds(new Vector3(0, 5, -4), new Vector3(20, 14, 24));
        Keep(_dripMesh);
        AddRenderer("InkVR_TheatreDrips", _dripMesh, Textures.SoftDot, 3040);
    }

    private void AnimateDrips(float time, Vector3 eye)
    {
        if (_dripMesh == null) return;
        var ink = new Color(0.03f, 0.022f, 0.018f, 1f);
        for (var d = 0; d < _ceilingDrips.Count; d++)
        {
            var dr = _ceilingDrips[d];
            var fall = dr.From.y - dr.FloorY;
            var tFall = Mathf.Sqrt(2f * fall / 9.81f);
            var cycle = Mathf.Repeat(time + dr.Phase, dr.Period);
            // a drop gathers (0.8 s), falls, then splashes and rings out
            var gather = cycle < 0.8f;
            var falling = !gather && cycle < 0.8f + tFall;
            var after = cycle - 0.8f - tFall;
            var y = gather ? dr.From.y - cycle * 0.02f : falling ? dr.From.y - 0.5f * 9.81f * (cycle - 0.8f) * (cycle - 0.8f) : dr.FloorY;
            var dropPos = new Vector3(dr.From.x, y, dr.From.z);
            var dropSize = gather ? Mathf.Lerp(0.01f, 0.035f, cycle / 0.8f) : falling ? 0.035f : 0f;
            Quad(d * DripQuads, dropPos, dropSize, eye, ink, true, falling ? 1.8f : 1f);
            // the splash: a ring that spreads and fades, lying on the floor
            var ring = after > 0 && after < 1.2f;
            var rr = ring ? Mathf.Lerp(0.05f, 0.35f, after / 1.2f) : 0f;
            FloorQuad(d * DripQuads + 1, new Vector3(dr.From.x, dr.FloorY + 0.006f, dr.From.z), rr, ring ? new Color(ink.r, ink.g, ink.b, (1 - after / 1.2f) * 0.6f) : Color.clear);
            // a fleck thrown up
            var fleck = after > 0 && after < 0.35f;
            var fy = fleck ? dr.FloorY + Mathf.Sin(after / 0.35f * Mathf.PI) * 0.12f : 0f;
            Quad(d * DripQuads + 2, new Vector3(dr.From.x + after * 0.3f, fy, dr.From.z), fleck ? 0.018f : 0f, eye, ink, false, 1f);
        }

        _dripMesh.SetVertices(_dripVerts);
        _dripMesh.SetColors(_dripColors);
    }

    private void Quad(int i, Vector3 p, float size, Vector3 eye, Color c, bool upright, float stretch)
    {
        var f = (eye - p).normalized;
        var r = Vector3.Cross(Vector3.up, f);
        r = r.sqrMagnitude < 1e-4f ? Vector3.right : r.normalized;
        var u = upright ? Vector3.up : Vector3.Cross(f, r);
        var h = size * 0.5f;
        _dripVerts[i * 4] = p - r * h - u * h * stretch;
        _dripVerts[i * 4 + 1] = p + r * h - u * h * stretch;
        _dripVerts[i * 4 + 2] = p + r * h + u * h * stretch;
        _dripVerts[i * 4 + 3] = p - r * h + u * h * stretch;
        var col = size > 0 ? c : Color.clear;
        _dripColors[i * 4] = _dripColors[i * 4 + 1] = _dripColors[i * 4 + 2] = _dripColors[i * 4 + 3] = col;
    }

    private void FloorQuad(int i, Vector3 p, float r, Color c)
    {
        _dripVerts[i * 4] = p + new Vector3(-r, 0, -r);
        _dripVerts[i * 4 + 1] = p + new Vector3(r, 0, -r);
        _dripVerts[i * 4 + 2] = p + new Vector3(r, 0, r);
        _dripVerts[i * 4 + 3] = p + new Vector3(-r, 0, r);
        _dripColors[i * 4] = _dripColors[i * 4 + 1] = _dripColors[i * 4 + 2] = _dripColors[i * 4 + 3] = c;
    }
}
