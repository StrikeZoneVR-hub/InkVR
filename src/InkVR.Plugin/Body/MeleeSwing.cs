using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InkVR.Core;
using InkVR.Input;
using InkVR.UI;
using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// Swing your arm to hit. The held melee weapon is swept through the world every frame, its head and three points
/// down its shaft; when it moves fast enough and passes through something, the game's own hit runs on that spot
/// (BaseWeapon.OnRaycastHit: the same damage, breaking, sounds and effects as a button swing). Enemies count
/// anywhere on them: their head, arms and legs, not just the narrow capsule the game keeps round their middle
/// (see EnemyReach). One hit on something hittable per swing; knocking a wall or the floor on the way (a wind-up
/// that scrapes the ceiling) still thuds but doesn't use the swing up. A swing ends when the weapon slows down or
/// turns back, so each chop of a quick chop-chop is its own swing. Speed is measured in your play space, so
/// walking and turning never count as swinging. The attack button still plays the game's swing, but not when
/// it's pulled while your arm is already swinging: then it's your swing that hits.
/// Quiet unless you mean it (0.10.0): the whoosh only plays for a real swing of the arm (the head fast AND the hand
/// moving, not a flick of the wrist, whose 60 cm of handle sends the head flying), louder the harder you swing; and a
/// knock on a wall or the floor only buzzes the controller unless you swung hard, so carrying the axe about is silent.
/// </summary>
[HarmonyPatch(typeof(BaseWeapon))]
public static class MeleeSwing
{
    private const float StartSpeed = 1.8f; // m/s at the weapon head: a deliberate swing, not a wave
    private const float EndSpeed = 1.0f;
    private const float Cooldown = 0.3f;   // s between hits (the game's own swing takes about 0.6 s)
    private const float HeadRadius = 0.07f; // m
    private const float MaxSpeed = 25f;     // faster than any arm: a tracking glitch
    private const float WhooshSpeed = 3.2f;  // m/s at the head for the swing sound
    private const float WhooshHand = 0.9f;   // m/s at the hand: the arm swings, not just the wrist
    private const float WhooshGap = 0.45f;   // s between whooshes
    private const float SolidHitSpeed = 3.0f; // m/s (the swing's peak) for a wall or the floor to get the game's loud hit
    private const float SolidGap = 0.8f;     // s between loud wall/floor hits, whatever they hit

    private static BaseWeapon _weapon;
    private static Transform _model;
    // the far end of the weapon (the head), and points back down it towards the hand
    private static readonly float[] Along = { 0.92f, 0.78f, 0.62f, 0.46f };
    private static readonly Vector3[] PointsLocal = new Vector3[Along.Length];
    private static readonly Vector3[] Prev = new Vector3[Along.Length], Now = new Vector3[Along.Length];
    private static readonly F3[] PrevF = new F3[Along.Length], NowF = new F3[Along.Length];
    private static Vector3 _prevHeadTracking, _swingDir, _prevGrip;
    private static bool _havePrev, _swinging, _hitThisSwing, _whooshed;
    private static float _lastHit = -10f, _slowFor, _gameSwingUntil, _lastThudAt = -10f, _lastWhoosh = -10f, _lastLoudSolid = -10f;
    private static System.IntPtr _lastThud;
    private static AudioSource _audio;
    private static Il2CppStructArray<RaycastHit> _hits;
    private static Il2CppReferenceArray<Collider> _overlap;

    /// <summary>Head speed this frame (m/s, play space), the fastest this swing, and totals (dev/log).</summary>
    public static float Speed, PeakSpeed;

    /// <summary>How fast the hand holding the weapon is moving (m/s, play space).</summary>
    public static float HandSpeed;
    public static int Swings, Hits;
    public static string LastHit = "";

    /// <summary>The weapon's head in the dominant hand's aim space (metres), once a melee weapon is held.</summary>
    public static Vector3? HeadInAim { get; private set; }

