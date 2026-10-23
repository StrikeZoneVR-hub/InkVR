using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InkVR.Core;
using UnityEngine;

namespace InkVR.Input;

/// <summary>
/// The game finds what you can interact with by sphere-casting from the camera, then highlights it and
/// shows its prompt. In VR it is your hands that find things: hover either hand over, touch, or reach
/// towards something interactable and the game's own highlight and prompt appear; the interact button
/// (grip or X) then uses it. The game is handed a ray from your head to the spot your hand found (its
/// targeting is built for camera rays), so everything downstream stays the game's own behaviour.
/// Which hand found it is kept in <see cref="HoverHand"/>: the groundwork for motion controls.
/// With interact set to "look at it", the game's own look targeting is left alone.
/// </summary>
[HarmonyPatch(typeof(InteractableInputController))]
public static class InteractionAim
{
    /// <summary>The hand currently over an interactable (null when neither is).</summary>
    public static Hand? HoverHand { get; private set; }

    /// <summary>The interactable a hand is over.</summary>
    public static Interactable Hovered { get; private set; }

    /// <summary>Dev: what the hands last found (collider, how, where).</summary>
    public static string LastFind = "";

    private static float _savedLookDistance = -1;
    private static int _pickedFrame = -1;
    private static bool _picked;
    private static Vector3 _origin, _direction;
    private static float _reach;
    private static Il2CppReferenceArray<Collider> _overlap;
    private static Vector3 _palm;

    private static bool Pick(InteractableInputController ctrl, out Vector3 origin, out Vector3 direction, out float reach)
    {
        if (_pickedFrame != Time.frameCount)
        {
            _pickedFrame = Time.frameCount;
            _picked = Compute(ctrl, out _origin, out _direction, out _reach);
        }

        origin = _origin;
        direction = _direction;
        reach = _reach;
        return _picked;
    }

    private static bool Compute(InteractableInputController ctrl, out Vector3 origin, out Vector3 direction, out float reach)
    {
        origin = direction = default;
        reach = 0;
        if (!ModConfig.InteractWithHand.Value || !XrManager.IsRunning || !VRRig.InGame || VRRig.CinemaActive || !XrInput.Ready) return false;
        var head = VRRig.Head;
        if (head == null) return false;

        var scale = VRRig.ActiveScale;
        var mask = ~ctrl.m_IgnoreLayers.value;
        var best = float.MaxValue;
        Hand? bestHand = null;
        Interactable bestItem = null;
        var bestPoint = Vector3.zero;
        var bestPalm = Vector3.zero;
        Collider bestCollider = null;

        var why = "";
        void Consider(Hand hand, Collider col, Vector3 point, float score, string how)
        {
            if (col == null || score >= best) return;
            var item = col.GetComponentInParent<Interactable>();
            if (item == null || !item.isActiveAndEnabled) return;
            why = $"{how} {col.name} (trigger={col.isTrigger}, layer={col.gameObject.layer}) item={item.name} at {point}";
            best = score;
            bestHand = hand;
            bestItem = item;
            bestPoint = point;
            bestPalm = _palm;
            bestCollider = col;
        }

        _overlap ??= new Il2CppReferenceArray<Collider>(16);
        foreach (var hand in new[] { ControllerBindings.Dominant, ControllerBindings.OffHand })
        {
            var c = XrInput.Get(hand);
            var dev = hand == Hand.Right ? Body.Hands.DevRight : Body.Hands.DevLeft;
            if (!c.PoseValid && !dev.HasValue) continue;
            VRRig.TrackingToWorld(dev?.pos ?? c.GripPosition, dev?.rot ?? c.GripRotation, out var grip, out var gripRot);
            var pointing = gripRot * Vector3.forward;
            var palmNormal = gripRot * (hand == Hand.Right ? Vector3.left : Vector3.right);
            var palm = grip + pointing * 0.05f * scale;
            _palm = palm;

            // 1. Touching: anything interactable right at the hand (touching beats reaching).
            var n = Physics.OverlapSphereNonAlloc(palm, 0.10f * scale, _overlap, mask, QueryTriggerInteraction.Collide);
            for (var i = 0; i < n; i++)
            {
                var col = _overlap[i];
                if (col == null) continue;
                var p = col.bounds.ClosestPoint(palm);
                Consider(hand, col, p, (p - palm).magnitude * 0.5f, $"{hand} touch");
            }

            // 2. Reaching towards it, or 3. holding the palm over it.
            foreach (var (dir, len) in new[] { (pointing, 0.40f), (palmNormal, 0.28f) })
            {
                var start = palm - dir * 0.06f * scale;
                if (Physics.SphereCast(start, 0.05f * scale, dir, out var hit, (len + 0.06f) * scale, mask, QueryTriggerInteraction.Collide))
                    Consider(hand, hit.collider, hit.point, hit.distance, $"{hand} cast");
            }
        }

        HoverHand = bestHand;
        Hovered = bestItem;
        LastFind = why;
        var eye = head.position;
        if (bestHand == null)
        {
            // Nothing at either hand: a ray that can't find anything.
            origin = eye;
            direction = head.forward;
            reach = 0.0001f;
            return true;
        }

        // The game's own targeting, on a short ray through what your hand found, from just outside it on the
        // hand's side (a cast that starts inside a collider can't see it, and big pick-up zones surround the
        // hand; from outside, nothing between your head and your hand can get in the way either).
        var b = bestCollider.bounds;
        var approach = b.center - bestPalm;
        if (b.Contains(bestPalm) || approach.sqrMagnitude < 1e-6f) approach = b.center - eye;
        direction = approach.normalized;
        var radius = b.extents.magnitude;
        origin = b.center - direction * (radius + 0.25f * scale);
        reach = radius * 2f + 0.5f * scale;
        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(InteractableInputController.UpdateInteraction), typeof(Vector3), typeof(Vector3))]
    private static void Aim2(InteractableInputController __instance, ref Vector3 origin, ref Vector3 direction)
    {
        if (!Pick(__instance, out var o, out var d, out var reach)) return;
        origin = o;
        direction = d;
        _savedLookDistance = __instance.m_LookDistance;
        __instance.m_LookDistance = reach;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(InteractableInputController.UpdateInteraction), typeof(Vector3), typeof(Vector3))]
    private static void After2(InteractableInputController __instance)
    {
        if (_savedLookDistance < 0) return;
        __instance.m_LookDistance = _savedLookDistance;
        _savedLookDistance = -1;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(InteractableInputController.UpdateInteraction), typeof(Vector3), typeof(Vector3), typeof(float))]
    private static void Aim3(InteractableInputController __instance, ref Vector3 __0, ref Vector3 __1, ref float __2)
    {
        if (!Pick(__instance, out var o, out var d, out var reach)) return;
        __0 = o;
        __1 = d;
        __2 = reach;
    }
}
