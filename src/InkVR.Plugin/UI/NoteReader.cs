using HarmonyLib;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The tapes' notes (the game's audio-log card: "Voice of WALLY FRANKS" and what they say) close up. In the flat game
/// the card sits in the corner of the screen; in VR the whole HUD is a panel 1.6 m away, where the card's small print
/// was a blur. While a note is up it's lifted out of that panel onto a page of its own, half a metre in front of you
/// and a little below, big enough to read, and cut out of the far panel so it isn't there twice. It fades with the
/// game's own card (walk away from the tape and it goes).
/// </summary>
[HarmonyPatch]
public static class NoteReader
{
    private const float Distance = 0.52f, Height = 0.30f, Below = 0.13f, Left = 0.10f, FollowAngle = 40f;

    private static AudioLogModalController _modal;
    private static VRPanel _page;
    private static readonly VRPanel[] Frame = new VRPanel[4];
    private static MeshRenderer _mainRenderer;
    private static float _yaw;
    private static bool _shown;
    private static readonly Vector3[] Corners = new Vector3[4];

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
        _page.Material.mainTextureOffset = rect.position;
        _page.Material.mainTextureScale = rect.size;
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
        foreach (var g in _modal.GetComponentsInChildren<UnityEngine.UI.Graphic>())
        {
            if (!g.enabled || !g.gameObject.activeInHierarchy || g.color.a < 0.01f) continue;
            g.rectTransform.GetWorldCorners(Corners);
            foreach (var c in Corners)
            {
                var v = cam.WorldToViewportPoint(c);
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
