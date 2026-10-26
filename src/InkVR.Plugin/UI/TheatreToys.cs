using System.Collections.Generic;
using InkVR.Body;
using InkVR.Core;
using InkVR.Input;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The things to find in the theatre: the auditorium doors swing open as you come to them; the desk bell rings;
/// the piano plays (and sometimes, when your back's turned, plays itself); Bacon Soup cans and a little Bendy doll
/// can be picked up and thrown; locked doors rattle, and something knocks back; the cardboard Bendy moves when
/// you aren't looking; writing seeps out of the wall when you look at it. All in the theatre's own space.
/// </summary>
public static class TheatreToys
{
    private static Cinema _cinema;
    private static Theatre _t;
    private static Transform _root;
    private static bool _ready;

    // hands, in the theatre
    private static readonly Vector3[] Tip = new Vector3[2], Palm = new Vector3[2], PrevTip = new Vector3[2], PrevPalm = new Vector3[2];
    private static readonly Vector3[] PalmVel = new Vector3[2], TipVel = new Vector3[2];
    private static readonly Quaternion[] HandRot = new Quaternion[2];
    private static readonly bool[] HandValid = new bool[2], GripWas = new bool[2];
    private static float _prevT;

    /// <summary>Back where everything was (the theatre opened again).</summary>
    public static void Reset()
    {
        if (!_ready) return;
        foreach (var g in Grabs)
        {
            g.Hand = -1;
            g.Flying = false;
            g.Home();
        }

        Grips.Holding(Hand.Left, 0);
        Grips.Holding(Hand.Right, 0);
        _doorOpen = 0;
        _writing = 0;
        _lookedAtWriting = 0;
        _cutout = 0;
        _ghostNext = Time.unscaledTime + 25f;
    }

    /// <summary>The theatre isn't showing: let go of anything held.</summary>
    public static void Idle()
    {
        if (!_ready) return;
        foreach (var g in Grabs)
            if (g.Hand >= 0)
            {
                Grips.Holding((Hand)g.Hand, 0);
                g.Hand = -1;
            }
    }

    private static bool Setup(Cinema cinema)
    {
        if (_ready && _cinema == cinema) return true;
        _cinema = cinema;
        _t = cinema.Theatre;
        _root = cinema.Root;
        if (_t == null || _root == null) return false;
        Grabs.Clear();
        for (var i = 0; i < Theatre.CanCount; i++)
        {
            var p = _t.Piece("Can" + i);
            if (p != null) Grabs.Add(new Grab { T = p, Rest = p.localPosition, RestRot = Quaternion.identity, Radius = Theatre.CanRadius, Bottom = Theatre.CanHeight * 0.5f, Grip = Theatre.CanRadius, Can = true });
        }

        var doll = _t.Piece("Doll");
        if (doll != null) Grabs.Add(new Grab { T = doll, Rest = doll.localPosition, RestRot = Quaternion.identity, Radius = 0.08f, Bottom = 0f, Centre = 0.15f, Grip = 0.045f, Doll = true });
        SetupKeys();
        _ghostNext = Time.unscaledTime + 25f;
        TheatreSounds.Prewarm();
        _ready = true;
        return true;
    }

    public static void Update(Cinema cinema)
    {
        if (cinema == null || !Setup(cinema)) return;
        if (!_root.gameObject.activeInHierarchy) return;
        var now = Time.unscaledTime;
        var dt = Mathf.Clamp(now - _prevT, 1e-3f, 0.1f);
        _prevT = now;
        SampleHands(dt);

        UpdateDoors(dt);
        UpdateBell(now);
        UpdateKeys(dt, now);
        UpdateGrabs(dt);
        UpdateLockedDoors(now);
        UpdateCutout(now);
        UpdateWriting(dt);
    }

    // ---------------- hands ----------------

