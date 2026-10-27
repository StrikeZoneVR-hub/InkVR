using InkVR.Body;
using InkVR.Rendering;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The cellar under the stage, down the stairs from backstage: brick and concrete, pipes along the joists, ink on the
/// floor, a little shrine someone keeps, and a working replica of the Ink Machine. Pull its lever and it starts: the
/// flywheel turns, the pistons pump, the gauges climb, the porthole slops with ink and the whole thing thumps.
/// </summary>
public sealed partial class Theatre
{
    private const float CellarBackZ = 16.5f, CellarFrontZ = BackstageZ + 0.1f;

    public static Vector3 MachineAt => new(-4.6f, BasementFloor, 11.2f);
    public static Vector3 MachineLever => new(-2.5f, BasementFloor + 1.05f, 12.6f);
    public static Vector3 MachineWheel => MachineAt + new Vector3(0, 1.45f, -1.18f);

    private void BuildBasement()
    {
        float y0 = BasementFloor, y1 = BasementTop;
        var con = new Color(0.62f, 0.6f, 0.58f);
        // the floor, the brick walls, the joists and boards of the stage above
        FloorRect(_concrete, -Half, -CellarHalf - 0.06f, CellarFrontZ, CellarBackZ, y0, con, 3f);
        FloorRect(_concrete, CellarHalf + 0.06f, Half, CellarFrontZ, CellarBackZ, y0, con, 3f);
        FloorRect(_concrete, -CellarHalf - 0.06f, CellarHalf + 0.06f, CellarFrontZ, CellarTopZ - 0.06f, y0, con, 3f);
        FloorRect(_concrete, -CellarHalf - 0.06f, CellarHalf + 0.06f, CellarBottomZ, CellarBackZ, y0, con, 3f);
        WallX(_brick, -Half, CellarFrontZ, CellarBackZ, y0, y1, Vector3.right, Brickwork * 0.85f, null);
        WallX(_brick, Half, CellarFrontZ, CellarBackZ, y0, y1, Vector3.left, Brickwork * 0.85f, null);
        WallZ(_brick, CellarFrontZ, -Half, Half, y0, y1, Vector3.forward, Brickwork * 0.85f, null);
        WallZ(_brick, CellarBackZ, -Half, Half, y0, y1, Vector3.back, Brickwork * 0.85f, null);
        CeilingRect(_wood, -Half, -CellarHalf - 0.06f, CellarFrontZ, CellarBackZ, y1, WoodDark * 0.55f);
        CeilingRect(_wood, CellarHalf + 0.06f, Half, CellarFrontZ, CellarBackZ, y1, WoodDark * 0.55f);
        CeilingRect(_wood, -CellarHalf - 0.06f, CellarHalf + 0.06f, CellarFrontZ, CellarTopZ - 0.06f, y1, WoodDark * 0.55f);
        CeilingRect(_wood, -CellarHalf - 0.06f, CellarHalf + 0.06f, BackstageBackZ, CellarBackZ, y1, WoodDark * 0.55f);
        for (var z = CellarFrontZ + 0.6f; z < CellarBackZ - 0.2f; z += 1.2f)
            foreach (var (xa, xb) in new[] { (-Half, -CellarHalf - 0.06f), (CellarHalf + 0.06f, Half) })
                Box(_wood, new Vector3((xa + xb) * 0.5f, y1 - 0.12f, z), new Vector3(xb - xa, 0.24f, 0.16f), WoodDark * 0.6f, 1f, true);

        // the stairwell's outside walls, closing off the space under the steps
        foreach (var s in new[] { -1f, 1f })
            WallX(_brick, s * (CellarHalf + 0.06f), CellarTopZ - 0.06f, CellarBottomZ, y0, y1, new Vector3(s, 0, 0), Brickwork * 0.8f, null);
        WallZ(_brick, CellarTopZ - 0.06f, -CellarHalf - 0.06f, CellarHalf + 0.06f, y0, y1, Vector3.back, Brickwork * 0.8f, null);

        // pipes along the ceiling and down the walls
        var pipe = new Color(0.36f, 0.33f, 0.3f);
        void Pipe(params Vector3[] pts)
        {
            var r = new float[pts.Length];
            for (var i = 0; i < r.Length; i++) r[i] = 0.07f;
            var mb = new MeshBuilder();
            mb.Tube(pts, r, Vector3.up, 12, true, true);
            AddBuilder(_plate, mb, pipe);
        }

        Pipe(new Vector3(-Half + 0.3f, y1 - 0.4f, CellarFrontZ + 0.3f), new Vector3(-Half + 0.3f, y1 - 0.4f, 15.8f), new Vector3(Half - 0.3f, y1 - 0.4f, 15.8f), new Vector3(Half - 0.3f, y0 + 0.5f, 15.8f));
        Pipe(MachineAt + new Vector3(0, 3.0f, 0), new Vector3(MachineAt.x, y1 - 0.25f, MachineAt.z), new Vector3(-Half + 0.6f, y1 - 0.25f, MachineAt.z));
        Pipe(new Vector3(MachineAt.x + 1.1f, y0 + 0.6f, MachineAt.z), new Vector3(2.5f, y0 + 0.6f, MachineAt.z), new Vector3(2.5f, y1 - 0.6f, MachineAt.z), new Vector3(7.5f, y1 - 0.6f, MachineAt.z));

        // ink on the floor, spreading from the machine
        var black = new Color(0.035f, 0.025f, 0.02f, 0.92f);
        foreach (var (x, z, r) in new[] { (MachineAt.x + 1.0f, MachineAt.z + 0.4f, 1.3f), (MachineAt.x - 0.6f, MachineAt.z - 1.4f, 0.9f), (3.5f, 9.0f, 0.8f), (6.6f, 14.8f, 1.0f) })
            Patch(_ink, new Vector3(x - r, y0 + 0.006f, z - r), new Vector3(x + r, y0 + 0.006f, z - r), new Vector3(x + r, y0 + 0.006f, z + r), new Vector3(x - r, y0 + 0.006f, z + r), Vector3.up, black, 2, 2, unlit: true);

        BuildInkMachine();
        BuildShrine();
        BuildCellarClutter();
        PlanCellarLamps();
    }

