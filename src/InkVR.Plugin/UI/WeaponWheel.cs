using System.Collections.Generic;
using InkVR.Body;
using InkVR.Core;
using InkVR.Input;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.UI;

/// <summary>
/// The weapon wheel: hold the right stick down and an ink splat opens in front of your hand, its choices lettered
/// round it in the title screen's brush lettering. Tilt the stick (or move your hand) towards one and let go:
///   your weapon (draw it), HANDS (put it away), SEEING TOOL (when the game lets Henry have it), INVENTORY.
/// Putting away and drawing are the same as the shoulder holster; the Seeing Tool and the inventory are the game's own.
/// </summary>
public static class WeaponWheel
{
    private const float HoldToOpen = 0.18f, Size = 0.24f; // seconds, metres across

    private sealed class Slot
    {
        public string Label;
        public System.Action Act;
        public TMPro.TextMeshPro Text;
    }

    private static readonly List<Slot> Slots = new();
    private static Transform _root;
    private static VRPanel _back;
    private static float _heldSince = -1f;
    private static bool _open;
    private static Vector3 _openHand;
    private static int _choice = -1;

    /// <summary>The wheel is open: the right stick picks from it (it doesn't turn you).</summary>
    public static bool IsOpen => _open;

    /// <summary>Dev: force the stick click and a stick direction (null = the controller's own).</summary>
    public static bool? DevClick;
    public static Vector2? DevStick;

    public static void Update()
    {
        var on = ModConfig.WeaponWheel.Value && XrManager.IsRunning && VRRig.InGame && !VRRig.CinemaActive;
        var dom = ControllerBindings.Dom;
        var click = DevClick ?? dom.StickClick;
        if (!on)
        {
            if (_open) Close(false);
            _heldSince = -1f;
            return;
        }

        if (click)
        {
            if (_heldSince < 0) _heldSince = Time.unscaledTime;
            if (!_open && Time.unscaledTime - _heldSince >= HoldToOpen) Open();
        }
        else
        {
            _heldSince = -1f;
            if (_open) Close(true);
            return;
        }

        if (_open) Choose(DevStick ?? dom.Stick);
    }

    // ---------------- opening ----------------

    private static void Open()
    {
        Slots.Clear();
        var weapon = WeaponHandling.WeaponName;
        var holding = GameManager.Instance?.Player?.WeaponGameObject is { activeInHierarchy: true } && !WeaponHandling.Holstered;
        if (weapon != null)
            Slots.Add(new Slot { Label = Pretty(weapon) + (holding ? "\n<size=60%>IN HAND</size>" : ""), Act = () => WeaponHandling.Draw() });
        Slots.Add(new Slot { Label = "HANDS", Act = () => WeaponHandling.PutAway("with the wheel") });
        var player = GameManager.Instance?.Player;
        if (player != null && player.m_CanHaveSeeingTool)
            Slots.Add(new Slot { Label = "SEEING\nTOOL", Act = () => player.UseSeeingTool(!player.isSeeingToolActive) });
        Slots.Add(new Slot { Label = "INVENTORY", Act = InventoryButton.Show });

        Build();
        var head = VRRig.Head;
        _openHand = HandPos();
        var toHead = head != null ? (head.position - _openHand).normalized : Vector3.back;
        _root.position = Vector3.Lerp(_openHand, head != null ? head.position : _openHand, 0.3f) + Vector3.up * (0.05f * VRRig.ActiveScale);
        _root.rotation = Quaternion.LookRotation(-toHead, Vector3.up);
        _root.localScale = Vector3.one * VRRig.ActiveScale;
        Layout();
        _root.gameObject.SetActive(true);
        _open = true;
        _choice = -1;
        XrInput.Haptic(ControllerBindings.Dominant, 0.25f, 0.04f);
        InkPool.InkSound(_root.position, 0.4f);
    }

