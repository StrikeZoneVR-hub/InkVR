using System.Collections.Generic;
using InkVR.Body;
using InkVR.Core;
using InkVR.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.Input;

/// <summary>
/// Henry's satchel, over your left shoulder (the off-hand side), like a native VR game's (0.10.0, a tester's idea).
///   Carry: what you pick up stays in your hand (the game has already taken it). Let go of it over your left shoulder
///          and it goes in the satchel; let go anywhere else and it finds its own way there.
///   Throw: let go of it mid-swing and it flies, bounces and lands, then melts back into ink and into the satchel (a
///          quest item can never be lost).
///   Take out: reach over your left shoulder with either hand and grip: the satchel comes round in front of you,
///          open, what's in it held up out of it. Take something with your other hand; let go of the satchel and it
///          goes back over your shoulder.
///   Use: let go of what you're holding on the thing it's for (a pedestal, a slot, Alice's door) and the game takes
///          it from there, as if you'd pressed the button on it.
/// When your objective moves on, what you were carrying for it has been handed over: the satchel empties.
/// </summary>
public static class Backpack
{
    private sealed class Item
    {
        public GameObject Copy;
        public string Name;
        public Vector3 Scale;
    }

    private sealed class InHand
    {
        public Item Item;
        public Hand Hand;
        public Quaternion Rot, FromRot;   // grip space
        public Vector3 Offset, FromOffset;
        public float Radius, Since;
        public Interactable Original;
        public float CheckAt;
    }

    private sealed class Flying
    {
        public Item Item;
        public Vector3 Velocity, Spin;
        public float RestSince = -1f, Radius, Started;
        public bool Home; // flying back to the shoulder (not thrown)
        public Vector3 From;
    }

    private static readonly List<Item> Bag = new();
    private static readonly InHand[] Held = new InHand[2];
    private static readonly List<Flying> Air = new();
    private static readonly bool[] WasGripping = new bool[2];
    private static Transform _root;
    private static string _objective;

    // the satchel out in a hand
    private static Hand? _bagHand;
    private static Transform _satchel;
    private static float _bagOutAt;
    private static readonly List<(Item item, Vector3 slot)> Shown = new();

    /// <summary>Dev: what's in the satchel and in each hand.</summary>
    public static string Describe()
    {
        var names = new List<string>();
        foreach (var i in Bag) names.Add(i.Name);
        return $"bag [{string.Join(", ", names)}] left={Held[0]?.Item.Name ?? "-"} right={Held[1]?.Item.Name ?? "-"} flying={Air.Count} out={_bagHand?.ToString() ?? "no"}";
    }

    public static bool Enabled => ModConfig.Backpack.Value;

    private static Transform Root
    {
        get
        {
            if (_root == null)
            {
                var go = new GameObject("InkVR_Backpack");
                Object.DontDestroyOnLoad(go);
                _root = go.transform;
            }

            return _root;
        }
    }

    // ---------------- taking things ----------------

    /// <summary>A thing just taken (the game has it): its copy is now in your hand, settling into a natural hold.</summary>
    public static void TakeIntoHand(GameObject copy, Hand hand, Interactable original, Vector3 gripPos, Quaternion gripRot)
    {
        var item = new Item { Copy = copy, Name = original != null ? original.name : copy.name, Scale = copy.transform.localScale };
        Hold(item, hand, gripPos, gripRot, original);
    }

    private static void Hold(Item item, Hand hand, Vector3 gripPos, Quaternion gripRot, Interactable original = null)
    {
        Drop(hand); // whatever was there goes home
        var t = item.Copy.transform;
        t.SetParent(null, true);
        item.Copy.SetActive(true);
        var radius = Grips.CarryPose(t, gripRot, hand == Hand.Left, out var rot, out var offset);
        Held[(int)hand] = new InHand
        {
            Item = item, Hand = hand, Rot = rot, Offset = offset, Radius = radius, Since = Time.unscaledTime, Original = original,
            FromRot = Quaternion.Inverse(gripRot) * t.rotation, FromOffset = Quaternion.Inverse(gripRot) * (t.position - gripPos),
            CheckAt = original != null ? Time.unscaledTime + 0.35f : 0f
        };
        Grips.Holding(hand, radius);
    }

