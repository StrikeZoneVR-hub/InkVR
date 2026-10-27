using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.Body;

/// <summary>
/// The game's own character models (Alice, Sammy, the Projectionist, the Ink Demon, the Searchers...), loaded from
/// the player's installation through the game's Resources and stripped down to what's drawn: no AI, no animator, no
/// physics, no sounds. Nothing from the game ships with InkVR.
/// </summary>
public static class GameModels
{
    /// <summary>
    /// A copy of a Resources prefab with only its transforms and renderers. It's made under an inactive holder, so none
    /// of the game's scripts on it ever wake. Null if the path isn't there.
    /// </summary>
    public static GameObject Spawn(string path, Transform parent, int layer)
    {
        var loaded = Resources.Load(path);
        var prefab = loaded != null ? loaded.TryCast<GameObject>() : null;
        if (prefab == null)
        {
            Plugin.L.LogWarning($"Game model '{path}' not found");
            return null;
        }

        var holder = new GameObject("InkVR_ModelHolder");
        holder.SetActive(false);
        var go = Object.Instantiate(prefab, holder.transform);
        go.name = "InkVR_Model_" + prefab.name;
        Strip(go);
        go.transform.SetParent(parent, false);
        Object.Destroy(holder);
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        return go;
    }

    /// <summary>Everything but transforms, renderers and mesh filters goes: scripts first (others depend on them).</summary>
    private static void Strip(GameObject go)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            var left = 0;
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null || Keep(c)) continue;
                var script = c.TryCast<MonoBehaviour>() != null;
                if (pass == 0 && !script) continue;
                try
                {
                    Object.DestroyImmediate(c);
                }
                catch
                {
                    left++;
                }
            }

            if (pass > 0 && left == 0) break;
        }
    }

    private static bool Keep(Component c) =>
        c.TryCast<Transform>() != null || c.TryCast<SkinnedMeshRenderer>() != null || c.TryCast<MeshRenderer>() != null || c.TryCast<MeshFilter>() != null;

    /// <summary>Dev: what a model is made of.</summary>
    public static string Describe(GameObject go)
    {
        var sb = new StringBuilder();
        var b = new Bounds();
        var any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (any) b.Encapsulate(r.bounds);
            else b = r.bounds;
            any = true;
            var smr = r.TryCast<SkinnedMeshRenderer>();
            var mesh = smr != null ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            sb.Append($"\n  {r.name}: {(smr != null ? "skinned" : "mesh")} {(mesh != null ? $"{mesh.name} v{mesh.vertexCount} sub{mesh.subMeshCount} readable={mesh.isReadable} blend={mesh.blendShapeCount}" : "-")}");
            if (smr != null) sb.Append($" bones={smr.bones.Length} root={(smr.rootBone != null ? smr.rootBone.name : "-")}");
            foreach (var m in r.sharedMaterials)
                if (m != null)
                    sb.Append($"\n    mat {m.name} shader={(m.shader != null ? m.shader.name : "-")} queue={m.renderQueue}");
        }

        var eye = Find(go.transform, "Eye_Position");
        sb.Insert(0, $"{go.name}: bounds {b.min:F2}..{b.max:F2} eye={(eye != null ? eye.position.ToString("F2") : "-")}");
        return sb.ToString();
    }

    public static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name)
                return t;
        return null;
    }
}