    private static Vector3 HandPos()
    {
        var c = ControllerBindings.Dom;
        var dev = ControllerBindings.Dominant == Hand.Right ? Hands.DevRight : Hands.DevLeft;
        VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, dev?.rot ?? c.GripRotation, out var p, out _);
        return p;
    }

    private static string Pretty(string weapon)
    {
        var n = weapon.Replace("(Clone)", "").Replace("Weapon_", "").Replace("_", " ").Trim();
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < n.Length; i++)
        {
            if (i > 0 && char.IsUpper(n[i]) && char.IsLower(n[i - 1])) sb.Append(' ');
            sb.Append(n[i]);
        }

        var s = sb.ToString().ToUpperInvariant();
        return s == "PIPE" ? "GENT PIPE" : s;
    }

    // ---------------- choosing ----------------

    private static Vector2 SlotDir(int i) => new(Mathf.Sin(i * 2f * Mathf.PI / Slots.Count), Mathf.Cos(i * 2f * Mathf.PI / Slots.Count));

    private static void Choose(Vector2 stick)
    {
        // the stick if it's tilted, else where your hand has moved since the wheel opened
        var dir = stick;
        if (dir.magnitude < 0.5f)
        {
            var moved = _root.InverseTransformVector(HandPos() - _openHand);
            dir = new Vector2(moved.x, moved.y) / 0.06f;
        }

        var pick = -1;
        if (dir.magnitude >= 0.5f)
        {
            var best = -2f;
            for (var i = 0; i < Slots.Count; i++)
            {
                var d = Vector2.Dot(dir.normalized, SlotDir(i));
                if (d > best)
                {
                    best = d;
                    pick = i;
                }
            }
        }

        if (pick != _choice)
        {
            _choice = pick;
            if (pick >= 0) XrInput.Haptic(ControllerBindings.Dominant, 0.12f, 0.02f);
            Layout();
        }
    }

    private static void Close(bool apply)
    {
        _open = false;
        if (_root != null) _root.gameObject.SetActive(false);
        if (!apply || _choice < 0 || _choice >= Slots.Count) return;
        var s = Slots[_choice];
        Plugin.L.LogInfo($"Weapon wheel: {s.Label.Split('\n')[0]}");
        XrInput.Haptic(ControllerBindings.Dominant, 0.4f, 0.06f);
        s.Act();
    }

    // ---------------- drawing ----------------

    private static void Build()
    {
        if (_root == null)
        {
            _root = new GameObject("InkVR_WeaponWheel").transform;
            Object.DontDestroyOnLoad(_root.gameObject);
            _root.gameObject.layer = VRLayers.UI;
            _back = new VRPanel("InkVR_WheelSplat", _root, SplatTexture(), Color.white, 40);
            _back.SetSize(Size, Size);
        }

        // VR rigs are rebuilt per level: follow the origin's scale
        foreach (var t in _root.GetComponentsInChildren<TMPro.TextMeshPro>(true)) Object.Destroy(t.gameObject);
        for (var i = 0; i < Slots.Count; i++)
        {
            var go = new GameObject("InkVR_WheelLabel" + i);
            go.layer = VRLayers.UI;
            go.transform.SetParent(_root, false);
            var tmp = go.AddComponent<TMPro.TextMeshPro>();
            var font = GameFonts.Title;
            if (font != null) tmp.font = font;
            tmp.text = Slots[i].Label;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(0.16f, 0.06f);
            var mat = tmp.fontMaterial;
            mat.renderQueue = 4045;
            mat.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            Slots[i].Text = tmp;
        }
    }

    private static void Layout()
    {
        for (var i = 0; i < Slots.Count; i++)
        {
            var t = Slots[i].Text;
            if (t == null) continue;
            var picked = i == _choice;
            var d = SlotDir(i) * (Size * 0.3f);
            t.transform.localPosition = new Vector3(d.x, d.y, -0.004f);
            t.fontSize = picked ? 0.36f : 0.28f;
            t.color = picked ? new Color(0.93f, 0.67f, 0.2f) : new Color(0.95f, 0.89f, 0.76f);
        }
    }

    private static Texture2D _splat;

    /// <summary>A round blot of ink with a ragged edge and a few drips, a paper hub in the middle.</summary>
    private static Texture2D SplatTexture()
    {
        if (_splat != null) return _splat;
        const int N = 512;
        var ink = new Color(0.05f, 0.04f, 0.035f, 0.94f);
        var c = new InkCanvas(N, N, Color.clear, 77);
        var lobes = new float[24];
        for (var i = 0; i < lobes.Length; i++) lobes[i] = c.Rand(-0.025f, 0.03f);
        for (var y = 0; y < N; y++)
        for (var x = 0; x < N; x++)
        {
            float u = (x + 0.5f) / N - 0.5f, v = (y + 0.5f) / N - 0.5f;
            var r = Mathf.Sqrt(u * u + v * v);
            var a = (Mathf.Atan2(v, u) / (2f * Mathf.PI) + 0.5f) * lobes.Length;
            var i0 = (int)a % lobes.Length;
            var edge = 0.44f + Mathf.Lerp(lobes[i0], lobes[(i0 + 1) % lobes.Length], a - (int)a);
            var al = Mathf.Clamp01((edge - r) * 160f);
            if (al > 0) c.Px[y * N + x] = new Color(ink.r, ink.g, ink.b, ink.a * al);
        }

        // drips running off the bottom, splashes round the edge
        for (var i = 0; i < 6; i++)
        {
            var x = c.Rand(0.3f, 0.7f) * N;
            var len = c.Rand(0.04f, 0.1f) * N;
            c.Stroke(x, N * 0.08f, x + c.Rand(-2, 2), N * 0.08f - len, c.Rand(6, 11), ink, 0.3f);
            c.Dot(x, N * 0.08f - len, c.Rand(7, 11), ink);
        }

        for (var i = 0; i < 18; i++)
        {
            var ang = c.Rand(0, Mathf.PI * 2f);
            var rr = c.Rand(0.47f, 0.5f) * N;
            c.Dot(N / 2f + Mathf.Cos(ang) * rr, N / 2f + Mathf.Sin(ang) * rr, c.Rand(3, 9), ink);
        }

        // the paper hub
        c.Dot(N / 2f, N / 2f, N * 0.07f, new Color(0.93f, 0.86f, 0.7f, 1f));
        c.Dot(N / 2f, N / 2f, N * 0.025f, ink);
        _splat = c.ToTexture("InkVR_WheelSplat", false);
        return _splat;
    }
}