    private void BuildInkMachine()
    {
        var m = MachineAt;
        var iron = new Color(0.42f, 0.4f, 0.38f);
        var brass = new Color(0.78f, 0.58f, 0.28f);
        // the plinth and the great tank, banded and riveted, with its domed top and stack
        Box(_plate, m + new Vector3(0, 0.15f, 0), new Vector3(2.6f, 0.3f, 2.6f), iron * 0.8f);
        Lathe(_plate, m + Vector3.up * 0.3f, new[]
        {
            new Vector2(1.0f, 0f), new Vector2(1.02f, 0.08f), new Vector2(0.96f, 0.14f), new Vector2(0.96f, 1.9f), new Vector2(1.0f, 1.96f), new Vector2(1.0f, 2.04f),
            new Vector2(0.94f, 2.1f), new Vector2(0.8f, 2.35f), new Vector2(0.55f, 2.52f), new Vector2(0.2f, 2.6f), new Vector2(0f, 2.62f)
        }, iron, 32, false, 6f);
        foreach (var h in new[] { 0.7f, 1.3f }) Ring(_plate, m + Vector3.up * (0.3f + h), 0.97f, 0.035f, brass, 40, 6);
        Lathe(_plate, m + Vector3.up * 2.85f, new[] { new Vector2(0.24f, 0f), new Vector2(0.22f, 0.12f), new Vector2(0.2f, 0.12f), new Vector2(0.2f, 0.6f) }, iron * 0.9f, 16);

        // the porthole on the front, showing the ink inside (it slops when the machine runs)
        var port = m + new Vector3(0.97f, 1.25f, 0.35f);
        var portRing = new MeshBuilder();
        portRing.Torus(port, Vector3.right, 0.24f, 0.045f, 28, 8);
        AddBuilder(_plate, portRing, brass);
        InGroup("MachineInk", port, () =>
        {
            var glass = new MeshBuilder();
            glass.Disc(port + Vector3.right * 0.01f, new Vector3(0, 0, -0.22f), new Vector3(0, 0.22f, 0), 24);
            AddBuilder(_painted, glass, Dark * 1.6f);
        });

        // the pump: two cylinders on the front with their pistons (TheatreLife pumps them)
        for (var i = 0; i < 2; i++)
        {
            var c = m + new Vector3(1.15f, 0.3f, -0.55f + i * 0.4f);
            Lathe(_plate, c, new[] { new Vector2(0.15f, 0f), new Vector2(0.15f, 0.55f), new Vector2(0.12f, 0.6f), new Vector2(0.05f, 0.6f) }, iron * 1.1f, 16);
            InGroup("Piston" + i, c + Vector3.up * 0.6f, () =>
            {
                Lathe(_plate, c + Vector3.up * 0.6f, new[] { new Vector2(0.035f, 0f), new Vector2(0.035f, 0.4f), new Vector2(0.09f, 0.42f), new Vector2(0.09f, 0.5f), new Vector2(0f, 0.51f) }, brass, 12);
            });
        }

        // the flywheel on the side (it spins), on an axle out of the tank
        var w = MachineWheel;
        var axle = new MeshBuilder();
        axle.Tube(new[] { w + Vector3.forward * 0.28f, w - Vector3.forward * 0.08f }, new[] { 0.06f, 0.06f }, Vector3.up, 12, false, true);
        AddBuilder(_plate, axle, iron);
        InGroup("Flywheel", w, () =>
        {
            var wheel = new MeshBuilder();
            wheel.Torus(w, Vector3.forward, 0.62f, 0.06f, 40, 8);
            for (var k = 0; k < 6; k++)
            {
                var a = k / 6f * Mathf.PI * 2;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                wheel.Tube(new[] { w + d * 0.08f, w + d * 0.58f }, new[] { 0.035f, 0.025f }, Vector3.forward, 8, false, false);
            }

            Drum(w - Vector3.forward * 0.06f, w + Vector3.forward * 0.06f, 0.11f, 14, wheel);
            AddBuilder(_plate, wheel, iron * 0.9f);
        });

        // three gauges across the front, their needles swing up as it runs
        for (var i = 0; i < 3; i++)
        {
            var g = m + new Vector3(0.99f, 1.9f, -0.55f + i * 0.55f);
            var bezel = new MeshBuilder();
            Drum(g, g + Vector3.right * 0.07f, 0.16f, 24, bezel);
            AddBuilder(_plate, bezel, brass);
            var dial = NewPart("Gauge" + i, StyleTextures.Paint("dialGauge"), false, 2001);
            var face = new MeshBuilder();
            face.Disc(g + Vector3.right * 0.072f, new Vector3(0, 0, -0.14f), new Vector3(0, 0.14f, 0), 24);
            AddBuilder(dial, face, new Color(1.15f, 1.15f, 1.1f));
            var pivot = g + Vector3.right * 0.085f;
            InGroup("Needle" + i, pivot, () => Box(_box, pivot + Vector3.up * 0.05f, new Vector3(0.01f, 0.11f, 0.014f), Dark));
        }

        // the controls: a stand, a lever with a black knob, a brass plate
        var lever = MachineLever;
        Box(_plate, lever + new Vector3(0, -0.53f, 0), new Vector3(0.36f, 1.06f, 0.36f), iron * 0.9f);
        Box(_plate, lever + new Vector3(0, -0.01f, 0), new Vector3(0.4f, 0.04f, 0.4f), brass);
        InGroup("MachineLever", lever, () =>
        {
            var arm = new MeshBuilder();
            arm.Tube(new[] { lever, lever + new Vector3(0, 0.45f, 0) }, new[] { 0.025f, 0.022f }, Vector3.forward, 10, true, false);
            arm.Ellipsoid(lever + new Vector3(0, 0.5f, 0), new Vector3(0.06f, 0.06f, 0.06f), Quaternion.identity, 12, 8);
            AddBuilder(_plate, arm, Dark * 2f);
        });
        var rotPlate = Quaternion.Euler(0, -90, 0);
        Signs.Add(new SignSpec { Text = "THE INK MACHINE", Pos = m + new Vector3(1.03f, 0.8f, 0.35f), Rot = rotPlate, Height = 0.09f, Width = 1.2f, Color = new Color(1f, 0.84f, 0.5f) });
        Signs.Add(new SignSpec { Text = "PULL", Pos = lever + new Vector3(0.21f, -0.15f, 0), Rot = rotPlate, Height = 0.06f, Width = 0.3f, Color = new Color(1f, 0.84f, 0.5f) });
        Box(_box, m + new Vector3(1.0f, 0.8f, 0.35f) + Vector3.left * 0.01f, new Vector3(0.02f, 0.16f, 1.3f), Dark);
    }

