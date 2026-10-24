using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using InkVR.Body;
using InkVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace InkVR.Input;

/// <summary>
/// Things you press with your hand (motion controls part 3): a fingertip or palm that moves into a button, a
/// tape recorder, a squeaky toy, a piano or organ key, a lift button, a radio or a projector presses it, as the
/// game's own interaction (a press, not a rest: the hand has to be moving into it, and has to come away again
/// before the next press). Grip and X still work on all of them.
/// </summary>
public static class PhysicalTouch
{
    private sealed class Target
    {
        public Interactable Item;
        public Collider Col;
        public string What;
        public float Next;
        public bool Armed = true;
    }

    private static readonly List<Target> Targets = new();
    private static readonly HashSet<IntPtr> Known = new();
    private static string _scene;
    private static float _nextScan;
    private static readonly Vector3[] PrevTip = new Vector3[2], PrevPalm = new Vector3[2];
    private static readonly float[] PrevT = new float[2];

    public static string Last = "";
    public static int Count => Targets.Count;

    // Interactable types that are pressed, and scripts that own pressable things.
    private static readonly string[] PressTypes = { "AudioLog", "InteractableAudio", "AudioPlayer", "SqueakToy", "InteractableMusicalInstrument" };
    private static readonly string[] PressOwners = { "CH3LiftButton", "CH3AliceButtonController", "RadioEasterEggController", "ProjectorController", "CH2PipeOrganController", "CH3ToyMachine" };

    private static void Scan()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var added = 0;
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Interactable>()))
        {
            var it = o.Cast<Interactable>();
            if (!it.gameObject.scene.IsValid() || Known.Contains(it.Pointer)) continue;
            Known.Add(it.Pointer);
            var what = Pressable(it);
            if (what == null) continue;
            var col = it.GetComponent<Collider>() ?? it.GetComponentInChildren<Collider>();
            if (col == null) continue;
            Targets.Add(new Target { Item = it, Col = col, What = what });
            added++;
        }

        Targets.RemoveAll(t => t.Item == null || t.Col == null);
        if (added > 0) Plugin.L.LogInfo($"Press by touch: {Targets.Count} things in this level ({added} new, {sw.Elapsed.TotalMilliseconds:F1} ms)");
    }

    private static string Pressable(Interactable it)
    {
        var type = it.GetIl2CppType().Name;
        foreach (var t in PressTypes)
            if (type == t)
                return t;
        var tr = it.transform;
        for (var up = 0; up < 3 && tr != null; up++, tr = tr.parent)
            foreach (var c in tr.GetComponents<MonoBehaviour>())
            {
                var n = c.GetIl2CppType().Name;
                foreach (var owner in PressOwners)
                    if (n == owner)
                        return owner;
            }

        var name = it.name.ToLowerInvariant();
        return name.Contains("button") || name.Contains("buzzer") ? "button" : null;
    }

    public static void Update()
    {
        var scene = SceneManager.GetActiveScene().name;
        if (scene != _scene)
        {
            _scene = scene;
            Targets.Clear();
            Known.Clear();
            _nextScan = Time.unscaledTime + 2.5f;
        }

        if (!ModConfig.PhysicalInteractions.Value || !XrManager.IsRunning || !VRRig.InGame || VRRig.CinemaActive) return;
        var now = Time.unscaledTime;
        if (now >= _nextScan)
        {
            _nextScan = now + 8f;
            Scan();
        }

        var head = VRRig.Head;
        if (head == null || Targets.Count == 0) return;
        var k = VRRig.ActiveScale;
        foreach (var hand in new[] { Hand.Right, Hand.Left })
        {
            var h = (int)hand;
            var (pos, rot) = HandGrip(hand);
            var tip = pos + rot * new Vector3(0, -0.02f, 0.12f) * k;
            var palm = pos + rot * new Vector3(hand == Hand.Right ? -0.02f : 0.02f, -0.02f, 0.05f) * k;
            var dt = now - PrevT[h];
            var tipVel = dt > 0 && dt < 0.2f ? (tip - PrevTip[h]) / dt : Vector3.zero;
            var palmVel = dt > 0 && dt < 0.2f ? (palm - PrevPalm[h]) / dt : Vector3.zero;
            PrevTip[h] = tip;
            PrevPalm[h] = palm;
            PrevT[h] = now;
            if (tipVel.magnitude > 9f * k) continue; // tracking jump
            if (PhysicalDoors.Holding(hand)) continue;

            foreach (var t in Targets)
            {
                if (t.Item == null || t.Col == null || !t.Col.enabled || !t.Item.gameObject.activeInHierarchy) continue;
                var c = t.Col.bounds.center;
                if ((c - head.position).sqrMagnitude > 2.5f * k * 2.5f * k) continue;
                Press(t, hand, tip, tipVel, 0.015f * k, now, k);
                Press(t, hand, palm, palmVel, 0.04f * k, now, k);
            }
        }
    }

    private static void Press(Target t, Hand hand, Vector3 at, Vector3 vel, float r, float now, float k)
    {
        var closest = t.Col.ClosestPoint(at);
        var gap = (closest - at).magnitude;
        if (gap > r + 0.025f * k)
        {
            t.Armed = true; // came away: ready for the next press
            return;
        }

        if (gap > r || !t.Armed || now < t.Next) return;
        // moving into it, not resting on it
        var into = closest == at ? vel.magnitude : Vector3.Dot(vel, (closest - at).normalized);
        if (into < 0.25f * k) return;
        t.Armed = false;
        t.Next = now + 0.5f;
        XrInput.Haptic(hand, 0.35f, 0.03f);
        var ctrl = GameManager.Instance?.Player?.m_Interaction;
        if (ctrl != null && ctrl.Interactable != null && ctrl.Interactable.Pointer == t.Item.Pointer) ctrl.Interact();
        else t.Item.Interact();
        Last = $"pressed {t.What} '{t.Item.name}' with the {hand} hand ({into / k:F1} m/s)";
        Plugin.L.LogInfo($"Press by touch: {Last}");
    }

    private static (Vector3 pos, Quaternion rot) HandGrip(Hand hand)
    {
        var dev = hand == Hand.Right ? Hands.DevRight : Hands.DevLeft;
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, dev?.rot ?? c.GripRotation, out var p, out var r);
        return (p, r);
    }

    /// <summary>Dev: the n-th nearest pressable thing, and a hand placed just in front of it.</summary>
    public static string DevNearest(int n, out Vector3 at, out Vector3 normal)
    {
        at = normal = default;
        var player = GameManager.Instance?.Player;
        if (player == null) return "no player";
        if (Targets.Count == 0) Scan();
        var list = new List<(float d, Target t)>();
        foreach (var t in Targets)
            if (t.Item != null && t.Col != null && t.Item.gameObject.activeInHierarchy)
                list.Add(((t.Col.bounds.center - player.transform.position).magnitude, t));
        list.Sort((a, b) => a.d.CompareTo(b.d));
        if (n >= list.Count) return "none";
        var best = list[n].t;
        at = best.Col.bounds.center;
        normal = (player.transform.position - at);
        normal.y = 0;
        normal.Normalize();
        return $"{best.What} '{Dev.DevTools.PathOf(best.Item.transform)}' at {list[n].d / VRRig.ActiveScale:F1} m, size {best.Col.bounds.size / VRRig.ActiveScale} m; last '{Last}'";
    }
}
