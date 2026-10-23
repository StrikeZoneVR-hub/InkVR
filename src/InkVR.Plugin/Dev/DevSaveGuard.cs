using HarmonyLib;

namespace InkVR.Dev;

/// <summary>
/// Developer test runs (tools/vr-start.sh sets INKVR_DEV_NOSAVE=1) must never touch the player's save
/// slots: the game keeps all three in one file. With the variable set, the game simply doesn't save.
/// Normal launches (Steam, the launcher) never set it.
/// </summary>
[HarmonyPatch(typeof(GameDataManager), nameof(GameDataManager.SaveAsync))]
internal static class DevSaveGuard
{
    public static bool Active => System.Environment.GetEnvironmentVariable("INKVR_DEV_NOSAVE") == "1";
    private static bool _logged;

    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (!Active) return true;
        if (!_logged)
        {
            _logged = true;
            Plugin.L.LogWarning("Dev test session: game saving is disabled (INKVR_DEV_NOSAVE=1)");
        }

        return false;
    }
}
