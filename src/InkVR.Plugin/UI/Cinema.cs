using System.Collections.Generic;
using InkVR.Rendering;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The theatre the title screen, pause menu and loading screens play in: the game's UI on a big framed
/// screen in a 1930s picture palace (<see cref="Theatre"/>), lit by a flickering projector beam from the
/// booth behind you, with dust drifting through it and the odd film scratch.
/// </summary>
public class Cinema
{
    public readonly VRPanel Screen;
    private readonly VRPanel _grain;
    private readonly Material _beamMat;
    private readonly Transform _root;
    private readonly Theatre _theatre;
    private float _flickerT;
    private float _scratchUntil;
    private float _showT, _house = 1f, _curtainHand = 1f;
    private bool _showFull;
    private readonly Light _bounce, _fill;
    private readonly VRPanel _scratch;
    private readonly List<TMPro.TextMeshPro> _signs = new();
    private readonly Dictionary<string, TMPro.TextMeshPro> _signById = new();

    /// <summary>The theatre's root (it moves around you as you walk: see <see cref="TheatreWalk"/>).</summary>
    public Transform Root => _root;

    public Theatre Theatre => _theatre;

    /// <summary>A sign that was given an id (null until the signs are made).</summary>
    public TMPro.TextMeshPro Sign(string id) => _signById.TryGetValue(id, out var t) && t != null ? t : null;

    public Cinema(Transform origin, Texture screenTexture)
    {
        _root = new GameObject("InkVR_Theatre").transform;
        _root.SetParent(origin, false);
        var w = Theatre.ScreenWidth;
        var h = w * 9f / 16f;
        var screenPos = new Vector3(0, Theatre.ScreenCenterY, Theatre.ScreenZ);

        // Screen (the game's UI) with a slight film grain over it. Depth-tested, so your hands pass in front.
        Screen = new VRPanel("InkVR_CinemaScreen", _root, screenTexture, Color.white, 0);
        Screen.SetSize(w, h);
        Screen.Transform.localPosition = screenPos;
        Screen.ZTest(UnityEngine.Rendering.CompareFunction.LessEqual);
        _grain = new VRPanel("InkVR_CinemaGrain", _root, Textures.Grain, new Color(1, 0.95f, 0.85f, 0.05f), 2);
        _grain.SetSize(w, h);
        _grain.Transform.localPosition = screenPos - Vector3.forward * 0.005f;
        _grain.ZTest(UnityEngine.Rendering.CompareFunction.LessEqual);
        _scratch = new VRPanel("InkVR_CinemaScratch", _root, Scratches(), new Color(0, 0, 0, 0.35f), 3);
        _scratch.SetSize(w, h);
        _scratch.Transform.localPosition = screenPos - Vector3.forward * 0.006f;
        _scratch.ZTest(UnityEngine.Rendering.CompareFunction.LessEqual);
        _scratch.Visible = false;
        Flat("InkVR_CinemaFrame", FrameTexture(), new Vector3(w + 0.5f, h + 0.5f, 1), screenPos + Vector3.forward * 0.02f, Quaternion.identity, -2);

        // The picture palace around it.
        _theatre = new Theatre(_root);
        Refresh();

        // Projector beam from the booth window at the back, over your head to the screen.
        var projector = Theatre.Projector;
        _beamMat = new Material(Shader.Find("UI/Default")) { renderQueue = 3900 };
        _beamMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        var beam = new MeshBuilder();
        var hw = w / 2;
        var hh = h / 2;
        // Nested shells, so the beam's edges fade into the dark instead of ending in a hard line.
        foreach (var shell in new[] { 0.45f, 0.62f, 0.78f, 0.91f, 1.02f })
        {
            var corners = new[]
            {
                screenPos + new Vector3(-hw, -hh, -0.01f) * shell, screenPos + new Vector3(hw, -hh, -0.01f) * shell,
                screenPos + new Vector3(hw, hh, -0.01f) * shell, screenPos + new Vector3(-hw, hh, -0.01f) * shell
            };
            var near = new Color(1f, 0.9f, 0.68f, 0.03f);
            var far = new Color(1f, 0.9f, 0.68f, 0.011f);
            for (var i = 0; i < 4; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % 4];
                beam.Quad(projector, projector, b, a, near, near, far, far);
            }
        }

