using System.Collections.Generic;
using HarmonyLib;
using InkVR.Body;
using InkVR.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.Input;

/// <summary>
/// Physical interactions (motion controls part 2). Grip something and the game's interaction waits for your
/// hand to do the real thing:
///   valves and wheels: turn them (rotate your wrist, or wind your hand round the wheel) about a third of a turn
///   levers: pull them (a decisive pull of 12 cm or so, any direction)
///   Bacon Soup: pick the can up and bring it to your mouth to eat it; let go anywhere else and it goes back
/// Then the game's own interaction runs (its animation, sounds, puzzle logic). Anything else interacts on
/// grip as before, and X always does the classic instant interaction. Holding still with the grip for 2.5 s
/// also completes it, so nothing can get stuck.
/// </summary>
public static class PhysicalInteract
{
    public enum Kind
    {
        None,
        Valve,
        Lever,
        Soup,
        Take,
        Place
    }

    private sealed class Gesture
    {
        public Kind Kind;
        public Hand Hand;
        public Interactable Item;
        public InteractableInputController Ctrl;
        public Vector3 Centre, Axis, StartHand, LastVec;
        public Quaternion LastRot;
        public float Turned, NextTick, Started, StillSince;
        public Vector3 StillAt;
        public Quaternion StillRot;
        public GameObject Held;
        public readonly List<Renderer> Hidden = new();
        public CH1Pedestal Pedestal;
        public Quaternion HoldRot;
        public Vector3 HoldOffset;
    }

    /// <summary>A thing you took, still in your hand after the game has it (it goes into your pocket when you let go).</summary>
    private sealed class Trophy
    {
        public GameObject Copy;
        public Hand Hand;
        public Quaternion Rot;
        public Vector3 Offset;
        public Interactable Original;
        public float CheckAt, ShrinkFrom = -1f;
    }

    private static readonly List<Trophy> Trophies = new();

    private static Gesture _g;

    /// <summary>Dev: what the last gesture did.</summary>
    public static string LastResult = "";

    public static Kind Active => _g?.Kind ?? Kind.None;

    private static readonly bool[] Claimed = new bool[2];
    private static readonly bool[] WasGripping = new bool[2];
    private static int _claimFrame = -1;

    /// <summary>
    /// Called where the controllers become the game's buttons, before a grip can press "interact": a grip that
    /// closes on a valve, a lever or a can starts the hand gesture instead (the game is pressed for you when it's
    /// done), and a grip that holds the gun's front or reaches for the holster isn't an interact at all.
    /// Returns whether each hand's grip is kept from the game this frame.
    /// </summary>
    public static (bool left, bool right) ClaimGrips()
    {
        if (_claimFrame != Time.frameCount)
        {
            _claimFrame = Time.frameCount;
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                var h = (int)hand;
                var gripping = Grip(hand) > 0.6f;
                var pressed = gripping && !WasGripping[h];
                WasGripping[h] = gripping;
                if (!gripping)
                {
                    Claimed[h] = false;
                    continue;
                }

                if (!pressed) continue;
                Claimed[h] = Claim(hand);
            }
        }

