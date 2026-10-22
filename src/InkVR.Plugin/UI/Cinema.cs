using System.Collections.Generic;
using InkVR.Rendering;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The theatre the title screen, pause menu and loading screens play in: a big framed screen in a
/// dark Joey Drew Studios screening room, curtains, worn floorboards and a flickering projector beam
/// from the booth behind you. Everything is painted at runtime in the game's ink style.
/// </summary>
public class Cinema
{
    private const float ScreenWidth = 4.6f, ScreenDistance = 4.0f, ScreenCenterY = 1.75f;

    public readonly VRPanel Screen;
    private readonly VRPanel _grain;
    private readonly Material _beamMat;
    private readonly List<Transform> _motes = new();
    private readonly List<Vector3> _moteVel = new();
    private readonly Transform _root;
    private float _flickerT;
    private float _scratchUntil;
    private readonly VRPanel _scratch;

    public Cinema(Transform origin, Texture screenTexture)
    {
        _root = new GameObject("InkVR_Theatre").transform;
        _root.SetParent(origin, false);
        var h = ScreenWidth * 9f / 16f;
        var screenPos = new Vector3(0, ScreenCenterY, ScreenDistance);

        // Screen (the game's UI) with a slight film grain over it.
        Screen = new VRPanel("InkVR_CinemaScreen", _root, screenTexture, Color.white, 0);
        Screen.SetSize(ScreenWidth, h);
        Screen.Transform.localPosition = screenPos;
        _grain = new VRPanel("InkVR_CinemaGrain", _root, Textures.Grain, new Color(1, 0.95f, 0.85f, 0.05f), 2);
        _grain.SetSize(ScreenWidth, h);
        _grain.Transform.localPosition = screenPos - Vector3.forward * 0.005f;
        _scratch = new VRPanel("InkVR_CinemaScratch", _root, Scratches(), new Color(0, 0, 0, 0.35f), 3);
        _scratch.SetSize(ScreenWidth, h);
        _scratch.Transform.localPosition = screenPos - Vector3.forward * 0.006f;
        _scratch.Visible = false;

        // Frame, curtains, valance with the studio's name, floor.
        Flat("InkVR_CinemaFrame", FrameTexture(), new Vector3(ScreenWidth + 0.5f, h + 0.5f, 1), screenPos + Vector3.forward * 0.02f, Quaternion.identity, -2);
        var curtain = CurtainTexture();
        Flat("InkVR_CurtainL", curtain, new Vector3(2.6f, 4.6f, 1), new Vector3(-(ScreenWidth / 2 + 1.35f), 2.3f, ScreenDistance - 0.25f), Quaternion.Euler(0, -14, 0), -3);
        Flat("InkVR_CurtainR", curtain, new Vector3(2.6f, 4.6f, 1), new Vector3(ScreenWidth / 2 + 1.35f, 2.3f, ScreenDistance - 0.25f), Quaternion.Euler(0, 14, 0), -3);
        Flat("InkVR_Valance", ValanceTexture(), new Vector3(ScreenWidth + 3.4f, 1.0f, 1), new Vector3(0, ScreenCenterY + h / 2 + 0.55f, ScreenDistance - 0.3f), Quaternion.identity, -1);
        Flat("InkVR_Floor", FloorTexture(), new Vector3(14, 14, 1), new Vector3(0, 0, 2), Quaternion.Euler(90, 0, 0), -5);
        Sign("JOEY DREW STUDIOS", new Vector3(0, ScreenCenterY + h / 2 + 0.58f, ScreenDistance - 0.33f), 0.26f, new Color(0.93f, 0.82f, 0.55f));
        Sign("InkVR  -  VR mod made by StrikeZoneVR", new Vector3(0, ScreenCenterY - h / 2 - 0.22f, ScreenDistance - 0.05f), 0.11f, new Color(0.8f, 0.7f, 0.5f, 0.85f));

        // Projector beam from the booth behind and above you, with dust drifting through it.
        var projector = new Vector3(0, 3.1f, -2.6f);
        _beamMat = new Material(Shader.Find("UI/Default")) { renderQueue = 3900 };
        _beamMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        var beam = new MeshBuilder();
        var hw = ScreenWidth / 2;
        var hh = h / 2;
        var corners = new[]
        {
            screenPos + new Vector3(-hw, -hh, -0.01f), screenPos + new Vector3(hw, -hh, -0.01f),
            screenPos + new Vector3(hw, hh, -0.01f), screenPos + new Vector3(-hw, hh, -0.01f)
        };
        var near = new Color(1f, 0.9f, 0.68f, 0.16f);
        var far = new Color(1f, 0.9f, 0.68f, 0.06f);
        for (var i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            beam.Quad(projector, projector, b, a, near, near, far, far);
        }

        var beamGo = new GameObject("InkVR_ProjectorBeam");
        beamGo.layer = VRLayers.UI;
        beamGo.transform.SetParent(_root, false);
        beamGo.AddComponent<MeshFilter>().sharedMesh = beam.ToMesh("InkVR_Beam");
        var beamRenderer = beamGo.AddComponent<MeshRenderer>();
        beamRenderer.sharedMaterial = _beamMat;
        beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Light bouncing off the screen onto the audience (lights Henry's hands; layer 31 only).
        var bounce = new GameObject("InkVR_ScreenLight").AddComponent<Light>();
        bounce.transform.SetParent(_root, false);
        bounce.transform.localRotation = Quaternion.LookRotation(new Vector3(0, -0.35f, -1f));
        bounce.type = LightType.Directional;
        bounce.color = new Color(1f, 0.86f, 0.62f);
        bounce.intensity = 1.1f;
        bounce.cullingMask = VRLayers.UIMask;
        bounce.shadows = LightShadows.None;
        var fill = new GameObject("InkVR_BoothLight").AddComponent<Light>();
        fill.transform.SetParent(_root, false);
        fill.transform.localRotation = Quaternion.LookRotation(new Vector3(0, -0.6f, 1f));
        fill.type = LightType.Directional;
        fill.color = new Color(0.9f, 0.75f, 0.5f);
        fill.intensity = 0.35f;
        fill.cullingMask = VRLayers.UIMask;
        fill.shadows = LightShadows.None;

        var lens = new VRPanel("InkVR_ProjectorLens", _root, Textures.SoftDot, new Color(1f, 0.92f, 0.7f, 0.9f), 5);
        lens.SetSize(0.35f, 0.35f);
        lens.Transform.localPosition = projector;
        lens.Transform.localRotation = Quaternion.LookRotation(projector - screenPos);

        var rng = new System.Random(3);
        for (var i = 0; i < 40; i++)
        {
            var mote = new VRPanel("InkVR_Mote", _root, Textures.SoftDot, new Color(1f, 0.95f, 0.8f, 0.5f), 4);
            var t = (float)rng.NextDouble();
            var p = Vector3.Lerp(projector, screenPos, 0.15f + t * 0.8f);
            p += new Vector3(((float)rng.NextDouble() - 0.5f) * hw * 2 * t, ((float)rng.NextDouble() - 0.5f) * hh * 2 * t, 0);
            mote.Transform.localPosition = p;
            mote.SetSize(0.012f, 0.012f);
            _motes.Add(mote.Transform);
            _moteVel.Add(new Vector3(((float)rng.NextDouble() - 0.5f) * 0.03f, ((float)rng.NextDouble() - 0.4f) * 0.02f, ((float)rng.NextDouble() - 0.5f) * 0.03f));
        }
    }

    /// <summary>Projector flicker, drifting dust, the odd film scratch.</summary>
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
        for (var i = 0; i < _motes.Count; i++)
        {
            var m = _motes[i];
            m.localPosition += _moteVel[i] * dt;
            if (m.localPosition.y > 3.2f || m.localPosition.y < 0.6f) _moteVel[i] = new Vector3(_moteVel[i].x, -_moteVel[i].y, _moteVel[i].z);
            if (head != null) m.rotation = Quaternion.LookRotation(m.position - head.position);
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

    private void Sign(string text, Vector3 pos, float height, Color color)
    {
        var go = new GameObject("InkVR_Sign");
        go.layer = VRLayers.UI;
        go.transform.SetParent(_root, false);
        go.transform.localPosition = pos;
        var tmp = go.AddComponent<TMPro.TextMeshPro>();
        var font = GameFonts.Title;
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = height * 10f;
        tmp.color = color;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.rectTransform.sizeDelta = new Vector2(8, height * 2);
        tmp.fontMaterial.renderQueue = 4010; // instance material: in front of the valance/frame, never the game's own UI
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

    private static Texture2D CurtainTexture()
    {
        var c = new InkCanvas(256, 512, new Color(0.22f, 0.14f, 0.08f), 5);
        for (var x = 0; x < c.W; x++)
        {
            // Vertical folds: shade by a sine, ink line in each fold.
            var s = Mathf.Sin(x / (float)c.W * Mathf.PI * 2 * 6);
            for (var y = 0; y < c.H; y++)
            {
                var shade = 0.55f + 0.45f * (s * 0.5f + 0.5f);
                var p = c.Px[y * c.W + x];
                c.Px[y * c.W + x] = new Color(p.r * shade, p.g * shade, p.b * shade, 1);
            }
        }

        for (var i = 0; i < 6; i++)
        {
            var x = (i + 0.75f) * c.W / 6f;
            c.Stroke(x, 0, x + c.Rand(-4, 4), c.H, 2.2f, Ink, 1.2f, 0.8f);
            c.Hatch(x - 18, 0, 14, c.H, 80, 7, 1.0f, Ink, 0.35f);
        }

        c.Grain(0.12f);
        return c.ToTexture("InkVR_Curtain");
    }

    private static Texture2D ValanceTexture()
    {
        var c = new InkCanvas(512, 96, new Color(0.18f, 0.11f, 0.06f), 9);
        // Scalloped lower edge.
        for (var x = 0; x < c.W; x++)
        {
            var edge = 18 + 12 * Mathf.Abs(Mathf.Sin(x / (float)c.W * Mathf.PI * 10));
            for (var y = 0; y < edge; y++) c.Px[y * c.W + x] = Color.clear;
            c.Dot(x, edge, 1.3f, Ink);
        }

        c.Stroke(0, c.H - 6, c.W, c.H - 6, 2.5f, Ink);
        c.Hatch(0, 30, c.W, 50, 15, 9, 0.9f, Ink, 0.25f);
        c.Grain(0.12f);
        return c.ToTexture("InkVR_Valance");
    }

    private static Texture2D FloorTexture()
    {
        var c = new InkCanvas(512, 512, new Color(0.16f, 0.11f, 0.07f), 21);
        var plank = 512 / 10f;
        for (var i = 0; i <= 10; i++) c.Stroke(i * plank, 0, i * plank, c.H, 1.8f, Ink, 0.8f);
        for (var i = 0; i < 10; i++)
        {
            var y = c.Rand(0, c.H);
            c.Stroke(i * plank, y, (i + 1) * plank, y, 1.4f, Ink, 0.4f);
            for (var g = 0; g < 6; g++)
            {
                var gx = i * plank + c.Rand(6, plank - 6);
                var gy = c.Rand(0, c.H);
                c.Stroke(gx, gy, gx + c.Rand(-2, 2), gy + c.Rand(20, 60), 0.8f, Ink, 0.4f, 0.5f);
            }
        }

        // Fade to darkness away from the middle (the room's edges vanish into the dark).
        for (var y = 0; y < c.H; y++)
        for (var x = 0; x < c.W; x++)
        {
            var d = Vector2.Distance(new Vector2(x, y), new Vector2(256, 300)) / 256f;
            var f = Mathf.Clamp01(1.15f - d);
            var p = c.Px[y * c.W + x];
            c.Px[y * c.W + x] = new Color(p.r * f, p.g * f, p.b * f, 1);
        }

        c.Grain(0.2f);
        return c.ToTexture("InkVR_Floor", false);
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
