using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InkVR.Body;
using InkVR.Core;
using InkVR.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.Input;

/// <summary>
/// Gravity pull, in the studio's own ink. Point a hand at something you could pick up (a collectable, a can of
/// Bacon Soup, a weapon lying about), up to 5 m away, and a thread of ink reaches out from your hand to it. Grip
/// to take hold of the thread, then flick your hand back towards you, or just keep holding the grip a moment
/// (easier sitting down), and it flies to your hand in an arc, dripping ink all the way. It lands in your hand
/// and the game takes it, just as if you had gripped it there. Walls get in the way; anything within reach of
/// your hand is still simply gripped.
/// </summary>
public static class GravityPull
{
    // Seated, everything is further from your hand (you can't lean over to it), so the pull reaches further and
    // forgives a looser aim.
    private static float Range => ModConfig.SeatedMode.Value ? 8.5f : 5f;            // metres
    private static float CastRadius => ModConfig.SeatedMode.Value ? 0.3f : 0.2f;     // metres either side of where the hand points
    private static float MaxAngle => ModConfig.SeatedMode.Value ? 22f : 16f;         // degrees off the hand's aim
    private const float MinDistance = 0.45f;  // closer than this, you just grip it
    private const float ReachMin = 0.75f;     // things that stay put (valves, buttons, tapes): only when they're out of reach
    private const float FlickSpeed = 1.2f;    // m/s of the hand back towards you
    private const float HoldToPull = 0.15f;   // or just grip (a moment, so a brush of the grip doesn't)

    private sealed class HandState
    {
        public Interactable Target;
        public PhysicalInteract.Kind Kind;
        public bool IsWeapon, Locked, Reach;
        public float LockedAt, NextDrip, NextTick;
        public LineRenderer Line;

        // a reach: the thread snaps out to what you used, for a moment
        public float ZapUntil;
        public Vector3 ZapTo;
    }

    private sealed class Flight
    {
        public Interactable Item;
        public PhysicalInteract.Kind Kind;
        public bool IsWeapon;
        public Hand Hand;
        public Transform Copy;
        public readonly List<Renderer> Hidden = new();
        public Vector3 From, Spin;
        public float Start, Duration, NextDrip;
    }

    private static readonly HandState[] States = { new(), new() };
    private static readonly List<Flight> Flights = new();
    private static readonly Dictionary<System.IntPtr, (PhysicalInteract.Kind kind, bool weapon, bool reach, float at)> KindCache = new();
    private static Il2CppStructArray<RaycastHit> _hits;
    private static Material _inkLine;
    private static string _scene = "";

    /// <summary>Dev/log: what each hand is pointing at, and the last pull.</summary>
    public static string LastPull = "";

    public static string Targets => $"L={Describe(States[0])} R={Describe(States[1])} flights={Flights.Count}";

    private static string Describe(HandState s) => s.Target == null ? "-" : $"{s.Target.name}{(s.Locked ? " (held)" : "")}";