    public static void Update()
    {
        var weapon = HeldMelee();
        if (weapon == null || weapon.Pointer != _weapon?.Pointer) Setup(weapon);
        if (_weapon == null || _model == null || !_model.gameObject.activeInHierarchy)
        {
            _havePrev = _swinging = false;
            HeadInAim = null;
            return;
        }

        var origin = VRRig.ActiveOrigin;
        for (var i = 0; i < PointsLocal.Length; i++) Now[i] = _model.TransformPoint(PointsLocal[i]);
        var head = Now[0];
        var headTracking = origin.InverseTransformPoint(head); // metres: the origin carries the world scale
        var c = ControllerBindings.Dom;
        if (c.PoseValid)
            HeadInAim = Quaternion.Inverse(c.AimRotation) * (headTracking - Vector3.up * VRRig.HeightLift - c.AimPosition);

        var dt = Time.unscaledDeltaTime;
        if (!_havePrev || dt < 1e-4f)
        {
            Remember(headTracking, c.GripPosition);
            return;
        }

        HandSpeed = c.PoseValid ? (c.GripPosition - _prevGrip).magnitude / dt : 0f;
        KnockLoose(dt);

        var vel = (headTracking - _prevHeadTracking) / dt;
        Speed = vel.magnitude;
        if (Speed > MaxSpeed)
        {
            // Tracking jumped (lost and found again, or a recentre): not a swing.
            Speed = 0;
            _swinging = false;
            Remember(headTracking, c.GripPosition);
            return;
        }

        if (!c.PoseValid)
        {
            // A fast swing can lose tracking for a frame: carry on with the swing, just don't sweep this frame.
            Remember(headTracking, c.GripPosition);
            return;
        }

        var now = Time.unscaledTime;
        var gameSwing = now < _gameSwingUntil;
        if (!ModConfig.PhysicalMelee.Value || gameSwing || !VRRig.InGame || VRRig.CinemaActive || GameManager.Instance?.Player?.isMoveLocked == true)
        {
            _swinging = false;
            Remember(headTracking, c.GripPosition);
            return;
        }

        if (_swinging)
        {
            // Slowing right down, or turning back (the top of a chop), ends this swing.
            PeakSpeed = Mathf.Max(PeakSpeed, Speed);
            _slowFor = Speed < EndSpeed ? _slowFor + dt : 0;
            var turnedBack = Speed > EndSpeed && Vector3.Dot(vel / Speed, _swingDir) < -0.25f;
            if (_slowFor > 0.05f || turnedBack) _swinging = false;
            else if (Speed > EndSpeed) _swingDir = (_swingDir + vel / Speed * 0.3f).normalized; // follows the arc
        }

        if (!_swinging && Speed >= StartSpeed)
        {
            _swinging = true;
            _hitThisSwing = false;
            _slowFor = 0;
            _swingDir = vel / Speed;
            PeakSpeed = Speed;
            Swings++;
            _whooshed = false;
            XrInput.Haptic(ControllerBindings.Dominant, 0.12f, 0.03f);
        }

        // The swing sound, once per swing, only when the arm is really swinging (and as loud as the swing).
        if (_swinging && !_whooshed && Speed >= WhooshSpeed && HandSpeed >= WhooshHand && now - _lastWhoosh > WhooshGap)
        {
            _whooshed = true;
            _lastWhoosh = now;
            Whoosh(head, Mathf.Clamp((PeakSpeed - 2.4f) / 4f, 0.25f, 0.8f));
        }

        if (_swinging && !_hitThisSwing && now - _lastHit > Cooldown && HitEnemy())
        {
            _hitThisSwing = true;
            _lastHit = now;
            Hits++;
        }
        else if (_swinging && !_hitThisSwing && now - _lastHit > Cooldown && Sweep(out var hit, out var hittable))
        {
            // A wall or the floor thuds once (not again while you scrape along it) and doesn't use the swing up.
            var thudAgain = !hittable && hit.collider.Pointer == _lastThud && now - _lastThudAt < 0.6f;
            // Bumping a wall or the floor with the axe in hand (not a hard swing): a buzz in the hand, no loud crack.
            var quietKnock = !hittable && (PeakSpeed < SolidHitSpeed || HandSpeed < 0.8f || now - _lastLoudSolid < SolidGap);
            if (!thudAgain && quietKnock)
            {
                _lastThud = hit.collider.Pointer;
                _lastThudAt = now;
                XrInput.Haptic(ControllerBindings.Dominant, 0.18f, 0.03f);
            }
            else if (!thudAgain)
            {
                if (!hittable) _lastLoudSolid = now;
                if (hittable)
                {
                    _hitThisSwing = true;
                    _lastHit = now;
                    Hits++;
                }
                else
                {
                    _lastThud = hit.collider.Pointer;
                    _lastThudAt = now;
                }

                LastHit = $"{Dev.DevTools.PathOf(hit.collider.transform)} at {hit.point} speed {PeakSpeed:F1} m/s {(hittable ? "(hittable)" : "(solid)")}";
                Plugin.L.LogInfo($"Swing hit {LastHit}");
                XrInput.Haptic(ControllerBindings.Dominant, hittable ? 0.9f : 0.5f, hittable ? 0.12f : 0.06f);
                var ai = hittable ? hit.collider.GetComponentInParent<Ai.BaseAiController>() : null;
                var before = ai != null ? ai.m_CurrentHealth : 0;
                _weapon.OnRaycastHit(hit);
                if (ai != null)
                {
                    var own = hit.collider.GetComponent<IHittable>() != null;
                    Plugin.L.LogInfo($"Swing hit enemy {ai.GetIl2CppType().Name} '{ai.name}': collider '{hit.collider.name}' layer {hit.collider.gameObject.layer} trigger={hit.collider.isTrigger} hittableOnCollider={own}; health {before} -> {ai.m_CurrentHealth} of {ai.m_Health}");
                }
            }
        }

        Remember(headTracking, c.GripPosition);
    }

