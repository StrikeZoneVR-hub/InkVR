using BepInEx.Configuration;

namespace InkVR;

/// <summary>InkVR settings (BepInEx/config/com.strikezonevr.inkvr.cfg), shared with the in-game VR menu and the launcher.</summary>
public static class ModConfig
{
    // VR
    public static ConfigEntry<bool> VREnabled;
    public static ConfigEntry<float> WorldScale;
    public static ConfigEntry<float> HeightOffset;

    // Comfort / controls
    public static ConfigEntry<bool> SnapTurn;
    public static ConfigEntry<float> SnapTurnAngle;
    public static ConfigEntry<float> SmoothTurnSpeed;
    public static ConfigEntry<bool> LeftHanded;
    public static ConfigEntry<bool> InteractWithHand;
    public static ConfigEntry<bool> ReduceFlashes;

    // Body
    public static ConfigEntry<bool> ShowHands;
    public static ConfigEntry<bool> ShowArms;
    public static ConfigEntry<float> HeldItemPitch;

    // Graphics
    public static ConfigEntry<bool> VRDisableMotionBlur;
    public static ConfigEntry<bool> VRDisableDepthOfField;
    public static ConfigEntry<bool> VRAODownsample;
    public static ConfigEntry<float> RenderScale;

    // Debug
    public static ConfigEntry<bool> PerfLog;

    public static ConfigFile File { get; private set; }

    public static void Bind(ConfigFile cfg)
    {
        File = cfg;
        VREnabled = cfg.Bind("VR", "Enabled", true, "Start the game in VR (OpenXR). Off = flat-screen with the mod idle.");
        WorldScale = cfg.Bind("VR", "WorldScale", 2.95f, "Game units per real-world metre. Henry's eye is 5.03 units above the floor (= 1.70 m at 2.95); doors are 7.4 units (2.5 m).");
        HeightOffset = cfg.Bind("VR", "HeightOffset", 0f, "Metres added to your tracked head height (seated play / height calibration).");

        SnapTurn = cfg.Bind("Comfort", "SnapTurn", true, "Turn in steps with the right stick (off = smooth turning).");
        SnapTurnAngle = cfg.Bind("Comfort", "SnapTurnAngle", 45f, "Degrees per snap turn.");
        SmoothTurnSpeed = cfg.Bind("Comfort", "SmoothTurnSpeed", 120f, "Degrees per second for smooth turning.");
        LeftHanded = cfg.Bind("Controls", "LeftHanded", false, "Swap the controller roles (move on the right stick, attack with the left trigger).");
        InteractWithHand = cfg.Bind("Controls", "InteractWithHand", true, "Point at things with your dominant hand to interact (off = look at them, like the flat game).");
        ReduceFlashes = cfg.Bind("Comfort", "ReduceFlashes", false, "Calm the theatre's projector flicker and film scratches.");

        ShowHands = cfg.Bind("Body", "ShowHands", true, "Show Henry's hands.");
        ShowArms = cfg.Bind("Body", "ShowArms", true, "Show Henry's forearms and sleeves.");
        HeldItemPitch = cfg.Bind("Body", "HeldItemPitch", 0f, "Extra pitch (degrees) applied to held weapons and items.");

        VRDisableMotionBlur = cfg.Bind("Graphics", "DisableMotionBlurInVR", true, "Motion blur smears the whole view when you turn your head.");
        VRDisableDepthOfField = cfg.Bind("Graphics", "DisableDepthOfFieldInVR", true, "Your eyes focus naturally in VR; screen-space depth of field blurs what you look at.");
        VRAODownsample = cfg.Bind("Graphics", "HalfResAmbientOcclusionInVR", true, "Render ambient occlusion at half resolution (saves ~1.5 ms per frame in VR).");
        RenderScale = cfg.Bind("Graphics", "RenderScale", 1.0f, "Headset render resolution multiplier (1.0 = the runtime's recommended resolution).");

        PerfLog = cfg.Bind("Debug", "LogPerformance", true, "Write VR frame timing to the BepInEx log every 5 seconds (helps tuning).");
    }
}
