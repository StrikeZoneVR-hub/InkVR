using BepInEx.Configuration;

namespace InkVR;

/// <summary>InkVR settings (BepInEx/config/com.strikezonevr.inkvr.cfg), shared with the in-game VR menu and the launcher.</summary>
public static class ModConfig
{
    // VR
    public static ConfigEntry<bool> VREnabled;
    public static ConfigEntry<float> WorldScale;
    public static ConfigEntry<bool> AutoWorldScale;
    public static ConfigEntry<float> MeasuredEyeHeight;
    public static ConfigEntry<bool> VolumetricLightVR;
    public static ConfigEntry<float> HeightOffset;

    // Comfort / controls
    public static ConfigEntry<bool> SnapTurn;
    public static ConfigEntry<float> SnapTurnAngle;
    public static ConfigEntry<float> SmoothTurnSpeed;
    public static ConfigEntry<bool> LeftHanded;
    public static ConfigEntry<bool> InteractWithHand;
    public static ConfigEntry<bool> PhysicalMelee;
    public static ConfigEntry<bool> AimGunsWithHand;
    public static ConfigEntry<bool> ReduceFlashes;

    // Body
    public static ConfigEntry<string> BodyMode;
    public static ConfigEntry<string> BodyStyle;

    /// <summary>A full body is shown.</summary>
    public static bool FullBody => BodyMode.Value == "Full";

    /// <summary>Arms are shown (full body, or arms and hands).</summary>
    public static bool ArmsShown => BodyMode.Value == "Full" || BodyMode.Value == "Arms";

    /// <summary>Any of Henry is shown.</summary>
    public static bool HandsShown => BodyMode.Value != "None";

    /// <summary>Lifelike hands and body (else the toon ones).</summary>
    public static bool Lifelike => BodyStyle.Value != "Toon";
    public static ConfigEntry<float> HeldItemPitch;
    public static ConfigEntry<bool> InkOnHands;
    public static ConfigEntry<float> InkFadeMinutes;

    // Graphics
    public static ConfigEntry<bool> VRDisableMotionBlur;
    public static ConfigEntry<bool> VRDisableDepthOfField;
    public static ConfigEntry<bool> VRAODownsample;
    public static ConfigEntry<float> RenderScale;
    public static ConfigEntry<bool> DynamicResolution;
    public static ConfigEntry<float> MinRenderScale;
    public static ConfigEntry<bool> LaserPointer;

    // Debug
    public static ConfigEntry<bool> PerfLog;

    public static ConfigFile File { get; private set; }

