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
    private static readonly Beam Laser = new("InkVR_Laser");

    /// <summary>The pointer is over the game's UI while a menu wants pointer input (blocks gameplay buttons).</summary>
    public static bool IsOverInteractive { get; private set; }

    /// <summary>The laser is on the screen (so in the theatre the stick stays the menu's).</summary>
    public static bool OnScreen { get; private set; }

    /// <summary>Dev: run the laser on the mock runtime from XrInput's dev aim and trigger (no controllers there).</summary>
    public static bool DevForce;

    // Pulling a trigger and letting it go jolts the controller a little; on a small button that used to slide the
    // laser off it, and the click went to the background. So: the trigger presses at 55% and lets go at 30% (a
    // half-pull doesn't flicker), the spot steadies as you squeeze, it holds still on what you pressed, and letting
    // go anywhere near where you pressed clicks what you pressed.
    private const float PressAt = 0.55f, ReleaseAt = 0.3f;

    // In the UI texture's pixels (tuned at 2560 wide): how far the spot may slip off a pressed button, and drag.
    private const float SlipPx = 70f * UICapture.Width / 2560f, DragPx = 5f * UICapture.Width / 2560f;
    private static bool _trig, _haveSpot;
    private static Vector2 _spot;
    private static GameObject _clickHandler;
    private static EventSystem _pedSystem;

    public static void Update()
    {
        OnScreen = false;
        if (DevForce) XrInput.ApplyDev();
        var c = ControllerBindings.Dom;
        var was = _trig;
        _trig = _trig ? c.Trigger > ReleaseAt : c.Trigger > PressAt;
        var down = _trig && !was;
        var up = !_trig && was;

        if (!ModConfig.LaserPointer.Value)
        {
            Release();
            Show(false);
            IsOverInteractive = false;
            return;
        }

        var panel = VRRig.PointerPanel;
        var theatre = VRRig.CinemaShowing;
        if (!(XrInput.Ready || DevForce) || panel == null || !panel.Visible || !c.PoseValid || EventSystem.current == null || (theatre && !TheatreWalk.ScreenVisible))
        {
            Lost(up);
            return;
        }

        VRRig.TrackingToWorld(c.AimPosition, c.AimRotation, out var origin, out var rotation);
        var dir = rotation * Vector3.forward;
        var scale = panel.Transform.lossyScale.x / Mathf.Max(panel.Transform.localScale.x, 1e-4f); // rig scale
        var plane = new Plane(panel.Transform.forward, panel.Transform.position);
        var ray = new Ray(origin, dir);
        // (in the theatre you can point at the screen from the back of the balcony)
        if (!plane.Raycast(ray, out var dist) || dist <= 0 || dist > (theatre ? 30f : MaxLength) * scale)
        {
            Lost(up);
            return;
        }

        var hit = ray.GetPoint(dist);
        var local = panel.Transform.InverseTransformPoint(hit);
        if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f)
        {
            Lost(up);
            return;
        }

        OnScreen = true;
        Show(true);
        Laser.Draw(origin, hit, panel.Transform.rotation, scale);

        var pixel = new Vector2((local.x + 0.5f) * UICapture.Width, (local.y + 0.5f) * UICapture.Height);
        Process(pixel, c, down, up);
    }

    /// <summary>The laser left the screen: a press survives a moment off it (a jolt), and is let go with the trigger.</summary>
    private static void Lost(bool up)
    {
        _haveSpot = false;
        Show(false);
        if (!_trig || up) Release();
    }

    private static void Process(Vector2 raw, ControllerState c, bool down, bool up)
    {
        // a new event system (the game rebuilds its menus after quitting to the menu): new pointer data for it
        if (_ped == null || _pedSystem == null || _pedSystem != EventSystem.current)
        {
            _ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            _pedSystem = EventSystem.current;
            _hovered = _pressed = _drag = _clickHandler = null;
            _dragging = false;
        }

        _results ??= new RaycastResults();

        // steadier the more you squeeze the trigger
        var squeeze = Mathf.InverseLerp(0.08f, PressAt, c.Trigger);
        if (!_haveSpot)
        {
            _spot = raw;
            _haveSpot = true;
        }
        else _spot = Vector2.Lerp(_spot, raw, 1f - Mathf.Exp(-Time.unscaledDeltaTime * Mathf.Lerp(45f, 8f, squeeze)));

        // pressed on a button: it stays on that button unless you really move off it (a slider drags, though)
        var pixel = _spot;
        if (_pressed != null && _drag == null && (_spot - _ped.pressPosition).magnitude < SlipPx) pixel = _ped.pressPosition;

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

        if (down && target != null)
        {
            _ped.pressPosition = pixel;
            _ped.pointerPressRaycast = _results[0];
            _ped.eligibleForClick = true;
            _pressed = ExecuteEvents.ExecuteHierarchy(target, _ped, ExecuteEvents.pointerDownHandler) ?? target;
            _clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            _ped.pointerPress = _pressed;
            _ped.rawPointerPress = target;
            _drag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
            if (_drag != null) ExecuteEvents.Execute(_drag, _ped, ExecuteEvents.initializePotentialDrag);
            XrInput.Haptic(ControllerBindings.Dominant, 0.25f, 0.02f);
            LastClick = $"pressed {target.name} ({(_clickHandler != null ? _clickHandler.name : "no click handler")}) at {pixel}";
        }

        if (_trig && _drag != null)
        {
            if (!_dragging && (pixel - _ped.pressPosition).sqrMagnitude > DragPx * DragPx)
            {
                _dragging = true;
                _ped.dragging = true;
                _ped.pointerDrag = _drag;
                ExecuteEvents.Execute(_drag, _ped, ExecuteEvents.beginDragHandler);
            }

            if (_dragging) ExecuteEvents.Execute(_drag, _ped, ExecuteEvents.dragHandler);
        }

        if (up) Release(target, pixel);
    }

    /// <summary>Dev/log: what the laser last pressed and clicked.</summary>
    public static string LastClick = "";

    private static void Release(GameObject target = null, Vector2? at = null)
    {
        if (_ped == null) return;
        if (_pressed != null)
        {
            ExecuteEvents.Execute(_pressed, _ped, ExecuteEvents.pointerUpHandler);
            if (!_dragging)
            {
                // what you pressed is clicked if you let go on it, or near where you pressed it
                var here = target != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) : null;
                var near = at.HasValue && (at.Value - _ped.pressPosition).magnitude < SlipPx;
                if (_clickHandler != null && (here == _clickHandler || near))
                {
                    ExecuteEvents.Execute(_clickHandler, _ped, ExecuteEvents.pointerClickHandler);
                    LastClick += $" -> clicked {_clickHandler.name}";
                }
                else if (_clickHandler == null && target != null)
                {
                    ExecuteEvents.ExecuteHierarchy(target, _ped, ExecuteEvents.pointerClickHandler);
                    LastClick += $" -> clicked under {target.name}";
                }
                else LastClick += " -> let go off it (no click)";
                Plugin.L.LogInfo("Laser: " + LastClick);
            }
        }

        if (_dragging && _drag != null) ExecuteEvents.Execute(_drag, _ped, ExecuteEvents.endDragHandler);
        if (target == null && _hovered != null)
        {
            ExecuteEvents.ExecuteHierarchy(_hovered, _ped, ExecuteEvents.pointerExitHandler);
            _hovered = null;
        }

        _pressed = null;
        _drag = null;
        _clickHandler = null;
        _dragging = false;
        _ped.dragging = false;
        _ped.pointerPress = null;
        _ped.pointerDrag = null;
        IsOverInteractive = false;
    }

    // ---------------- visuals ----------------

    private static void Show(bool on) => Laser.Show(on);

    /// <summary>A laser beam from the hand with a soft dot where it lands (the menus' laser; the theatre mirror's too).</summary>
    public sealed class Beam
    {
        private readonly string _name;
        private LineRenderer _line;
        private VRPanel _dot;

        public Beam(string name) => _name = name;

        public void Show(bool on)
        {
            if (_line != null && _line.gameObject.activeSelf != on) _line.gameObject.SetActive(on);
            if (_dot != null && _dot.GameObject != null) _dot.Visible = on;
        }

        /// <summary>From the hand to a surface; <paramref name="surface"/> faces away from you (a VR panel's turn).</summary>
        public void Draw(Vector3 from, Vector3 to, Quaternion surface, float scale)
        {
            if (_line == null)
            {
                var go = new GameObject(_name);
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
                _dot = new VRPanel(_name + "Dot", null, Textures.SoftDot, new Color(1f, 0.9f, 0.6f, 1f), 60);
                Object.DontDestroyOnLoad(_dot.GameObject);
            }

            Show(true);
            _line.startWidth = 0.004f * scale;
            _line.endWidth = 0.0015f * scale;
            _line.SetPosition(0, from);
            _line.SetPosition(1, to);
            _dot.Transform.SetPositionAndRotation(to - surface * Vector3.forward * 0.002f * scale, surface);
            _dot.Transform.localScale = Vector3.one * 0.018f * scale;
        }
    }
}
