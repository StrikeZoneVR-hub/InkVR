using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.UI;

/// <summary>
/// Adds a "VR SETTINGS" page to the game's own Options menu (title screen and pause menu), built from
/// the game's own option/category prefabs so it looks and behaves exactly like the other option pages.
/// </summary>
public static class VRSettingsMenu
{
    private sealed class Row
    {
        public string Label;
        public Func<string> Value;
        public Action<int> Step;
        public MenuItemOption Item;
    }

    private sealed class Page
    {
        public OptionsMenuController Controller;
        public GameObject Root;
        public readonly List<Row> Rows = new();
        public bool Open => Root != null && Root.activeSelf;
    }

    private static readonly Dictionary<IntPtr, Page> Pages = new();
    private const string Credit = "VR MOD MADE BY STRIKEZONEVR";

    // ---------------- settings definition ----------------

    private static List<Row> Definition() => new()
    {
        Category("COMFORT"),
        Toggle("TURNING", ModConfig.SnapTurn, "SNAP", "SMOOTH"),
        Choice("SNAP ANGLE", ModConfig.SnapTurnAngle, new[] { 30f, 45f, 60f, 90f }, v => $"{v:0} DEGREES"),
        Choice("SMOOTH TURN SPEED", ModConfig.SmoothTurnSpeed, new[] { 60f, 90f, 120f, 150f, 180f, 240f }, v => $"{v:0} PER SEC"),
        Toggle("CALM THEATRE FLICKER", ModConfig.ReduceFlashes, "ON", "OFF"),
        Category("CONTROLS"),
        Toggle("DOMINANT HAND", ModConfig.LeftHanded, "LEFT", "RIGHT"),
        Toggle("INTERACT AIM", ModConfig.InteractWithHand, "HAND", "HEAD"),
        Choice("HEIGHT ADJUST", ModConfig.HeightOffset, Range(-0.6f, 0.6f, 0.05f), v => Mathf.Abs(v) < 0.001f ? "NONE" : v > 0 ? $"UP {v * 100:0} CM" : $"DOWN {-v * 100:0} CM"),
        Category("HENRY"),
        Toggle("HANDS", ModConfig.ShowHands, "ON", "OFF"),
        Toggle("ARMS & SLEEVES", ModConfig.ShowArms, "ON", "OFF"),
        Category("GRAPHICS"),
        Choice("RENDER SCALE", ModConfig.RenderScale, Range(0.6f, 1.5f, 0.1f), v => $"{v * 100:0}%"),
        Toggle("HALF-RES AMBIENT OCCLUSION", ModConfig.VRAODownsample, "ON", "OFF"),
        Toggle("MOTION BLUR", ModConfig.VRDisableMotionBlur, "OFF", "ON"),
        Toggle("DEPTH OF FIELD", ModConfig.VRDisableDepthOfField, "OFF", "ON"),
    };

    private static Row Category(string label) => new() { Label = label };

    private static Row Toggle(string label, ConfigEntry<bool> entry, string whenTrue, string whenFalse) => new()
    {
        Label = label,
        Value = () => entry.Value ? whenTrue : whenFalse,
        Step = _ => entry.Value = !entry.Value
    };

    private static Row Choice(string label, ConfigEntry<float> entry, float[] values, Func<float, string> format) => new()
    {
        Label = label,
        Value = () => format(entry.Value),
        Step = dir =>
        {
            var i = Nearest(values, entry.Value);
            entry.Value = values[Mathf.Clamp(i + dir, 0, values.Length - 1)];
        }
    };

    private static float[] Range(float min, float max, float step)
    {
        var list = new List<float>();
        for (var v = min; v <= max + step * 0.5f; v += step) list.Add((float)Math.Round(v, 3));
        return list.ToArray();
    }

    private static int Nearest(float[] values, float v)
    {
        var best = 0;
        for (var i = 1; i < values.Length; i++)
            if (Mathf.Abs(values[i] - v) < Mathf.Abs(values[best] - v))
                best = i;
        return best;
    }

    // ---------------- building ----------------

    internal static void Inject(OptionsMenuController ctrl)
    {
        if (ctrl == null || Pages.ContainsKey(ctrl.Pointer)) return;
        try
        {
            var page = new Page { Controller = ctrl };
            AddNavButton(ctrl, page); // the page itself is built on first open, when the canvas is live
            Pages[ctrl.Pointer] = page;
            Plugin.L.LogInfo("VR SETTINGS added to the Options menu");
        }
        catch (Exception e)
        {
            Plugin.L.LogError("Could not add VR settings to the Options menu: " + e);
        }
    }