    private static Il2CppReferenceArray<Collider> _near;
    private static readonly Dictionary<System.IntPtr, float> Knocked = new();

    /// <summary>
    /// The weapon knocks loose things about as it goes through them, as hard as it's moving. Only things the game
    /// already lets roll and fly (pool balls, broken bits of plank, buckets...): nothing it holds still is touched,
    /// so nothing the story needs can be knocked out of place.
    /// </summary>
    private static void KnockLoose(float dt)
    {
        if (!ModConfig.SolidWeapons.Value || GameManager.Instance?.Player == null) return;
        var k = VRRig.ActiveScale;
        var now = Time.unscaledTime;
        var player = GameManager.Instance.Player.transform;
        var mask = ~_weapon.m_IgnoreLayers.value & ~(1 << VRLayers.UI) & Physics.DefaultRaycastLayers;
        _near ??= new Il2CppReferenceArray<Collider>(16);
        for (var p = 0; p < Now.Length; p++)
        {
            var v = (Now[p] - Prev[p]) / dt; // world units a second
            if (v.magnitude < 0.6f * k || v.magnitude > MaxSpeed * k) continue;
            var n = Physics.OverlapSphereNonAlloc(Now[p], HeadRadius * k * (p == 0 ? 1.4f : 0.8f), _near, mask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < n; i++)
            {
                var c = _near[i];
                var rb = c != null ? c.attachedRigidbody : null;
                if (rb == null || rb.isKinematic || c.transform.IsChildOf(player) || c.transform.IsChildOf(_model)) continue;
                if (c.GetComponentInParent<Ai.BaseAiController>() != null) continue;
                if (Knocked.TryGetValue(rb.Pointer, out var at) && now - at < 0.35f) continue;
                Knocked[rb.Pointer] = now;
                var push = Vector3.ClampMagnitude(v, 7f * k) * 0.8f;
                rb.AddForceAtPosition(push * rb.mass, c.ClosestPoint(Now[p]), ForceMode.Impulse);
                XrInput.Haptic(ControllerBindings.Dominant, 0.2f, 0.03f);
            }
        }

        if (Knocked.Count > 64) Knocked.Clear();
    }

    private static void Remember(Vector3 headTracking, Vector3 grip)
    {
        for (var i = 0; i < Now.Length; i++) Prev[i] = Now[i];
        _prevHeadTracking = headTracking;
        _prevGrip = grip;
        _havePrev = true;
    }

