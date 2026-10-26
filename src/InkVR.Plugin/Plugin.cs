using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using InkVR.Core;
using InkVR.Dev;
using HarmonyLib;
using UnityEngine;

namespace InkVR;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "com.strikezonevr.inkvr";
    public const string Name = "InkVR";
    public const string Version = "0.9.1";

    internal static ManualLogSource L;
    internal static string DataDir;

    public override void Load()
    {
        L = Log;
        DataDir = Path.Combine(Paths.BepInExRootPath, "InkVR");
        Directory.CreateDirectory(DataDir);

        L.LogInfo($"{Name} {Version} loading. Unity {Application.unityVersion}, game {Application.version}");

        // The headset keeps rendering while the desktop window is unfocused, so the game must too.
        Application.runInBackground = true;

        ModConfig.Bind(Config);
        AddComponent<VRRuntime>();
        AddComponent<DevTools>();
        var harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);
        ShakePatches.Apply(harmony);

        L.LogInfo($"{Name} loaded.");
    }
}
