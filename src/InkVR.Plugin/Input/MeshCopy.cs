using System.Collections.Generic;
using UnityEngine;

namespace InkVR.Input;

/// <summary>
/// Copies of the game's objects that can be carried (in the hand, in flight, picked up and thrown).
///
/// Most are plain mesh copies. But the game merged many props into one big static mesh per scene when it was built
/// (the Bacon Soup cans on the shelves, the chairs): those objects no longer have a mesh of their own, only a slice
/// of the scene's batch, in world space. A plain copy drew some other slice of the level somewhere else, so a can
/// vanished from your hand the moment you took it. Batched pieces are drawn from their own slice of the batch
/// instead, every frame, wherever their carrier is.
/// </summary>
public static class MeshCopy
{
    private sealed class Slice
    {
        public Transform Carrier;
        public Mesh Mesh;
        public Material[] Materials;
        public int First;
        public Matrix4x4 ToCarrier;
        public int Layer;
    }

    private static readonly List<Slice> Slices = new();

    /// <summary>
    /// Copy one renderer's look onto the carrier, where it is now relative to the carrier. Set the carrier's pose
    /// and scale before calling. Returns the copy's world bounds (for centring in a hand).
    /// </summary>
    public static Bounds Add(MeshRenderer r, Transform carrier)
    {
        var mf = r.GetComponent<MeshFilter>();
        var mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null) return new Bounds(r.transform.position, Vector3.zero);

        if (r.isPartOfStaticBatch)
        {
            // Its slice of the scene's batch: vertices already in world space, so the carrier's inverse places it.
            Slices.Add(new Slice
            {
                Carrier = carrier, Mesh = mesh, Materials = r.sharedMaterials, First = r.subMeshStartIndex,
                ToCarrier = carrier.worldToLocalMatrix, Layer = r.gameObject.layer
            });
            return r.bounds;
        }

        var copy = new GameObject(r.name);
        copy.transform.SetParent(carrier, false);
        copy.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
        var cs = carrier.lossyScale;
        var rs = r.transform.lossyScale;
        copy.transform.localScale = new Vector3(rs.x / Mathf.Max(cs.x, 1e-6f), rs.y / Mathf.Max(cs.y, 1e-6f), rs.z / Mathf.Max(cs.z, 1e-6f));
        copy.layer = r.gameObject.layer;
        copy.AddComponent<MeshFilter>().sharedMesh = mesh;
        var cr = copy.AddComponent<MeshRenderer>();
        cr.sharedMaterials = r.sharedMaterials;
        return new Bounds(r.transform.TransformPoint(mesh.bounds.center), Vector3.Scale(mesh.bounds.size, rs));
    }

    /// <summary>Copy every visible mesh under src onto the carrier. Returns their world bounds (empty if none).</summary>
    public static bool AddAll(Transform src, Transform carrier, bool includeHidden, out Bounds bounds, List<Renderer> hide = null)
    {
        bounds = default;
        var any = false;
        foreach (var r in src.GetComponentsInChildren<MeshRenderer>(includeHidden))
        {
            if (!includeHidden && (!r.enabled || !r.gameObject.activeInHierarchy)) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var b = Add(r, carrier);
            if (!any) bounds = b;
            else bounds.Encapsulate(b);
            any = true;
            if (hide != null && r.enabled)
            {
                r.enabled = false;
                hide.Add(r);
            }
        }

        return any;
    }

    /// <summary>Once a frame, after everything carried has moved: draw the batched slices where their carriers are.</summary>
    public static void Draw()
    {
        for (var i = Slices.Count - 1; i >= 0; i--)
        {
            var s = Slices[i];
            if (s.Carrier == null || s.Mesh == null)
            {
                Slices.RemoveAt(i);
                continue;
            }

            if (!s.Carrier.gameObject.activeInHierarchy) continue;
            var m = s.Carrier.localToWorldMatrix * s.ToCarrier;
            for (var k = 0; k < s.Materials.Length; k++)
                if (s.Materials[k] != null)
                    Graphics.DrawMesh(s.Mesh, m, s.Materials[k], s.Layer, null, s.First + k);
        }
    }
}
