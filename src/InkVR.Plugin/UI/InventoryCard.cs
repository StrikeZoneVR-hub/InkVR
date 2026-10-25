using System.Collections.Generic;
using InkVR.Body;
using InkVR.Core;
using InkVR.Input;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.UI;

/// <summary>
/// The inventory card: a scrap of studio paper held up in your other hand. It shows the game's current objective
/// (its heading in the title screen's brush lettering, what to do, the tip) and the items it asks for, the game's
/// own pictures of them: the ones you've found inked in full, the rest faint, with the count.
/// </summary>
public static class InventoryCard
{
    private const float W = 0.24f, H = 0.17f; // metres

    private static Transform _root;
    private static VRPanel _paper;
    private static TMPro.TextMeshPro _header, _body, _tip, _count;
    private static readonly List<VRPanel> Icons = new();

    public static bool IsOpen => _root != null && _root.gameObject.activeSelf;

    public static void Open()
    {
        Build();
        Fill();
        Follow();
        _root.gameObject.SetActive(true);
        // the game's lettering, once it's loaded (it may not be the first time the card is built)
        var title = GameFonts.Title;
        foreach (var t in new[] { _header, _body, _tip, _count })
        {
            if (title != null && t.font != title) t.font = title;
            OnTop(t);
            t.ForceMeshUpdate();
        }
        Plugin.L.LogInfo($"Inventory card: '{_header.text}' / '{_body.text}' / '{_tip.text}' / '{_count.text}', {Icons.Count} items, fonts {_header.font?.name ?? "none"} + {_body.font?.name ?? "none"}");
        XrInput.Haptic(ControllerBindings.OffHand, 0.25f, 0.04f);
        InkPool.InkSound(_root.position, 0.3f);
    }

    public static void Close()
    {
        if (_root != null) _root.gameObject.SetActive(false);
    }

    /// <summary>Every frame while it's up: it stays in your hand, turned to face you.</summary>
    public static void Update()
    {
        if (!IsOpen) return;
        if (!VRRig.InGame || VRRig.CinemaActive)
        {
            Close();
            return;
        }

        Follow();
    }

