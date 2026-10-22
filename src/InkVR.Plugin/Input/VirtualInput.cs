using System.Collections.Generic;
using HarmonyLib;
using TMG.Controls;
using UnityEngine;

namespace InkVR.Input;

/// <summary>
/// Game actions the mod can drive. Every source (VR controllers, dev commands) writes here,
/// and the Harmony patches below merge it into the game's own PlayerInput queries, so all
/// existing gameplay code sees VR input as if it came from a gamepad.
/// </summary>
public enum GameAction
{
    Attack,
    Run,
    Jump,
    Pause,
    Back,
    Interact
}

public static class VirtualInput
{
    private static readonly Dictionary<GameAction, int> PressedFrame = new();
    private static readonly Dictionary<GameAction, int> ReleasedFrame = new();
    private static readonly Dictionary<GameAction, bool> Held = new();
    private static readonly Dictionary<GameAction, float> HeldUntil = new();

    public static Vector2 Move;
    public static Vector2 Look;

    /// <summary>Horizontal look/turn input captured from the game's own input while the VR rig owns the camera.</summary>
    public static float TurnInput;

    /// <summary>In VR the headset aims the camera; stick look turns the play space instead (see VRRig).</summary>
    public static bool HeadsetOwnsLook => InkVR.Core.XrManager.IsRunning && InkVR.Core.VRRig.InGame;

    /// <summary>Set the held state of an action for this frame; edges become Pressed/Released.</summary>
    public static void SetHeld(GameAction action, bool held)
    {
        Held.TryGetValue(action, out var was);
        if (held && !was) PressedFrame[action] = Time.frameCount;
        if (!held && was) ReleasedFrame[action] = Time.frameCount;
        Held[action] = held;
    }

    /// <summary>One-frame press on the next frame, held for <paramref name="seconds"/> (dev use).</summary>
    public static void Tap(GameAction action, float seconds = 0f)
    {
        PressedFrame[action] = Time.frameCount + 1;
        HeldUntil[action] = Time.unscaledTime + Mathf.Max(seconds, 0.05f);
    }

    private static readonly Dictionary<GameAction, int> PressSeen = new();
    private static readonly Dictionary<GameAction, int> ReleaseSeen = new();

    public static bool IsPressed(GameAction a) => Edge(PressedFrame, PressSeen, a);

    public static bool IsReleased(GameAction a) => Edge(ReleasedFrame, ReleaseSeen, a);

    public static bool IsHeld(GameAction a) =>
        (Held.TryGetValue(a, out var h) && h) || (HeldUntil.TryGetValue(a, out var t) && Time.unscaledTime < t);

    public static bool AnyPressed()
    {
        foreach (var kv in PressedFrame)
            if (Edge(PressedFrame, PressSeen, kv.Key))
                return true;
        return false;
    }

    /// <summary>
    /// An edge is visible during the frame it happened. If nothing read it that frame (input sampled
    /// after the reader ran), it stays visible for the following frame so presses are never lost.
    /// </summary>
    private static bool Edge(Dictionary<GameAction, int> frames, Dictionary<GameAction, int> seen, GameAction a)
    {
        if (!frames.TryGetValue(a, out var f)) return false;
        var now = Time.frameCount;
        if (f == now)
        {
            seen[a] = now;
            return true;
        }

        return f == now - 1 && (!seen.TryGetValue(a, out var s) || s != f);
    }
}

[HarmonyPatch(typeof(PlayerInput))]
public static class PlayerInputPatches
{
    /// <summary>Dev: how often the game queries each input (shows which ones a scene actually polls).</summary>
    public static readonly Dictionary<string, int> Calls = new();

    private static void Count(string name)
    {
        Calls.TryGetValue(name, out var n);
        Calls[name] = n + 1;
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.Any))]
    private static void Any(ref bool __result)
    {
        Count("Any");
        __result |= VirtualInput.AnyPressed();
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.Attack))]
    private static void Attack(ref bool __result)
    {
        Count("Attack");
        __result |= VirtualInput.IsPressed(GameAction.Attack);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.AttackHold))]
    private static void AttackHold(ref bool __result)
    {
        Count("AttackHold");
        __result |= VirtualInput.IsHeld(GameAction.Attack);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.Run))]
    private static void Run(ref bool __result)
    {
        Count("Run");
        __result |= VirtualInput.IsHeld(GameAction.Run);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.RunDown))]
    private static void RunDown(ref bool __result)
    {
        Count("RunDown");
        __result |= VirtualInput.IsPressed(GameAction.Run);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.Jump))]
    private static void Jump(ref bool __result)
    {
        Count("Jump");
        __result |= VirtualInput.IsPressed(GameAction.Jump);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.Pause))]
    private static void Pause(ref bool __result)
    {
        Count("Pause");
        __result |= VirtualInput.IsPressed(GameAction.Pause);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.BackOnPressed))]
    private static void Back(ref bool __result)
    {
        Count("Back");
        __result |= VirtualInput.IsPressed(GameAction.Back);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.InteractOnPressed))]
    private static void InteractPressed(ref bool __result)
    {
        Count("InteractPressed");
        __result |= VirtualInput.IsPressed(GameAction.Interact);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.InteractOnReleased))]
    private static void InteractReleased(ref bool __result)
    {
        Count("InteractReleased");
        __result |= VirtualInput.IsReleased(GameAction.Interact);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.MoveX))]
    private static void MoveX(ref float __result)
    {
        Count("MoveX");
        __result = Combine(__result, VirtualInput.Move.x);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.MoveY))]
    private static void MoveY(ref float __result)
    {
        Count("MoveY");
        __result = Combine(__result, VirtualInput.Move.y);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.LookX))]
    private static void LookX(float TSpeed, ref float __result)
    {
        Count("LookX");
        __result = Combine(__result, VirtualInput.Look.x);
        if (!VirtualInput.HeadsetOwnsLook) return;
        VirtualInput.TurnInput = Mathf.Clamp(__result, -1f, 1f);
        __result = 0;
    }

    [HarmonyPostfix, HarmonyPatch(nameof(PlayerInput.LookY))]
    private static void LookY(float TSpeed, ref float __result)
    {
        __result = Combine(__result, VirtualInput.Look.y);
        if (VirtualInput.HeadsetOwnsLook) __result = 0;
    }

    private static float Combine(float game, float vr) => Mathf.Abs(vr) > Mathf.Abs(game) ? vr : game;
}
