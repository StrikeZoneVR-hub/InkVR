using BepInEx.Configuration;

namespace BendyVR;

public static class ModConfig
{
    public static ConfigEntry<bool> VREnabled;
    public static ConfigEntry<float> WorldScale;
    public static ConfigEntry<float> SmoothTurnSpeed;
    public static ConfigEntry<float> HeightOffset;
    public static ConfigEntry<bool> VRDisableMotionBlur;
    public static ConfigEntry<bool> VRDisableDepthOfField;
    public static ConfigEntry<bool> VRAODownsample;

    public static void Bind(ConfigFile cfg)
    {
        VREnabled = cfg.Bind("VR", "Enabled", true, "Start the game in VR (OpenXR). Off = flat-screen with the mod idle.");
        HeightOffset = cfg.Bind("VR", "HeightOffset", 0f, "Metres added to your tracked head height (seated play / height calibration).");
        SmoothTurnSpeed = cfg.Bind("Comfort", "SmoothTurnSpeed", 120f, "Degrees per second for smooth turning.");
        WorldScale = cfg.Bind("VR", "WorldScale", 2.95f, "Game units per real-world metre. Henry's eye is 5.03 units above the floor (= 1.70 m at 2.95); doors are 7.4 units (2.5 m).");
        VRDisableMotionBlur = cfg.Bind("Graphics", "DisableMotionBlurInVR", true, "Motion blur smears the whole view when you turn your head.");
        VRAODownsample = cfg.Bind("Graphics", "HalfResAmbientOcclusionInVR", true, "Render ambient occlusion at half resolution (saves ~1.5 ms per frame in VR).");
        VRDisableDepthOfField = cfg.Bind("Graphics", "DisableDepthOfFieldInVR", true, "Your eyes focus naturally in VR; screen-space depth of field blurs what you look at.");
    }
}