    /// <summary>Any part of the weapon touched any part of a living enemy this frame: the game's own hit, on that enemy.</summary>
    private static bool HitEnemy()
    {
        var scale = VRRig.ActiveScale;
        for (var i = 0; i < Now.Length; i++)
        {
            PrevF[i] = F3.Of(Prev[i]);
            NowF[i] = F3.Of(Now[i]);
        }

        var mask = ~_weapon.m_IgnoreLayers.value & ~(1 << VRLayers.UI);
        if (!EnemyReach.Touch(PrevF, NowF, HeadRadius * scale, Now[0], 1.6f * scale, mask, out var ai, out var body, out var at)) return false;

        // The game takes the hit on the enemy's own body collider: aim at it from just outside, through the spot touched.
        var bounds = body.bounds;
        var centre = bounds.center;
        centre.y = Mathf.Clamp(at.y, bounds.min.y, bounds.max.y);
        var outward = at - centre;
        if (outward.sqrMagnitude < 1e-6f) outward = -(Now[0] - Prev[0]);
        var from = centre + outward.normalized * (bounds.extents.magnitude + 0.5f * scale);
        if (!body.Raycast(new Ray(from, (centre - from).normalized), out var hit, 10f * scale))
        {
            Plugin.L.LogInfo($"Swing touched {ai.name} but couldn't reach its body collider");
            return false;
        }

        hit.point = at;
        var before = ai.m_CurrentHealth;
        LastHit = $"{Dev.DevTools.PathOf(body.transform)} at {at} speed {PeakSpeed:F1} m/s (enemy)";
        XrInput.Haptic(ControllerBindings.Dominant, 0.9f, 0.12f);
        _weapon.OnRaycastHit(hit);
        Plugin.L.LogInfo($"Swing hit enemy {ai.GetIl2CppType().Name} '{ai.name}' at {at}: health {before} -> {ai.m_CurrentHealth} of {ai.m_Health}");
        return true;
    }

    private static bool _attackHeld, _attackSwallowed;

    /// <summary>
    /// The attack trigger as the game hears it, with a melee weapon in hand. Pulled while your arm is already
    /// swinging, the game's canned swing would take the weapon over (and only hit what your eyes point at), so the
    /// game doesn't hear that pull: your own swing does the hitting. Pulled with your arm still, it's the game's swing.
    /// </summary>
    public static float FilterAttack(float trigger)
    {
        var down = trigger > 0.35f;
        if (down && !_attackHeld)
        {
            _attackSwallowed = ModConfig.PhysicalMelee.Value && _weapon != null && _model != null && _model.gameObject.activeInHierarchy
                               && (_swinging || HandSpeed > 1.2f);
            if (_attackSwallowed) Plugin.L.LogInfo($"Trigger pulled mid-swing (hand {HandSpeed:F1} m/s): your swing hits, not the game's button swing");
        }

        _attackHeld = down;
        return down && _attackSwallowed ? 0f : trigger;
    }

    private static BaseWeapon HeldMelee()
    {
        var p = GameManager.Instance?.Player;
        var go = p?.WeaponGameObject;
        if (go == null || !go.activeInHierarchy) return null;
        var w = go.GetComponent<BaseWeapon>();
        return w != null && w.TryCast<MeleeWeapon>() != null ? w : null;
    }

    /// <summary>Find the weapon's model and its far end from the hand (the axe head, the pipe's end).</summary>
    private static void Setup(BaseWeapon weapon)
    {
        _weapon = weapon;
        _model = null;
        _havePrev = _swinging = false;
        if (weapon == null) return;

        var filter = weapon.GetComponentInChildren<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;
        _model = filter.transform;
        var b = filter.sharedMesh.bounds;
        var axis = b.extents.x >= b.extents.y && b.extents.x >= b.extents.z ? Vector3.right
            : b.extents.y >= b.extents.z ? Vector3.up : Vector3.forward;
        var half = Vector3.Scale(b.extents, axis);
        var a = b.center + half;
        var z = b.center - half;
        var hand = _model.InverseTransformPoint(weapon.transform.parent != null ? HandPoint(weapon) : weapon.transform.position);
        var far = (a - hand).sqrMagnitude > (z - hand).sqrMagnitude ? a : z;
        // the head from the model's own shape where it can be read (some copies have the hand mount at the head end)
        var axisIndex = axis == Vector3.right ? 0 : axis == Vector3.up ? 1 : 2;
        if (Grips.HeadEnd(filter.sharedMesh, axisIndex, b, out var headPositive)) far = headPositive ? a : z;
        var near = far == a ? z : a;
        _nearLocal = near;
        _farLocal = far;
        for (var i = 0; i < Along.Length; i++) PointsLocal[i] = Vector3.Lerp(near, far, Along[i]);
        Plugin.L.LogInfo($"Melee weapon: {weapon.name}, swing to hit (head at {PointsLocal[0]} of {b.size} in {_model.name})");
    }

