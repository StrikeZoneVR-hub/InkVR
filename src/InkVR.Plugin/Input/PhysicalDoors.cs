using System;
using System.Collections.Generic;
using DG.Tweening;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InkVR.Body;
using InkVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace InkVR.Input;

/// <summary>
/// Doors you open with your hands (motion controls part 3). The studio's hinged doors swing like real ones:
///   push one with an open hand and it swings away, as hard as you pushed it; grip it and pull it towards you,
///   or walk it open; fling it and it swings on, slows, and swings shut with a bang if it comes back far enough;
///   it stops against your body and against walls; a locked door rattles in its frame (and the game plays its
///   own "locked" response, as if you had tried the handle).
/// The game is told when a door opens and closes (its sounds, its occlusion, and whatever story waits on that
/// door), and while the game moves a door itself (X, a script, a scare) the door is the game's.
/// </summary>
public static class PhysicalDoors
{
    private enum Kind
    {
        Base,
        Simple,
        Custom
    }

    private sealed class Door
    {
        public Kind Kind;
        public BaseDoorController B;
        public DoorController D;
        public CustomDoorController C;
        public Transform Root, Hinge;
        public Quaternion Closed;
        public bool Ready, Measured;
        public float Alpha, Width, Thick, Y0, Y1; // the leaf in the hinge's own space (angle, metres of world)
        public float Min = -100f, Max = 100f;
        public string StoppedBy = ""; // what stops it each way (log)
        public float Theta, Omega;
        public Quaternion LastSet;
        public bool Driving, GameOpen;
        public float GameUntil, OwnCallUntil, NextCreak, NextLocked, RattleUntil, NextTick;
        public readonly sbyte[] Side = new sbyte[6];
        public AudioSource Audio;

        public bool Locked => Kind switch
        {
            Kind.Base => B.IsLocked,
            Kind.Simple => D.IsLocked,
            _ => C.IsLocked
        };

        public bool OpenInGame => Kind switch
        {
            Kind.Base => B.IsOpen,
            Kind.Simple => D.IsOpen,
            _ => C.IsOpen
        };
    }

    private struct Pusher
    {
        public Vector3 Pos;
        public float R, Bottom, Top; // Bottom/Top: world heights it covers (a hand is a ball, the body a column)
        public int Id;
        public bool Body, Jumped;
        public Vector3 Vel;
        public Hand Hand;
    }

    private static readonly Vector3[] PrevPusher = new Vector3[6];
    private static readonly float[] PrevPusherT = new float[6];

    private static readonly List<Door> Doors = new();
    private static readonly Dictionary<IntPtr, Door> ByPtr = new();
    private static readonly List<Pusher> Pushers = new();
    private static string _scene;
    private static float _nextScan;

    private static Door _grabDoor;
    private static Hand _grabHand;
    private static float _grabOffset, _grabStart;

    /// <summary>Dev: what happened last.</summary>
    public static string Last = "";

    public static int Count => Doors.Count;

    // ---------------- finding the doors ----------------