    private void BuildShrine()
    {
        // in the back corner: a crate for an altar, a Bendy cut-out, candles, a few cans, the words on the wall
        var f = BasementFloor;
        var c = new Vector3(7.4f, f, 15.6f);
        Box(_crate, c + Vector3.up * 0.4f, new Vector3(1.1f, 0.8f, 0.6f), new Color(0.7f, 0.55f, 0.35f));
        Standee(c + new Vector3(0, 0, 0.5f), "bendy_pose_14", 180f);
        var rng = new System.Random(8);
        for (var i = 0; i < 6; i++)
        {
            var at = c + new Vector3(-0.45f + i * 0.18f, 0.8f, -0.12f + (float)rng.NextDouble() * 0.15f);
            var h = 0.08f + (float)rng.NextDouble() * 0.12f;
            Lathe(_posterBg, at, new[] { new Vector2(0.025f, 0f), new Vector2(0.025f, h), new Vector2(0f, h + 0.005f) }, Cream, 8);
            Glow(at + Vector3.up * (h + 0.04f), 0.07f, new Color(1f, 0.85f, 0.5f, 1f), GlowGroup.Flicker);
            Glow(at + Vector3.up * (h + 0.04f), 0.4f, new Color(1f, 0.65f, 0.3f, 0.25f), GlowGroup.Flicker);
        }

        var ink = new Color(0.04f, 0.03f, 0.025f, 0f);
        Signs.Add(new SignSpec { Text = "HE WILL SET US FREE", Pos = new Vector3(Half - 0.02f, f + 2.2f, 13.6f), Rot = Quaternion.Euler(0, 90, 0), Height = 0.32f, Width = 4.4f, Color = ink, Id = "CellarWriting" });
    }

