using System.Collections.Generic;
using InkVR.Body;
using UnityEngine;

namespace InkVR.UI;

/// <summary>What the booth's switchboard is set to (the show reads these).</summary>
public static class TheatreState
{
    public static bool HouseUp, CurtainOpen = true, SpotOn, MarqueeOn = true;

    /// <summary>The follow spot is the booth's (aimed by hand), not the show's.</summary>
    public static bool SpotManual;
}

/// <summary>
/// The 1.0 rooms come alive: doors swing open as you come to them, the wardrobe dresses you (take a costume and you're
/// wearing it), the watch case puts a watch on your wrist, the ink machine starts when you pull its lever, the
/// projectors' reels turn, the follow spot aims where you push it, the switchboard runs the house lights, the curtain,
/// the spot and the marquee, the sandbags sway, the rewind reel spins, writing seeps out of the cellar wall.
/// </summary>
public static class TheatreLife
{
    private static Cinema _cinema;
    private static Theatre _t;
    private static bool _ready;

    // which handle each hand has hold of (null: none)
    private static readonly string[] Claim = new string[2];
    private static readonly bool[] GripWas = new bool[2];

    public static void Reset()
    {
        Claim[0] = Claim[1] = null;
        _machineOn = false;
        _lever = 0;
        _cellarWriting = 0;
        _lookedCellar = 0;
        TheatreState.SpotManual = false;
        Arcade.Reset();
    }

    public static void Idle()
    {
        Claim[0] = Claim[1] = null;
        TheatreSounds.StopLoops();
        Progress.Tick();
    }

    private static bool Setup(Cinema cinema)
    {
        if (_ready && _cinema == cinema && _t != null && cinema.Root != null) return true;
        _cinema = cinema;
        _t = cinema.Theatre;
        if (_t == null) return false;
        _ready = true;
        return true;
    }

    public static void Update(Cinema cinema)
    {
        if (cinema == null || !Setup(cinema) || !cinema.Root.gameObject.activeInHierarchy) return;
        var dt = Mathf.Clamp(Time.unscaledDeltaTime, 1e-3f, 0.1f);
        var now = Time.unscaledTime;
        for (var h = 0; h < 2; h++)
        {
            var g = TheatreToys.GripOf(h);
            if (!g) Claim[h] = null;
            _gripEdge[h] = g && !GripWas[h];
            GripWas[h] = g;
        }

        Doors(dt);
        Wardrobe(dt);
        WatchCase();
        InkMachine(dt, now);
        Booth(dt, now);
        Swaying(dt, now);
        CellarWriting(dt);
        Arcade.Update(cinema, dt, now);
        Progress.Tick();
    }

    private static readonly bool[] _gripEdge = new bool[2];

    /// <summary>A hand that has just closed on this spot (within r of the palm, and free), or -1.</summary>
    private static int TakeHold(Vector3 at, float r, string id)
    {
        for (var h = 0; h < 2; h++)
        {
            if (!_gripEdge[h] || Claim[h] != null || TheatreToys.Holding(h)) continue;
            if ((TheatreToys.PalmOf(h) - at).sqrMagnitude > r * r) continue;
            Claim[h] = id;
            TheatreToys.BuzzOf(h, 0.3f, 0.03f);
            return h;
        }

        return -1;
    }

    private static int Holder(string id) => Claim[0] == id ? 0 : Claim[1] == id ? 1 : -1;

    /// <summary>For the Fun Hall: a free hand closing on this spot takes hold of it (or -1).</summary>
    internal static int Grab(Vector3 at, float r, string id) => TakeHold(at, r, id);

    /// <summary>Which hand has hold of this (or -1).</summary>
    internal static int HolderOf(string id) => Holder(id);

    // =====================================================================================================
    // Doors
    // =====================================================================================================

    private static readonly Dictionary<string, float> DoorOpen = new();

    private static void Doors(float dt)
    {
        Door("DressingDoor", Theatre.DressingDoorHinge + new Vector3(0.5f, 0, 0), -100f, dt);
        Door("BoothDoor", Theatre.BoothDoorHinge + new Vector3(0.4f, 0, 0), 100f, dt);
    }