    private static void AddNavButton(OptionsMenuController ctrl, Page page)
    {
        var template = ctrl.m_AdvancedBtn;
        var go = Object.Instantiate(template.gameObject, template.transform.parent);
        go.name = "InkVR_VRSettingsBtn";
        go.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
        var button = go.GetComponent<MenuItemButton>();
        button.Init(MenuItemButtonDataVO.Create("VR SETTINGS", (Il2CppSystem.Action)new Action(() => Show(page, true))));
        SetText(go.transform.Find("Label"), "VR SETTINGS");
        var baseButton = go.GetComponent<TMG.UI.Controls.BaseUIButton>();
        if (baseButton != null) baseButton.add_OnClick(Handler(() => Show(page, true)));
        var arrows = FindChildStartingWith(go.transform, "Arrows");
        if (arrows != null) arrows.gameObject.SetActive(false);
    }

    private static void BuildPage(OptionsMenuController ctrl, Page page)
    {
        var template = ctrl.m_SettingsMenu;
        var root = Object.Instantiate(template, template.transform.parent);
        root.name = "InkVR_VRMenu";
        root.SetActive(true); // the option rows measure their text while building; hidden again below
        page.Root = root;

        var title = root.transform.Find("Images/TitleLabel");
        SetText(title, "VR");

        var nav = root.transform.Find("Navigation");
        for (var i = nav.childCount - 1; i >= 0; i--) Object.DestroyImmediate(nav.GetChild(i).gameObject);

        foreach (var row in Definition())
        {
            if (row.Value == null)
            {
                var cat = Object.Instantiate(ctrl.m_MenuItemCategoryPrefab, nav);
                cat.gameObject.SetActive(true);
                cat.Init(nav, row.Label);
                SetText(cat.transform.Find("CategoryLbl"), row.Label);
                continue;
            }

            // The game's option row, set up by InkVR (MenuItemOption.Init expects localisation keys).
            var item = Object.Instantiate(ctrl.m_MenuItemOptionPrefab, nav);
            item.gameObject.SetActive(true);
            item.enabled = false;
            row.Item = item;
            var r = row;
            var left = item.transform.Find("Left");
            var right = item.transform.Find("Right");
            SetArrows(left, right, false);
            Layout(item, row.Label + ":", row.Value());
            var rowButton = item.GetComponent<TMG.UI.Controls.BaseUIButton>();
            if (rowButton != null)
            {
                rowButton.add_OnClick(Handler(() => Change(page, r, +1)));
                rowButton.add_OnEnter(Handler(() => SetArrows(left, right, true)));
                rowButton.add_OnExit(Handler(() => SetArrows(left, right, false)));
            }

            left?.GetComponent<TMG.UI.Controls.BaseUIButton>()?.add_OnClick(Handler(() => Change(page, r, -1)));
            right?.GetComponent<TMG.UI.Controls.BaseUIButton>()?.add_OnClick(Handler(() => Change(page, r, +1)));
            page.Rows.Add(row);
        }

        AddCredit(root.transform, title);
        root.SetActive(false);
    }