        var beamGo = new GameObject("InkVR_ProjectorBeam");
        beamGo.layer = VRLayers.UI;
        beamGo.transform.SetParent(_root, false);
        beamGo.AddComponent<MeshFilter>().sharedMesh = beam.ToMesh("InkVR_Beam");
        var beamRenderer = beamGo.AddComponent<MeshRenderer>();
        beamRenderer.sharedMaterial = _beamMat;
        beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Light from the screen onto the audience, and from the house behind (lights Henry's hands; layer 31 only).
        var bounce = _bounce = new GameObject("InkVR_ScreenLight").AddComponent<Light>();
        bounce.transform.SetParent(_root, false);
        bounce.transform.localRotation = Quaternion.LookRotation(new Vector3(0, -0.35f, -1f));
        bounce.type = LightType.Directional;
        bounce.color = new Color(1f, 0.86f, 0.62f);
        bounce.intensity = 1.1f;
        bounce.cullingMask = VRLayers.UIMask;
        bounce.shadows = LightShadows.None;
        var fill = _fill = new GameObject("InkVR_BoothLight").AddComponent<Light>();
        fill.transform.SetParent(_root, false);
        fill.transform.localRotation = Quaternion.LookRotation(new Vector3(0, -0.6f, 1f));
        fill.type = LightType.Directional;
        fill.color = new Color(0.9f, 0.75f, 0.5f);
        fill.intensity = 0.75f;
        fill.cullingMask = VRLayers.UIMask;
        fill.shadows = LightShadows.None;
    }

    /// <summary>Rebuild the signs (a chapter load resets the game's dynamic font atlases and blanks them).</summary>
    public void Refresh()
    {
        foreach (var t in _signs)
            if (t != null)
                Object.Destroy(t.gameObject);
        _signs.Clear();
        _signById.Clear();
        foreach (var spec in _theatre.Signs) MakeSign(spec);
    }

    /// <summary>
    /// The theatre has opened: the show starts. On the title screen the full opening (house lights up, the follow spots
    /// sweep the curtain, the lights go down and the curtain parts); over a paused game the curtain is already open.
    /// </summary>
    public void StartShow(bool full)
    {
        _showFull = full;
        _showT = full ? 0f : 99f;
        _house = full ? 1f : 0.4f;
    }

    /// <summary>Projector flicker, the theatre's lights, drifting dust, the odd film scratch.</summary>
    public void Animate()
    {
        if (_root == null || !_root.gameObject.activeInHierarchy) return;
        var calm = ModConfig.ReduceFlashes.Value;
        var dt = Time.unscaledDeltaTime;
        _flickerT += dt;
        var flicker = calm ? 1f : 0.94f + 0.06f * Mathf.PerlinNoise(_flickerT * 9f, 0.3f);
        Screen.Color = new Color(flicker, flicker, flicker, 1);
        _beamMat.color = new Color(1, 1, 1, flicker);
        _grain.Visible = !calm;
        if (!calm)
        {
            _grain.Material.mainTextureOffset = new Vector2(Random.value, Random.value);
            if (Time.unscaledTime > _scratchUntil + 1.5f && Random.value < dt * 0.5f) _scratchUntil = Time.unscaledTime + Random.Range(0.08f, 0.25f);
            _scratch.Visible = Time.unscaledTime < _scratchUntil;
            if (_scratch.Visible)
                _scratch.Material.mainTextureOffset = new Vector2(Random.value, 0);
        }
        else
        {
            _scratch.Visible = false;
        }

        var head = Core.VRRig.Head;
        var eye = head != null ? _root.InverseTransformPoint(head.position) : new Vector3(0, 1.6f, 0);
        _theatre.Animate(_flickerT, flicker, calm, eye);
        RunShow(dt, flicker, calm);
    }

    // ---------------- the show: the curtain, the house lights, the follow spots, the screen's light ----------------

