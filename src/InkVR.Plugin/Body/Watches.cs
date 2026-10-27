using System;
using InkVR.Rendering;
using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// The watch on your left wrist (WATCH in VR SETTINGS). Henry's tank watch, or one of five more: Bendy's character
/// watch (his gloved arms for hands, horns on the bezel), the studio's gold dress watch, an ink-machine gauge (its vial
/// fills as the studio's ink rises chapter by chapter), Alice's angel watch with a little halo floating over it, and the
/// Ink Demon's, dripping. Every one keeps your real time. On a cartoon glove it sits just above the cuff.
/// </summary>
public partial class HandModel
{
    /// <summary>How inky the studio is right now (0..1): the ink gauge watch's vial shows it.</summary>
    public static float StudioInk = 0.2f;

    private Action<float> _watchTick;
    private Transform _secondHand;

    private void BuildWatch(WatchKind kind, Func<string, Material> material)
    {
        if (kind == WatchKind.None) return;
        // On a bare wrist it sits at the wrist; on a cartoon glove, round the glove's cuff (strapped over it, cartoon style).
        var z = Lifelike ? 0f : 0.009f;
        var r = Lifelike ? 0.0296f : 0.0405f;
        var strap = kind switch
        {
            WatchKind.Bendy => "strapBlack",
            WatchKind.Studio => "goldLinks",
            WatchKind.Angel => "strapWhite",
            WatchKind.Demon => "strapInk",
            _ => "strap"
        };
        var band = new MeshBuilder();
        if (Lifelike) band.Tube(new[] { new Vector3(0, 0, -0.032f + z), new Vector3(0, 0, -0.009f + z) }, new[] { r, r }, Vector3.up, 20, false, false, new Vector2(1f, 0.84f));
        else band.Tube(new[] { new Vector3(0, 0, -0.022f), new Vector3(0, 0, -0.001f) }, new[] { 0.0457f, 0.0372f }, Vector3.up, 24, false, false, new Vector2(1f, 0.84f)); // follows the cuff's flare
        Part("WatchStrap", _wrist, band, material(strap), 0.0009f, stain: true);

        var face = new GameObject("WatchFace").transform;
        face.SetParent(_wrist, false);
        face.localPosition = new Vector3(0, r * 0.84f - 0.0003f, -0.0205f + z);
        face.localRotation = Quaternion.Euler(0, -90f, 0); // local +Z = 12 o'clock (the little-finger side), +X = 3 (the fingers)

        switch (kind)
        {
            case WatchKind.Tank:
                TankWatch(face, material);
                break;
            case WatchKind.Bendy:
                BendyWatch(face, material);
                break;
            case WatchKind.Studio:
                StudioWatch(face, material);
                break;
            case WatchKind.Gauge:
                GaugeWatch(face, material);
                break;
            case WatchKind.Angel:
                AngelWatch(face, material);
                break;
            case WatchKind.Demon:
                DemonWatch(face, material);
                break;
        }
    }

    /// <summary>Henry's: a 1930s tank watch, a gilt rectangular case, cream dial with an inked minute track and 12/3/6/9.</summary>
    private void TankWatch(Transform face, Func<string, Material> material)
    {
        var caseMb = new MeshBuilder();
        caseMb.Box(new Vector3(0, 0.003f, 0), new Vector3(0.024f, 0.006f, 0.032f), Quaternion.identity);
        foreach (var z in new[] { -1f, 1f })
        foreach (var x in new[] { -1f, 1f })
            caseMb.Box(new Vector3(0.0072f * x, 0.0022f, 0.0175f * z), new Vector3(0.0036f, 0.0032f, 0.0052f), Quaternion.identity);
        Part("WatchCase", face, caseMb, material("gold"), 0.0006f);
        Crown(face, 0.0118f, 0.003f, material("gold"));
        var dial = new MeshBuilder();
        dial.FlatQuad(new Vector3(0, 0.00615f, 0), new Vector3(0.0098f, 0, 0), new Vector3(0, 0, 0.0136f));
        Part("WatchDial", face, dial, material("dial"), 0);
        _hourHand = WatchHand(face, 0.0072f, 0.0022f, material("ink"));
        _minuteHand = WatchHand(face, 0.0112f, 0.0015f, material("ink"));
        Pin(face, 0.0072f, material("gold"));
    }

    // ---------------- round watches ----------------

