using System.Collections.Generic;
using InkVR.Body;
using InkVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.Input;

/// <summary>
/// The studio's throwing games by hand: the darts in Chapter 1's break room and the ball toss in Chapter 4.
/// The dart (or ball) the game holds up in front of the camera is in your hand instead; hold the trigger, swing
/// and let go, and it flies the way your hand was moving, as hard as you threw it (a gentle release lobs it where
/// your hand points). The game does the rest: the board, the score, the reload.
/// </summary>
public static class MinigameThrow
{
    private static readonly List<WinnableMiniGameBaseController> Games = new();
    private static string _scene = "";
    private static WinnableMiniGameBaseController _game;
    private static Quaternion _heldRel = Quaternion.identity;
    private static bool _armed;
    private static float _pendingUntil = -1f;
    private static Vector3 _dir, _from;
    private static float _gain;

    /// <summary>A throwing game is on and the dominant trigger is the throw (the game doesn't see it).</summary>
    public static bool Owns { get; private set; }

    /// <summary>Dev: the last throw.</summary>
    public static string Last = "";

    public static void Update()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != _scene)
        {
            // the throwing games in this scene, found once (a search like this costs too much mid-play)
            _scene = scene;
            Games.Clear();
            foreach (var o in Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<WinnableMiniGameBaseController>()))
            {
                var g = o.Cast<WinnableMiniGameBaseController>();
                if (g.TryCast<Minigame_Darts>() != null || g.TryCast<Minigame_BallToss>() != null) Games.Add(g);
            }

            _game = null;
        }

        WinnableMiniGameBaseController active = null;
        if (ModConfig.PhysicalThrow.Value && XrManager.IsRunning && VRRig.InGame && !VRRig.CinemaActive)
            foreach (var g in Games)
                if (g != null && g.CurrentState == WinnableMiniGameBaseController.MiniGameState.ACTIVE)
                    active = g;

        if (active == null)
        {
            Owns = false;
            _armed = false;
            _game = null;
            return;
        }

        if (_game == null || _game.Pointer != active.Pointer)
        {
            // how the game turns what it holds against its view: the same against your hand
            _game = active;
            var held0 = active.m_HeldItemPositionTransform;
            var look0 = active.m_LocalLookTransform;
            _heldRel = held0 != null && look0 != null ? Quaternion.Inverse(look0.rotation) * held0.rotation : Quaternion.identity;
            Plugin.L.LogInfo($"Throwing game: {active.name} by hand (hold the trigger, swing, let go)");
        }

        Owns = true;
        var dom = ControllerBindings.Dominant;
        var (grip, rot) = Grip(dom);
        var aim = Aim(dom);
        var palm = grip + rot * new Vector3(dom == Hand.Right ? -0.03f : 0.03f, -0.01f, 0.05f) * VRRig.ActiveScale;

        // what the game holds up, in your hand
        var held = active.m_HeldItemPositionTransform;
        if (held != null) held.SetPositionAndRotation(palm, aim * _heldRel);

        var trigger = TriggerValue(dom);
        if (!_armed && trigger > 0.6f)
        {
            _armed = true;
            XrInput.Haptic(dom, 0.15f, 0.03f);
        }
        else if (_armed && trigger < 0.3f)
        {
            _armed = false;
            Throw(dom, palm, aim);
        }
    }

    private static void Throw(Hand hand, Vector3 palm, Quaternion aim)
    {
        var v = WeaponHandling.Velocity(hand);
        var speed = v.magnitude / VRRig.ActiveScale; // real metres per second
        if (speed > 1.1f)
        {
            _dir = v.normalized;
            _gain = Mathf.Clamp(speed / 4f, 0.4f, 1.5f);
        }
        else
        {
            _dir = (aim * Vector3.forward + Vector3.up * 0.1f).normalized;
            _gain = 0.6f;
        }

        _from = palm;
        _pendingUntil = Time.unscaledTime + 0.35f;
        VirtualInput.Tap(GameAction.Attack); // the game throws on its own attack: the next frame
        XrInput.Haptic(hand, 0.4f, 0.06f);
        Last = $"{speed:F1} m/s, gain {_gain:F2}, along {_dir}";
        Plugin.L.LogInfo($"Throwing game: threw ({Last})");
    }

    /// <summary>The game is launching a thrown object: if it's the one just thrown by hand, it leaves the hand the way the hand went.</summary>
    internal static bool TakeThrow(ThrowableObject thrown, ref Vector3 force)
    {
        if (Time.unscaledTime > _pendingUntil) return false;
        _pendingUntil = -1f;
        force = _dir * force.magnitude * _gain;
        thrown.transform.SetPositionAndRotation(_from, Quaternion.LookRotation(_dir));
        return true;
    }

    private static (Vector3 pos, Quaternion rot) Grip(Hand hand)
    {
        var dev = hand == Hand.Right ? Hands.DevRight : Hands.DevLeft;
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, dev?.rot ?? c.GripRotation, out var p, out var r);
        return (p, r);
    }

    private static Quaternion Aim(Hand hand)
    {
        var dev = hand == Hand.Right ? Hands.DevRight : Hands.DevLeft;
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(c.AimPosition, dev?.rot ?? c.AimRotation, out _, out var r);
        return r;
    }

    private static float TriggerValue(Hand hand) => hand == Hand.Right && XrInput.DevRightTrigger.HasValue ? XrInput.DevRightTrigger.Value : XrInput.Get(hand).Trigger;
}
