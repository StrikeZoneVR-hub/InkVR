using System;
using System.Collections.Generic;
using InkVR.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace InkVR.Body;

/// <summary>
/// One of Henry's hands with its forearm and upper arm, built procedurally (metres, local space).
/// Canonical hand frame: wrist at the origin, fingers along +Z, palm facing -Y, thumb towards -X on
/// the right hand (+X on the left). Fingers are rigid jointed segments curled about their local X.
/// </summary>
public class HandModel
{
    private struct Finger
    {
        public Transform[] Joints;
        public float[] Rest, Fist;
        public float Spread;
        public bool IsThumb;
    }

    public readonly bool IsLeft;
    public readonly Transform Root;     // grip pose (set every frame)
    private readonly Transform _wrist;  // hand origin inside Root (grip offset)
    private readonly Transform _arm;    // forearm + upper arm container (world-posed by IK)
    private readonly Transform _forearm, _upperArm, _elbow;
    private readonly Finger[] _fingers = new Finger[4];
    private readonly Finger _thumb;
    private readonly Transform _hourHand, _minuteHand;
    private readonly List<Renderer> _renderers = new();

    /// <summary>The skin and sleeve parts ink can get onto.</summary>
    public readonly List<StainPart> Stains = new();
    private readonly Dictionary<string, Material> _materials = new();
    private readonly float _side;

    private float _indexCurl = 0.2f, _gripCurl = 0.2f, _thumbCurl = 0.2f;

    private const float ForearmLength = 0.26f, UpperArmLength = 0.30f;