    private void RoundCase(Transform face, float radius, float height, Material caseMat, Material bezelMat, string dialKey, Func<string, Material> material)
    {
        var body = new MeshBuilder();
        body.Tube(new[] { Vector3.zero, new Vector3(0, height, 0) }, new[] { radius, radius }, Vector3.forward, 28, true, false);
        foreach (var z in new[] { -1f, 1f })
        foreach (var x in new[] { -1f, 1f })
            body.Box(new Vector3(0.006f * x, height * 0.4f, (radius + 0.0018f) * z), new Vector3(0.0032f, height * 0.6f, 0.005f), Quaternion.identity); // lugs
        Part("WatchCase", face, body, caseMat, 0.0006f);
        var bezel = new MeshBuilder();
        bezel.Torus(new Vector3(0, height, 0), Vector3.up, radius - 0.0006f, 0.0013f, 32, 8);
        Part("WatchBezel", face, bezel, bezelMat, 0.0004f);
        var dial = new MeshBuilder();
        dial.Disc(new Vector3(0, height + 0.0001f, 0), new Vector3(radius - 0.0016f, 0, 0), new Vector3(0, 0, radius - 0.0016f), 36);
        Part("WatchDial", face, dial, material(dialKey), 0);
    }

    private void Crown(Transform face, float x, float y, Material m)
    {
        var crown = new MeshBuilder();
        crown.Tube(new[] { new Vector3(x, y, 0), new Vector3(x + 0.0034f, y, 0) }, new[] { 0.0021f, 0.0021f }, Vector3.up, 10, false, false);
        crown.FlatQuad(new Vector3(x + 0.0034f, y, 0), new Vector3(0, 0, 0.0021f), new Vector3(0, 0.0021f, 0));
        Part("WatchCrown", face, crown, m, 0.0004f);
    }

    private void Pin(Transform face, float y, Material m)
    {
        var pin = new MeshBuilder();
        pin.Ellipsoid(new Vector3(0, y, 0), new Vector3(0.0016f, 0.0006f, 0.0016f), Quaternion.identity, 10, 5);
        Part("WatchPin", face, pin, m, 0);
    }

    /// <summary>Bendy's character watch: glossy black, his horns on the bezel, his face on the dial, white-gloved hands.</summary>
    private void BendyWatch(Transform face, Func<string, Material> material)
    {
        const float h = 0.0055f, rad = 0.0168f;
        var black = material("watchBlack");
        RoundCase(face, rad, h, black, black, "dialBendy", material);
        foreach (var side in new[] { -1f, 1f })
        {
            var a = side * 38f * Mathf.Deg2Rad;
            var outDir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            var p0 = outDir * (rad - 0.002f) + Vector3.up * h;
            var horn = new MeshBuilder();
            horn.Tube(new[] { p0, p0 + outDir * 0.003f + Vector3.up * 0.003f, p0 + outDir * 0.0045f + Vector3.up * 0.0062f, p0 + outDir * 0.0035f + Vector3.up * 0.0085f },
                new[] { 0.0034f, 0.0024f, 0.0013f, 0.0003f }, Vector3.forward, 10, true, true);
            Part("WatchHorn", face, horn, black, 0.0004f);
        }

        Crown(face, rad, h * 0.5f, black);
        var glove = material("glove");
        _hourHand = WatchHand(face, 0.0068f, 0.0012f, material("ink"), h + 0.0005f);
        _minuteHand = WatchHand(face, 0.0106f, 0.001f, material("ink"), h + 0.0009f);
        foreach (var (hand, len) in new[] { (_hourHand, 0.0068f), (_minuteHand, 0.0106f) })
        {
            var ball = new MeshBuilder();
            ball.Ellipsoid(new Vector3(0, 0, len * 0.95f), new Vector3(0.0019f, 0.0009f, 0.0021f), Quaternion.identity, 12, 6);
            Part("WatchGlove", hand, ball, glove, 0.0003f);
        }

        Pin(face, h + 0.0012f, black);
    }

    /// <summary>The studio's gold dress watch: a fluted gold case on a gold bracelet, a pocket-watch bow at the crown.</summary>
    private void StudioWatch(Transform face, Func<string, Material> material)
    {
        const float h = 0.006f, rad = 0.0172f;
        var gold = material("gold");
        RoundCase(face, rad, h, gold, gold, "dialStudio", material);
        var flutes = new MeshBuilder();
        for (var i = 0; i < 36; i++)
        {
            var a = i / 36f * Mathf.PI * 2;
            flutes.Box(new Vector3(Mathf.Sin(a) * (rad + 0.0002f), h * 0.55f, Mathf.Cos(a) * (rad + 0.0002f)), new Vector3(0.0009f, h * 0.7f, 0.0009f), Quaternion.Euler(0, a * Mathf.Rad2Deg, 0));
        }

        Part("WatchFlutes", face, flutes, gold, 0);
        Crown(face, rad, h * 0.5f, gold);
        var bow = new MeshBuilder();
        bow.Torus(new Vector3(rad + 0.0058f, h * 0.5f, 0), Vector3.forward, 0.0026f, 0.0006f, 18, 6);
        Part("WatchBow", face, bow, gold, 0.0002f);
        _hourHand = WatchHand(face, 0.0068f, 0.0018f, material("ink"), h + 0.0005f);
        _minuteHand = WatchHand(face, 0.0108f, 0.0012f, material("ink"), h + 0.0009f);
        Pin(face, h + 0.0012f, gold);
    }