    private static void Door(string name, Vector3 middle, float swing, float dt)
    {
        var t = _t.Piece(name);
        if (t == null) return;
        var eye = TheatreWalk.Eye;
        var near = new Vector2(eye.x - middle.x, eye.z - middle.z).magnitude < 1.7f && Mathf.Abs(TheatreWalk.Floor - middle.y) < 0.7f;
        DoorOpen.TryGetValue(name, out var open);
        var was = open;
        open = Mathf.MoveTowards(open, near ? 1f : 0f, dt * (near ? 1.4f : 0.8f));
        DoorOpen[name] = open;
        if (near && was <= 0.001f) TheatreSounds.Play(TheatreSounds.Creak, middle + Vector3.up * 1.1f, 0.4f, Random.Range(0.95f, 1.15f));
        if (!near && was > 0.02f && open <= 0.02f) TheatreSounds.Play(TheatreSounds.Thud, middle + Vector3.up * 1.0f, 0.3f, 1.2f);
        t.localRotation = Quaternion.Euler(0, Mathf.SmoothStep(0, 1, open) * swing, 0);
    }

    // =====================================================================================================
    // The wardrobe and the watch case
    // =====================================================================================================

    private static readonly float[] Swing = new float[Theatre.WardrobeCount], SwingVel = new float[Theatre.WardrobeCount];

    private static void Wardrobe(float dt)
    {
        var current = Styles.Current;
        for (var i = 0; i < Theatre.WardrobeCount; i++)
        {
            var piece = _t.Piece("Costume" + i);
            if (piece == null) continue;
            var style = Styles.All[i];
            // the one you're wearing isn't on the rail
            var wearing = style == current && ModConfig.FullBody;
            if (piece.gameObject.activeSelf == wearing) piece.gameObject.SetActive(!wearing);
            if (wearing) continue;

            var hook = Theatre.CostumeHook(i);
            var middle = hook + new Vector3(0.02f, -0.5f, 0);
            // brushing past sets it swinging
            for (var h = 0; h < 2; h++)
            {
                if (!TheatreToys.HandOk(h)) continue;
                var d = TheatreToys.PalmOf(h) - middle;
                if (Mathf.Abs(d.y) < 0.55f && new Vector2(d.x, d.z).magnitude < 0.22f) SwingVel[i] += -TheatreToys.PalmVelOf(h).z * dt * 60f;
            }

            SwingVel[i] += (-Swing[i] * 30f - SwingVel[i] * 2.2f) * dt;
            Swing[i] = Mathf.Clamp(Swing[i] + SwingVel[i] * dt, -35f, 35f);
            piece.localRotation = Quaternion.Euler(Swing[i], 0, 0);

            // take hold of it: you're wearing it
            if (TakeHold(middle, 0.24f, "Costume" + i) < 0) continue;
            ModConfig.BodyStyle.Value = style.Key;
            TheatreSounds.Play(TheatreSounds.Chime, middle, 0.5f, 1.2f);
            Plugin.L.LogInfo($"Wardrobe: wearing {style.Label}");
        }
    }

    private static readonly string[] WatchKeys = { "Tank", "Bendy", "Studio", "Gauge", "Angel", "Demon", "None" };

    private static void WatchCase()
    {
        for (var i = 0; i < Theatre.WatchCount; i++)
        {
            var piece = _t.Piece("Watch" + i);
            if (piece == null) continue;
            var on = ModConfig.Watch.Value == WatchKeys[i];
            if (piece.gameObject.activeSelf == on) piece.gameObject.SetActive(!on);
            if (on) continue;
            var at = Theatre.WatchSpot(i);
            var took = TakeHold(at, 0.09f, "Watch" + i);
            if (took < 0)
            {
                // or a fingertip on it
                for (var h = 0; h < 2 && took < 0; h++)
                    if (TheatreToys.HandOk(h) && (TheatreToys.TipOf(h) - at).sqrMagnitude < 0.035f * 0.035f && TheatreToys.TipVelOf(h).y < -0.05f)
                        took = h;
                if (took < 0) continue;
            }

            ModConfig.Watch.Value = WatchKeys[i];
            TheatreSounds.Play(TheatreSounds.Click, at, 0.5f, 1.6f);
            TheatreToys.BuzzOf(took, 0.4f, 0.05f);
            Plugin.L.LogInfo($"Watch case: wearing the {WatchKeys[i]} watch");
        }
    }