    private void BuildCellarClutter()
    {
        var f = BasementFloor;
        // barrels, crates, a coffin-shaped box, the old organ pipes, a big gear from somewhere much deeper
        foreach (var (x, z) in new[] { (-8.3f, 7.0f), (-7.6f, 7.0f), (-8.3f, 7.7f), (6.0f, 7.0f), (8.3f, 9.6f) })
        {
            Lathe(_plate, new Vector3(x, f, z), new[] { new Vector2(0.28f, 0f), new Vector2(0.31f, 0.1f), new Vector2(0.33f, 0.45f), new Vector2(0.31f, 0.8f), new Vector2(0.28f, 0.9f), new Vector2(0f, 0.9f) }, new Color(0.3f, 0.27f, 0.25f), 18);
            Glow(new Vector3(x, f + 0.905f, z), 0.4f, new Color(0.02f, 0.015f, 0.01f, 0.75f), GlowGroup.Steady);
        }

        Box(_crate, new Vector3(-8.1f, f + 0.45f, 14.6f), new Vector3(0.9f, 0.9f, 0.9f), new Color(0.75f, 0.58f, 0.36f));
        Box(_crate, new Vector3(-8.0f, f + 0.35f, 15.6f), new Vector3(0.7f, 0.7f, 0.7f), Quaternion.Euler(0, 15, 0), new Color(0.75f, 0.58f, 0.36f));
        Box(_wood, new Vector3(4.0f, f + 0.3f, 15.7f), new Vector3(2.0f, 0.6f, 0.7f), WoodDark * 0.9f);
        for (var i = 0; i < 5; i++)
        {
            var at = new Vector3(8.6f - i * 0.02f, f, 7.4f + i * 0.28f);
            Lathe(_plate, at, new[] { new Vector2(0.08f, 0f), new Vector2(0.08f, 1.4f + i * 0.25f), new Vector2(0.11f, 1.45f + i * 0.25f), new Vector2(0f, 1.46f + i * 0.25f) }, new Color(0.78f, 0.6f, 0.3f), 12);
        }

        // the gear, leaning on the wall (a fan find: from the Abyss)
        var gear = new Vector3(Half - 0.25f, f + 0.62f, 11.8f);
        InGroup("Gear", gear, () =>
        {
            var mb = new MeshBuilder();
            mb.Torus(gear, Vector3.right, 0.48f, 0.08f, 36, 8);
            for (var k = 0; k < 12; k++)
            {
                var a = k / 12f * Mathf.PI * 2;
                mb.Box(gear + new Vector3(0, Mathf.Sin(a), Mathf.Cos(a)) * 0.6f, new Vector3(0.12f, 0.14f, 0.12f), Quaternion.Euler(-a * Mathf.Rad2Deg, 0, 0));
            }

            for (var k = 0; k < 4; k++)
            {
                var a = k / 4f * Mathf.PI * 2;
                mb.Tube(new[] { gear, gear + new Vector3(0, Mathf.Sin(a), Mathf.Cos(a)) * 0.42f }, new[] { 0.04f, 0.04f }, Vector3.right, 8, false, false);
            }

            AddBuilder(_plate, mb, new Color(0.55f, 0.45f, 0.3f));
        });
    }

