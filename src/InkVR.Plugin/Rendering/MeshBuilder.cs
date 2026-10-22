using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace InkVR.Rendering;

/// <summary>Builds simple procedural meshes (tubes, capsules, ellipsoids, rings) with UVs and smooth normals.</summary>
public class MeshBuilder
{
    private readonly List<Vector3> _v = new();
    private readonly List<Vector3> _n = new();
    private readonly List<Vector2> _uv = new();
    private readonly List<Color> _c = new();
    private readonly List<int> _t = new();
    public Color VertexColor = Color.white;

    private int Vert(Vector3 p, Vector3 n, Vector2 uv)
    {
        _v.Add(p);
        _n.Add(n);
        _uv.Add(uv);
        _c.Add(VertexColor);
        return _v.Count - 1;
    }

    /// <summary>Set per shape: true when that shape's natural order is counter-clockwise from outside.</summary>
    private bool _flip;

    private void Tri(int a, int b, int c)
    {
        _t.Add(a);
        if (_flip)
        {
            _t.Add(c);
            _t.Add(b);
        }
        else
        {
            _t.Add(b);
            _t.Add(c);
        }
    }

    /// <summary>
    /// A tube along a polyline of centres with per-ring radii (ellipse: radius * (x scale, y scale)),
    /// optional rounded caps. u wraps around, v runs along the length.
    /// </summary>
    public void Tube(Vector3[] centers, float[] radii, Vector3 up, int sides = 14, bool capStart = true, bool capEnd = true, Vector2? ellipse = null)
    {
        var el = ellipse ?? Vector2.one;
        var rings = centers.Length;
        _flip = true;
        var total = 0f;
        var lengths = new float[rings];
        for (var i = 1; i < rings; i++) lengths[i] = total += Vector3.Distance(centers[i - 1], centers[i]);

        var start = _v.Count;
        for (var i = 0; i < rings; i++)
        {
            var fwd = (i < rings - 1 ? centers[i + 1] - centers[i] : centers[i] - centers[i - 1]).normalized;
            if (i > 0 && i < rings - 1) fwd = (centers[i + 1] - centers[i - 1]).normalized;
            var right = Vector3.Cross(up, fwd).normalized;
            if (right.sqrMagnitude < 1e-4f) right = Vector3.Cross(Vector3.forward, fwd).normalized;
            var ringUp = Vector3.Cross(fwd, right);
            for (var s = 0; s <= sides; s++)
            {
                var a = (float)s / sides * Mathf.PI * 2;
                var dir = right * (Mathf.Cos(a) * el.x) + ringUp * (Mathf.Sin(a) * el.y);
                var nrm = (right * (Mathf.Cos(a) / el.x) + ringUp * (Mathf.Sin(a) / el.y)).normalized;
                Vert(centers[i] + dir * radii[i], nrm, new Vector2((float)s / sides, total > 0 ? lengths[i] / total : 0));
            }
        }

        for (var i = 0; i < rings - 1; i++)
        for (var s = 0; s < sides; s++)
        {
            var a = start + i * (sides + 1) + s;
            var b = a + sides + 1;
            Tri(a, b, a + 1);
            Tri(a + 1, b, b + 1);
        }

        if (capStart) Cap(centers[0], (centers[0] - centers[1]).normalized, radii[0], up, sides, el, 0f);
        if (capEnd) Cap(centers[rings - 1], (centers[rings - 1] - centers[rings - 2]).normalized, radii[rings - 1], up, sides, el, 1f);
        _flip = false;
    }

    /// <summary>A hemispherical cap pointing along <paramref name="outward"/>.</summary>
    private void Cap(Vector3 center, Vector3 outward, float r, Vector3 up, int sides, Vector2 el, float v)
    {
        var right = Vector3.Cross(up, outward).normalized;
        if (right.sqrMagnitude < 1e-4f) right = Vector3.Cross(Vector3.forward, outward).normalized;
        var ringUp = Vector3.Cross(outward, right);
        const int stacks = 4;
        var start = _v.Count;
        for (var k = 0; k <= stacks; k++)
        {
            var phi = (float)k / stacks * Mathf.PI * 0.5f;
            var rr = Mathf.Cos(phi);
            var h = Mathf.Sin(phi);
            for (var s = 0; s <= sides; s++)
            {
                var a = (float)s / sides * Mathf.PI * 2;
                var dir = right * (Mathf.Cos(a) * el.x * rr) + ringUp * (Mathf.Sin(a) * el.y * rr) + outward * h;
                Vert(center + dir * r, dir.normalized, new Vector2((float)s / sides, v));
            }
        }

        var flip = Vector3.Dot(Vector3.Cross(right, ringUp), outward) < 0;
        for (var k = 0; k < stacks; k++)
        for (var s = 0; s < sides; s++)
        {
            var a = start + k * (sides + 1) + s;
            var b = a + sides + 1;
            if (flip)
            {
                Tri(a, a + 1, b);
                Tri(a + 1, b + 1, b);
            }
            else
            {
                Tri(a, b, a + 1);
                Tri(a + 1, b, b + 1);
            }
        }
    }

    /// <summary>A capsule between two points with different end radii.</summary>
    public void Capsule(Vector3 a, Vector3 b, float ra, float rb, Vector3 up, int sides = 12) =>
        Tube(new[] { a, b }, new[] { ra, rb }, up, sides);

    /// <summary>An ellipsoid (UV sphere) with radii along its local axes.</summary>
    public void Ellipsoid(Vector3 center, Vector3 radii, Quaternion rotation, int sides = 16, int stacks = 10)
    {
        var start = _v.Count;
        for (var k = 0; k <= stacks; k++)
        {
            var th = (float)k / stacks * Mathf.PI;
            for (var s = 0; s <= sides; s++)
            {
                var ph = (float)s / sides * Mathf.PI * 2;
                var unit = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                var p = Vector3.Scale(unit, radii);
                var n = new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z).normalized;
                Vert(center + rotation * p, rotation * n, new Vector2((float)s / sides, 1 - (float)k / stacks));
            }
        }

