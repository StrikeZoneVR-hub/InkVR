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
        public MenuItemButton NavButton;
        public readonly List<Row> Rows = new();
        public bool Open => Root != null && Root.activeSelf;
        public int Selected;
        public float OpenedAt;
        public bool NavSelected;
    }

    private static readonly Dictionary<IntPtr, Page> Pages = new();
    private const string Credit = "VR MOD MADE BY STRIKEZONEVR";
    private const string SecondColumn = "CONTROLS";

    // ---------------- settings definition ----------------

    private static List<Row> Definition() => new()
    {
        Category("COMFORT"),
        Toggle("TURNING", ModConfig.SnapTurn, "SNAP", "SMOOTH"),
        Choice("SNAP ANGLE", ModConfig.SnapTurnAngle, new[] { 30f, 45f, 60f, 90f }, v => $"{v:0} DEGREES"),
        Choice("SMOOTH TURN SPEED", ModConfig.SmoothTurnSpeed, new[] { 60f, 90f, 120f, 150f, 180f, 240f }, v => $"{v:0} PER SEC"),
        WorldSize(),
        Toggle("PLAY", ModConfig.SeatedMode, "SEATED", "STANDING"),
        Toggle("CALM THEATRE FLICKER", ModConfig.ReduceFlashes, "ON", "OFF"),
        Category("HENRY"),
        Pick("BODY", ModConfig.BodyMode, new[] { "Full", "Arms", "Hands", "None" }, new[] { "FULL BODY", "ARMS & HANDS", "HANDS ONLY", "NONE" }),
        Pick("STYLE", ModConfig.BodyStyle, new[] { "Lifelike", "Toon" }, new[] { "LIFELIKE", "TOON" }),
        Toggle("INK ON HENRY", ModConfig.InkOnHands, "ON", "OFF"),
        Category("CONTROLS"),
        Toggle("DOMINANT HAND", ModConfig.LeftHanded, "LEFT", "RIGHT"),
        Toggle("INTERACT", ModConfig.InteractWithHand, "HAND HOVER", "LOOK AT IT"),
        Toggle("MELEE", ModConfig.PhysicalMelee, "SWING YOUR ARM", "BUTTON ONLY"),
        Toggle("GUN AIM", ModConfig.AimGunsWithHand, "HAND", "HEAD"),
        MotionControls(),
        Toggle("GRAVITY PULL", ModConfig.GravityPull, "ON", "OFF"),
        WheelAndInventory(),
        Choice("HEIGHT ADJUST", ModConfig.HeightOffset, Range(-0.6f, 0.6f, 0.05f), v => Mathf.Abs(v) < 0.001f ? "NONE" : v > 0 ? $"UP {v * 100:0} CM" : $"DOWN {-v * 100:0} CM"),
        Toggle("LASER POINTER", ModConfig.LaserPointer, "ON", "OFF"),
        Category("GRAPHICS"),
        Choice("RENDER SCALE", ModConfig.RenderScale, Range(0.6f, 1.5f, 0.1f), v => $"{v * 100:0}%"),
        Toggle("AUTO RESOLUTION", ModConfig.DynamicResolution, "ON", "OFF"),
        Toggle("HALF-RES AMBIENT OCCLUSION", ModConfig.VRAODownsample, "ON", "OFF"),
        Toggle("LIGHT BEAMS", ModConfig.VolumetricLightVR, "ON", "OFF"),
    };

    /// <summary>The weapon wheel (click the right stick) and the inventory card (hold Y) in one row: both, either, or neither.</summary>
    private static Row WheelAndInventory()
    {
        var labels = new[] { "BOTH", "WHEEL ONLY", "INVENTORY ONLY", "OFF" };
        int Current() => ModConfig.WeaponWheel.Value ? (ModConfig.InventoryButton.Value ? 0 : 1) : (ModConfig.InventoryButton.Value ? 2 : 3);
        return new Row
        {
            Label = "WHEEL & INVENTORY",
            Value = () => labels[Current()],
            Step = dir =>
            {
                var i = (Current() + dir + labels.Length) % labels.Length;
                ModConfig.WeaponWheel.Value = i <= 1;
                ModConfig.InventoryButton.Value = i == 0 || i == 2;
            }
        };
    }

    /// <summary>AUTO (sized to your height) or a fixed world size.</summary>
    private static Row WorldSize() => new()
    {
        Label = "WORLD SIZE",
        Value = () => ModConfig.AutoWorldScale.Value
            ? (Core.VRRig.AutoScale > 0 ? $"AUTO ({2.95f / Core.VRRig.AutoScale * 100f:0}%)" : "AUTO")
            : $"{2.95f / ModConfig.WorldScale.Value * 100f:0}%",
        Step = dir =>
        {
            var i = ModConfig.AutoWorldScale.Value ? 0 : 1 + Nearest(WorldScales, ModConfig.WorldScale.Value);
            i = Mathf.Clamp(i + dir, 0, WorldScales.Length);
            ModConfig.AutoWorldScale.Value = i == 0;
            if (i > 0) ModConfig.WorldScale.Value = WorldScales[i - 1];
        }
    };

    // Game units per metre; shown as world size relative to Henry's eye height (2.95 = 100%).
    private static readonly float[] WorldScales = { 4.21f, 3.93f, 3.69f, 3.47f, 3.28f, 3.11f, 2.95f, 2.81f, 2.68f, 2.57f, 2.46f };

    private static Row Category(string label) => new() { Label = label };

    private static Row Toggle(string label, ConfigEntry<bool> entry, string whenTrue, string whenFalse) => new()
    {
        Label = label,
        Value = () => entry.Value ? whenTrue : whenFalse,
        Step = _ => entry.Value = !entry.Value
    };

    /// <summary>
    /// The hand-motion extras in one row (the page has room for one more): valves, levers and soup by hand,
    /// throwing with your arm, picking up props, two hands on guns and the axe, the shoulder holster. The launcher
    /// has each on its own.
    /// </summary>
    private static Row MotionControls()
    {
        var entries = new[] { ModConfig.PhysicalDoors, ModConfig.PhysicalInteractions, ModConfig.PhysicalThrow, ModConfig.GrabProps, ModConfig.TwoHandedGuns, ModConfig.Holster };
        return new Row
        {
            Label = "MOTION CONTROLS",
            Value = () =>
            {
                var on = 0;
                foreach (var e in entries)
                    if (e.Value)
                        on++;
                return on == entries.Length ? "ALL BY HAND" : on == 0 ? "BUTTONS" : "SOME (SEE LAUNCHER)";
            },
            Step = _ =>
            {
                var all = true;
                foreach (var e in entries) all &= e.Value;
                foreach (var e in entries) e.Value = !all;
            }
        };
    }

    private static Row Pick(string label, ConfigEntry<string> entry, string[] values, string[] names) => new()
    {
        Label = label,
        Value = () => names[Mathf.Max(0, Array.IndexOf(values, entry.Value))],
        Step = dir => entry.Value = values[(Mathf.Max(0, Array.IndexOf(values, entry.Value)) + dir + values.Length) % values.Length]
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
        if (button.m_Arrows != null) button.m_Arrows.SetActive(false);
        page.NavButton = button;
        AddToNavigation(ctrl, page);
    }

    /// <summary>Put VR SETTINGS in the game's own list of Options entries so the thumbstick reaches it.</summary>
    internal static void AddToNavigation(OptionsMenuController ctrl) => AddToNavigation(ctrl, null);

    private static void AddToNavigation(OptionsMenuController ctrl, Page page)
    {
        if (page == null && !Pages.TryGetValue(ctrl.Pointer, out page)) return;
        if (page.NavButton == null) return;
        foreach (var list in new[] { ctrl.m_OptionsMenuItemButtons, ctrl.m_ActiveMenuItemButtons })
        {
            if (list == null || list.Contains(page.NavButton)) continue;
            var at = list.IndexOf(ctrl.m_AdvancedBtn);
            if (at < 0) continue; // the active list belongs to another page right now
            list.Insert(at + 1, page.NavButton);
        }

        if (ctrl.m_CurrentMenuItem == OptionsMenuController.MenuItem.OPTIONS && ctrl.m_ActiveMenuItemButtons != null)
            ctrl.m_IndexMax = ctrl.m_ActiveMenuItemButtons.Count - 1;
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
        // The list outgrew one column: CONTROLS onwards goes in a second one (the stick still runs down the first, then the second).
        var nav2 = Object.Instantiate(nav.gameObject, nav.parent).transform;
        nav2.name = "InkVR_Navigation2";
        var column = nav;

        foreach (var row in Definition())
        {
            if (row.Value == null)
            {
                if (row.Label == SecondColumn) column = nav2;
                var cat = Object.Instantiate(ctrl.m_MenuItemCategoryPrefab, column);
                cat.gameObject.SetActive(true);
                cat.Init(column, row.Label);
                SetText(cat.transform.Find("CategoryLbl"), row.Label);
                continue;
            }

            // The game's option row, set up by InkVR (MenuItemOption.Init expects localisation keys).
            var item = Object.Instantiate(ctrl.m_MenuItemOptionPrefab, column);
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

        // Two columns side by side, right of the page title, both hung from the same top line and clear of the credit.
        foreach (var (col, x) in new[] { (nav, -137f), (nav2, 568f) })
        {
            col.localScale = new Vector3(0.92f, 0.92f, 1f);
            var rt = col.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.sizeDelta = new Vector2(620f, 800f);
                rt.anchoredPosition = new Vector2(x, 22f);
            }

            var group = col.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (group != null)
            {
                group.childAlignment = TextAnchor.UpperCenter;
                group.spacing += 4f; // a little air under each heading
            }
        }

        // the game's ring of brush strokes frames a single list; with two it would cut through them
        var images = root.transform.Find("Images");
        if (images != null)
            for (var i = 0; i < images.childCount; i++)
                if (images.GetChild(i) != title)
                    images.GetChild(i).gameObject.SetActive(false);
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
        rt.sizeDelta = new Vector2(1400, 60);
        rt.anchoredPosition = new Vector2(0, -462);
        var text = go.GetComponent<TextMeshProUGUI>();
        SetText(go.transform, Credit);
        text.fontSize = Mathf.Max(24f, text.fontSize * 0.27f);
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
    }

    private static void Change(Page page, Row row, int dir)
    {
        row.Step(dir);
        if (row.Item != null) Layout(row.Item, row.Label + ":", row.Value());
        var index = page.Rows.IndexOf(row);
        if (index >= 0 && index != page.Selected)
        {
            page.Selected = index;
            Highlight(page);
        }
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
        _waitRelease = true;
        _holdGameInput = true;
        if (vr)
        {
            page.OpenedAt = Time.unscaledTime;
            page.Selected = 0;
            foreach (var row in page.Rows)
                if (row.Item != null)
                    Layout(row.Item, row.Label + ":", row.Value());
            Highlight(page);
        }
    }

    // ---------------- thumbstick / keyboard navigation ----------------

    private static float _repeatAt;
    private static int _heldDir;
    private static bool _waitRelease = true;

    /// <summary>True while the VR page is open: the game's own Options input is paused (see the Update patch).</summary>
    internal static bool AnyOpen(OptionsMenuController ctrl) => Pages.TryGetValue(ctrl.Pointer, out var p) && p.Open;

    private static bool _prevUp, _prevDown, _prevA, _prevB;

    // After InkVR acts on a press, the game must not act on the same press too.
    private static bool _holdGameInput;

    internal static bool NavSelectedFor(OptionsMenuController ctrl) => Pages.TryGetValue(ctrl.Pointer, out var p) && p.NavSelected;

    /// <summary>
    /// Runs before the game's Options Update. The game's list only cycles SETTINGS/ADVANCED, so moving
    /// down from ADVANCED (or up from SETTINGS) lands on VR SETTINGS here instead; while it is selected
    /// InkVR handles the stick and buttons. Returns false to skip the game's update for this frame.
    /// </summary>
    internal static bool BeforeOptionsUpdate(OptionsMenuController ctrl)
    {
        if (!Pages.TryGetValue(ctrl.Pointer, out var page)) return true;
        if (page.Open) return false;

        var pad = Input.VirtualGamepad.Effective;
        bool up = pad.LeftStick.y > 0.6f || UnityEngine.Input.GetKey(KeyCode.UpArrow);
        bool down = pad.LeftStick.y < -0.6f || UnityEngine.Input.GetKey(KeyCode.DownArrow);
        bool a = pad.A || UnityEngine.Input.GetKey(KeyCode.Return);
        bool b = pad.B || UnityEngine.Input.GetKey(KeyCode.Escape);
        var upEdge = up && !_prevUp;
        var downEdge = down && !_prevDown;
        var aEdge = a && !_prevA;
        var bEdge = b && !_prevB;
        (_prevUp, _prevDown, _prevA, _prevB) = (up, down, a, b);
        if (_holdGameInput)
        {
            if (up || down || a || b || Mathf.Abs(pad.LeftStick.x) > 0.5f) return false;
            _holdGameInput = false;
        }

        var onOptions = page.NavButton != null && ctrl.m_CurrentMenuItem == OptionsMenuController.MenuItem.OPTIONS &&
                        ctrl.m_OptionsMenu != null && ctrl.m_OptionsMenu.activeInHierarchy;
        if (!onOptions)
        {
            if (page.NavSelected) SelectNav(page, false);
            return true;
        }

        var advanced = ctrl.m_OptionsMenuItemButtons.IndexOf(ctrl.m_AdvancedBtn);
        if (!page.NavSelected)
        {
            if ((downEdge && ctrl.m_SelectedIndex == advanced) || (upEdge && ctrl.m_SelectedIndex == 0))
            {
                SelectNav(page, true);
                _holdGameInput = true;
                return false;
            }

            return true;
        }

        if (upEdge || downEdge)
        {
            // Hand the highlight back to the game's list (just the highlight: nothing is activated).
            SelectNav(page, false);
            var index = upEdge ? advanced : 0;
            ctrl.m_SelectedIndex = index;
            var target = ctrl.m_OptionsMenuItemButtons[index];
            if (target != null && target.m_Arrows != null) target.m_Arrows.SetActive(true);
            _holdGameInput = true;
        }
        else if (aEdge)
        {
            SelectNav(page, false);
            Show(page, true);
        }
        else if (bEdge)
        {
            SelectNav(page, false);
            return true; // the game's own back
        }

        return false;
    }

    private static void SelectNav(Page page, bool on)
    {
        page.NavSelected = on;
        var ctrl = page.Controller;
        if (on)
        {
            ctrl.DeselectAllCurrentMenuItems();
            foreach (var b in ctrl.m_OptionsMenuItemButtons)
                if (b != null && b != page.NavButton)
                {
                    b.Deselect();
                    if (b.m_Arrows != null) b.m_Arrows.SetActive(false);
                }
        }

        var arrows = page.NavButton.m_Arrows ?? FindChildStartingWith(page.NavButton.transform, "Arrows")?.gameObject;
        if (arrows != null) arrows.SetActive(on);
        if (on) Input.XrInput.Haptic(Input.ControllerBindings.OffHand, 0.12f, 0.015f);
    }

    /// <summary>Every frame: move through the open VR page with the stick (arrow keys), change with left/right or A, back with B.</summary>
    public static void Update()
    {
        foreach (var page in Pages.Values)
        {
            if (!page.Open || page.Rows.Count == 0) continue;
            var pad = Input.VirtualGamepad.Effective;
            var up = pad.LeftStick.y > 0.6f || UnityEngine.Input.GetKey(KeyCode.UpArrow);
            var down = pad.LeftStick.y < -0.6f || UnityEngine.Input.GetKey(KeyCode.DownArrow);
            var left = pad.LeftStick.x < -0.6f || UnityEngine.Input.GetKey(KeyCode.LeftArrow);
            var right = pad.LeftStick.x > 0.6f || UnityEngine.Input.GetKey(KeyCode.RightArrow);
            var confirm = pad.A || UnityEngine.Input.GetKey(KeyCode.Return) || UnityEngine.Input.GetKey(KeyCode.Space);
            var back = pad.B || UnityEngine.Input.GetKey(KeyCode.Escape) || UnityEngine.Input.GetKey(KeyCode.Backspace);

            // The press that opened the page has to be let go first.
            if (_waitRelease || Time.unscaledTime - page.OpenedAt < 0.2f)
            {
                _waitRelease = up || down || left || right || confirm || back;
                return;
            }

            if (back)
            {
                Show(page, false);
                return;
            }

            var dir = up ? 1 : down ? 2 : left ? 3 : right ? 4 : confirm ? 5 : 0;
            if (dir == 0)
            {
                _heldDir = 0;
                return;
            }

            var repeat = dir == _heldDir;
            if (repeat && (dir == 5 || Time.unscaledTime < _repeatAt)) return; // A never auto-repeats
            _repeatAt = Time.unscaledTime + (repeat ? 0.14f : 0.42f);
            _heldDir = dir;

            switch (dir)
            {
                case 1:
                    page.Selected = (page.Selected - 1 + page.Rows.Count) % page.Rows.Count;
                    Highlight(page);
                    break;
                case 2:
                    page.Selected = (page.Selected + 1) % page.Rows.Count;
                    Highlight(page);
                    break;
                case 3:
                    Change(page, page.Rows[page.Selected], -1);
                    break;
                default:
                    Change(page, page.Rows[page.Selected], +1);
                    break;
            }

            Input.XrInput.Haptic(Input.ControllerBindings.OffHand, 0.12f, 0.015f);
            return;
        }
    }

    /// <summary>The selected row shows its arrows and reads brighter, like the game's own option pages.</summary>
    private static void Highlight(Page page)
    {
        for (var i = 0; i < page.Rows.Count; i++)
        {
            var item = page.Rows[i].Item;
            if (item == null) continue;
            var on = i == page.Selected;
            SetArrows(item.transform.Find("Left"), item.transform.Find("Right"), on);
            foreach (var name in new[] { "Label", "Status" })
            {
                var t = item.transform.Find(name)?.GetComponent<TextMeshProUGUI>();
                if (t != null) t.alpha = on ? 1f : 0.6f;
            }
        }
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

    // Opening the Options list rebuilds its navigation: keep VR SETTINGS in it.
    [HarmonyPostfix, HarmonyPatch(nameof(OptionsMenuController.ShowOptionsMenu))]
    private static void AfterShowOptions(OptionsMenuController __instance) => VRSettingsMenu.AddToNavigation(__instance);

    // VR SETTINGS in the Options list, and the VR page's own navigation while it is open.
    [HarmonyPrefix, HarmonyPatch("Update")]
    private static bool BeforeUpdate(OptionsMenuController __instance) => VRSettingsMenu.BeforeOptionsUpdate(__instance);
}