/// <summary>
/// The inventory button: hold Y and a paper card comes up in your other hand: the current objective (its heading,
/// what to do, the tip) and the items it asks for, the ones you've found inked in full, the rest faint, with the
/// count. Let go of Y and it's put away. A short tap of Y still goes to the game as before.
/// </summary>
public static class InventoryButton
{
    private const float HoldToShow = 0.4f;
    private static float _since = -1f, _autoClose = -1f;
    private static bool _shown;
    private static int _tapFrames;

    /// <summary>The Y the game sees this frame: held back while it might be a long press, a short tap passed on.</summary>
    public static bool FilterY(bool y)
    {
        if (_autoClose > 0 && Time.unscaledTime > _autoClose && !_shown)
        {
            _autoClose = -1f;
            InventoryCard.Close();
        }

        if (!ModConfig.InventoryButton.Value || !VRRig.InGame || VRRig.CinemaActive)
        {
            if (InventoryCard.IsOpen) InventoryCard.Close();
            return y;
        }

        if (y)
        {
            if (_since < 0)
            {
                _since = Time.unscaledTime;
                _shown = false;
            }

            if (!_shown && Time.unscaledTime - _since >= HoldToShow)
            {
                _shown = true;
                InventoryCard.Open();
            }

            return false;
        }

        if (_since >= 0)
        {
            // let go: a short press was a tap of Y for the game; a long one puts the card away
            if (!_shown) _tapFrames = 2;
            else InventoryCard.Close();
            _shown = false;
            _since = -1f;
        }

        if (_tapFrames > 0)
        {
            _tapFrames--;
            return true;
        }

        return false;
    }

    /// <summary>From the wheel (or the dev "inventory" command): the card for a few seconds.</summary>
    public static void Show()
    {
        InventoryCard.Open();
        _autoClose = Time.unscaledTime + 5f;
    }
}
