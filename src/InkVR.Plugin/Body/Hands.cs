using System.Collections.Generic;
using System.Text.RegularExpressions;
using InkVR.Core;
using InkVR.Input;
using InkVR.Rendering;
using InkVR.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InkVR.Body;

/// <summary>
/// Henry's hands and arms, following the controllers, plus the game's own held items/weapons
/// moved into the dominant hand.
/// </summary>
public static class Hands
{
    private static Transform _root;
    private static HandModel _left, _right;
    private static HenryBody _toonBody, _studioBody;
    private static readonly Dictionary<string, Material> Materials = new();
    private static int _chapter = -1;
    private static bool _usingFallbackShader;
    private static float _nextShaderCheck;

    // Held items (the game's HandContainer/Hand mount).
    private static Transform _handContainer, _gameHand;
    private static Vector3 _handRestPos;
    private static Quaternion _handRestRot;

    /// <summary>The paintable parts of one hand and arm (empty until the hands are built).</summary>
    public static List<StainPart> StainParts(Hand hand) => (hand == Hand.Left ? _left : _right)?.Stains;

    /// <summary>The full body being shown (null for arms or hands only).</summary>
    public static HenryBody Body { get; private set; }

    public static Transform WristOf(Hand hand) => (hand == Hand.Left ? _left : _right)?.Wrist;

    public static Vector3 PalmPosition(Hand hand) => (hand == Hand.Left ? _left : _right)?.WristPosition ?? Vector3.zero;

    /// <summary>World bounds around one hand and forearm.</summary>
    public static Bounds Bounds(Hand hand)
    {
        var parts = StainParts(hand);
        if (parts == null || parts.Count == 0) return default;
        var b = parts[0].R.bounds;
        foreach (var p in parts)
            if (p.R != null && p.R.gameObject.activeInHierarchy)
                b.Encapsulate(p.R.bounds);
        return b;
    }

    /// <summary>Dev/testing: how far the dev-posed hands curl (0 open, 1 a fist).</summary>
    public static float DevCurl = 0.3f;

    /// <summary>Dev/testing: tracking-space grip poses used instead of the controllers.</summary>
    public static (Vector3 pos, Quaternion rot)? DevLeft, DevRight;

    public static void Update()
    {
        if (!XrManager.IsRunning || !ModConfig.HandsShown)
        {
            Show(false);
            return;
        }

        Ensure();
        if (_left != null && _left.Lifelike != ModConfig.Lifelike) Rebuild();
        UpdateChapterInk();
        if (_usingFallbackShader && Time.unscaledTime > _nextShaderCheck) TryUseGameShader();

        var scale = VRRig.ActiveScale;
        var layer = VRRig.CinemaActive || !VRRig.InGame ? VRLayers.UI : 0;
        PoseBoth(scale, layer, Time.unscaledDeltaTime);
    }

    /// <summary>Re-pose with the newest controller poses just before rendering (no animation step).</summary>
    public static void LatePose()
    {
        if (_left == null || _right == null || !XrManager.IsRunning || !ModConfig.HandsShown) return;
        PoseBoth(VRRig.ActiveScale, VRRig.CinemaActive || !VRRig.InGame ? VRLayers.UI : 0, 0f);
    }

    private static void PoseBoth(float scale, int layer, float dt)
    {
        if (_root == null) return; // the game's reset destroyed Henry this frame; he's rebuilt on the next update
        PoseBody(scale, layer, dt);
        var holding = HoldingWeapon();
        Pose(_left, Hand.Left, DevLeft, scale, layer, holding && ControllerBindings.Dominant == Hand.Left, dt);
        Pose(_right, Hand.Right, DevRight, scale, layer, holding && ControllerBindings.Dominant == Hand.Right, dt);
        AttachHeldItem();
    }