    public static void Update()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != _scene)
        {
            _scene = scene;
            KindCache.Clear();
            foreach (var f in Flights.ToArray()) Land(f, false);
        }

        var on = ModConfig.GravityPull.Value && XrManager.IsRunning && VRRig.InGame && !VRRig.CinemaActive;
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var s = States[(int)hand];
            if (!on)
            {
                Drop(s);
                continue;
            }

            if (s.Locked) Hold(hand, s);
            else Aim(hand, s);
            DrawThread(hand, s);
        }

        for (var i = Flights.Count - 1; i >= 0; i--) Fly(Flights[i]);
        DevDrive();
    }

    // ---------------- dev: point the right hand at something, grip, hold ----------------

    private static Vector3? _devTarget;
    private static float _devStart, _devLogAt;

    /// <summary>Dev: the right hand points at a world point, grips after 0.4 s and holds (hold-to-pull), lets go at 1.9 s.</summary>
    public static void DevPull(Vector3 target)
    {
        _devTarget = target;
        _devStart = Time.unscaledTime;
    }

    private static void DevDrive()
    {
        if (!_devTarget.HasValue || VRRig.ActiveOrigin == null) return;
        var t = Time.unscaledTime - _devStart;
        var hand = new Vector3(0.22f, 1.2f, 0.25f);
        var target = VRRig.ActiveOrigin.InverseTransformPoint(_devTarget.Value) - Vector3.up * VRRig.HeightLift;
        var rot = Quaternion.LookRotation(target - hand);
        XrInput.DevRightAim = (hand, rot);
        XrInput.ApplyDev();
        XrInput.DevRightGrip = t > 0.4f && t < 1.9f ? 1f : 0f;
        if (t > 2.8f)
        {
            Plugin.L.LogInfo($"PULL done: {LastPull} | {Targets}");
            XrInput.DevRightAim = null;
            XrInput.DevRightGrip = null;
            _devTarget = null;
        }
    }

    // ---------------- pointing ----------------

    private static void Aim(Hand hand, HandState s)
    {
        var before = s.Target;
        s.Target = Busy(hand) ? null : Find(hand, out s.Kind, out s.IsWeapon, out s.Reach);
        if (s.Target != null && (before == null || before.Pointer != s.Target.Pointer))
            XrInput.Haptic(hand, 0.08f, 0.015f);
    }

    /// <summary>The hand is already doing something: holding a weapon, gripping a valve or a door, or right over something it can grip.</summary>
    private static bool Busy(Hand hand)
    {
        if (InteractionAim.HoverHand == hand && InteractionAim.HoverNear && InteractionAim.Hovered != null) return true;
        if (PhysicalInteract.Active != PhysicalInteract.Kind.None) return true;
        var player = GameManager.Instance?.Player;
        var weapon = player?.WeaponGameObject;
        return hand == ControllerBindings.Dominant && weapon != null && weapon.activeInHierarchy;
    }

    private static (Vector3 pos, Vector3 dir) AimRay(Hand hand)
    {
        var dev = hand == Hand.Right ? XrInput.DevRightAim : null;
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(dev?.pos ?? c.AimPosition, dev?.rot ?? c.AimRotation, out var p, out var r);
        return (p, r * Vector3.forward);
    }

    private static Interactable Find(Hand hand, out PhysicalInteract.Kind kind, out bool weapon, out bool reach)
    {
        kind = PhysicalInteract.Kind.None;
        weapon = false;
        reach = false;
        var k = VRRig.ActiveScale;
        var (origin, dir) = AimRay(hand);
        var player = GameManager.Instance?.Player;
        if (player == null) return null;
        var mask = Physics.DefaultRaycastLayers & ~(1 << player.gameObject.layer) & ~VRLayers.UIMask;
        _hits ??= new Il2CppStructArray<RaycastHit>(32);
        var n = Physics.SphereCastNonAlloc(origin, CastRadius * k, dir, _hits, Range * k, mask, QueryTriggerInteraction.Collide);

        // the nearest wall: nothing behind it can be pulled
        var wall = float.MaxValue;
        for (var i = 0; i < n; i++)
        {
            var h = _hits[i];
            var col = h.collider;
            if (col == null || col.isTrigger || col.transform.IsChildOf(player.transform)) continue;
            if (col.GetComponentInParent<Interactable>() != null) continue;
            if (h.distance > 0 && h.distance < wall) wall = h.distance;
        }

        if (_devTarget.HasValue && hand == Hand.Right && Time.unscaledTime >= _devLogAt)
        {
            _devLogAt = Time.unscaledTime + 0.5f;
            var sb = new System.Text.StringBuilder($"PULLCAST {n} hits, wall {(wall < float.MaxValue ? wall / k : -1f):F2} m, busy={Busy(hand)}:");
            for (var i = 0; i < n && i < 12; i++)
            {
                var h = _hits[i];
                if (h.collider == null) continue;
                var it = h.collider.GetComponentInParent<Interactable>();
                sb.Append($" [{h.collider.name}{(h.collider.isTrigger ? " trig" : "")} {h.distance / k:F2}m{(it != null ? " item=" + it.name + (it.isActiveAndEnabled ? "" : " off") : "")}]");
            }

            Plugin.L.LogInfo(sb.ToString());
        }

        Interactable best = null;
        var bestScore = float.MaxValue;
        for (var i = 0; i < n; i++)
        {
            var h = _hits[i];
            var col = h.collider;
            if (col == null || col.transform.IsChildOf(player.transform)) continue;
            var item = col.GetComponentInParent<Interactable>();
            if (item == null || !item.isActiveAndEnabled) continue;
            var centre = Centre(item);
            var to = centre - origin;
            var dist = to.magnitude;
            if (dist < MinDistance * k) continue;
            var angle = Vector3.Angle(dir, to);
            if (angle > MaxAngle + Mathf.Atan2(CastRadius * k, dist) * Mathf.Rad2Deg) continue;
            if (!Pullable(item, out var kd, out var wp, out var rc)) continue;
            if (rc && dist < ReachMin * k) continue;
            if (!InSight(origin, centre, item, mask, k)) continue;
            // something to bring to your hand wins over something to use where it is
            var score = angle + dist / k * 1.5f + (rc ? 10f : 0f);
            if (score >= bestScore) continue;
            bestScore = score;
            best = item;
            kind = kd;
            weapon = wp;
            reach = rc;
        }

        return best;
    }

    /// <summary>
    /// Nothing solid between your hand and it: a thin ray from the hand reaches the thing itself, or stops within a
    /// hand's breadth of it (the table, crate or coffin it's lying on doesn't hide it).
    /// </summary>
    private static bool InSight(Vector3 from, Vector3 centre, Interactable item, int mask, float k)
    {
        var to = centre - from;
        var len = to.magnitude;
        if (!Physics.Raycast(from, to / len, out var hit, len, mask, QueryTriggerInteraction.Ignore)) return true;
        var weapon = item.GetComponentInParent<BaseWeapon>();
        var root = weapon != null ? weapon.transform : item.transform;
        if (hit.transform.IsChildOf(root)) return true;
        return (hit.point - centre).magnitude < 0.3f * k;
    }

    /// <summary>
    /// Something you could pick up (a story item, a can of soup, a weapon lying about), or, out of your reach,
    /// something you use where it is (a valve, a lever, a button, a tape, a switch, a chest...).
    /// </summary>
    private static bool Pullable(Interactable item, out PhysicalInteract.Kind kind, out bool weapon, out bool reach)
    {
        if (!KindCache.TryGetValue(item.Pointer, out var c) || Time.unscaledTime - c.at > 5f)
        {
            var w = item.GetComponentInParent<BaseWeapon>();
            var held = GameManager.Instance?.Player?.WeaponGameObject;
            var inHand = w != null && ((held != null && w.transform.IsChildOf(held.transform)) || w.transform.IsChildOf(GameManager.Instance.Player.transform));
            var isWeapon = w != null && !inHand;
            var kd = w != null ? PhysicalInteract.Kind.None : PhysicalInteract.Classify(item);
            var bring = isWeapon || kd == PhysicalInteract.Kind.Take || kd == PhysicalInteract.Kind.Soup;
            c = (kd, isWeapon, !bring && w == null && Usable(item), Time.unscaledTime);
            KindCache[item.Pointer] = c;
        }

        kind = c.kind;
        weapon = c.weapon;
        reach = c.reach;
        return weapon || reach || kind == PhysicalInteract.Kind.Take || kind == PhysicalInteract.Kind.Soup;
    }

    // Never used from afar: doors (yours to push), the hiding cupboards, enemies and Boris, the fairground games and the
    // carts (they need you there), the melting Tommy gun.
    private static readonly string[] NotFromAfar =
    {
        "BaseDoorController", "DoorController", "CustomDoorController", "CH1EntranceDoor", "CH5SafehouseDoor", "LittleMiracleStationController",
        "CH4Cart", "CH4CartRide", "CH4AbyssController", "CH3MeltingTommyGun", "PoolBall", "AnimationEventController"
    };

    private static bool Usable(Interactable item)
    {
        if (item.GetComponentInParent<Ai.BaseAiController>() != null) return false;
        var t = item.transform;
        for (var up = 0; up < 4 && t != null; up++, t = t.parent)
            foreach (var c in t.GetComponents<MonoBehaviour>())
            {
                var n = c.GetIl2CppType().Name;
                if (n.StartsWith("Minigame")) return false;
                foreach (var x in NotFromAfar)
                    if (n == x)
                        return false;
            }

        return true;
    }

    private static Vector3 Centre(Interactable item)
    {
        var r = item.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds.center : item.transform.position;
    }

    // ---------------- taking hold ----------------

    /// <summary>This hand's thread is on that item.</summary>
    public static bool Aims(Hand hand, Interactable item) => States[(int)hand].Target != null && item != null && States[(int)hand].Target.Pointer == item.Pointer;

    /// <summary>A grip just closed: if this hand is pointing at something to pull, it takes hold of the thread (the grip isn't an interact).</summary>
    public static bool TryLock(Hand hand, bool reach)
    {
        if (!ModConfig.GravityPull.Value) return false;
        var s = States[(int)hand];
        if (s.Target == null || s.Locked || s.Reach != reach) return false;
        s.Locked = true;
        s.LockedAt = Time.unscaledTime;
        XrInput.Haptic(hand, 0.3f, 0.05f);
        InkPool.InkSound(Centre(s.Target), 0.5f);
        return true;
    }

    private static float Grip(Hand hand) => (hand == Hand.Right ? XrInput.DevRightGrip : XrInput.DevLeftGrip) ?? XrInput.Get(hand).Grip;

    private static void Hold(Hand hand, HandState s)
    {
        if (s.Target == null || !s.Target.isActiveAndEnabled || Grip(hand) < 0.45f)
        {
            s.Locked = false;
            return;
        }

        var now = Time.unscaledTime;
        if (now >= s.NextTick)
        {
            s.NextTick = now + 0.12f;
            XrInput.Haptic(hand, 0.12f, 0.02f);
        }

        // a flick back towards you (or up), or just keep holding
        var k = VRRig.ActiveScale;
        var head = VRRig.Head != null ? VRRig.Head.position : AimRay(hand).pos;
        var (handPos, _) = AimRay(hand);
        var v = WeaponHandling.Velocity(hand) / k;
        var towards = Vector3.Dot(v, (head - handPos).normalized);
        if (towards > FlickSpeed || v.y > FlickSpeed * 1.3f || now - s.LockedAt > HoldToPull) Launch(hand, s);
    }

    // ---------------- the flight ----------------

    private static void Launch(Hand hand, HandState s)
    {
        var item = s.Target;
        s.Locked = false;
        s.Target = null;
        if (s.Reach)
        {
            UseFromAfar(hand, s, item);
            return;
        }

        var f = new Flight { Item = item, Kind = s.Kind, IsWeapon = s.IsWeapon, Hand = hand, Start = Time.unscaledTime, Spin = Random.onUnitSphere };
        var root = new GameObject("InkVR_Pulled_" + item.name).transform;
        f.From = Centre(item);
        root.position = f.From;
        MeshCopy.AddAll(item.transform, root, false, out _, f.Hidden);

        f.Copy = root;
        var dist = (HandPos(hand) - f.From).magnitude / VRRig.ActiveScale;
        f.Duration = Mathf.Clamp(0.28f + dist * 0.07f, 0.32f, 0.62f);
        Flights.Add(f);
        XrInput.Haptic(hand, 0.45f, 0.06f);
        InkPool.InkSound(f.From, 0.8f);
        // a splash of ink where it tore free
        for (var i = 0; i < 6; i++)
            InkDrops.Spawn(f.From, (Random.onUnitSphere + Vector3.up * 0.6f) * (0.8f * VRRig.ActiveScale), Random.Range(0.002f, 0.0045f) * VRRig.ActiveScale);
        LastPull = $"{item.name} ({(f.IsWeapon ? "weapon" : f.Kind.ToString())}) {dist:F1} m into the {hand} hand";
        Plugin.L.LogInfo($"Gravity pull: {LastPull}");
    }

    /// <summary>A reach: the thread snaps tight to it and it's used where it is, as if you'd reached it (the game's own use).</summary>
    private static void UseFromAfar(Hand hand, HandState s, Interactable item)
    {
        var at = Centre(item);
        s.ZapTo = at;
        s.ZapUntil = Time.unscaledTime + 0.3f;
        XrInput.Haptic(hand, 0.5f, 0.07f);
        InkPool.InkSound(at, 0.7f);
        for (var i = 0; i < 6; i++)
            InkDrops.Spawn(at, (Random.onUnitSphere + Vector3.up * 0.5f) * (0.7f * VRRig.ActiveScale), Random.Range(0.002f, 0.004f) * VRRig.ActiveScale);
        var used = PhysicalInteract.CaughtFromPull(s.Kind, hand, item, new List<Renderer>());
        var dist = (HandPos(hand) - at).magnitude / VRRig.ActiveScale;
        LastPull = $"{item.name} ({s.Kind}) used from {dist:F1} m with the {hand} hand";
        Plugin.L.LogInfo($"Gravity pull: reached {LastPull}{(used ? "" : " (busy: not used)")}");
    }

    private static Vector3 HandPos(Hand hand)
    {
        var dev = hand == Hand.Right ? Hands.DevRight : Hands.DevLeft;
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, dev?.rot ?? c.GripRotation, out var p, out _);
        return p;
    }

    private static void Fly(Flight f)
    {
        if (f.Copy == null || f.Item == null)
        {
            Land(f, false);
            return;
        }

        var k = VRRig.ActiveScale;
        var u = Mathf.Clamp01((Time.unscaledTime - f.Start) / f.Duration);
        var t = u * u * (1.6f - 0.6f * u); // slow to tear free, fast at the end
        var to = HandPos(f.Hand);
        var mid = (f.From + to) * 0.5f + Vector3.up * ((f.From - to).magnitude * 0.25f + 0.12f * k);
        var a = Vector3.Lerp(f.From, mid, t);
        var b = Vector3.Lerp(mid, to, t);
        var pos = Vector3.Lerp(a, b, t);
        var vel = (b - a).normalized;
        f.Copy.position = pos;
        f.Copy.rotation = Quaternion.AngleAxis(360f * u * 0.6f, f.Spin) * f.Copy.rotation;
        var now = Time.unscaledTime;
        if (now >= f.NextDrip)
        {
            // ink drips off it all the way, and lands where it lands
            f.NextDrip = now + 0.025f;
            InkDrops.Spawn(pos + Random.insideUnitSphere * (0.03f * k), (vel * 0.6f + Vector3.down * 0.4f) * k, Random.Range(0.0018f, 0.0038f) * k);
        }

        if (u >= 1f) Land(f, true);
    }

    private static void Land(Flight f, bool caught)
    {
        Flights.Remove(f);
        if (f.Copy != null) Object.Destroy(f.Copy.gameObject);
        var taken = false;
        if (caught && f.Item != null)
        {
            taken = PhysicalInteract.CaughtFromPull(f.Kind, f.Hand, f.Item, f.Hidden);
            XrInput.Haptic(f.Hand, 0.6f, 0.08f);
            var at = HandPos(f.Hand);
            InkPool.InkSound(at, 0.6f);
            for (var i = 0; i < 5; i++)
                InkDrops.Spawn(at, (Random.onUnitSphere + Vector3.up * 0.4f) * (0.6f * VRRig.ActiveScale), Random.Range(0.0018f, 0.0035f) * VRRig.ActiveScale);
        }

        // whatever the game didn't put away is back where it was
        foreach (var r in f.Hidden)
            if (r != null)
                r.enabled = true;
        if (caught) Plugin.L.LogInfo($"Gravity pull: caught {f.Item?.name} ({(taken ? "taken" : "not taken")})");
    }

    // ---------------- the ink thread ----------------

    private static void DrawThread(Hand hand, HandState s)
    {
        var zap = s.Target == null && Time.unscaledTime < s.ZapUntil;
        if (s.Target == null && !zap)
        {
            if (s.Line != null && s.Line.enabled) s.Line.enabled = false;
            return;
        }

        if (s.Line == null)
        {
            var go = new GameObject("InkVR_PullThread_" + hand);
            Object.DontDestroyOnLoad(go);
            s.Line = go.AddComponent<LineRenderer>();
            s.Line.sharedMaterial = InkLine();
            s.Line.textureMode = LineTextureMode.Tile;
            s.Line.positionCount = 20;
            s.Line.numCapVertices = 3;
            s.Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.Line.receiveShadows = false;
        }

        var k = VRRig.ActiveScale;
        var (from, _) = AimRay(hand);
        var to = zap ? s.ZapTo : Centre(s.Target);
        // the thread sags like a string of ink, and tightens when you take hold of it (or reach with it)
        var sag = (s.Locked || zap ? 0.02f : 0.12f) * (to - from).magnitude;
        var mid = (from + to) * 0.5f + Vector3.down * sag;
        for (var i = 0; i < 20; i++)
        {
            var t = i / 19f;
            var p = Vector3.Lerp(Vector3.Lerp(from, mid, t), Vector3.Lerp(mid, to, t), t);
            s.Line.SetPosition(i, p);
        }

        var w = (zap ? 0.03f * (s.ZapUntil - Time.unscaledTime) / 0.3f + 0.006f : s.Locked ? 0.024f : 0.013f) * k;
        s.Line.startWidth = w;
        s.Line.endWidth = w * 0.45f;
        var len = (to - from).magnitude / k;
        s.Line.material.mainTextureScale = new Vector2(len * 9f, 1f);
        s.Line.material.mainTextureOffset = new Vector2(-Time.unscaledTime * (s.Locked ? 3.5f : 1.2f), 0); // the ink flows back to your hand
        s.Line.enabled = true;
        if (zap) return;

        // the thing itself weeps ink while you hold it
        var now = Time.unscaledTime;
        if (now >= s.NextDrip)
        {
            s.NextDrip = now + (s.Locked ? 0.06f : 0.35f);
            var r = s.Target.GetComponentInChildren<Renderer>();
            var b = r != null ? r.bounds : new Bounds(to, Vector3.one * 0.1f * k);
            InkDrops.Spawn(new Vector3(Random.Range(b.min.x, b.max.x), b.min.y, Random.Range(b.min.z, b.max.z)), Vector3.down * (0.2f * k), Random.Range(0.0015f, 0.003f) * k);
        }
    }

    /// <summary>The thread: a row of ink beads, soft-edged, that flows along it.</summary>
    private static Material InkLine()
    {
        if (_inkLine != null) return _inkLine;
        const int W = 64, H = 16;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "InkVR_PullThread", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color32[W * H];
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            // a bead with a thin string between beads
            float u = (x + 0.5f) / W, v = (y + 0.5f) / H - 0.5f;
            var bead = Mathf.Clamp01((0.46f - Mathf.Sqrt((u - 0.5f) * (u - 0.5f) * 1.2f + v * v)) * 14f);
            var str = Mathf.Clamp01((0.24f - Mathf.Abs(v)) * 14f);
            var a = Mathf.Max(bead, str);
            px[y * W + x] = new Color32(10, 8, 7, (byte)(a * 240));
        }

        tex.SetPixels32(px);
        tex.Apply(false, true);
        tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(tex);
        _inkLine = new Material(Shader.Find("Sprites/Default")) { name = "InkVR_PullThread", mainTexture = tex };
        _inkLine.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(_inkLine);
        return _inkLine;
    }

    private static void Drop(HandState s)
    {
        s.Target = null;
        s.Locked = false;
        if (s.Line != null && s.Line.enabled) s.Line.enabled = false;
    }
}
