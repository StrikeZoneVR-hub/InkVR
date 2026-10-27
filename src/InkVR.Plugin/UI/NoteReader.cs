using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The tapes' notes (the game's audio-log card: "Voice of WALLY FRANKS" and what they say) close up. In the flat game
/// the card sits in the corner of the screen; in VR the whole HUD is a panel 1.6 m away, where the card's small print
/// was a blur. While a note is up it's lifted out of that panel onto a page of its own, half a metre in front of you
/// and a little below, big enough to read, and cut out of the far panel so it isn't there twice. It fades with the
/// game's own card (walk away from the tape and it goes).
/// The game's UI is drawn at 4K (see UICapture), so the card has about 1100 x 1450 pixels: more than the page shows.
/// </summary>
[HarmonyPatch]
public static class NoteReader
{
    private const float Distance = 0.50f, Height = 0.42f, Below = 0.12f, Left = 0.10f, FollowAngle = 40f;

    private static AudioLogModalController _modal;
    private static VRPanel _page;
    private static readonly VRPanel[] Frame = new VRPanel[4];
    private static MeshRenderer _mainRenderer;
    private static float _yaw;
    private static bool _shown;
    // A game-side array: GetWorldCorners fills the array it's given, and a plain C# array is copied across and back
    // without the answers (every corner read as 0,0,0: the whole card a speck mid-screen, so no close-up).
    private static readonly Il2CppStructArray<Vector3> Corners = new(4);
    private static RenderTexture _sharp;

    /// <summary>Dev: the note's place on the game's screen (0..1), when one is up.</summary>
    public static Rect? DevRect;

    [HarmonyPostfix, HarmonyPatch(typeof(AudioLogModalController), nameof(AudioLogModalController.PlayIn))]
    private static void OnPlayIn(AudioLogModalController __instance) => _modal = __instance;

    [HarmonyPostfix, HarmonyPatch(typeof(AudioLogModalController), nameof(AudioLogModalController.Show))]
    private static void OnShow(AudioLogModalController __instance) => _modal = __instance;

    /// <summary>After the HUD panel is placed each frame (the rig's origin, your head, the HUD panel).</summary>
    public static void Update(Transform origin, Transform head, VRPanel hud)
    {
        if (!ModConfig.CloseUpNotes.Value || hud == null || origin == null || head == null || !Visible(out var rect, out var alpha))
        {
            Hide(hud);
            return;
        }

        DevRect = rect;
        if (_page == null || _page.GameObject == null)
        {
            _page = new VRPanel("InkVR_NoteCloseUp", origin, UICapture.Texture, Color.white, 6);
            for (var i = 0; i < 4; i++) Frame[i] = null;
        }

        if (_page.Transform.parent != origin) _page.Transform.SetParent(origin, false);
        if (CopySharp(rect, out var crop))
        {
            _page.Material.mainTexture = _sharp;
            _page.Material.mainTextureOffset = crop.position;
            _page.Material.mainTextureScale = crop.size;
        }
        else
        {
            _page.Material.mainTexture = UICapture.Texture;
            _page.Material.mainTextureOffset = rect.position;
            _page.Material.mainTextureScale = rect.size;
        }

        var aspect = rect.width * UICapture.Width / Mathf.Max(1f, rect.height * UICapture.Height);
        _page.SetSize(Height * aspect, Height);
        _page.Color = new Color(1, 1, 1, alpha);
        _page.Visible = true;

        // Where it sits: in front of you, a little below and to the left, turned to face you. It stays put while you
        // look round it, and comes back in front once you've turned well away.
        var headLocal = origin.InverseTransformPoint(head.position);
        var fwd = origin.InverseTransformDirection(head.forward);
        var yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        if (!_shown) _yaw = yaw;
        else if (Mathf.Abs(Mathf.DeltaAngle(_yaw, yaw)) > FollowAngle) _yaw = Mathf.MoveTowardsAngle(_yaw, yaw, 150f * Time.unscaledDeltaTime);
        var turn = Quaternion.Euler(0, _yaw, 0);
        var pos = headLocal + turn * new Vector3(-Left, -Below, Distance);
        _page.Transform.localPosition = pos;
        _page.Transform.localRotation = Quaternion.LookRotation(pos - headLocal, Vector3.up);
        _shown = true;

        CutOut(hud, rect);
    }

