using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.Body;

/// <summary>
/// Character models that only exist inside a chapter's files (Chapter 3's Alice Angel, Boris, Brute Boris), read out
/// of the Archives scene's statues on the player's own machine (<see cref="GameFile"/>) and rebuilt as live objects:
/// the bone hierarchy, the skinned meshes, the game's own textures and materials. Nothing from the game ships with
/// InkVR. Built once per session and kept.
/// </summary>
public static class ArchiveModels
{
    private const string Scene = "level7";
    private static readonly Dictionary<string, GameObject> Built = new();

    // the two shaders the studio's characters use (both loaded with any character from the game's Resources)
    private const string DiffuseShader = "Meatly Shaders/Surfaces/Diffuse - No Bendy Effect";
    private const string InkShader = "Meatly Shaders/Character Shaders/Character Ink";

    /// <summary>
    /// A copy of a statue from the Archives ("Alice", "Boris", "BruteBoris"), as a model to wear: under an inactive
    /// holder until it's taken, like <see cref="GameModels.Spawn"/>. Null if it can't be read.
    /// </summary>
    public static GameObject Spawn(string statue, int layer)
    {
        try
        {
            if (!Built.TryGetValue(statue, out var original) || original == null)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                original = Build(statue);
                GameFile.CloseAll();
                if (original == null) return null;
                Built[statue] = original;
                Plugin.L.LogInfo($"Archive model '{statue}' read from the game's files in {clock.ElapsedMilliseconds} ms");
            }

            var copy = Object.Instantiate(original);
            copy.name = "InkVR_Model_" + statue;
            copy.SetActive(true);
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            return copy;
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"Archive model '{statue}' couldn't be read: {e.Message}");
            GameFile.CloseAll();
            return null;
        }
    }

    // ---------------- reading the scene ----------------

    private static GameObject Build(string statue)
    {
        var scene = GameFile.Get(Scene);
        if (scene == null) return null;
        EnsureShaders();

        // the statue: a GameObject of that name whose parent is "Statues"
        long rootTransform = -1;
        foreach (var id in scene.ObjectsOf(1))
        {
            var go = scene.Read(id);
            if ((string)go["m_Name"] != statue) continue;
            var tr = TransformOf(scene, go);
            if (tr < 0) continue;
            var t = scene.Read(tr);
            var father = Ptr(t["m_Father"]);
            if (father.path == 0) continue;
            var ft = scene.Read(father.path);
            var fgo = scene.Read(Ptr(ft["m_GameObject"]).path);
            if ((string)fgo["m_Name"] != "Statues") continue;
            rootTransform = tr;
            break;
        }

        if (rootTransform < 0)
        {
            Plugin.L.LogWarning($"Archive model '{statue}' not found");
            return null;
        }

        var made = new Dictionary<long, Transform>();
        var renderers = new List<(long go, Dictionary<string, object> comp, int classId)>();
        var holder = new GameObject("InkVR_ArchiveModels");
        holder.SetActive(false);
        Object.DontDestroyOnLoad(holder);
        var root = MakeTransform(scene, rootTransform, holder.transform, made, renderers);
        root.localPosition = Vector3.zero;

        foreach (var (goId, comp, classId) in renderers)
        {
            var t = made[TransformOf(scene, scene.Read(goId))];
            if (classId == 137) MakeSkinned(scene, t, comp, made);
            else MakeStatic(scene, t, comp);
        }

        return root.gameObject;
    }

    private static long TransformOf(GameFile f, Dictionary<string, object> go)
    {
        foreach (var c in (List<object>)go["m_Component"])
        {
            var p = Ptr(((Dictionary<string, object>)c)["component"]);
            if (p.file == 0 && f.ClassOf(p.path) == 4) return p.path;
        }

        return -1;
    }

    private static Transform MakeTransform(GameFile f, long trId, Transform parent, Dictionary<long, Transform> made, List<(long, Dictionary<string, object>, int)> renderers)
    {
        var t = f.Read(trId);
        var goId = Ptr(t["m_GameObject"]).path;
        var go = f.Read(goId);
        var obj = new GameObject((string)go["m_Name"]);
        var tr = obj.transform;
        tr.SetParent(parent, false);
        tr.localPosition = V3(t["m_LocalPosition"]);
        tr.localRotation = Q(t["m_LocalRotation"]);
        tr.localScale = V3(t["m_LocalScale"]);
        made[trId] = tr;

        foreach (var c in (List<object>)go["m_Component"])
        {
            var p = Ptr(((Dictionary<string, object>)c)["component"]);
            if (p.file != 0) continue;
            var cls = f.ClassOf(p.path);
            if (cls == 137) renderers.Add((goId, f.Read(p.path), 137));
            else if (cls == 33)
            {
                // a static mesh: its MeshFilter and MeshRenderer together
                var filter = f.Read(p.path);
                foreach (var c2 in (List<object>)go["m_Component"])
                {
                    var p2 = Ptr(((Dictionary<string, object>)c2)["component"]);
                    if (p2.file == 0 && f.ClassOf(p2.path) == 23)
                    {
                        var mr = f.Read(p2.path);
                        mr["m_Mesh"] = filter["m_Mesh"];
                        renderers.Add((goId, mr, 23));
                    }
                }
            }
        }

        foreach (var c in (List<object>)t["m_Children"])
        {
            var p = Ptr(c);
            if (p.file == 0) MakeTransform(f, p.path, tr, made, renderers);
        }

        return tr;
    }

    private static void MakeSkinned(GameFile f, Transform t, Dictionary<string, object> smr, Dictionary<long, Transform> made)
    {
        var mesh = MeshAt(f, Ptr(smr["m_Mesh"]));
        if (mesh == null) return;
        var r = t.gameObject.AddComponent<SkinnedMeshRenderer>();
        var bonePtrs = (List<object>)smr["m_Bones"];
        var bones = new Transform[bonePtrs.Count];
        for (var i = 0; i < bones.Length; i++)
        {
            var p = Ptr(bonePtrs[i]);
            made.TryGetValue(p.path, out bones[i]);
        }

        r.sharedMesh = mesh;
        r.bones = bones;
        var root = Ptr(smr["m_RootBone"]);
        if (root.path != 0 && made.TryGetValue(root.path, out var rb)) r.rootBone = rb;
        r.sharedMaterials = Materials(f, (List<object>)smr["m_Materials"]);
        r.updateWhenOffscreen = true;
    }

    private static void MakeStatic(GameFile f, Transform t, Dictionary<string, object> mr)
    {
        var mesh = MeshAt(f, Ptr(mr["m_Mesh"]));
        if (mesh == null) return;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterials = Materials(f, (List<object>)mr["m_Materials"]);
    }

    // ---------------- meshes ----------------

    private static readonly Dictionary<string, Mesh> Meshes = new();

    private static Mesh MeshAt(GameFile from, (int file, long path) p)
    {
        var f = from.Resolve(p.file);
        if (f == null || p.path == 0) return null;
        var key = f.Path + "#" + p.path;
        if (Meshes.TryGetValue(key, out var cached) && cached != null) return cached;
        var m = f.Read(p.path);
        if (m == null) return null;
        var mesh = BuildMesh(m);
        Meshes[key] = mesh;
        return mesh;
    }

    private static Mesh BuildMesh(Dictionary<string, object> m)
    {
        if (Convert.ToInt32(m["m_MeshCompression"]) != 0) throw new NotSupportedException("compressed mesh");
        var vd = (Dictionary<string, object>)m["m_VertexData"];
        var count = (int)Convert.ToUInt32(vd["m_VertexCount"]);
        var data = (byte[])vd["m_DataSize"];
        var stream = (Dictionary<string, object>)m["m_StreamData"];
        var streamSize = (int)Convert.ToUInt32(stream["size"]);
        if (streamSize > 0) data = GameFile.Stream((string)stream["path"], (long)Convert.ToUInt64(stream["offset"]), streamSize);
        if (data == null) throw new InvalidOperationException("no vertex data");

        // the channels, and where each stream starts (streams are 16-byte aligned, one after another)
        var channels = (List<object>)vd["m_Channels"];
        var ch = new (int stream, int offset, int format, int dim)[channels.Count];
        var streams = 0;
        for (var i = 0; i < ch.Length; i++)
        {
            var c = (Dictionary<string, object>)channels[i];
            ch[i] = (Convert.ToInt32(c["stream"]), Convert.ToInt32(c["offset"]), Convert.ToInt32(c["format"]), Convert.ToInt32(c["dimension"]) & 0xF);
            if (ch[i].dim > 0) streams = Math.Max(streams, ch[i].stream + 1);
        }

        var strideOf = new int[streams];
        var startOf = new int[streams];
        for (var s = 0; s < streams; s++)
            foreach (var c in ch)
                if (c.stream == s && c.dim > 0)
                    strideOf[s] += c.dim * FormatSize(c.format);
        var off = 0;
        for (var s = 0; s < streams; s++)
        {
            startOf[s] = off;
            off += count * strideOf[s];
            off = (off + 15) & ~15;
        }

        float[] Channel(int index)
        {
            if (index >= ch.Length || ch[index].dim == 0) return null;
            var c = ch[index];
            var size = FormatSize(c.format);
            var outp = new float[count * c.dim];
            for (var v = 0; v < count; v++)
            {
                var at = startOf[c.stream] + v * strideOf[c.stream] + c.offset;
                for (var d = 0; d < c.dim; d++) outp[v * c.dim + d] = ReadFormat(data, at + d * size, c.format);
            }

            return outp;
        }

        var mesh = new Mesh { name = (string)m["m_Name"] + "_InkVR" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        var pos = Channel(0);
        var verts = new Vector3[count];
        for (var v = 0; v < count; v++) verts[v] = new Vector3(pos[v * 3], pos[v * 3 + 1], pos[v * 3 + 2]);
        mesh.vertices = verts;
        var nrm = Channel(1);
        if (nrm != null)
        {
            var n = new Vector3[count];
            for (var v = 0; v < count; v++) n[v] = new Vector3(nrm[v * 3], nrm[v * 3 + 1], nrm[v * 3 + 2]);
            mesh.normals = n;
        }

        var tan = Channel(2);
        if (tan != null && ch[2].dim == 4)
        {
            var tg = new Vector4[count];
            for (var v = 0; v < count; v++) tg[v] = new Vector4(tan[v * 4], tan[v * 4 + 1], tan[v * 4 + 2], tan[v * 4 + 3]);
            mesh.tangents = tg;
        }

        var col = Channel(3);
        if (col != null && ch[3].dim == 4)
        {
            var cs = new Color[count];
            for (var v = 0; v < count; v++) cs[v] = new Color(col[v * 4], col[v * 4 + 1], col[v * 4 + 2], col[v * 4 + 3]);
            mesh.colors = cs;
        }

        for (var u = 0; u < 4; u++)
        {
            var uv = Channel(4 + u);
            if (uv == null) continue;
            var dim = ch[4 + u].dim;
            var a = new Vector2[count];
            for (var v = 0; v < count; v++) a[v] = new Vector2(uv[v * dim], dim > 1 ? uv[v * dim + 1] : 0);
            if (u == 0) mesh.uv = a;
            else if (u == 1) mesh.uv2 = a;
            else if (u == 2) mesh.uv3 = a;
            else mesh.uv4 = a;
        }

        // triangles, per submesh
        var ib = (byte[])m["m_IndexBuffer"];
        var wide = Convert.ToInt32(m["m_IndexFormat"]) == 1;
        var subs = (List<object>)m["m_SubMeshes"];
        mesh.subMeshCount = subs.Count;
        for (var s = 0; s < subs.Count; s++)
        {
            var sm = (Dictionary<string, object>)subs[s];
            var first = (int)Convert.ToUInt32(sm["firstByte"]);
            var n = (int)Convert.ToUInt32(sm["indexCount"]);
            var baseVertex = (int)Convert.ToUInt32(sm["baseVertex"]);
            var tris = new int[n];
            for (var i = 0; i < n; i++) tris[i] = (wide ? (int)BitConverter.ToUInt32(ib, first + i * 4) : BitConverter.ToUInt16(ib, first + i * 2)) + baseVertex;
            mesh.SetTriangles(tris, s);
        }

        // bind poses (column-major) and the skin
        var bind = (List<object>)m["m_BindPose"];
        if (bind.Count > 0)
        {
            var bp = new Matrix4x4[bind.Count];
            for (var i = 0; i < bp.Length; i++) bp[i] = Matrix((Dictionary<string, object>)bind[i]);
            mesh.bindposes = bp;
            var w = Channel(12);
            var idx = Channel(13);
            if (w != null && idx != null) SetWeights(mesh, count, w, ch[12].dim, idx, ch[13].dim);
        }

        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
        mesh.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return mesh;
    }

    private static int FormatSize(int f) => f switch
    {
        0 => 4, 1 => 2, 2 or 3 => 1, 4 or 5 => 2, 6 or 7 => 1, 8 or 9 => 2, 10 or 11 => 4, _ => 4
    };

    private static float ReadFormat(byte[] b, int at, int f) => f switch
    {
        0 => BitConverter.ToSingle(b, at),
        1 => (float)BitConverter.ToHalf(b, at),
        2 => b[at] / 255f,
        3 => Math.Max((sbyte)b[at] / 127f, -1f),
        4 => BitConverter.ToUInt16(b, at) / 65535f,
        5 => Math.Max(BitConverter.ToInt16(b, at) / 32767f, -1f),
        6 => b[at],
        7 => (sbyte)b[at],
        8 => BitConverter.ToUInt16(b, at),
        9 => BitConverter.ToInt16(b, at),
        10 => BitConverter.ToUInt32(b, at),
        11 => BitConverter.ToInt32(b, at),
        _ => 0f
    };

    /// <summary>Each vertex's weights (heaviest first, zero weights dropped), through the engine call behind Mesh.SetBoneWeights.</summary>
    private static unsafe void SetWeights(Mesh mesh, int n, float[] w, int wDim, float[] idx, int iDim)
    {
        var per = Math.Min(wDim, iDim);
        var counts = (byte*)Marshal.AllocHGlobal(n);
        var records = (byte*)Marshal.AllocHGlobal(n * per * 8);
        try
        {
            var total = 0;
            var tmp = new (float w, int b)[per];
            for (var v = 0; v < n; v++)
            {
                var k = 0;
                var sum = 0f;
                for (var j = 0; j < per; j++)
                {
                    var wt = w[v * wDim + j];
                    if (wt <= 1e-5f) continue;
                    tmp[k++] = (wt, (int)idx[v * iDim + j]);
                    sum += wt;
                }

                if (k == 0)
                {
                    tmp[0] = (1f, (int)idx[v * iDim]);
                    k = 1;
                    sum = 1f;
                }

                Array.Sort(tmp, 0, k, Comparer<(float w, int b)>.Create((a, b) => b.w.CompareTo(a.w)));
                counts[v] = (byte)k;
                for (var j = 0; j < k; j++)
                {
                    *(float*)(records + total * 8) = tmp[j].w / sum;
                    *(int*)(records + total * 8 + 4) = tmp[j].b;
                    total++;
                }
            }

            mesh.InternalSetBoneWeights((IntPtr)counts, n, (IntPtr)records, total);
        }
        finally
        {
            Marshal.FreeHGlobal((IntPtr)counts);
            Marshal.FreeHGlobal((IntPtr)records);
        }
    }

    // ---------------- materials and textures ----------------

    private static readonly Dictionary<string, Material> Mats = new();
    private static readonly Dictionary<string, Texture2D> Texs = new();

    private static Material[] Materials(GameFile from, List<object> ptrs)
    {
        var mats = new Material[ptrs.Count];
        for (var i = 0; i < mats.Length; i++) mats[i] = MaterialAt(from, Ptr(ptrs[i]));
        return mats;
    }

    private static Material MaterialAt(GameFile from, (int file, long path) p)
    {
        var f = from.Resolve(p.file);
        if (f == null || p.path == 0) return null;
        var key = f.Path + "#" + p.path;
        if (Mats.TryGetValue(key, out var cached) && cached != null) return cached;
        var m = f.Read(p.path);
        if (m == null) return null;
        var props = (Dictionary<string, object>)m["m_SavedProperties"];
        var texEnvs = (List<object>)props["m_TexEnvs"];
        var names = new HashSet<string>();
        foreach (var e in texEnvs) names.Add((string)((object[])e)[0]);
        // which of the two character shaders: the ink one has its own texture slots
        var shader = Shader.Find(names.Contains("_MainTexture") || names.Contains("_CharacterNormals") ? InkShader : DiffuseShader) ?? Shader.Find(DiffuseShader);
        if (shader == null) throw new InvalidOperationException("the game's character shaders aren't loaded");
        var mat = new Material(shader) { name = (string)m["m_Name"] };
        foreach (var e in texEnvs)
        {
            var pair = (object[])e;
            var name = (string)pair[0];
            var env = (Dictionary<string, object>)pair[1];
            var tex = TextureAt(f, Ptr(env["m_Texture"]));
            if (tex == null || !mat.HasProperty(name)) continue;
            mat.SetTexture(name, tex);
            mat.SetTextureScale(name, V2(env["m_Scale"]));
            mat.SetTextureOffset(name, V2(env["m_Offset"]));
        }

        foreach (var e in (List<object>)props["m_Floats"])
        {
            var pair = (object[])e;
            if (mat.HasProperty((string)pair[0])) mat.SetFloat((string)pair[0], Convert.ToSingle(pair[1]));
        }

        foreach (var e in (List<object>)props["m_Colors"])
        {
            var pair = (object[])e;
            var c = (Dictionary<string, object>)pair[1];
            if (mat.HasProperty((string)pair[0])) mat.SetColor((string)pair[0], new Color(Convert.ToSingle(c["r"]), Convert.ToSingle(c["g"]), Convert.ToSingle(c["b"]), Convert.ToSingle(c["a"])));
        }

        foreach (var k in (List<object>)m["m_ValidKeywords"]) mat.EnableKeyword((string)k);
        var queue = Convert.ToInt32(m["m_CustomRenderQueue"]);
        if (queue > 0) mat.renderQueue = queue;
        mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Mats[key] = mat;
        return mat;
    }

    private static Texture2D TextureAt(GameFile from, (int file, long path) p)
    {
        var f = from.Resolve(p.file);
        if (f == null || p.path == 0 || f.ClassOf(p.path) != 28) return null;
        var key = f.Path + "#" + p.path;
        if (Texs.TryGetValue(key, out var cached) && cached != null) return cached;
        var t = f.Read(p.path);
        var w = Convert.ToInt32(t["m_Width"]);
        var h = Convert.ToInt32(t["m_Height"]);
        var fmt = Convert.ToInt32(t["m_TextureFormat"]);
        var mips = Math.Max(1, Convert.ToInt32(t["m_MipCount"]));
        var data = (byte[])t["image data"];
        var stream = (Dictionary<string, object>)t["m_StreamData"];
        var size = (int)Convert.ToUInt32(stream["size"]);
        if (size > 0) data = GameFile.Stream((string)stream["path"], (long)Convert.ToUInt64(stream["offset"]), size);
        if (data == null || data.Length == 0) return null;
        var linear = Convert.ToInt32(t["m_ColorSpace"]) == 0;
        var tex = new Texture2D(w, h, (TextureFormat)fmt, mips, linear) { name = (string)t["m_Name"] };
        var settings = (Dictionary<string, object>)t["m_TextureSettings"];
        tex.filterMode = (FilterMode)Convert.ToInt32(settings["m_FilterMode"]);
        tex.anisoLevel = Convert.ToInt32(settings["m_Aniso"]);
        tex.wrapModeU = (TextureWrapMode)Convert.ToInt32(settings["m_WrapU"]);
        tex.wrapModeV = (TextureWrapMode)Convert.ToInt32(settings["m_WrapV"]);
        tex.LoadRawTextureData(data);
        tex.Apply(false, true);
        tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Texs[key] = tex;
        return tex;
    }

    /// <summary>The character shaders come in with any character from the game's Resources: load one if none is.</summary>
    private static void EnsureShaders()
    {
        if (Shader.Find(InkShader) != null && Shader.Find(DiffuseShader) != null) return;
        Resources.Load("gameplay/characters/ai_sammylawrence");
        Resources.Load("gameplay/characters/ai_alicea");
    }

    // ---------------- values ----------------

    private static (int file, long path) Ptr(object o)
    {
        var d = (Dictionary<string, object>)o;
        return (Convert.ToInt32(d["m_FileID"]), Convert.ToInt64(d["m_PathID"]));
    }

    private static float F(Dictionary<string, object> d, string k) => Convert.ToSingle(d[k]);

    private static Vector3 V3(object o)
    {
        var d = (Dictionary<string, object>)o;
        return new Vector3(F(d, "x"), F(d, "y"), F(d, "z"));
    }

    private static Vector2 V2(object o)
    {
        var d = (Dictionary<string, object>)o;
        return new Vector2(F(d, "x"), F(d, "y"));
    }

    private static Quaternion Q(object o)
    {
        var d = (Dictionary<string, object>)o;
        return new Quaternion(F(d, "x"), F(d, "y"), F(d, "z"), F(d, "w"));
    }

    /// <summary>A serialized Matrix4x4f: "e" + row + column (e03, e13, e23 hold the translation).</summary>
    private static Matrix4x4 Matrix(Dictionary<string, object> d)
    {
        return new Matrix4x4
        {
            m00 = F(d, "e00"), m01 = F(d, "e01"), m02 = F(d, "e02"), m03 = F(d, "e03"),
            m10 = F(d, "e10"), m11 = F(d, "e11"), m12 = F(d, "e12"), m13 = F(d, "e13"),
            m20 = F(d, "e20"), m21 = F(d, "e21"), m22 = F(d, "e22"), m23 = F(d, "e23"),
            m30 = F(d, "e30"), m31 = F(d, "e31"), m32 = F(d, "e32"), m33 = F(d, "e33")
        };
    }
}