    private static void SampleHands(float dt)
    {
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var h = (int)hand;
            var dev = hand == Hand.Right ? Hands.DevRight : Hands.DevLeft;
            var c = XrInput.Get(hand);
            HandValid[h] = dev.HasValue || (XrInput.Ready && c.PoseValid);
            if (!HandValid[h]) continue;
            VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, dev?.rot ?? c.GripRotation, out var p, out var r);
            var inv = Quaternion.Inverse(_root.rotation);
            var lr = inv * r;
            var lp = _root.InverseTransformPoint(p);
            HandRot[h] = lr;
            PrevTip[h] = Tip[h];
            PrevPalm[h] = Palm[h];
            Tip[h] = lp + lr * new Vector3(0, -0.02f, 0.12f);
            Palm[h] = lp + lr * new Vector3(hand == Hand.Right ? -0.02f : 0.02f, -0.02f, 0.05f);
            var tv = (Tip[h] - PrevTip[h]) / dt;
            var pv = (Palm[h] - PrevPalm[h]) / dt;
            if (tv.magnitude > 9f) tv = Vector3.zero; // a jump (turning, a teleport)
            if (pv.magnitude > 9f) pv = Vector3.zero;
            TipVel[h] = Vector3.Lerp(TipVel[h], tv, 0.5f);
            PalmVel[h] = Vector3.Lerp(PalmVel[h], pv, 0.5f);
        }
    }

    private static bool Gripping(int h)
    {
        var dev = h == 1 ? Hands.DevRight : Hands.DevLeft;
        if (dev.HasValue) return DevGrip[h];
        return XrInput.Ready && XrInput.Get((Hand)h).GripPressed;
    }

    /// <summary>Dev: hold the grip on the mock runtime.</summary>
    public static readonly bool[] DevGrip = new bool[2];

    private static void Buzz(int h, float amp, float sec)
    {
        if (XrInput.Ready) XrInput.Haptic((Hand)h, amp, sec);
    }

    private static bool InView(Vector3 p, float maxAngle, float maxDist)
    {
        var to = p - TheatreWalk.Eye;
        var d = to.magnitude;
        if (d > maxDist) return false;
        var head = VRRig.Head;
        if (head == null) return true;
        var fwd = Quaternion.Inverse(_root.rotation) * head.forward;
        return Vector3.Angle(fwd, to) < maxAngle;
    }

    // ---------------- the auditorium doors ----------------

    private static float _doorOpen;
    private static bool _doorWasOpening;

    private static void UpdateDoors(float dt)
    {
        var l = _t.Piece("DoorL");
        var r = _t.Piece("DoorR");
        if (l == null || r == null) return;
        var d = Theatre.LobbyDoorway;
        var eye = TheatreWalk.Eye;
        var near = new Vector2(eye.x - d.x, eye.z - d.z).magnitude < 2.4f && Mathf.Abs(TheatreWalk.Floor - d.y) < 0.6f;
        // (anything thrown through keeps them open too)
        foreach (var g in Grabs)
            if (g.Flying && new Vector2(g.T.localPosition.x - d.x, g.T.localPosition.z - d.z).magnitude < 2.0f)
                near = true;
        var target = near ? 1f : 0f;
        var was = _doorOpen;
        _doorOpen = Mathf.MoveTowards(_doorOpen, target, dt * (near ? 1.5f : 0.9f));
        if (near && !_doorWasOpening && _doorOpen < 0.3f) TheatreSounds.Play(TheatreSounds.Creak, d + Vector3.up * 1.2f, 0.55f, Random.Range(0.9f, 1.1f));
        if (!near && was > 0.02f && _doorOpen <= 0.02f) TheatreSounds.Play(TheatreSounds.Thud, d + Vector3.up * 1.0f, 0.5f);
        _doorWasOpening = near;
        var a = Mathf.SmoothStep(0, 1, _doorOpen) * 100f;
        l.localRotation = Quaternion.Euler(0, a, 0);
        r.localRotation = Quaternion.Euler(0, -a, 0);
    }

    // ---------------- the desk bell ----------------

    private static bool _bellDown;
    private static float _bellPush;

    private static void UpdateBell(float now)
    {
        var plunger = _t.Piece("Bell");
        if (plunger == null) return;
        var top = Theatre.BellTop;
        var depth = 0f;
        var speed = 0f;
        var by = -1;
        void Try(Vector3 p, Vector3 v, float r, int h)
        {
            if (new Vector2(p.x - top.x, p.z - top.z).magnitude > r) return;
            var d = top.y + 0.004f - p.y;
            if (d <= 0 || d > 0.06f || d <= depth) return;
            depth = d;
            speed = -v.y;
            by = h;
        }

        for (var h = 0; h < 2; h++)
        {
            if (!HandValid[h]) continue;
            Try(Tip[h], TipVel[h], 0.022f, h);
            Try(Palm[h], PalmVel[h], 0.04f, h);
        }

        var down = depth > 0.003f;
        if (down && !_bellDown)
        {
            TheatreSounds.Play(TheatreSounds.Bell, top, Mathf.Clamp(0.45f + speed * 0.6f, 0.45f, 1f), Random.Range(0.99f, 1.01f));
            Buzz(by, 0.5f, 0.04f);
        }

        _bellDown = down || (_bellDown && depth > 0.001f);
        _bellPush = Mathf.Lerp(_bellPush, Mathf.Min(0.006f, depth), 0.6f);
        plunger.localPosition = top - Vector3.up * _bellPush;
    }

    // ---------------- the piano ----------------

    private static Mesh _keysMesh;
    private static Vector3[] _keysBase, _keysWork;
    private static float[] _keyDepth, _keyTarget;
    private static readonly int[] HandKey = { -1, -1 };
    private static float _lastPlayed = -100f, _ghostNext;
    private static readonly List<(int key, float at)> Ghost = new();

    private static void SetupKeys()
    {
        _keysMesh = _t.KeysMesh;
        if (_keysMesh == null) return;
        var vs = _keysMesh.vertices;
        _keysBase = new Vector3[vs.Length];
        for (var i = 0; i < vs.Length; i++) _keysBase[i] = vs[i];
        _keysWork = (Vector3[])_keysBase.Clone();
        _keyDepth = new float[_t.Keys.Count];
        _keyTarget = new float[_t.Keys.Count];
    }

    private static int KeyUnder(Vector3 p)
    {
        // black keys first: they stand above the white ones
        var white = -1;
        for (var i = 0; i < _t.Keys.Count; i++)
        {
            var k = _t.Keys[i];
            if (p.x < k.X0 || p.x > k.X1 || Mathf.Abs(p.z - k.Z) > k.HalfW) continue;
            if (p.y > k.Top + 0.004f || p.y < k.Top - 0.05f) continue;
            if (k.Black) return i;
            white = i;
        }

        return white;
    }

    private static void UpdateKeys(float dt, float now)
    {
        if (_keysMesh == null) return;
        for (var i = 0; i < _keyTarget.Length; i++) _keyTarget[i] = 0;
        for (var h = 0; h < 2; h++)
        {
            var key = HandValid[h] ? KeyUnder(Tip[h]) : -1;
            if (key >= 0)
            {
                _keyTarget[key] = 1;
                if (key != HandKey[h])
                {
                    var speed = Mathf.Max(0, -TipVel[h].y);
                    TheatreSounds.Play(TheatreSounds.Note(_t.Keys[key].Note), Tip[h], Mathf.Clamp(0.3f + speed * 1.1f, 0.3f, 1f));
                    Buzz(h, 0.25f, 0.02f);
                    _lastPlayed = now;
                }
            }

            HandKey[h] = key;
        }

        // the piano plays on its own, now and then, when your back's turned (and you're not too near)
        var piano = Theatre.PianoCentre;
        if (Ghost.Count == 0 && now > _ghostNext && now - _lastPlayed > 20f)
        {
            var d = (piano - TheatreWalk.Eye).magnitude;
            if (d > 3.5f && d < 16f && !InView(piano, 100f, 99f) && TheatreWalk.Eye.z < -13f)
            {
                // a slow falling minor arpeggio, and a low note under it
                var phrase = new[] { 50, 46, 43, 38, 19 };
                for (var i = 0; i < phrase.Length; i++) Ghost.Add((NoteKey(phrase[i]), now + i * 0.42f + (i == 4 ? 0.3f : 0f)));
                _ghostNext = now + Random.Range(150f, 260f);
            }
        }

        for (var i = Ghost.Count - 1; i >= 0; i--)
        {
            var (key, at) = Ghost[i];
            if (key < 0)
            {
                Ghost.RemoveAt(i);
                continue;
            }

            if (now >= at && now < at + 0.5f) _keyTarget[key] = 1;
            if (now >= at && now - dt < at) TheatreSounds.Play(TheatreSounds.Note(_t.Keys[key].Note), piano, 0.55f);
            if (now > at + 0.5f) Ghost.RemoveAt(i);
        }

        var changed = false;
        for (var i = 0; i < _keyDepth.Length; i++)
        {
            var want = _keyTarget[i];
            if (Mathf.Abs(_keyDepth[i] - want) < 1e-3f)
            {
                if (_keyDepth[i] != want) changed = true;
                _keyDepth[i] = want;
                continue;
            }

            _keyDepth[i] = Mathf.MoveTowards(_keyDepth[i], want, dt * 25f);
            changed = true;
        }

        if (!changed) return;
        for (var i = 0; i < _keyDepth.Length; i++)
        {
            var k = _t.Keys[i];
            var drop = _keyDepth[i] * (k.Black ? 0.007f : 0.009f);
            for (var v = k.V0; v < k.V1 && v < _keysWork.Length; v++)
            {
                var b = _keysBase[v];
                // the front of the key goes down more than the back (it pivots at the back)
                var along = Mathf.InverseLerp(k.X0, k.X1, b.x);
                _keysWork[v] = new Vector3(b.x, b.y - drop * (0.35f + 0.65f * along), b.z);
            }
        }

        _keysMesh.SetVertices(_keysWork);
    }

    private static int NoteKey(int note)
    {
        for (var i = 0; i < _t.Keys.Count; i++)
            if (_t.Keys[i].Note == note)
                return i;
        return -1;
    }

    // ---------------- things to pick up and throw ----------------

    private sealed class Grab
    {
        public Transform T;
        public Vector3 Rest;
        public Quaternion RestRot;
        public float Radius, Bottom, Centre, Grip;
        public bool Can, Doll;
        public int Hand = -1;
        public Vector3 HoldPos;
        public Quaternion HoldRot;
        public bool Flying;
        public Vector3 Velocity, Spin;
        public float RestingSince = -1f, Squish;

        public Vector3 Middle => T.localPosition + T.localRotation * (Vector3.up * Centre);

        public void Home()
        {
            T.localPosition = Rest;
            T.localRotation = RestRot;
            T.localScale = Vector3.one;
            RestingSince = -1f;
        }
    }

    private static readonly List<Grab> Grabs = new();

    private static void UpdateGrabs(float dt)
    {
        for (var h = 0; h < 2; h++)
        {
            var grip = HandValid[h] && Gripping(h);
            var pressed = grip && !GripWas[h];
            GripWas[h] = grip;
            Grab held = null;
            foreach (var g in Grabs)
                if (g.Hand == h)
                    held = g;
            if (held != null && !grip) Release(held, h);
            else if (held == null && pressed) TryGrab(h);
        }

        foreach (var g in Grabs)
        {
            if (g.Hand >= 0)
            {
                var h = g.Hand;
                var hr = HandRot[h];
                var hp = Palm[h];
                g.T.localPosition = hp + hr * g.HoldPos;
                g.T.localRotation = hr * g.HoldRot;
            }
            else if (g.Flying) Fly(g, dt);
            else if (g.RestingSince > 0 && Time.unscaledTime - g.RestingSince > 45f && !InView(g.Middle, 70f, 40f))
            {
                // a while on the floor somewhere, unwatched: back where it came from
                g.Home();
            }

            if (g.Squish > 0)
            {
                g.Squish = Mathf.Max(0, g.Squish - dt * 3f);
                var s = 1f - 0.18f * Mathf.Sin(g.Squish * Mathf.PI);
                g.T.localScale = new Vector3(1f / Mathf.Sqrt(s), s, 1f / Mathf.Sqrt(s));
            }
        }
    }

    private static void TryGrab(int h)
    {
        Grab best = null;
        var bestD = 0.11f;
        foreach (var g in Grabs)
        {
            if (g.Hand >= 0) continue;
            var d = (g.Middle - Palm[h]).magnitude - g.Grip;
            if (d < bestD)
            {
                best = g;
                bestD = d;
            }
        }

        if (best == null) return;
        best.Hand = h;
        best.Flying = false;
        best.RestingSince = -1f;
        var inv = Quaternion.Inverse(HandRot[h]);
        // the can comes to the palm (keeping how it was turned); the doll is held where it was taken
        var at = best.Can ? Palm[h] + HandRot[h] * new Vector3(h == 1 ? -0.025f : 0.025f, -0.01f, 0.01f) - best.T.localRotation * (Vector3.up * best.Centre) : best.T.localPosition;
        best.HoldPos = inv * (at - Palm[h]);
        best.HoldRot = inv * best.T.localRotation;
        Grips.Holding((Hand)h, best.Grip);
        Buzz(h, 0.3f, 0.03f);
        if (best.Doll)
        {
            best.Squish = 1f;
            TheatreSounds.Play(TheatreSounds.Squeak, best.Middle, 0.7f, Random.Range(0.92f, 1.08f));
        }
        else TheatreSounds.Play(TheatreSounds.Clank, best.Middle, 0.25f, 1.3f);
    }

    private static void Release(Grab g, int h)
    {
        g.Hand = -1;
        Grips.Holding((Hand)h, 0);
        var v = PalmVel[h];
        if (v.magnitude > 0.35f)
        {
            g.Flying = true;
            g.Velocity = v * 1.15f;
            g.Spin = Random.onUnitSphere * Mathf.Clamp(v.magnitude * 220f, 60f, 720f);
            return;
        }

        // let go of it gently: it drops to whatever's under it
        g.Flying = true;
        g.Velocity = v;
        g.Spin = Vector3.zero;
    }

    private static void Fly(Grab g, float dt)
    {
        var p = g.T.localPosition;
        var v = g.Velocity + Physics.gravity * dt;
        p += v * dt;
        Theatre.Contain(ref p, ref v, g.Radius);
        var ground = Theatre.SurfaceUnder(p + Vector3.up * 0.05f) + g.Bottom;
        var rot = Quaternion.Euler(g.Spin * dt) * g.T.localRotation;
        if (p.y < ground)
        {
            p.y = ground;
            var hit = Mathf.Abs(v.y);
            if (hit > 0.6f) TheatreSounds.Play(g.Doll ? TheatreSounds.Thud : TheatreSounds.Clank, p, Mathf.Clamp01(hit * 0.25f) * (g.Doll ? 0.4f : 0.8f), g.Doll ? 1.6f : Random.Range(0.9f, 1.15f));
            v = new Vector3(v.x * 0.55f, -v.y * (g.Doll ? 0.15f : 0.3f), v.z * 0.55f);
            g.Spin *= 0.4f;
            if (v.magnitude < 0.45f)
            {
                // settled: standing up (a can on its end, the doll sat down), facing however it came to rest
                var fwd = Vector3.ProjectOnPlane(rot * Vector3.forward, Vector3.up);
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                g.Flying = false;
                g.RestingSince = Time.unscaledTime;
                v = Vector3.zero;
            }
        }

        g.Velocity = v;
        g.T.localPosition = p;
        g.T.localRotation = rot;
    }

    // ---------------- locked doors ----------------

    private static float _rattleNext, _knockAt = -1f, _knockNext;
    private static Vector3 _knockFrom;

    private static void UpdateLockedDoors(float now)
    {
        if (_knockAt > 0 && now >= _knockAt)
        {
            _knockAt = -1f;
            TheatreSounds.Play(TheatreSounds.Knock, _knockFrom, 0.95f);
        }

        if (now < _rattleNext) return;
        bool Push(Vector3 p, Vector3 v, int h)
        {
            foreach (var (bottom, n, right, halfW, height) in Theatre.LockedDoors)
            {
                var rel = p - bottom;
                var off = Vector3.Dot(rel, n);
                if (off > 0.05f || off < -0.03f) continue;
                if (Mathf.Abs(Vector3.Dot(rel, right)) > halfW || rel.y < 0.3f || rel.y > height) continue;
                if (Vector3.Dot(v, -n) < 0.25f) continue;
                TheatreSounds.Play(TheatreSounds.Rattle, p, 0.8f, Random.Range(0.92f, 1.06f));
                Buzz(h, 0.6f, 0.12f);
                _rattleNext = now + 0.7f;
                if (now > _knockNext)
                {
                    // ...and something on the other side knocks back
                    _knockAt = now + 1.6f;
                    _knockNext = now + 35f;
                    _knockFrom = p - n * 0.6f;
                }

                return true;
            }

            return false;
        }

        for (var h = 0; h < 2; h++)
            if (HandValid[h] && (Push(Tip[h], TipVel[h], h) || Push(Palm[h], PalmVel[h], h)))
                return;
    }

    // ---------------- the cardboard Bendy ----------------

    private static int _cutout;
    private static float _cutoutUnseenSince = -1f;

    private static void UpdateCutout(float now)
    {
        var t = _t.Piece("Cutout");
        if (t == null) return;
        var spots = Theatre.CutoutSpots;
        var here = spots[_cutout].foot;
        var seen = InView(here + Vector3.up * 0.7f, 62f, 30f);
        if (seen) _cutoutUnseenSince = -1f;
        else if (_cutoutUnseenSince < 0) _cutoutUnseenSince = now;

        var eye = TheatreWalk.Eye;
        var next = (_cutout + 1) % spots.Length;
        float FlatDist(Vector3 p) => new Vector2(p.x - eye.x, p.z - eye.z).magnitude;
        if (_cutoutUnseenSince > 0 && now - _cutoutUnseenSince > 2.5f && FlatDist(here) > 3f && FlatDist(spots[next].foot) > 3.5f
            && !InView(spots[next].foot + Vector3.up * 0.7f, 62f, 30f) && eye.z < Theatre.LobbyDoorway.z + 3f)
        {
            _cutout = next;
            _cutoutUnseenSince = now;
            here = spots[_cutout].foot;
        }

        // always turned to face you
        var to = eye - here;
        to.y = 0;
        t.localPosition = here;
        if (to.sqrMagnitude > 1e-4f) t.localRotation = Quaternion.LookRotation(-to.normalized);
    }

    // ---------------- the writing on the wall ----------------

    private static float _writing, _lookedAtWriting;

    private static void UpdateWriting(float dt)
    {
        var at = Theatre.WritingAt;
        var eye = TheatreWalk.Eye;
        if (_writing <= 0 && eye.z < Theatre.LobbyDoorway.z && eye.x < at.x && InView(at, 24f, 9f)) _lookedAtWriting += dt;
        else if (_writing <= 0) _lookedAtWriting = Mathf.Max(0, _lookedAtWriting - dt);
        if (_writing <= 0 && _lookedAtWriting > 0.7f)
        {
            _writing = 0.001f;
            TheatreSounds.Play(TheatreSounds.Seep, at, 0.75f);
        }

        if (_writing > 0 && _writing < 1) _writing = Mathf.Min(1, _writing + dt / 3.5f);
        var a = Mathf.SmoothStep(0, 1, _writing);
        foreach (var id in new[] { "Writing1", "Writing2" })
        {
            var tmp = _cinema.Sign(id);
            if (tmp == null) continue;
            var c = tmp.color;
            if (Mathf.Abs(c.a - a) > 1e-3f) tmp.color = new Color(c.r, c.g, c.b, a);
        }

        var drips = _t.Piece("Drips");
        if (drips != null) drips.localScale = new Vector3(1, Mathf.Max(0.001f, Mathf.Pow(a, 0.7f)), 1);
    }

    public static string Describe()
    {
        if (!_ready) return "not ready";
        var held = string.Join(",", Grabs.FindAll(g => g.Hand >= 0).ConvertAll(g => g.T.name + "@" + g.Hand));
        var flying = Grabs.FindAll(g => g.Flying).Count;
        var where = string.Join(" ", Grabs.FindAll(g => (g.T.localPosition - g.Rest).sqrMagnitude > 1e-4f).ConvertAll(g => $"{g.T.name.Replace("InkVR_Theatre_", "")}=({g.T.localPosition.x:F2},{g.T.localPosition.y:F2},{g.T.localPosition.z:F2})"));
        return $"moved[{where}] door={_doorOpen:F2} bell={_bellDown} keys={(_keysMesh != null ? _t.Keys.Count : 0)} held=[{held}] flying={flying} cutout={_cutout} writing={_writing:F2} tipR=({Tip[1].x:F2},{Tip[1].y:F2},{Tip[1].z:F2}) palmR=({Palm[1].x:F2},{Palm[1].y:F2},{Palm[1].z:F2})";
    }
}
