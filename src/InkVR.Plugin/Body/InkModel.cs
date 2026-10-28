using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using InkVR.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace InkVR.Body;

/// <summary>
/// Henry's whole body: built in Blender from MakeHuman's CC0 base (tools/henry), shipped with InkVR as an .inkmodel file
/// (bones, textures, skinned parts; written by tools/henry/export_inkmodel.py) and worn through <see cref="CharacterSkin"/>
/// like any character model, drawn with the game's own surface shader.
/// The file is read once per session: its meshes, textures and materials are kept (they outlive the game's resets,
/// which destroy every kept GameObject), and each wearer gets a fresh skeleton and renderers over them.
/// </summary>
public static class InkModel
{
    private sealed class Data
    {
        public string Name;
        public string[] Bones;
        public int[] Parents;
        public Vector3[] Pos;
        public Quaternion[] Rot;
        public readonly List<(string Name, Mesh Mesh, Material Mat)> Parts = new();
    }

    private static readonly Dictionary<string, Data> Loaded = new();

    public static string Folder => Path.Combine(Paths.PluginPath, "InkVR", "models");

    /// <summary>The model in this file, to wear: at the origin, unparented (null if it can't be read).</summary>
    public static GameObject Spawn(string file, int layer)
    {
        try
        {
            if (!Loaded.TryGetValue(file, out var d) || !Alive(d))
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                d = Load(Path.Combine(Folder, file));
                if (d == null) return null;
                Loaded[file] = d;
                Plugin.L.LogInfo($"Model '{file}' read in {clock.ElapsedMilliseconds} ms ({d.Bones.Length} bones, {d.Parts.Count} parts)");
            }

            return Make(d, layer);
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"Model '{file}' couldn't be read: {e}");
            return null;
        }
    }

    private static bool Alive(Data d)
    {
        foreach (var p in d.Parts)
            if (p.Mesh == null || p.Mat == null)
                return false;
        return true;
    }

    private static GameObject Make(Data d, int layer)
    {
        var root = new GameObject("InkVR_Model_" + d.Name) { layer = layer };
        var bones = new Transform[d.Bones.Length];
        for (var i = 0; i < bones.Length; i++)
        {
            var go = new GameObject(d.Bones[i]) { layer = layer };
            var t = go.transform;
            t.SetParent(d.Parents[i] >= 0 ? bones[d.Parents[i]] : root.transform, false);
            t.localPosition = d.Pos[i];
            t.localRotation = d.Rot[i];
            bones[i] = t;
        }

        foreach (var (name, mesh, mat) in d.Parts)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(root.transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[0];
            smr.sharedMaterial = mat;
            smr.updateWhenOffscreen = true;
        }

        return root;
    }

    private static Data Load(string path)
    {
        if (!File.Exists(path))
        {
            Plugin.L.LogWarning($"Model file missing: {path}");
            return null;
        }

        using var r = new BinaryReader(File.OpenRead(path));
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "INKM" || r.ReadUInt32() != 1) throw new InvalidDataException("not an .inkmodel (version 1)");
        var d = new Data { Name = Path.GetFileNameWithoutExtension(path) };

        // bones, and where the skin was bound to them (their rest frames, model space)
        var boneCount = (int)r.ReadUInt32();
        d.Bones = new string[boneCount];
        d.Parents = new int[boneCount];
        d.Pos = new Vector3[boneCount];
        d.Rot = new Quaternion[boneCount];
        var world = new Matrix4x4[boneCount];
        var bindposes = new Matrix4x4[boneCount];
        for (var i = 0; i < boneCount; i++)
        {
            d.Bones[i] = Str(r);
            d.Parents[i] = r.ReadInt32();
            d.Pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            d.Rot[i] = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            var local = Matrix4x4.TRS(d.Pos[i], d.Rot[i], Vector3.one);
            world[i] = d.Parents[i] >= 0 ? world[d.Parents[i]] * local : local;
            bindposes[i] = world[i].inverse;
        }

        // textures (with their mip levels, made on the GPU from the full-size one)
        var texCount = (int)r.ReadUInt32();
        var textures = new Texture2D[texCount];
        for (var i = 0; i < texCount; i++)
        {
            var name = Str(r);
            int w = r.ReadUInt16(), h = r.ReadUInt16();
            var packed = r.ReadBytes((int)r.ReadUInt32());
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "InkVR_" + name, anisoLevel = 4 };
            var size = 0;
            for (var m = 0; m < tex.mipmapCount; m++) size += Math.Max(1, w >> m) * Math.Max(1, h >> m) * 4;
            var raw = new byte[size];
            using (var z = new ZLibStream(new MemoryStream(packed), CompressionMode.Decompress))
            {
                var read = 0;
                while (read < w * h * 4)
                {
                    var n = z.Read(raw, read, w * h * 4 - read);
                    if (n <= 0) break;
                    read += n;
                }
            }

            tex.LoadRawTextureData(raw);
            tex.Apply(true, false); // (left readable: ink is painted onto copies of it)
            tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            textures[i] = tex;
        }

        // parts: one skinned mesh each
        var partCount = (int)r.ReadUInt32();
        for (var p = 0; p < partCount; p++)
        {
            var name = Str(r);
            var matName = Str(r);
            var texIndex = r.ReadInt32();
            r.ReadByte(); // flags (alpha was baked solid by the exporter)
            var n = (int)r.ReadUInt32();
            var verts = new Vector3[n];
            var normals = new Vector3[n];
            var uvs = new Vector2[n];
            var boneIdx = new byte[n * 4];
            var weights = new float[n * 4];
            for (var v = 0; v < n; v++)
            {
                verts[v] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                normals[v] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                uvs[v] = new Vector2(r.ReadSingle(), r.ReadSingle());
                for (var k = 0; k < 4; k++) boneIdx[v * 4 + k] = r.ReadByte();
                for (var k = 0; k < 4; k++) weights[v * 4 + k] = r.ReadSingle();
            }

            var ic = (int)r.ReadUInt32();
            var tris = new int[ic];
            for (var k = 0; k < ic; k++) tris[k] = (int)r.ReadUInt32();

            var mesh = new Mesh { name = "InkVR_" + name };
            if (n > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            SetWeights(mesh, n, weights, boneIdx);
            mesh.bindposes = bindposes;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            var tex = texIndex >= 0 && texIndex < textures.Length ? textures[texIndex] : null;
            var mat = InkMaterials.GameSurface(tex, "InkVR_" + matName);
            mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            d.Parts.Add((name, mesh, mat));
        }

        return d;
    }

    // ---------------- reading ----------------

    private static string Str(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));

    /// <summary>The bone weights through the engine call behind Mesh.SetBoneWeights (Mesh.boneWeights is stripped).</summary>
    private static unsafe void SetWeights(Mesh mesh, int n, float[] w, byte[] idx)
    {
        var counts = (byte*)Marshal.AllocHGlobal(n);
        var records = (byte*)Marshal.AllocHGlobal(n * 4 * 8);
        try
        {
            var total = 0;
            for (var v = 0; v < n; v++)
            {
                var k = 0;
                var sum = 0f;
                for (var j = 0; j < 4; j++) sum += w[v * 4 + j] > 1e-5f ? w[v * 4 + j] : 0f;
                for (var j = 0; j < 4; j++)
                {
                    var wt = w[v * 4 + j];
                    if (wt <= 1e-5f) continue; // (already heaviest first)
                    *(float*)(records + total * 8) = wt / sum;
                    *(int*)(records + total * 8 + 4) = idx[v * 4 + j];
                    total++;
                    k++;
                }

                if (k == 0)
                {
                    *(float*)(records + total * 8) = 1f;
                    *(int*)(records + total * 8 + 4) = idx[v * 4];
                    total++;
                    k = 1;
                }

                counts[v] = (byte)k;
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