        return (Claimed[0], Claimed[1]);
    }

    private static bool Claim(Hand hand)
    {
        if (!XrManager.IsRunning || !VRRig.InGame || VRRig.CinemaActive) return false;
        if (WeaponHandling.WantsGrip(hand)) return true; // the gun's front, or the holster
        if (_g != null) return _g.Hand == hand;
        if (ModConfig.PhysicalInteractions.Value)
        {
            var item = InteractionAim.Hovered;
            var kind = InteractionAim.HoverHand == hand && item != null ? Classify(item) : Kind.None;
            if (kind != Kind.None)
            {
                Begin(kind, hand, item, GameManager.Instance?.Player?.m_Interaction);
                return true;
            }
        }

        // a door: take hold of it and swing it yourself
        return PhysicalDoors.TryGrab(hand);
    }

    private static bool DevGrip(Hand hand) => (hand == Hand.Right ? XrInput.DevRightGrip : XrInput.DevLeftGrip) is > 0.6f;

    private static float Grip(Hand hand)
    {
        var dev = hand == Hand.Right ? XrInput.DevRightGrip : XrInput.DevLeftGrip;
        return dev ?? XrInput.Get(hand).Grip;
    }

    // ---------------- what kind of thing is it ----------------

    public static Kind Classify(Interactable item)
    {
        if (item.TryCast<CannedSoupEdible>() != null) return Kind.Soup;
        // Chapter 1's collectables: take them in your hand; carry each to its pedestal and set it down
        var ped = item.GetComponentInParent<CH1Pedestal>();
        if (ped != null)
        {
            if (ped.m_Pedestal != null && ped.m_Pedestal.Pointer == item.Pointer) return ped.isCollected && !ped.isComplete ? Kind.Place : Kind.None;
        }

        foreach (var p in Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<CH1Pedestal>()))
        {
            var pd = p.Cast<CH1Pedestal>();
            if (pd.m_Collectable != null && pd.m_Collectable.Pointer == item.Pointer) return Kind.Take;
        }

        if (Takeable(item)) return Kind.Take;
        if (item.TryCast<InteractablePowerLever>() != null) return Kind.Lever;
        // lids lift, drawers slide out, switches flip: a pull by hand, like a lever
        if (item.TryCast<InteractableTrunk>() != null || item.TryCast<InteractableDrawer>() != null) return Kind.Lever;
        if (item.GetComponentInParent<InteractableSwitchBox>() != null) return Kind.Lever;
        var lever = item.GetComponentInParent<CH3LeverLight>();
        if (lever != null && lever.m_Lever != null && lever.m_Lever.Pointer == item.Pointer) return Kind.Lever;
        var valve = item.GetComponentInParent<CH1PipeValve>();
        if (valve != null && valve.m_Valve != null && valve.m_Valve.Pointer == item.Pointer) return Kind.Valve;
        var puzzle = item.GetComponentInParent<CH3InkPressurePuzzle>();
        if (puzzle != null && puzzle.m_InkValves != null)
            foreach (var v in puzzle.m_InkValves)
                if (v?.Valve != null && v.Valve.Pointer == item.Pointer)
                    return Kind.Valve;

        var name = item.name.ToLowerInvariant();
        var parent = item.transform.parent != null ? item.transform.parent.name.ToLowerInvariant() : "";
        if (Word(name, "valve") || Word(name, "wheel") || Word(parent, "valve")) return Kind.Valve;
        if (Word(name, "lever") || Word(parent, "lever")) return Kind.Lever;
        return Kind.None;
    }

    private static bool Word(string s, string w) => s.Contains(w) && !s.Contains("door") && !s.Contains("panel");

    /// <summary>A small thing the game collects in one go (it disappears into your inventory): you take it in your hand.</summary>
    private static bool Takeable(Interactable item)
    {
        if (!item.m_EnableSingleInteraction) return false;
        var name = item.name.ToLowerInvariant();
        if (!(name.Contains("collect") || name.Contains("gear") || name.Contains("key") || name.Contains("battery") || name.Contains("item")
              || name.Contains("pickup") || name.Contains("bone") || name.Contains("heart") || name.Contains("core") || name.Contains("thickink") || name.Contains("ink_jar")))
            return false;
        if (item.GetComponentInParent<MeleeWeapon>() != null || item.GetComponentInParent<GunWeapon>() != null || item.GetComponentInParent<ThrowWeapon>() != null) return false;
        var any = false;
        foreach (var r in item.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.enabled) continue;
            any = true;
            var size = r.bounds.size / VRRig.ActiveScale;
            if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) > 0.7f) return false;
        }

        return any;
    }

    // ---------------- the gesture ----------------

    private static void Begin(Kind kind, Hand hand, Interactable item, InteractableInputController ctrl)
    {
        var grip = HandGrip(hand);
        var r = item.GetComponentInChildren<Renderer>();
        var b = r != null ? r.bounds : new Bounds(item.transform.position, Vector3.one * 0.2f);
        var g = new Gesture
        {
            Kind = kind, Hand = hand, Item = item, Ctrl = ctrl, Centre = b.center, StartHand = grip.pos, LastRot = grip.rot,
            Started = Time.unscaledTime, StillSince = Time.unscaledTime, StillAt = grip.pos, StillRot = grip.rot,
        };
        g.Axis = WheelAxis(r, item.transform);
        // Cranks and wheels turn about their own pivot (the object's origin), when it's on the object.
        var pivot = item.transform.position;
        var reach = b;
        reach.Expand(0.3f * VRRig.ActiveScale);
        if (kind == Kind.Valve && reach.Contains(pivot)) g.Centre = pivot;
        g.LastVec = grip.pos - g.Centre;
        _g = g;
        XrInput.Haptic(hand, 0.3f, 0.04f);
        if (kind == Kind.Soup) PickUp(g);
        if (kind == Kind.Place)
        {
            g.Pedestal = item.GetComponentInParent<CH1Pedestal>();
            var collected = g.Pedestal?.m_Collected;
            if (collected != null) CopyIntoHand(g, collected.transform, true, grip.pos, grip.rot);
        }

        if (kind == Kind.Take)
        {
            // into your hand at once; the game takes it (its sound, its inventory) the same moment
            CopyIntoHand(g, item.transform, false, grip.pos, grip.rot);
            var copy = g.Held;
            g.Held = null;
            Trophies.Add(new Trophy { Copy = copy, Hand = hand, Rot = g.HoldRot, Offset = g.HoldOffset, Original = item, CheckAt = Time.unscaledTime + 0.35f });
            _g = g;
            Complete("taken");
            return;
        }
        LastResult = $"{kind} {item.name}: started";
        Plugin.L.LogInfo($"Physical interaction: {kind} '{item.name}' with the {hand} hand");
    }

    /// <summary>A wheel turns about its thinnest axis.</summary>
    private static Vector3 WheelAxis(Renderer r, Transform t)
    {
        var mf = r != null ? r.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) return t.forward;
        var e = mf.sharedMesh.bounds.extents;
        var local = e.x <= e.y && e.x <= e.z ? Vector3.right : e.y <= e.z ? Vector3.up : Vector3.forward;
        return r.transform.TransformDirection(local).normalized;
    }

    private static (Vector3 pos, Quaternion rot) HandGrip(Hand hand)
    {
        var dev = hand == Hand.Right ? Hands.DevRight : Hands.DevLeft;
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, dev?.rot ?? c.GripRotation, out var p, out var r);
        return (p, r);
    }

    public static void Update()
    {
        if (!XrInput.Ready) ClaimGrips(); // dev grips on the mock runtime (no controller sampling there)
        UpdateTrophies();
        var g = _g;
        if (g == null) return;
        if (g.Item == null || !VRRig.InGame || VRRig.CinemaActive)
        {
            Cancel("lost");
            return;
        }

        var scale = VRRig.ActiveScale;
        var (pos, rot) = HandGrip(g.Hand);
        var held = Grip(g.Hand) > 0.35f;

        // Holding still: a slow, steady hold also completes (nothing may get stuck on a gesture).
        if ((pos - g.StillAt).magnitude > 0.03f * scale || Quaternion.Angle(rot, g.StillRot) > 8f)
        {
            g.StillAt = pos;
            g.StillRot = rot;
            g.StillSince = Time.unscaledTime;
        }

        switch (g.Kind)
        {
            case Kind.Valve:
            {
                if (!held)
                {
                    Cancel("let go");
                    return;
                }

                // Winding the hand round the pivot (a crank, a wheel's rim), or twisting the wrist (a wheel held
                // across its face): whichever turned more. Any axis: cranks and wheels sit every which way.
                var v = pos - g.Centre;
                var wind = v.magnitude > 0.03f * scale && g.LastVec.magnitude > 0.03f * scale ? Vector3.Angle(g.LastVec, v) : 0f;
                (rot * Quaternion.Inverse(g.LastRot)).ToAngleAxis(out var twist, out _);
                if (twist > 180f) twist = 360f - twist;
                if (twist < 1f) twist = 0f; // tracking jitter
                g.LastVec = v;
                g.LastRot = rot;
                g.Turned += Mathf.Max(wind, twist);
                if (g.Turned >= g.NextTick)
                {
                    g.NextTick += 15f;
                    XrInput.Haptic(g.Hand, 0.25f, 0.02f); // the ratchet of the valve
                }

                if (g.Turned >= 100f) Complete("turned");
                else if (Time.unscaledTime - g.StillSince > 2.5f) Complete("held");
                break;
            }
            case Kind.Lever:
            {
                if (!held)
                {
                    Cancel("let go");
                    return;
                }

                var pulled = (pos - g.StartHand).magnitude / scale;
                if (pulled > g.NextTick / 100f)
                {
                    g.NextTick += 3f;
                    XrInput.Haptic(g.Hand, 0.15f + pulled, 0.02f);
                }

                if (pulled >= 0.12f) Complete("pulled");
                else if (Time.unscaledTime - g.StillSince > 2.5f) Complete("held");
                break;
            }
            case Kind.Place:
            {
                // carry it to the pedestal and set it down there
                if (g.Held != null) g.Held.transform.SetPositionAndRotation(pos + rot * g.HoldOffset, rot * g.HoldRot);
                if (!held)
                {
                    Cancel("let go");
                    return;
                }

                var spot = g.Pedestal?.m_Collected != null ? g.Pedestal.m_Collected.transform.position : g.Item.transform.position;
                var item = g.Held != null ? g.Held.transform.position : pos;
                if ((item - spot).magnitude < 0.22f * scale)
                {
                    DropHeld(g);
                    Complete("placed");
                }
                else if (Time.unscaledTime - g.StillSince > 2.5f)
                {
                    DropHeld(g);
                    Complete("held");
                }

                break;
            }
            case Kind.Soup:
            {
                Carry(g, pos, rot);
                if (!held)
                {
                    PutBack(g);
                    Cancel("put back");
                    return;
                }

                var head = VRRig.Head;
                if (head == null) break;
                var mouth = head.position + head.rotation * new Vector3(0, -0.085f, 0.07f) * scale;
                var can = g.Held != null ? g.Held.transform.position : pos;
                if ((can - mouth).magnitude < 0.13f * scale)
                {
                    XrInput.Haptic(g.Hand, 0.5f, 0.15f);
                    DropHeld(g);
                    Complete("eaten");
                }

                break;
            }
        }
    }

    /// <summary>Things taken: they stay in your hand while you hold the grip (if the game really took them), and shrink into your pocket when you let go.</summary>
    private static void UpdateTrophies()
    {
        var now = Time.unscaledTime;
        for (var i = Trophies.Count - 1; i >= 0; i--)
        {
            var t = Trophies[i];
            if (t.Copy == null)
            {
                Trophies.RemoveAt(i);
                continue;
            }

            var (pos, rot) = HandGrip(t.Hand);
            if (t.ShrinkFrom < 0)
            {
                t.Copy.transform.SetPositionAndRotation(pos + rot * t.Offset, rot * t.Rot);
                // the game didn't take it after all (it's still there): no copy in the hand
                if (now >= t.CheckAt && t.CheckAt > 0)
                {
                    t.CheckAt = 0;
                    if (t.Original != null && t.Original.gameObject.activeInHierarchy && AnyVisible(t.Original.transform))
                    {
                        Object.Destroy(t.Copy);
                        Trophies.RemoveAt(i);
                        continue;
                    }
                }

                if (Grip(t.Hand) < 0.35f) t.ShrinkFrom = now;
                continue;
            }

            var k = 1f - Mathf.Clamp01((now - t.ShrinkFrom) / 0.25f);
            t.Copy.transform.position = Vector3.Lerp(t.Copy.transform.position, pos, 0.3f);
            t.Copy.transform.localScale = Vector3.one * k;
            if (k <= 0)
            {
                Object.Destroy(t.Copy);
                Trophies.RemoveAt(i);
            }
        }
    }

    private static bool AnyVisible(Transform root)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            if (r.enabled && r.gameObject.activeInHierarchy)
                return true;
        return false;
    }

    /// <summary>A copy of an object's meshes, held in the hand where it was gripped (or, for a placed item, at the palm).</summary>
    private static void CopyIntoHand(Gesture g, Transform src, bool includeHidden, Vector3 gripPos, Quaternion gripRot)
    {
        var root = new GameObject("InkVR_Held");
        Object.DontDestroyOnLoad(root);
        root.transform.SetPositionAndRotation(src.position, src.rotation);
        Bounds b = default;
        var any = false;
        foreach (var r in src.GetComponentsInChildren<MeshRenderer>(includeHidden))
        {
            if (!includeHidden && !r.enabled) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var copy = new GameObject(r.name);
            copy.transform.SetParent(root.transform, false);
            copy.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
            copy.transform.localScale = Vector3.Scale(r.transform.lossyScale, new Vector3(1f / src.lossyScale.x, 1f / src.lossyScale.y, 1f / src.lossyScale.z));
            copy.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            copy.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
            var wb = new Bounds(r.transform.TransformPoint(mf.sharedMesh.bounds.center), Vector3.zero);
            if (!any) b = wb;
            else b.Encapsulate(wb);
            any = true;
        }

        root.transform.localScale = src.lossyScale;
        g.Held = root;
        // Held as it was gripped; a placed item (from its pedestal, out of sight) sits at the palm.
        var scale = VRRig.ActiveScale;
        var palm = gripPos + gripRot * new Vector3(g.Hand == Hand.Right ? -0.03f : 0.03f, -0.02f, 0.05f) * scale;
        var centre = any ? b.center : src.position;
        if (includeHidden) root.transform.position += palm - centre;
        g.HoldRot = Quaternion.Inverse(gripRot) * root.transform.rotation;
        g.HoldOffset = Quaternion.Inverse(gripRot) * (root.transform.position - gripPos);
    }

    private static void Complete(string how)
    {
        var g = _g;
        _g = null;
        if (g?.Item == null) return;
        XrInput.Haptic(g.Hand, 0.5f, 0.08f);
        // The game's own interaction, as if you had pressed the button on it.
        if (g.Ctrl != null && g.Ctrl.Interactable != null && g.Ctrl.Interactable.Pointer == g.Item.Pointer) g.Ctrl.Interact();
        else g.Item.Interact();

        LastResult = $"{g.Kind} {g.Item.name}: {how}";
        Plugin.L.LogInfo($"Physical interaction done: {LastResult}");
    }

    private static void Cancel(string why)
    {
        var g = _g;
        _g = null;
        if (g == null) return;
        if (g.Kind == Kind.Soup)
        {
            PutBack(g);
            DropHeld(g);
        }

        if (g.Kind == Kind.Place) DropHeld(g);

        LastResult = $"{g.Kind} {(g.Item != null ? g.Item.name : "?")}: {why}";
    }

    // ---------------- soup in the hand ----------------

    /// <summary>The can leaves its shelf (hidden) and a copy of it sits in your hand.</summary>
    private static void PickUp(Gesture g)
    {
        var root = new GameObject("InkVR_HeldSoup");
        Object.DontDestroyOnLoad(root);
        var itemT = g.Item.transform;
        foreach (var r in g.Item.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.enabled) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var copy = new GameObject(r.name);
            copy.transform.SetParent(root.transform, false);
            copy.transform.localPosition = Quaternion.Inverse(itemT.rotation) * (r.transform.position - itemT.position);
            copy.transform.localRotation = Quaternion.Inverse(itemT.rotation) * r.transform.rotation;
            copy.transform.localScale = r.transform.lossyScale;
            copy.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var cr = copy.AddComponent<MeshRenderer>();
            cr.sharedMaterials = r.sharedMaterials;
            r.enabled = false;
            g.Hidden.Add(r);
        }

        g.Held = root;
        // Keep the can upright in the hand, as it stood on the shelf (its tilt relative to upright).
        g.LastRot = Quaternion.Inverse(Quaternion.Euler(0, itemT.eulerAngles.y, 0)) * itemT.rotation;
    }

    private static void Carry(Gesture g, Vector3 grip, Quaternion rot)
    {
        if (g.Held == null) return;
        var scale = VRRig.ActiveScale;
        var palm = grip + rot * new Vector3(g.Hand == Hand.Right ? -0.03f : 0.03f, -0.01f, 0.04f) * scale;
        // The can stays upright in your hand, turning with it.
        g.Held.transform.SetPositionAndRotation(palm, Quaternion.Euler(0, rot.eulerAngles.y, 0) * g.LastRot);
        g.Held.transform.localScale = Vector3.one;
    }

    private static void PutBack(Gesture g)
    {
        foreach (var r in g.Hidden)
            if (r != null)
                r.enabled = true;
        g.Hidden.Clear();
    }

    private static void DropHeld(Gesture g)
    {
        if (g.Held != null) Object.Destroy(g.Held);
        g.Held = null;
    }
}