    private static void Scan()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var added = 0;
        added += Add<BaseDoorController>(Kind.Base);
        added += Add<DoorController>(Kind.Simple);
        added += Add<CustomDoorController>(Kind.Custom);
        Doors.RemoveAll(d => d.Root == null || d.Hinge == null);
        if (added > 0) Plugin.L.LogInfo($"Doors: {Doors.Count} hinged doors in this level ({added} new, {sw.Elapsed.TotalMilliseconds:F1} ms)");
    }

    private static int Add<T>(Kind kind) where T : Component
    {
        var added = 0;
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>()))
        {
            var c = o.Cast<Component>();
            if (!c.gameObject.scene.IsValid() || ByPtr.ContainsKey(c.Pointer)) continue;
            var d = new Door { Kind = kind, Root = c.transform };
            switch (kind)
            {
                case Kind.Base:
                    d.B = c.Cast<BaseDoorController>();
                    d.Hinge = d.B.m_Door;
                    break;
                case Kind.Simple:
                    d.D = c.Cast<DoorController>();
                    d.Hinge = d.D.m_DoorHinge;
                    break;
                default:
                    d.C = c.Cast<CustomDoorController>();
                    d.Hinge = d.C.m_DoorHinge;
                    break;
            }

            ByPtr[c.Pointer] = d;
            if (d.Hinge == null) continue;
            Doors.Add(d);
            added++;
        }

        return added;
    }

    /// <summary>The leaf (the door's solid colliders under the hinge) measured in the hinge's own space, and how far it can swing each way before it meets a wall.</summary>
    private static void Measure(Door d)
    {
        d.Measured = true;
        var hinge = d.Hinge;
        var any = false;
        Vector3 min = default, max = default;
        foreach (var col in hinge.GetComponentsInChildren<Collider>(true))
        {
            if (col.isTrigger) continue;
            // the collider's own box, in its own space: a box collider's, a mesh's bounds, or failing those its world box
            Vector3 c, e;
            var t = col.transform;
            var box = col.TryCast<BoxCollider>();
            var mesh = box == null ? col.TryCast<MeshCollider>() : null;
            if (box != null)
            {
                c = box.center;
                e = box.size * 0.5f;
            }
            else if (mesh != null && mesh.sharedMesh != null)
            {
                var mb = mesh.sharedMesh.bounds;
                c = mb.center;
                e = mb.extents;
            }
            else
            {
                var wb = col.bounds;
                c = t.InverseTransformPoint(wb.center);
                e = t.InverseTransformVector(wb.extents);
                e = new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z));
            }

            for (var i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                var p = hinge.InverseTransformPoint(t.TransformPoint(corner));
                if (!any)
                {
                    min = max = p;
                    any = true;
                }

                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        if (!any) return;
        // The leaf runs out from the hinge along its longer horizontal side.
        var sx = max.x - min.x;
        var sz = max.z - min.z;
        var far = sx >= sz
            ? new Vector2(Mathf.Abs(max.x) > Mathf.Abs(min.x) ? max.x : min.x, (min.z + max.z) * 0.5f)
            : new Vector2((min.x + max.x) * 0.5f, Mathf.Abs(max.z) > Mathf.Abs(min.z) ? max.z : min.z);
        var s = hinge.lossyScale.x;
        d.Alpha = Mathf.Atan2(far.x, far.y) * Mathf.Rad2Deg;
        d.Width = far.magnitude * s;
        d.Thick = Mathf.Min(sx, sz) * s;
        d.Y0 = min.y * hinge.lossyScale.y;
        d.Y1 = max.y * hinge.lossyScale.y;
        if (d.Kind == Kind.Custom && d.C.m_OpenRotation.y != 0)
        {
            // a door that only opens one way
            var open = Mathf.DeltaAngle(0, d.C.m_OpenRotation.y);
            d.Min = Mathf.Min(0, open) - 5f;
            d.Max = Mathf.Max(0, open) + 5f;
        }

        FindStops(d);
        d.Ready = true;
        Plugin.L.LogInfo($"Door {d.Root.name}: leaf {d.Width / VRRig.ActiveScale:F2} m wide, {d.Thick / VRRig.ActiveScale * 100:F0} cm thick, swings {d.Min:F0}..{d.Max:F0}°{(d.StoppedBy.Length > 0 ? " (walls at" + d.StoppedBy + ")" : "")}");
    }

    /// <summary>
    /// Sweeps the leaf each way (a slimmed box, so the frame it hangs in doesn't count) to find walls it would hit.
    /// Whatever the leaf already touches when (nearly) shut, its frame, trim or the game's invisible doorway blocker,
    /// isn't a wall to stop against.
    /// </summary>
    private static void FindStops(Door d)
    {
        var mask = Physics.DefaultRaycastLayers & ~(1 << GameManager.Instance.Player.gameObject.layer);
        var parentRot = (d.Hinge.parent != null ? d.Hinge.parent.rotation : Quaternion.identity) * d.Closed;
        var half = new Vector3(d.Thick * 0.3f, (d.Y1 - d.Y0) * 0.4f, d.Width * 0.3f);
        Il2CppReferenceArray<Collider> Overlaps(float a)
        {
            var rot = parentRot * Quaternion.Euler(0, d.Alpha + a, 0);
            var mid = d.Hinge.position + rot * Vector3.forward * (d.Width * 0.6f) + parentRot * Vector3.up * ((d.Y0 + d.Y1) * 0.5f);
            return Physics.OverlapBox(mid, half, rot, mask, QueryTriggerInteraction.Ignore);
        }

        var shut = new HashSet<System.IntPtr>();
        foreach (var a in new[] { -6f, 0f, 6f })
            foreach (var hit in Overlaps(a))
                shut.Add(hit.Pointer);

        d.StoppedBy = "";
        for (var dir = -1; dir <= 1; dir += 2)
        {
            for (var a = 15f; a <= 155f; a += 10f)
            {
                Collider wall = null;
                foreach (var hit in Overlaps(a * dir))
                {
                    if (hit.transform.IsChildOf(d.Root) || shut.Contains(hit.Pointer)) continue;
                    wall = hit;
                    break;
                }

                if (wall == null) continue;
                var stop = (a - 8f) * dir;
                if (dir < 0) d.Min = Mathf.Max(d.Min, stop);
                else d.Max = Mathf.Min(d.Max, stop);
                d.StoppedBy += $" {(dir < 0 ? "-" : "+")}{a:F0}°: {wall.name}";
                break;
            }
        }
    }

    // ---------------- every frame ----------------

    public static void Update()
    {
        var scene = SceneManager.GetActiveScene().name;
        if (scene != _scene)
        {
            _scene = scene;
            Doors.Clear();
            ByPtr.Clear();
            _grabDoor = null;
            _nextScan = Time.unscaledTime + 2f;
        }

        if (!ModConfig.PhysicalDoors.Value || !XrManager.IsRunning || !VRRig.InGame || VRRig.CinemaActive)
        {
            Release();
            return;
        }

        Probe();
        var now = Time.unscaledTime;
        if (now >= _nextScan)
        {
            _nextScan = now + 8f;
            Scan();
        }

        var head = VRRig.Head;
        var player = GameManager.Instance?.Player;
        if (head == null || player == null || Doors.Count == 0) return;
        var dt = Mathf.Clamp(Time.unscaledDeltaTime, 1e-4f, 0.05f);
        var k = VRRig.ActiveScale;
        GatherPushers(player, k);
        var reach = 4f * k;
        foreach (var d in Doors)
        {
            if (d.Hinge == null) continue;
            var to = d.Hinge.position - head.position;
            if (to.sqrMagnitude > reach * reach && d != _grabDoor)
            {
                d.Driving = false;
                continue;
            }

            Step(d, dt, now, k);
        }

        if (_grabDoor != null && Grip(_grabHand) < 0.35f) Release();
    }

    private static void GatherPushers(PlayerController player, float k)
    {
        Pushers.Clear();
        GatherRaw(player, k);
        // a hand that jumps (tracking lost and found, a teleport) doesn't shove the door on the way
        var now = Time.unscaledTime;
        for (var i = 0; i < Pushers.Count; i++)
        {
            var p = Pushers[i];
            var dt = now - PrevPusherT[p.Id];
            p.Jumped = dt <= 0 || dt > 0.25f || (p.Pos - PrevPusher[p.Id]).magnitude / dt > 9f * k;
            p.Vel = dt > 0 && !p.Jumped ? (p.Pos - PrevPusher[p.Id]) / dt : Vector3.zero;
            if (dt > 0)
            {
                PrevPusher[p.Id] = p.Pos;
                PrevPusherT[p.Id] = now;
            }

            Pushers[i] = p;
        }
    }

    private static void GatherRaw(PlayerController player, float k)
    {
        foreach (var hand in new[] { Hand.Right, Hand.Left })
        {
            var (pos, rot) = HandGrip(hand);
            var palm = pos + rot * new Vector3(hand == Hand.Right ? -0.02f : 0.02f, -0.02f, 0.05f) * k;
            var tip = pos + rot * new Vector3(0, -0.02f, 0.12f) * k;
            var hi = (int)hand * 2;
            Pushers.Add(new Pusher { Pos = palm, R = 0.045f * k, Bottom = palm.y - 0.045f * k, Top = palm.y + 0.045f * k, Id = hi, Hand = hand });
            Pushers.Add(new Pusher { Pos = tip, R = 0.025f * k, Bottom = tip.y - 0.025f * k, Top = tip.y + 0.025f * k, Id = hi + 1, Hand = hand });
        }

        var cc = player.CharacterController;
        if (cc != null)
        {
            var s = player.transform.lossyScale.x;
            var c = player.transform.TransformPoint(cc.center);
            var h = cc.height * s * 0.5f;
            Pushers.Add(new Pusher { Pos = c, R = cc.radius * s, Bottom = c.y - h, Top = c.y + h, Id = 4, Body = true });
        }
    }

    private static void Step(Door d, float dt, float now, float k)
    {
        if (!d.Measured)
        {
            if (d.OpenInGame || IsTweening(d)) return;
            d.Closed = d.Hinge.localRotation;
            Measure(d);
            d.LastSet = d.Hinge.localRotation;
        }

        if (!d.Ready) return;
        var actual = d.Hinge.localRotation;

        // The game moving the door itself (X, a script, a scare): it's the game's until it stops.
        if (now > d.OwnCallUntil && Quaternion.Angle(actual, d.LastSet) > 0.4f)
        {
            d.GameUntil = now + 0.25f;
            d.LastSet = actual;
            d.Theta = AngleOf(d, actual);
            d.Omega = 0;
            d.GameOpen = d.OpenInGame || Mathf.Abs(d.Theta) > 4f;
            if (d.Theta > d.Max) d.Max = d.Theta + 1f;
            if (d.Theta < d.Min) d.Min = d.Theta - 1f;
            if (_grabDoor == d) Release();
            return;
        }

        if (now < d.GameUntil) return;
        var locked = d.Locked;
        // the game locks some doors open once they're opened (they stay open): they just stay put
        if (locked && d.GameOpen && Mathf.Abs(d.Theta) > 10f)
        {
            if (_grabDoor == d) Release();
            return;
        }

        var before = d.Theta;
        var frame = Frame(d);
        var pushed = false;

        if (_grabDoor == d)
        {
            var (pos, _) = HandGrip(_grabHand);
            var l = Quaternion.Inverse(frame.rot) * (pos - frame.origin);
            var phi = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
            var target = Mathf.DeltaAngle(0, phi - d.Alpha - _grabOffset);
            if (locked)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(target, d.Theta)) > 3f) Rattle(d, _grabHand, now, Mathf.DeltaAngle(d.Theta, target));
            }
            else
            {
                d.Theta = Mathf.Clamp(Mathf.MoveTowards(d.Theta, target, 400f * dt), d.Min, d.Max);
                d.Omega = Mathf.Lerp(d.Omega, (d.Theta - before) / dt, 0.5f);
                pushed = true;
            }
        }
        else if (!locked && d.GameOpen)
        {
            d.Theta += d.Omega * dt;
            d.Omega *= Mathf.Exp(-1.8f * dt);
        }

        // Hands and body against the leaf: the door gives way (or, locked, rattles).
        foreach (var p in Pushers)
        {
            if (p.Top < frame.origin.y + d.Y0 || p.Bottom > frame.origin.y + d.Y1) continue;
            var l = Quaternion.Inverse(frame.rot) * (p.Pos - frame.origin);
            var dist = new Vector2(l.x, l.z).magnitude;
            if (dist < 0.01f * k || dist > d.Width + p.R)
            {
                d.Side[p.Id] = 0;
                continue;
            }

            var phi = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
            var delta = Mathf.DeltaAngle(d.Alpha + d.Theta, phi);
            var margin = Mathf.Asin(Mathf.Clamp01((p.R + d.Thick * 0.5f) / dist)) * Mathf.Rad2Deg;
            var side = d.Side[p.Id];
            if (side == 0) side = (sbyte)(delta >= 0 ? 1 : -1);
            var crossed = Mathf.Sign(delta) != side;
            if (p.Jumped)
            {
                d.Side[p.Id] = (sbyte)(delta >= 0 ? 1 : -1);
                continue;
            }

            if (Mathf.Abs(delta) < margin || crossed)
            {
                // The body only pushes a door that's already ajar (a shut door is latched); it always stops one swinging into it.
                var latched = p.Body && !d.GameOpen && Mathf.Abs(d.Omega) < 5f;
                if (locked || latched)
                {
                    // (only a hand moving into it tries the door: one resting there doesn't keep rattling it)
                    var into = Vector3.Dot(p.Vel, frame.rot * Quaternion.Euler(0, d.Alpha + d.Theta - 90f * side, 0) * Vector3.forward);
                    if (locked && !p.Body && into > 0.2f * k) Rattle(d, p.Hand, now, -side);
                }
                else if (_grabDoor != d || p.Body)
                {
                    var t = Mathf.DeltaAngle(0, phi - d.Alpha - side * margin);
                    d.Theta = Mathf.Clamp(t, d.Min, d.Max);
                    var v = (d.Theta - before) / dt;
                    // a push carries the door on; a door swinging into you stops (and bounces a little)
                    d.Omega = p.Body && Mathf.Sign(d.Omega) == -Mathf.Sign(v) ? -d.Omega * 0.15f : Mathf.Lerp(d.Omega, v, 0.6f);
                    pushed = true;
                    if (!p.Body && now >= d.NextTick)
                    {
                        d.NextTick = now + 0.06f;
                        XrInput.Haptic(p.Hand, Mathf.Clamp(0.08f + Mathf.Abs(v) / 400f, 0.08f, 0.5f), 0.02f);
                    }
                }
            }

            delta = Mathf.DeltaAngle(d.Alpha + d.Theta, phi);
            d.Side[p.Id] = (sbyte)(delta >= 0 ? 1 : -1);
        }

        // The stops: the door's own swing range and any wall in the way.
        if (d.Theta > d.Max)
        {
            d.Theta = d.Max;
            if (d.Omega > 0) d.Omega = -d.Omega * 0.25f;
        }
        else if (d.Theta < d.Min)
        {
            d.Theta = d.Min;
            if (d.Omega < 0) d.Omega = -d.Omega * 0.25f;
        }

        // Open and shut, as far as the game is concerned.
        // (a door swinging back through its frame latches shut there: it doesn't swing on through)
        var free = !pushed && _grabDoor != d;
        var through = d.GameOpen && free && before != 0 && Mathf.Sign(before) != Mathf.Sign(d.Theta);
        var settling = d.GameOpen && free && Mathf.Abs(d.Theta) < 2f && (Mathf.Sign(d.Omega) != Mathf.Sign(d.Theta) || Mathf.Abs(d.Omega) < 8f);
        if (!d.GameOpen && Mathf.Abs(d.Theta) > 4f && !locked) TellOpen(d, now);
        else if (through || settling) TellClosed(d, now, Mathf.Abs(d.Omega));

        if (!d.GameOpen && Mathf.Abs(d.Theta) <= 4f && now >= d.RattleUntil && !pushed)
        {
            // a shut door at rest: leave it exactly as the game has it
            if (d.Driving)
            {
                d.Theta = 0;
                d.Omega = 0;
                Apply(d, 0);
                d.Driving = false;
            }

            return;
        }

        d.Driving = true;
        var show = d.Theta;
        if (now < d.RattleUntil) show += Mathf.Sin(now * 70f) * 0.9f * (d.RattleUntil - now) / 0.35f;
        Apply(d, show);
        Creak(d, now);
    }

    private static (Vector3 origin, Quaternion rot) Frame(Door d)
    {
        var parent = d.Hinge.parent;
        return (d.Hinge.position, (parent != null ? parent.rotation : Quaternion.identity) * d.Closed);
    }

    private static float AngleOf(Door d, Quaternion local)
    {
        var rel = Quaternion.Inverse(d.Closed) * local;
        var f = rel * Vector3.forward;
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    private static void Apply(Door d, float theta)
    {
        var q = d.Closed * Quaternion.Euler(0, theta, 0);
        d.Hinge.localRotation = q;
        d.LastSet = q;
    }

    private static bool IsTweening(Door d)
    {
        try
        {
            return DOTween.IsTweening(d.Hinge, false);
        }
        catch
        {
            return false;
        }
    }

    // ---------------- telling the game ----------------

    private static void TellOpen(Door d, float now)
    {
        d.GameOpen = true;
        d.OwnCallUntil = now + 0.2f;
        try
        {
            // The game's own opening (its sound, occlusion and listeners), over a moment, to where the door already is.
            switch (d.Kind)
            {
                case Kind.Base:
                    d.B.Open(0.15f, Ease.Linear, d.Theta);
                    break;
                case Kind.Simple:
                    d.D.Open(0.15f, d.Theta);
                    break;
                default:
                    d.C.Open(0.15f, d.Theta);
                    break;
            }

            // finished at once (its listeners run now), so it never tugs the door against your hand
            DOTween.Complete(d.Hinge, true);
            Apply(d, d.Theta);
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"Door {d.Root.name}: open failed: {e.Message}");
        }

        Last = $"opened {d.Root.name} at {d.Theta:F0}°";
        Plugin.L.LogInfo($"Door {Last}");
    }

    private static void TellClosed(Door d, float now, float speed)
    {
        d.GameOpen = false;
        d.Theta = 0;
        d.Omega = 0;
        d.OwnCallUntil = now + 0.2f;
        Apply(d, 0);
        try
        {
            switch (d.Kind)
            {
                case Kind.Base:
                    d.B.Close(0.15f, Ease.Linear);
                    break;
                case Kind.Simple:
                    d.D.Close(0.15f);
                    break;
                default:
                    d.C.Close(0.15f);
                    break;
            }

            DOTween.Complete(d.Hinge, true);
            Apply(d, 0);
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"Door {d.Root.name}: close failed: {e.Message}");
        }

        // a slam you feel in the hand that's nearest
        if (speed > 60f) XrInput.Haptic(Hand.Right, Mathf.Clamp01(speed / 300f) * 0.4f, 0.05f);
        Last = $"closed {d.Root.name} ({speed:F0}°/s)";
        Plugin.L.LogInfo($"Door {Last}");
    }

    /// <summary>A locked door: it shakes in its frame, and the game responds as if you'd tried the handle.</summary>
    private static void Rattle(Door d, Hand hand, float now, float towards)
    {
        if (now < d.NextLocked) return;
        d.NextLocked = now + 1.4f;
        d.RattleUntil = now + 0.35f;
        XrInput.Haptic(hand, 0.55f, 0.18f);
        d.OwnCallUntil = now + 0.05f;
        try
        {
            switch (d.Kind)
            {
                case Kind.Base:
                    (towards >= 0 ? d.B.m_DoorBack : d.B.m_DoorFront)?.Interact();
                    break;
                case Kind.Simple:
                    d.D.Interact();
                    break;
                default:
                    d.C.m_DoorInteractable?.Interact();
                    break;
            }
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"Door {d.Root.name}: locked response failed: {e.Message}");
        }

        Last = $"locked {d.Root.name}";
        Plugin.L.LogInfo($"Door {Last}");
    }

    private static void Creak(Door d, float now)
    {
        if (d.Kind != Kind.Base || now < d.NextCreak) return;
        var w = Mathf.Abs(d.Omega);
        if (w < 25f || w > 160f || d.B.m_DoorCreakClip == null) return;
        d.NextCreak = now + 3f;
        if (d.Audio == null)
        {
            var go = new GameObject("InkVR_DoorAudio");
            go.transform.SetParent(d.Hinge, false);
            d.Audio = go.AddComponent<AudioSource>();
            d.Audio.playOnAwake = false;
            d.Audio.spatialBlend = 1f;
            d.Audio.minDistance = 1f * VRRig.ActiveScale;
            d.Audio.maxDistance = 15f * VRRig.ActiveScale;
            foreach (var s in GameManager.Instance.Player.GetComponentsInChildren<AudioSource>(true))
                if (s.outputAudioMixerGroup != null)
                {
                    d.Audio.outputAudioMixerGroup = s.outputAudioMixerGroup;
                    break;
                }
        }

        d.Audio.pitch = Mathf.Clamp(0.8f + w / 300f, 0.85f, 1.2f);
        d.Audio.PlayOneShot(d.B.m_DoorCreakClip, 0.7f);
    }

    // ---------------- grabbing ----------------

    /// <summary>A grip that closes on a door leaf (outer part, where the handle is) takes hold of the door.</summary>
    public static bool TryGrab(Hand hand)
    {
        if (!ModConfig.PhysicalDoors.Value || _grabDoor != null) return false;
        var k = VRRig.ActiveScale;
        var (pos, _) = HandGrip(hand);
        Door best = null;
        var bestGap = 0.08f * k;
        var bestOffset = 0f;
        foreach (var d in Doors)
        {
            if (!d.Ready || d.Hinge == null || Time.unscaledTime < d.GameUntil) continue;
            var frame = Frame(d);
            var l = Quaternion.Inverse(frame.rot) * (pos - frame.origin);
            if (l.y < d.Y0 || l.y > d.Y1) continue;
            var dist = new Vector2(l.x, l.z).magnitude;
            if (dist < d.Width * 0.3f || dist > d.Width + 0.05f * k) continue;
            var phi = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
            var delta = Mathf.DeltaAngle(d.Alpha + d.Theta, phi);
            var gap = Mathf.Abs(Mathf.Sin(delta * Mathf.Deg2Rad)) * dist - d.Thick * 0.5f;
            if (Mathf.Abs(delta) > 60f || gap > bestGap) continue;
            best = d;
            bestGap = gap;
            bestOffset = delta;
        }

        if (best == null) return false;
        _grabDoor = best;
        _grabHand = hand;
        _grabOffset = bestOffset;
        _grabStart = Time.unscaledTime;
        XrInput.Haptic(hand, 0.3f, 0.04f);
        Last = $"grabbed {best.Root.name} ({(best.Locked ? "locked" : "unlocked")})";
        Plugin.L.LogInfo($"Door {Last}");
        return true;
    }

    private static void Release()
    {
        if (_grabDoor == null) return;
        Last = $"let go of {_grabDoor.Root.name} at {_grabDoor.Theta:F0}° ({_grabDoor.Omega:F0}°/s)";
        _grabDoor = null;
    }

    public static bool Holding(Hand hand) => _grabDoor != null && _grabHand == hand;

    private static float Grip(Hand hand)
    {
        var dev = hand == Hand.Right ? XrInput.DevRightGrip : XrInput.DevLeftGrip;
        return dev ?? XrInput.Get(hand).Grip;
    }

    private static (Vector3 pos, Quaternion rot) HandGrip(Hand hand)
    {
        var dev = hand == Hand.Right ? Hands.DevRight : Hands.DevLeft;
        var c = XrInput.Get(hand);
        VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, dev?.rot ?? c.GripRotation, out var p, out var r);
        return (p, r);
    }

    // ---------------- dev ----------------

    /// <summary>Dev: the game's own door opening (or X on its front), with the hinge angle logged as it moves.</summary>
    public static void DevGameOpen(string how, float speed, float angle)
    {
        DevNearest(out var hinge);
        var d = Doors.Find(x => x.Hinge == hinge);
        if (d == null) return;
        d.GameUntil = Time.unscaledTime + 3f;
        switch (how)
        {
            case "x":
                if (d.Kind == Kind.Base) d.B.m_DoorFront.Interact();
                else if (d.Kind == Kind.Simple) d.D.Interact();
                else d.C.m_DoorInteractable.Interact();
                break;
            case "close":
                if (d.Kind == Kind.Base) d.B.Close(speed, Ease.Linear);
                break;
            case "force":
                if (d.Kind == Kind.Base) d.B.ForceOpen(angle, false);
                break;
            case "forceclose":
                if (d.Kind == Kind.Base) d.B.ForceClose();
                break;
            case "internal":
                if (d.Kind == Kind.Base) d.B.InternalOpen(speed, Ease.Linear, angle);
                break;
            default:
                if (d.Kind == Kind.Base) d.B.Open(speed, Ease.Linear, angle);
                else if (d.Kind == Kind.Simple) d.D.Open(speed, angle);
                else d.C.Open(speed, angle);
                break;
        }

        _probe = (hinge, Time.unscaledTime, 0);
    }

    private static (Transform hinge, float t0, int n) _probe;

    private static void Probe()
    {
        if (_probe.hinge == null) return;
        var t = Time.unscaledTime - _probe.t0;
        if (t > 2.5f)
        {
            _probe.hinge = null;
            return;
        }

        if (t < _probe.n * 0.1f) return;
        _probe.n++;
        var d = Doors.Find(x => x.Hinge == _probe.hinge);
        Plugin.L.LogInfo($"DOORPROBE t={t:F2} lrot={_probe.hinge.localEulerAngles} open={d?.OpenInGame} locked={d?.Locked} tween={(d != null && IsTweening(d))}");
    }

    /// <summary>Dev: the right hand to a point on the nearest door's leaf: u along it (0 hinge, 1 edge), h metres above the hinge's middle, off metres out on your side.</summary>
    public static string DevHand(float u, float h, float off, float seconds)
    {
        DevNearest(out var hinge);
        var d = Doors.Find(x => x.Hinge == hinge);
        var player = GameManager.Instance?.Player;
        if (d == null || !d.Ready || player == null || VRRig.ActiveOrigin == null) return "no measured door near";
        var k = VRRig.ActiveScale;
        var frame = Frame(d);
        var along = frame.rot * Quaternion.Euler(0, d.Alpha + d.Theta, 0) * Vector3.forward;
        var lp = Quaternion.Inverse(frame.rot) * (player.transform.position - frame.origin);
        var side = Mathf.DeltaAngle(d.Alpha + d.Theta, Mathf.Atan2(lp.x, lp.z) * Mathf.Rad2Deg) >= 0 ? 1f : -1f;
        var normal = frame.rot * Quaternion.Euler(0, d.Alpha + d.Theta + 90f * side, 0) * Vector3.forward;
        var mid = (d.Y0 + d.Y1) * 0.5f;
        var p = frame.origin + along * (u * d.Width) + Vector3.up * (mid + h * k) + normal * (off * k + d.Thick * 0.5f);
        // the grip sits a few centimetres behind the palm
        var grip = p + normal * (0.05f * k);
        var local = VRRig.ActiveOrigin.InverseTransformPoint(grip) - Vector3.up * VRRig.HeightLift;
        Dev.DevRecorder.HandGoTo(local, seconds);
        return $"hand to {d.Root.name} u={u} h={h} off={off} (tracking {local})";
    }

    /// <summary>Dev: a recording camera on the given side of the nearest door, off towards its hinge or edge, looking at the doorway.</summary>
    public static string DevCam(int side, float metres, float u, float up)
    {
        DevNearest(out var hinge);
        var d = Doors.Find(x => x.Hinge == hinge);
        if (d == null || !d.Ready) return "no measured door near";
        var k = VRRig.ActiveScale;
        var frame = Frame(d);
        var along = frame.rot * Quaternion.Euler(0, d.Alpha, 0) * Vector3.forward;
        var normal = frame.rot * Quaternion.Euler(0, d.Alpha + 90f * side, 0) * Vector3.forward;
        var centre = frame.origin + along * (d.Width * 0.5f);
        var pos = frame.origin + along * (d.Width * u) + normal * (metres * k) + Vector3.up * (up * k);
        Dev.DevRecorder.SetFixed(pos, centre - Vector3.up * (0.1f * k));
        return $"camera {metres} m out on side {side}";
    }

    /// <summary>Dev: stand in front of the nearest door's leaf (side +1/-1), this far away, facing it.</summary>
    public static string DevStand(float metres, int side)
    {
        DevNearest(out var hinge);
        var d = Doors.Find(x => x.Hinge == hinge);
        if (d == null || !d.Ready) return "no measured door near";
        var k = VRRig.ActiveScale;
        var frame = Frame(d);
        var along = frame.rot * Quaternion.Euler(0, d.Alpha, 0) * Vector3.forward;
        var normal = frame.rot * Quaternion.Euler(0, d.Alpha + 90f * side, 0) * Vector3.forward;
        var centre = frame.origin + along * (d.Width * 0.5f);
        var stand = centre + normal * (metres * k);
        var player = GameManager.Instance.Player;
        var cc = player.CharacterController;
        cc.enabled = false;
        player.transform.position = new Vector3(stand.x, player.transform.position.y, stand.z);
        cc.enabled = true;
        VRRig.DevLookAt = new Vector3(centre.x, centre.y, centre.z);
        return $"standing {metres} m in front of {d.Root.name} (side {side})";
    }

    /// <summary>Dev: every door by distance, with its lock and path.</summary>
    public static string DevList()
    {
        var player = GameManager.Instance?.Player;
        if (player == null) return "no player";
        if (Doors.Count == 0) Scan();
        var sb = new System.Text.StringBuilder();
        foreach (var d in Doors)
        {
            if (d.Hinge == null) continue;
            var dist = (d.Hinge.position - player.transform.position).magnitude / VRRig.ActiveScale;
            sb.Append($"\n  {dist,6:F1} m {d.Kind} locked={d.Locked} open={d.OpenInGame} active={d.Hinge.gameObject.activeInHierarchy} {Dev.DevTools.PathOf(d.Root)}");
        }

        return sb.ToString();
    }

    /// <summary>Dev: the nearest door, measured, with its state.</summary>
    public static string DevNearest(out Transform hinge)
    {
        hinge = null;
        var player = GameManager.Instance?.Player;
        if (player == null) return "no player";
        if (Doors.Count == 0) Scan();
        Door best = null;
        var bd = float.MaxValue;
        foreach (var d in Doors)
        {
            if (d.Hinge == null || !d.Hinge.gameObject.activeInHierarchy) continue;
            var dd = (d.Hinge.position - player.transform.position).sqrMagnitude;
            if (dd < bd)
            {
                bd = dd;
                best = d;
            }
        }

        if (best == null) return "no doors";
        hinge = best.Hinge;
        var info = new System.Text.StringBuilder();
        if (best.Ready)
        {
            GatherPushers(player, VRRig.ActiveScale);
            var frame = Frame(best);
            foreach (var p in Pushers)
            {
                var l = Quaternion.Inverse(frame.rot) * (p.Pos - frame.origin);
                var phi = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
                info.Append($" | p{p.Id} local=({l.x / VRRig.ActiveScale:F2},{l.y / VRRig.ActiveScale:F2},{l.z / VRRig.ActiveScale:F2}) m r={new Vector2(l.x, l.z).magnitude / VRRig.ActiveScale:F2} delta={Mathf.DeltaAngle(best.Alpha + best.Theta, phi):F0} y={(p.Bottom - frame.origin.y) / VRRig.ActiveScale:F2}..{(p.Top - frame.origin.y) / VRRig.ActiveScale:F2}");
            }

            info.Append($" | leafY={best.Y0 / VRRig.ActiveScale:F2}..{best.Y1 / VRRig.ActiveScale:F2}");
        }
        return $"{best.Kind} {best.Root.name} at {Mathf.Sqrt(bd) / VRRig.ActiveScale:F2} m: ready={best.Ready} locked={best.Locked} open(game)={best.OpenInGame} ours={best.GameOpen} theta={best.Theta:F1} omega={best.Omega:F0} " +
               $"range={best.Min:F0}..{best.Max:F0} leaf={best.Width / VRRig.ActiveScale:F2}m alpha={best.Alpha:F0} hinge lrot={best.Hinge.localEulerAngles} last='{Last}'" + info;
    }
}