    // =====================================================================================================
    // The ink machine
    // =====================================================================================================

    private static bool _machineOn;
    private static float _lever, _run, _wheel, _phase, _hissAt;

    public static bool MachineRunning => _machineOn;

    private static void InkMachine(float dt, float now)
    {
        var lever = _t.Piece("MachineLever");
        if (lever == null) return;
        var pivot = Theatre.MachineLever;
        var knob = pivot + Quaternion.Euler(0, 0, -_lever) * new Vector3(0, 0.5f, 0);
        var h = Holder("MachineLever");
        if (h < 0) h = TakeHold(knob, 0.14f, "MachineLever");
        if (h >= 0)
        {
            // the lever follows the hand: upright is off, pulled down towards you is on
            var d = TheatreToys.PalmOf(h) - pivot;
            _lever = Mathf.Clamp(Mathf.Atan2(d.x, Mathf.Max(0.01f, d.y)) * Mathf.Rad2Deg, 0f, 70f);
        }
        else _lever = Mathf.MoveTowards(_lever, _machineOn ? 70f : 0f, dt * 220f);

        var want = _lever > 40f;
        if (want != _machineOn)
        {
            _machineOn = want;
            TheatreSounds.Play(TheatreSounds.Clank, pivot, 0.7f, 0.7f);
            TheatreSounds.Play(TheatreSounds.Hiss, Theatre.MachineAt + Vector3.up * 2.5f, 0.6f, 0.8f);
            if (h >= 0) TheatreToys.BuzzOf(h, 0.7f, 0.12f);
            Plugin.L.LogInfo("Ink machine " + (want ? "started" : "stopped"));
        }

        lever.localRotation = Quaternion.Euler(0, 0, -_lever);

        // spin up and wind down
        _run = Mathf.MoveTowards(_run, _machineOn ? 1f : 0f, dt * (_machineOn ? 0.35f : 0.25f));
        var speed = _run * 1.4f; // strokes a second
        var was = _phase;
        _phase += speed * dt;
        if (_run > 0.2f && Mathf.Floor(_phase) != Mathf.Floor(was)) TheatreSounds.Play(TheatreSounds.Thump, Theatre.MachineAt + Vector3.up, 0.35f + 0.4f * _run, 0.9f + 0.15f * _run);
        if (_run > 0.6f && now > _hissAt)
        {
            TheatreSounds.Play(TheatreSounds.Hiss, Theatre.MachineAt + new Vector3(0, 2.9f, 0), 0.25f, Random.Range(0.8f, 1.1f));
            _hissAt = now + Random.Range(3f, 7f);
        }

        _wheel += speed * 220f * dt;
        var wheel = _t.Piece("Flywheel");
        if (wheel != null) wheel.localRotation = Quaternion.Euler(0, 0, -_wheel);
        for (var i = 0; i < 2; i++)
        {
            var p = _t.Piece("Piston" + i);
            if (p == null) continue;
            var y = (Mathf.Sin((_phase + i * 0.5f) * Mathf.PI * 2) * 0.5f + 0.5f) * 0.18f * _run;
            p.localPosition = _t.PivotOf("Piston" + i) + Vector3.up * y;
        }

        for (var i = 0; i < 3; i++)
        {
            var n = _t.Piece("Needle" + i);
            if (n == null) continue;
            var target = -110f + _run * (150f + 20f * Mathf.Sin(now * (1.3f + i * 0.7f) + i));
            n.localRotation = Quaternion.Euler(target, 0, 0);
        }

        var ink = _t.Piece("MachineInk");
        if (ink != null) ink.localScale = new Vector3(1f, 1f + Mathf.Sin(now * 5f) * 0.06f * _run, 1f + Mathf.Sin(now * 3.7f) * 0.05f * _run);

        // the cellar's light throbs with the pump; the machine hums while it runs
        var throb = 1f + 0.1f * _run * Mathf.Sin(_phase * Mathf.PI * 2);
        _t.SetRoomTint(Theatre.Room.Basement, new Color(throb, throb * 0.98f, throb * 0.95f, 1f));
        TheatreSounds.Loop("machine", TheatreSounds.Whirr, Theatre.MachineAt + Vector3.up * 1.5f, 0.22f * _run, 0.35f);
    }

