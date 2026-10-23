using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using InkVR.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InkVR.Body;

/// <summary>A vertex's one or two bones (indices into the skeleton) and their weights.</summary>
public struct SkinWeight
{
    public int B0, B1;
    public float W0, W1;

    public static SkinWeight Rigid(int bone) => new() { B0 = bone, W0 = 1f, B1 = -1 };

    /// <summary>From bone a to bone b as t goes 0 → 1.</summary>
    public static SkinWeight Blend(int a, int b, float t)
    {
        t = Mathf.Clamp01(t);
        if (t <= 0.001f) return Rigid(a);
        if (t >= 0.999f) return Rigid(b);
        return t < 0.5f ? new SkinWeight { B0 = a, W0 = 1 - t, B1 = b, W1 = t } : new SkinWeight { B0 = b, W0 = t, B1 = a, W1 = 1 - t };
    }
}

/// <summary>
/// Builds skinned meshes (Henry's body, the lifelike hands) from procedural pieces: each piece is a
/// <see cref="MeshBuilder"/> in the skeleton's bind space, a material, an ink outline thickness, and a
/// weight for every vertex. Pieces sharing a material become one mesh (or one sub-mesh); the outlines of
/// all pieces become one inverted-hull mesh skinned the same way.
/// </summary>
public sealed class SkinBuilder
{
    private sealed class Piece
    {
        public MeshBuilder Mesh;
        public string Material;
        public float Outline;
        public SkinWeight[] Weights;
    }

    private readonly List<Piece> _pieces = new();

    public void Add(MeshBuilder mb, string material, float outline, Func<Vector3, SkinWeight> weigh)
    {
        var v = mb.VertexArray;
        var w = new SkinWeight[v.Length];
        for (var i = 0; i < v.Length; i++) w[i] = weigh(v[i]);
        _pieces.Add(new Piece { Mesh = mb, Material = material, Outline = outline, Weights = w });
    }

    /// <summary>One material's renderer from <see cref="BuildSeparate"/>, with what painting ink onto it needs.</summary>
    public sealed class Piece2
    {
        public SkinnedMeshRenderer Renderer;
        public MeshBuilder Geometry;
        public string Material;
        public SkinWeight[] Weights;
    }

    /// <summary>
    /// One renderer per material (each can then carry its own paintable texture), plus the outline.
    /// </summary>
    public List<Piece2> BuildSeparate(Transform parent, Transform[] bones, Transform rootBone, Func<string, Material> material, string name,
        out SkinnedMeshRenderer outline, out Matrix4x4[] bindposes)
    {
        var result = new List<Piece2>();
        bindposes = Bindposes(parent, bones);
        var groups = new List<string>();
        foreach (var p in _pieces)
            if (!groups.Contains(p.Material))
                groups.Add(p.Material);

        foreach (var key in groups)
        {
            var merged = new MeshBuilder();
            var weights = new List<SkinWeight>();
            foreach (var p in _pieces)
            {
                if (p.Material != key) continue;
                merged.Append(p.Mesh);
                weights.AddRange(p.Weights);
            }

            var mesh = merged.ToMesh($"{name}_{key}");
            SetWeights(mesh, weights);
            mesh.bindposes = bindposes;
            var smr = Renderer(parent, $"{name}_{key}", mesh, new[] { material(key) }, bones, rootBone);
            result.Add(new Piece2 { Renderer = smr, Geometry = merged, Material = key, Weights = weights.ToArray() });
        }

        outline = BuildOutline(parent, bones, rootBone, bindposes, name);
        return result;
    }

