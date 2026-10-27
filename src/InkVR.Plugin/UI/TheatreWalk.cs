using InkVR.Core;
using InkVR.Input;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// Walking around the theatre (the main menu and the pause screen). The off-hand stick walks you where you look
/// (the off-hand trigger hurries), the dominant stick turns, and you go wherever the theatre's walk map lets you:
/// the aisles, the stage steps, the lobby, the stairs and the balcony. Walls, seats and rails stop you (sliding
/// along them), and the floor rises and falls with the rows, steps and stairs.
///
/// Click the move stick to walk (and again for the menu); with the laser on, a push of the stick while you're
/// not pointing at the screen walks you too. While you aren't walking, the stick stays the menu's.
/// The theatre moves around you (its root, under the cinema rig), so the headset, hands and laser are untouched.
/// </summary>
public static class TheatreWalk
{
    private const float WalkSpeed = 1.6f, HurrySpeed = 2.7f;

    private static Vector3 _offset; // where the tracking space's origin is, in the theatre (horizontal)
    private static float _yaw; // how the tracking space is turned in the theatre
    private static float _floor, _shownFloor;
    private static Vector3 _last;
    private static bool _placed, _walking, _clickWas;

    /// <summary>Walking with the stick (clicked into), rather than using it on the menu.</summary>
    public static bool WalkMode { get; private set; }
    private static Vector2 _devStick;
    private static float _devStickUntil;

    /// <summary>The stick is walking you (so the game's menu doesn't get it).</summary>
    public static bool TakesStick => _walking;

    // where the theatre was last frame (world), so things standing on its floor can move with it
    private static Matrix4x4 _rootWas, _rootNow, _rootStep;
    private static bool _haveRoot;
    private static float _rootYawWas, _rootYawStep;

    /// <summary>Where a point on the theatre's floor last frame (world) is now: you walked or turned, the floor moved.</summary>
    public static Vector3 CarryPoint(Vector3 world) => _haveRoot ? _rootStep.MultiplyPoint3x4(world) : world;

    /// <summary>How far the theatre turned round you since last frame (degrees).</summary>
    public static float CarryYaw => _haveRoot ? _rootYawStep : 0f;

    /// <summary>How fast the floor under a point is moving (world, per second).</summary>
    public static Vector3 FloorVelocity(Vector3 world, float dt) => dt > 1e-4f && _haveRoot ? (CarryPoint(world) - world) / dt : Vector3.zero;

    /// <summary>Your eye, in the theatre.</summary>
    public static Vector3 Eye { get; private set; } = new(0, 1.6f, 0);

    /// <summary>Your head's forward and right, in the theatre (level).</summary>
    public static Vector3 Forward { get; private set; } = Vector3.forward;

    public static Vector3 Right { get; private set; } = Vector3.right;

    /// <summary>The floor you're standing on, in the theatre.</summary>
    public static float Floor => _floor;

    /// <summary>The screen can be seen from where you are (not from the lobby or the stairs).</summary>
    public static bool ScreenVisible { get; private set; } = true;

    private static bool On => ModConfig.ExploreTheatre.Value;

    /// <summary>Back to your seat, facing the screen (whenever the theatre opens).</summary>
    public static void Reset()
    {
        _placed = false;
        _walking = false;
        WalkMode = false;
        TheatreToys.Reset();
        TheatreLife.Reset();
    }

    /// <summary>Dev: walk with this stick for a while (the mock runtime has no controllers).</summary>
    public static void DevStick(Vector2 v, float seconds)
    {
        _devStick = v;
        _devStickUntil = Time.unscaledTime + seconds;
    }

    /// <summary>Dev: stand here in the theatre (head over x, z, on the floor nearest <paramref name="level"/>), facing yaw.</summary>
    public static void DevPlace(float x, float z, float yaw, float level)
    {
        var head = VRRig.HeadLocalPosition;
        var hf = VRRig.HeadLocalRotation * Vector3.forward;
        _yaw = yaw - Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg;
        var rot = Quaternion.Euler(0, _yaw, 0);
        _offset = new Vector3(x, 0, z) - rot * new Vector3(head.x, 0, head.z);
        _last = new Vector3(x, 0, z);
        _floor = Theatre.Walkable(x, z, level, out var f) ? f : level;
        _shownFloor = _floor;
        _placed = true;
    }

    private static Vector2 Stick()
    {
        if (Time.unscaledTime < _devStickUntil) return _devStick;
        if (!XrInput.Ready) return Vector2.zero;
        var s = ControllerBindings.Off.Stick;
        return s.magnitude < 0.15f ? Vector2.zero : s;
    }