    // =====================================================================================================
    // The booth: reels, the follow spot, the switchboard, the rewind bench, the clock
    // =====================================================================================================

    private static float _reels, _spotYaw, _spotPitch = -14f, _rewind, _rewindVel, _rewindTick;
    private static readonly float[] Switch = { -60f, 60f, -60f, 60f };

    private static void Booth(float dt, float now)
    {
        // the projector runs whenever there's a picture: its reels turn, and you hear it in the booth
        _reels += dt * 160f;
        for (var r = 0; r < 2; r++)
        {
            var reel = _t.Piece("ReelMain" + r);
            if (reel != null) reel.localRotation = Quaternion.Euler(-_reels * (r == 0 ? 0.8f : 1.4f), 0, 0);
        }

        var inBooth = Theatre.RoomOf(TheatreWalk.Eye, Vector3.zero) == Theatre.Room.Booth;
        TheatreSounds.Loop("projector", TheatreSounds.Whirr, new Vector3(0.2f, Theatre.Projector.y, -15f), inBooth ? 0.16f : 0f, 1f);

        Switchboard(dt);
        FollowSpot(dt);

        // the rewind reel: spin it with your hand, it runs down
        var rew = _t.Piece("RewindReel");
        if (rew != null)
        {
            var c = _t.PivotOf("RewindReel");
            for (var h = 0; h < 2; h++)
            {
                if (!TheatreToys.HandOk(h)) continue;
                var d = TheatreToys.PalmOf(h) - c;
                if (Mathf.Abs(d.x) > 0.12f || new Vector2(d.y, d.z).magnitude > 0.28f) continue;
                var v = TheatreToys.PalmVelOf(h);
                var tangential = (d.y * v.z - d.z * v.y) / Mathf.Max(0.05f, d.y * d.y + d.z * d.z);
                _rewindVel = Mathf.Lerp(_rewindVel, tangential * Mathf.Rad2Deg, 0.3f);
            }

            _rewindVel *= Mathf.Exp(-dt * 0.8f);
            _rewind += _rewindVel * dt;
            rew.localRotation = Quaternion.Euler(-_rewind, 0, 0);
            _rewindTick += Mathf.Abs(_rewindVel) * dt;
            if (_rewindTick > 45f)
            {
                _rewindTick = 0;
                TheatreSounds.Play(TheatreSounds.Click, c, 0.12f, 2.2f);
            }
        }

        // the clock over the lobby doors keeps your time
        var time = System.DateTime.Now;
        var hour = _t.Piece("BoothClockHour");
        var minute = _t.Piece("BoothClockMinute");
        if (hour != null) hour.localRotation = Quaternion.Euler(0, 0, -((time.Hour % 12) + time.Minute / 60f) * 30f);
        if (minute != null) minute.localRotation = Quaternion.Euler(0, 0, -(time.Minute + time.Second / 60f) * 6f);
    }

    private static void Switchboard(float dt)
    {
        for (var i = 0; i < Theatre.SwitchCount; i++)
        {
            var piece = _t.Piece("Switch" + i);
            if (piece == null) continue;
            var hinge = Theatre.SwitchHinge(i);
            var id = "Switch" + i;
            var handle = hinge + Quaternion.Euler(0, 0, Switch[i]) * new Vector3(0.3f, 0, 0);
            var h = Holder(id);
            if (h < 0) h = TakeHold(handle, 0.12f, id);
            var on = State(i);
            if (h >= 0)
            {
                var d = TheatreToys.PalmOf(h) - hinge;
                Switch[i] = Mathf.Clamp(Mathf.Atan2(d.y, Mathf.Max(0.02f, d.x)) * Mathf.Rad2Deg, -60f, 60f);
                var flip = Switch[i] > 25f ? true : Switch[i] < -25f ? false : on;
                if (flip != on)
                {
                    SetState(i, flip);
                    TheatreSounds.Play(TheatreSounds.Click, hinge, 0.55f);
                    TheatreToys.BuzzOf(h, 0.5f, 0.05f);
                    Plugin.L.LogInfo($"Switchboard: {Theatre.SwitchNames[i]} {(flip ? "on" : "off")}");
                }
            }
            else Switch[i] = Mathf.MoveTowards(Switch[i], on ? 60f : -60f, dt * 300f);

            piece.localRotation = Quaternion.Euler(0, 0, Switch[i]);
        }
    }