    /// <summary>One renderer with a sub-mesh per material, plus the outline. Returns every renderer made.</summary>
    public List<SkinnedMeshRenderer> BuildCombined(Transform parent, Transform[] bones, Transform rootBone, Func<string, Material> material, string name)
    {
        var bindposes = Bindposes(parent, bones);
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var weights = new List<SkinWeight>();
        var keys = new List<string>();
        var subTris = new List<List<int>>();
        foreach (var p in _pieces)
        {
            var mi = keys.IndexOf(p.Material);
            if (mi < 0)
            {
                keys.Add(p.Material);
                subTris.Add(new List<int>());
                mi = keys.Count - 1;
            }

            var start = verts.Count;
            verts.AddRange(p.Mesh.VertexArray);
            normals.AddRange(p.Mesh.NormalArray);
            uvs.AddRange(p.Mesh.UvArray);
            weights.AddRange(p.Weights);
            foreach (var k in p.Mesh.TriangleArray) subTris[mi].Add(start + k);
        }

        var mesh = new Mesh { name = name };
        if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts.ToArray());
        mesh.SetNormals(normals.ToArray());
        mesh.SetUVs(0, uvs.ToArray());
        SetWeights(mesh, weights);
        mesh.bindposes = bindposes;
        mesh.subMeshCount = subTris.Count;
        for (var i = 0; i < subTris.Count; i++) mesh.SetTriangles(subTris[i].ToArray(), i);
        mesh.RecalculateBounds();
        Keep(mesh);
        var mats = new Material[keys.Count];
        for (var i = 0; i < mats.Length; i++) mats[i] = material(keys[i]);
        var list = new List<SkinnedMeshRenderer> { Renderer(parent, name, mesh, mats, bones, rootBone) };
        var ink = BuildOutline(parent, bones, rootBone, bindposes, name);
        if (ink != null) list.Add(ink);
        return list;
    }

    private SkinnedMeshRenderer BuildOutline(Transform parent, Transform[] bones, Transform rootBone, Matrix4x4[] bindposes, string name)
    {
        var verts = new List<Vector3>();
        var weights = new List<SkinWeight>();
        var tris = new List<int>();
        foreach (var p in _pieces)
        {
            if (p.Outline <= 0) continue;
            var v = p.Mesh.VertexArray;
            var n = p.Mesh.NormalArray;
            var start = verts.Count;
            for (var i = 0; i < v.Length; i++) verts.Add(v[i] + n[i] * p.Outline);
            weights.AddRange(p.Weights);
            foreach (var k in p.Mesh.TriangleArray) tris.Add(start + k);
        }

        if (verts.Count == 0) return null;
        var mesh = new Mesh { name = name + "_Ink" };
        if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts.ToArray());
        SetWeights(mesh, weights);
        mesh.bindposes = bindposes;
        mesh.SetTriangles(tris.ToArray(), 0);
        mesh.RecalculateBounds();
        Keep(mesh);
        return Renderer(parent, name + "_Ink", mesh, new[] { InkMaterials.Outline }, bones, rootBone);
    }

    private static Matrix4x4[] Bindposes(Transform meshParent, Transform[] bones)
    {
        var b = new Matrix4x4[bones.Length];
        for (var i = 0; i < bones.Length; i++) b[i] = bones[i].worldToLocalMatrix * meshParent.localToWorldMatrix;
        return b;
    }

    private static SkinnedMeshRenderer Renderer(Transform parent, string name, Mesh mesh, Material[] mats, Transform[] bones, Transform rootBone)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = mesh;
        smr.bones = bones;
        smr.rootBone = rootBone;
        smr.sharedMaterials = mats;
        smr.updateWhenOffscreen = true;
        smr.shadowCastingMode = ShadowCastingMode.Off;
        smr.receiveShadows = false;
        return smr;
    }

    private static void Keep(Object o)
    {
        o.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(o);
    }

    /// <summary>
    /// The game's build stripped Mesh.boneWeights, but not the engine call behind Mesh.SetBoneWeights: hand it
    /// the bones-per-vertex counts and the BoneWeight1 records (float weight, int bone), heaviest first.
    /// </summary>
    private static unsafe void SetWeights(Mesh mesh, List<SkinWeight> weights)
    {
        var n = weights.Count;
        var total = 0;
        foreach (var w in weights) total += w.B1 >= 0 ? 2 : 1;
        var counts = (byte*)Marshal.AllocHGlobal(n);
        var records = (byte*)Marshal.AllocHGlobal(total * 8);
        try
        {
            var r = 0;
            for (var i = 0; i < n; i++)
            {
                var w = weights[i];
                counts[i] = (byte)(w.B1 >= 0 ? 2 : 1);
                *(float*)(records + r * 8) = w.W0;
                *(int*)(records + r * 8 + 4) = w.B0;
                r++;
                if (w.B1 < 0) continue;
                *(float*)(records + r * 8) = w.W1;
                *(int*)(records + r * 8 + 4) = w.B1;
                r++;
            }

            mesh.InternalSetBoneWeights((IntPtr)counts, n, (IntPtr)records, total);
        }
        finally
        {
            Marshal.FreeHGlobal((IntPtr)counts);
            Marshal.FreeHGlobal((IntPtr)records);
        }
    }
}