    /// <summary>The card is up (fading in, readable, or fading out): where it is on the game's screen (0..1).</summary>
    private static bool Visible(out Rect rect, out float alpha)
    {
        rect = default;
        alpha = 0;
        if (_modal == null || _modal.gameObject == null || !_modal.gameObject.activeInHierarchy) return false;
        var group = _modal.m_CanvasGroup;
        alpha = group != null ? group.alpha : 1f;
        if (alpha < 0.02f) return false;
        var canvas = _modal.GetComponentInParent<Canvas>();
        var cam = canvas != null ? canvas.worldCamera : null;
        if (cam == null) return false;

        // The card is everything drawn in the modal (its paper, the tape recorder, the words): their screen bounds.
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var any = false;
        var vp = cam.projectionMatrix * cam.worldToCameraMatrix;
        foreach (var g in _modal.GetComponentsInChildren<UnityEngine.UI.Graphic>())
        {
            if (!g.enabled || !g.gameObject.activeInHierarchy || g.color.a < 0.01f) continue;
            g.rectTransform.GetWorldCorners(Corners);
            foreach (var c in Corners)
            {
                var v = Viewport(vp, c);
                min = Vector2.Min(min, v);
                max = Vector2.Max(max, v);
            }

            any = true;
        }

        if (!any) return false;
        min = Vector2.Max(min, Vector2.zero);
        max = Vector2.Min(max, Vector2.one);
        // a hair of margin so the paper's torn edge isn't clipped
        var pad = new Vector2(4f / UICapture.Width, 4f / UICapture.Height);
        min = Vector2.Max(min - pad, Vector2.zero);
        max = Vector2.Min(max + pad, Vector2.one);
        if (max.x - min.x < 0.02f || max.y - min.y < 0.02f || max.x - min.x > 0.8f) return false;
        rect = new Rect(min, max - min);
        return true;
    }

    /// <summary>
    /// The card copied out of the UI texture into a small texture of its own (full detail: no mipmap blur), with its
    /// own mipmaps leaning sharp, since the page is seen a little smaller than the card's pixels.
    /// <paramref name="crop"/> is where the card is in it.
    /// </summary>
    private static bool CopySharp(Rect card, out Rect crop)
    {
        crop = default;
        if (SystemInfo.copyTextureSupport == UnityEngine.Rendering.CopyTextureSupport.None) return false;
        var src = UICapture.Texture;
        var x = Mathf.Clamp(Mathf.FloorToInt(card.xMin * src.width), 0, src.width - 1);
        var y = Mathf.Clamp(Mathf.FloorToInt(card.yMin * src.height), 0, src.height - 1);
        var w = Mathf.Min(src.width - x, Mathf.CeilToInt(card.xMax * src.width) - x);
        var h = Mathf.Min(src.height - y, Mathf.CeilToInt(card.yMax * src.height) - y);
        if (w < 8 || h < 8) return false;
        if (_sharp == null || _sharp.width < w || _sharp.height < h)
        {
            if (_sharp != null) Object.Destroy(_sharp);
            // the same kind of texture as the UI's (CopyTexture needs it), a little room to spare
            _sharp = new RenderTexture((w + 255) / 256 * 256, (h + 255) / 256 * 256, 0, RenderTextureFormat.ARGB32)
            {
                name = "InkVR_NoteSharp", useMipMap = true, autoGenerateMips = false, antiAliasing = 1,
                filterMode = FilterMode.Trilinear, anisoLevel = 8, mipMapBias = -0.5f,
            };
            _sharp.Create();
            Object.DontDestroyOnLoad(_sharp);
        }

        Graphics.CopyTexture(src, 0, 0, x, y, w, h, _sharp, 0, 0, 0, 0);
        _sharp.GenerateMips();
        crop = new Rect(0, 0, w / (float)_sharp.width, h / (float)_sharp.height);
        DevCrop = crop;
        return true;
    }

    /// <summary>Dev: why no note is shown (each of the card's pieces and where it is on the screen).</summary>
    private static string DevWhy()
    {
        if (!ModConfig.CloseUpNotes.Value) return "TAPE NOTES off";
        if (_modal == null) return "no card hooked";
        if (_modal.gameObject == null || !_modal.gameObject.activeInHierarchy) return "card inactive";
        var canvas = _modal.GetComponentInParent<Canvas>();
        var cam = canvas != null ? canvas.worldCamera : null;
        var sb = new System.Text.StringBuilder($"alpha={(_modal.m_CanvasGroup != null ? _modal.m_CanvasGroup.alpha : -1):F2} canvas={(canvas != null ? canvas.name : "-")} cam={(cam != null ? cam.name : "-")}");
        if (cam == null) return sb.ToString();
        foreach (var g in _modal.GetComponentsInChildren<UnityEngine.UI.Graphic>())
        {
            g.rectTransform.GetWorldCorners(Corners);
            var vp = cam.projectionMatrix * cam.worldToCameraMatrix;
            var a = Viewport(vp, Corners[0]);
            var b = Viewport(vp, Corners[2]);
            sb.Append($"\n  {g.name} on={g.enabled && g.gameObject.activeInHierarchy} a={g.color.a:F2} ({a.x:F2},{a.y:F2})-({b.x:F2},{b.y:F2})");
        }

        return sb.ToString();
    }

