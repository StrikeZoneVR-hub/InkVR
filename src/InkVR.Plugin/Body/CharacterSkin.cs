using System;
using System.Collections.Generic;
using UnityEngine;
using InkVR.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InkVR.Body;

/// <summary>
/// A style worn as the game's own character model (see <see cref="GameModels"/>): one connected skinned body, posed
/// every frame from Henry's invisible skeleton (<see cref="HenryBody"/> still does the standing, crouching, stepping
/// and stairs) and from the hands (<see cref="HandModel"/>: the controllers' wrists and finger curls).
/// The character keeps its own proportions: it's scaled so its eyes are at yours, the torso hangs from the head along
/// Henry's spine, the legs reach the planted feet and the arms reach the controllers by two-bone IK (stretching a
/// little if a short-armed character can't reach), and its fingers curl like Henry's.
/// Its head is never drawn for you: one copy of each mesh without the head is what you see, and the whole model
/// casts your shadow and shows in the theatre's mirrors.
/// </summary>
public sealed class CharacterSkin
{
    private enum R
    {
        Hips, Chest, Neck, Head,
        LClav, LUpper, LFore, LHand, RClav, RUpper, RFore, RHand,
        LThigh, LShin, LFoot, LToe, RThigh, RShin, RFoot, RToe,
        Count
    }

    public readonly Transform Root;
    public readonly StyleDef Def;

    // every transform in the model, with its rest pose (model space: feet at the origin, facing +z, unscaled)
    private readonly List<Transform> _all = new();
    private readonly Dictionary<IntPtr, int> _index = new();
    private Vector3[] _rp;
    private Quaternion[] _rr, _rl;
    private int[] _depth;

    private readonly int[] _role = new int[(int)R.Count];
    private readonly List<int> _torso = new(); // hips, spine..., chest, neck, head (by height)
    private readonly int[][] _fingers = new int[10][]; // left index, middle, ring, pinky, thumb; then right
    private readonly int[] _foreTwist = { -1, -1 };

    private Vector3 _restEye;
    private float _restEyeY, _floor;

    // this frame's targets
    private Vector3[] _tp;
    private Quaternion[] _tq;
    private bool[] _set;
    private int[] _anchor;
    private readonly List<int> _order = new();

    // what's drawn: the copies without the head for your eyes, the whole model for mirrors and your shadow
    private readonly List<Renderer> _eyes = new(), _whole = new(), _always = new();

    // the skinned meshes ink paints (and the copy that shares its stained material)
    private readonly List<(SkinnedMeshRenderer smr, SkinnedMeshRenderer twin)> _paint = new();

    /// <summary>The model's paintable pieces: ink from the world gets onto them as onto Henry.</summary>
    public readonly List<StainPart> Stains = new();

    private const int MaxInkVertices = 25000;

    /// <summary>Each skinned mesh with a picture of its own becomes a stain piece (its first material).</summary>
    private void RegisterInk()
    {
        foreach (var (smr, twin) in _paint)
        {
            var mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable || mesh.vertexCount > MaxInkVertices) continue;
            var weights = TopTwo(mesh);
            if (weights == null) continue;
            // the first material with a picture of its own (Brute Boris's first is a plain ink layer)
            var sub = -1;
            var mats0 = smr.sharedMaterials;
            for (var m = 0; m < mats0.Length && m < mesh.subMeshCount; m++)
            {
                var mat = mats0[m];
                if (mat == null) continue;
                var slot = mat.HasProperty("_MainTexture") ? "_MainTexture" : "_MainTex";
                if (mat.HasProperty(slot) && mat.GetTexture(slot) != null)
                {
                    sub = m;
                    break;
                }
            }

            if (sub < 0) continue;
            var verts = mesh.vertices;
            var tris = mesh.GetTriangles(sub);
            var ok = true;
            foreach (var t in tris)
                if (t < 0 || t >= verts.Length)
                {
                    ok = false;
                    break;
                }

            if (!ok || weights.Length != verts.Length) continue;
            var mb = MeshBuilder.FromArrays(verts, mesh.normals, mesh.uv, tris);
            var src = smr.bones;
            var bones = new Transform[src.Length];
            for (var i = 0; i < bones.Length; i++) bones[i] = src[i];
            var part = InkStains.RegisterModel(smr, sub, mb, weights, bones, mesh.bindposes, RegionOf(smr));
            if (part == null) continue;
            Stains.Add(part);
            if (twin != null)
            {
                var mats = twin.sharedMaterials;
                mats[sub] = part.Mat;
                twin.sharedMaterials = mats;
            }
        }

