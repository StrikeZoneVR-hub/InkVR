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
public partial class HandModel
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
    private Transform _hourHand, _minuteHand;
    private readonly List<Renderer> _renderers = new();
    private readonly List<Renderer> _casters = new(); // the parts that cast your shadow (not the ink outlines)

    /// <summary>The skin and sleeve parts ink can get onto.</summary>
    public readonly List<StainPart> Stains = new();
    private readonly Dictionary<string, Material> _materials = new();
    private readonly float _side;

    private float _indexCurl = 0.2f, _gripCurl = 0.2f, _thumbCurl = 0.2f;

    private const float ForearmLength = 0.26f, UpperArmLength = 0.30f;

    /// <summary>Lifelike hands: one smooth skinned mesh per hand. Toon: the original jointed cartoon hands.</summary>
    public readonly bool Lifelike;

    /// <summary>Whose hands and arms these are (STYLE), and the watch on the left wrist (WATCH).</summary>
    public readonly StyleDef Style;

    public readonly WatchKind Watch;

    // The hand's joint layout (wrist space), shared by both styles.
    private static readonly float[] FingerX = { -0.030f, -0.0095f, 0.0105f, 0.0285f };
    private static readonly float[] FingerZ = { 0.090f, 0.093f, 0.090f, 0.082f };
    private static readonly float[] FingerSpread = { -4f, 0f, 4f, 9f };
    private static readonly float[][] FingerLengths = { new[] { 0.040f, 0.024f, 0.021f }, new[] { 0.044f, 0.028f, 0.022f }, new[] { 0.041f, 0.026f, 0.021f }, new[] { 0.032f, 0.019f, 0.018f } };
    private static readonly float[] FingerRadius = { 0.0092f, 0.0095f, 0.0089f, 0.0078f };
    private static readonly float[] ThumbLength = { 0.040f, 0.032f, 0.026f };
    private static readonly float[] ThumbRadius = { 0.0128f, 0.0108f, 0.0099f };

    public HandModel(bool left, Transform parent, Func<string, Material> baseMaterial, StyleDef style, WatchKind watch)
    {
        IsLeft = left;
        Style = style;
        Watch = watch;
        var lifelike = Lifelike = style.Hands == HandKind.Lifelike;
        _side = left ? -1f : 1f;
        // the style's own skin, gloves and sleeves stand in for Henry's
        Material material(string key) => baseMaterial(Styles.MapKey(style, key));

        Root = new GameObject(left ? "InkVR_HenryHand_L" : "InkVR_HenryHand_R").transform;
        Root.SetParent(parent, false);
        _wrist = new GameObject("Wrist").transform;
        _wrist.SetParent(Root, false);
        // Grip space: the handle runs along +Z, palm normal along X. Fingers (+Z here) point down the
        // handle side (-Y grip), the back of the hand faces outward (+X right hand, -X left hand).
        var rot = Quaternion.LookRotation(Vector3.down, new Vector3(_side, 0, 0));
        _wrist.localRotation = rot;
        _wrist.localPosition = new Vector3(0.032f * _side, 0, 0) - rot * new Vector3(0, 0, 0.05f);

        // The joints (both styles animate the same way): index, middle, ring, pinky, then the thumb.
        for (var f = 0; f < 4; f++)
        {
            var joints = new Transform[3];
            var parentT = _wrist;
            var pos = new Vector3(FingerX[f] * _side, 0.004f, FingerZ[f]);
            for (var j = 0; j < 3; j++)
            {
                var joint = new GameObject($"Finger{f}_{j}").transform;
                joint.SetParent(parentT, false);
                joint.localPosition = pos;
                if (j == 0) joint.localRotation = Quaternion.Euler(0, FingerSpread[f] * _side, 0);
                joints[j] = joint;
                parentT = joint;
                pos = new Vector3(0, 0, FingerLengths[f][j]);
            }

            _fingers[f] = new Finger { Joints = joints, Rest = new[] { 8f, 10f, 6f }, Fist = new[] { 82f, 96f, 58f }, Spread = FingerSpread[f] * _side };
        }

        var thumbJoints = new Transform[3];
        var thumbParent = _wrist;
        var thumbPos = new Vector3(-0.025f * _side, -0.006f, 0.020f);
        var thumbDir = new Vector3(-0.45f * _side, -0.22f, 0.86f).normalized;
        var thumbUp = new Vector3(-0.65f * _side, 0.75f, 0.1f).normalized;
        for (var j = 0; j < 3; j++)
        {
            var joint = new GameObject($"Thumb_{j}").transform;
            joint.SetParent(thumbParent, false);
            joint.localPosition = j == 0 ? thumbPos : new Vector3(0, 0, ThumbLength[j - 1]);
            if (j == 0) joint.localRotation = Quaternion.LookRotation(thumbDir, thumbUp);
            thumbJoints[j] = joint;
            thumbParent = joint;
        }

        _thumb = new Finger { Joints = thumbJoints, Rest = new[] { 0f, 8f, 6f }, Fist = new[] { 0f, 38f, 34f }, IsThumb = true };

        if (lifelike) BuildLifelike(material, thumbPos, thumbDir, thumbUp);
        else BuildToon(material);

        // Henry's wedding band (Linda), on the left ring finger (not over a cartoon glove); Joey's gold signet ring on his right.
        if (left && lifelike && style.WeddingBand)
        {
            var ring = new MeshBuilder();
            ring.Torus(new Vector3(0, 0, 0.012f), Vector3.forward, FingerRadius[2] * 1.1f + 0.0011f, 0.0025f, 24, 10);
            Part("WeddingBand", _fingers[2].Joints[0], ring, material("gold"), 0.0007f);
        }

        if (!left && lifelike && style.Id == BodyStyle.Joey)
        {
            var ring = new MeshBuilder();
            ring.Torus(new Vector3(0, 0, 0.011f), Vector3.forward, FingerRadius[2] * 1.1f + 0.0012f, 0.0027f, 24, 10);
            ring.Box(new Vector3(0, FingerRadius[2] * 1.1f + 0.0034f, 0.011f), new Vector3(0.0085f, 0.0026f, 0.0075f), Quaternion.identity);
            Part("SignetRing", _fingers[2].Joints[0], ring, material("gold"), 0.0007f);
        }

        // Bigger hands for the bigger characters (Brute Boris most of all).
        var handScale = Mathf.Lerp(1f, style.ArmBulk, 0.45f);
        if (handScale > 1.001f) _wrist.localScale = Vector3.one * handScale;

        // Arm: forearm (skin, a sleeve rolled up it or a jacket's sleeve down it, or bare) and upper arm, posed by IK in world space.
        _arm = new GameObject(left ? "InkVR_HenryArm_L" : "InkVR_HenryArm_R").transform;
        _arm.SetParent(parent, false);
        _forearm = new GameObject("Forearm").transform;
        _forearm.SetParent(_arm, false);
        var bulk = style.ArmBulk;
        var fore = new MeshBuilder();
        fore.Tube(new[] { Vector3.zero, new Vector3(0, 0, ForearmLength * 0.5f), new Vector3(0, 0, ForearmLength) },
            new[] { 0.040f * bulk, 0.034f * bulk, 0.026f * Mathf.Lerp(1f, bulk, 0.6f) }, Vector3.up, 16, false, false, new Vector2(1f, 0.82f));
        Part("ForearmSkin", _forearm, fore, material(lifelike ? "lifeForearm" : "toonForearm"), lifelike ? 0.0014f : 0.003f, stain: true);
        var bare = style.Sleeves == SleeveKind.Bare;
        if (style.Sleeves == SleeveKind.Rolled)
        {
            var sleeve = new MeshBuilder();
            var cuff = ForearmLength * 0.48f;
            sleeve.Tube(new[] { new Vector3(0, 0, -0.01f), new Vector3(0, 0, cuff * 0.6f), new Vector3(0, 0, cuff - 0.03f), new Vector3(0, 0, cuff - 0.012f), new Vector3(0, 0, cuff) },
                new[] { 0.050f * bulk, 0.047f * bulk, 0.046f * bulk, 0.052f * bulk, 0.049f * bulk }, Vector3.up, 18, false, false, new Vector2(1f, 0.85f));
            Part("RolledSleeve", _forearm, sleeve, material("sleeveRolled"), 0.0026f, stain: true, sleeve: true);
        }
        else if (style.Sleeves == SleeveKind.Full)
        {
            // a jacket's sleeve down to just above the wrist, the shirt's white cuff showing under it, a gold cufflink
            var end = ForearmLength - 0.045f;
            var sleeve = new MeshBuilder();
            sleeve.Tube(new[] { new Vector3(0, 0, -0.01f), new Vector3(0, 0, end * 0.5f), new Vector3(0, 0, end - 0.004f), new Vector3(0, 0, end) },
                new[] { 0.05f, 0.045f, 0.04f, 0.038f }, Vector3.up, 18, false, false, new Vector2(1f, 0.86f));
            Part("JacketSleeve", _forearm, sleeve, material("sleeve"), 0.0026f, stain: true, sleeve: true);
            var shirt = new MeshBuilder();
            shirt.Tube(new[] { new Vector3(0, 0, end - 0.01f), new Vector3(0, 0, end + 0.016f) }, new[] { 0.034f, 0.033f }, Vector3.up, 18, false, false, new Vector2(1f, 0.86f));
            Part("ShirtCuff", _forearm, shirt, material("shirtCuff"), 0.0016f, stain: true, sleeve: true);
            var link = new MeshBuilder();
            link.Ellipsoid(new Vector3(0.033f * 0.98f * -_side, 0, end + 0.008f), new Vector3(0.0022f, 0.0045f, 0.0045f), Quaternion.identity, 10, 6);
            Part("Cufflink", _forearm, link, material("gold"), 0.0005f);
        }

        _elbow = new GameObject("Elbow").transform;
        _elbow.SetParent(_arm, false);
        var elbow = new MeshBuilder();
        var er = bare ? 0.042f * bulk : 0.05f * bulk;
        elbow.Ellipsoid(Vector3.zero, new Vector3(er, er * 0.96f, er), Quaternion.identity, 14, 8);
        Part("ElbowSleeve", _elbow, elbow, material("sleeve"), bare && !lifelike ? 0.003f : 0.0026f, stain: true, sleeve: true);

        _upperArm = new GameObject("UpperArm").transform;
        _upperArm.SetParent(_arm, false);
        var upper = new MeshBuilder();
        // flared at the top, so it grows out of the shoulder rather than starting at a ball
        upper.Tube(new[] { Vector3.zero, new Vector3(0, 0, 0.05f), new Vector3(0, 0, UpperArmLength) },
            bare ? new[] { 0.056f * bulk, 0.049f * bulk, 0.042f * bulk } : new[] { 0.066f * bulk, 0.058f * bulk, 0.050f * bulk }, Vector3.up, 16, true, false, new Vector2(1f, 0.9f));
        Part("UpperSleeve", _upperArm, upper, material("sleeve"), bare && !lifelike ? 0.003f : 0.0026f, stain: true, sleeve: true);

        // the deltoid: the top of the shoulder goes up with the arm (the torso's own shoulder stays), so a raised arm
        // looks like a shoulder moving, not a doll's arm turning in a socket
        var deltoid = new MeshBuilder();
        var dr = bare ? 0.058f * bulk : 0.068f * bulk;
        deltoid.Ellipsoid(new Vector3(0, 0.008f, 0.03f), new Vector3(dr, dr * 0.92f, dr * 1.2f), Quaternion.identity, 16, 10);
        Part("Deltoid", _upperArm, deltoid, material("sleeve"), bare && !lifelike ? 0.003f : 0.0026f, stain: true, sleeve: true);

        // The watch on the left wrist, its hands following your real clock (Watches.cs).
        _watchFrom = _renderers.Count;
        if (left) BuildWatch(watch, material);
    }

    private readonly int _watchFrom;
    private bool _drawn = true;

    /// <summary>The hand and arm drawn, or not (a style worn as one of the game's models has its own); the watch stays.</summary>
    public void SetDrawn(bool on)
    {
        if (_drawn == on) return;
        _drawn = on;
        for (var i = 0; i < _watchFrom && i < _renderers.Count; i++)
            if (_renderers[i] != null)
                _renderers[i].enabled = on;
    }

    /// <summary>How far the fingers are curled now (0 open, 1 a fist): the index, the other three, the thumb.</summary>
    public float IndexCurl => _indexCurl;

    public float GripCurl => _gripCurl;
    public float ThumbCurl => _thumbCurl;

    /// <summary>
    /// Toon: a white cartoon glove, rubber-hose style like the studio's own characters wear. A puffy hand with the
    /// three stitch lines down the back, three fat rounded fingers and a thumb (the fourth tucked away, as the cartoons
    /// draw it), a ball at every joint so the fingers bend like hoses, and a flared cuff rolled at the wrist. Thick ink.
    /// </summary>
    private void BuildToon(Func<string, Material> material)
    {
        // three fingers spread evenly across the hand, the middle one longest
        float[] x = { -0.025f, 0.0f, 0.025f };
        for (var f = 0; f < 3; f++)
        {
            var root = _fingers[f].Joints[0];
            root.localPosition = new Vector3(x[f] * _side, 0.005f, FingerZ[1] - 0.002f);
            _fingers[f].Spread = (f - 1) * 7f * _side;
            root.localRotation = Quaternion.Euler(0, _fingers[f].Spread, 0);
        }

        // a smooth, puffy profile in many rings, so the game's cel shading curves round it instead of stepping at a ring
        const int rings = 12;
        var centres = new Vector3[rings];
        var radii = new float[rings];
        for (var i = 0; i < rings; i++)
        {
            var t = i / (float)(rings - 1);
            centres[i] = new Vector3(0, 0.004f * t, Mathf.Lerp(-0.004f, 0.088f, t));
            radii[i] = t < 0.62f ? 0.032f + 0.017f * Mathf.Sin(t / 0.62f * Mathf.PI / 2f) : 0.049f - 0.005f * Mathf.Pow((t - 0.62f) / 0.38f, 2f);
        }

        var palm = new MeshBuilder();
        palm.Tube(centres, radii, Vector3.up, 22, true, true, new Vector2(1f, 0.58f));
        Part("Palm", _wrist, palm, material("glovePalm"), 0.0034f, stain: true);

        var cuff = new MeshBuilder();
        cuff.Tube(new[] { new Vector3(0, 0, 0.006f), new Vector3(0, 0, -0.018f), new Vector3(0, 0, -0.042f) }, new[] { 0.034f, 0.044f, 0.057f },
            Vector3.up, 22, false, false, new Vector2(1f, 0.82f));
        cuff.Torus(new Vector3(0, 0, -0.043f), Vector3.forward, 0.052f, 0.0075f, 26, 8);
        Part("Cuff", _wrist, cuff, material("gloveCuff"), 0.0032f, stain: true);

        for (var f = 0; f < 3; f++)
        {
            var r = FingerRadius[1] * 1.45f;
            for (var j = 0; j < 3; j++)
            {
                var len = FingerLengths[f == 1 ? 1 : f == 0 ? 0 : 2][j] * (f == 1 ? 1.06f : 1f);
                var seg = new MeshBuilder();
                seg.Tube(new[] { Vector3.zero, new Vector3(0, 0, len) }, new[] { r, r * 0.98f }, Vector3.up, 16, false, false);
                seg.Ellipsoid(Vector3.zero, new Vector3(r, r, r), Quaternion.identity, 16, 9);
                if (j == 2) seg.Ellipsoid(new Vector3(0, 0, len), new Vector3(r * 1.06f, r, r * 1.2f), Quaternion.identity, 16, 9);
                Part($"Seg{f}_{j}", _fingers[f].Joints[j], seg, material("glove"), 0.0028f, stain: true);
            }
        }

        for (var j = 0; j < 3; j++)
        {
            var r = ThumbRadius[j] * 1.32f;
            var seg = new MeshBuilder();
            seg.Tube(new[] { Vector3.zero, new Vector3(0, 0, ThumbLength[j]) }, new[] { r, r * 0.96f }, Vector3.up, 16, false, false);
            seg.Ellipsoid(Vector3.zero, new Vector3(r, r, r), Quaternion.identity, 16, 9);
            if (j == 2) seg.Ellipsoid(new Vector3(0, 0, ThumbLength[j]), new Vector3(r * 1.05f, r, r * 1.2f), Quaternion.identity, 16, 9);
            Part($"ThumbSeg_{j}", _thumb.Joints[j], seg, material("glove"), 0.0028f, stain: true);
        }
    }

    /// <summary>
    /// A lifelike hand: one continuous skinned mesh, so the knuckles bend smoothly instead of hinging like a
    /// puppet. A shaped palm (knuckle ridge across the back, the fleshy pads at the heel and the base of the
    /// thumb), tapering fingers with real proportions, rounded fingertips and fingernails, natural skin
    /// tones, and a thin ink line so it still belongs in the game's drawn world.
    /// </summary>
    private void BuildLifelike(Func<string, Material> material, Vector3 thumbPos, Vector3 thumbDir, Vector3 thumbUp)
    {
        // Bones: 0 the wrist, 1..12 the fingers (finger × 3 + joint + 1), 13..15 the thumb.
        var bones = new Transform[16];
        bones[0] = _wrist;
        for (var f = 0; f < 4; f++)
        for (var j = 0; j < 3; j++)
            bones[1 + f * 3 + j] = _fingers[f].Joints[j];
        for (var j = 0; j < 3; j++) bones[13 + j] = _thumb.Joints[j];

        var sb = new SkinBuilder();
        var up = Vector3.up;

        // Palm: rounded-box sections from the wrist to the knuckles, the back domed, the palm side fuller at the heel.
        float[] z = { -0.01f, 0.006f, 0.03f, 0.055f, 0.075f, 0.09f };
        float[] halfWidth = { 0.024f, 0.029f, 0.036f, 0.041f, 0.043f, 0.042f };
        float[] back = { 0.0105f, 0.011f, 0.0118f, 0.0112f, 0.0106f, 0.0096f };
        float[] front = { 0.0125f, 0.0152f, 0.0165f, 0.0148f, 0.0128f, 0.0105f };
        float[] pads = { 0.55f, 1f, 1f, 0.65f, 0.25f, 0f }; // how much the thumb's pad and the heel swell the palm side
        float[] ridge = { 0f, 0f, 0f, 0.2f, 0.65f, 1f };  // the knuckles rising across the back, towards the fingers
        const int sides = 28;
        var pts = new Vector3[z.Length, sides + 1];
        var centers = new Vector3[z.Length];
        for (var k = 0; k < z.Length; k++)
        {
            var yc = 0.002f + 0.0003f * k;
            centers[k] = new Vector3(0, yc, z[k]);
            for (var i = 0; i <= sides; i++)
            {
                var a = (float)(i % sides) / sides * Mathf.PI * 2;
                var c = Mathf.Cos(a);
                var s = Mathf.Sin(a);
                var px = halfWidth[k] * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 0.9f);
                var py = (s >= 0 ? back[k] : front[k]) * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 0.9f);
                if (s >= 0 && ridge[k] > 0)
                {
                    // Knuckles: a smooth bump on the back where each finger joins the hand.
                    var bump = 0f;
                    for (var f = 0; f < 4; f++)
                    {
                        var dx = (px - FingerX[f] * _side) / 0.0085f;
                        bump += Mathf.Exp(-dx * dx);
                    }

                    py *= 1f + 0.22f * ridge[k] * Mathf.Min(1f, bump) * Mathf.Pow(s, 4);
                }

                if (s < 0)
                {
                    // The palm side is fuller under the thumb (thenar) and the little finger (heel), smoothly.
                    var across = px * _side / halfWidth[k]; // -1 thumb side .. +1 little-finger side
                    var swell = 0.32f * Mathf.SmoothStep(0, 1, Mathf.Clamp01(-across * 1.4f)) + 0.2f * Mathf.SmoothStep(0, 1, Mathf.Clamp01(across * 1.4f));
                    py *= 1f + swell * pads[k];
                }
                pts[k, i] = new Vector3(px, yc + py, z[k]);
            }
        }

        var palm = new MeshBuilder();
        palm.Loft(pts, centers, true, true);
        sb.Add(palm, "lifePalm", 0.0009f, _ => SkinWeight.Rigid(0));

        for (var f = 0; f < 4; f++)
        {
            var b0 = 1 + f * 3;
            var mcp = _wrist.InverseTransformPoint(_fingers[f].Joints[0].position);
            var dir = _wrist.InverseTransformDirection(_fingers[f].Joints[0].forward).normalized;
            var l1 = FingerLengths[f][0];
            var l2 = FingerLengths[f][1];
            var l3 = FingerLengths[f][2];
            var r = FingerRadius[f];

            // The finger: one tube through all three joints, slimming towards a rounded tip.
            float[] ts = { -0.014f, 0f, l1 * 0.45f, l1, l1 + l2 * 0.5f, l1 + l2, l1 + l2 + l3 * 0.5f, l1 + l2 + l3 * 0.88f };
            float[] rs = { 1.06f, 1.0f, 0.93f, 0.95f, 0.87f, 0.86f, 0.82f, 0.78f };
            var cs = new Vector3[ts.Length];
            var radii = new float[ts.Length];
            for (var k = 0; k < ts.Length; k++)
            {
                cs[k] = mcp + dir * ts[k];
                radii[k] = r * rs[k];
            }

            var finger = new MeshBuilder();
            finger.Tube(cs, radii, up, 14, false, true, new Vector2(1.08f, 0.92f));
            sb.Add(finger, "lifeSkin", 0.0007f, v =>
            {
                var t = Vector3.Dot(v - mcp, dir);
                if (t < l1 * 0.5f) return SkinWeight.Blend(0, b0, (t + 0.004f) / 0.01f);
                if (t < l1 + l2 * 0.5f) return SkinWeight.Blend(b0, b0 + 1, (t - (l1 - 0.005f)) / 0.01f);
                return SkinWeight.Blend(b0 + 1, b0 + 2, (t - (l1 + l2 - 0.004f)) / 0.008f);
            });

            var nailUp = Vector3.ProjectOnPlane(up, dir).normalized;
            var tipR = r * 0.8f;
            var nail = new MeshBuilder();
            nail.Ellipsoid(mcp + dir * (l1 + l2 + l3 * 0.56f) + nailUp * (tipR * 0.84f), new Vector3(tipR * 0.78f, tipR * 0.16f, l3 * 0.42f), Quaternion.LookRotation(dir, nailUp), 14, 6);
            sb.Add(nail, "lifeNail", 0.0003f, _ => SkinWeight.Rigid(b0 + 2));
        }

        // The thumb, rooted in its pad.
        {
            var l0 = ThumbLength[0];
            var l1 = ThumbLength[1];
            var l2 = ThumbLength[2];
            float[] ts = { -0.008f, 0.008f, 0.024f, l0, l0 + l1 * 0.5f, l0 + l1, l0 + l1 + l2 * 0.5f, l0 + l1 + l2 * 0.88f };
            float[] rs = { 0.0155f, 0.0146f, 0.0133f, 0.0119f, 0.0109f, 0.0106f, 0.0102f, 0.0094f };
            var cs = new Vector3[ts.Length];
            for (var k = 0; k < ts.Length; k++) cs[k] = thumbPos + thumbDir * ts[k];
            var thumb = new MeshBuilder();
            thumb.Tube(cs, rs, thumbUp, 14, false, true, new Vector2(1.08f, 0.94f));
            sb.Add(thumb, "lifeSkin", 0.0007f, v =>
            {
                var t = Vector3.Dot(v - thumbPos, thumbDir);
                if (t < l0 * 0.5f) return SkinWeight.Blend(0, 13, (t + 0.005f) / 0.02f);
                if (t < l0 + l1 * 0.5f) return SkinWeight.Blend(13, 14, (t - (l0 - 0.006f)) / 0.012f);
                return SkinWeight.Blend(14, 15, (t - (l0 + l1 - 0.005f)) / 0.01f);
            });

            var nailUp = Vector3.ProjectOnPlane(thumbUp, thumbDir).normalized;
            var nail = new MeshBuilder();
            nail.Ellipsoid(thumbPos + thumbDir * (l0 + l1 + l2 * 0.55f) + nailUp * (0.0094f * 0.86f), new Vector3(0.0094f * 0.8f, 0.0094f * 0.16f, l2 * 0.4f), Quaternion.LookRotation(thumbDir, nailUp), 14, 6);
            sb.Add(nail, "lifeNail", 0.0003f, _ => SkinWeight.Rigid(15));
        }

        var name = IsLeft ? "LifelikeHand_L" : "LifelikeHand_R";
        var built = sb.BuildSeparate(_wrist, bones, _wrist, material, name, out var ink, out var bind);
        foreach (var piece in built)
        {
            _renderers.Add(piece.Renderer);
            _casters.Add(piece.Renderer);
            piece.Renderer.shadowCastingMode = ModConfig.PlayerShadows.Value ? ShadowCastingMode.On : ShadowCastingMode.Off;
            var part = InkStains.RegisterSkinned(piece.Renderer, piece.Geometry, piece.Weights, bones, bind, InkRegion.Hand, piece.Bands);
            if (part != null) Stains.Add(part);
        }

        if (ink != null) _renderers.Add(ink);
    }

    private Transform WatchHand(Transform face, float length, float width, Material m, float y = 0.0066f)
    {
        var pivot = new GameObject("WatchHand").transform;
        pivot.SetParent(face, false);
        pivot.localPosition = new Vector3(0, y, 0);
        var mb = new MeshBuilder();
        mb.Box(new Vector3(0, 0, length * 0.42f), new Vector3(width, 0.0004f, length * 1.16f), Quaternion.identity);
        mb.Box(new Vector3(0, 0, length * 0.92f), new Vector3(width * 0.55f, 0.0004f, length * 0.2f), Quaternion.identity);
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
        r.shadowCastingMode = ModConfig.PlayerShadows.Value ? ShadowCastingMode.On : ShadowCastingMode.Off; // your hands' shadow
        _renderers.Add(r);
        _casters.Add(r);
        if (stain)
        {
            var part = InkStains.Register(go.transform, r, material, mb, sleeve, sleeve || go.name.Contains("Forearm") ? InkRegion.Arm : InkRegion.Hand);
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

    /// <summary>YOUR SHADOW on or off, live.</summary>
    public void ApplyShadows(bool on)
    {
        var mode = on ? ShadowCastingMode.On : ShadowCastingMode.Off;
        foreach (var r in _casters)
            if (r != null && r.shadowCastingMode != mode)
                r.shadowCastingMode = mode;
    }

    public Vector3 WristPosition => _wrist.position;

    private Transform[] _jointList;

    /// <summary>
    /// The finger joints as they're posed now: index, middle, ring and pinky, then the thumb, three each from the
    /// base (+Z along each segment, +Y out of its back). A worn model's fingers copy them, so they open and close as
    /// Henry's always have.
    /// </summary>
    public Transform[] FingerJoints
    {
        get
        {
            if (_jointList != null) return _jointList;
            var list = new Transform[15];
            for (var f = 0; f < 4; f++)
            for (var j = 0; j < 3; j++)
                list[f * 3 + j] = _fingers[f].Joints[j];
            for (var j = 0; j < 3; j++) list[12 + j] = _thumb.Joints[j];
            return _jointList = list;
        }
    }

    /// <summary>The hand's frame: +Z along the fingers, +Y out of the back of the hand.</summary>
    public Transform Wrist => _wrist;

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

        _watchTick?.Invoke(dt);
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
