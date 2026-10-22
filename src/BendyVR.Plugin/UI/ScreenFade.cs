using UnityEngine;

namespace BendyVR.UI;

/// <summary>Reads the game's black/white full-screen fades (ScreenBlockerController canvas groups).</summary>
public static class ScreenFade
{
    public static Color Current()
    {
        var gm = GameManager.Instance;
        if (gm == null) return Color.clear;
        var black = Alpha(gm.m_ScreenBlockerController);
        var white = Alpha(gm.m_ScreenWhiteBlockerController);
        return white > black ? new Color(1, 1, 1, white) : new Color(0, 0, 0, black);
    }

    private static float Alpha(ScreenBlockerController c)
    {
        if (c == null || !c.gameObject.activeInHierarchy) return 0;
        var group = c.m_Blocker;
        return group != null ? group.alpha : 0;
    }
}