    /// <summary>Is this hand holding something taken (or out of the satchel)?</summary>
    public static bool Holding(Hand hand) => Held[(int)hand] != null;

    /// <summary>A Chapter 1 offering being carried back to its pedestal: if it's in the satchel, it comes out of it.</summary>
    public static void TakeOutByName(string name)
    {
        for (var i = Bag.Count - 1; i >= 0; i--)
        {
            if (!SameThing(Bag[i].Name, name)) continue;
            if (Bag[i].Copy != null) Object.Destroy(Bag[i].Copy);
            Bag.RemoveAt(i);
            return;
        }
    }

    private static bool SameThing(string a, string b)
    {
        string Clean(string s) => s.Replace("(Clone)", "").Trim().ToLowerInvariant();
        return Clean(a) == Clean(b);
    }

    // ---------------- grips ----------------

    /// <summary>Called where grips become the game's interact: a grip over the left shoulder (the satchel), or the other hand taking something out of the open satchel.</summary>
    public static bool WantsGrip(Hand hand)
    {
        if (!Enabled || !VRRig.InGame || VRRig.CinemaActive || Held[(int)hand] != null) return false;
        if (_bagHand.HasValue && hand != _bagHand.Value) return NearestShown(GripOf(hand).pos, out _) >= 0;
        return !_bagHand.HasValue && AtShoulder(GripOf(hand).pos);
    }

