using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BendyVR.Dev;
using HarmonyLib;
using UnityEngine;

namespace BendyVR;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "com.bendyvr.mod";
    public const string Name = "BendyVR";
    public const string Version = "0.1.0";

    internal static ManualLogSource L;
    internal static string DataDir;

    public override void Load()
    {
        L = Log;
        DataDir = Path.Combine(Paths.BepInExRootPath, "BendyVR");
        Directory.CreateDirectory(DataDir);

        L.LogInfo($"{Name} {Version} loading. Unity {Application.unityVersion}, game {Application.version}");

        AddComponent<DevTools>();
        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

        L.LogInfo($"{Name} loaded.");
    }
}