    private static bool State(int i) => i switch
    {
        0 => TheatreState.HouseUp,
        1 => TheatreState.CurtainOpen,
        2 => TheatreState.SpotOn,
        _ => TheatreState.MarqueeOn
    };

    private static void SetState(int i, bool on)
    {
        switch (i)
        {
            case 0:
                TheatreState.HouseUp = on;
                break;
            case 1:
                TheatreState.CurtainOpen = on;
                break;
            case 2:
                TheatreState.SpotOn = on;
                break;
            default:
                TheatreState.MarqueeOn = on;
                break;
        }
    }

    private static void FollowSpot(float dt)
    {
        var piece = _t.Piece("FollowSpot");
        if (piece == null) return;
        var pivot = Theatre.FollowSpotPivot;
        var rot = Quaternion.Euler(-_spotPitch, _spotYaw, 0);
        var handle = pivot + rot * new Vector3(0, 0, -0.72f);
        var h = Holder("FollowSpot");
        if (h < 0) h = TakeHold(handle, 0.14f, "FollowSpot");
        if (h >= 0)
        {
            // push the handle and the nose goes the other way, as on a real one
            var d = pivot - TheatreToys.PalmOf(h);
            if (d.sqrMagnitude > 0.01f)
            {
                _spotYaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -26f, 26f);
                _spotPitch = Mathf.Clamp(Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -30f, 2f);
            }
        }

        piece.localRotation = Quaternion.Euler(-_spotPitch, _spotYaw, 0);
        TheatreState.SpotManual = TheatreState.SpotOn;
        if (!TheatreState.SpotOn)
        {
            if (_lastSpotOn) _t.SetSpot(0, Vector3.zero, 0f, 1f);
            _lastSpotOn = false;
            return;
        }

        _lastSpotOn = true;
        var dir = Quaternion.Euler(-_spotPitch, _spotYaw, 0) * Vector3.forward;
        var nose = pivot + dir * 0.42f;
        _t.AimSpot(0, nose, dir, 0.95f);
    }

    private static bool _lastSpotOn;

    // =====================================================================================================
    // Small things moving: sandbags, Alice's halo; the writing on the cellar wall
    // =====================================================================================================

    private static void Swaying(float dt, float now)
    {
        for (var i = 0; i < 14; i++)
        {
            var bag = _t.Piece("Sandbag" + i);
            if (bag == null) continue;
            bag.localRotation = Quaternion.Euler(Mathf.Sin(now * 0.6f + i) * 1.5f, 0, Mathf.Sin(now * 0.45f + i * 1.7f) * 2f);
        }

        var halo = _t.Piece("AliceHalo");
        if (halo != null)
        {
            halo.localRotation = Quaternion.Euler(Mathf.Sin(now * 1.1f) * 6f, now * 25f, 0);
            halo.localPosition = _t.PivotOf("AliceHalo") + Vector3.up * (Mathf.Sin(now * 1.7f) * 0.02f);
        }
    }

    private static float _cellarWriting, _lookedCellar;

    private static void CellarWriting(float dt)
    {
        var tmp = _cinema.Sign("CellarWriting");
        if (tmp == null) return;
        var at = tmp.transform.localPosition;
        var inCellar = Theatre.RoomOf(TheatreWalk.Eye, Vector3.zero) == Theatre.Room.Basement;
        if (_cellarWriting <= 0 && inCellar && TheatreToys.Sees(at, 24f, 10f)) _lookedCellar += dt;
        else if (_cellarWriting <= 0) _lookedCellar = Mathf.Max(0, _lookedCellar - dt);
        if (_cellarWriting <= 0 && _lookedCellar > 0.8f)
        {
            _cellarWriting = 0.001f;
            TheatreSounds.Play(TheatreSounds.Seep, at, 0.8f, 0.85f);
        }

        if (_cellarWriting > 0 && _cellarWriting < 1) _cellarWriting = Mathf.Min(1, _cellarWriting + dt / 4f);
        var a = Mathf.SmoothStep(0, 1, _cellarWriting);
        var c = tmp.color;
        if (Mathf.Abs(c.a - a) > 1e-3f) tmp.color = new Color(c.r, c.g, c.b, a);
    }
}
