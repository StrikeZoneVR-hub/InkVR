using System.Collections.Generic;
using InkVR.Body;
using InkVR.Rendering;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// Backstage, through the wings either side of the stage: the crossover behind the screen with its ghost light, the
/// fly gallery's ropes and sandbags, the prop store, the stairs down to the cellar, and Alice Angel's dressing room:
/// a vanity mirror ringed with bulbs, a full-length mirror (each shows you as you are, head and all), a wardrobe
/// with a costume for every style (take one and you're wearing it), and a case of watches to choose from.
/// </summary>
public sealed partial class Theatre
{
    private Part _brick, _plate, _checker, _tent, _crate, _concrete, _notes, _painted, _rope;

    private static readonly Color Brickwork = new(0.62f, 0.36f, 0.27f);
    private static readonly Color Blackout = new(0.07f, 0.06f, 0.06f);
    private static readonly Color Rose = new(0.92f, 0.74f, 0.72f);

    private void LoadRoomArt()
    {
        _brick = NewPart("Brick", TheatreArt.Brick());
        _plate = NewPart("Plate", TheatreArt.Plate());
        _checker = NewPart("Checker", TheatreArt.Checker());
        _tent = NewPart("Tent", TheatreArt.Tent());
        _crate = NewPart("Crate", TheatreArt.Crate());
        _concrete = NewPart("Concrete", TheatreArt.Concrete());
        _notes = NewPart("Notes", TheatreArt.Notes());
        _painted = NewPart("Painted", TheatreArt.Painted());
        _rope = NewPart("Rope", TheatreArt.RopeTwist());
    }

    // ---- what the toys need to know ----

    /// <summary>The dressing room door's hinge (it swings open as you come to it).</summary>
    public static Vector3 DressingDoorHinge => new(DressingDoorX - DressingDoorHalf, StageY, DressingZ);

    public static Vector3 GhostLight => new(0.6f, StageY, 7.3f);

    public const int WardrobeCount = 12;

    /// <summary>Where costume i hangs: its hook, on the rail along the dressing room's left wall.</summary>
    public static Vector3 CostumeHook(int i) => new(DressingX + 0.42f, StageY + 2.0f, 9.0f + i * 0.4f);

    public const int WatchCount = 7;

    /// <summary>Where watch i lies in the case on the dressing table (its face's centre).</summary>
    public static Vector3 WatchSpot(int i) => new(5.66f + (i % 4) * 0.13f + (i >= 4 ? 0.065f : 0f), StageY + 0.79f, 13.64f - (i >= 4 ? 0.13f : 0f));

    public readonly List<Mirror> Mirrors = new();

    // =====================================================================================================
    // The shell: floors, walls and the fly loft's ceiling
    // =====================================================================================================

    private void BuildBackstage()
    {
        var f = StageY;
        var up = Vector3.up;
        var boards = WoodDark * 1.05f;

        // the wings' floor, either side of the stage, and the boards behind the screen (round the stairwell and the
        // dressing room)
        foreach (var s in new[] { -1f, 1f })
            Patch(_wood, new Vector3(s * OpenHalf, f, ProsZ), new Vector3(s * Half, f, ProsZ), new Vector3(s * Half, f, ScreenZ + 0.4f), new Vector3(s * OpenHalf, f, ScreenZ + 0.4f), up, boards, Sub(Half - OpenHalf, 1.2f), 2, 2.2f, 2.2f);
        FloorRect(_wood, -Half, Half, ScreenZ + 0.4f, CrossoverZ, f, boards);
        FloorRect(_wood, -Half, -CellarHalf, CrossoverZ, BackstageBackZ, f, boards);
        FloorRect(_wood, CellarHalf, DressingX, CrossoverZ, BackstageBackZ, f, boards);
        FloorRect(_wood, -CellarHalf, CellarHalf, CrossoverZ, CellarTopZ, f, boards);

        // the brick walls (each face its own, so the hall's never shows through)
        var top = BackstageCeiling;
        WallZ(_brick, ProsZ + 0.06f, -Half, Half, f, top, Vector3.forward, Brickwork, new[] { new Vector4(-OpenHalf, f - 1, OpenHalf, OpenTop) });
        WallX(_brick, -Half, ProsZ, BackstageBackZ, f, top, Vector3.right, Brickwork, null);
        WallX(_brick, Half, ProsZ, DressingZ, f, top, Vector3.left, Brickwork, null);
        WallX(_brick, Half, DressingZ, BackstageBackZ, DressingCeiling, top, Vector3.left, Brickwork, null);
        WallZ(_brick, BackstageBackZ, -Half, DressingX, f, top, Vector3.back, Brickwork, null);
        WallZ(_brick, BackstageBackZ, DressingX, Half, DressingCeiling, top, Vector3.back, Brickwork, null);
        // the dressing room's outside: its front wall (with the door) and its side, to the loft
        WallZ(_brick, DressingZ - 0.06f, DressingX, Half, f, top, Vector3.back, Brickwork * 0.95f, new[] { new Vector4(DressingDoorX - DressingDoorHalf, f - 1, DressingDoorX + DressingDoorHalf, f + DoorHeight) });
        WallX(_brick, DressingX - 0.06f, DressingZ - 0.06f, BackstageBackZ, f, top, Vector3.left, Brickwork * 0.95f, null);
        Patch(_box, new Vector3(DressingX - 0.06f, DressingCeiling + 0.08f, BackstageBackZ), new Vector3(Half, DressingCeiling + 0.08f, BackstageBackZ), new Vector3(Half, DressingCeiling + 0.08f, DressingZ - 0.06f), new Vector3(DressingX - 0.06f, DressingCeiling + 0.08f, DressingZ - 0.06f), up, WoodDark * 0.6f, 4, 4);

        // the black masking behind the screen, and the border above it, seen from behind
        Patch(_curtain, new Vector3(OpenHalf, f, BackstageZ + 0.01f), new Vector3(-OpenHalf, f, BackstageZ + 0.01f), new Vector3(-OpenHalf, top, BackstageZ + 0.01f), new Vector3(OpenHalf, top, BackstageZ + 0.01f), Vector3.forward, Blackout, 6, 4, uvRect: new Vector4(0, 0, 6, 2));
        // and the gap between the proscenium and the masking over the stage: the loft's floor-to-grid black
        Patch(_curtain, new Vector3(-OpenHalf, OpenTop + 0.3f, ProsZ + 0.3f), new Vector3(OpenHalf, OpenTop + 0.3f, ProsZ + 0.3f), new Vector3(OpenHalf, top, ProsZ + 0.3f), new Vector3(-OpenHalf, top, ProsZ + 0.3f), Vector3.back, Blackout, 4, 2, uvRect: new Vector4(0, 0, 4, 1));

        // the loft's ceiling: the grid, its beams and pulley blocks
        Patch(_wood, new Vector3(-Half, top, BackstageBackZ), new Vector3(Half, top, BackstageBackZ), new Vector3(Half, top, ProsZ), new Vector3(-Half, top, ProsZ), Vector3.down, WoodDark * 0.45f, 8, 6, 2.2f, 2.2f);
        for (var z = ProsZ + 0.8f; z < BackstageBackZ - 0.3f; z += 1.6f)
        {
            Box(_wood, new Vector3(0, top - 0.15f, z), new Vector3(2 * Half, 0.24f, 0.18f), WoodDark * 0.7f, 1f, true);
            for (var x = -6f; x <= 6.01f; x += 3f) Box(_plate, new Vector3(x, top - 0.36f, z), new Vector3(0.14f, 0.22f, 0.22f), Iron * 1.5f);
        }

        // signs to find your way
        var dir = new Color(1f, 0.84f, 0.5f);
        Signs.Add(new SignSpec { Text = "STAGE", Pos = new Vector3(-7.5f, f + 2.6f, ProsZ + 0.1f), Rot = Quaternion.identity, Height = 0.18f, Width = 1.2f, Color = dir });
        Signs.Add(new SignSpec { Text = "STAGE", Pos = new Vector3(7.5f, f + 2.6f, ProsZ + 0.1f), Rot = Quaternion.identity, Height = 0.18f, Width = 1.2f, Color = dir });
        Box(_box, new Vector3(-7.5f, f + 2.6f, ProsZ + 0.08f), new Vector3(1.1f, 0.3f, 0.03f), Dark);
        Box(_box, new Vector3(7.5f, f + 2.6f, ProsZ + 0.08f), new Vector3(1.1f, 0.3f, 0.03f), Dark);

        BuildGhostLight();
        BuildFlyRail();
        BuildPropStore();
        BuildCallBoard();
        BuildStairwell();
        BuildDressingRoom();
        PlanBackstageLamps();
    }