    public HandModel(bool left, Transform parent, Func<string, Material> material)
    {
        IsLeft = left;
        _side = left ? -1f : 1f;

        Root = new GameObject(left ? "InkVR_HenryHand_L" : "InkVR_HenryHand_R").transform;
        Root.SetParent(parent, false);
        _wrist = new GameObject("Wrist").transform;
        _wrist.SetParent(Root, false);
        // Grip space: the handle runs along +Z, palm normal along X. Fingers (+Z here) point down the
        // handle side (-Y grip), the back of the hand faces outward (+X right hand, -X left hand).
        var rot = Quaternion.LookRotation(Vector3.down, new Vector3(_side, 0, 0));
        _wrist.localRotation = rot;
        _wrist.localPosition = new Vector3(0.032f * _side, 0, 0) - rot * new Vector3(0, 0, 0.05f);

        // Palm: a flattened tube from wrist to knuckles.
        var palm = new MeshBuilder();
        palm.Tube(new[] { new Vector3(0, 0, 0.0f), new Vector3(0, 0.002f, 0.03f), new Vector3(0, 0.003f, 0.065f), new Vector3(0, 0.003f, 0.088f) },
            new[] { 0.027f, 0.039f, 0.043f, 0.040f }, Vector3.up, 18, true, true, new Vector2(1f, 0.36f));
        Part("Palm", _wrist, palm, material("palm"), 0.0018f, stain: true);

        // Fingers: index, middle, ring, pinky.
        float[] xs = { -0.030f, -0.0095f, 0.0105f, 0.0285f };
        float[] zs = { 0.090f, 0.093f, 0.090f, 0.082f };
        float[] spread = { -4f, 0f, 4f, 9f };
        float[][] lengths = { new[] { 0.040f, 0.024f, 0.021f }, new[] { 0.044f, 0.028f, 0.022f }, new[] { 0.041f, 0.026f, 0.021f }, new[] { 0.032f, 0.019f, 0.018f } };
        float[] radii = { 0.0092f, 0.0095f, 0.0089f, 0.0078f };
        for (var f = 0; f < 4; f++)
        {
            var joints = new Transform[3];
            var parentT = _wrist;
            var pos = new Vector3(xs[f] * _side, 0.004f, zs[f]);
            for (var j = 0; j < 3; j++)
            {
                var joint = new GameObject($"Finger{f}_{j}").transform;
                joint.SetParent(parentT, false);
                joint.localPosition = pos;
                if (j == 0) joint.localRotation = Quaternion.Euler(0, spread[f] * _side, 0);
                var r0 = radii[f] * (1f - 0.05f * j);
                var seg = new MeshBuilder();
                seg.Tube(new[] { Vector3.zero, new Vector3(0, 0, lengths[f][j]) }, new[] { r0, r0 * 0.95f }, Vector3.up, 12, j == 0, true);
                Part($"Seg{f}_{j}", joint, seg, material(j == 2 ? "tip" : "finger"), 0.0015f, stain: true);
                if (j > 0)
                {
                    // The finger's middle/end knuckle: a slight bulge, wider than the segments either side.
                    var bump = new MeshBuilder();
                    bump.Ellipsoid(new Vector3(0, 0.0012f, 0), new Vector3(r0 * 1.16f, r0 * 1.1f, r0 * 0.8f), Quaternion.identity, 12, 7);
                    Part($"Joint{f}_{j}", joint, bump, material("finger"), 0.0011f, stain: true);
                }
                joints[j] = joint;
                parentT = joint;
                pos = new Vector3(0, 0, lengths[f][j]);
            }

            _fingers[f] = new Finger { Joints = joints, Rest = new[] { 8f, 10f, 6f }, Fist = new[] { 82f, 96f, 58f }, Spread = spread[f] * _side };

            // Knuckle on the back of the hand where the finger meets it.
            var knuckle = new MeshBuilder();
            knuckle.Ellipsoid(new Vector3(xs[f] * _side, 0.0104f, zs[f] - 0.006f), new Vector3(radii[f] * 1.02f, 0.0058f, radii[f] * 1.0f), Quaternion.identity, 12, 7);
            Part($"Knuckle{f}", _wrist, knuckle, material("palm"), 0.0012f, stain: true);

            // Henry's wedding band (Linda), on the left ring finger.
            if (left && f == 2)
            {
                var ring = new MeshBuilder();
                ring.Torus(new Vector3(0, 0, 0.012f), Vector3.forward, radii[f] + 0.0017f, 0.0025f, 24, 10);
                Part("WeddingBand", joints[0], ring, material("gold"), 0.0007f);
            }
        }

        // Thumb: three segments from the base of the palm, opposed to the fingers.
        var thumbJoints = new Transform[3];
        var thumbParent = _wrist;
        var thumbPos = new Vector3(-0.025f * _side, -0.006f, 0.020f);
        var thumbDir = new Vector3(-0.45f * _side, -0.22f, 0.86f).normalized;
        var thumbUp = new Vector3(-0.65f * _side, 0.75f, 0.1f).normalized;
        float[] thumbLen = { 0.040f, 0.032f, 0.026f };
        float[] thumbRad = { 0.0128f, 0.0108f, 0.0099f };
        for (var j = 0; j < 3; j++)
        {
            var joint = new GameObject($"Thumb_{j}").transform;
            joint.SetParent(thumbParent, false);
            joint.localPosition = thumbPos;
            if (j == 0) joint.localRotation = Quaternion.LookRotation(thumbDir, thumbUp);
            var seg = new MeshBuilder();
            seg.Tube(new[] { Vector3.zero, new Vector3(0, 0, thumbLen[j]) }, new[] { thumbRad[j], thumbRad[j] * 0.93f }, Vector3.up, 12, true, true);
            Part($"ThumbSeg_{j}", joint, seg, material(j == 2 ? "tip" : "finger"), 0.0015f, stain: true);
            thumbJoints[j] = joint;
            thumbParent = joint;
            thumbPos = new Vector3(0, 0, thumbLen[j]);
        }

        _thumb = new Finger { Joints = thumbJoints, Rest = new[] { 0f, 8f, 6f }, Fist = new[] { 0f, 38f, 34f }, IsThumb = true };

        // Arm: forearm (skin + rolled sleeve) and upper arm (sleeve), posed by IK in world space.
        _arm = new GameObject(left ? "InkVR_HenryArm_L" : "InkVR_HenryArm_R").transform;
        _arm.SetParent(parent, false);
        _forearm = new GameObject("Forearm").transform;
        _forearm.SetParent(_arm, false);
        var fore = new MeshBuilder();
        fore.Tube(new[] { Vector3.zero, new Vector3(0, 0, ForearmLength * 0.5f), new Vector3(0, 0, ForearmLength) },
            new[] { 0.040f, 0.034f, 0.026f }, Vector3.up, 16, false, false, new Vector2(1f, 0.82f));
        Part("ForearmSkin", _forearm, fore, material("forearm"), 0.0022f, stain: true);
        var sleeve = new MeshBuilder();
        var cuff = ForearmLength * 0.48f;
        sleeve.Tube(new[] { new Vector3(0, 0, -0.01f), new Vector3(0, 0, cuff * 0.6f), new Vector3(0, 0, cuff - 0.03f), new Vector3(0, 0, cuff - 0.012f), new Vector3(0, 0, cuff) },
            new[] { 0.050f, 0.047f, 0.046f, 0.052f, 0.049f }, Vector3.up, 18, false, false, new Vector2(1f, 0.85f));
        Part("RolledSleeve", _forearm, sleeve, material("sleeveRolled"), 0.0026f, stain: true, sleeve: true);

        _elbow = new GameObject("Elbow").transform;
        _elbow.SetParent(_arm, false);
        var elbow = new MeshBuilder();
        elbow.Ellipsoid(Vector3.zero, new Vector3(0.05f, 0.048f, 0.05f), Quaternion.identity, 14, 8);
        Part("ElbowSleeve", _elbow, elbow, material("sleeve"), 0.0026f, stain: true, sleeve: true);

        _upperArm = new GameObject("UpperArm").transform;
        _upperArm.SetParent(_arm, false);
        var upper = new MeshBuilder();
        upper.Tube(new[] { Vector3.zero, new Vector3(0, 0, UpperArmLength) }, new[] { 0.055f, 0.050f }, Vector3.up, 16, true, false, new Vector2(1f, 0.9f));
        Part("UpperSleeve", _upperArm, upper, material("sleeve"), 0.0026f, stain: true, sleeve: true);

        // Wristwatch on the left wrist, its hands following your real clock.
        if (left)
        {
            // A sturdy 1930s wristwatch: wide leather strap, brass drum case with a crown, cream dial.
            var band = new MeshBuilder();
            band.Tube(new[] { new Vector3(0, 0, -0.032f), new Vector3(0, 0, -0.009f) }, new[] { 0.0296f, 0.0296f }, Vector3.up, 20, false, false, new Vector2(1f, 0.84f));
            Part("WatchStrap", _wrist, band, material("strap"), 0.0009f, stain: true);
            var face = new GameObject("WatchFace").transform;
            face.SetParent(_wrist, false);
            face.localPosition = new Vector3(0, 0.0243f, -0.0205f);
            var drum = new MeshBuilder();
            drum.Tube(new[] { Vector3.zero, new Vector3(0, 0.0068f, 0) }, new[] { 0.0188f, 0.0180f }, Vector3.forward, 26, true, true);
            Part("WatchCase", face, drum, material("gold"), 0.0011f);
            var crown = new MeshBuilder();
            crown.Tube(new[] { new Vector3(0, 0.0034f, 0.0178f), new Vector3(0, 0.0034f, 0.0218f) }, new[] { 0.0029f, 0.0029f }, Vector3.up, 10, true, true);
            Part("WatchCrown", face, crown, material("gold"), 0.0007f);
            var dial = new MeshBuilder();
            dial.Ellipsoid(new Vector3(0, 0.0068f, 0), new Vector3(0.0162f, 0.0016f, 0.0162f), Quaternion.identity, 26, 8);
            Part("WatchDial", face, dial, material("dial"), 0);
            _hourHand = WatchHand(face, 0.0088f, 0.0013f, material("ink"));
            _minuteHand = WatchHand(face, 0.0128f, 0.0009f, material("ink"));
        }
    }

