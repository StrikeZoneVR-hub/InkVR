using UnityEngine;
using UnityEngine.Rendering;

namespace InkVR.UI;

/// <summary>A world-space quad on the VR UI layer showing a texture, drawn on top of the scene.</summary>
public class VRPanel
{
    public readonly GameObject GameObject;
    public readonly Transform Transform;
    private readonly Material _material;

    public VRPanel(string name, Transform parent, Texture texture, Color color, int sortingOrder)
    {
        GameObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        GameObject.name = name;
        Object.Destroy(GameObject.GetComponent<Collider>());
        GameObject.layer = VRLayers.UI;
        Transform = GameObject.transform;
        Transform.SetParent(parent, false);

        _material = new Material(Shader.Find("UI/Default")) { mainTexture = texture, color = color, renderQueue = 4000 + sortingOrder };
        _material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        var r = GameObject.GetComponent<MeshRenderer>();
        r.sharedMaterial = _material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    public Color Color
    {
        get => _material.color;
        set => _material.color = value;
    }

    public bool Visible
    {
        get => GameObject.activeSelf;
        set
        {
            if (GameObject.activeSelf != value) GameObject.SetActive(value);
        }
    }

    /// <summary>Size in the parent's units (metres inside a rig origin).</summary>
    public void SetSize(float width, float height) => Transform.localScale = new Vector3(width, height, 1);
}

public static class VRLayers
{
    /// <summary>Unused by the game; only the mod's VR UI cameras render it.</summary>
    public const int UI = 31;

    public const int UIMask = 1 << UI;
}