        if (Stains.Count > 0) Plugin.L.LogInfo($"Body: {Def.Label} takes ink ({Stains.Count} piece{(Stains.Count > 1 ? "s" : "")})");
    }

    /// <summary>
    /// Which part of the body a piece is, by where it reaches at rest (still standing at the origin): only on the feet
    /// (shoes), down to the shins (trousers, and skin to the ankles: wading inks these up to the ink line), or above.
    /// </summary>
    private InkRegion RegionOf(SkinnedMeshRenderer smr)
    {
        var b = smr.sharedMesh.bounds;
        var toWorld = smr.transform.localToWorldMatrix;
        var mid = toWorld.MultiplyPoint3x4(b.center).y;
        var half = Mathf.Abs(toWorld.MultiplyVector(new Vector3(0, b.extents.y, 0)).y);
        var lo = mid - half - _floor;
        var hi = mid + half - _floor;
        if (hi < _restEyeY * 0.1f) return InkRegion.Feet;
        return lo < _restEyeY * 0.3f ? InkRegion.Legs : InkRegion.Torso;
    }

    /// <summary>Each vertex's two heaviest bones (the ink's own skinning).</summary>
    private static SkinWeight[] TopTwo(Mesh mesh)
    {
        if (!ReadWeights(mesh, out var per, out var wt, out var bi)) return null;
        var n = per.Length;
        var w = new SkinWeight[n];
        var k = 0;
        for (var v = 0; v < n; v++)
        {
            var c = per[v];
            if (c == 0 || k + c > wt.Length)
            {
                w[v] = SkinWeight.Rigid(0);
                k += c;
                continue;
            }

            if (c == 1 || wt[k] + wt[k + 1] <= 1e-5f) w[v] = SkinWeight.Rigid(bi[k]);
            else w[v] = SkinWeight.Blend(bi[k], bi[k + 1], wt[k + 1] / (wt[k] + wt[k + 1]));
            k += c;
        }

        return w;
    }

    /// <summary>
    /// A mesh's skin: bones per vertex, and the weight records (heaviest first) one after another. The game's build
    /// stripped Mesh.boneWeights; the engine's own calls hand back raw pointers into the mesh's data, read here.
    /// </summary>
    private static bool ReadWeights(Mesh mesh, out byte[] per, out float[] weight, out int[] bone)
    {
        per = Array.Empty<byte>();
        weight = Array.Empty<float>();
        bone = Array.Empty<int>();
        if (mesh == null || !mesh.HasBoneWeights()) return false;
        var n = mesh.vertexCount;
        var total = mesh.GetAllBoneWeightsArraySize();
        var perPtr = mesh.GetBonesPerVertexArray();
        var allPtr = mesh.GetAllBoneWeightsArray();
        if (n <= 0 || total <= 0 || perPtr == IntPtr.Zero || allPtr == IntPtr.Zero) return false;
        per = new byte[n];
        System.Runtime.InteropServices.Marshal.Copy(perPtr, per, 0, n);
        var raw = new byte[total * 8];
        System.Runtime.InteropServices.Marshal.Copy(allPtr, raw, 0, raw.Length);
        weight = new float[total];
        bone = new int[total];
        for (var i = 0; i < total; i++)
        {
            weight[i] = BitConverter.ToSingle(raw, i * 8);
            bone[i] = BitConverter.ToInt32(raw, i * 8 + 4);
        }

        return true;
    }
    private readonly HashSet<IntPtr> _headBones = new();
    private bool _headShown, _shadows = true, _low;

    public float Scale { get; private set; } = 1f;

    /// <summary>The model stands on legs (a Searcher doesn't).</summary>
    public bool Legs { get; private set; }

    private CharacterSkin(StyleDef def, GameObject model)
    {
        Def = def;
        Root = model.transform;
    }

    public static CharacterSkin TryCreate(StyleDef def, Transform parent, int layer)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var go = def.ModelFile != null ? InkModel.Spawn(def.ModelFile, layer)
            : def.GameModel != null ? GameModels.Spawn(def.GameModel, null, layer) : ArchiveModels.Spawn(def.ArchiveModel, layer);
        var spawned = clock.ElapsedMilliseconds;
        if (go == null) return null;
        var skin = new CharacterSkin(def, go);
        try
        {
            if (!skin.Setup())
            {
                Plugin.L.LogWarning($"Body: {def.Label}'s model doesn't have the bones it needs; using the drawn style");
                Object.Destroy(go);
                return null;
            }
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"Body: {def.Label}'s model failed ({e.Message}); using the drawn style");
            Object.Destroy(go);
            return null;
        }

        skin.Root.SetParent(parent, true);
        try
        {
            skin.RegisterInk();
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"Body: {def.Label}'s ink couldn't be set up ({e.Message}) {e.StackTrace}");
        }

        Plugin.L.LogInfo($"Body: {def.Label} is {(def.ModelFile != null ? "InkVR's" : "the game's own")} model ({skin._all.Count} bones, eyes {skin._restEyeY:F2} up; {spawned} + {clock.ElapsedMilliseconds - spawned} ms)");
        return skin;
    }

    // =====================================================================================================
    // Setup: the rest pose, the bones by role, the meshes
    // =====================================================================================================

    private bool Setup()
    {
        Root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Root.localScale = Vector3.one;
        foreach (var name in Def.ModelHide ?? Array.Empty<string>())
        {
            var t = GameModels.Find(Root, name);
            if (t != null) t.gameObject.SetActive(false);
        }

        foreach (var t in Root.GetComponentsInChildren<Transform>(true))
        {
            if (t == Root) continue;
            _index[t.Pointer] = _all.Count;
            _all.Add(t);
        }

        var n = _all.Count;
        _rp = new Vector3[n];
        _rr = new Quaternion[n];
        _rl = new Quaternion[n];
        _depth = new int[n];
        _tp = new Vector3[n];
        _tq = new Quaternion[n];
        _set = new bool[n];
        _anchor = new int[n];
        for (var i = 0; i < n; i++)
        {
            var d = 0;
            for (var p = _all[i].parent; p != null && p != Root; p = p.parent) d++;
            _depth[i] = d;
        }

        ToBindPose();
        for (var i = 0; i < n; i++)
        {
            var t = _all[i];
            _rp[i] = t.position;
            _rr[i] = t.rotation;
            _rl[i] = t.localRotation;
        }

        // ---- roles, by name (two naming schemes: "bn_L_forearm001" and "BN_L_ForeArm001") ----
        for (var r = 0; r < (int)R.Count; r++) _role[r] = -1;
        var spine = new List<int>();
        var spineBase = -1;
        for (var i = 0; i < n; i++)
        {
            var k = Key(_all[i].name);
            if (k.StartsWith("spine") && !k.Contains("twist")) spine.Add(i);
            switch (k)
            {
                case "hips" or "pelvis": Claim(R.Hips, i); break;
                case "spinebase": spineBase = i; break;
                case "chest": Claim(R.Chest, i); break;
                case "neck": Claim(R.Neck, i); break;
                case "head": Claim(R.Head, i); break;
                case "lclavicle": Claim(R.LClav, i); break;
                case "rclavicle": Claim(R.RClav, i); break;
                case "larm" or "lupperarm": Claim(R.LUpper, i); break;
                case "rarm" or "rupperarm": Claim(R.RUpper, i); break;
                case "lforearm": Claim(R.LFore, i); break;
                case "rforearm": Claim(R.RFore, i); break;
                case "lhand": Claim(R.LHand, i); break;
                case "rhand": Claim(R.RHand, i); break;
                case "lthigh": Claim(R.LThigh, i); break;
                case "rthigh": Claim(R.RThigh, i); break;
                case "lshin" or "lcalve": Claim(R.LShin, i); break;
                case "rshin" or "rcalve": Claim(R.RShin, i); break;
                case "lfoot": Claim(R.LFoot, i); break;
                case "rfoot": Claim(R.RFoot, i); break;
                case "lball" or "ltoe" or "lfootball": Claim(R.LToe, i); break;
                case "rball" or "rtoe" or "rfootball": Claim(R.RToe, i); break;
                case "lforearmtwist" or "lforearmtwista": _foreTwist[0] = i; break;
                case "rforearmtwist" or "rforearmtwista": _foreTwist[1] = i; break;
            }
        }

        // ---- facing +z (the posing assumes it): the Archives' statues stand turned. Across the shoulders, then forward ----
        var across = _rp[_role[(int)R.RUpper]] - _rp[_role[(int)R.LUpper]];
        if (across.sqrMagnitude < 1e-10f && _role[(int)R.RThigh] >= 0 && _role[(int)R.LThigh] >= 0) across = _rp[_role[(int)R.RThigh]] - _rp[_role[(int)R.LThigh]];
        var facing = Vector3.Cross(across, Vector3.up);
        facing.y = 0;
        if (facing.sqrMagnitude > 1e-10f)
        {
            var yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(yaw) > 1f)
            {
                Root.rotation = Quaternion.Euler(0, -yaw, 0);
                for (var i = 0; i < n; i++)
                {
                    _rp[i] = _all[i].position;
                    _rr[i] = _all[i].rotation;
                }
            }
        }

        // ---- the floor: under the soles (a statue stands on a plinth) ----
        _floor = float.MaxValue;
        foreach (var smr in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            var toWorld = smr.transform.localToWorldMatrix;
            foreach (var v in mesh.vertices) _floor = Mathf.Min(_floor, toWorld.MultiplyPoint3x4(v).y);
        }

        if (_floor == float.MaxValue) _floor = 0f;

        // no hips (a Searcher drags itself, legless): the base of its spine is its hips
        if (_role[(int)R.Hips] < 0 && spineBase >= 0) _role[(int)R.Hips] = spineBase;
        Legs = _role[(int)R.LThigh] >= 0 && _role[(int)R.LShin] >= 0 && _role[(int)R.LFoot] >= 0 && _role[(int)R.RThigh] >= 0 && _role[(int)R.RShin] >= 0 && _role[(int)R.RFoot] >= 0;
        foreach (var r in new[] { R.Hips, R.Chest, R.Head, R.LUpper, R.LFore, R.LHand, R.RUpper, R.RFore, R.RHand })
            if (_role[(int)r] < 0)
            {
                Plugin.L.LogWarning($"Body: no {r} bone");
                return false;
            }

        // the torso, bottom to top: hips, the spine bones between the hips and the chest, the chest, the neck, the head
        _torso.Add(_role[(int)R.Hips]);
        var hipY = _rp[_role[(int)R.Hips]].y;
        var chestY = _rp[_role[(int)R.Chest]].y;
        spine.RemoveAll(i => _rp[i].y <= hipY + 1e-4f || _rp[i].y >= chestY - 1e-4f);
        spine.Sort((a, b) => _rp[a].y.CompareTo(_rp[b].y));
        _torso.AddRange(spine);
        _torso.Add(_role[(int)R.Chest]);
        if (_role[(int)R.Neck] >= 0) _torso.Add(_role[(int)R.Neck]);
        _torso.Add(_role[(int)R.Head]);

        // fingers: each finger's joints, base first
        for (var side = 0; side < 2; side++)
        {
            var hand = _role[side == 0 ? (int)R.LHand : (int)R.RHand];
            var names = new[] { new[] { "index" }, new[] { "middle", "mid" }, new[] { "ring" }, new[] { "pinky" }, new[] { "thumb" } };
            for (var f = 0; f < 5; f++) _fingers[side * 5 + f] = FingerChain(hand, side == 0 ? "l" : "r", names[f]);
        }

        // ---- head bones (the head and everything on it) ----
        var head = _role[(int)R.Head];
        foreach (var t in _all[head].GetComponentsInChildren<Transform>(true)) _headBones.Add(t.Pointer);

        // ---- bones nothing drives: they go with the nearest bone that is (and those riding on the head are the head's) ----
        BuildOrder();

        // ---- the eyes: between the eye bones if it has them, else at the front of the head, a little above its
        // middle (the head's own vertices), else the model's own eye point ----
        _devFace = "";
        var face = HeadBounds();
        _devFace += $" face {face.min:F2}..{face.max:F2} neck {(_role[(int)R.Neck] >= 0 ? _rp[_role[(int)R.Neck]] : Vector3.zero):F2} chest {_rp[_role[(int)R.Chest]]:F2} top {HeadTop():F2}";
        if (EyeBones(out var between)) _restEye = between;
        // (the middle by vertices, or by height if a dense lower face drags that down: a cartoon head's eyes are high)
        else if (face.size.y > 1e-4f) _restEye = new Vector3(_rp[head].x, Mathf.Max(_faceMedianY, face.min.y + face.size.y * 0.45f), Mathf.Lerp(_faceMedianZ, face.max.z, 0.5f));
        else
        {
            var eyeT = GameModels.Find(Root, "Eye_Position");
            _restEye = eyeT != null ? eyeT.position : _rp[head];
        }

        _restEyeY = Mathf.Max(0.1f, _restEye.y - _floor);
        BuildRings();
        BuildRenderers();
        return true;
    }

    /// <summary>
    /// Every skinned bone put where the meshes were bound to it (the prefab's own pose can differ: a raised arm, a mask
    /// held in a hand), so the rest pose the posing works from is the one the skin is made for. Parents first, so
    /// anything riding on a bone (a halo, the eye point) moves with it.
    /// </summary>
    private void ToBindPose()
    {
        var bind = new Dictionary<IntPtr, Matrix4x4>();
        foreach (var smr in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null) continue;
            var bones = smr.bones;
            var poses = mesh.bindposes;
            var meshToWorld = smr.transform.localToWorldMatrix;
            for (var i = 0; i < bones.Length && i < poses.Length; i++)
                if (bones[i] != null && !bind.ContainsKey(bones[i].Pointer))
                    bind[bones[i].Pointer] = meshToWorld * poses[i].inverse;
        }

        var order = new List<int>();
        for (var i = 0; i < _all.Count; i++)
            if (bind.ContainsKey(_all[i].Pointer))
                order.Add(i);
        order.Sort((a, b) => _depth[a].CompareTo(_depth[b]));
        foreach (var i in order)
        {
            var m = bind[_all[i].Pointer];
            var pos = new Vector3(m.m03, m.m13, m.m23);
            var fwd = new Vector3(m.m02, m.m12, m.m22);
            var up = new Vector3(m.m01, m.m11, m.m21);
            if (fwd.sqrMagnitude < 1e-12f || up.sqrMagnitude < 1e-12f) continue;
            _all[i].SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, up));
        }
    }

    private void Claim(R r, int i)
    {
        // a "drv_" or link transform never wins over the real bone
        if (_role[(int)r] < 0) _role[(int)r] = i;
    }

    /// <summary>"bn_L_ForeArm001" → "lforearm", "BN_Spine_A001" → "spinea", "bn_hips001" → "hips".</summary>
    private static string Key(string name)
    {
        var s = name.ToLowerInvariant();
        if (s.StartsWith("bn_")) s = s.Substring(3);
        else return "~" + s; // not a bone ("lnk_", "ptlnk_", "drv_", meshes...)
        s = s.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ' ', '_', '.');
        return s.Replace("_", "");
    }

    private int[] FingerChain(int hand, string side, string[] names)
    {
        var list = new List<int>();
        var handT = _all[hand];
        foreach (var t in handT.GetComponentsInChildren<Transform>(true))
        {
            var k = Key(t.name);
            if (!k.StartsWith(side)) continue;
            var rest = k.Substring(1);
            foreach (var nm in names)
                if (rest.StartsWith(nm) && !rest.Contains("twist"))
                {
                    var tail = rest.Substring(nm.Length);
                    if (nm == "mid" && tail.StartsWith("dle")) continue; // "middle" handled by its own name
                    list.Add(_index[t.Pointer]);
                    break;
                }
        }

        // base first: the chain is a parent-child line, so order by depth
        list.Sort((a, b) => _depth[a].CompareTo(_depth[b]));
        return list.Count > 0 ? list.ToArray() : null;
    }

    private float HeadTop()
    {
        var top = _rp[_role[(int)R.Head]].y;
        foreach (var smr in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            var toWorld = smr.transform.localToWorldMatrix;
            foreach (var v in mesh.vertices) top = Mathf.Max(top, toWorld.MultiplyPoint3x4(v).y);
        }

        return top;
    }

    private bool EyeBones(out Vector3 between)
    {
        between = default;
        Vector3? l = null, r = null;
        for (var i = 0; i < _all.Count; i++)
        {
            var k = Key(_all[i].name);
            if (k == "leye" || k == "leyeball") l = _rp[i];
            if (k == "reye" || k == "reyeball") r = _rp[i];
        }

        if (!l.HasValue || !r.HasValue) return false;
        between = (l.Value + r.Value) * 0.5f;
        return true;
    }

    /// <summary>Where the head's vertices are at rest (weighted to the head, or sitting where the head is), model space.</summary>
    private float _faceMedianY, _faceMedianZ;

    private Bounds HeadBounds()
    {
        var b = new Bounds();
        var any = false;
        var ys = new List<float>();
        var zs = new List<float>();
        foreach (var smr in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable || !smr.gameObject.activeInHierarchy) continue;
            var share = HeadShare(mesh, smr.bones, out _);
            var v = mesh.vertices;
            var toWorld = smr.transform.localToWorldMatrix;
            int byW = 0, bySpace = 0;
            var lowW = float.MaxValue;
            for (var i = 0; i < v.Length; i++)
            {
                var w = toWorld.MultiplyPoint3x4(v[i]);
                var sh = i < share.Length ? share[i] : 0f;
                var near = NearHead(w);
                if (sh >= 0.5f && near) { byW++; lowW = Mathf.Min(lowW, w.y); }
                else if (InHeadSpace(w)) bySpace++;
                if ((sh < 0.5f || !near) && !InHeadSpace(w)) continue;
                if (!any) b = new Bounds(w, Vector3.zero);
                else b.Encapsulate(w);
                any = true;
                ys.Add(w.y);
                zs.Add(w.z);
            }

            _devFace += $" [{smr.name} v{v.Length} share{share.Length} byW{byW} lowW{lowW:F2} bySpace{bySpace}]";
        }

        // the middle of the head by its vertices: horns, ears and hanging cables have few, the face has many
        if (ys.Count > 0)
        {
            ys.Sort();
            zs.Sort();
            _faceMedianY = ys[ys.Count / 2];
            _faceMedianZ = zs[zs.Count / 2];
        }

        return b;
    }

    /// <summary>Which bones are driven, and for the rest that hang off nothing driven, which driven bone they go with.</summary>
    private void BuildOrder()
    {
        var driven = new bool[_all.Count];
        foreach (var i in _role)
            if (i >= 0)
                driven[i] = true;
        foreach (var i in _torso) driven[i] = true;
        foreach (var t in _foreTwist)
            if (t >= 0)
                driven[t] = true;

        for (var i = 0; i < _all.Count; i++)
        {
            _anchor[i] = -1;
            if (driven[i] || HasDrivenAncestor(i, driven) || IsAncestorOfDriven(i, driven)) continue;
            // a skinned bone off on its own (spine extras, back spikes, cables, the camera): the nearest driven bone carries it
            var best = -1;
            var bd = float.MaxValue;
            for (var j = 0; j < _all.Count; j++)
            {
                if (!driven[j]) continue;
                var d = (_rp[j] - _rp[i]).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = j;
                }
            }

            _anchor[i] = best;
        }

        var head = _role[(int)R.Head];
        for (var i = 0; i < _all.Count; i++)
            if (_anchor[i] == head)
                foreach (var t in _all[i].GetComponentsInChildren<Transform>(true))
                    _headBones.Add(t.Pointer);

        for (var i = 0; i < _all.Count; i++)
            if (driven[i] || _anchor[i] >= 0)
                _order.Add(i);
        _order.Sort((a, b) => _depth[a].CompareTo(_depth[b]));
    }

    private bool HasDrivenAncestor(int i, bool[] driven)
    {
        for (var p = _all[i].parent; p != null && p != Root; p = p.parent)
            if (_index.TryGetValue(p.Pointer, out var j) && driven[j])
                return true;
        return false;
    }

    private bool IsAncestorOfDriven(int i, bool[] driven)
    {
        for (var j = 0; j < _all.Count; j++)
        {
            if (!driven[j]) continue;
            for (var p = _all[j].parent; p != null && p != Root; p = p.parent)
                if (p == _all[i])
                    return true;
        }

        return false;
    }

    private void BuildRenderers()
    {
        foreach (var r in Root.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.gameObject.activeInHierarchy) continue;
            var smr = r.TryCast<SkinnedMeshRenderer>();
            if (smr == null && InHandProp(r.transform))
            {
                // what the character carries (Sammy's axe, Allison's sword): you carry your own
                r.gameObject.SetActive(false);
                continue;
            }

            if (smr == null)
            {
                // props: on the head (a halo) they're the head's; anything else you see
                (OnHead(r.transform) ? _whole : _eyes).Add(r);
                continue;
            }

            smr.updateWhenOffscreen = true;
            if (Def.DrawnHead)
            {
                // the drawn head stands in for the model's: the headless copy is the body everyone sees
                var body = WithoutHead(smr);
                smr.enabled = false;
                smr.gameObject.SetActive(false);
                if (body != null)
                {
                    _always.Add(body);
                    _paint.Add((body, null));
                }

                continue;
            }

            _whole.Add(smr);
            var fp = WithoutHead(smr);
            if (fp != null) _eyes.Add(fp);
            _paint.Add((smr, fp));
        }
    }

    private bool InHandProp(Transform t)
    {
        for (var p = t; p != null && p != Root; p = p.parent)
        {
            var n = p.name.ToLowerInvariant();
            if (n.Contains("prp_") || n.Contains("prop_")) return true;
        }

        return false;
    }

    private bool OnHead(Transform t)
    {
        for (var p = t; p != null && p != Root; p = p.parent)
            if (_headBones.Contains(p.Pointer))
                return true;
        return false;
    }

    /// <summary>A copy of a skinned mesh without the triangles the head moves (or null if it's all head, Sammy's mask).</summary>
    private SkinnedMeshRenderer WithoutHead(SkinnedMeshRenderer smr)
    {
        var mesh = smr.sharedMesh;
        if (mesh == null || !mesh.isReadable) return null;
        var bones = smr.bones;
        var share = HeadShare(mesh, bones, out _);
        var rootOnHead = smr.rootBone != null && _headBones.Contains(smr.rootBone.Pointer);
        var verts = mesh.vertices;
        var toWorld = smr.transform.localToWorldMatrix;
        var onHead = new bool[verts.Length];
        var any = false;
        for (var i = 0; i < verts.Length; i++)
        {
            // weighted to the head; or no weights at all on a head-rooted mesh (Sammy's mask); or simply sitting where
            // the head is (the Projectionist's projector is skinned to his neck)
            var w = i < share.Length ? share[i] : (rootOnHead ? 1f : 0f);
            var at = toWorld.MultiplyPoint3x4(verts[i]);
            onHead[i] = (w > 0.35f && NearHead(at)) || InHeadSpace(at);
            any |= !onHead[i];
        }

        if (!any) return null;
        var copy = Object.Instantiate(mesh);
        copy.name = mesh.name + "_InkVR_NoHead";
        for (var s = 0; s < copy.subMeshCount; s++)
        {
            var tris = copy.GetTriangles(s);
            var keep = new List<int>(tris.Length);
            for (var t = 0; t + 2 < tris.Length; t += 3)
            {
                if (onHead[tris[t]] || onHead[tris[t + 1]] || onHead[tris[t + 2]]) continue;
                keep.Add(tris[t]);
                keep.Add(tris[t + 1]);
                keep.Add(tris[t + 2]);
            }

            copy.SetTriangles(keep.ToArray(), s);
        }

        var go = new GameObject(smr.name + "_Eyes") { layer = smr.gameObject.layer };
        go.transform.SetParent(smr.transform.parent, false);
        var fp = go.AddComponent<SkinnedMeshRenderer>();
        fp.sharedMesh = copy;
        fp.bones = bones;
        fp.rootBone = smr.rootBone;
        fp.sharedMaterials = smr.sharedMaterials;
        fp.updateWhenOffscreen = true;
        fp.shadowCastingMode = ShadowCastingMode.Off;
        fp.receiveShadows = smr.receiveShadows;
        return fp;
    }

    private bool IsHead(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Transform> bones, int i) =>
        i >= 0 && i < bones.Length && bones[i] != null && _headBones.Contains(bones[i].Pointer);

    /// <summary>
    /// Where the head is at rest: above halfway up the neck, and no further out than the head's own width (the arms,
    /// out in their T-pose, stay). Model space.
    /// </summary>
    private bool InHeadSpace(Vector3 v)
    {
        var head = _rp[_role[(int)R.Head]];
        var neck = _role[(int)R.Neck] >= 0 ? _rp[_role[(int)R.Neck]] : _rp[_role[(int)R.Chest]];
        if (v.y < (neck.y + head.y) * 0.5f || head.y <= neck.y) return false;
        // (T-pose arms are out at the shoulders, below this)
        return NearHead(v);
    }

    private float _nearHead = -1f;

    /// <summary>Within reach of the head bone at rest (the head's size, from the neck up, with room for horns and ears).</summary>
    private bool NearHead(Vector3 v)
    {
        if (_nearHead < 0)
        {
            var head = _rp[_role[(int)R.Head]];
            var neck = _role[(int)R.Neck] >= 0 ? _rp[_role[(int)R.Neck]] : _rp[_role[(int)R.Chest]];
            _nearHead = 1.6f * Mathf.Max(HeadTop() - neck.y, (head - neck).magnitude);
        }

        return (v - _rp[_role[(int)R.Head]]).magnitude < _nearHead;
    }


    /// <summary>
    /// How much of each vertex the head moves (0..1), and the head bone moving it most (or -1). The game's build
    /// stripped Mesh.boneWeights; the engine's own per-vertex counts and weight records are still there.
    /// </summary>
    private float[] HeadShare(Mesh mesh, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Transform> bones, out int[] top)
    {
        var n = mesh.vertexCount;
        var share = new float[n];
        top = new int[n];
        if (!ReadWeights(mesh, out var per, out var wt, out var bi)) return Array.Empty<float>();
        var k = 0;
        for (var v = 0; v < n && v < per.Length; v++)
        {
            top[v] = -1;
            var best = 0f;
            for (var j = 0; j < per[v] && k < wt.Length; j++, k++)
            {
                if (!IsHead(bones, bi[k])) continue;
                share[v] += wt[k];
                if (wt[k] > best)
                {
                    best = wt[k];
                    top[v] = bi[k];
                }
            }
        }

        return share;
    }

    // =====================================================================================================
    // Showing
    // =====================================================================================================

    /// <summary>A mirror is rendering: the whole model, head and all. Otherwise the headless copy, and the whole one in your shadow.</summary>
    public void ShowHead(bool on)
    {
        _headShown = on;
        Refresh();
    }

    public void ApplyShadows(bool on)
    {
        _shadows = on;
        Refresh();
    }

    /// <summary>Lying down or with your head near the floor: the body steps aside (as Henry's does).</summary>
    public void SetLow(bool low)
    {
        if (_low == low) return;
        _low = low;
        Refresh();
    }

    /// <summary>Dev: draw the whole model (head and all) for every camera, for pictures of it from outside.</summary>
    public static bool DevWhole;

    private bool _devWhole;

    private void Refresh()
    {
        _devWhole = DevWhole;
        var whole = _headShown || DevWhole;
        foreach (var r in _eyes)
            if (r != null)
                r.enabled = !_low && !whole;
        foreach (var r in _always)
        {
            if (r == null) continue;
            r.enabled = !_low;
            var cast = _shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (r.shadowCastingMode != cast) r.shadowCastingMode = cast;
        }
        foreach (var r in _whole)
        {
            if (r == null) continue;
            var mode = whole ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            if (r.shadowCastingMode != mode) r.shadowCastingMode = mode;
            r.enabled = !_low && (whole || _shadows);
        }
    }

    public void SetLayer(int layer)
    {
        if (Root.gameObject.layer == layer) return;
        foreach (var t in Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    public void SetVisible(bool on)
    {
        if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
    }

    public void Destroy()
    {
        foreach (var r in _eyes)
        {
            var smr = r != null ? r.TryCast<SkinnedMeshRenderer>() : null;
            if (smr != null && smr.sharedMesh != null && smr.sharedMesh.name.EndsWith("_InkVR_NoHead")) Object.Destroy(smr.sharedMesh);
        }

        Object.Destroy(Root.gameObject);
    }

    // =====================================================================================================
    // Posing
    // =====================================================================================================

    /// <summary>Henry's solved skeleton this frame, in world space (see HenryBody.SolvedPose).</summary>
    public struct Pose
    {
        public Vector3 Eye;
        public Quaternion HeadRot;
        public float K; // world units per metre of Henry
        public Quaternion Pelvis, Spine, Chest, Neck; // Henry's torso frames (z along the bone, y forward)
        public Vector3 NeckBase; // the bottom of Henry's neck
        public Vector3 HipL, HipR, KneeL, KneeR, AnkleL, AnkleR;
        public Quaternion FootL, FootR; // z towards the toes, y up
        public float GroundL, GroundR;
        public Vector3 ElbowHintL, ElbowHintR;
        public float HenryLeg; // hip to ankle, standing (world)
        public Vector3 HenryAnkleOffset; // ankle minus hip, standing, in Henry's own frame (metres)
    }

    /// <summary>The hands this frame: the wrist frame (z along the fingers, y out of the back of the hand) and curls.</summary>
    public struct HandPose
    {
        public bool Valid;
        public Vector3 Wrist;
        public Quaternion Rot;
        public float Scale; // world units per metre of hand
        public float Index, Grip, Thumb;

        /// <summary>The hand's own finger joints, posed (<see cref="HandModel.FingerJoints"/>): the fingers copy them.</summary>
        public Transform[] Joints;
    }

    private readonly Quaternion[] _wristFrame = new Quaternion[2];
    private readonly bool[] _wristOk = new bool[2];
    private readonly float[] _wristRadius = { -1f, -1f };

    /// <summary>
    /// This frame's wrist (0 left, 1 right): the joint, the hand's frame there, and the wrist's radius (world), for
    /// the watch.
    /// </summary>
    public bool Wrist(int side, out Vector3 pos, out Quaternion frame, out float radius)
    {
        var hand = _role[side == 0 ? (int)R.LHand : (int)R.RHand];
        pos = _all[hand].position;
        frame = _wristFrame[side];
        if (_wristRadius[side] < 0) _wristRadius[side] = MeasureWrist(side);
        radius = _wristRadius[side] * Scale;
        return _wristOk[side];
    }

    // ---- where the watch goes: points round each forearm's skin just short of the wrist (the furthest out in sixteen
    // directions, at both ends of the strap and its middle), each as the bones that move it, how much, and where it sat
    // on each at bind ----

    private const int RingDirs = 16, RingSlices = 3, RingPts = RingDirs * RingSlices;
    private readonly (Transform bone, float w, Vector3 at)[][][] _ring = new (Transform, float, Vector3)[2][][];
    private readonly Vector3[] _ringPts = new Vector3[RingPts];

    private void BuildRings()
    {
        for (var side = 0; side < 2; side++)
        {
            var hand = _role[side == 0 ? (int)R.LHand : (int)R.RHand];
            var fore = _role[side == 0 ? (int)R.LFore : (int)R.RFore];
            var hp = _rp[hand];
            var axis = hp - _rp[fore];
            var len = axis.magnitude;
            if (len < 1e-5f) continue;
            axis /= len;
            var back = Vector3.ProjectOnPlane(Canonical(side, out _) * Vector3.up, axis).normalized;
            if (back.sqrMagnitude < 0.5f) continue;
            var across = Vector3.Cross(axis, back);
            var dirs = new Vector3[RingDirs];
            for (var k = 0; k < RingDirs; k++)
            {
                var a = k * (360f / RingDirs) * Mathf.Deg2Rad;
                dirs[k] = back * Mathf.Cos(a) + across * Mathf.Sin(a);
            }

            var best = new float[RingPts];
            var pick = new (SkinnedMeshRenderer smr, int v)[RingPts];
            for (var k = 0; k < RingPts; k++) best[k] = float.MinValue;
            foreach (var smr in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null || !mesh.isReadable || !smr.gameObject.activeInHierarchy || smr.name.EndsWith("_Eyes")) continue;
                var verts = mesh.vertices;
                var toWorld = smr.transform.localToWorldMatrix;
                for (var v = 0; v < verts.Length; v++)
                {
                    var d = toWorld.MultiplyPoint3x4(verts[v]) - hp;
                    var t = Vector3.Dot(d, axis);
                    // (the length of the strap, about two centimetres up the arm from the wrist on a Henry-sized arm)
                    const float from = -0.135f, to = -0.035f;
                    if (t < from * len || t > to * len) continue;
                    var radial = d - axis * t;
                    if (radial.magnitude > 0.3f * len) continue;
                    var slice = Mathf.Clamp((int)((t / len - from) / (to - from) * RingSlices), 0, RingSlices - 1);
                    for (var k = 0; k < RingDirs; k++)
                    {
                        var s = Vector3.Dot(radial, dirs[k]);
                        var at = slice * RingDirs + k;
                        if (s <= best[at]) continue;
                        best[at] = s;
                        pick[at] = (smr, v);
                    }
                }
            }

            var ring = new (Transform, float, Vector3)[RingPts][];
            for (var k = 0; k < RingPts && ring != null; k++)
            {
                if (pick[k].smr == null) ring = null;
                else ring[k] = Influences(pick[k].smr, pick[k].v);
                if (ring != null && ring[k] == null) ring = null;
            }

            _ring[side] = ring;
        }
    }

    /// <summary>A vertex as its bones, weights, and where it sits in each bone's space at bind (so it can be skinned here).</summary>
    private static (Transform, float, Vector3)[] Influences(SkinnedMeshRenderer smr, int v)
    {
        var mesh = smr.sharedMesh;
        if (!ReadWeights(mesh, out var per, out var weight, out var bone)) return null;
        var k = 0;
        for (var i = 0; i < v; i++) k += per[i];
        var bones = smr.bones;
        var poses = mesh.bindposes;
        var p = mesh.vertices[v];
        var list = new List<(Transform, float, Vector3)>();
        for (var j = 0; j < per[v] && k + j < weight.Length; j++)
        {
            var b = bone[k + j];
            if (b < 0 || b >= bones.Length || b >= poses.Length || bones[b] == null || weight[k + j] <= 0f) continue;
            list.Add((bones[b], weight[k + j], poses[b].MultiplyPoint3x4(p)));
        }

        return list.Count > 0 ? list.ToArray() : null;
    }

    /// <summary>
    /// Where a watch sits on this forearm right now, fitted to its skin: the middle of the strap, its frame (z along the
    /// arm towards the hand, y out of the back of the hand, so the face turns with your hand like a real watch) and the
    /// half-width and half-depth of the oval the strap must be to go round the skin without sinking into it.
    /// </summary>
    public bool WatchSpot(int side, out Vector3 centre, out Quaternion frame, out float halfWidth, out float halfDepth)
    {
        centre = default;
        frame = Quaternion.identity;
        halfWidth = halfDepth = 0f;
        var ring = _ring[side];
        if (!_wristOk[side] || ring == null) return false;
        var c = Vector3.zero;
        for (var k = 0; k < RingPts; k++)
        {
            var p = Vector3.zero;
            foreach (var (b, w, at) in ring[k]) p += b.localToWorldMatrix.MultiplyPoint3x4(at) * w;
            _ringPts[k] = p;
            c += p;
        }

        c /= RingPts;
        var hand = _all[_role[side == 0 ? (int)R.LHand : (int)R.RHand]].position;
        var fore = _all[_role[side == 0 ? (int)R.LFore : (int)R.RFore]].position;
        var along = (hand - fore).normalized;
        var up = Vector3.ProjectOnPlane(_wristFrame[side] * Vector3.up, along);
        if (up.sqrMagnitude < 1e-10f || along.sqrMagnitude < 0.5f) return false;
        frame = Quaternion.LookRotation(along, up);
        var x = frame * Vector3.right;
        var y = frame * Vector3.up;
        float hw = 1e-4f, hd = 1e-4f;
        for (var k = 0; k < RingPts; k++)
        {
            var d = _ringPts[k] - c;
            hw = Mathf.Max(hw, Mathf.Abs(Vector3.Dot(d, x)));
            hd = Mathf.Max(hd, Mathf.Abs(Vector3.Dot(d, y)));
        }

        // the oval as wide and deep as the skin, grown until every point is inside it
        var m = 1f;
        for (var k = 0; k < RingPts; k++)
        {
            var d = _ringPts[k] - c;
            var ex = Vector3.Dot(d, x) / hw;
            var ey = Vector3.Dot(d, y) / hd;
            m = Mathf.Max(m, Mathf.Sqrt(ex * ex + ey * ey));
        }

        centre = c;
        halfWidth = hw * m;
        halfDepth = hd * m;
        return true;
    }

    /// <summary>How thick the forearm is just short of the wrist, at rest (model units): the middle of its vertices there.</summary>
    private float MeasureWrist(int side)
    {
        var hand = _rp[_role[side == 0 ? (int)R.LHand : (int)R.RHand]];
        var fore = _rp[_role[side == 0 ? (int)R.LFore : (int)R.RFore]];
        var axis = hand - fore;
        var len = axis.magnitude;
        if (len < 1e-5f) return 0.05f;
        axis /= len;
        var d = new List<float>();
        foreach (var smr in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable || smr.name.EndsWith("_Eyes")) continue;
            var toWorld = smr.transform.localToWorldMatrix;
            foreach (var v0 in mesh.vertices)
            {
                var v = toWorld.MultiplyPoint3x4(v0) - hand;
                var t = Vector3.Dot(v, axis);
                if (t < -0.12f * len || t > -0.02f * len) continue;
                var r = (v - axis * t).magnitude;
                if (r < 0.3f * len) d.Add(r);
            }
        }

        if (d.Count == 0) return 0.08f * len;
        d.Sort();
        return d[d.Count / 2];
    }

    public void Apply(in Pose p, in HandPose left, in HandPose right)
    {
        _wristOk[0] = _wristOk[1] = false;
        if (_devWhole != DevWhole) Refresh();
        var s = Scale = 1.62f * p.K / _restEyeY;
        for (var i = 0; i < _set.Length; i++) _set[i] = false;

        // ---- the head: turned with yours, its eyes at yours ----
        var head = _role[(int)R.Head];
        var headRot = p.HeadRot * _rr[head];
        var eyeInHead = Quaternion.Inverse(_rr[head]) * (_restEye - _rp[head]);
        Target(head, p.Eye - headRot * eyeInHead * s, headRot);

        // ---- the base of the neck: where Henry's is, relative to your eyes, at this body's size. Looking down, Henry's
        // drops back (so his chest leaves your view and your feet come into it); this body's does the same ----
        var top = _torso.Count - 2;
        var topBone = _torso[top];
        var neck = p.Eye + (p.NeckBase - p.Eye) * ((_restEye - _rp[topBone]).magnitude * s / Mathf.Max((p.NeckBase - p.Eye).magnitude, 1e-4f));
        var neckRest = _rp[head] - _rp[topBone];
        Target(topBone, neck, Look(_tp[head] - neck, p.Neck * Vector3.up) * Quaternion.Inverse(Look(neckRest, Vector3.forward)) * _rr[topBone]);

        // ---- the torso hangs from there along Henry's spine: chest, spine, hips (each its own length) ----
        var below = neck;
        var total = 0f;
        for (var j = 0; j < top; j++) total += (_rp[_torso[j + 1]] - _rp[_torso[j]]).magnitude;
        var along = total;
        for (var j = top - 1; j >= 0; j--)
        {
            var bone = _torso[j];
            var next = _torso[j + 1];
            var rest = _rp[next] - _rp[bone];
            var len = rest.magnitude;
            var u = (along - len * 0.5f) / Mathf.Max(total, 1e-4f);
            var frame = u < 0.3f ? p.Pelvis : u < 0.66f ? p.Spine : p.Chest;
            along -= len;
            var restFrame = Look(rest, Vector3.forward);
            var rot = frame * Quaternion.Inverse(restFrame) * _rr[bone];
            var pos = below - frame * Vector3.forward * (len * s);
            Target(bone, pos, rot);
            below = pos;
        }

        // ---- the legs: from the hips to where Henry's feet are planted, at this body's own size ----
        if (Legs)
        {
            Leg(0, p, s);
            Leg(1, p, s);
        }

        // ---- the arms and hands ----
        Arm(0, p, left, s);
        Arm(1, p, right, s);

        // ---- bones nothing drives go with the nearest one that is ----
        foreach (var i in _order)
        {
            var a = _anchor[i];
            if (a < 0 || !_set[a]) continue;
            var inv = Quaternion.Inverse(_rr[a]);
            Target(i, _tp[a] + _tq[a] * (inv * (_rp[i] - _rp[a])) * s, _tq[a] * inv * _rr[i]);
        }

        // ---- apply, parents first ----
        Root.position = _tp[_role[(int)R.Hips]];
        Root.rotation = Quaternion.Euler(0, YawOf(p.Pelvis * Vector3.up), 0);
        var parentScale = Root.parent != null ? Root.parent.lossyScale.x : 1f;
        Root.localScale = Vector3.one * (s / Mathf.Max(parentScale, 1e-6f));
        foreach (var i in _order)
        {
            if (!_set[i]) continue;
            _all[i].SetPositionAndRotation(_tp[i], _tq[i]);
        }

        Fingers(0, left);
        Fingers(1, right);

        // the projector's beam: from the lens, where you look, with a projector's flicker
        if (Def.LensLight)
        {
            if (_lens == null)
            {
                var go = new GameObject("InkVR_LensLight");
                go.transform.SetParent(Root, false);
                _lens = go.AddComponent<Light>();
                _lens.type = LightType.Spot;
                _lens.spotAngle = 38f;
                _lens.color = new Color(1f, 0.93f, 0.78f);
                _lens.shadows = LightShadows.None;
            }

            _lens.transform.SetPositionAndRotation(p.Eye + p.HeadRot * Vector3.forward * (0.12f * p.K), p.HeadRot);
            _lens.range = 6f * p.K;
            _lens.intensity = 1.0f + 0.15f * Mathf.PerlinNoise(Time.unscaledTime * 12f, 0.3f);
            _lens.enabled = !_low;
        }
    }

    private Light _lens;

    private void Target(int i, Vector3 pos, Quaternion rot)
    {
        _tp[i] = pos;
        _tq[i] = rot;
        _set[i] = true;
    }

    private static Quaternion Look(Vector3 dir, Vector3 up) =>
        dir.sqrMagnitude < 1e-10f ? Quaternion.identity : Quaternion.LookRotation(dir, Mathf.Abs(Vector3.Dot(dir.normalized, up.normalized)) > 0.98f ? Vector3.up : up);

    private static float YawOf(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

    /// <summary>A pose relative to a parent's rest: where the child is now, given where its parent is now.</summary>
    private void Carry(int parent, int child)
    {
        var inv = Quaternion.Inverse(_rr[parent]);
        Target(child, _tp[parent] + _tq[parent] * (inv * (_rp[child] - _rp[parent])) * Scale, _tq[parent] * inv * _rr[child]);
    }

    private void Leg(int side, in Pose p, float s)
    {
        var thigh = _role[side == 0 ? (int)R.LThigh : (int)R.RThigh];
        var shin = _role[side == 0 ? (int)R.LShin : (int)R.RShin];
        var foot = _role[side == 0 ? (int)R.LFoot : (int)R.RFoot];
        var toe = _role[side == 0 ? (int)R.LToe : (int)R.RToe];
        Carry(_role[(int)R.Hips], thigh);
        var hip = _tp[thigh];

        var henryHip = side == 0 ? p.HipL : p.HipR;
        var henryAnkle = side == 0 ? p.AnkleL : p.AnkleR;
        var henryKnee = side == 0 ? p.KneeL : p.KneeR;
        var footRot = side == 0 ? p.FootL : p.FootR;
        var ground = side == 0 ? p.GroundL : p.GroundR;

        var a = (_rp[shin] - _rp[thigh]).magnitude * s;
        var b = (_rp[foot] - _rp[shin]).magnitude * s;
        // where the foot goes: this body's own stance (its rest foot under its hip, turned with the foot), moved by
        // as much as Henry's foot has stepped away from his, scaled to this body's legs
        var yaw = Quaternion.Euler(0, YawOf(footRot * Vector3.forward), 0);
        var restOff = _rp[foot] - _rp[thigh];
        var stance = yaw * new Vector3(restOff.x, 0, restOff.z) * s;
        var henryStance = yaw * new Vector3(p.HenryAnkleOffset.x, 0, p.HenryAnkleOffset.z) * p.K;
        var stepped = henryAnkle - henryHip - henryStance;
        stepped.y = 0;
        var ratio = (a + b) / Mathf.Max(p.HenryLeg, 1e-4f);
        var ankle = hip + stance + stepped * ratio;
        ankle.y = ground + (_rp[foot].y - _floor) * s;

        var pole = henryKnee - (henryHip + henryAnkle) * 0.5f;
        var knee = TwoBone(hip, ankle, a, b, pole, out ankle);
        var restBend = Vector3.forward;
        Target(thigh, hip, Look(knee - hip, pole) * Quaternion.Inverse(Look(_rp[shin] - _rp[thigh], restBend)) * _rr[thigh]);
        Target(shin, knee, Look(ankle - knee, pole) * Quaternion.Inverse(Look(_rp[foot] - _rp[shin], restBend)) * _rr[shin]);
        var restFoot = toe >= 0 ? _rp[toe] - _rp[foot] : Vector3.forward;
        restFoot.y = Mathf.Min(restFoot.y, 0);
        Target(foot, ankle, footRot * Quaternion.Inverse(Look(restFoot, Vector3.up)) * _rr[foot]);
    }

    private readonly float[] _twist = new float[2];

    private void Arm(int side, in Pose p, in HandPose h, float s)
    {
        var clav = _role[side == 0 ? (int)R.LClav : (int)R.RClav];
        var upper = _role[side == 0 ? (int)R.LUpper : (int)R.RUpper];
        var fore = _role[side == 0 ? (int)R.LFore : (int)R.RFore];
        var hand = _role[side == 0 ? (int)R.LHand : (int)R.RHand];
        var chest = _role[(int)R.Chest];
        if (clav >= 0)
        {
            Carry(chest, clav);
            Carry(clav, upper);
        }
        else Carry(chest, upper);

        var shoulder = _tp[upper];
        if (!h.Valid)
        {
            // no controller: the arm hangs as it rests, carried by the chest
            Carry(upper, fore);
            Carry(fore, hand);
            return;
        }

        // the hand: turned like the controller's hand, its palm where Henry's palm would be
        var canon = Canonical(side, out var palm);
        var handRot = h.Rot * Quaternion.Inverse(canon) * _rr[hand];
        var palmWorld = h.Wrist + h.Rot * new Vector3(0, 0, 0.045f) * h.Scale;
        var wrist = palmWorld - handRot * palm * s;

        var a = (_rp[fore] - _rp[upper]).magnitude * s;
        var b = (_rp[hand] - _rp[fore]).magnitude * s;
        // the elbow hangs down and back, only a little out (these arms are often longer than Henry's, and bend more)
        var bodyUp = p.Chest * Vector3.forward;
        var bodyFwd = p.Chest * Vector3.up;
        var bodyRight = Vector3.Cross(bodyUp, bodyFwd);
        var hint = bodyRight * (side == 0 ? -0.2f : 0.2f) - bodyUp * 0.75f - bodyFwd * 0.35f;
        // reaching further than the arm: the shoulder comes forward first (the collarbone swings, as yours does)
        if (clav >= 0)
        {
            var over = (wrist - shoulder).magnitude - (a + b) * 0.97f;
            if (over > 0f)
            {
                var pivot = _tp[clav];
                var want = shoulder + (wrist - shoulder).normalized * Mathf.Min(over, 0.08f * s);
                var turn = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(shoulder - pivot, want - pivot), 30f);
                Target(clav, pivot, turn * _tq[clav]);
                Carry(clav, upper);
                shoulder = _tp[upper];
            }
        }

        var reach = wrist - shoulder;
        _devArm[side] = $"{(side == 0 ? "L" : "R")} shoulder {shoulder:F2} wrist {wrist:F2} reach {reach.magnitude:F2}/{a + b:F2}";
        Vector3 elbow;
        if (reach.magnitude >= (a + b) * 0.999f)
        {
            // too short to reach (a short-armed character, a long reach): the arm straightens and stretches
            elbow = shoulder + reach * (a / (a + b));
        }
        else elbow = TwoBone(shoulder, wrist, a, b, hint, out _);

        // (the plane the elbow bends in, from its pole: the elbow's own offset vanishes as the arm straightens, and
        // with it the arm's roll would flicker)
        var bend = Vector3.ProjectOnPlane(hint, reach);
        if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(elbow - (shoulder + wrist) * 0.5f, reach);
        var restReach = _rp[hand] - _rp[upper];
        var restBend = Vector3.ProjectOnPlane(_rp[fore] - (_rp[upper] + _rp[hand]) * 0.5f, restReach);
        // a straight (or nearly straight) T-pose arm has no bend of its own to go by: elbows bend backwards
        if (restBend.magnitude < restReach.magnitude * 0.08f) restBend = Vector3.back;
        Target(upper, shoulder, Look(elbow - shoulder, bend) * Quaternion.Inverse(Look(_rp[fore] - _rp[upper], restBend)) * _rr[upper]);
        var foreRot = Look(wrist - elbow, bend) * Quaternion.Inverse(Look(_rp[hand] - _rp[fore], restBend)) * _rr[fore];
        if (_foreTwist[side] < 0)
        {
            // no twist bone (Henry's model): the forearm itself turns half way with the hand, as an arm's bones do, so the
            // wrist doesn't wring
            var axis = (wrist - elbow).normalized;
            var restUp = Vector3.ProjectOnPlane(_rr[hand] * Vector3.up, (_rp[hand] - _rp[fore]).normalized);
            var foreUp = foreRot * (Quaternion.Inverse(_rr[fore]) * restUp);
            var handUp = handRot * (Quaternion.Inverse(_rr[hand]) * restUp);
            var twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(foreUp, axis), Vector3.ProjectOnPlane(handUp, axis), axis);
            // (continuous from last frame, so it never flips half a turn as the hand passes straight back)
            twist = Mathf.Clamp(_twist[side] + Mathf.DeltaAngle(_twist[side], twist), -160f, 160f);
            _twist[side] = twist;
            foreRot = Quaternion.AngleAxis(twist * 0.5f, axis) * foreRot;
        }

        Target(fore, elbow, foreRot);
        Target(hand, wrist, handRot);
        _wristFrame[side] = h.Rot;
        _wristOk[side] = true;

        // the forearm's twist bone takes half the wrist's turn, so the wrist doesn't wring
        var tw = _foreTwist[side];
        if (tw >= 0)
        {
            var axis = (wrist - elbow).normalized;
            var restAxis = (_rp[hand] - _rp[fore]).normalized;
            var restUp = Vector3.ProjectOnPlane(_rr[hand] * Vector3.up, restAxis);
            var foreUp = _tq[fore] * (Quaternion.Inverse(_rr[fore]) * restUp);
            var handUp = handRot * (Quaternion.Inverse(_rr[hand]) * restUp);
            var twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(foreUp, axis), Vector3.ProjectOnPlane(handUp, axis), axis);
            var mid = Vector3.Lerp(_tp[fore], wrist, (_rp[tw] - _rp[fore]).magnitude / Mathf.Max((_rp[hand] - _rp[fore]).magnitude, 1e-4f));
            var inv = Quaternion.Inverse(_rr[fore]);
            Target(tw, mid, Quaternion.AngleAxis(twist * 0.6f, axis) * _tq[fore] * inv * _rr[tw]);
        }
    }

    /// <summary>
    /// The model's hand at rest, as a frame like the controllers' hand: z along the fingers, y out of the back of the
    /// hand (the thumb towards -x on the right hand, +x on the left). <paramref name="palm"/>: the palm's middle, in the
    /// hand bone's own space (unscaled).
    /// </summary>
    private Quaternion Canonical(int side, out Vector3 palm)
    {
        var hand = _role[side == 0 ? (int)R.LHand : (int)R.RHand];
        var middle = _fingers[side * 5 + 1] ?? _fingers[side * 5] ?? _fingers[side * 5 + 3];
        var thumb = _fingers[side * 5 + 4];
        var knuckle = middle != null ? _rp[middle[0]] : _rp[hand] + (_rp[hand] - _rp[_role[side == 0 ? (int)R.LFore : (int)R.RFore]]).normalized * 0.1f;
        var along = (knuckle - _rp[hand]).normalized;
        var t = thumb != null ? Vector3.ProjectOnPlane(_rp[thumb[0]] - _rp[hand], along).normalized : (side == 0 ? Vector3.forward : Vector3.forward);
        var back = side == 1 ? Vector3.Cross(t, along) : Vector3.Cross(along, t);
        var index = _fingers[side * 5];
        var pinky = _fingers[side * 5 + 3];
        if (index != null && pinky != null)
        {
            var knuckles = Vector3.Cross(_rp[index[0]] - _rp[hand], _rp[pinky[0]] - _rp[hand]);
            if (knuckles.sqrMagnitude > 1e-12f) back = Vector3.Dot(knuckles, back) >= 0 ? knuckles.normalized : -knuckles.normalized;
        }
        palm = Quaternion.Inverse(_rr[hand]) * ((knuckle - _rp[hand]) * 0.5f - back * ((knuckle - _rp[hand]).magnitude * 0.12f));
        return Quaternion.LookRotation(along, back);
    }

    private void Fingers(int side, in HandPose h)
    {
        if (!h.Valid) return;
        if (h.Joints != null)
        {
            // each segment pointed where the same segment of Henry's own hand points (it's posed every frame, hidden):
            // so the fingers rest, close and hold things exactly as they always have
            for (var f = 0; f < 5; f++)
            {
                var chain = _fingers[side * 5 + f];
                if (chain == null) continue;
                for (var j = 0; j < chain.Length; j++)
                {
                    var i = chain[j];
                    var bone = _all[i];
                    bone.localRotation = _rl[i];
                    var src = h.Joints[f * 3 + Mathf.Min(j, 2)];
                    var rest = j + 1 < chain.Length ? _rp[chain[j + 1]] - _rp[i] : j > 0 ? _rp[i] - _rp[chain[j - 1]] : Vector3.zero;
                    if (src == null || rest.sqrMagnitude < 1e-12f) continue;
                    var now = bone.rotation * (Quaternion.Inverse(_rr[i]) * rest);
                    bone.rotation = Quaternion.FromToRotation(now, src.forward) * bone.rotation;
                }
            }

            return;
        }

        var canonBack = Canonical(side, out _) * Vector3.up;
        for (var f = 0; f < 5; f++)
        {
            var chain = _fingers[side * 5 + f];
            if (chain == null) continue;
            var curl = f == 0 ? h.Index : f == 4 ? h.Thumb : Mathf.Min(1f, h.Grip + f * 0.02f);
            for (var j = 0; j < chain.Length; j++)
            {
                var i = chain[j];
                var next = j + 1 < chain.Length ? chain[j + 1] : -1;
                var dir = next >= 0 ? _rp[next] - _rp[i] : (j > 0 ? _rp[i] - _rp[chain[j - 1]] : Vector3.forward);
                var axisWorld = Vector3.Cross(canonBack, dir.normalized);
                if (axisWorld.sqrMagnitude < 1e-6f) continue;
                var axis = Quaternion.Inverse(_rr[i]) * axisWorld.normalized;
                float angle;
                if (f == 4) angle = j == 0 ? curl * 25f : curl * (j == 1 ? 45f : 55f);
                else angle = 6f + curl * (j == 0 ? 72f : j == 1 ? 95f : 70f);
                _all[i].localRotation = _rl[i] * Quaternion.AngleAxis(angle, axis);
            }
        }
    }

    /// <summary>Two-bone IK: the middle joint, bent towards the pole; the end clamped to what the bones can reach.</summary>
    private static Vector3 TwoBone(Vector3 start, Vector3 end, float a, float b, Vector3 pole, out Vector3 reached)
    {
        var to = end - start;
        var d = Mathf.Clamp(to.magnitude, Mathf.Abs(a - b) + 1e-4f, (a + b) * 0.999f);
        var dir = to.sqrMagnitude > 1e-10f ? to.normalized : Vector3.down;
        reached = start + dir * d;
        var cosA = (a * a + d * d - b * b) / (2 * a * d);
        var sinA = Mathf.Sqrt(Mathf.Max(0, 1 - cosA * cosA));
        var bend = Vector3.ProjectOnPlane(pole, dir).normalized;
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.forward, dir).normalized;
        return start + dir * (a * cosA) + bend * (a * sinA);
    }

    private readonly string[] _devArm = { "-", "-" };
    private string _devFace = "-";

    /// <summary>Dev: the rig as found.</summary>
    public string Describe()
    {
        var sb = new System.Text.StringBuilder($"{Def.Label}: eye {_restEye:F2} head {_rp[_role[(int)R.Head]]:F2} {_devFace} scale {Scale:F3} torso");
        foreach (var i in _torso) sb.Append(' ').Append(_all[i].name);
        for (var r = 0; r < (int)R.Count; r++) sb.Append($" {(R)r}={(_role[r] >= 0 ? _all[_role[r]].name : "-")}");
        for (var f = 0; f < 10; f++) sb.Append($" f{f}={(_fingers[f] != null ? _fingers[f].Length : 0)}");
        var anchored = 0;
        foreach (var a in _anchor)
            if (a >= 0)
                anchored++;
        sb.Append($" anchored={anchored} eyes={_eyes.Count} whole={_whole.Count} legs={Legs} | {_devArm[0]} | {_devArm[1]}");
        return sb.ToString();
    }
}