    private Transform WatchHand(Transform face, float length, float width, Material m)
    {
        var pivot = new GameObject("WatchHand").transform;
        pivot.SetParent(face, false);
        pivot.localPosition = new Vector3(0, 0.0087f, 0);
        var mb = new MeshBuilder();
        mb.Tube(new[] { Vector3.zero, new Vector3(0, 0, length) }, new[] { width, width * 0.45f }, Vector3.up, 6, true, true, new Vector2(1f, 0.4f));
        Part("Hand", pivot, mb, m, 0);
        return pivot;
    }

    private void Part(string name, Transform parent, MeshBuilder mb, Material material, float outline, bool stain = false, bool sleeve = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(name);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        _renderers.Add(r);
        if (stain)
        {
            var part = InkStains.Register(go.transform, r, material, mb, sleeve);
            if (part != null) Stains.Add(part);
        }
        if (outline <= 0) return;
        var o = new GameObject(name + "_Ink");
        o.transform.SetParent(go.transform, false);
        o.AddComponent<MeshFilter>().sharedMesh = mb.ToOutlineMesh(name + "_Ink", outline);
        var orr = o.AddComponent<MeshRenderer>();
        orr.sharedMaterial = InkMaterials.Outline;
        orr.shadowCastingMode = ShadowCastingMode.Off;
        orr.receiveShadows = false;
        _renderers.Add(orr);
    }