    private void RunShow(float dt, float flicker, bool calm)
    {
        _showT += dt;
        var t = _showT;
        // the curtain parts from 3 s to 6 s into the opening (and after that, the booth's switch draws it)
        _curtainHand = Mathf.MoveTowards(_curtainHand, TheatreState.CurtainOpen ? 1f : 0f, dt * 0.33f);
        var open = Mathf.Min(Mathf.Clamp01((t - 3f) / 3f), _curtainHand);
        _theatre.SetCurtain(open);

        // house lights: up as the theatre opens, down for the show, up again while you walk about
        var target = t < 1.2f ? 1f : UI.TheatreWalk.WalkMode || TheatreState.HouseUp ? 1f : 0.38f;
        _house = Mathf.MoveTowards(_house, target, dt * (t < 6f ? 0.3f : 0.6f));
        _theatre.SetHouseLights(_house);
        _fill.intensity = 0.75f * (0.55f + 0.45f * _house);

        // the screen's light: as bright as the picture (the projector's flicker, a slow drift as it changes),
        // and none through a closed curtain
        var drift = calm ? 1f : 0.88f + 0.12f * Mathf.PerlinNoise(_flickerT * 0.35f, 0.7f);
        var level = Mathf.SmoothStep(0, 1, open) * flicker * drift;
        _theatre.SetScreenLight(level, new Color(1f, 0.97f, 0.9f));
        _bounce.intensity = 1.1f * (0.35f + 0.65f * level);

        // the follow spots sweep the closed curtain, meet in the middle, part with it and go out
        if (_showFull && t > 0.8f && t < 7.5f)
        {
            var fade = Mathf.Clamp01((t - 0.8f) / 0.6f) * Mathf.Clamp01((7.5f - t) / 1.2f);
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -1f : 1f;
                var sweep = Mathf.Clamp01((t - 0.8f) / 2.4f);
                var x = side * Mathf.Lerp(4.2f, 0.55f, Mathf.SmoothStep(0, 1, sweep));
                x += side * Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - 3.4f) / 3f)) * 3.6f; // out with the curtain
                var y = 2.4f + Mathf.Sin(t * 2.1f + i) * 0.35f * (1 - sweep);
                _theatre.SetSpot(i, new Vector3(x, y, Theatre.ScreenZ), fade * 0.95f, 1.15f);
            }
        }
        else
        {
            if (!TheatreState.SpotManual) _theatre.SetSpot(0, Vector3.zero, 0f, 1f);
            _theatre.SetSpot(1, Vector3.zero, 0f, 1f);
        }
    }

    private void Flat(string name, Texture2D tex, Vector3 size, Vector3 pos, Quaternion rot, int order)
    {
        var p = new VRPanel(name, _root, tex, Color.white, order);
        p.Transform.localScale = size;
        p.Transform.localPosition = pos;
        p.Transform.localRotation = rot;
        p.ZTest(UnityEngine.Rendering.CompareFunction.LessEqual);
    }

    private TMPro.TextMeshPro _toast;

    /// <summary>A line of lettering that floats in front of you for a moment (the caller moves and fades it).</summary>
    public TMPro.TextMeshPro Toast(string text)
    {
        if (_toast == null)
        {
            var go = new GameObject("InkVR_Toast") { layer = VRLayers.UI };
            go.transform.SetParent(_root, false);
            _toast = go.AddComponent<TMPro.TextMeshPro>();
            var font = GameFonts.Title;
            if (font != null) _toast.font = font;
            _toast.alignment = TMPro.TextAlignmentOptions.Center;
            _toast.enableWordWrapping = false;
            _toast.fontSize = 0.9f;
            _toast.rectTransform.sizeDelta = new Vector2(2.4f, 0.3f);
        }

        _toast.gameObject.SetActive(true);
        _toast.text = text;
        _toast.color = new Color(1f, 0.86f, 0.55f, 1f);
        var mat = _toast.fontMaterial;
        mat.renderQueue = 4020;
        mat.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
        return _toast;
    }

    private void MakeSign(Theatre.SignSpec spec)
    {
        var go = new GameObject("InkVR_Sign");
        go.layer = VRLayers.UI;
        var piece = spec.Group != null ? _theatre.Piece(spec.Group) : null;
        go.transform.SetParent(piece != null ? piece : _root, false);
        go.transform.localPosition = piece != null ? spec.Pos - _theatre.PivotOf(spec.Group) : spec.Pos;
        go.transform.localRotation = spec.Rot;
        var tmp = go.AddComponent<TMPro.TextMeshPro>();
        _signs.Add(tmp);
        if (spec.Id != null) _signById[spec.Id] = tmp;
        var font = GameFonts.Title;
        if (font != null) tmp.font = font;
        tmp.text = spec.Text;
        tmp.fontSize = spec.Height * 10f;
        tmp.color = spec.Color;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.rectTransform.sizeDelta = new Vector2(spec.Width, spec.Height * 2);
        var mat = tmp.fontMaterial; // instance material: drawn after the theatre, hidden behind walls like everything else
        mat.renderQueue = 4010;
        mat.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
    }

    // ---------------- painted textures ----------------

    private static readonly Color Ink = new(0.05f, 0.035f, 0.025f, 1f);

    private static Texture2D FrameTexture()
    {
        var c = new InkCanvas(512, 288, new Color(0.13f, 0.09f, 0.055f), 11);
        // Inner opening is transparent (the screen shows through).
        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
        {
            var bx = Mathf.Min(x, c.W - 1 - x);
            var by = Mathf.Min(y, c.H - 1 - y);
            if (bx > 26 && by > 26) c.Px[y * c.W + x] = Color.clear;
        }

        for (var i = 0; i < 2; i++)
        {
            var inset = 4 + i * 20;
            c.Stroke(inset, inset, c.W - inset, inset, 2.2f, Ink);
            c.Stroke(c.W - inset, inset, c.W - inset, c.H - inset, 2.2f, Ink);
            c.Stroke(c.W - inset, c.H - inset, inset, c.H - inset, 2.2f, Ink);
            c.Stroke(inset, c.H - inset, inset, inset, 2.2f, Ink);
        }

        for (var i = 0; i < 160; i++)
        {
            // Wood grain dashes along the frame.
            var horizontal = c.Rand() < 0.5f;
            var x = c.Rand(0, c.W);
            var y = c.Rand(0, c.H);
            if (Mathf.Min(x, c.W - x) > 24 && Mathf.Min(y, c.H - y) > 24) continue;
            if (horizontal) c.Stroke(x, y, x + c.Rand(10, 40), y + c.Rand(-1, 1), 0.9f, Ink, 0.3f, 0.6f);
            else c.Stroke(x, y, x + c.Rand(-1, 1), y + c.Rand(10, 40), 0.9f, Ink, 0.3f, 0.6f);
        }

        c.Grain(0.15f);
        return c.ToTexture("InkVR_Frame", false);
    }

    private static Texture2D Scratches()
    {
        var c = new InkCanvas(256, 64, Color.clear, 4);
        for (var i = 0; i < 5; i++)
        {
            var x = c.Rand(0, c.W);
            c.Stroke(x, 0, x + c.Rand(-3, 3), c.H, c.Rand(0.6f, 1.4f), Color.white, 0.4f, 0.9f);
        }

        return c.ToTexture("InkVR_Scratches");
    }
}

/// <summary>The game's own TextMeshPro fonts, looked up by name once loaded.</summary>
public static class GameFonts
{
    private static TMPro.TMP_FontAsset _title, _body;

    /// <summary>The brush lettering used on the title screen buttons.</summary>
    public static TMPro.TMP_FontAsset Title => _title ??= Find("DK Downward Fall SDF");

    /// <summary>The clean font used for menu descriptions.</summary>
    public static TMPro.TMP_FontAsset Body => _body ??= Find("CaviarDreams_Bold SDF");

    private static TMPro.TMP_FontAsset Find(string name)
    {
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<TMPro.TMP_FontAsset>()))
        {
            var f = o.Cast<TMPro.TMP_FontAsset>();
            if (f.name == name) return f;
        }

        return null;
    }
}