    /// <summary>Over the left shoulder (the off hand's side), a little behind: where the satchel hangs.</summary>
    public static bool AtShoulder(Vector3 p)
    {
        var head = VRRig.Head;
        if (head == null) return false;
        var k = VRRig.ActiveScale;
        var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, fwd);
        var side = ControllerBindings.OffHand == Hand.Left ? -1f : 1f;
        var spot = head.position + right * (0.15f * side * k) - fwd * (0.13f * k) - Vector3.up * (0.12f * k);
        return (p - spot).magnitude < 0.22f * k;
    }

    private static (Vector3 pos, Quaternion rot) GripOf(Hand hand)
    {
        var dev = hand == Hand.Right ? Hands.DevRight : Hands.DevLeft;
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, dev?.rot ?? c.GripRotation, out var p, out var r);
        return (p, r);
    }

    private static float GripValue(Hand hand) => (hand == Hand.Right ? XrInput.DevRightGrip : XrInput.DevLeftGrip) ?? XrInput.Get(hand).Grip;

    // ---------------- every frame ----------------

    public static void Update()
    {
        if (!XrManager.IsRunning) return;
        if (!VRRig.InGame || VRRig.CinemaActive)
        {
            PutAway();
            return;
        }

        CheckObjective();
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var h = (int)hand;
            var gripping = GripValue(hand) > 0.6f;
            var pressed = gripping && !WasGripping[h];
            WasGripping[h] = gripping;
            if (Held[h] != null) UpdateHeld(Held[h], GripValue(hand) > 0.35f);
            else if (pressed && Enabled) Press(hand);
        }

        UpdateSatchel();
        UpdateAir();
    }

    private static void Press(Hand hand)
    {
        var (pos, rot) = GripOf(hand);
        if (_bagHand.HasValue && hand != _bagHand.Value)
        {
            // taking something out of the open satchel
            var i = NearestShown(pos, out var item);
            if (i < 0) return;
            Bag.Remove(item);
            Shown.RemoveAt(i);
            item.Copy.transform.localScale = item.Scale;
            Hold(item, hand, pos, rot);
            XrInput.Haptic(hand, 0.35f, 0.05f);
            Plugin.L.LogInfo($"Satchel: took out {item.Name}");
            return;
        }

        if (!_bagHand.HasValue && AtShoulder(pos))
        {
            _bagHand = hand;
            _bagOutAt = Time.unscaledTime;
            BuildSatchel();
            _satchel.gameObject.SetActive(true);
            XrInput.Haptic(hand, 0.4f, 0.06f);
            InkPool.InkSound(pos, 0.12f);
            Plugin.L.LogInfo($"Satchel: out, {Bag.Count} things in it");
        }
    }

    private static void UpdateHeld(InHand held, bool gripping)
    {
        var item = held.Item;
        if (item.Copy == null)
        {
            Held[(int)held.Hand] = null;
            Grips.Holding(held.Hand, 0);
            return;
        }

        var (pos, rot) = GripOf(held.Hand);
        var k = VRRig.ActiveScale;
        // settling from how you took it into a natural hold
        var s = Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.unscaledTime - held.Since) / 0.18f));
        var hold = Quaternion.Slerp(held.FromRot, held.Rot, s);
        var offset = Vector3.Lerp(held.FromOffset, held.Offset, s);
        item.Copy.transform.SetPositionAndRotation(pos + rot * offset, rot * hold);

        // the game didn't take it after all (it's still there): no copy in the hand
        if (held.CheckAt > 0 && Time.unscaledTime >= held.CheckAt)
        {
            held.CheckAt = 0;
            if (held.Original != null && held.Original.gameObject.activeInHierarchy && AnyVisible(held.Original.transform))
            {
                Object.Destroy(item.Copy);
                Held[(int)held.Hand] = null;
                Grips.Holding(held.Hand, 0);
                return;
            }
        }

        if (gripping) return;

        // let go: into the satchel, thrown, used on something, or off home by itself
        Held[(int)held.Hand] = null;
        Grips.Holding(held.Hand, 0);
        var v = WeaponHandling.Velocity(held.Hand);
        var speed = v.magnitude / k;
        if (AtShoulder(pos))
        {
            Store(item);
            XrInput.Haptic(held.Hand, 0.4f, 0.06f);
            Plugin.L.LogInfo($"Satchel: put {item.Name} in");
        }
        else if (speed > 1.5f && ModConfig.PhysicalThrow.Value)
        {
            Air.Add(new Flying { Item = item, Velocity = v * 1.15f, Spin = Random.onUnitSphere * Mathf.Min(900f, speed * 180f), Radius = held.Radius * k, Started = Time.unscaledTime });
            XrInput.Haptic(held.Hand, 0.35f, 0.05f);
            Plugin.L.LogInfo($"Satchel: threw {item.Name} at {speed:F1} m/s");
        }
        else if (InteractionAim.HoverHand == held.Hand && UseTarget(InteractionAim.Hovered, held.Original))
        {
            UseOn(InteractionAim.Hovered, item, held.Hand);
        }
        else
        {
            GoHome(item);
        }
    }

    /// <summary>Something your held item could be for: anything interactive that isn't itself a pickup (a pedestal, a slot, a door, a machine).</summary>
    private static bool UseTarget(Interactable target, Interactable original)
    {
        if (target == null || !target.isActiveAndEnabled) return false;
        if (original != null && target.Pointer == original.Pointer) return false;
        var kind = PhysicalInteract.Classify(target);
        return kind != PhysicalInteract.Kind.Take && kind != PhysicalInteract.Kind.Soup;
    }

    private static void UseOn(Interactable target, Item item, Hand hand)
    {
        var ctrl = GameManager.Instance?.Player?.m_Interaction;
        if (ctrl != null && ctrl.Interactable != null && ctrl.Interactable.Pointer == target.Pointer) ctrl.Interact();
        else target.Interact();
        XrInput.Haptic(hand, 0.5f, 0.08f);
        Plugin.L.LogInfo($"Satchel: used {item.Name} on {target.name}");
        // it goes in, then home (if it was handed over, the next objective empties the satchel of it)
        GoHome(item, Centre(target.transform));
    }

    private static Vector3 Centre(Transform t)
    {
        var r = t.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds.center : t.position;
    }

    private static void Store(Item item)
    {
        if (item.Copy == null) return;
        item.Copy.transform.SetParent(Root, false);
        item.Copy.SetActive(false);
        if (!Bag.Contains(item)) Bag.Add(item);
    }

    /// <summary>It flies back over your shoulder into the satchel (from where it is, or from a spot it went into first).</summary>
    private static void GoHome(Item item, Vector3? via = null)
    {
        Air.Add(new Flying { Item = item, Home = true, From = via ?? item.Copy.transform.position, Started = Time.unscaledTime });
    }

    /// <summary>Holding something, and let go of the grip in a cutscene or a menu: it goes in the satchel.</summary>
    private static void PutAway()
    {
        for (var h = 0; h < 2; h++)
        {
            if (Held[h] == null) continue;
            Store(Held[h].Item);
            Grips.Holding((Hand)h, 0);
            Held[h] = null;
        }

        CloseSatchel();
    }

    private static void Drop(Hand hand)
    {
        var held = Held[(int)hand];
        if (held == null) return;
        Held[(int)hand] = null;
        GoHome(held.Item);
    }

    // ---------------- in the air ----------------

    private static void UpdateAir()
    {
        var dt = Time.unscaledDeltaTime;
        var now = Time.unscaledTime;
        var k = VRRig.ActiveScale;
        for (var i = Air.Count - 1; i >= 0; i--)
        {
            var f = Air[i];
            var t = f.Item.Copy != null ? f.Item.Copy.transform : null;
            if (t == null)
            {
                Air.RemoveAt(i);
                continue;
            }

            if (f.Home)
            {
                // a short arc to the left shoulder, shrinking into the satchel
                var head = VRRig.Head;
                var u = Mathf.Clamp01((now - f.Started) / 0.4f);
                var shoulder = head != null ? head.position - head.right * (0.15f * k) - Vector3.up * (0.12f * k) - Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized * (0.12f * k) : f.From;
                var mid = Vector3.Lerp(f.From, shoulder, 0.5f) + Vector3.up * (0.12f * k);
                var a = Vector3.Lerp(f.From, mid, u);
                var b = Vector3.Lerp(mid, shoulder, u);
                t.position = Vector3.Lerp(a, b, u);
                t.localScale = f.Item.Scale * Mathf.Max(0.05f, 1f - u * u);
                if (u >= 1f)
                {
                    t.localScale = f.Item.Scale;
                    Store(f.Item);
                    Air.RemoveAt(i);
                }

                continue;
            }

            if (f.RestSince < 0)
            {
                // thrown: gravity, and it bounces off what it hits
                f.Velocity += Physics.gravity * dt;
                var move = f.Velocity * dt;
                var len = move.magnitude;
                var mask = Physics.DefaultRaycastLayers & ~VRLayersMask();
                if (len > 1e-6f && Physics.SphereCast(t.position, Mathf.Max(0.01f * k, f.Radius * 0.8f), move / len, out var hit, len, mask, QueryTriggerInteraction.Ignore))
                {
                    t.position = hit.point + hit.normal * Mathf.Max(0.01f * k, f.Radius);
                    f.Velocity = Vector3.Reflect(f.Velocity, hit.normal) * 0.32f;
                    f.Spin *= 0.4f;
                    if (Mathf.Abs(Vector3.Dot(f.Velocity, hit.normal)) < 0.6f * k)
                    {
                        f.RestSince = now;
                        InkPool.InkSound(t.position, 0.2f);
                    }
                }
                else
                {
                    t.position += move;
                }

                t.rotation = Quaternion.Euler(f.Spin * dt) * t.rotation;
                if (now - f.Started > 6f) f.RestSince = now; // fell somewhere it never landed
                continue;
            }

            // landed: a moment where it fell, then it melts away into the satchel
            var since = now - f.RestSince;
            if (since > 1.1f)
            {
                var m = Mathf.Clamp01((since - 1.1f) / 0.4f);
                t.localScale = f.Item.Scale * Mathf.Max(0.02f, 1f - m);
                if (m >= 1f)
                {
                    t.localScale = f.Item.Scale;
                    Store(f.Item);
                    Air.RemoveAt(i);
                    Plugin.L.LogInfo($"Satchel: {f.Item.Name} came back");
                }
            }
        }
    }

    private static int VRLayersMask()
    {
        var player = GameManager.Instance?.Player;
        var m = UI.VRLayers.UIMask;
        if (player != null) m |= 1 << player.gameObject.layer;
        return m;
    }

    // ---------------- the satchel ----------------

    private static void UpdateSatchel()
    {
        if (!_bagHand.HasValue) return;
        var hand = _bagHand.Value;
        if (GripValue(hand) < 0.35f)
        {
            // let go: back over your shoulder
            XrInput.Haptic(hand, 0.25f, 0.04f);
            Plugin.L.LogInfo("Satchel: back over your shoulder");
            CloseSatchel();
            return;
        }

        var (pos, rot) = GripOf(hand);
        var k = VRRig.ActiveScale;
        var head = VRRig.Head;
        // held by its top, hanging below your hand, its open side towards you
        var toHead = head != null ? Vector3.ProjectOnPlane(head.position - pos, Vector3.up).normalized : Vector3.back;
        if (toHead.sqrMagnitude < 1e-4f) toHead = Vector3.back;
        _satchel.SetPositionAndRotation(pos - Vector3.up * (0.05f * k), Quaternion.LookRotation(-toHead, Vector3.up));
        var grow = Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.unscaledTime - _bagOutAt) / 0.2f));
        _satchel.localScale = Vector3.one * (k * grow);

        // what's in it, held up out of its mouth in a row, turning slowly
        Shown.Clear();
        var n = Bag.Count;
        for (var i = 0; i < n; i++)
        {
            var item = Bag[i];
            if (item.Copy == null) continue;
            // a row floating just in front of the bag, on your side (two rows when it's full), clear of the hand on the strap
            var row = n > 4 ? i / 4 : 0;
            var inRow = n > 4 ? Mathf.Min(4, n - row * 4) : n;
            var x = (i % 4 - (inRow - 1) * 0.5f) * 0.12f;
            var slot = _satchel.TransformPoint(new Vector3(x, -0.07f - row * 0.11f + 0.01f * Mathf.Sin(Time.unscaledTime * 2f + i), -0.15f) * grow);
            var t = item.Copy.transform;
            if (!item.Copy.activeSelf)
            {
                item.Copy.SetActive(true);
                t.SetParent(null, true);
            }

            // shrunk to fit (no bigger than 11 cm)
            var size = SizeOf(item);
            var fit = size > 1e-4f ? Mathf.Min(1f, 0.11f * k / size) : 1f;
            t.localScale = item.Scale * (fit * Mathf.Max(0.05f, grow));
            t.rotation = Quaternion.Euler(0, Time.unscaledTime * 40f + i * 60f, 0) * Quaternion.Euler(-20, 0, 0);
            t.position = slot;
            Shown.Add((item, slot));
        }
    }

    private static float SizeOf(Item item)
    {
        var any = false;
        var b = new Bounds();
        foreach (var r in item.Copy.GetComponentsInChildren<Renderer>())
        {
            if (!any)
            {
                b = r.bounds;
                any = true;
            }
            else b.Encapsulate(r.bounds);
        }

        // measured at its current scale: scale back to full size
        var cur = item.Copy.transform.localScale.x;
        return any && cur > 1e-6f ? Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) * (item.Scale.x / cur) : 0f;
    }

    private static int NearestShown(Vector3 p, out Item item)
    {
        item = null;
        var best = -1;
        var bestD = 0.11f * VRRig.ActiveScale;
        for (var i = 0; i < Shown.Count; i++)
        {
            var d = (Shown[i].slot - p).magnitude;
            if (d >= bestD) continue;
            bestD = d;
            best = i;
            item = Shown[i].item;
        }

        return best;
    }

    private static void CloseSatchel()
    {
        if (_satchel != null) _satchel.gameObject.SetActive(false);
        _bagHand = null;
        foreach (var (item, _) in Shown)
        {
            if (item.Copy == null) continue;
            item.Copy.transform.localScale = item.Scale;
            Store(item);
        }

        Shown.Clear();
        foreach (var item in Bag)
            if (item.Copy != null && item.Copy.activeSelf)
                Store(item);
    }

    /// <summary>
    /// A leather satchel in the studio's drawn style: a rounded body, its flap thrown back open, a buckle, the strap
    /// you're holding it by, all with an ink line round them. Built once.
    /// </summary>
    private static void BuildSatchel()
    {
        if (_satchel != null) return;
        var go = new GameObject("InkVR_Satchel");
        Object.DontDestroyOnLoad(go);
        _satchel = go.transform;
        var leather = InkMaterials.GameSurface(LeatherTexture(), "InkVR_SatchelLeather");
        var brass = InkMaterials.GameSurface(HenryTextures.Flat(new Color(0.78f, 0.6f, 0.27f), "InkVR_SatchelBrass"), "InkVR_SatchelBrass");
        var dark = InkMaterials.GameSurface(HenryTextures.Flat(new Color(0.12f, 0.08f, 0.05f), "InkVR_SatchelInside"), "InkVR_SatchelInside");

        // body: hangs below the hand (its mouth at y = -0.03), soft leather that bulges in the middle and rounds off
        // at the corners and the bottom (rings of a rounded rectangle, lofted from the bottom up, open at the top)
        const int rings = 9, around = 32;
        var pts = new Vector3[rings, around + 1];
        var centres = new Vector3[rings];
        for (var k = 0; k < rings; k++)
        {
            var t = k / (float)(rings - 1);
            var y = Mathf.Lerp(-0.25f, -0.03f, t);
            var bulge = Mathf.Sin(Mathf.Lerp(0.35f, 1f, t) * Mathf.PI * 0.62f);  // fuller in the middle, rounded underneath
            var a = Mathf.Lerp(0.115f, 0.138f, bulge) * (t < 0.12f ? Mathf.Lerp(0.82f, 1f, t / 0.12f) : 1f);
            var b = Mathf.Lerp(0.038f, 0.055f, bulge) * (t < 0.12f ? Mathf.Lerp(0.7f, 1f, t / 0.12f) : 1f) * Mathf.Lerp(1f, 0.85f, t * t);
            centres[k] = new Vector3(0, y, 0);
            for (var i = 0; i <= around; i++)
            {
                var ang = (float)(i % around) / around * Mathf.PI * 2f;
                var c = Mathf.Cos(ang);
                var sn = Mathf.Sin(ang);
                // a superellipse: square-ish with rounded corners
                var x = a * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 0.45f);
                var z = b * Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.45f);
                pts[k, i] = new Vector3(x, y, z);
            }
        }

        var body = new MeshBuilder();
        body.Loft(pts, centres, true, false);
        Piece(_satchel, "Body", body, leather, 0.004f);
        // inside: dark, a little below the rim, so the mouth reads as open
        var mouth = new MeshBuilder();
        mouth.Ellipsoid(new Vector3(0, -0.05f, 0), new Vector3(0.125f, 0.012f, 0.044f), Quaternion.identity, 24, 6);
        Piece(_satchel, "Mouth", mouth, dark, 0f);
        // a stitched rim round the mouth
        var rim = new MeshBuilder();
        var rimPts = new Vector3[around + 1];
        var rimR = new float[around + 1];
        for (var i = 0; i <= around; i++) rimPts[i] = pts[rings - 1, i] + Vector3.up * 0.002f;
        for (var i = 0; i <= around; i++) rimR[i] = 0.006f;
        rim.Tube(rimPts, rimR, Vector3.up, 8, false, false);
        Piece(_satchel, "Rim", rim, leather, 0.0015f);
        // the flap, thrown back over the far side, curving down it
        var flap = new MeshBuilder();
        flap.Box(new Vector3(0, -0.024f, 0.036f), new Vector3(0.25f, 0.009f, 0.032f), Quaternion.Euler(38, 0, 0));
        flap.Box(new Vector3(0, -0.092f, 0.056f), new Vector3(0.25f, 0.13f, 0.008f), Quaternion.Euler(4, 0, 0));
        Piece(_satchel, "Flap", flap, leather, 0.003f);
        var buckle = new MeshBuilder();
        buckle.Torus(new Vector3(0, -0.135f, 0.063f), Vector3.forward, 0.017f, 0.004f, 18, 6);
        buckle.Box(new Vector3(0, -0.135f, 0.065f), new Vector3(0.004f, 0.03f, 0.004f), Quaternion.identity);
        Piece(_satchel, "Buckle", buckle, brass, 0.0015f);
        // the strap, up from both sides to your hand
        var strap = new MeshBuilder();
        strap.Tube(new[] { new Vector3(-0.13f, -0.06f, 0), new Vector3(-0.07f, 0.02f, 0), new Vector3(0, 0.05f, 0), new Vector3(0.07f, 0.02f, 0), new Vector3(0.13f, -0.06f, 0) },
            new[] { 0.012f, 0.012f, 0.012f, 0.012f, 0.012f }, Vector3.forward, 8, true, true, new Vector2(1f, 0.35f));
        Piece(_satchel, "Strap", strap, leather, 0.002f);
        go.SetActive(false);
    }

    private static void Piece(Transform parent, string name, MeshBuilder mb, Material m, float outline)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("InkVR_Satchel" + name);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        if (outline <= 0) return;
        var o = new GameObject(name + "_Ink");
        o.transform.SetParent(go.transform, false);
        o.AddComponent<MeshFilter>().sharedMesh = mb.ToOutlineMesh("InkVR_Satchel" + name + "_Ink", outline);
        var orr = o.AddComponent<MeshRenderer>();
        orr.sharedMaterial = InkMaterials.Outline;
        orr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>Worn brown leather: mottled, darker at the seams, a few ink scuffs.</summary>
    private static Texture2D LeatherTexture()
    {
        var c = new UI.InkCanvas(128, 128, new Color(0.42f, 0.26f, 0.14f), 57);
        for (var i = 0; i < 260; i++) c.Dot(c.Rand(0, 128), c.Rand(0, 128), c.Rand(1.5f, 5f), new Color(0.3f, 0.18f, 0.09f), 0.25f);
        for (var i = 0; i < 90; i++) c.Dot(c.Rand(0, 128), c.Rand(0, 128), c.Rand(0.8f, 2.4f), new Color(0.55f, 0.36f, 0.2f), 0.3f);
        for (var x = 0; x < 128; x += 3) c.Dot(x, 4, 1.1f, HenryTextures.Ink, 0.6f); // a stitched seam
        for (var i = 0; i < 6; i++) c.Dot(c.Rand(10, 118), c.Rand(10, 118), c.Rand(2f, 4f), HenryTextures.Ink, 0.5f);
        c.Grain(0.1f);
        return c.ToTexture("InkVR_SatchelLeather", false);
    }

    private static bool AnyVisible(Transform root)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            if (r.enabled && r.gameObject.activeInHierarchy)
                return true;
        return false;
    }

    // ---------------- the objective ----------------

    /// <summary>The objective moved on: what you carried for it has been handed over.</summary>
    private static void CheckObjective()
    {
        var o = GameManager.Instance?.m_CurrentObjective;
        var key = o == null ? "" : $"{o.Header}|{o.Objective}";
        if (_objective == null)
        {
            _objective = key;
            return;
        }

        if (key == _objective) return;
        _objective = key;
        if (Bag.Count == 0) return;
        Plugin.L.LogInfo($"Satchel: a new objective ('{key}'): {Bag.Count} things handed over");
        CloseSatchel();
        foreach (var item in Bag)
            if (item.Copy != null)
                Object.Destroy(item.Copy);
        Bag.Clear();
    }

    /// <summary>A new chapter or the main menu: the satchel starts empty.</summary>
    public static void Clear()
    {
        CloseSatchel();
        foreach (var item in Bag)
            if (item.Copy != null)
                Object.Destroy(item.Copy);
        Bag.Clear();
        for (var h = 0; h < 2; h++)
        {
            if (Held[h]?.Item.Copy != null) Object.Destroy(Held[h].Item.Copy);
            Held[h] = null;
        }

        foreach (var f in Air)
            if (f.Item.Copy != null)
                Object.Destroy(f.Item.Copy);
        Air.Clear();
        _objective = null;
    }
}