    /// <summary>Called with the controllers each frame: is the stick walking you (true), or the menu's?</summary>
    public static bool WantsStick(ControllerState off)
    {
        if (!On || !VRRig.CinemaShowing)
        {
            _walking = WalkMode = false;
            _clickWas = off.StickClick;
            return false;
        }

        if (off.StickClick && !_clickWas)
        {
            WalkMode = !WalkMode;
            XrInput.Haptic(ControllerBindings.OffHand, WalkMode ? 0.35f : 0.2f, 0.04f);
            // stop walking beside a seat and you sit down in it; walk again and you get up
            if (!WalkMode) TrySit();
            else StandUp();
        }

        _clickWas = off.StickClick;
        var m = off.Stick.magnitude;
        if (WalkMode) _walking = true;
        else if (_walking && m < 0.15f) _walking = false;
        else if (!_walking && m > 0.25f && ModConfig.LaserPointer.Value && !LaserPointer.OnScreen) _walking = true;
        return _walking;
    }

    /// <summary>Snap or smooth turn: the theatre turns around your head.</summary>
    public static void Turn(float degrees)
    {
        if (!On || !_placed) return;
        var head = VRRig.HeadLocalPosition;
        var flat = new Vector3(head.x, 0, head.z);
        var h = Quaternion.Euler(0, _yaw, 0) * flat + _offset;
        _yaw += degrees;
        _offset = h - Quaternion.Euler(0, _yaw, 0) * flat;
    }

    public static void Update(Cinema cinema)
    {
        var root = cinema?.Root;
        if (root == null) return;
        _stepRoot = root;
        var head = VRRig.HeadLocalPosition;
        var headRot = VRRig.HeadLocalRotation;
        if (!_placed) Seat(head, headRot);

        var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (On)
        {
            var x = VRRig.SmoothTurnInput;
            if (!ModConfig.SnapTurn.Value && Mathf.Abs(x) > 0.15f) Turn(x * ModConfig.SmoothTurnSpeed.Value * dt);
        }

        var rot = Quaternion.Euler(0, _yaw, 0);
        var fwd = rot * (headRot * Vector3.forward);
        fwd.y = 0;
        fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : rot * Vector3.forward;
        var right = Vector3.Cross(Vector3.up, fwd);
        Forward = fwd;
        Right = right;

        var here = rot * new Vector3(head.x, 0, head.z) + _offset; // where your head is now (after any real steps)
        var at = here;
        if (On)
        {
            var stick = Stick();
            if (stick.sqrMagnitude > 0 && (_walking || Time.unscaledTime < _devStickUntil))
            {
                var hurry = XrInput.Ready && ControllerBindings.Off.Trigger > 0.5f;
                at += (fwd * stick.y + right * stick.x) * ((hurry ? HurrySpeed : WalkSpeed) * Mathf.Min(1f, stick.magnitude) * dt);
            }

            // walls, seats and rails stop you (you slide along them); real steps into them too (not while you're sat down)
            if (!Sitting) at = Resolve(_last, at);
            _offset += at - here;
        }

        _last = at;
        _shownFloor = Mathf.Abs(_shownFloor - _floor) < 1e-3f ? _floor : Mathf.Lerp(_shownFloor, _floor, 1f - Mathf.Exp(-dt * 12f));

        var inv = Quaternion.Inverse(rot);
        root.localRotation = inv;
        root.localPosition = -(inv * (_offset + Vector3.up * _shownFloor));
        _rootNow = root.localToWorldMatrix;
        var yawNow = root.eulerAngles.y;
        // (a jump, like being put back in your seat, isn't a step)
        _rootStep = _haveRoot && (_rootNow.GetColumn(3) - _rootWas.GetColumn(3)).sqrMagnitude < 1f ? _rootNow * _rootWas.inverse : Matrix4x4.identity;
        _rootYawStep = _haveRoot ? Mathf.DeltaAngle(_rootYawWas, yawNow) : 0f;
        if (Mathf.Abs(_rootYawStep) > 45f) _rootYawStep = 0f;
        _rootWas = _rootNow;
        _rootYawWas = yawNow;
        _haveRoot = true;
        Footsteps(dt);
        Eye = new Vector3(at.x, head.y + _shownFloor, at.z);
        ScreenVisible = Theatre.SeesScreen(Eye);
    }

    // ---------------- sitting in a seat ----------------

    /// <summary>How far down you sit (metres): the body bends to it, and the theatre's floor stays where it is.</summary>
    public const float SitDrop = 0.45f;

    public static bool Sitting { get; private set; }
    private static Vector3 _standAt;
    private static float _standFloor;

    /// <summary>Dev: sit in the nearest seat (or stand up).</summary>
    public static void DevSit(bool sit)
    {
        if (sit) TrySit();
        else StandUp();
    }