    private void FloorRect(Part p, float x0, float x1, float z0, float z1, float y, Color tint, float tile = 2.2f)
    {
        if (x1 - x0 < 1e-3f || z1 - z0 < 1e-3f) return;
        Patch(p, new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), Vector3.up, tint, Sub(x1 - x0, 1.2f), Sub(z1 - z0, 1.2f), tile, tile);
    }

    private void CeilingRect(Part p, float x0, float x1, float z0, float z1, float y, Color tint, float tile = 2.2f)
    {
        if (x1 - x0 < 1e-3f || z1 - z0 < 1e-3f) return;
        Patch(p, new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), new Vector3(x0, y, z0), Vector3.down, tint, Sub(x1 - x0, 1.2f), Sub(z1 - z0, 1.2f), tile, tile);
    }

    /// <summary>A wall across z (in the x-y plane) from x0 to x1, facing n (±z), with holes (x0, y0, x1, y1).</summary>
    private void WallZ(Part p, float z, float x0, float x1, float y0, float y1, Vector3 n, Color tint, Vector4[] holes, float tileU = 1.4f, float tileV = 1.4f)
    {
        // seen from its front, u must run to the viewer's right: +x when facing -z, -x when facing +z
        if (n.z < 0) WallWithHoles(p, new Vector3(0, 0, z), Vector3.right, x0, x1, y0, y1, n, tint, holes ?? new Vector4[0], tileU, tileV);
        else
        {
            var flipped = new List<Vector4>();
            if (holes != null)
                foreach (var h in holes)
                    flipped.Add(new Vector4(-h.z, h.y, -h.x, h.w));
            WallWithHoles(p, new Vector3(0, 0, z), Vector3.left, -x1, -x0, y0, y1, n, tint, flipped.ToArray(), tileU, tileV);
        }
    }

    /// <summary>A wall along z (in the z-y plane) at x, from z0 to z1, facing n (±x), with holes (z0, y0, z1, y1).</summary>
    private void WallX(Part p, float x, float z0, float z1, float y0, float y1, Vector3 n, Color tint, Vector4[] holes, float tileU = 1.4f, float tileV = 1.4f)
    {
        if (n.x > 0) WallWithHoles(p, new Vector3(x, 0, 0), Vector3.back, -z1, -z0, y0, y1, n, tint, Flip(holes), tileU, tileV);
        else WallWithHoles(p, new Vector3(x, 0, 0), Vector3.forward, z0, z1, y0, y1, n, tint, holes ?? new Vector4[0], tileU, tileV);

        static Vector4[] Flip(Vector4[] hs)
        {
            if (hs == null) return new Vector4[0];
            var o = new Vector4[hs.Length];
            for (var i = 0; i < hs.Length; i++) o[i] = new Vector4(-hs[i].z, hs[i].y, -hs[i].x, hs[i].w);
            return o;
        }
    }

    // =====================================================================================================
    // The crossover: the ghost light, the fly rail, the call board
    // =====================================================================================================

    private void BuildGhostLight()
    {
        // a bare bulb on a stand, left burning on an empty stage (so no one falls in the dark, and for the ghosts)
        var g = GhostLight;
        Lathe(_plate, g, new[] { new Vector2(0.22f, 0f), new Vector2(0.2f, 0.05f), new Vector2(0.04f, 0.08f), new Vector2(0.025f, 0.1f), new Vector2(0.025f, 1.55f), new Vector2(0.04f, 1.6f), new Vector2(0f, 1.62f) }, Iron * 1.4f, 14);
        var bulb = g + up(1.72f);
        Lathe(_posterBg, bulb - up(0.08f), new[] { new Vector2(0f, 0f), new Vector2(0.045f, 0.03f), new Vector2(0.06f, 0.09f), new Vector2(0.045f, 0.15f), new Vector2(0f, 0.17f) }, new Color(1.4f, 1.3f, 1.05f), 14, true);
        // its wire cage
        for (var k = 0; k < 6; k++)
        {
            var a = k / 6f * Mathf.PI * 2;
            var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.085f;
            Box(_box, bulb + o, new Vector3(0.008f, 0.22f, 0.008f), Iron);
        }

        Glow(bulb, 0.2f, new Color(1f, 0.95f, 0.8f, 1f), GlowGroup.Flicker);
        Glow(bulb, 1.4f, new Color(1f, 0.85f, 0.6f, 0.28f), GlowGroup.Flicker);
        static Vector3 up(float h) => Vector3.up * h;
    }

    private void BuildFlyRail()
    {
        // along the left wall: the pin rail, ropes running up to the grid, sandbags hanging off some of them
        float f = StageY, x = -Half + 0.3f;
        Box(_wood, new Vector3(x, f + 1.0f, 9.6f), new Vector3(0.22f, 0.12f, 6.4f), WoodDark);
        Box(_wood, new Vector3(x - 0.05f, f + 0.5f, 6.6f), new Vector3(0.12f, 1.0f, 0.12f), WoodDark);
        Box(_wood, new Vector3(x - 0.05f, f + 0.5f, 12.6f), new Vector3(0.12f, 1.0f, 0.12f), WoodDark);
        var rng = new System.Random(11);
        for (var i = 0; i < 14; i++)
        {
            var z = 6.75f + i * 0.43f;
            Box(_plate, new Vector3(x, f + 1.08f, z), new Vector3(0.03f, 0.28f, 0.03f), Gold * 0.8f); // the belaying pin
            var rope = new MeshBuilder();
            rope.Tube(new[] { new Vector3(x + 0.04f, f + 1.15f, z), new Vector3(x + 0.08f, BackstageCeiling - 0.4f, z) }, new[] { 0.012f, 0.012f }, Vector3.forward, 6, false, false);
            AddBuilder(_rope, rope, new Color(0.8f, 0.7f, 0.52f));
            if (i % 3 != 1) continue;
            // a sandbag on this line, hanging free (it swings: TheatreLife)
            var bag = new Vector3(x + 0.55f, f + 2.4f + (float)rng.NextDouble() * 1.6f, z);
            InGroup("Sandbag" + i, new Vector3(bag.x, BackstageCeiling - 0.4f, bag.z), () =>
            {
                var line = new MeshBuilder();
                line.Tube(new[] { new Vector3(bag.x, BackstageCeiling - 0.4f, bag.z), bag + Vector3.up * 0.25f }, new[] { 0.008f, 0.008f }, Vector3.forward, 6, false, false);
                AddBuilder(_rope, line, new Color(0.8f, 0.7f, 0.52f));
                var sack = new MeshBuilder();
                sack.Ellipsoid(bag, new Vector3(0.14f, 0.22f, 0.14f), Quaternion.identity, 14, 10);
                AddBuilder(_crate, sack, new Color(0.72f, 0.6f, 0.42f));
            });
        }
    }

    private void BuildCallBoard()
    {
        // on the dressing room's outside wall: a cork board of notices, a clock, the running order
        var f = StageY;
        var z = DressingZ - 0.08f;
        var c = new Vector3(7.1f, f + 1.6f, z);
        Box(_wood, c, new Vector3(2.2f, 1.1f, 0.05f), new Color(0.55f, 0.38f, 0.24f));
        Box(_box, c + new Vector3(0, 0.6f, -0.01f), new Vector3(2.3f, 0.1f, 0.08f), WoodDark);
        Box(_box, c + new Vector3(0, -0.6f, -0.01f), new Vector3(2.3f, 0.1f, 0.08f), WoodDark);
        var rng = new System.Random(5);
        for (var i = 0; i < 6; i++)
        {
            var at = c + new Vector3(-0.85f + (i % 3) * 0.75f + (float)rng.NextDouble() * 0.1f, i < 3 ? 0.22f : -0.24f, -0.03f);
            var tilt = Quaternion.Euler(0, 0, ((float)rng.NextDouble() - 0.5f) * 10f);
            var r = tilt * Vector3.left * 0.25f;
            var u = tilt * Vector3.up * 0.18f;
            Patch(_notes, at - r - u, at + r - u, at + r + u, at - r + u, Vector3.back, Cream, 1, 1);
            Box(_plate, at + u * 0.85f + Vector3.back * 0.01f, new Vector3(0.025f, 0.025f, 0.02f), new Color(0.75f, 0.2f, 0.12f));
        }

        var rot = Quaternion.Euler(0, 180, 0);
        Signs.Add(new SignSpec { Text = "CALL BOARD", Pos = c + new Vector3(0, 0.6f, -0.06f), Rot = rot, Height = 0.08f, Width = 2f, Color = Cream });
        Signs.Add(new SignSpec { Text = "TONIGHT: THE DANCING DEMON  -  8 PM SHARP", Pos = c + new Vector3(0, -0.6f, -0.06f), Rot = rot, Height = 0.05f, Width = 2.2f, Color = Cream });
        // the clock, stopped
        var clock = c + new Vector3(-1.55f, 0.35f, 0);
        Box(_box, clock, new Vector3(0.42f, 0.42f, 0.06f), WoodDark);
        Patch(_posterBg, clock + new Vector3(0.17f, -0.17f, -0.035f), clock + new Vector3(-0.17f, -0.17f, -0.035f), clock + new Vector3(-0.17f, 0.17f, -0.035f), clock + new Vector3(0.17f, 0.17f, -0.035f), Vector3.back, Cream, 1, 1);
        Box(_box, clock + new Vector3(0, 0.06f, -0.045f), new Vector3(0.012f, 0.12f, 0.005f), Dark);
        Box(_box, clock + new Vector3(-0.045f, 0, -0.045f), new Vector3(0.09f, 0.012f, 0.005f), Dark);
    }

    // =====================================================================================================
    // The prop store
    // =====================================================================================================

    private void BuildPropStore()
    {
        var f = StageY;
        var rng = new System.Random(19);
        // crates, stacked against the back wall and the dividing wall
        void Crate(Vector3 bottom, float size, float yaw) => Box(_crate, bottom + Vector3.up * (size * 0.5f), Vector3.one * size, Quaternion.Euler(0, yaw, 0), new Color(0.78f, 0.6f, 0.38f));
        Crate(new Vector3(-8.3f, f, 13.3f), 0.9f, 4);
        Crate(new Vector3(-7.35f, f, 13.35f), 0.8f, -6);
        Crate(new Vector3(-8.25f, f + 0.9f, 13.3f), 0.7f, 12);
        Crate(new Vector3(-6.4f, f, 13.4f), 0.6f, 2);
        Crate(new Vector3(-4.3f, f, 13.2f), 0.75f, -3);
        Crate(new Vector3(-4.35f, f + 0.75f, 13.25f), 0.5f, 20);

        // ink barrels
        foreach (var (x, z) in new[] { (-5.4f, 13.4f), (-5.95f, 12.8f), (-3.9f, 9.3f) })
        {
            Lathe(_plate, new Vector3(x, f, z), new[] { new Vector2(0.28f, 0f), new Vector2(0.31f, 0.1f), new Vector2(0.33f, 0.45f), new Vector2(0.31f, 0.8f), new Vector2(0.28f, 0.9f), new Vector2(0f, 0.9f) }, new Color(0.3f, 0.27f, 0.25f), 18);
            Glow(new Vector3(x, f + 0.905f, z), 0.4f, new Color(0.02f, 0.015f, 0.01f, 0.75f), GlowGroup.Steady);
        }

        // a rack of costumes for the shows
        var rack = new Vector3(-6.0f, f, 10.2f);
        foreach (var dz in new[] { -1.1f, 1.1f }) Box(_plate, rack + new Vector3(0, 0.85f, dz), new Vector3(0.05f, 1.7f, 0.05f), Iron);
        Box(_plate, rack + new Vector3(0, 1.7f, 0), new Vector3(0.04f, 0.04f, 2.3f), Iron);
        var cloth = new[] { new Color(0.8f, 0.2f, 0.15f), new Color(0.15f, 0.2f, 0.45f), new Color(0.9f, 0.85f, 0.7f), new Color(0.2f, 0.45f, 0.25f), new Color(0.12f, 0.1f, 0.1f), new Color(0.75f, 0.6f, 0.2f) };
        for (var i = 0; i < 9; i++)
        {
            var at = rack + new Vector3(0, 1.25f, -0.95f + i * 0.24f);
            var h = 0.75f + (float)rng.NextDouble() * 0.3f;
            Box(_fabric, at + Vector3.down * (h * 0.5f - 0.4f), new Vector3(0.36f, h, 0.07f), Quaternion.Euler(0, 90 + ((float)rng.NextDouble() - 0.5f) * 20f, 0), cloth[i % cloth.Length]);
        }

        // a stack of Bendy cut-outs leaning on the wall, a big wooden letter sign, a sheep prop
        Standee(new Vector3(-8.5f, f, 8.9f), "bendy_pose_11", 75f);
        Standee(new Vector3(-8.35f, f, 9.6f), "bendy_pose_03", 80f);
        foreach (var (ch, k) in new[] { ('B', 0), ('E', 1), ('N', 2) })
        {
            var at = new Vector3(-3.7f, f + 0.45f, 10.4f + k * 0.55f);
            Box(_painted, at, new Vector3(0.12f, 0.9f, 0.5f), Quaternion.Euler(0, 0, 8 - k * 4), new Color(0.95f, 0.85f, 0.55f));
            Signs.Add(new SignSpec { Text = ch.ToString(), Pos = at + new Vector3(0.07f, 0, 0), Rot = Quaternion.Euler(0, -90, 0), Height = 0.6f, Width = 0.5f, Color = new Color(0.12f, 0.07f, 0.04f) });
        }

        // the sheep: Sammy's flock was cardboard too
        var sheep = new Vector3(-7.0f, f, 11.6f);
        var wool = new MeshBuilder();
        wool.Ellipsoid(sheep + new Vector3(0, 0.55f, 0), new Vector3(0.22f, 0.2f, 0.36f), Quaternion.identity, 16, 10);
        for (var k = 0; k < 7; k++) wool.Ellipsoid(sheep + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.3f, 0.65f + (float)rng.NextDouble() * 0.1f, ((float)rng.NextDouble() - 0.5f) * 0.5f), Vector3.one * 0.11f, Quaternion.identity, 10, 8);
        AddBuilder(_fabric, wool, Cream);
        var legs = new MeshBuilder();
        foreach (var lx in new[] { -0.1f, 0.1f })
        foreach (var lz in new[] { -0.22f, 0.22f })
            legs.Tube(new[] { sheep + new Vector3(lx, 0, lz), sheep + new Vector3(lx, 0.42f, lz) }, new[] { 0.025f, 0.025f }, Vector3.forward, 8, false, false);
        legs.Ellipsoid(sheep + new Vector3(0, 0.62f, 0.4f), new Vector3(0.09f, 0.1f, 0.13f), Quaternion.identity, 12, 8);
        AddBuilder(_box, legs, Dark * 2f);
    }

    // =====================================================================================================
    // The stairs down
    // =====================================================================================================

    private void BuildStairwell()
    {
        var f = StageY;
        var rise = CellarRise;
        // the treads and risers, from the backstage floor down to the cellar
        for (var i = 0; i < CellarSteps; i++)
        {
            var z0 = CellarTopZ + i * CellarTread;
            var z1 = z0 + CellarTread;
            var top = f - (i + 1) * rise;
            Box(_wood, new Vector3(0, top - 0.03f, (z0 + z1) * 0.5f), new Vector3(CellarHalf * 2, 0.06f, CellarTread + 0.02f), WoodDark * 1.1f);
            Patch(_wood, new Vector3(-CellarHalf, top, z0), new Vector3(CellarHalf, top, z0), new Vector3(CellarHalf, top + rise, z0), new Vector3(-CellarHalf, top + rise, z0), Vector3.back, WoodDark * 0.8f, 2, 1, uvRect: new Vector4(0, 0, 1, 0.2f));
            Box(_plate, new Vector3(0, top - 0.005f, z0 + 0.03f), new Vector3(CellarHalf * 2, 0.012f, 0.05f), Gold * 0.7f);
        }

        // its walls: brick down the sides (inside the stairwell), boards round its top
        foreach (var s in new[] { -1f, 1f })
            WallX(_brick, s * CellarHalf, CellarTopZ, BackstageBackZ, BasementFloor, f, new Vector3(-s, 0, 0), Brickwork * 0.8f, null);
        // a rail round the opening
        foreach (var s in new[] { -1f, 1f })
        {
            var x = s * (CellarHalf + 0.05f);
            Box(_wood, new Vector3(x, f + 0.95f, (CellarTopZ + BackstageBackZ) * 0.5f), new Vector3(0.06f, 0.05f, BackstageBackZ - CellarTopZ), WoodDark);
            for (var z = CellarTopZ + 0.3f; z < BackstageBackZ - 0.1f; z += 1.0f) Box(_wood, new Vector3(x, f + 0.48f, z), new Vector3(0.05f, 0.95f, 0.05f), WoodDark);
        }

        Box(_box, new Vector3(0, f + 2.5f, CellarTopZ - 0.2f), new Vector3(1.5f, 0.32f, 0.05f), Dark);
        Signs.Add(new SignSpec { Text = "TO THE CELLAR", Pos = new Vector3(0, f + 2.5f, CellarTopZ - 0.23f), Rot = Quaternion.identity, Height = 0.15f, Width = 1.4f, Color = new Color(1f, 0.84f, 0.5f) });
        Box(_plate, new Vector3(0, f + 2.5f, CellarTopZ - 0.15f) + Vector3.up * 0.5f, new Vector3(0.03f, 0.7f, 0.03f), Iron);
    }

    // =====================================================================================================
    // Alice Angel's dressing room
    // =====================================================================================================

    private void BuildDressingRoom()
    {
        float f = StageY, x0 = DressingX, x1 = Half, z0 = DressingZ, z1 = BackstageBackZ, top = DressingCeiling;
        var paper = Rose;
        var door = new Vector4(DressingDoorX - DressingDoorHalf, f - 1, DressingDoorX + DressingDoorHalf, f + DoorHeight);

        // floor, a rug, walls papered in rose, a picture rail, a plain ceiling
        FloorRect(_wood, x0, x1, z0, z1, f, Wood * 0.95f);
        Patch(_carpet, new Vector3(4.6f, f + 0.005f, 9.6f), new Vector3(7.8f, f + 0.005f, 9.6f), new Vector3(7.8f, f + 0.005f, 12.6f), new Vector3(4.6f, f + 0.005f, 12.6f), Vector3.up, new Color(0.55f, 0.2f, 0.28f), 3, 3, uvRect: new Vector4(0, 0, 2, 2));
        WallZ(_wood, z0, x0, x1, f, f + 1.0f, Vector3.forward, WoodDark, new[] { door });
        WallZ(_wall, z0, x0, x1, f + 1.0f, top, Vector3.forward, paper, new[] { door });
        WallZ(_wood, z1 - 0.001f, x0, x1, f, f + 1.0f, Vector3.back, WoodDark, null);
        WallZ(_wall, z1 - 0.001f, x0, x1, f + 1.0f, top, Vector3.back, paper, null);
        WallX(_wood, x0, z0, z1, f, f + 1.0f, Vector3.right, WoodDark, null);
        WallX(_wall, x0, z0, z1, f + 1.0f, top, Vector3.right, paper, null);
        WallX(_wood, x1 - 0.001f, z0, z1, f, f + 1.0f, Vector3.left, WoodDark, null);
        WallX(_wall, x1 - 0.001f, z0, z1, f + 1.0f, top, Vector3.left, paper, null);
        CeilingRect(_box, x0, x1, z0, z1, top, Plaster * 0.95f);
        // the door, its frame and Alice's star on it (the leaf swings: TheatreLife)
        Doorway(new Vector3(DressingDoorX, f, z0 - 0.07f), Vector3.right, Vector3.back, DressingDoorHalf * 2, DoorHeight, false);
        Doorway(new Vector3(DressingDoorX, f, z0 + 0.002f), Vector3.left, Vector3.forward, DressingDoorHalf * 2, DoorHeight, false);
        var hinge = DressingDoorHinge;
        InGroup("DressingDoor", hinge, () =>
        {
            Box(_door, hinge + new Vector3(DressingDoorHalf, DoorHeight * 0.5f, 0), new Vector3(DressingDoorHalf * 2 - 0.02f, DoorHeight - 0.01f, 0.05f), new Color(0.82f, 0.6f, 0.62f));
            Star(hinge + new Vector3(DressingDoorHalf, 1.6f, -0.03f), 0.2f, Vector3.back, Gold);
            foreach (var side in new[] { -1f, 1f }) Box(_box, hinge + new Vector3(DressingDoorHalf * 2 - 0.12f, 1.05f, side * 0.035f), new Vector3(0.05f, 0.05f, 0.03f), Gold);
        });
        Signs.Add(new SignSpec { Text = "ALICE", Pos = hinge + new Vector3(DressingDoorHalf, 1.6f, -0.06f), Rot = Quaternion.identity, Height = 0.07f, Width = 0.3f, Color = new Color(0.12f, 0.07f, 0.04f), Group = "DressingDoor" });
        Signs.Add(new SignSpec { Text = "DRESSING ROOM ONE", Pos = new Vector3(DressingDoorX, f + DoorHeight + 0.3f, z0 - 0.1f), Rot = Quaternion.Euler(0, 0, 0), Height = 0.1f, Width = 1.4f, Color = Cream });

        BuildVanity();
        BuildWardrobe();
        BuildCheval();
        BuildAliceThings();
    }

    /// <summary>A flat five-pointed star facing n.</summary>
    private void Star(Vector3 c, float r, Vector3 n, Color tint)
    {
        var right = Vector3.Cross(Vector3.up, -n).normalized;
        for (var k = 0; k < 5; k++)
        {
            var a = Mathf.PI * 0.5f + k * Mathf.PI * 2 / 5;
            var tip = c + (right * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * r;
            var l = c + (right * Mathf.Cos(a + 0.63f) + Vector3.up * Mathf.Sin(a + 0.63f)) * r * 0.4f;
            var rr = c + (right * Mathf.Cos(a - 0.63f) + Vector3.up * Mathf.Sin(a - 0.63f)) * r * 0.4f;
            Patch(_box, rr, c, l, tip, n, tint, 1, 1, uvRect: new Vector4(0.3f, 0.3f, 0.7f, 0.7f));
        }
    }

    private void BuildVanity()
    {
        // a dressing table against the back wall, a mirror over it ringed with bulbs
        float f = StageY, cx = 5.6f, z = BackstageBackZ - 0.32f;
        Box(_wood, new Vector3(cx, f + 0.72f, z), new Vector3(1.9f, 0.06f, 0.6f), new Color(0.82f, 0.62f, 0.56f));
        foreach (var dx in new[] { -0.75f, 0.75f }) Box(_wood, new Vector3(cx + dx, f + 0.35f, z), new Vector3(0.4f, 0.7f, 0.56f), new Color(0.78f, 0.58f, 0.52f));
        Box(_box, new Vector3(cx, f + 0.6f, z - 0.27f), new Vector3(1.0f, 0.16f, 0.04f), new Color(0.78f, 0.58f, 0.52f));
        var mc = new Vector3(cx, f + 1.62f, BackstageBackZ - 0.03f);
        const float mw = 1.4f, mh = 1.0f;
        Box(_box, mc + new Vector3(0, mh * 0.5f + 0.08f, -0.02f), new Vector3(mw + 0.32f, 0.16f, 0.06f), Gold);
        Box(_box, mc - new Vector3(0, mh * 0.5f + 0.08f, 0.02f), new Vector3(mw + 0.32f, 0.16f, 0.06f), Gold);
        foreach (var s in new[] { -1f, 1f }) Box(_box, mc + new Vector3(s * (mw * 0.5f + 0.08f), 0, -0.02f), new Vector3(0.16f, mh, 0.06f), Gold);
        for (var k = 0; k < 7; k++) Glow(mc + new Vector3(-mw * 0.5f + k * mw / 6f, mh * 0.5f + 0.08f, -0.08f), 0.13f, new Color(1f, 0.95f, 0.82f, 1f), GlowGroup.Steady);

        for (var k = 0; k < 4; k++)
            foreach (var s in new[] { -1f, 1f })
                Glow(mc + new Vector3(s * (mw * 0.5f + 0.08f), -mh * 0.375f + k * mh / 4f, -0.08f), 0.13f, new Color(1f, 0.95f, 0.82f, 1f), GlowGroup.Steady);
        Mirrors.Add(new Mirror(_root, mc, Quaternion.Euler(0, 180, 0), mw, mh, new Color(0.93f, 0.92f, 0.9f)));

        // on the table: a powder puff, bottles, a vase of flowers, a framed picture, the case of watches
        var t = f + 0.75f;
        Lathe(_posterBg, new Vector3(cx - 0.6f, t, z - 0.05f), new[] { new Vector2(0.07f, 0f), new Vector2(0.07f, 0.03f), new Vector2(0f, 0.035f) }, new Color(0.95f, 0.8f, 0.85f), 14);
        foreach (var (dx, h, col) in new[] { (-0.42f, 0.16f, new Color(0.6f, 0.75f, 0.8f)), (-0.34f, 0.11f, new Color(0.85f, 0.55f, 0.6f)), (-0.27f, 0.2f, new Color(0.8f, 0.75f, 0.5f)) })
            Lathe(_box, new Vector3(cx + dx, t, z + 0.12f), new[] { new Vector2(0.03f, 0f), new Vector2(0.032f, h * 0.7f), new Vector2(0.012f, h * 0.8f), new Vector2(0.012f, h), new Vector2(0f, h) }, col, 10);
        var vase = new Vector3(cx + 0.68f, t, z + 0.12f);
        Lathe(_box, vase, new[] { new Vector2(0.05f, 0f), new Vector2(0.07f, 0.08f), new Vector2(0.035f, 0.2f), new Vector2(0.045f, 0.24f), new Vector2(0f, 0.24f) }, new Color(0.5f, 0.65f, 0.75f), 14);
        var rng = new System.Random(3);
        for (var k = 0; k < 6; k++)
        {
            var head = vase + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.18f, 0.36f + (float)rng.NextDouble() * 0.1f, ((float)rng.NextDouble() - 0.5f) * 0.14f);
            var stem = new MeshBuilder();
            stem.Tube(new[] { vase + Vector3.up * 0.2f, head }, new[] { 0.004f, 0.004f }, Vector3.forward, 5, false, false);
            AddBuilder(_box, stem, new Color(0.3f, 0.5f, 0.25f));
            var bloom = new MeshBuilder();
            bloom.Ellipsoid(head, new Vector3(0.035f, 0.025f, 0.035f), Quaternion.identity, 10, 6);
            AddBuilder(_fabric, bloom, k % 2 == 0 ? new Color(0.95f, 0.55f, 0.6f) : new Color(0.98f, 0.92f, 0.8f));
        }

        BuildWatchCase(new Vector3(5.85f, t, 13.58f));
    }

    private void BuildWatchCase(Vector3 c)
    {
        // a shallow case lined in velvet, a watch in each hollow (pick one up to wear it: TheatreLife)
        Box(_wood, c + new Vector3(0, 0.02f, 0), new Vector3(0.62f, 0.04f, 0.32f), WoodDark);
        Patch(_fabric, c + new Vector3(-0.29f, 0.041f, -0.14f), c + new Vector3(0.29f, 0.041f, -0.14f), c + new Vector3(0.29f, 0.041f, 0.14f), c + new Vector3(-0.29f, 0.041f, 0.14f), Vector3.up, new Color(0.45f, 0.1f, 0.12f), 2, 1);
        var dials = new[] { ("dial", (Texture2D)null), ("dialBendy", null), ("dialStudio", null), ("dialGauge", null), ("dialAngel", null), ("dialDemon", null), ("none", null) };
        for (var i = 0; i < WatchCount; i++)
        {
            var at = WatchSpot(i);
            var key = dials[i].Item1;
            InGroup("Watch" + i, at, () =>
            {
                if (key == "none")
                {
                    // an empty hollow and a card: "NO WATCH"
                    Box(_posterBg, at + new Vector3(0, 0.004f, 0), new Vector3(0.09f, 0.004f, 0.05f), Cream);
                    return;
                }

                var caseCol = key switch { "dialBendy" => Dark * 2f, "dialDemon" => Dark * 1.5f, "dialGauge" => new Color(0.78f, 0.6f, 0.3f), _ => Gold };
                Lathe(_box, at, new[] { new Vector2(0.034f, 0f), new Vector2(0.036f, 0.008f), new Vector2(0.032f, 0.012f), new Vector2(0f, 0.012f) }, caseCol, 18);
                var tex = key == "dial" ? HenryTextures.WatchFace(18) : StyleTextures.Paint(key);
                var p = NewPart("Watch_" + key, tex, false, 2001);
                var dial = new MeshBuilder();
                dial.Disc(at + Vector3.up * 0.0125f, new Vector3(0.03f, 0, 0), new Vector3(0, 0, 0.03f), 24);
                AddBuilder(p, dial, new Color(1.2f, 1.2f, 1.2f));
                // its strap, curled into the hollow
                var strap = new MeshBuilder();
                strap.Torus(at + Vector3.down * 0.002f, Vector3.right, 0.03f, 0.006f, 16, 6);
                AddBuilder(_fabric, strap, key == "dialStudio" ? Gold : key == "dialAngel" ? Cream : Dark * 2.2f);
            });
        }

        Signs.Add(new SignSpec { Text = "TAKE A WATCH", Pos = c + new Vector3(0, 0.08f, 0.17f), Rot = Quaternion.Euler(0, 180, 0), Height = 0.035f, Width = 0.6f, Color = Cream });
    }

    private void BuildCheval()
    {
        // a full-length mirror in a gilt frame on the right-hand wall
        var f = StageY;
        var mc = new Vector3(Half - 0.05f, f + 1.2f, 11.2f);
        const float mw = 0.95f, mh = 2.0f;
        var n = Vector3.left;
        Box(_box, mc + new Vector3(-0.02f, mh * 0.5f + 0.07f, 0), new Vector3(0.06f, 0.14f, mw + 0.28f), Gold);
        Box(_box, mc + new Vector3(-0.02f, -mh * 0.5f - 0.07f, 0), new Vector3(0.06f, 0.14f, mw + 0.28f), Gold);
        foreach (var s in new[] { -1f, 1f }) Box(_box, mc + new Vector3(-0.02f, 0, s * (mw * 0.5f + 0.07f)), new Vector3(0.06f, mh, 0.14f), Gold);
        Star(mc + new Vector3(-0.06f, mh * 0.5f + 0.24f, 0), 0.12f, n, Gold);
        Mirrors.Add(new Mirror(_root, mc, Quaternion.Euler(0, -90, 0), mw, mh, new Color(0.93f, 0.92f, 0.9f)));
        Signs.Add(new SignSpec { Text = "Who are you tonight?", Pos = mc + new Vector3(-0.06f, -mh * 0.5f - 0.07f, 0), Rot = Quaternion.Euler(0, 90, 0), Height = 0.05f, Width = 0.9f, Color = new Color(0.12f, 0.07f, 0.04f) });
    }

    private void BuildWardrobe()
    {
        // along the left wall: a long open wardrobe, a rail, and a costume for every style
        float f = StageY, x = DressingX;
        var zs = CostumeHook(0).z - 0.35f;
        var ze = CostumeHook(WardrobeCount - 1).z + 0.35f;
        var zc = (zs + ze) * 0.5f;
        var len = ze - zs;
        var wood = new Color(0.62f, 0.42f, 0.36f);
        Box(_wood, new Vector3(x + 0.03f, f + 1.3f, zc), new Vector3(0.06f, 2.6f, len), wood * 0.8f);
        Box(_wood, new Vector3(x + 0.35f, f + 2.62f, zc), new Vector3(0.7f, 0.06f, len + 0.1f), wood);
        Box(_wood, new Vector3(x + 0.35f, f + 0.1f, zc), new Vector3(0.7f, 0.2f, len + 0.1f), wood);
        foreach (var z in new[] { zs - 0.03f, ze + 0.03f }) Box(_wood, new Vector3(x + 0.35f, f + 1.3f, z), new Vector3(0.7f, 2.6f, 0.06f), wood);
        Box(_plate, new Vector3(CostumeHook(0).x, CostumeHook(0).y, zc), new Vector3(0.025f, 0.025f, len), Gold);
        Signs.Add(new SignSpec { Text = "WARDROBE  -  TAKE A COSTUME", Pos = new Vector3(x + 0.71f, f + 2.62f, zc), Rot = Quaternion.Euler(0, -90, 0), Height = 0.07f, Width = 2.6f, Color = new Color(0.12f, 0.07f, 0.04f) });

        for (var i = 0; i < WardrobeCount; i++)
        {
            var style = Styles.All[i];
            var hook = CostumeHook(i);
            InGroup("Costume" + i, hook, () => Costume(style, hook));
            Signs.Add(new SignSpec { Text = style.Label, Pos = hook + new Vector3(0.02f, -1.62f, 0), Rot = Quaternion.Euler(0, -90, 0), Height = 0.028f, Width = 0.36f, Color = new Color(0.12f, 0.07f, 0.04f) });
            Box(_posterBg, hook + new Vector3(0.0f, -1.62f, 0), new Vector3(0.012f, 0.05f, 0.34f), Cream);
        }
    }

    /// <summary>One costume on its hanger: the outfit, flat on the hanger, with the style's own face looking out.</summary>
    private void Costume(StyleDef style, Vector3 hook)
    {
        var (torso, legs, head) = style.Id switch
        {
            BodyStyle.Lifelike => ("costumeHenryVest", "costumeHenryTrousers", "henryFace"),
            BodyStyle.Toon => ("costumeToonShirt", "costumeToonTrousers", "toonHenryFace"),
            BodyStyle.Bendy => ("bendyBody", "bendyLegs", "bendyHead"),
            BodyStyle.Boris => ("borisOveralls", "borisOverallLegs", "borisHead"),
            BodyStyle.Alice => ("aliceDress", "aliceLegs", "aliceHead"),
            BodyStyle.InkDemon => ("demonBody", "demonLegs", "demonHead"),
            BodyStyle.Sammy => ("sammyOveralls", "sammyOverallLegs", "sammyMask"),
            BodyStyle.Projectionist => ("normanVest", "normanTrousers", "projector"),
            BodyStyle.BruteBoris => ("bruteBody", "bruteOverallLegs", "bruteHead"),
            BodyStyle.Searcher => ("gooBody", "gooLegs", "searcherHead"),
            BodyStyle.Joey => ("joeySuit", "joeySuitLegs", "joeyFace"),
            _ => ("wallyOveralls", "wallyOverallLegs", "wallyFace")
        };

        Part P(string key) => CostumePart(key);
        var white = new Color(1.15f, 1.15f, 1.15f);
        // the hanger
        var hanger = new MeshBuilder();
        hanger.Torus(hook + new Vector3(0, -0.02f, 0), Vector3.right, 0.025f, 0.004f, 10, 4);
        hanger.Tube(new[] { hook + new Vector3(0, -0.05f, -0.16f), hook + new Vector3(0, -0.11f, 0), hook + new Vector3(0, -0.05f, 0.16f) }, new[] { 0.007f, 0.007f, 0.007f }, Vector3.right, 6, true, true);
        AddBuilder(_plate, hanger, Gold * 0.9f);
        // the outfit: a jacket or dress, sleeves, trousers or stockings, laid flat facing the room
        var top = new MeshBuilder();
        top.Ellipsoid(hook + new Vector3(0, -0.36f, 0), new Vector3(0.05f, 0.27f, 0.17f), Quaternion.identity, 16, 10);
        foreach (var s in new[] { -1f, 1f })
            top.Tube(new[] { hook + new Vector3(0, -0.16f, s * 0.15f), hook + new Vector3(0, -0.4f, s * 0.2f), hook + new Vector3(0.01f, -0.58f, s * 0.19f) }, new[] { 0.045f, 0.04f, 0.035f }, Vector3.right, 10, true, true);
        AddBuilder(P(torso), top, white);
        var bottom = new MeshBuilder();
        if (style.Id == BodyStyle.Alice)
            bottom.Tube(new[] { hook + new Vector3(0, -0.55f, 0), hook + new Vector3(0, -0.85f, 0) }, new[] { 0.15f, 0.21f }, Vector3.right, 16, false, true, new Vector2(0.3f, 1f));
        foreach (var s in new[] { -1f, 1f })
            bottom.Tube(new[] { hook + new Vector3(0, -0.6f, s * 0.07f), hook + new Vector3(0, -1.05f, s * 0.075f), hook + new Vector3(0, -1.32f, s * 0.08f) }, new[] { 0.055f, 0.048f, 0.044f }, Vector3.right, 10, true, true);
        AddBuilder(P(style.Id == BodyStyle.Alice ? "aliceSkirt" : legs), bottom, white);
        // the face, turned to the room, on a little stand above the hanger
        var face = new MeshBuilder();
        face.Ellipsoid(hook + new Vector3(0.06f, 0.08f, 0), new Vector3(0.075f, 0.085f, 0.075f), Quaternion.Euler(0, 90, 0), 18, 12);
        AddBuilder(P(head), face, white);
        if (style.Id is BodyStyle.Bendy or BodyStyle.InkDemon)
            foreach (var s in new[] { -1f, 1f })
            {
                var horn = new MeshBuilder();
                horn.Tube(new[] { hook + new Vector3(0.06f, 0.14f, s * 0.04f), hook + new Vector3(0.06f, 0.2f, s * 0.07f), hook + new Vector3(0.07f, 0.24f, s * 0.06f) }, new[] { 0.018f, 0.01f, 0.002f }, Vector3.right, 8, true, true);
                AddBuilder(P(style.Id == BodyStyle.Bendy ? "bendyBody" : "demonBody"), horn, white);
            }

        if (style.Id == BodyStyle.Alice)
        {
            var halo = new MeshBuilder();
            halo.Torus(hook + new Vector3(0.06f, 0.2f, 0), Vector3.up, 0.06f, 0.007f, 20, 6);
            AddBuilder(_box, halo, Gold * 1.2f);
        }
    }

    private readonly Dictionary<string, Part> _costumeParts = new();

    private Part CostumePart(string key)
    {
        if (_costumeParts.TryGetValue(key, out var p)) return p;
        Texture2D tex = key switch
        {
            "costumeHenryVest" => StyleTextures.Fabric("InkVR_Costume_HenryVest", new StyleTextures.Cloth { Base = new Color(0.42f, 0.3f, 0.22f), Shade = 0.3f, Placket = true, Buttons = true, Seed = 301 }),
            "costumeHenryTrousers" => StyleTextures.Fabric("InkVR_Costume_HenryTrousers", new StyleTextures.Cloth { Base = new Color(0.36f, 0.34f, 0.32f), Shade = 0.3f, Pinstripe = 14, StripeColor = new Color(0.6f, 0.58f, 0.55f), Seed = 302 }),
            "costumeToonShirt" => StyleTextures.Fabric("InkVR_Costume_ToonShirt", new StyleTextures.Cloth { Base = new Color(0.95f, 0.88f, 0.66f), Shade = 0.3f, Placket = true, Buttons = true, Seed = 303 }),
            "costumeToonTrousers" => StyleTextures.Fabric("InkVR_Costume_ToonTrousers", new StyleTextures.Cloth { Base = new Color(0.55f, 0.38f, 0.22f), Shade = 0.3f, Seed = 304 }),
            _ => StyleTextures.Paint(key)
        };
        p = NewPart("Costume_" + key, tex ?? TheatreArt.Painted());
        _costumeParts[key] = p;
        return p;
    }

    private void BuildAliceThings()
    {
        var f = StageY;
        // her halo on a stand by the door (a fan find), a stool at the table, a folding screen, a picture of her
        var stand = new Vector3(8.4f, f, 9.0f);
        Lathe(_box, stand, new[] { new Vector2(0.15f, 0f), new Vector2(0.12f, 0.03f), new Vector2(0.02f, 0.05f), new Vector2(0.015f, 1.2f), new Vector2(0f, 1.22f) }, Gold * 0.8f, 12);
        InGroup("AliceHalo", stand + Vector3.up * 1.32f, () =>
        {
            var halo = new MeshBuilder();
            halo.Torus(stand + Vector3.up * 1.32f, Vector3.up, 0.13f, 0.016f, 28, 8);
            AddBuilder(_box, halo, new Color(1.3f, 1.05f, 0.55f));
        });
        Glow(stand + Vector3.up * 1.32f, 0.6f, new Color(1f, 0.85f, 0.5f, 0.3f), GlowGroup.Flicker);

        var stool = new Vector3(5.6f, f, 13.0f);
        Lathe(_fabric, stool + Vector3.up * 0.42f, new[] { new Vector2(0.2f, 0f), new Vector2(0.21f, 0.05f), new Vector2(0.15f, 0.09f), new Vector2(0f, 0.1f) }, new Color(0.7f, 0.25f, 0.3f), 16);
        foreach (var a in new[] { 45f, 135f, 225f, 315f })
        {
            var d = Quaternion.Euler(0, a, 0) * Vector3.forward * 0.13f;
            Box(_wood, stool + d + Vector3.up * 0.21f, new Vector3(0.035f, 0.42f, 0.035f), WoodDark);
        }

        // the folding screen, three panels in a zig-zag
        for (var k = 0; k < 3; k++)
        {
            var c = new Vector3(8.2f, f + 0.85f, 12.4f + k * 0.52f);
            Box(_tent, c, new Vector3(0.04f, 1.7f, 0.5f), Quaternion.Euler(0, k % 2 == 0 ? 25 : -25, 0), new Color(0.9f, 0.75f, 0.75f));
        }

        // a framed portrait of Alice (a cartoon one-sheet) over the wardrobe end, and her script
        var pic = new Vector3(DressingX + 0.03f, f + 2.95f, 13.6f);
        Box(_box, pic, new Vector3(0.05f, 0.42f, 0.34f), Gold);
        Patch(_posterBg, pic + new Vector3(0.03f, -0.17f, 0.13f), pic + new Vector3(0.03f, -0.17f, -0.13f), pic + new Vector3(0.03f, 0.17f, -0.13f), pic + new Vector3(0.03f, 0.17f, 0.13f), Vector3.right, Cream, 1, 1);
        Signs.Add(new SignSpec { Text = "ALICE ANGEL", Pos = pic + new Vector3(0.04f, -0.12f, 0), Rot = Quaternion.Euler(0, -90, 0), Height = 0.035f, Width = 0.26f, Color = new Color(0.45f, 0.08f, 0.04f) });
        Signs.Add(new SignSpec { Text = "with love, S.C.", Pos = pic + new Vector3(0.04f, 0.1f, 0), Rot = Quaternion.Euler(0, -90, 0), Height = 0.022f, Width = 0.26f, Color = new Color(0.12f, 0.07f, 0.04f) });
    }

    // =====================================================================================================
    // Backstage light
    // =====================================================================================================

    private void PlanBackstageLamps()
    {
        var f = StageY;
        var warm = new Color(1f, 0.8f, 0.52f);
        // the ghost light: the one bulb burning in the crossover
        AddLamp(GhostLight + Vector3.up * 1.72f, new Color(1f, 0.9f, 0.7f) * 1.5f, 3.2f);
        // work lights in the wings and the prop store, a lamp over the stairs, light spilling off the stage
        foreach (var s in new[] { -1f, 1f }) AddLamp(new Vector3(s * 7.4f, f + 3.0f, 5.6f), warm * 0.9f, 2.2f, Vector3.down, 0.5f);
        AddLamp(new Vector3(-6.2f, f + 3.2f, 11.2f), warm * 1.0f, 2.6f, Vector3.down, 0.5f);
        AddLamp(new Vector3(0, f + 2.6f, CellarTopZ + 0.2f), warm * 0.8f, 1.8f, Vector3.down, 0.4f);
        AddLamp(new Vector3(7.1f, f + 2.6f, DressingZ - 0.7f), warm * 0.8f, 1.6f, Vector3.forward, 0.4f); // over the call board
        // Alice's room: the vanity's bulbs, a pink-shaded ceiling lamp, the mirror's own lamp
        AddLamp(new Vector3(5.6f, f + 1.7f, BackstageBackZ - 0.45f), new Color(1f, 0.92f, 0.8f) * 1.6f, 1.6f);
        AddLamp(new Vector3(6.1f, DressingCeiling - 0.4f, 11.2f), new Color(1f, 0.82f, 0.72f) * 1.1f, 3.2f);
        AddLamp(new Vector3(Half - 0.8f, f + 2.4f, 11.2f), new Color(1f, 0.9f, 0.75f) * 0.8f, 1.6f, Vector3.left, 0.3f);
        // the shade of that ceiling lamp, and the work lights' cages
        var shade = new Vector3(6.1f, DressingCeiling - 0.32f, 11.2f);
        Lathe(_fabric, shade, new[] { new Vector2(0.3f, 0f), new Vector2(0.18f, 0.22f), new Vector2(0f, 0.22f) }, new Color(1.1f, 0.75f, 0.72f), 18, true);
        Glow(shade + Vector3.down * 0.05f, 1.2f, new Color(1f, 0.8f, 0.7f, 0.3f), GlowGroup.Steady);
        foreach (var p in new[] { new Vector3(-7.4f, f + 3.0f, 5.6f), new Vector3(7.4f, f + 3.0f, 5.6f), new Vector3(-6.2f, f + 3.2f, 11.2f), new Vector3(0, f + 2.6f, CellarTopZ + 0.2f) })
        {
            Box(_plate, p + Vector3.up * 0.1f, new Vector3(0.18f, 0.08f, 0.18f), Iron * 1.5f);
            Glow(p, 0.16f, new Color(1f, 0.9f, 0.7f, 1f), GlowGroup.Flicker);
            Glow(p, 0.9f, new Color(1f, 0.8f, 0.55f, 0.25f), GlowGroup.Flicker);
        }
    }
}