    /// <summary>
    /// Whether this session starts in VR. The InkVR Launcher sets INKVR_MODE=vr|flat for the game it starts,
    /// so "Play flat" never rewrites the saved setting; launching from Steam uses [VR] Enabled.
    /// </summary>
    public static bool StartInVR
    {
        get
        {
            var mode = System.Environment.GetEnvironmentVariable("INKVR_MODE");
            if (string.Equals(mode, "vr", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(mode, "flat", System.StringComparison.OrdinalIgnoreCase)) return false;
            return VREnabled.Value;
        }
    }

    public static void Bind(ConfigFile cfg)
    {
        File = cfg;
        VREnabled = cfg.Bind("VR", "Enabled", true, "Start the game in VR (OpenXR). Off = flat-screen with the mod idle.");
        WorldScale = cfg.Bind("VR", "WorldScale", 2.95f, "Game units per real-world metre. Henry's eye is 5.03 units above the floor (= 1.70 m at 2.95); doors are 7.4 units (2.5 m).");
        AutoWorldScale = cfg.Bind("VR", "AutoWorldScale", true, "Size the world to you: your standing eye height (measured as each level starts) becomes Henry's eye height. Off = use WorldScale.");
        MeasuredEyeHeight = cfg.Bind("VR", "MeasuredEyeHeight", 0f, "Your standing eye height in metres, last measured by AutoWorldScale (0 = not yet).");
        HeightOffset = cfg.Bind("VR", "HeightOffset", 0f, "Metres added to your tracked head height (seated play / height calibration).");

        SnapTurn = cfg.Bind("Comfort", "SnapTurn", true, "Turn in steps with the right stick (off = smooth turning).");
        SnapTurnAngle = cfg.Bind("Comfort", "SnapTurnAngle", 45f, "Degrees per snap turn.");
        SmoothTurnSpeed = cfg.Bind("Comfort", "SmoothTurnSpeed", 120f, "Degrees per second for smooth turning.");
        LeftHanded = cfg.Bind("Controls", "LeftHanded", false, "Swap the controller roles (move on the right stick, attack with the left trigger).");
        InteractWithHand = cfg.Bind("Controls", "InteractWithHand", true, "Point at things with your dominant hand to interact (off = look at them, like the flat game).");
        PhysicalMelee = cfg.Bind("Controls", "PhysicalMelee", true, "Swing your arm to hit with melee weapons (the axe, the pipe...). The attack button still swings too.");
        AimGunsWithHand = cfg.Bind("Controls", "AimGunsWithHand", true, "Guns fire where your hand points (off = where you look, like the flat game).");
        ReduceFlashes = cfg.Bind("Comfort", "ReduceFlashes", false, "Calm the theatre's projector flicker and film scratches.");

        BodyMode = cfg.Bind("Body", "Mode", "Full", new ConfigDescription(
            "How much of Henry you see: Full (full body), Arms (arms and hands), Hands (hands only), None.",
            new AcceptableValueList<string>("Full", "Arms", "Hands", "None")));
        BodyStyle = cfg.Bind("Body", "Style", "Lifelike", new ConfigDescription(
            "How Henry looks: Lifelike (real hands, lifelike body in waistcoat and tie) or Toon (cartoon hands, chunky body with braces and big shoes).",
            new AcceptableValueList<string>("Lifelike", "Toon")));
        HeldItemPitch = cfg.Bind("Body", "HeldItemPitch", 0f, "Extra pitch (degrees) applied to held weapons and items.");
        InkOnHands = cfg.Bind("Body", "InkOnHands", true, "Ink from spills, splats, pools, drips and ink bursts gets onto Henry's hands and sleeves.");
        InkFadeMinutes = cfg.Bind("Body", "InkFadeMinutes", 3f, "How long fresh ink takes to dry and fade back to the chapter's usual stains.");

        VRDisableMotionBlur = cfg.Bind("Graphics", "DisableMotionBlurInVR", true, "Motion blur smears the whole view when you turn your head.");
        VRDisableDepthOfField = cfg.Bind("Graphics", "DisableDepthOfFieldInVR", true, "Your eyes focus naturally in VR; screen-space depth of field blurs what you look at.");
        VRAODownsample = cfg.Bind("Graphics", "HalfResAmbientOcclusionInVR", true, "Render ambient occlusion at half resolution (saves ~1.5 ms per frame in VR).");
        RenderScale = cfg.Bind("Graphics", "RenderScale", 1.0f, "Headset render resolution multiplier (1.0 = the runtime's recommended resolution). With DynamicResolution this is the most it will use.");
        DynamicResolution = cfg.Bind("Graphics", "DynamicResolution", true, "Lower the resolution a little when the graphics card can't keep up with the headset's refresh rate, and raise it again when it can (smooth frame rate first).");
        MinRenderScale = cfg.Bind("Graphics", "MinRenderScale", 0.7f, "The lowest DynamicResolution may go.");
        VolumetricLightVR = cfg.Bind("Graphics", "VolumetricLightInVR", true, "The game's light beams through the air, rendered per eye in VR (off saves GPU time).");
        LaserPointer = cfg.Bind("Controls", "LaserPointer", false, "Show a laser from your hand to click menus (the thumbstick and A always work).");

        PerfLog = cfg.Bind("Debug", "LogPerformance", true, "Write VR frame timing to the BepInEx log every 5 seconds (helps tuning).");
    }
}