    /// <summary>
    /// A gauge off the ink machine: a heavy riveted brass case, a valve wheel for a crown, a seconds needle that ticks,
    /// and a glass vial on the side whose ink rises as the studio gets inkier.
    /// </summary>
    private void GaugeWatch(Transform face, Func<string, Material> material)
    {
        const float h = 0.0075f, rad = 0.0168f;
        var brass = material("brass");
        RoundCase(face, rad, h, brass, brass, "dialGauge", material);
        var rivets = new MeshBuilder();
        for (var i = 0; i < 8; i++)
        {
            var a = (i + 0.5f) / 8f * Mathf.PI * 2;
            rivets.Ellipsoid(new Vector3(Mathf.Sin(a) * (rad - 0.0006f), h + 0.0011f, Mathf.Cos(a) * (rad - 0.0006f)), new Vector3(0.0009f, 0.0006f, 0.0009f), Quaternion.identity, 8, 4);
        }

        Part("WatchRivets", face, rivets, brass, 0);
        // the valve wheel
        var wheel = new MeshBuilder();
        wheel.Tube(new[] { new Vector3(rad, h * 0.5f, 0), new Vector3(rad + 0.003f, h * 0.5f, 0) }, new[] { 0.0011f, 0.0011f }, Vector3.up, 8, false, false);
        wheel.Torus(new Vector3(rad + 0.0036f, h * 0.5f, 0), Vector3.right, 0.0033f, 0.0007f, 20, 6);
        wheel.Box(new Vector3(rad + 0.0036f, h * 0.5f, 0), new Vector3(0.0008f, 0.0066f, 0.0008f), Quaternion.identity);
        wheel.Box(new Vector3(rad + 0.0036f, h * 0.5f, 0), new Vector3(0.0008f, 0.0008f, 0.0066f), Quaternion.identity);
        Part("WatchValve", face, wheel, material("ink"), 0.0003f);
        // the vial on the 9 o'clock side: glass, and the ink inside it
        var vialAt = new Vector3(-rad - 0.003f, h * 0.45f, 0);
        var glass = new MeshBuilder();
        glass.Tube(new[] { vialAt + new Vector3(0, 0, -0.008f), vialAt + new Vector3(0, 0, 0.008f) }, new[] { 0.0026f, 0.0026f }, Vector3.up, 14, true, true);
        Part("WatchVial", face, glass, material("vialGlass"), 0.0004f);
        var inkPivot = new GameObject("WatchVialInk").transform;
        inkPivot.SetParent(face, false);
        inkPivot.localPosition = vialAt + new Vector3(0, 0, -0.0075f);
        var ink = new MeshBuilder();
        ink.Tube(new[] { Vector3.zero, new Vector3(0, 0, 0.015f) }, new[] { 0.0022f, 0.0022f }, Vector3.up, 12, true, true);
        Part("WatchVialInk", inkPivot, ink, material("ink"), 0);
        _hourHand = WatchHand(face, 0.0064f, 0.0019f, material("ink"), h + 0.0005f);
        _minuteHand = WatchHand(face, 0.0102f, 0.0013f, material("ink"), h + 0.0009f);
        _secondHand = WatchHand(face, 0.0118f, 0.0006f, brass, h + 0.0013f);
        Pin(face, h + 0.0016f, brass);
        var level = StudioInk;
        _watchTick = dt =>
        {
            // the vial fills (and sloshes a little as you move)
            level = Mathf.MoveTowards(level, StudioInk, dt * 0.2f);
            var slosh = 1f + Mathf.Sin(Time.unscaledTime * 7f) * 0.03f;
            inkPivot.localScale = new Vector3(1f, 1f, Mathf.Clamp(level * slosh, 0.08f, 1f));
            var now = DateTime.Now;
            _secondHand.localRotation = Quaternion.Euler(0, now.Second * 6f, 0); // it ticks
        };
    }

