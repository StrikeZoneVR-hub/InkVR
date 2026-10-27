using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The game draws all of its UI with cameras under "[UI MANAGER]" (screen-space canvases).
/// In VR those cameras render into one RenderTexture instead, which VR panels then display.
/// </summary>
public static class UICapture
{
    // 4K rather than 1080p: the game's UI scales with the screen, so its text and the tapes' notes are drawn with twice
    // the detail each way (the notes are read close up, at about 1100 x 1450 pixels).
    public const int Width = 3840;
    public const int Height = 2160;
    private const string UIManagerName = "[UI MANAGER]";

    private static RenderTexture _rt;
    private static float _nextScan;

    public static RenderTexture Texture
    {
        get
        {
            if (_rt == null)
            {
                // Mipmapped: the HUD panel is seen smaller than the texture, which without mips sparkles and shimmers.
                _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "InkVR_UI", useMipMap = true, autoGenerateMips = true, antiAliasing = 1 };
                _rt.filterMode = FilterMode.Trilinear;
                _rt.anisoLevel = 4;
                _rt.Create();
                Object.DontDestroyOnLoad(_rt);
            }

            return _rt;
        }
    }

    /// <summary>Redirect any UI camera (new menus spawn their own) into the UI texture.</summary>
    public static void Update()
    {
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 0.25f;

        foreach (var cam in Camera.allCameras)
        {
            if (cam == null || cam.targetTexture == Texture || !IsUnderUIManager(cam.transform)) continue;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.targetTexture = Texture;
            if (cam.name == "UICamera")
            {
                // The first UI camera clears the texture to transparent; later ones draw on top.
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0, 0, 0, 0);
            }

            Plugin.L.LogInfo($"UI camera '{cam.name}' now renders to the VR UI texture");
        }
    }

    private static bool IsUnderUIManager(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.parent == null)
                return t.name == UIManagerName;
        return false;
    }
}