    /// <summary>"VR MOD MADE BY STRIKEZONEVR" along the bottom of the page, in the page title's own lettering.</summary>
    private static void AddCredit(Transform root, Transform titleTemplate)
    {
        if (titleTemplate == null) return;
        var go = Object.Instantiate(titleTemplate.gameObject, root);
        go.name = "InkVR_Credit";
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1400, 70);
        rt.anchoredPosition = new Vector2(0, -430);
        var text = go.GetComponent<TextMeshProUGUI>();
        SetText(go.transform, Credit);
        text.fontSize = Mathf.Max(28f, text.fontSize * 0.38f);
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
    }

    private static void Change(Page page, Row row, int dir)
    {
        row.Step(dir);
        if (row.Item != null) Layout(row.Item, row.Label + ":", row.Value());
        Input.XrInput.Haptic(Input.ControllerBindings.Dominant, 0.2f, 0.02f);
    }

    private static void Show(Page page, bool vr)
    {
        if (vr && page.Root == null)
        {
            try
            {
                BuildPage(page.Controller, page);
            }
            catch (Exception e)
            {
                Plugin.L.LogError("Could not build the VR settings page: " + e);
                if (page.Root != null) Object.Destroy(page.Root);
                page.Root = null;
                page.Rows.Clear();
                return;
            }
        }

        if (page.Root == null) return;
        if (page.Root.activeSelf == vr) return;
        Plugin.L.LogInfo(vr ? "VR settings opened" : "VR settings closed");
        page.Controller.m_OptionsMenu.SetActive(!vr);
        page.Root.SetActive(vr);
        if (vr)
            foreach (var row in page.Rows)
                if (row.Item != null)
                    Layout(row.Item, row.Label + ":", row.Value());
    }

    /// <summary>Back out of the VR page to the Options list instead of leaving Options entirely.</summary>
    internal static bool HandleBack(OptionsMenuController ctrl)
    {
        if (!Pages.TryGetValue(ctrl.Pointer, out var page) || !page.Open) return false;
        Show(page, false);
        return true;
    }

    internal static void Forget(OptionsMenuController ctrl) => Pages.Remove(ctrl.Pointer);

    // ---------------- helpers ----------------

    /// <summary>Centre "LABEL: STATUS" on the row and put the arrows just outside it, like the game does.</summary>
    private static void Layout(MenuItemOption item, string label, string status)
    {
        var l = item.transform.Find("Label");
        var st = item.transform.Find("Status");
        SetText(l, label);
        SetText(st, status);
        var lt = l?.GetComponent<TextMeshProUGUI>();
        var stt = st?.GetComponent<TextMeshProUGUI>();
        if (lt == null || stt == null) return;
        const float pad = 14f;
        var lw = lt.GetPreferredValues(label).x;
        var sw = stt.GetPreferredValues(status).x;
        var total = lw + pad + sw;
        Place(l, -total / 2 + lw / 2, lw);
        Place(st, total / 2 - sw / 2, sw);
        var leftArrow = item.transform.Find("Left");
        var rightArrow = item.transform.Find("Right");
        if (leftArrow != null) Place(leftArrow, -total / 2 - 30, -1);
        if (rightArrow != null) Place(rightArrow, total / 2 + 30, -1);
    }

    private static void Place(Transform t, float x, float width)
    {
        var rt = t.Cast<RectTransform>();
        rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
        if (width > 0) rt.sizeDelta = new Vector2(width + 4, rt.sizeDelta.y);
    }

    private static void SetArrows(Transform left, Transform right, bool on)
    {
        left?.gameObject.SetActive(on);
        right?.gameObject.SetActive(on);
    }

    private static Il2CppSystem.EventHandler Handler(Action action) =>
        DelegateSupport.ConvertDelegate<Il2CppSystem.EventHandler>(new Action<Il2CppSystem.Object, Il2CppSystem.EventArgs>((_, _) => action()));

    /// <summary>Set a label's text and stop the game's localisation from overwriting it.</summary>
    private static void SetText(Transform t, string value)
    {
        if (t == null) return;
        var loc = t.GetComponent<I2.Loc.Localize>();
        if (loc != null) loc.enabled = false;
        var tmp = t.GetComponent<TextMeshProUGUI>();
        if (tmp != null) tmp.text = value;
    }

    private static Transform FindChildStartingWith(Transform t, string prefix)
    {
        for (var i = 0; i < t.childCount; i++)
            if (t.GetChild(i).name.StartsWith(prefix, StringComparison.Ordinal))
                return t.GetChild(i);
        return null;
    }
}

[HarmonyPatch(typeof(OptionsMenuController))]
internal static class OptionsMenuPatches
{
    [HarmonyPostfix, HarmonyPatch(nameof(OptionsMenuController.InitController))]
    private static void AfterInit(OptionsMenuController __instance) => VRSettingsMenu.Inject(__instance);

    [HarmonyPrefix, HarmonyPatch(nameof(OptionsMenuController.GetBackFromCurrentMenuItem))]
    private static bool Back(OptionsMenuController __instance) => !VRSettingsMenu.HandleBack(__instance);

    [HarmonyPrefix, HarmonyPatch(nameof(OptionsMenuController.OnDisposed))]
    private static void Disposed(OptionsMenuController __instance) => VRSettingsMenu.Forget(__instance);
}
