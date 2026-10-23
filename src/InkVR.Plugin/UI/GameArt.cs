using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.UI;

/// <summary>
/// The game's own artwork for the theatre (Bendy's face, his poses, the chapter pictures, the ink splat).
/// The InkVR Launcher reads these from your installed game and keeps PNG copies in
/// %LOCALAPPDATA%\InkVR\art; nothing is shipped with the mod. Missing art just means a painted stand-in.
/// </summary>
public static class GameArt
{
    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InkVR", "art");

    /// <summary>Load one image (null when the launcher hasn't cached it, or the image can't be decoded).</summary>
    public static Texture2D Load(string name)
    {
        var path = Path.Combine(Dir, name + ".png");
        if (!File.Exists(path)) return null;
        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "InkVR_Art_" + name };
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path), true))
            {
                Object.Destroy(tex);
                return null;
            }

            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 4;
            tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            Object.DontDestroyOnLoad(tex);
            return tex;
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"Theatre art '{name}' couldn't be loaded: {e.Message}");
            return null;
        }
    }
}