    private static Vector3 _nearLocal, _farLocal;

    /// <summary>The held melee weapon's length in the world: its hand end and its far end (the axe head).</summary>
    public static bool HandleLine(out Vector3 near, out Vector3 far)
    {
        near = far = default;
        if (_model == null || _weapon == null || !_weapon.gameObject.activeInHierarchy) return false;
        near = _model.TransformPoint(_nearLocal);
        far = _model.TransformPoint(_farLocal);
        return true;
    }

    /// <summary>Where the hand holds the weapon: the game's HandContainer/Hand mount.</summary>
    private static Vector3 HandPoint(BaseWeapon weapon)
    {
        var container = GameManager.Instance?.Player?.m_HandContainer;
        return container != null && container.childCount > 0 ? container.GetChild(0).position : weapon.transform.position;
    }

    private static bool IsHittable(Collider c) => Hittable.Is(c);

    /// <summary>
    /// Sweep the head (and a point behind it) from last frame to now. Something hittable (a plank, a Searcher) wins
    /// over a wall or the floor in the same sweep, nearest first; hittable trigger volumes count too.
    /// </summary>
    private static bool Sweep(out RaycastHit best, out bool hittable)
    {
        best = default;
        hittable = false;
        var scale = VRRig.ActiveScale;
        var radius = HeadRadius * scale;
        var mask = ~_weapon.m_IgnoreLayers.value & ~(1 << VRLayers.UI);
        var player = GameManager.Instance.Player.transform;
        _hits ??= new Il2CppStructArray<RaycastHit>(24);
        var found = false;
        var bestSolid = float.MaxValue;
        var bestHittable = float.MaxValue;

        for (var p = 0; p < Now.Length; p++)
        {
            var from = Prev[p];
            var to = Now[p];
            var move = to - from;
            var length = move.magnitude;
            if (length < 1e-5f) continue;
            var dir = move / length;
            var start = from - dir * radius; // starting a little behind: a cast can't see what it starts inside
            var n = Physics.SphereCastNonAlloc(start, radius, dir, _hits, length + radius, mask, QueryTriggerInteraction.Collide);
            for (var i = 0; i < n; i++)
            {
                var h = _hits[i];
                var col = h.collider;
                if (col == null || h.distance <= 0f || col.transform.IsChildOf(player)) continue;
                var canHit = IsHittable(col);
                if (col.isTrigger && !canHit) continue;
                if (canHit)
                {
                    if (h.distance >= bestHittable) continue;
                    bestHittable = h.distance;
                    best = h;
                    hittable = true;
                    found = true;
                }
                else if (!hittable && h.distance < bestSolid)
                {
                    bestSolid = h.distance;
                    best = h;
                    found = true;
                }
            }
        }

        if (found) return true;

        // Already inside something (pressed against a plank, then swung through it): hit it from just behind the head.
        _overlap ??= new Il2CppReferenceArray<Collider>(8);
        var head = Now[0];
        var count = Physics.OverlapSphereNonAlloc(head, radius, _overlap, mask, QueryTriggerInteraction.Ignore);
        var back = (head - Prev[0]).normalized;
        for (var i = 0; i < count; i++)
        {
            var col = _overlap[i];
            if (col == null || col.transform.IsChildOf(player) || back == Vector3.zero) continue;
            var from = head - back * (radius * 4f + 0.3f * scale);
            if (!col.Raycast(new Ray(from, (col.ClosestPoint(head) - from).normalized), out var h, 2f * scale)) continue;
            best = h;
            hittable = IsHittable(col);
            return true;
        }

        return false;
    }

    /// <summary>The weapon's own swing sound, from the head, through the game's sound-effects mix.</summary>
    private static void Whoosh(Vector3 at, float volume)
    {
        var clips = _weapon.m_AttackClips;
        if (clips == null || clips.Count == 0) return;
        if (_audio == null)
        {
            var go = new GameObject("InkVR_SwingAudio");
            Object.DontDestroyOnLoad(go);
            _audio = go.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
        }

        if (_audio.outputAudioMixerGroup == null)
            foreach (var s in GameManager.Instance.Player.GetComponentsInChildren<AudioSource>(true))
                if (s.outputAudioMixerGroup != null)
                {
                    _audio.outputAudioMixerGroup = s.outputAudioMixerGroup;
                    break;
                }

        var scale = VRRig.ActiveScale;
        _audio.minDistance = 1f * scale;
        _audio.maxDistance = 20f * scale;
        _audio.transform.position = at;
        _audio.PlayOneShot(clips[Random.Range(0, clips.Count)], volume);
    }