    /// <summary>Dev: where the card is in the sharp texture.</summary>
    public static Rect? DevCrop;

    /// <summary>Dev: the card in the UI texture and in the page's own copy, as PNGs.</summary>
    public static void DevSave(string dir)
    {
        if (DevRect is not { } r) { Plugin.L.LogWarning("noteshot: no note up: " + DevWhy()); return; }
        System.IO.Directory.CreateDirectory(dir);
        Save(UICapture.Texture, r, System.IO.Path.Combine(dir, "note_hud.png"));
        if (_sharp != null && DevCrop is { } c) Save(_sharp, c, System.IO.Path.Combine(dir, "note_sharp.png"));
        Plugin.L.LogInfo("noteshot -> " + dir);
    }

    private static void Save(RenderTexture rt, Rect r, string file)
    {
        var x = Mathf.FloorToInt(r.x * rt.width);
        var y = Mathf.FloorToInt(r.y * rt.height);
        var w = Mathf.Min(rt.width - x, Mathf.CeilToInt(r.width * rt.width));
        var h = Mathf.Min(rt.height - y, Mathf.CeilToInt(r.height * rt.height));
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(x, y, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = active;
        System.IO.File.WriteAllBytes(file, ImageConversion.EncodeToPNG(tex));
        Object.Destroy(tex);
    }

    /// <summary>
    /// A point's place on the UI camera's screen (0..1), from the camera's own matrices (the UI camera draws into a
    /// texture while VR runs, and this doesn't depend on how Unity treats such a camera).
    /// </summary>
    private static Vector2 Viewport(Matrix4x4 viewProj, Vector3 world)
    {
        var clip = viewProj * new Vector4(world.x, world.y, world.z, 1f);
        var w = Mathf.Abs(clip.w) > 1e-6f ? clip.w : 1e-6f;
        return new Vector2(clip.x / w * 0.5f + 0.5f, clip.y / w * 0.5f + 0.5f);
    }

    /// <summary>The far HUD panel without the card: four strips round it (the panel's own quad is switched off meanwhile).</summary>
    private static void CutOut(VRPanel hud, Rect hole)
    {
        _mainRenderer ??= hud.GameObject.GetComponent<MeshRenderer>();
        if (_mainRenderer == null) return;
        _mainRenderer.enabled = false;
        // left, right (full height), bottom and top (between them)
        Strip(0, hud, new Rect(0, 0, hole.xMin, 1));
        Strip(1, hud, new Rect(hole.xMax, 0, 1 - hole.xMax, 1));
        Strip(2, hud, new Rect(hole.xMin, 0, hole.width, hole.yMin));
        Strip(3, hud, new Rect(hole.xMin, hole.yMax, hole.width, 1 - hole.yMax));
    }

    private static void Strip(int i, VRPanel hud, Rect r)
    {
        if (Frame[i] == null || Frame[i].GameObject == null)
        {
            Frame[i] = new VRPanel("InkVR_UIPanel_Strip" + i, hud.Transform, UICapture.Texture, Color.white, 0);
        }

        var p = Frame[i];
        p.Material.color = hud.Material.color;
        if (r.width <= 0.0005f || r.height <= 0.0005f)
        {
            p.Visible = false;
            return;
        }

        p.Visible = true;
        p.Material.mainTextureOffset = r.position;
        p.Material.mainTextureScale = r.size;
        // a child of the HUD quad (scaled to the panel), so its own units are fractions of the panel
        p.Transform.localPosition = new Vector3(r.center.x - 0.5f, r.center.y - 0.5f, 0f);
        p.Transform.localRotation = Quaternion.identity;
        p.Transform.localScale = new Vector3(r.width, r.height, 1f);
    }

    private static void Hide(VRPanel hud)
    {
        DevRect = null;
        DevCrop = null;
        _shown = false;
        if (_page != null && _page.GameObject != null) _page.Visible = false;
        for (var i = 0; i < 4; i++)
            if (Frame[i] != null && Frame[i].GameObject != null)
                Frame[i].Visible = false;
        if (hud != null && hud.GameObject != null)
        {
            _mainRenderer ??= hud.GameObject.GetComponent<MeshRenderer>();
            if (_mainRenderer != null && !_mainRenderer.enabled) _mainRenderer.enabled = true;
        }
    }

    /// <summary>The player rig was rebuilt (a new level): the old panels went with it.</summary>
    public static void Reset()
    {
        _page = null;
        _mainRenderer = null;
        for (var i = 0; i < 4; i++) Frame[i] = null;
        _shown = false;
    }
}