    private static void Pose(HandModel model, Hand hand, (Vector3 pos, Quaternion rot)? dev, float scale, int layer, bool fist, float dt)
    {
        var c = XrInput.Get(hand);
        var valid = dev.HasValue || (XrInput.Ready && c.PoseValid);
        model.SetVisible(valid, valid && ModConfig.ArmsShown);
        if (!valid) return;

        var gripPos = dev?.pos ?? c.GripPosition;
        var gripRot = dev?.rot ?? c.GripRotation;
        VRRig.TrackingToWorld(gripPos, gripRot, out var wp, out var wr);
        model.Root.SetPositionAndRotation(wp, wr);
        model.Root.localScale = Vector3.one * scale;
        model.SetLayer(layer);

        var index = c.TriggerTouch ? Mathf.Lerp(0.3f, 1f, c.Trigger) : 0.05f;
        var grip = Mathf.Lerp(0.15f, 1f, c.Grip);
        var thumb = c.ThumbTouch ? 0.85f : 0.15f;
        if (fist) index = grip = thumb = 1f;
        if (dev.HasValue)
        {
            index = DevCurl * 0.85f;
            grip = DevCurl;
            thumb = DevCurl;
        }

        model.Animate(index, grip, thumb, dt);

        if (Body != null)
        {
            // Full body: the arms hang from its shoulders.
            model.SolveArm(hand == Hand.Left ? Body.ShoulderLeft : Body.ShoulderRight, hand == Hand.Left ? Body.ElbowHintLeft : Body.ElbowHintRight, scale);
        }
        else if (ModConfig.ArmsShown)
        {
            // Shoulder estimated from the head: down, out to the side, a little back; elbow hangs down/out.
            var side = hand == Hand.Left ? -1f : 1f;
            var headPos = VRRig.HeadLocalPosition - Vector3.up * VRRig.HeightLift;
            var fwd = VRRig.HeadLocalRotation * Vector3.forward;
            var yaw = Quaternion.Euler(0, Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, 0);
            var shoulder = headPos + yaw * new Vector3(0.17f * side, -0.24f, -0.07f);
            var hint = shoulder + yaw * new Vector3(0.25f * side, -0.45f, -0.25f);
            VRRig.TrackingToWorld(shoulder, Quaternion.identity, out var sw, out _);
            VRRig.TrackingToWorld(hint, Quaternion.identity, out var hw, out _);
            model.SolveArm(sw, hw, scale);
        }
    }

    private static void Show(bool on)
    {
        _left?.SetVisible(on, on);
        _right?.SetVisible(on, on);
        if (on) return;
        _toonBody?.SetVisible(false);
        _studioBody?.SetVisible(false);
        Body = null;
    }

    // ---------------- the full body ----------------

    private static void PoseBody(float scale, int layer, float dt)
    {
        // (cutscenes and scripted moments too: the body stands under your view and stays yours; lying on the floor,
        // HenryBody.Pose has it step aside until you're up)
        var want = !ModConfig.FullBody ? (BodyStyle?)null : ModConfig.Lifelike ? BodyStyle.Lifelike : BodyStyle.Toon;
        var head = VRRig.Head;
        if (want == null || head == null)
        {
            _toonBody?.SetVisible(false);
            _studioBody?.SetVisible(false);
            Body = null;
            return;
        }

        if (want == BodyStyle.Toon) _toonBody ??= new HenryBody(BodyStyle.Toon, _root, Mat);
        else _studioBody ??= new HenryBody(BodyStyle.Lifelike, _root, Mat);
        Body = want == BodyStyle.Toon ? _toonBody : _studioBody;
        (want == BodyStyle.Toon ? _studioBody : _toonBody)?.SetVisible(false);
        Body.SetVisible(true);
        Body.SetLayer(layer);

        var bodyScale = Mathf.Clamp(VRRig.StandingEyeHeight / HenryBody.EyeHeight, 0.75f, 1.35f);
        Body.Pose(head.position, head.rotation, scale * bodyScale, HandWorld(Hand.Left, DevLeft), HandWorld(Hand.Right, DevRight), GroundAt, dt);
    }

