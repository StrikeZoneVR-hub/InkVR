using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace InkVR.UI;

/// <summary>
/// The game's hurt borders (ink creeping in round the screen edges when something hits Henry, and the black that
/// closes in at the last hit) are full-screen UI images. On the VR UI panel they'd be a floating rectangle with ink
/// in it; instead each image is hidden from the panel and drawn head-locked, with the flat screen's edges pushed
/// just past the edges of your view, so the ink frames your vision the way it frames the monitor.
/// </summary>
public static class HurtVisor
{
    // Where the flat screen's edges land: the edges of your headset's own view (read from the VR camera), so the ink
    // sits in your periphery the way it sits at a monitor's edges. Past them each image's edge pixels are stretched
    // on out (Extend), so the ink carries on beyond your view instead of stopping at a hard edge.
    // Dev: set HalfYaw/HalfPitch (degrees) to fix the angles instead.
    public static float HalfYaw, HalfPitch;
    private const float Distance = 1f, Extend = 1.6f;

    private sealed class Mirror
    {
        public Image Img;
        public CanvasGroup[] Groups;
        public RectTransform Root;
        public VRPanel Quad;
        public bool Black, Extended;
    }

    private static readonly List<Mirror> Mirrors = new();
    private static HurtBordersController _ctrl;
    private static Transform _visor;
    private static readonly Il2CppStructArray<Vector3> Corners = new(4);

    public static int Showing { get; private set; }

    /// <summary>Called every frame in game (not in the pause theatre) with the VR UI camera.</summary>
    public static void Update(Camera uiCam)
    {
        var ctrl = GameManager.Instance?.m_HurtBordersController;
        if (ctrl == null || uiCam == null)
        {
            Showing = 0;
            return;
        }

        if (ctrl != _ctrl || _visor == null || _visor.parent != uiCam.transform) Build(ctrl, uiCam);

        float tanX, tanY;
        if (HalfYaw > 0 && HalfPitch > 0)
        {
            tanX = Mathf.Tan(HalfYaw * Mathf.Deg2Rad);
            tanY = Mathf.Tan(HalfPitch * Mathf.Deg2Rad);
        }
        else
        {
            // the eye's view, a touch wider for the other eye's offset
            tanY = Mathf.Tan(Mathf.Clamp(uiCam.fieldOfView, 60f, 140f) * 0.5f * Mathf.Deg2Rad) * 1.03f;
            tanX = tanY * Mathf.Clamp(uiCam.aspect, 0.6f, 2f);
        }

        var w = 2f * Distance * tanX;
        var h = 2f * Distance * tanY;
        var showing = 0;
        foreach (var m in Mirrors)
        {
            if (m.Img == null || m.Quad.GameObject == null) continue;
            var cr = m.Img.canvasRenderer;
            if (!cr.cull) cr.cull = true; // off the VR UI panel
            var a = m.Img.isActiveAndEnabled ? m.Img.color.a * cr.GetAlpha() : 0f;
            foreach (var g in m.Groups)
                if (g != null)
                    a *= g.alpha;
            if (a < 0.003f)
            {
                m.Quad.Visible = false;
                continue;
            }

            m.Quad.Visible = true;
            var c = m.Img.color;
            m.Quad.Color = new Color(c.r, c.g, c.b, a);
            if (m.Black)
            {
                // The black covers everything, whatever its rect.
                m.Quad.Transform.localPosition = new Vector3(0, 0, Distance);
                m.Quad.Transform.localRotation = Quaternion.identity;
                m.Quad.SetSize(w * 3f, h * 3f);
            }
            else Place(m, w, h);

            showing++;
        }

        Showing = showing;
    }

