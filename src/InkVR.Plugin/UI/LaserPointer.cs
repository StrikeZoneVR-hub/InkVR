using InkVR.Core;
using InkVR.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using RaycastResults = Il2CppSystem.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>;

namespace InkVR.UI;

/// <summary>
/// Points the dominant controller at the screen showing the game's UI and drives the game's own
/// uGUI event system with it (hover, press, click, drag), so every existing menu works as-is.
/// </summary>
public static class LaserPointer
{
    private const float MaxLength = 8f;

    private static PointerEventData _ped;
    private static RaycastResults _results;
    private static GameObject _hovered, _pressed, _drag;
    private static bool _dragging;
    private static LineRenderer _line;
    private static VRPanel _dot;

    /// <summary>The pointer is over the game's UI while a menu wants pointer input (blocks gameplay buttons).</summary>
    public static bool IsOverInteractive { get; private set; }

    public static void Update()
    {
        var panel = VRRig.PointerPanel;
        var c = ControllerBindings.Dom;
        if (!XrInput.Ready || panel == null || !panel.Visible || !c.PoseValid || EventSystem.current == null)
        {
            Release();
            Show(false);
            return;
        }

        VRRig.TrackingToWorld(c.AimPosition, c.AimRotation, out var origin, out var rotation);
        var dir = rotation * Vector3.forward;
        var scale = panel.Transform.lossyScale.x / Mathf.Max(panel.Transform.localScale.x, 1e-4f); // rig scale
        var plane = new Plane(panel.Transform.forward, panel.Transform.position);
        var ray = new Ray(origin, dir);
        if (!plane.Raycast(ray, out var dist) || dist <= 0 || dist > MaxLength * scale)
        {
            Release();
            Show(false);
            return;
        }

        var hit = ray.GetPoint(dist);
        var local = panel.Transform.InverseTransformPoint(hit);
        if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f)
        {
            Release();
            Show(false);
            return;
        }

        Show(true);
        DrawLaser(origin, hit, panel, scale);

        var pixel = new Vector2((local.x + 0.5f) * UICapture.Width, (local.y + 0.5f) * UICapture.Height);
        Process(pixel, c);
    }

    private static void Process(Vector2 pixel, ControllerState c)
    {
        _ped ??= new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        _results ??= new RaycastResults();
        var delta = pixel - _ped.position;
        _ped.delta = delta;
        _ped.position = pixel;
        _results.Clear();
        EventSystem.current.RaycastAll(_ped, _results);
        var target = _results.Count > 0 ? _results[0].gameObject : null;
        if (_results.Count > 0) _ped.pointerCurrentRaycast = _results[0];

        if (target != _hovered)
        {
            if (_hovered != null) ExecuteEvents.ExecuteHierarchy(_hovered, _ped, ExecuteEvents.pointerExitHandler);
            if (target != null)
            {
                ExecuteEvents.ExecuteHierarchy(target, _ped, ExecuteEvents.pointerEnterHandler);
                XrInput.Haptic(ControllerBindings.Dominant, 0.08f, 0.012f);
            }

            _hovered = target;
        }

        IsOverInteractive = target != null && VRRig.MenuWantsPointer;

        var down = c.TriggerPressed && !c.PrevTriggerPressed;
        var up = !c.TriggerPressed && c.PrevTriggerPressed;

        if (down && target != null)
        {
            _ped.pressPosition = pixel;
            _ped.pointerPressRaycast = _results[0];
            _ped.eligibleForClick = true;
            _pressed = ExecuteEvents.ExecuteHierarchy(target, _ped, ExecuteEvents.pointerDownHandler) ?? target;
            _ped.pointerPress = _pressed;
            _ped.rawPointerPress = target;
            _drag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
            if (_drag != null) ExecuteEvents.Execute(_drag, _ped, ExecuteEvents.initializePotentialDrag);
            XrInput.Haptic(ControllerBindings.Dominant, 0.25f, 0.02f);
        }

        if (c.TriggerPressed && _drag != null)
        {
            if (!_dragging && (pixel - _ped.pressPosition).sqrMagnitude > 25f)
            {
                _dragging = true;
                _ped.dragging = true;
                _ped.pointerDrag = _drag;
                ExecuteEvents.Execute(_drag, _ped, ExecuteEvents.beginDragHandler);
            }

            if (_dragging) ExecuteEvents.Execute(_drag, _ped, ExecuteEvents.dragHandler);
        }

        if (up) Release(target);
    }

    private static void Release(GameObject target = null)
    {
        if (_ped == null) return;
        if (_pressed != null)
        {
            ExecuteEvents.Execute(_pressed, _ped, ExecuteEvents.pointerUpHandler);
            if (!_dragging && target != null)
                ExecuteEvents.ExecuteHierarchy(target, _ped, ExecuteEvents.pointerClickHandler);
        }

        if (_dragging && _drag != null) ExecuteEvents.Execute(_drag, _ped, ExecuteEvents.endDragHandler);
        if (target == null && _hovered != null)
        {
            ExecuteEvents.ExecuteHierarchy(_hovered, _ped, ExecuteEvents.pointerExitHandler);
            _hovered = null;
        }

        _pressed = null;
        _drag = null;
        _dragging = false;
        _ped.dragging = false;
        _ped.pointerPress = null;
        _ped.pointerDrag = null;
        IsOverInteractive = false;
    }

    // ---------------- visuals ----------------

    private static void Show(bool on)
    {
        if (_line != null && _line.gameObject.activeSelf != on) _line.gameObject.SetActive(on);
        if (_dot != null) _dot.Visible = on;
    }

    private static void DrawLaser(Vector3 from, Vector3 to, VRPanel panel, float scale)
    {
        if (_line == null)
        {
            var go = new GameObject("InkVR_Laser");
            Object.DontDestroyOnLoad(go);
            go.layer = VRLayers.UI;
            _line = go.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.material = new Material(Shader.Find("Sprites/Default")) { renderQueue = 4050 };
            _line.startColor = new Color(0.98f, 0.86f, 0.55f, 0.55f);
            _line.endColor = new Color(0.98f, 0.86f, 0.55f, 0.95f);
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
        }

        if (_dot == null || _dot.GameObject == null)
        {
            _dot = new VRPanel("InkVR_LaserDot", null, Textures.SoftDot, new Color(1f, 0.9f, 0.6f, 1f), 60);
            Object.DontDestroyOnLoad(_dot.GameObject);
        }

        _line.startWidth = 0.004f * scale;
        _line.endWidth = 0.0015f * scale;
        _line.SetPosition(0, from);
        _line.SetPosition(1, to);
        _dot.Transform.SetPositionAndRotation(to - panel.Transform.forward * 0.002f * scale, panel.Transform.rotation);
        _dot.Transform.localScale = Vector3.one * 0.018f * scale;
    }
}