    /// <summary>Alice's angel watch: a gold case with little wings, hearts on the dial, a halo floating and turning over it.</summary>
    private void AngelWatch(Transform face, Func<string, Material> material)
    {
        const float h = 0.005f, rad = 0.0158f;
        var gold = material("gold");
        RoundCase(face, rad, h, gold, gold, "dialAngel", material);
        var white = material("glove");
        foreach (var side in new[] { -1f, 1f })
        {
            var wing = new MeshBuilder();
            for (var f = 0; f < 3; f++)
                wing.Ellipsoid(new Vector3(side * (rad + 0.003f + f * 0.0022f), h * 0.6f + f * 0.0012f, 0.002f - f * 0.0018f), new Vector3(0.0034f - f * 0.0006f, 0.0007f, 0.0016f),
                    Quaternion.Euler(0, 0, side * (18f + f * 10f)), 10, 5);
            Part("WatchWing", face, wing, white, 0.0003f);
        }

        Crown(face, rad, h * 0.5f, gold);
        var halo = new GameObject("WatchHalo").transform;
        halo.SetParent(face, false);
        var ring = new MeshBuilder();
        ring.Torus(Vector3.zero, Vector3.up, 0.0072f, 0.0011f, 28, 6);
        Part("WatchHaloRing", halo, ring, material("halo"), 0.0003f);
        _hourHand = WatchHand(face, 0.0062f, 0.0015f, material("ink"), h + 0.0005f);
        _minuteHand = WatchHand(face, 0.0098f, 0.001f, material("ink"), h + 0.0009f);
        Pin(face, h + 0.0012f, gold);
        _watchTick = _ =>
        {
            var t = Time.unscaledTime;
            halo.localPosition = new Vector3(0, h + 0.0085f + Mathf.Sin(t * 2.4f) * 0.0009f, 0);
            halo.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.3f) * 8f, t * 40f, 0);
        };
    }

    /// <summary>The Ink Demon's: a lumpy black case, his grin on a cracked black dial, bone-white hands, ink dripping off it.</summary>
    private void DemonWatch(Transform face, Func<string, Material> material)
    {
        const float h = 0.0058f, rad = 0.017f;
        var black = material("watchInk");
        RoundCase(face, rad, h, black, black, "dialDemon", material);
        var lumps = new MeshBuilder();
        var rng = new System.Random(9);
        for (var i = 0; i < 9; i++)
        {
            var a = (float)rng.NextDouble() * Mathf.PI * 2;
            var s = 0.0016f + (float)rng.NextDouble() * 0.0016f;
            lumps.Ellipsoid(new Vector3(Mathf.Sin(a) * rad, h * (0.4f + (float)rng.NextDouble() * 0.5f), Mathf.Cos(a) * rad), new Vector3(s, s * 0.8f, s), Quaternion.identity, 10, 6);
        }

        Part("WatchLumps", face, lumps, black, 0.0004f);
        // drips hanging off the 6 o'clock side, slowly stretching, letting go and starting again
        var drips = new Transform[3];
        var phase = new float[3];
        for (var i = 0; i < 3; i++)
        {
            var at = new Vector3((i - 1) * 0.0055f, h * 0.3f, -rad - 0.0008f);
            var pivot = new GameObject("WatchDrip").transform;
            pivot.SetParent(face, false);
            pivot.localPosition = at;
            var drip = new MeshBuilder();
            drip.Tube(new[] { Vector3.zero, new Vector3(0, -0.004f, -0.0006f), new Vector3(0, -0.0075f, -0.0004f) }, new[] { 0.0012f, 0.0009f, 0.0006f }, Vector3.forward, 8, false, false);
            drip.Ellipsoid(new Vector3(0, -0.0078f, -0.0004f), new Vector3(0.0012f, 0.0015f, 0.0012f), Quaternion.identity, 8, 6);
            Part("WatchDrip", pivot, drip, black, 0.0002f);
            drips[i] = pivot;
            phase[i] = i * 0.37f;
        }

        var bone = material("bone");
        _hourHand = WatchHand(face, 0.0066f, 0.0018f, bone, h + 0.0005f);
        _minuteHand = WatchHand(face, 0.0104f, 0.0012f, bone, h + 0.0009f);
        Pin(face, h + 0.0012f, black);
        _watchTick = dt =>
        {
            for (var i = 0; i < 3; i++)
            {
                phase[i] = (phase[i] + dt / (2.5f + i * 0.7f)) % 1f;
                var stretch = Mathf.Lerp(0.25f, 1.25f, Mathf.SmoothStep(0, 1, phase[i]));
                drips[i].localScale = new Vector3(1f, stretch, 1f);
            }
        };
    }
}
