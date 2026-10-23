using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InkVR.Core;
using InkVR.Input;
using InkVR.UI;
using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// Swing your arm to hit. The held melee weapon's head (its far end from your hand) is swept through the
/// world every frame; when it moves fast enough and passes through something solid, the game's own hit
/// runs on that spot (BaseWeapon.OnRaycastHit: the same damage, breaking, sounds and effects as a button
/// swing). One hit per swing; a swing ends when the weapon slows down. Speed is measured in your play
/// space, so walking and turning never count as swinging. The attack button still plays the game's swing.
/// </summary>
[HarmonyPatch(typeof(BaseWeapon))]
public static class MeleeSwing
{
    private const float StartSpeed = 2.2f; // m/s at the weapon head: a deliberate swing, not a wave
    private const float EndSpeed = 1.1f;
    private const float Cooldown = 0.35f;  // s between hits (the game's own swing takes about 0.6 s)
    private const float HeadRadius = 0.07f; // m
    private const float MaxSpeed = 25f;     // faster than any arm: a tracking glitch

    private static BaseWeapon _weapon;
    private static Transform _model;
    private static Vector3 _headLocal, _neckLocal; // the far end of the weapon, and a little way back from it
    private static Vector3 _prevHead, _prevNeck, _prevHeadTracking;
    private static bool _havePrev, _swinging, _hitThisSwing;
    private static float _lastHit = -10f, _slowFor, _gameSwingUntil;
    private static AudioSource _audio;
    private static Il2CppStructArray<RaycastHit> _hits;
    private static Il2CppReferenceArray<Collider> _overlap;

    /// <summary>Head speed this frame (m/s, play space), the fastest this swing, and totals (dev/log).</summary>
    public static float Speed, PeakSpeed;
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
        var head = _model.TransformPoint(_headLocal);
        var neck = _model.TransformPoint(_neckLocal);
        var headTracking = origin.InverseTransformPoint(head); // metres: the origin carries the world scale
        var c = ControllerBindings.Dom;
        if (c.PoseValid)
            HeadInAim = Quaternion.Inverse(c.AimRotation) * (headTracking - Vector3.up * ModConfig.HeightOffset.Value - c.AimPosition);

        var dt = Time.unscaledDeltaTime;
        if (!_havePrev || dt < 1e-4f)
        {
            Remember(head, neck, headTracking);
            return;
        }

        Speed = (headTracking - _prevHeadTracking).magnitude / dt;
        if (Speed > MaxSpeed || !c.PoseValid)
        {
            // Tracking jumped (lost and found again, or a recentre): not a swing.
            Speed = 0;
            _swinging = false;
            Remember(head, neck, headTracking);
            return;
        }

        var now = Time.unscaledTime;
        var gameSwing = now < _gameSwingUntil;
        if (!ModConfig.PhysicalMelee.Value || gameSwing || !VRRig.InGame || VRRig.CinemaActive || GameManager.Instance?.Player?.isMoveLocked == true)
        {
            _swinging = false;
            Remember(head, neck, headTracking);
            return;
        }

        if (!_swinging && Speed >= StartSpeed)
        {
            _swinging = true;
            _hitThisSwing = false;
            _slowFor = 0;
            PeakSpeed = Speed;
            Swings++;
            Whoosh(head);
            XrInput.Haptic(ControllerBindings.Dominant, 0.12f, 0.03f);
        }
        else if (_swinging)
        {
            PeakSpeed = Mathf.Max(PeakSpeed, Speed);
            _slowFor = Speed < EndSpeed ? _slowFor + dt : 0;
            if (_slowFor > 0.12f) _swinging = false;
        }

        if (_swinging && !_hitThisSwing && now - _lastHit > Cooldown && Sweep(head, neck, out var hit))
        {
            _hitThisSwing = true;
            _lastHit = now;
            Hits++;
            LastHit = $"{Dev.DevTools.PathOf(hit.collider.transform)} at {hit.point} speed {PeakSpeed:F1} m/s";
            Plugin.L.LogInfo($"Swing hit {LastHit}");
            XrInput.Haptic(ControllerBindings.Dominant, 0.9f, 0.12f);
            _weapon.OnRaycastHit(hit);
        }

