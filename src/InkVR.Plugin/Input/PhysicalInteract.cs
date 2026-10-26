using System.Collections.Generic;
using InkVR.Body;
using InkVR.Core;
using InkVR.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.Input;

/// <summary>
/// Physical interactions (motion controls part 2). Grip something and the game's interaction waits for your
/// hand to do the real thing, and the thing moves with your hand while you do it:
///   valves and wheels: they turn in your hand as you wind them round (or twist your wrist), with a ratchet in the
///                      controller; let go part way and they stay where you left them, so you can turn hand over hand
///   levers and switches: the handle swings with your pull until it's thrown (let go early and it springs back);
///                      lids lift and drawers slide with a decisive pull
///   Bacon Soup: pick the can up, pull the lid off with your other hand, then tip it to your mouth to eat it
/// Then the game's own interaction runs (its animation, sounds, puzzle logic). X always does the classic instant
/// interaction, and holding a gesture still for 4 s completes it, so nothing can get stuck.
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

        // the part that moves with your hand (a valve's wheel, a lever's handle), as it was when you took hold
        public Transform Moving;
        public Vector3 MovingPos;
        public Quaternion MovingRot;
        public float Spun;

        // a lever: its hinge, and the axis it swings about (found from your first pull)
        public Vector3 Pivot, HingeAxis;
        public bool Swings, HaveAxis;
        public float Swung;

        // a can of soup: its lid, the soup under it, and the other hand pulling the lid
        public GameObject Lid, Soup;
        public bool Open;
        public Hand? LidHand;
        public Vector3 LidRest, CanUp, CanCentre;
        public float AtMouthSince = -1f, NextNudge;

        // settling into a natural hold
        public Quaternion FromRot;
        public Vector3 FromOffset;
        public float SettleFrom = -1f;
    }

    /// <summary>A thing you took, still in your hand after the game has it (it goes into your pocket when you let go).</summary>
    private sealed class Trophy
    {
        public GameObject Copy;
        public Hand Hand;
        public Quaternion Rot;
        public Vector3 Offset;
        public Interactable Original;
        public Vector3 Scale;
        public float CheckAt, ShrinkFrom = -1f;
    }

    /// <summary>A lid pulled off a can, falling where it was flung.</summary>
    private sealed class FlyingLid
    {
        public GameObject Lid;
        public Vector3 Velocity, Spin;
        public float Until;
    }

    private static readonly List<Trophy> Trophies = new();
    private static readonly List<FlyingLid> Lids = new();

    /// <summary>How far each valve has been turned so far (you can let go and carry on).</summary>
    private static readonly Dictionary<System.IntPtr, float> ValveProgress = new();

    private static Gesture _g;

    /// <summary>Dev: what the last gesture did.</summary>
    public static string LastResult = "";

    public static Kind Active => _g?.Kind ?? Kind.None;

    /// <summary>Dev: where the held can's lid is (world), if there is one.</summary>
    public static Vector3? DevLid => _g?.Lid != null ? _g.Lid.transform.position : null;

    /// <summary>Dev: the valve being turned, its pivot and axis, and how far it's turned.</summary>
    public static (Vector3 centre, Vector3 axis, float turned)? DevValve => _g != null && _g.Kind == Kind.Valve ? (_g.Centre, _g.Axis, _g.Turned) : null;

    private static readonly bool[] Claimed = new bool[2];
    private static readonly bool[] WasGripping = new bool[2];
    private static int _claimFrame = -1;

    private const float HoldToComplete = 4f;

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
        if (Backpack.WantsGrip(hand)) return true; // the satchel over your left shoulder, or something out of it
        if (Backpack.Holding(hand)) return true;   // a hand already holding something doesn't press "interact"
        if (WeaponHandling.WantsGrip(hand)) return true; // the gun's front, the axe's handle, or the holster
        if (_g != null)
        {
            // your other hand taking the lid of the can you're holding
            if (_g.Kind == Kind.Soup && hand != _g.Hand && !_g.Open && NearLid(_g, hand))
            {
                _g.LidHand = hand;
                XrInput.Haptic(hand, 0.3f, 0.04f);
                return true;
            }

            return _g.Hand == hand;
        }

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

        // pointing at something further off: take hold of its ink thread (gravity pull)
        if (GravityPull.TryLock(hand)) return true;

        // a door: take hold of it and swing it yourself
        return PhysicalDoors.TryGrab(hand);
    }

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
        if (Word(name, "valve") || Word(name, "wheel") || Word(parent, "valve") || Word(parent, "wheel") || Word(name, "crank")) return Kind.Valve;
        if (Word(name, "lever") || Word(parent, "lever") || Word(name, "switch") || Word(parent, "switch")) return Kind.Lever;
        return Kind.None;
    }

    private static bool Word(string s, string w) => s.Contains(w) && !s.Contains("door") && !s.Contains("panel");

    /// <summary>A lever's handle swings with your hand (a trunk's lid or a drawer just opens once you've pulled).</summary>
    private static bool Swings(Interactable item)
    {
        if (item.TryCast<InteractableTrunk>() != null || item.TryCast<InteractableDrawer>() != null) return false;
        if (item.TryCast<InteractablePowerLever>() != null || item.GetComponentInParent<CH3LeverLight>() != null) return true;
        var name = item.name.ToLowerInvariant();
        var parent = item.transform.parent != null ? item.transform.parent.name.ToLowerInvariant() : "";
        return name.Contains("lever") || parent.Contains("lever") || name.Contains("handle");
    }

    // What the game's pickups are called (it collects them in one go): Chapter 1's dry cells, Chapter 2's keys and its
    // missing valve wheel, Chapter 3's gears, hearts, power cores, thick ink and valve cranks, Chapter 4's nuts and reels...
    private static readonly string[] PickupWords =
    {
        "collect", "gear", "key", "battery", "cell", "item", "pickup", "pick_up", "bone", "heart", "core", "thickink", "thick_ink", "ink_jar",
        "inkjar", "valve", "crank", "wheel", "record", "reel", "nut", "fuse", "plug", "ticket", "token", "coin", "wrench", "spoon", "doll", "book", "film", "tool"
    };

    /// <summary>A small thing the game collects in one go (it disappears into your inventory): you take it in your hand.</summary>
    private static bool Takeable(Interactable item)
    {
        if (!item.m_EnableSingleInteraction) return false;
        var name = item.name.ToLowerInvariant();
        var named = false;
        foreach (var w in PickupWords)
            if (name.Contains(w))
            {
                named = true;
                break;
            }

        if (!named || name.Contains("door") || name.Contains("panel") || name.Contains("lever") || name.Contains("switch") || name.Contains("button")) return false;
        if (item.GetComponentInParent<MeleeWeapon>() != null || item.GetComponentInParent<GunWeapon>() != null || item.GetComponentInParent<ThrowWeapon>() != null) return false;
        // Things that stay where they are (you work them, they don't come with you): valves you turn, the lift's
        // buttons, tape recorders, instruments, switches, lids and drawers.
        if (item.GetComponentInParent<CH1PipeValve>() != null || item.GetComponentInParent<CH3InkPressurePuzzle>() != null || item.GetComponentInParent<CH3LiftButton>() != null
            || item.GetComponentInParent<InteractableSwitchBox>() != null || item.TryCast<InteractablePowerLever>() != null || item.TryCast<InteractableTrunk>() != null
            || item.TryCast<InteractableDrawer>() != null || item.TryCast<InteractableMusicalInstrument>() != null || item.TryCast<InteractableAudio>() != null
            || item.GetComponentInParent<AudioLog>() != null || item.GetComponentInParent<CH3LeverLight>() != null || item.GetComponentInParent<BaseDoorController>() != null)
            return false;
        var any = false;
        foreach (var r in item.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.enabled) continue;
            any = true;
            // a valve or wheel you carry is a hand's size; one built into the pipes is bigger, or part of the level's mesh
            if (r.isPartOfStaticBatch) return false;
            var size = r.bounds.size / VRRig.ActiveScale;
            var max = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (max > ((name.Contains("valve") || name.Contains("wheel") || name.Contains("crank")) ? 0.42f : 0.9f)) return false;
        }

        return any;
    }

    // ---------------- the gesture ----------------

    private static void Begin(Kind kind, Hand hand, Interactable item, InteractableInputController ctrl, bool hiddenToo = false)
    {
        var grip = HandGrip(hand);
        var r = item.GetComponentInChildren<Renderer>();
        var b = r != null ? r.bounds : new Bounds(item.transform.position, Vector3.one * 0.2f);
        var g = new Gesture
        {
            Kind = kind, Hand = hand, Item = item, Ctrl = ctrl, Centre = b.center, StartHand = grip.pos, LastRot = grip.rot,
            Started = Time.unscaledTime, StillSince = Time.unscaledTime, StillAt = grip.pos, StillRot = grip.rot,
        };
        g.Axis = WheelAxis(kind == Kind.Valve ? MovingPart(item, r) : r, item.transform);
        // Cranks and wheels turn about their own pivot (the object's origin), when it's on the object.
        var pivot = item.transform.position;
        var reach = b;
        reach.Expand(0.3f * VRRig.ActiveScale);
        if (kind == Kind.Valve && reach.Contains(pivot)) g.Centre = pivot;
        g.LastVec = grip.pos - g.Centre;
        _g = g;
        XrInput.Haptic(hand, 0.3f, 0.04f);

        if (kind == Kind.Valve)
        {
            ValveProgress.TryGetValue(item.Pointer, out g.Turned);
            g.NextTick = Mathf.Floor(g.Turned / 15f) * 15f + 15f;
            TakeHoldOfMoving(g, MovingPart(item, r));
        }

        if (kind == Kind.Lever && Swings(item))
        {
            var handle = MovingPart(item, r);
            TakeHoldOfMoving(g, handle);
            if (g.Moving != null)
            {
                // The handle hinges at its own origin (where its model is pivoted), if that's on it.
                var mb = handle.bounds;
                mb.Expand(0.25f * VRRig.ActiveScale);
                g.Pivot = mb.Contains(g.Moving.position) ? g.Moving.position : item.transform.position;
                g.Swings = true;
            }
        }

        if (kind == Kind.Soup)
        {
            PickUp(g, grip.pos, grip.rot, hiddenToo);
            Natural(g, grip.rot);
        }
        if (kind == Kind.Place)
        {
            g.Pedestal = item.GetComponentInParent<CH1Pedestal>();
            var collected = g.Pedestal?.m_Collected;
            if (collected != null)
            {
                Backpack.TakeOutByName(collected.name);
                CopyIntoHand(g, collected.transform, true, grip.pos, grip.rot);
                Natural(g, grip.rot);
            }
        }

        if (kind == Kind.Take)
        {
            // into your hand at once; the game takes it (its sound, its inventory) the same moment
            CopyIntoHand(g, item.transform, hiddenToo, grip.pos, grip.rot);
            var copy = g.Held;
            g.Held = null;
            if (Backpack.Enabled) Backpack.TakeIntoHand(copy, hand, item, grip.pos, grip.rot);
            else Trophies.Add(new Trophy { Copy = copy, Hand = hand, Rot = g.HoldRot, Offset = g.HoldOffset, Original = item, Scale = copy.transform.localScale, CheckAt = Time.unscaledTime + 0.35f });
            _g = g;
            Complete("taken");
            return;
        }

        LastResult = $"{kind} {item.name}: started";
        Plugin.L.LogInfo($"Physical interaction: {kind} '{item.name}' with the {hand} hand{(g.Moving != null ? $" (moving '{g.Moving.name}')" : "")}");
    }

    /// <summary>
    /// The part of a lever or valve that moves: the handle, wheel or crank, not the base it's mounted on. The base is
    /// usually merged into the scene's static mesh (the Chapter 1 power lever's housing), the moving part never is.
    /// </summary>
    private static Renderer MovingPart(Interactable item, Renderer first)
    {
        Renderer any = null;
        foreach (var r in item.GetComponentsInChildren<Renderer>())
        {
            if (r.isPartOfStaticBatch || !r.enabled) continue;
            var n = r.name.ToLowerInvariant();
            if (n.Contains("lever") || n.Contains("handle") || n.Contains("crank") || n.Contains("wheel") || n.Contains("valve")) return r;
            any ??= r;
        }

        return any ?? first;
    }

    /// <summary>The part your hand moves: the gripped model, unless the game merged it into the scene's static mesh (then it can't move).</summary>
    private static void TakeHoldOfMoving(Gesture g, Renderer r)
    {
        if (r == null || r.isPartOfStaticBatch) return;
        g.Moving = r.transform;
        g.MovingPos = r.transform.position;
        g.MovingRot = r.transform.rotation;
    }

    /// <summary>
    /// Gravity pull: an item has flown into your hand. It's taken just as if you'd gripped it there (a weapon is
    /// equipped, a can of soup is in your hand to eat). Its own renderers may still be hidden from the flight.
    /// </summary>
    /// <param name="flightHidden">The item's renderers the flight hid: a can of soup keeps them hidden while it's in your hand.</param>
    internal static bool CaughtFromPull(Kind kind, Hand hand, Interactable item, List<Renderer> flightHidden)
    {
        if (_g != null || item == null) return false;
        if (kind == Kind.Take || kind == Kind.Soup)
        {
            Begin(kind, hand, item, GameManager.Instance?.Player?.m_Interaction, hiddenToo: true);
            if (kind == Kind.Soup && _g != null && _g.Kind == Kind.Soup)
            {
                _g.Hidden.AddRange(flightHidden);
                flightHidden.Clear();
            }

            return true;
        }

        var ctrl = GameManager.Instance?.Player?.m_Interaction;
        if (ctrl != null && ctrl.Interactable != null && ctrl.Interactable.Pointer == item.Pointer) ctrl.Interact();
        else item.Interact();
        LastResult = $"pulled {item.name}";
        return true;
    }

    /// <summary>A wheel turns about its thinnest axis.</summary>
    private static Vector3 WheelAxis(Renderer r, Transform t)
    {
        var mf = r != null ? r.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null || (r != null && r.isPartOfStaticBatch)) return t.forward;
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

    private static Vector3 Palm(Hand hand, Vector3 grip, Quaternion rot) =>
        grip + rot * new Vector3(hand == Hand.Right ? -0.03f : 0.03f, -0.01f, 0.04f) * VRRig.ActiveScale;

    public static void Update()
    {
        if (!XrInput.Ready) ClaimGrips(); // dev grips on the mock runtime (no controller sampling there)
        UpdateTrophies();
        UpdateLids();
        UpdateCans();
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
                    ValveProgress[g.Item.Pointer] = g.Turned; // it stays turned this far
                    Cancel("let go");
                    return;
                }

                // Winding the hand round the pivot (a crank, a wheel's rim), or twisting the wrist (a wheel held
                // across its face): whichever turned more, either way round. The wheel turns with it.
                var v = Vector3.ProjectOnPlane(pos - g.Centre, g.Axis);
                var last = Vector3.ProjectOnPlane(g.LastVec, g.Axis);
                var wind = v.magnitude > 0.03f * scale && last.magnitude > 0.03f * scale ? Vector3.SignedAngle(last, v, g.Axis) : 0f;
                (rot * Quaternion.Inverse(g.LastRot)).ToAngleAxis(out var angle, out var axis);
                if (angle > 180f)
                {
                    angle = 360f - angle;
                    axis = -axis;
                }

                var twist = angle * Vector3.Dot(axis, g.Axis);
                if (Mathf.Abs(twist) < 1f) twist = 0f; // tracking jitter
                g.LastVec = pos - g.Centre;
                g.LastRot = rot;
                var step = Mathf.Abs(wind) >= Mathf.Abs(twist) ? wind : twist;
                g.Spun += step;
                g.Turned += Mathf.Abs(step);
                if (g.Moving != null)
                {
                    var q = Quaternion.AngleAxis(g.Spun, g.Axis);
                    g.Moving.SetPositionAndRotation(g.Centre + q * (g.MovingPos - g.Centre), q * g.MovingRot);
                }

                if (g.Turned >= g.NextTick)
                {
                    g.NextTick += 15f;
                    XrInput.Haptic(g.Hand, 0.25f, 0.02f); // the ratchet of the valve
                }

                if (g.Turned >= 110f)
                {
                    ValveProgress.Remove(g.Item.Pointer);
                    Complete("turned");
                }
                else if (Time.unscaledTime - g.StillSince > HoldToComplete) Complete("held");

                break;
            }
            case Kind.Lever:
            {
                if (!held)
                {
                    SpringBack(g);
                    Cancel("let go");
                    return;
                }

                var pulled = (pos - g.StartHand).magnitude / scale;
                if (g.Swings) Swing(g, pos);
                var progress = g.Swings ? g.Swung / 45f : pulled / 0.12f;
                if (progress * 100f > g.NextTick)
                {
                    g.NextTick += 12f;
                    XrInput.Haptic(g.Hand, 0.15f + 0.4f * Mathf.Clamp01(progress), 0.02f);
                }

                if (progress >= 1f)
                {
                    SpringBack(g); // the game's own animation throws it from where it started
                    Complete("pulled");
                }
                else if (Time.unscaledTime - g.StillSince > HoldToComplete)
                {
                    SpringBack(g);
                    Complete("held");
                }

                break;
            }
            case Kind.Place:
            {
                // carry it to the pedestal and set it down there
                PoseHeld(g, pos, rot);
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
                else if (Time.unscaledTime - g.StillSince > HoldToComplete)
                {
                    DropHeld(g);
                    Complete("held");
                }

                break;
            }
            case Kind.Soup:
                Soup(g, pos, rot, held);
                break;
        }
    }

    /// <summary>A lever's handle follows your pull about its hinge (the hinge's axis is set by which way you first pull).</summary>
    private static void Swing(Gesture g, Vector3 hand)
    {
        var scale = VRRig.ActiveScale;
        var from = g.StartHand - g.Pivot;
        var to = hand - g.Pivot;
        if (!g.HaveAxis)
        {
            var move = hand - g.StartHand;
            if (move.magnitude < 0.025f * scale) return;
            var axis = Vector3.Cross(from, move);
            if (axis.sqrMagnitude < 1e-8f) return;
            g.HingeAxis = axis.normalized;
            g.HaveAxis = true;
        }

        var a = Vector3.ProjectOnPlane(from, g.HingeAxis);
        var b = Vector3.ProjectOnPlane(to, g.HingeAxis);
        if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return;
        g.Swung = Mathf.Clamp(Vector3.SignedAngle(a, b, g.HingeAxis), 0f, 70f);
        var q = Quaternion.AngleAxis(g.Swung, g.HingeAxis);
        g.Moving.SetPositionAndRotation(g.Pivot + q * (g.MovingPos - g.Pivot), q * g.MovingRot);
    }

    private static void SpringBack(Gesture g)
    {
        if (!g.Swings || g.Moving == null) return;
        g.Moving.SetPositionAndRotation(g.MovingPos, g.MovingRot);
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
            t.Copy.transform.localScale = t.Scale * Mathf.Max(k, 1e-4f);
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
        root.transform.localScale = src.lossyScale;
        var any = MeshCopy.AddAll(src, root.transform, includeHidden, out var b);
        g.Held = root;
        // Held as it was gripped; a placed item (from its pedestal, out of sight) sits at the palm.
        var palm = Palm(g.Hand, gripPos, gripRot);
        var centre = any ? b.center : src.position;
        if (includeHidden) root.transform.position += palm - centre;
        g.HoldRot = Quaternion.Inverse(gripRot) * root.transform.rotation;
        g.HoldOffset = Quaternion.Inverse(gripRot) * (root.transform.position - gripPos);
    }

    private static void Complete(string how)
    {
        var g = _g;
        _g = null;
        if (g != null) Grips.Holding(g.Hand, 0);
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
        Grips.Holding(g.Hand, 0);
        if (g.Kind == Kind.Soup)
        {
            PutBack(g);
            DropHeld(g);
        }

        if (g.Kind == Kind.Place) DropHeld(g);

        LastResult = $"{g.Kind} {(g.Item != null ? g.Item.name : "?")}: {why}";
    }

    // ---------------- soup: take it, open it, drink it ----------------

    /// <summary>The can leaves its shelf (hidden) and a copy of it is in your hand, as you took it, with its lid on.</summary>
    private static void PickUp(Gesture g, Vector3 grip, Quaternion gripRot, bool fromPull)
    {
        var itemT = g.Item.transform;
        var root = new GameObject("InkVR_HeldSoup");
        Object.DontDestroyOnLoad(root);
        root.transform.SetPositionAndRotation(itemT.position, itemT.rotation);
        // (pulled to you, the can's own renderers are already hidden by the flight)
        if (!MeshCopy.AddAll(itemT, root.transform, fromPull, out var b, fromPull ? null : g.Hidden)) b = new Bounds(itemT.position, Vector3.one * 0.1f * VRRig.ActiveScale);
        g.Held = root;

        // Held as you took it: the can's centre at your palm, turned as it was against your hand.
        var palm = Palm(g.Hand, grip, gripRot);
        root.transform.position += palm - b.center;
        g.HoldRot = Quaternion.Inverse(gripRot) * root.transform.rotation;
        g.HoldOffset = Quaternion.Inverse(gripRot) * (root.transform.position - grip);

        // Its lid (and the soup under it), on top of the can as it stood on the shelf.
        var up = Quaternion.Inverse(root.transform.rotation) * Vector3.up;
        var centre = root.transform.InverseTransformPoint(palm);
        var radius = Mathf.Min(b.extents.x, b.extents.z) * 0.86f;
        var top = centre + up * (b.extents.y + 0.002f * VRRig.ActiveScale);
        g.CanUp = up;
        g.CanCentre = centre;
        g.LidRest = top;
        g.Soup = Disc(root.transform, top - up * (0.0015f * VRRig.ActiveScale), up, radius * 0.94f, 0.0008f, SoupMaterial, "InkVR_SoupInside");
        g.Lid = Disc(root.transform, top, up, radius, 0.0035f, LidMaterial, "InkVR_SoupLid");
    }

    private static GameObject Disc(Transform parent, Vector3 localCentre, Vector3 localUp, float radius, float thickness, Material mat, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localCentre;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, localUp);
        var mb = new MeshBuilder();
        mb.Ellipsoid(Vector3.zero, new Vector3(radius, thickness * VRRig.ActiveScale, radius), Quaternion.identity, 24, 6);
        go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(name);
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    private static Material _lidMat, _soupMat;

    private static Material LidMaterial => _lidMat ??= InkMaterials.GameSurface(LidTexture(), "InkVR_SoupLid");
    private static Material SoupMaterial => _soupMat ??= InkMaterials.GameSurface(HenryTextures.Flat(new Color(0.42f, 0.25f, 0.13f), "InkVR_SoupInside"), "InkVR_SoupInside");

    /// <summary>A tin lid: grey metal, a pressed ring, a ring-pull tab, an ink outline.</summary>
    private static Texture2D LidTexture()
    {
        var c = new UI.InkCanvas(64, 64, new Color(0.7f, 0.68f, 0.62f), 41);
        for (var a = 0f; a < 6.3f; a += 0.03f)
        {
            c.Dot(32 + Mathf.Cos(a) * 30, 32 + Mathf.Sin(a) * 30, 1.6f, HenryTextures.Ink, 0.9f);
            c.Dot(32 + Mathf.Cos(a) * 22, 32 + Mathf.Sin(a) * 22, 0.9f, HenryTextures.Ink, 0.4f);
        }

        c.Stroke(32, 32, 32, 46, 5f, new Color(0.55f, 0.53f, 0.48f));
        for (var a = 0f; a < 6.3f; a += 0.1f) c.Dot(32 + Mathf.Cos(a) * 6, 46 + Mathf.Sin(a) * 5, 1f, HenryTextures.Ink, 0.8f);
        c.Grain(0.12f);
        return c.ToTexture("InkVR_SoupLid", false);
    }

    private static bool NearLid(Gesture g, Hand hand)
    {
        if (g.Lid == null) return false;
        var (pos, rot) = HandGrip(hand);
        return (Palm(hand, pos, rot) - g.Lid.transform.position).magnitude < 0.11f * VRRig.ActiveScale;
    }

    private static void Soup(Gesture g, Vector3 pos, Quaternion rot, bool held)
    {
        var scale = VRRig.ActiveScale;
        PoseHeld(g, pos, rot);
        if (!held)
        {
            // let go mid-swing: the can flies, and lands where it lands (it's still a can of soup there)
            var v = WeaponHandling.Velocity(g.Hand);
            if (ModConfig.PhysicalThrow.Value && v.magnitude / scale > 1.5f && g.Held != null && !g.Open)
            {
                ThrowCan(g, v);
                Cancel("thrown");
                return;
            }

            PutBack(g);
            Cancel("put back");
            return;
        }

        // The other hand pulling the lid: it comes with that hand once it's pulled clear, and you can fling it away.
        if (!g.Open && g.LidHand.HasValue && g.Lid != null)
        {
            var lh = g.LidHand.Value;
            var (lp, lr) = HandGrip(lh);
            var lidPalm = Palm(lh, lp, lr);
            var rest = g.Held.transform.TransformPoint(g.LidRest);
            if (Grip(lh) < 0.35f)
            {
                g.LidHand = null; // let go of it before it came off: still on
            }
            else if ((lidPalm - rest).magnitude > 0.07f * scale)
            {
                OpenCan(g, lh);
            }
        }

        var head = VRRig.Head;
        if (head == null) return;
        var mouth = head.position + head.rotation * new Vector3(0, -0.085f, 0.07f) * scale;
        var can = g.Held != null ? g.Held.transform.TransformPoint(g.CanCentre) : pos;
        var atMouth = (can - mouth).magnitude < 0.14f * scale;
        if (!atMouth)
        {
            g.AtMouthSince = -1f;
            return;
        }

        var now = Time.unscaledTime;
        if (g.AtMouthSince < 0) g.AtMouthSince = now;
        if (!g.Open)
        {
            // Still sealed: a nudge in the hand. (After a few seconds at your mouth it pops open for you anyway.)
            if (now >= g.NextNudge)
            {
                g.NextNudge = now + 0.5f;
                XrInput.Haptic(g.Hand, 0.25f, 0.03f);
            }

            if (now - g.AtMouthSince > 2.5f) OpenCan(g, g.Hand);
            return;
        }

        // Tipped up to your mouth (or held there a moment): down it goes.
        var tilt = Vector3.Angle(g.Held.transform.TransformDirection(g.CanUp), Vector3.up);
        if (tilt > 30f || now - g.AtMouthSince > 0.8f)
        {
            XrInput.Haptic(g.Hand, 0.5f, 0.15f);
            DropHeld(g);
            Complete("eaten");
        }
    }

    private static void OpenCan(Gesture g, Hand by)
    {
        g.Open = true;
        g.LidHand = null;
        XrInput.Haptic(g.Hand, 0.45f, 0.05f);
        if (by != g.Hand) XrInput.Haptic(by, 0.45f, 0.05f);
        if (g.Lid == null) return;
        // the lid flies off the way the hand pulled it
        var lid = g.Lid;
        g.Lid = null;
        lid.transform.SetParent(null, true);
        Object.DontDestroyOnLoad(lid);
        var v = WeaponHandling.Velocity(by);
        if (v.magnitude < 0.6f * VRRig.ActiveScale) v = (lid.transform.up + Random.insideUnitSphere * 0.4f) * (1.2f * VRRig.ActiveScale);
        Lids.Add(new FlyingLid { Lid = lid, Velocity = v, Spin = Random.onUnitSphere * 720f, Until = Time.unscaledTime + 1.6f });
        Plugin.L.LogInfo($"Soup: the lid is off ({(by == g.Hand ? "popped" : "pulled with the other hand")})");
    }

    private static void UpdateLids()
    {
        var dt = Time.unscaledDeltaTime;
        for (var i = Lids.Count - 1; i >= 0; i--)
        {
            var l = Lids[i];
            if (l.Lid == null || Time.unscaledTime > l.Until)
            {
                if (l.Lid != null) Object.Destroy(l.Lid);
                Lids.RemoveAt(i);
                continue;
            }

            l.Velocity += Physics.gravity * dt;
            l.Lid.transform.position += l.Velocity * dt;
            l.Lid.transform.rotation = Quaternion.Euler(l.Spin * dt) * l.Lid.transform.rotation;
        }
    }

    // ---------------- natural holds ----------------

    /// <summary>From how you took it, it settles into a natural hold (Grips.CarryPose) over a moment; the fingers close on it.</summary>
    private static void Natural(Gesture g, Quaternion gripRot)
    {
        if (g.Held == null || !ModConfig.NaturalGrips.Value) return;
        g.FromRot = g.HoldRot;
        g.FromOffset = g.HoldOffset;
        var radius = Grips.CarryPose(g.Held.transform, gripRot, g.Hand == Hand.Left, out var rot, out var offset);
        g.HoldRot = rot;
        g.HoldOffset = offset;
        g.SettleFrom = Time.unscaledTime;
        Grips.Holding(g.Hand, radius);
    }

    private static void PoseHeld(Gesture g, Vector3 pos, Quaternion rot)
    {
        if (g.Held == null) return;
        var s = g.SettleFrom < 0 ? 1f : Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.unscaledTime - g.SettleFrom) / 0.18f));
        var hr = s >= 1f ? g.HoldRot : Quaternion.Slerp(g.FromRot, g.HoldRot, s);
        var ho = s >= 1f ? g.HoldOffset : Vector3.Lerp(g.FromOffset, g.HoldOffset, s);
        g.Held.transform.SetPositionAndRotation(pos + rot * ho, rot * hr);
    }

    // ---------------- a thrown can ----------------

    private sealed class ThrownCan
    {
        public GameObject Copy;
        public Interactable Can;
        public List<Renderer> Hidden;
        public Vector3 Velocity, Spin;
        public float Radius, Started;
    }

    private static readonly List<ThrownCan> Cans = new();

    private static void ThrowCan(Gesture g, Vector3 v)
    {
        var copy = g.Held;
        g.Held = null;
        g.Lid = null; // (still on: it flies with the can)
        var r = copy.GetComponentInChildren<Renderer>();
        var radius = r != null ? Mathf.Min(r.bounds.extents.x, r.bounds.extents.z) : 0.04f * VRRig.ActiveScale;
        Cans.Add(new ThrownCan { Copy = copy, Can = g.Item, Hidden = new List<Renderer>(g.Hidden), Velocity = v * 1.15f, Spin = Random.onUnitSphere * 600f, Radius = radius, Started = Time.unscaledTime });
        g.Hidden.Clear();
        XrInput.Haptic(g.Hand, 0.35f, 0.05f);
        Plugin.L.LogInfo($"Soup: thrown at {v.magnitude / VRRig.ActiveScale:F1} m/s");
    }

    /// <summary>Thrown cans fly, bounce, and come to rest; there the real can is put down (you can pick it up again).</summary>
    private static void UpdateCans()
    {
        var dt = Time.unscaledDeltaTime;
        var k = VRRig.ActiveScale;
        for (var i = Cans.Count - 1; i >= 0; i--)
        {
            var c = Cans[i];
            if (c.Copy == null)
            {
                Cans.RemoveAt(i);
                continue;
            }

            var t = c.Copy.transform;
            c.Velocity += Physics.gravity * dt;
            var move = c.Velocity * dt;
            var len = move.magnitude;
            var player = GameManager.Instance?.Player;
            var mask = Physics.DefaultRaycastLayers & ~UI.VRLayers.UIMask & (player != null ? ~(1 << player.gameObject.layer) : ~0);
            var rest = Time.unscaledTime - c.Started > 6f;
            if (len > 1e-6f && Physics.SphereCast(t.position, Mathf.Max(0.01f * k, c.Radius * 0.8f), move / len, out var hit, len, mask, QueryTriggerInteraction.Ignore))
            {
                t.position = hit.point + hit.normal * Mathf.Max(0.01f * k, c.Radius);
                c.Velocity = Vector3.Reflect(c.Velocity, hit.normal) * 0.3f;
                c.Spin *= 0.35f;
                InkPool.InkSound(t.position, 0.12f);
                if (Mathf.Abs(Vector3.Dot(c.Velocity, hit.normal)) < 0.7f * k) rest = true;
            }
            else t.position += move;

            t.rotation = Quaternion.Euler(c.Spin * dt) * t.rotation;
            if (!rest) continue;

            // down it comes: upright on whatever it landed on, and the real can is there
            var up = Vector3.up;
            var fwd = Vector3.ProjectOnPlane(t.forward, up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(t.up, up);
            if (c.Can != null)
            {
                c.Can.transform.SetPositionAndRotation(t.position, Quaternion.LookRotation(fwd.normalized, up));
                foreach (var rr in c.Hidden)
                    if (rr != null)
                        rr.enabled = true;
            }

            Object.Destroy(c.Copy);
            Cans.RemoveAt(i);
        }
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
        g.Lid = null;
        g.Soup = null;
    }
}
