using BepInEx.Configuration;

namespace BendyVR;

public static class ModConfig
{
    public static ConfigEntry<bool> VREnabled;
    public static ConfigEntry<float> WorldScale;

    public static void Bind(ConfigFile cfg)
    {
        VREnabled = cfg.Bind("VR", "Enabled", true, "Start the game in VR (OpenXR). Off = flat-screen with the mod idle.");
        WorldScale = cfg.Bind("VR", "WorldScale", 2.53f, "Game units per real-world metre. Henry's eye height is 4.3 units (= 1.70 m at 2.53).");
    }
}