        Remember(head, neck, headTracking);
    }

    private static void Remember(Vector3 head, Vector3 neck, Vector3 headTracking)
    {
        _prevHead = head;
        _prevNeck = neck;
        _prevHeadTracking = headTracking;
        _havePrev = true;
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
        var near = far == a ? z : a;
        _headLocal = Vector3.Lerp(near, far, 0.92f);
        _neckLocal = Vector3.Lerp(near, far, 0.72f);
        Plugin.L.LogInfo($"Melee weapon: {weapon.name}, swing to hit (head at {_headLocal} of {b.size} in {_model.name})");
    }

    /// <summary>Where the hand holds the weapon: the game's HandContainer/Hand mount.</summary>
    private static Vector3 HandPoint(BaseWeapon weapon)
    {
        var container = GameManager.Instance?.Player?.m_HandContainer;
        return container != null && container.childCount > 0 ? container.GetChild(0).position : weapon.transform.position;
    }

    /// <summary>Sweep the head (and a point behind it) from last frame to now; the nearest solid hit wins.</summary>
    private static bool Sweep(Vector3 head, Vector3 neck, out RaycastHit best)
    {
        best = default;
        var scale = VRRig.ActiveScale;
        var radius = HeadRadius * scale;
        var mask = ~_weapon.m_IgnoreLayers.value & ~(1 << VRLayers.UI);
        var player = GameManager.Instance.Player.transform;
        _hits ??= new Il2CppStructArray<RaycastHit>(16);
        var found = false;
        var bestDistance = float.MaxValue;

        foreach (var (from, to) in new[] { (_prevHead, head), (_prevNeck, neck) })
        {
            var move = to - from;
            var length = move.magnitude;
            if (length < 1e-5f) continue;
            var dir = move / length;
            var start = from - dir * radius; // starting a little behind: a cast can't see what it starts inside
            var n = Physics.SphereCastNonAlloc(start, radius, dir, _hits, length + radius, mask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider == null || h.distance <= 0f || h.collider.transform.IsChildOf(player)) continue;
                if (h.distance >= bestDistance) continue;
                bestDistance = h.distance;
                best = h;
                found = true;
            }
        }

        if (found) return true;

        // Already inside something (pressed against a plank, then swung through it): hit it from just behind the head.
        _overlap ??= new Il2CppReferenceArray<Collider>(8);
        var count = Physics.OverlapSphereNonAlloc(head, radius, _overlap, mask, QueryTriggerInteraction.Ignore);
        var back = (head - _prevHead).normalized;
        for (var i = 0; i < count; i++)
        {
            var col = _overlap[i];
            if (col == null || col.transform.IsChildOf(player) || back == Vector3.zero) continue;
            var from = head - back * (radius * 4f + 0.3f * scale);
            if (!col.Raycast(new Ray(from, (col.ClosestPoint(head) - from).normalized), out var h, 2f * scale)) continue;
            best = h;
            return true;
        }

        return false;
    }

    /// <summary>The weapon's own swing sound, from the head, through the game's sound-effects mix.</summary>
    private static void Whoosh(Vector3 at)
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
        _audio.PlayOneShot(clips[Random.Range(0, clips.Count)]);
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

    [HarmonyPostfix, HarmonyPatch(nameof(BaseWeapon.HandleSwingComplete))]
    private static void AfterSwingComplete() => _gameSwingUntil = Mathf.Min(_gameSwingUntil, Time.unscaledTime + 0.1f);

    // ---------------- dev: a scripted swing through a target ----------------

    private static Vector3? _devTarget;
    private static float _devStart;

    /// <summary>Dev: swing the right hand (dominant) down through a world point, once.</summary>
    public static void DevSwing(Vector3 target)
    {
        _devTarget = target;
        _devStart = Time.unscaledTime + 0.6f;
    }

    /// <summary>Runs before the hands are posed: drives the right controller through the scripted swing.</summary>
    public static void DevPreUpdate()
    {
        if (!_devTarget.HasValue || !HeadInAim.HasValue || VRRig.ActiveOrigin == null) return;
        var origin = VRRig.ActiveOrigin;
        var target = origin.InverseTransformPoint(_devTarget.Value) - Vector3.up * ModConfig.HeightOffset.Value;
        var eye = VRRig.HeadLocalPosition;
        var flat = Vector3.ProjectOnPlane(target - eye, Vector3.up);
        var rot = Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward);
        var right = rot * Vector3.right;
        var t = Mathf.Clamp01((Time.unscaledTime - _devStart) / 0.25f); // ~4 m/s at the head
        var path = Vector3.Lerp(target + Vector3.up * 0.45f + right * 0.15f, target - Vector3.up * 0.45f - right * 0.15f, t);
        XrInput.DevRightAim = (path - rot * HeadInAim.Value, rot);
        XrInput.ApplyDev();
        var since = Time.unscaledTime - _devStart;
        if (since > -0.05f && since < 0.35f)
            Plugin.L.LogInfo($"SWINGF t={since:F3} speed={Speed:F2} swinging={_swinging} head={(_model != null ? _model.TransformPoint(_headLocal) : Vector3.zero)} aim={XrInput.Get(Hand.Right).AimPosition} valid={XrInput.Get(Hand.Right).PoseValid} dom={ControllerBindings.Dominant} melee={ModConfig.PhysicalMelee.Value} locked={GameManager.Instance?.Player?.isMoveLocked} gameSwing={Time.unscaledTime < _gameSwingUntil} gameSwings={GameSwings}");
        if (Time.unscaledTime > _devStart + 0.6f)
        {
            Plugin.L.LogInfo($"SWING done: swings={Swings} hits={Hits} peak={PeakSpeed:F1} m/s last=[{LastHit}]");
            _devTarget = null;
        }
    }
}