    private static void Follow()
    {
        var hand = ControllerBindings.OffHand;
        var dev = hand == Hand.Right ? Hands.DevRight : Hands.DevLeft;
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, dev?.rot ?? c.GripRotation, out var p, out _);
        var k = VRRig.ActiveScale;
        var head = VRRig.Head;
        var toHead = head != null ? (head.position - p).normalized : Vector3.back;
        _root.position = p + Vector3.up * (0.1f * k) + toHead * (0.04f * k);
        _root.rotation = Quaternion.LookRotation(-toHead, Vector3.up);
        _root.localScale = Vector3.one * k;
    }

    // ---------------- what's on it ----------------

    private static string Tr(I2.Loc.LocalizedString s)
    {
        var term = s.mTerm;
        if (string.IsNullOrEmpty(term)) return "";
        try
        {
            var t = I2.Loc.LocalizationManager.GetTranslation(term);
            return string.IsNullOrEmpty(t) ? "" : t;
        }
        catch
        {
            return "";
        }
    }

    private static void Fill()
    {
        var o = GameManager.Instance?.m_CurrentObjective;
        var header = o != null ? Tr(o.Header) : "";
        var body = o != null ? Tr(o.Objective) : "";
        var tip = o != null ? Tr(o.Tip) : "";
        _header.text = string.IsNullOrEmpty(header) ? "OBJECTIVE" : header.ToUpperInvariant();
        _body.text = string.IsNullOrEmpty(body) ? "Nothing to do just now. Explore." : body;
        _tip.text = tip;

        foreach (var i in Icons) Object.Destroy(i.GameObject);
        Icons.Clear();
        _count.text = "";
        if (o == null) return;

        // the items it asks for, in a row along the bottom
        var sprites = new List<(Sprite s, bool got)>();
        if (o.Items != null)
            for (var i = 0; i < o.Items.Count; i++)
                sprites.Add((o.Items[i], o.Collected != null && i < o.Collected.Count && o.Collected[i]));
        if (sprites.Count == 0 && o.Item != null) sprites.Add((o.Item, true));
        var n = sprites.Count;
        var size = Mathf.Min(0.034f, (W - 0.03f) / Mathf.Max(1, n) - 0.004f);
        for (var i = 0; i < n; i++)
        {
            var (sp, got) = sprites[i];
            if (sp == null || sp.texture == null) continue;
            var icon = new VRPanel("InkVR_CardItem" + i, _root, sp.texture, got ? Color.white : new Color(1, 1, 1, 0.28f), 52);
            var r = sp.textureRect;
            icon.Material.mainTextureScale = new Vector2(r.width / sp.texture.width, r.height / sp.texture.height);
            icon.Material.mainTextureOffset = new Vector2(r.x / sp.texture.width, r.y / sp.texture.height);
            var aspect = r.width / Mathf.Max(1f, r.height);
            icon.SetSize(size * Mathf.Min(1.4f, aspect), size);
            icon.Transform.localPosition = new Vector3(-((n - 1) * (size + 0.004f)) / 2f + i * (size + 0.004f), -H / 2f + 0.03f, -0.002f);
            Icons.Add(icon);
        }

        if (o.ItemCounter > 0) _count.text = $"x {o.ItemCounter}";
        else if (n > 1)
        {
            var got = 0;
            foreach (var (_, g) in sprites)
                if (g)
                    got++;
            _count.text = $"{got} / {n}";
        }
    }

    // ---------------- the card ----------------

    private static void Build()
    {
        if (_root != null) return;
        _root = new GameObject("InkVR_InventoryCard").transform;
        Object.DontDestroyOnLoad(_root.gameObject);
        _root.gameObject.layer = VRLayers.UI;
        _paper = new VRPanel("InkVR_CardPaper", _root, PaperTexture(), Color.white, 50);
        _paper.SetSize(W, H);
        _header = Text("Header", GameFonts.Title, 0.016f, new Vector3(0, H / 2f - 0.022f, -0.002f), new Vector2(W - 0.02f, 0.03f));
        _body = Text("Body", GameFonts.Body ?? GameFonts.Title, 0.0125f, new Vector3(0, 0.006f, -0.002f), new Vector2(W - 0.03f, 0.06f));
        _body.enableWordWrapping = true;
        _tip = Text("Tip", GameFonts.Body ?? GameFonts.Title, 0.0085f, new Vector3(0, -0.032f, -0.002f), new Vector2(W - 0.03f, 0.02f));
        _tip.enableWordWrapping = true;
        _tip.color = new Color(0.36f, 0.25f, 0.15f);
        _count = Text("Count", GameFonts.Title, 0.012f, new Vector3(W / 2f - 0.03f, -H / 2f + 0.012f, -0.002f), new Vector2(0.05f, 0.02f));
        _root.gameObject.SetActive(false);
    }

    private static TMPro.TextMeshPro Text(string name, TMPro.TMP_FontAsset font, float size, Vector3 pos, Vector2 box)
    {
        var go = new GameObject("InkVR_Card" + name);
        go.layer = VRLayers.UI;
        go.transform.SetParent(_root, false);
        go.transform.localPosition = pos;
        // drawn at the wheel's lettering size and scaled down: very small font sizes don't draw
        go.transform.localScale = Vector3.one * 0.4f;
        var t = go.AddComponent<TMPro.TextMeshPro>();
        if (font != null) t.font = font;
        t.fontSize = size * 10f / 0.4f;
        t.color = new Color(0.08f, 0.06f, 0.05f);
        t.alignment = TMPro.TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.rectTransform.sizeDelta = box / 0.4f;
        OnTop(t);
        return t;
    }

    /// <summary>Drawn over the paper, in front of everything. (Changing the font swaps the material, so again after that.)</summary>
    private static void OnTop(TMPro.TextMeshPro t)
    {
        var mat = t.fontMaterial;
        mat.renderQueue = 4055;
        mat.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
    }

    /// <summary>A torn scrap of the studio's cream paper: inked border, a coffee ring, grain, an ink spatter.</summary>
    private static Texture2D PaperTexture()
    {
        const int w = 512, h = 362;
        var ink = new Color(0.07f, 0.05f, 0.04f, 1f);
        var c = new InkCanvas(w, h, new Color(0.93f, 0.86f, 0.7f, 1f), 41);
        // torn, uneven edges
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var e = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
            var ragged = 3f + 2.5f * Mathf.PerlinNoise(x * 0.08f, y * 0.08f);
            if (e < ragged) c.Px[y * w + x] = Color.clear;
            else if (e < ragged + 6) c.Px[y * w + x] = Color.Lerp(c.Px[y * w + x], new Color(0.62f, 0.48f, 0.3f, 1f), 0.5f);
        }

        var inset = 16;
        c.Stroke(inset, inset, w - inset, inset, 2.4f, ink);
        c.Stroke(w - inset, inset, w - inset, h - inset, 2.4f, ink);
        c.Stroke(w - inset, h - inset, inset, h - inset, 2.4f, ink);
        c.Stroke(inset, h - inset, inset, inset, 2.4f, ink);
        c.Stroke(inset + 30, h - 74, w - inset - 30, h - 74, 1.4f, ink, 0.4f, 0.7f); // under the heading
        // a coffee ring and a splash of ink in the corner
        for (var a = 0f; a < Mathf.PI * 2f; a += 0.05f)
            c.Dot(w - 80 + Mathf.Cos(a) * 38, 70 + Mathf.Sin(a) * 38, 2.2f, new Color(0.55f, 0.38f, 0.2f, 1f), 0.25f);
        c.InkBlot(40, h - 40, 14, ink);
        c.Grain(0.08f);
        return c.ToTexture("InkVR_CardPaper", false);
    }
}