    public Vector3 WristPosition => _wrist.position;

    public void SetVisible(bool hand, bool arm)
    {
        if (Root.gameObject.activeSelf != hand) Root.gameObject.SetActive(hand);
        if (_arm.gameObject.activeSelf != arm) _arm.gameObject.SetActive(arm);
    }

    public void SetLayer(int layer)
    {
        foreach (var r in _renderers)
            if (r != null && r.gameObject.layer != layer)
                r.gameObject.layer = layer;
    }

    /// <summary>Finger curls 0..1 (index from the trigger, the rest from the grip, thumb from touch).</summary>
    public void Animate(float index, float grip, float thumb, float dt)
    {
        var k = 1f - Mathf.Exp(-dt * 18f);
        _indexCurl = Mathf.Lerp(_indexCurl, index, k);
        _gripCurl = Mathf.Lerp(_gripCurl, grip, k);
        _thumbCurl = Mathf.Lerp(_thumbCurl, thumb, k);

        Curl(_fingers[0], _indexCurl);
        for (var f = 1; f < 4; f++) Curl(_fingers[f], Mathf.Min(1f, _gripCurl + f * 0.02f));
        Curl(_thumb, _thumbCurl);

        if (_hourHand != null)
        {
            var now = DateTime.Now;
            _minuteHand.localRotation = Quaternion.Euler(0, (now.Minute + now.Second / 60f) * 6f, 0);
            _hourHand.localRotation = Quaternion.Euler(0, (now.Hour % 12 + now.Minute / 60f) * 30f, 0);
        }
    }

    private static void Curl(Finger f, float t)
    {
        // The thumb's base keeps its opposed orientation; only its two outer joints bend.
        for (var j = f.IsThumb ? 1 : 0; j < f.Joints.Length; j++)
        {
            var angle = Mathf.Lerp(f.Rest[j], f.Fist[j], t);
            f.Joints[j].localRotation = Quaternion.Euler(angle, j == 0 ? f.Spread : 0, 0);
        }
    }

    /// <summary>Two-bone arm IK: shoulder → elbow → wrist, elbow pushed towards the hint.</summary>
    public void SolveArm(Vector3 shoulder, Vector3 elbowHint, float scale)
    {
        var a = UpperArmLength * scale;
        var b = ForearmLength * scale;
        var wrist = WristPosition;
        var toWrist = wrist - shoulder;
        var d = Mathf.Clamp(toWrist.magnitude, Mathf.Abs(a - b) + 1e-4f, (a + b) * 0.999f);
        var dir = toWrist.normalized;
        var cosA = (a * a + d * d - b * b) / (2 * a * d);
        var sinA = Mathf.Sqrt(Mathf.Max(0, 1 - cosA * cosA));
        var bend = Vector3.ProjectOnPlane(elbowHint - shoulder, dir).normalized;
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.down;
        var elbow = shoulder + dir * (a * cosA) + bend * (a * sinA);

        var handUp = _wrist.rotation * Vector3.up;
        _forearm.position = elbow;
        _forearm.rotation = Quaternion.LookRotation(wrist - elbow, handUp);
        _forearm.localScale = new Vector3(scale, scale, Vector3.Distance(elbow, wrist) / ForearmLength);
        _elbow.position = elbow;
        _elbow.rotation = _forearm.rotation;
        _elbow.localScale = Vector3.one * scale;
        _upperArm.position = shoulder;
        _upperArm.rotation = Quaternion.LookRotation(elbow - shoulder, handUp);
        _upperArm.localScale = new Vector3(scale, scale, Vector3.Distance(shoulder, elbow) / UpperArmLength);
    }

    public void Destroy()
    {
        if (Root != null) Object.Destroy(Root.gameObject);
        if (_arm != null) Object.Destroy(_arm.gameObject);
    }
}