        for (var k = 0; k < stacks; k++)
        for (var s = 0; s < sides; s++)
        {
            var a = start + k * (sides + 1) + s;
            var b = a + sides + 1;
            Tri(a, a + 1, b);
            Tri(a + 1, b + 1, b);
        }
    }

    /// <summary>A torus around <paramref name="axis"/>.</summary>
    public void Torus(Vector3 center, Vector3 axis, float radius, float tube, int seg = 24, int sides = 8)
    {
        axis = axis.normalized;
        var right = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        var fwd = Vector3.Cross(right, axis);
        var start = _v.Count;
        for (var i = 0; i <= seg; i++)
        {
            var a = (float)i / seg * Mathf.PI * 2;
            var ringC = center + (right * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * radius;
            var outDir = (ringC - center).normalized;
            for (var s = 0; s <= sides; s++)
            {
                var b = (float)s / sides * Mathf.PI * 2;
                var n = outDir * Mathf.Cos(b) + axis * Mathf.Sin(b);
                Vert(ringC + n * tube, n, new Vector2((float)i / seg, (float)s / sides));
            }
        }

        for (var i = 0; i < seg; i++)
        for (var s = 0; s < sides; s++)
        {
            var a = start + i * (sides + 1) + s;
            var b = a + sides + 1;
            Tri(a, a + 1, b);
            Tri(a + 1, b + 1, b);
        }
    }

    /// <summary>A flat quad from four corners (counter-clockwise when seen from the front).</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color? ca = null, Color? cb = null, Color? cc = null, Color? cd = null)
    {
        var n = Vector3.Cross(b - a, d - a).normalized;
        var keep = VertexColor;
        VertexColor = ca ?? keep;
        var i0 = Vert(a, n, new Vector2(0, 0));
        VertexColor = cb ?? keep;
        var i1 = Vert(b, n, new Vector2(1, 0));
        VertexColor = cc ?? keep;
        var i2 = Vert(c, n, new Vector2(1, 1));
        VertexColor = cd ?? keep;
        var i3 = Vert(d, n, new Vector2(0, 1));
        VertexColor = keep;
        Tri(i0, i3, i2);
        Tri(i0, i2, i1);
    }

    public Mesh ToMesh(string name)
    {
        var m = new Mesh { name = name };
        if (_v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
        m.SetVertices(_v.ToArray());
        m.SetNormals(_n.ToArray());
        m.SetUVs(0, _uv.ToArray());
        m.SetColors(_c.ToArray());
        m.SetTriangles(_t.ToArray(), 0);
        m.RecalculateBounds();
        Object.DontDestroyOnLoad(m);
        return m;
    }

    /// <summary>Inverted-hull ink outline: the same surface pushed out along its normals (drawn front-culled).</summary>
    public Mesh ToOutlineMesh(string name, float thickness)
    {
        var verts = new Vector3[_v.Count];
        for (var i = 0; i < verts.Length; i++) verts[i] = _v[i] + _n[i] * thickness;
        var m = new Mesh { name = name };
        m.SetVertices(verts);
        m.SetNormals(_n.ToArray());
        m.SetTriangles(_t.ToArray(), 0);
        m.RecalculateBounds();
        Object.DontDestroyOnLoad(m);
        return m;
    }
}

/// <summary>Materials shared by InkVR's procedural objects.</summary>
public static class InkMaterials
{
    private static Material _outline;

    /// <summary>Black ink outline: unlit, front faces culled (shows the inverted hull's silhouette).</summary>
    public static Material Outline
    {
        get
        {
            if (_outline != null) return _outline;
            _outline = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "InkVR_Outline" };
            _outline.SetColor("_Color", new Color(0.035f, 0.025f, 0.02f, 1f));
            _outline.SetInt("_SrcBlend", (int)BlendMode.One);
            _outline.SetInt("_DstBlend", (int)BlendMode.Zero);
            _outline.SetInt("_Cull", (int)CullMode.Front);
            _outline.SetInt("_ZWrite", 1);
            _outline.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            Object.DontDestroyOnLoad(_outline);
            return _outline;
        }
    }

    /// <summary>
    /// A lit material using the game's own environment shader (Meatly Shaders/Surfaces/Diffuse), so InkVR
    /// objects get the same lighting, ink-wave normals and "Bendy is near" ink effect as the world.
    /// </summary>
    public static Material GameSurface(Texture2D albedo, string name)
    {
        var shader = Shader.Find("Meatly Shaders/Surfaces/Diffuse") ?? Shader.Find("Standard");
        var m = new Material(shader) { name = name };
        m.SetTexture("_MainTex", albedo);
        m.SetColor("_Color", Color.white);
        var reference = FindEnvironmentMaterial();
        if (reference != null)
        {
            if (reference.HasProperty("_Normals")) m.SetTexture("_Normals", reference.GetTexture("_Normals"));
            if (reference.HasProperty("_BendyEffect")) m.SetTexture("_BendyEffect", reference.GetTexture("_BendyEffect"));
        }

        Object.DontDestroyOnLoad(m);
        return m;
    }

    private static Material FindEnvironmentMaterial()
    {
        var shader = Shader.Find("Meatly Shaders/Surfaces/Diffuse");
        if (shader == null) return null;
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Material>()))
        {
            var m = o.Cast<Material>();
            if (m.shader == shader && m.HasProperty("_Normals") && m.GetTexture("_Normals") != null) return m;
        }

        return null;
    }
}