    /// <summary>The image's rect on the flat screen, onto the head-locked plane (rotation and flips kept).</summary>
    private static void Place(Mirror m, float w, float h)
    {
        m.Img.rectTransform.GetWorldCorners(Corners);
        var rect = m.Root.rect;
        Vector2 P(int i)
        {
            var l = m.Root.InverseTransformPoint(Corners[i]);
            var u = (l.x - rect.xMin) / rect.width;
            var v = (l.y - rect.yMin) / rect.height;
            return new Vector2((u - 0.5f) * w, (v - 0.5f) * h);
        }

        // corners: 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right (in the image's own frame)
        var bl = P(0);
        var x = P(3) - bl;
        var y = P(1) - bl;
        var centre = bl + (x + y) * 0.5f;
        var angle = Mathf.Atan2(x.y, x.x) * Mathf.Rad2Deg;
        var flip = x.x * y.y - x.y * y.x < 0 ? -1f : 1f;
        var t = m.Quad.Transform;
        var e = m.Extended ? Extend : 1f;
        t.localPosition = new Vector3(centre.x, centre.y, Distance);
        t.localRotation = Quaternion.Euler(0, 0, angle);
        t.localScale = new Vector3(x.magnitude * e, y.magnitude * flip * e, 1f);
    }

    private static void Build(HurtBordersController ctrl, Camera uiCam)
    {
        Clear();
        _ctrl = ctrl;
        _visor = new GameObject("InkVR_HurtVisor").transform;
        _visor.SetParent(uiCam.transform, false);
        _visor.gameObject.layer = VRLayers.UI;

        var black = ctrl.m_BlackImage;
        if (black != null) Add(black, true, 59);
        var borders = ctrl.m_Borders;
        if (borders != null)
            for (var i = 0; i < borders.Count; i++)
                if (borders[i] != null)
                    Add(borders[i], false, 60);
        Plugin.L.LogInfo($"Hurt borders: {Mirrors.Count} images drawn head-locked instead of on the UI panel");
    }

    private static void Add(Image img, bool black, int order)
    {
        var canvas = img.canvas;
        var root = canvas != null ? canvas.rootCanvas.GetComponent<RectTransform>() : null;
        if (root == null) return;
        var groups = new List<CanvasGroup>();
        for (var t = img.transform; t != null; t = t.parent)
        {
            var g = t.GetComponent<CanvasGroup>();
            if (g != null) groups.Add(g);
        }

        var tex = img.mainTexture;
        var quad = new VRPanel("InkVR_Hurt_" + img.name, _visor, tex, Color.clear, order);
        var sprite = img.sprite;
        var whole = true;
        if (sprite != null && tex != null && tex.width > 0)
        {
            // A sprite packed in an atlas: just its own part of the texture.
            var r = sprite.textureRect;
            whole = r.width >= tex.width - 1 && r.height >= tex.height - 1;
            quad.Material.mainTextureScale = new Vector2(r.width / tex.width, r.height / tex.height);
            quad.Material.mainTextureOffset = new Vector2(r.x / tex.width, r.y / tex.height);
        }

        // A whole-texture image is drawn bigger with its UVs widened to match, the texture clamped: inside, it's the
        // image as it was; outside, its edge pixels carry on.
        var extended = whole && tex != null && !black;
        if (extended)
        {
            tex.wrapMode = TextureWrapMode.Clamp;
            quad.Material.mainTextureScale = new Vector2(Extend, Extend);
            quad.Material.mainTextureOffset = new Vector2(-(Extend - 1) / 2, -(Extend - 1) / 2);
        }

        quad.Visible = false;
        Mirrors.Add(new Mirror { Img = img, Groups = groups.ToArray(), Root = root, Quad = quad, Black = black, Extended = extended });
        var rt = img.rectTransform.rect;
        Plugin.L.LogInfo($"  hurt image '{img.name}': {(tex != null ? $"{tex.name} {tex.width}x{tex.height}" : "no texture")}, " +
                         $"rect {rt.width:F0}x{rt.height:F0} of {root.rect.width:F0}x{root.rect.height:F0}, {(extended ? "edges carried on" : black ? "covers the view" : "as is")}");
    }

    /// <summary>Back onto the flat screen (VR stopped, or a new chapter's controller).</summary>
    public static void Clear()
    {
        foreach (var m in Mirrors)
        {
            if (m.Img != null) m.Img.canvasRenderer.cull = false;
            if (m.Quad.GameObject != null) Object.Destroy(m.Quad.GameObject);
        }

        Mirrors.Clear();
        if (_visor != null) Object.Destroy(_visor.gameObject);
        _visor = null;
        _ctrl = null;
        Showing = 0;
    }
}