    private static Vector3? HandWorld(Hand hand, (Vector3 pos, Quaternion rot)? dev)
    {
        var c = XrInput.Get(hand);
        if (!dev.HasValue && !(XrInput.Ready && c.PoseValid)) return null;
        VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, Quaternion.identity, out var p, out _);
        return p;
    }

    /// <summary>The ground under a point: the theatre floor, or the level's floor by a short ray down (stairs, ramps).</summary>
    internal static float GroundAt(Vector3 above)
    {
        var origin = VRRig.ActiveOrigin;
        var floor = origin != null ? origin.position.y : above.y - 1.6f;
        if (VRRig.CinemaActive || !VRRig.InGame) return floor;
        var scale = VRRig.ActiveScale;
        var player = GameManager.Instance?.Player;
        var mask = Physics.DefaultRaycastLayers & ~VRLayers.UIMask;
        if (player != null) mask &= ~(1 << player.gameObject.layer);
        var from = new Vector3(above.x, Mathf.Max(above.y, floor + 0.6f * scale), above.z);
        return Physics.Raycast(from, Vector3.down, out var hit, (from.y - floor) + 0.8f * scale, mask, QueryTriggerInteraction.Ignore) ? hit.point.y : floor;
    }

    // ---------------- building ----------------

    private static void Ensure()
    {
        if (_root != null) return;
        if (_left != null || _toonBody != null || _studioBody != null)
        {
            // The game's reset (quitting to the main menu) destroyed everything kept between scenes, Henry included:
            // let go of the old pieces and build him again from scratch.
            foreach (var model in new[] { _left, _right })
                if (model != null)
                    InkStains.Forget(model.Stains);
            foreach (var body in new[] { _toonBody, _studioBody })
                if (body != null)
                    InkStains.Forget(body.Stains);
            _toonBody = _studioBody = null;
            Body = null;
            _handContainer = null;
            _gameHand = null;
            InkStains.PurgeDestroyed();
            Plugin.L.LogInfo("Henry was cleared away with the game's reset: building him again");
        }

        var go = new GameObject("InkVR_Henry");
        Object.DontDestroyOnLoad(go);
        _root = go.transform;
        BuildMaterials(InkLevel());
        _left = new HandModel(true, _root, Mat, ModConfig.Lifelike);
        _right = new HandModel(false, _root, Mat, ModConfig.Lifelike);
        Plugin.L.LogInfo($"Henry's hands built ({(_usingFallbackShader ? "fallback shader until a chapter loads" : "game shader")})");
    }

    /// <summary>STYLE changed: rebuild both hands in the other style (their ink stains go with them).</summary>
    private static void Rebuild()
    {
        foreach (var model in new[] { _left, _right })
        {
            InkStains.Forget(model.Stains);
            model.Destroy();
        }

        _left = new HandModel(true, _root, Mat, ModConfig.Lifelike);
        _right = new HandModel(false, _root, Mat, ModConfig.Lifelike);
        Plugin.L.LogInfo($"Henry's hands rebuilt ({(ModConfig.Lifelike ? "lifelike" : "toon")})");
    }

    private static Material Mat(string key)
    {
        if (!Materials.ContainsKey(key)) SetMaterial(key, BodyTexture(key, InkLevel()));
        return Materials[key];
    }

    /// <summary>The body's clothes, painted when a body first needs them (and again as the chapter's ink grows).</summary>
    private static Texture2D BodyTexture(string key, float ink) => key switch
    {
        "toonShirt" => BodyTextures.Shirt(false, ink),
        "toonHips" => BodyTextures.Hips(false),
        "toonTrousers" => BodyTextures.Trousers(false, ink),
        "toonShoe" => BodyTextures.Shoe(false, ink),
        "braces" => BodyTextures.Strap(BodyTextures.ToonBraces, "InkVR_HenryBraces"),
        "belt" => BodyTextures.Strap(BodyTextures.Belt, "InkVR_HenryBelt"),
        "studioShirt" => BodyTextures.Shirt(true, ink),
        "studioHips" => BodyTextures.Hips(true),
        "studioTrousers" => BodyTextures.Trousers(true, ink),
        "studioBoot" => BodyTextures.Shoe(true, ink),
        "vest" => BodyTextures.Vest(ink),
        "lifeSkin" => HenryTextures.LifelikeFinger(ink, 31),
        "lifePalm" => HenryTextures.LifelikePalm(ink, 32),
        "lifeNail" => HenryTextures.LifelikeNail(33),
        "lifeForearm" => HenryTextures.LifelikeForearm(ink, 34),
        "tie" => BodyTextures.Tie(),
        _ => HenryTextures.Flat(Color.magenta, "InkVR_Missing_" + key)
    };

    private static readonly string[] HandKeys = { "finger", "tip", "palm", "forearm", "sleeve", "sleeveRolled", "strap", "dial", "gold", "ink", "glove", "glovePalm", "gloveCuff", "toonForearm" };

    private static void SetMaterial(string key, Texture2D tex)
    {
        if (Materials.TryGetValue(key, out var m) && m != null)
        {
            var old = m.GetTexture("_MainTex");
            m.SetTexture("_MainTex", tex);
            if (old != null && old.Pointer != tex.Pointer) Object.Destroy(old);
            return;
        }

        Materials[key] = InkMaterials.GameSurface(tex, "InkVR_Henry_" + key);
        if (!_usingFallbackShader) TryUseGameShaderFor(Materials[key]);
    }

    private static void BuildMaterials(float ink)
    {
        void Set(string key, Texture2D tex) => SetMaterial(key, tex);

        Set("finger", HenryTextures.Finger(false, ink, 11));
        Set("tip", HenryTextures.Finger(true, ink, 12));
        Set("palm", HenryTextures.Palm(ink, 13));
        Set("forearm", HenryTextures.Forearm(ink, 14));
        Set("glove", HenryTextures.GloveFinger(ink, 21));
        Set("glovePalm", HenryTextures.GlovePalm(ink, 22));
        Set("gloveCuff", HenryTextures.GloveCuff(23));
        Set("toonForearm", HenryTextures.CartoonForearm(ink, 24));
        Set("sleeve", HenryTextures.Sleeve(false, 15));
        Set("sleeveRolled", HenryTextures.Sleeve(true, 16));
        Set("strap", HenryTextures.Strap(17));
        Set("dial", HenryTextures.WatchFace(18));
        Set("gold", HenryTextures.Flat(new Color(0.80f, 0.62f, 0.26f), "InkVR_Gold"));
        Set("ink", HenryTextures.Flat(HenryTextures.Ink, "InkVR_InkFlat"));
        _usingFallbackShader = Materials["finger"].shader.name != "Meatly Shaders/Surfaces/Diffuse";

        // Repaint whichever body clothes exist, at this chapter's ink level.
        foreach (var key in new List<string>(Materials.Keys))
            if (System.Array.IndexOf(HandKeys, key) < 0)
                Set(key, BodyTexture(key, ink));
    }

    private static void TryUseGameShaderFor(Material m)
    {
        var shader = Shader.Find("Meatly Shaders/Surfaces/Diffuse");
        if (shader == null || m.shader == shader) return;
        var tex = m.GetTexture("_MainTex");
        var fresh = InkMaterials.GameSurface(tex.Cast<Texture2D>(), m.name);
        m.shader = fresh.shader;
        m.CopyPropertiesFromMaterial(fresh);
    }

    /// <summary>The title screen doesn't load the game's surface shader; switch to it once a chapter has.</summary>
    private static void TryUseGameShader()
    {
        _nextShaderCheck = Time.unscaledTime + 2f;
        var shader = Shader.Find("Meatly Shaders/Surfaces/Diffuse");
        if (shader == null) return;
        foreach (var kv in Materials)
        {
            var tex = kv.Value.GetTexture("_MainTex");
            var fresh = InkMaterials.GameSurface(tex.Cast<Texture2D>(), kv.Value.name);
            kv.Value.shader = fresh.shader;
            kv.Value.CopyPropertiesFromMaterial(fresh);
            InkStains.AdoptShader(kv.Value, tex.name);
        }

        _usingFallbackShader = false;
        Plugin.L.LogInfo("Henry's hands now use the game's surface shader");
    }

    // ---------------- lore: ink by chapter ----------------

    private static int CurrentChapter()
    {
        var m = Regex.Match(SceneManager.GetActiveScene().name, @"CH(\d)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    /// <summary>Clean-ish in Chapter 1; the studio's ink creeps further up his hands each chapter.</summary>
    private static float InkLevel() => CurrentChapter() switch
    {
        1 => 0.15f,
        2 => 0.3f,
        3 => 0.5f,
        4 => 0.7f,
        5 => 0.9f,
        _ => 0.2f
    };

    private static void UpdateChapterInk()
    {
        var ch = CurrentChapter();
        if (ch == _chapter) return;
        if (_chapter != -1)
        {
            BuildMaterials(InkLevel());
            InkStains.Rebase();
        }

        _chapter = ch;
    }

    // ---------------- held items ----------------

    private static bool HoldingWeapon()
    {
        var p = GameManager.Instance?.Player;
        if (p == null || !VRRig.InGame || VRRig.CinemaActive) return false;
        var w = p.WeaponGameObject;
        return w != null && w.activeInHierarchy;
    }

    /// <summary>
    /// The game hangs weapons/items off HandContainer/Hand in front of the flat camera. Put that mount
    /// in the dominant hand instead: Hand lands on the grip, oriented like the controller's aim.
    /// </summary>
    private static void AttachHeldItem()
    {
        var p = GameManager.Instance?.Player;
        if (p == null || !VRRig.InGame || VRRig.CinemaActive) return;
        var container = p.m_HandContainer;
        if (container == null) return;
        if (container != _handContainer)
        {
            _handContainer = container;
            _gameHand = container.childCount > 0 ? container.GetChild(0) : null;
            if (_gameHand == null) return;
            _handRestPos = _gameHand.localPosition;
            _handRestRot = _gameHand.localRotation;
        }

        if (_gameHand == null) return;
        var c = ControllerBindings.Dom;
        if (!c.PoseValid) return;
        VRRig.TrackingToWorld(c.GripPosition, c.AimRotation, out var gripWorld, out var aimWorld);
        // Two hands on a gun: it points from the dominant hand towards the one holding its front.
        if (WeaponHandling.TwoHandAim(out _, out var twoHands)) aimWorld = twoHands;
        var rot = aimWorld * Quaternion.Euler(ModConfig.HeldItemPitch.Value, 0, 0) * Quaternion.Inverse(_handRestRot);
        _handContainer.rotation = rot;
        _handContainer.position = gripWorld - rot * Vector3.Scale(_handRestPos, _handContainer.lossyScale);

        // Two hands on the axe: its handle lies along both hands, still held at the dominant one.
        if (WeaponHandling.TwoHandMelee(out var along) && MeleeSwing.HandleLine(out var near, out var far))
        {
            var fix = Quaternion.FromToRotation((far - near).normalized, along);
            rot = fix * rot;
            _handContainer.rotation = rot;
            _handContainer.position = gripWorld - rot * Vector3.Scale(_handRestPos, _handContainer.lossyScale);
        }
    }
}