    private void PlanCellarLamps()
    {
        var f = BasementFloor;
        var bulb = new Color(1f, 0.82f, 0.55f);
        // a few bare bulbs on flex, the shrine's candles, the machine's own lamp
        foreach (var p in new[] { new Vector3(-1.8f, -0.9f, 9.0f), new Vector3(4.5f, -0.9f, 11.0f), new Vector3(-5.0f, -0.9f, 14.6f), new Vector3(2.2f, -0.9f, 15.4f) })
        {
            AddLamp(p, bulb * 1.1f, 2.6f);
            var flex = new MeshBuilder();
            flex.Tube(new[] { new Vector3(p.x, BasementTop, p.z), p + Vector3.up * 0.08f }, new[] { 0.008f, 0.008f }, Vector3.forward, 5, false, false);
            AddBuilder(_box, flex, Dark);
            Lathe(_posterBg, p - Vector3.up * 0.04f, new[] { new Vector2(0f, 0f), new Vector2(0.04f, 0.03f), new Vector2(0.045f, 0.08f), new Vector2(0.02f, 0.12f) }, new Color(1.4f, 1.3f, 1.05f), 10, true);
            Glow(p, 0.16f, new Color(1f, 0.9f, 0.7f, 1f), GlowGroup.Flicker);
            Glow(p, 1.0f, new Color(1f, 0.75f, 0.45f, 0.25f), GlowGroup.Flicker);
        }

        AddLamp(new Vector3(7.4f, f + 1.2f, 15.2f), new Color(1f, 0.6f, 0.3f) * 1.0f, 1.6f);
        AddLamp(MachineAt + new Vector3(1.6f, 2.4f, 0), new Color(1f, 0.85f, 0.6f) * 0.9f, 2.4f, Vector3.left + Vector3.down, 0.4f);
    }
}