    // The game's own button swing animates the weapon: not an arm swing. (Attack() runs every frame; the
    // swing events only fire for a real swing.)
    [HarmonyPostfix, HarmonyPatch(nameof(BaseWeapon.HandleSwingBegin))]
    private static void AfterSwingBegin()
    {
        _gameSwingUntil = Time.unscaledTime + 1.0f;
        GameSwings++;
    }

    public static int GameSwings;

    /// <summary>The game's own button swing is animating the weapon right now.</summary>
    public static bool GameSwinging => Time.unscaledTime < _gameSwingUntil;

    [HarmonyPostfix, HarmonyPatch(nameof(BaseWeapon.HandleSwingComplete))]
    private static void AfterSwingComplete() => _gameSwingUntil = Mathf.Min(_gameSwingUntil, Time.unscaledTime + 0.1f);

    // ---------------- dev: a scripted swing through a target ----------------

    private static Vector3? _devTarget;
    private static float _devStart;
    private static int _devChops = 1;
    private static int _devHitsAtStart;

    /// <summary>Dev: swing the right hand (dominant) down through a world point; with chops > 1, chop-chop (down,
    /// straight back up, down again) with no pause between.</summary>
    public static void DevSwing(Vector3 target, int chops = 1)
    {
        _devTarget = target;
        _devChops = Mathf.Max(1, chops);
        _devStart = Time.unscaledTime + 0.6f;
        _devHitsAtStart = Hits;
    }

    /// <summary>Runs before the hands are posed: drives the right controller through the scripted swing.</summary>
    public static void DevPreUpdate()
    {
        if (!_devTarget.HasValue || !HeadInAim.HasValue || VRRig.ActiveOrigin == null) return;
        var origin = VRRig.ActiveOrigin;
        var target = origin.InverseTransformPoint(_devTarget.Value) - Vector3.up * VRRig.HeightLift;
        var eye = VRRig.HeadLocalPosition;
        var flat = Vector3.ProjectOnPlane(target - eye, Vector3.up);
        var rot = Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward);
        var right = rot * Vector3.right;
        // ~4 m/s at the head; a chop-chop goes down, straight back up and down again, 0.25 s each way
        var since = Time.unscaledTime - _devStart;
        var leg = Mathf.Clamp(since / 0.25f, 0f, _devChops * 2 - 1);
        var k = Mathf.FloorToInt(Mathf.Min(leg, _devChops * 2 - 1.0001f));
        var f = leg - k;
        var t = k % 2 == 0 ? f : 1f - f;
        var path = Vector3.Lerp(target + Vector3.up * 0.45f + right * 0.15f, target - Vector3.up * 0.45f - right * 0.15f, t);
        XrInput.DevRightAim = (path - rot * HeadInAim.Value, rot);
        XrInput.ApplyDev();
        if (_devChops == 1 && since > -0.05f && since < 0.35f)
            Plugin.L.LogInfo($"SWINGF t={since:F3} speed={Speed:F2} swinging={_swinging} wall={Hands.WallTurn:F0}/{Hands.WallPush.magnitude / VRRig.ActiveScale * 100f:F0}cm head={(_model != null ? _model.TransformPoint(PointsLocal[0]) : Vector3.zero)} aim={XrInput.Get(Hand.Right).AimPosition} valid={XrInput.Get(Hand.Right).PoseValid} dom={ControllerBindings.Dominant} melee={ModConfig.PhysicalMelee.Value} locked={GameManager.Instance?.Player?.isMoveLocked} gameSwing={Time.unscaledTime < _gameSwingUntil} gameSwings={GameSwings}");
        if (since > (_devChops * 2 - 1) * 0.25f + 0.35f)
        {
            Plugin.L.LogInfo($"SWING done: chops={_devChops} swings={Swings} hits={Hits} (this test {Hits - _devHitsAtStart}) peak={PeakSpeed:F1} m/s last=[{LastHit}]");
            _devTarget = null;
        }
    }
}