    private static void TrySit()
    {
        if (Sitting || !Theatre.NearestSeat(Eye, _floor, 1.0f, out var seat, out var seatFloor)) return;
        var head = VRRig.HeadLocalPosition;
        var rot = Quaternion.Euler(0, _yaw, 0);
        _standAt = _last;
        _standFloor = _floor;
        _offset = new Vector3(seat.x, 0, seat.z) - rot * new Vector3(head.x, 0, head.z);
        _last = new Vector3(seat.x, 0, seat.z);
        _floor = seatFloor - SitDrop;
        Sitting = true;
        TheatreSounds.Play(TheatreSounds.Creak, seat + Vector3.up * 0.4f, 0.3f, 1.4f);
        Plugin.L.LogInfo($"Sat down at ({seat.x:F2}, {seat.z:F2})");
    }

    private static void StandUp()
    {
        if (!Sitting) return;
        var head = VRRig.HeadLocalPosition;
        var rot = Quaternion.Euler(0, _yaw, 0);
        _offset = _standAt - rot * new Vector3(head.x, 0, head.z);
        _last = _standAt;
        _floor = _standFloor;
        Sitting = false;
        TheatreSounds.Play(TheatreSounds.Creak, new Vector3(_standAt.x, _floor + 0.4f, _standAt.z), 0.25f, 1.6f);
    }

    private static void Seat(Vector3 head, Quaternion headRot)
    {
        Sitting = false;
        var f = headRot * Vector3.forward;
        _yaw = -Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        _offset = -(Quaternion.Euler(0, _yaw, 0) * new Vector3(head.x, 0, head.z));
        _last = Vector3.zero;
        _floor = _shownFloor = 0f;
        _placed = true;
    }

    private static Vector3 Resolve(Vector3 from, Vector3 to)
    {
        if ((to - from).sqrMagnitude < 1e-8f) return to;
        if (Theatre.Walkable(to.x, to.z, _floor, out var f))
        {
            _floor = f;
            return to;
        }

        if (Theatre.Walkable(to.x, from.z, _floor, out f))
        {
            _floor = f;
            return new Vector3(to.x, 0, from.z);
        }

        if (Theatre.Walkable(from.x, to.z, _floor, out f))
        {
            _floor = f;
            return new Vector3(from.x, 0, to.z);
        }

        // somewhere you can't be (a seat, after a recentre): don't hold you there
        return Theatre.Walkable(from.x, from.z, _floor, out _) ? from : to;
    }

    // ---------------- footsteps: soft, and on whatever's underfoot ----------------

    private static Body.HenryBody _stepBody;
    private static Transform _stepRoot;
    private static Vector3 _stepFrom;
    private static readonly Vector3[] LastFoot = new Vector3[2];
    private static float _stepTravel;
    private static int _stepTake;

    private static void Footsteps(float dt)
    {
        // with a body, its feet make the sounds as they come down; without one, a step every stride you go
        var body = Body.Hands.Body;
        if (body != _stepBody)
        {
            if (_stepBody != null) _stepBody.FootPlanted -= OnFoot;
            _stepBody = body;
            if (body != null) body.FootPlanted += OnFoot;
        }

        var here = new Vector3(Eye.x, 0, Eye.z);
        var d = (here - _stepFrom).magnitude;
        _stepFrom = here;
        if (body != null || d > 1f) return; // (a jump: put back in your seat)
        _stepTravel += d;
        if (_stepTravel < 0.62f) return;
        _stepTravel = 0;
        PlayStep(new Vector3(Eye.x, _floor, Eye.z), 1f);
    }

    private static void OnFoot(int foot, Vector3 world, float yaw)
    {
        if (!VRRig.CinemaShowing || _stepRoot == null) return;
        var local = _stepRoot.InverseTransformPoint(world);
        // a little shuffle in place (turning on the spot) is quieter than a stride
        var moved = new Vector2(local.x - LastFoot[foot].x, local.z - LastFoot[foot].z).magnitude;
        LastFoot[foot] = local;
        PlayStep(local, moved < 0.15f ? 0.5f : 1f);
    }

    private static void PlayStep(Vector3 at, float k)
    {
        var floor = Theatre.FloorSound(at);
        var volume = floor switch
        {
            TheatreSounds.Floor.Carpet => 0.09f,
            TheatreSounds.Floor.Concrete => 0.14f,
            TheatreSounds.Floor.Tile => 0.13f,
            _ => 0.15f
        };
        TheatreSounds.Play(TheatreSounds.Step(floor, _stepTake++), at, volume * k, Random.Range(0.94f, 1.06f));
    }

    public static string Describe() =>
        $"eye=({Eye.x:F2},{Eye.y:F2},{Eye.z:F2}) floor={_floor:F2} yaw={_yaw:F0} walking={_walking} screen={ScreenVisible}";
}
