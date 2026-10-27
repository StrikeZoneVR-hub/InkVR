using InkVR.Body;
using InkVR.Rendering;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The projection booth, up a short stair at the back of the balcony and through a door in the back wall. It hangs
/// over the lobby's doors (from the lobby you see its underside and front, gilded and clocked). Inside: the projector
/// throwing the picture through the middle port, its reels turning; a second machine for the changeover; a follow
/// spot in the left-hand port that you can aim at the stage yourself; the switchboard (house lights, curtain, follow
/// spot, marquee); shelves of film cans; Norman's rewind bench and his notes.
/// </summary>
public sealed partial class Theatre
{
    public static readonly float[] PortX = { -1.7f, 0f, 1.7f };
    public const float PortHalfW = 0.33f, PortHalfH = 0.22f;

    public static Vector3 FollowSpotPivot => new(-1.7f, Projector.y, -14.55f);
    public static Vector3 ReelAt(int projector, int reel) => new(PortX[projector + 1] - 0.0f, ProjFloor + 1.87f, -15.0f + (reel == 0 ? 0.32f : -0.32f));

    public const int SwitchCount = 4;
    public static readonly string[] SwitchNames = { "HOUSE", "CURTAIN", "SPOT", "MARQUEE" };

    /// <summary>Switch i's hinge on the switchboard (left wall); its handle stands 0.3 m out from it, up (on) or down (off).</summary>
    public static Vector3 SwitchHinge(int i) => new(ProjX0 + 0.12f, ProjFloor + 1.35f, -15.55f - i * 0.3f);

    public static Vector3 GoldenReelAt => new(1.95f, ProjFloor + 1.62f, ProjBackZ + 0.18f);
    public static Vector3 BoothDoorHinge => new(ProjDoorX - ProjDoorHalf, ProjFloor, BackZ - 0.25f);

    private void BuildProjectionBooth()
    {
        float f = ProjFloor, top = ProjCeiling, x0 = ProjX0, x1 = ProjX1, zf = LobbyZ, zb = ProjBackZ;
        var holes = BoothHoles();
        var paper = new Color(0.62f, 0.6f, 0.5f);

        // inside: boards, painted walls (the ports and the door through the front one), a low ceiling
        FloorRect(_wood, x0, x1, zb, zf, f, WoodDark * 1.1f);
        WallZ(_wood, zf, x0, x1, f, f + 0.9f, Vector3.back, WoodDark, holes);
        WallZ(_painted, zf, x0, x1, f + 0.9f, top, Vector3.back, paper, holes, 1f, 1f);
        WallZ(_wood, zb, x0, x1, f, f + 0.9f, Vector3.forward, WoodDark, null);
        WallZ(_painted, zb, x0, x1, f + 0.9f, top, Vector3.forward, paper, null, 1f, 1f);
        WallX(_wood, x0, zb, zf, f, f + 0.9f, Vector3.right, WoodDark, null);
        WallX(_painted, x0, zb, zf, f + 0.9f, top, Vector3.right, paper, null, 1f, 1f);
        WallX(_wood, x1, zb, zf, f, f + 0.9f, Vector3.left, WoodDark, null);
        WallX(_painted, x1, zb, zf, f + 0.9f, top, Vector3.left, paper, null, 1f, 1f);
        CeilingRect(_painted, x0, x1, zb, zf, top, paper * 0.85f, 1f);

        // the reveals through the back wall's thickness, for the ports and the door
        foreach (var h in holes) RevealZ(h, BackZ, zf);

        // outside, from the lobby: the underside, the front and ends, gilded, with a clock
        float ox0 = x0 - 0.1f, ox1 = x1 + 0.1f, oz = zb - 0.1f, under = f - 0.15f, lobbyTop = LobbyCeiling;
        CeilingRect(_box, ox0, ox1, oz, zf, under, WoodDark * 0.9f, 1.2f);
        WallZ(_wood, oz, ox0, ox1, under, lobbyTop, Vector3.back, WoodDark * 1.05f, null, 1.6f, 1.6f);
        WallX(_wood, ox0, oz, zf, under, lobbyTop, Vector3.left, WoodDark * 1.05f, null, 1.6f, 1.6f);
        WallX(_wood, ox1, oz, zf, under, lobbyTop, Vector3.right, WoodDark * 1.05f, null, 1.6f, 1.6f);
        Band(_ornament, new Vector3(ox1, under + 0.05f, oz - 0.02f), new Vector3(ox0, under + 0.05f, oz - 0.02f), Vector3.up, 0.3f, Vector3.back, Gold, 1.4f);
        Band(_ornament, new Vector3(ox1, lobbyTop - 0.45f, oz - 0.02f), new Vector3(ox0, lobbyTop - 0.45f, oz - 0.02f), Vector3.up, 0.3f, Vector3.back, Gold, 1.4f);
        foreach (var x in new[] { ox0 + 0.6f, (ox0 + ox1) * 0.5f, ox1 - 0.6f })
        {
            // iron brackets under it, back to the wall
            var a = new Vector3(x, under - 0.02f, oz + 0.3f);
            var b = new Vector3(x, under - 1.4f, zf - 0.05f);
            Box(_plate, (a + b) * 0.5f, new Vector3(0.06f, 0.06f, (b - a).magnitude), Quaternion.LookRotation(b - a), Iron * 1.4f);
        }

        var clock = new Vector3((ox0 + ox1) * 0.5f, under + 1.0f, oz - 0.04f);
        var bezel = new MeshBuilder();
        bezel.Torus(clock, Vector3.forward, 0.42f, 0.05f, 36, 8);
        AddBuilder(_box, bezel, Gold);
        var face = new MeshBuilder();
        face.Disc(clock + Vector3.back * 0.01f, new Vector3(-0.4f, 0, 0), new Vector3(0, 0.4f, 0), 32);
        AddBuilder(_posterBg, face, Cream);
        for (var h = 0; h < 12; h++)
        {
            var a = h / 12f * Mathf.PI * 2;
            Box(_box, clock + new Vector3(Mathf.Sin(a) * 0.32f, Mathf.Cos(a) * 0.32f, -0.02f), new Vector3(0.025f, 0.06f, 0.01f), Quaternion.Euler(0, 0, -a * Mathf.Rad2Deg), Dark);
        }

        InGroup("BoothClockHour", clock + Vector3.back * 0.03f, () => Box(_box, clock + new Vector3(0, 0.1f, -0.03f), new Vector3(0.03f, 0.2f, 0.01f), Dark));
        InGroup("BoothClockMinute", clock + Vector3.back * 0.04f, () => Box(_box, clock + new Vector3(0, 0.15f, -0.04f), new Vector3(0.02f, 0.3f, 0.01f), Dark));
        Signs.Add(new SignSpec { Text = "PROJECTION", Pos = new Vector3(clock.x - 2.4f, under + 1.0f, oz - 0.03f), Rot = Quaternion.identity, Height = 0.2f, Width = 2f, Color = new Color(1f, 0.84f, 0.5f) });
        Signs.Add(new SignSpec { Text = "MCMXXXIII", Pos = new Vector3(clock.x + 2.4f, under + 1.0f, oz - 0.03f), Rot = Quaternion.identity, Height = 0.16f, Width = 2f, Color = new Color(1f, 0.84f, 0.5f) });

        BuildProjector(0, true);
        BuildProjector(1, false);
        BuildFollowSpot();
        BuildSwitchboard();
        BuildFilmShelf();
        BuildRewindBench();
        BuildBoothDoor();
        BuildBoothStair();
        PlanBoothLamps();
    }

    /// <summary>The ports and the door, as holes in the booth's front wall (x0, y0, x1, y1).</summary>
    private static Vector4[] BoothHoles()
    {
        var y = Projector.y;
        return new[]
        {
            new Vector4(PortX[0] - PortHalfW, y - PortHalfH, PortX[0] + PortHalfW, y + PortHalfH),
            new Vector4(PortX[1] - PortHalfW, y - PortHalfH, PortX[1] + PortHalfW, y + PortHalfH),
            new Vector4(PortX[2] - PortHalfW, y - PortHalfH, PortX[2] + PortHalfW, y + PortHalfH),
            new Vector4(ProjDoorX - ProjDoorHalf, ProjFloor, ProjDoorX + ProjDoorHalf, ProjFloor + ProjDoorHeight)
        };
    }

    /// <summary>The four faces lining a hole through a wall between z0 and z1 (x0, y0, x1, y1).</summary>
    private void RevealZ(Vector4 h, float z0, float z1)
    {
        Patch(_box, new Vector3(h.x, h.y, z0), new Vector3(h.x, h.y, z1), new Vector3(h.x, h.w, z1), new Vector3(h.x, h.w, z0), Vector3.right, WoodDark, 1, 1);
        Patch(_box, new Vector3(h.z, h.y, z1), new Vector3(h.z, h.y, z0), new Vector3(h.z, h.w, z0), new Vector3(h.z, h.w, z1), Vector3.left, WoodDark, 1, 1);
        Patch(_box, new Vector3(h.x, h.w, z0), new Vector3(h.z, h.w, z0), new Vector3(h.z, h.w, z1), new Vector3(h.x, h.w, z1), Vector3.down, WoodDark, 1, 1);
        Patch(_box, new Vector3(h.x, h.y, z1), new Vector3(h.z, h.y, z1), new Vector3(h.z, h.y, z0), new Vector3(h.x, h.y, z0), Vector3.up, WoodDark, 1, 1);
    }

    private void BuildProjector(int k, bool main)
    {
        // a 1930s projector on its pedestal: lamp house behind, mechanism, the lens through the port, two reels on arms
        var x = PortX[k + 1];
        var f = ProjFloor;
        var y = Projector.y;
        var iron = new Color(0.3f, 0.32f, 0.3f);
        Lathe(_plate, new Vector3(x, f, -15.0f), new[] { new Vector2(0.32f, 0f), new Vector2(0.3f, 0.06f), new Vector2(0.12f, 0.1f), new Vector2(0.1f, 0.95f), new Vector2(0.16f, 1.0f), new Vector2(0f, 1.02f) }, iron, 16);
        Box(_plate, new Vector3(x, y - 0.02f, -14.95f), new Vector3(0.36f, 0.42f, 0.62f), iron * 1.1f);
        Box(_plate, new Vector3(x, y + 0.03f, -15.45f), new Vector3(0.34f, 0.5f, 0.4f), iron * 0.9f); // the lamp house
        Lathe(_plate, new Vector3(x, y + 0.28f, -15.45f), new[] { new Vector2(0.1f, 0f), new Vector2(0.1f, 0.14f), new Vector2(0.13f, 0.16f), new Vector2(0f, 0.17f) }, iron, 12); // its chimney
        var lens = new MeshBuilder();
        lens.Tube(new[] { new Vector3(x, y, -14.64f), new Vector3(x, y, -14.2f) }, new[] { 0.075f, 0.065f }, Vector3.up, 16, false, true);
        AddBuilder(_plate, lens, new Color(0.7f, 0.55f, 0.3f));
        if (main) Glow(new Vector3(x, y, -14.15f), 0.18f, new Color(1f, 0.97f, 0.88f, 1f), GlowGroup.Projector);
        Glow(new Vector3(x + 0.18f, y + 0.05f, -15.45f), 0.25f, new Color(1f, 0.75f, 0.4f, main ? 0.7f : 0.25f), main ? GlowGroup.Projector : GlowGroup.Steady); // light leaking from the lamp house vents
        for (var r = 0; r < 2; r++)
        {
            var at = ReelAt(k, r);
            // the arm
            Box(_plate, new Vector3(at.x, (at.y + y + 0.2f) * 0.5f, at.z), new Vector3(0.04f, at.y - (y + 0.2f), 0.04f), iron);
            InGroup((main ? "ReelMain" : "ReelSpare") + r, at, () =>
            {
                var reel = new MeshBuilder();
                Drum(at + Vector3.left * 0.035f, at + Vector3.right * 0.035f, 0.3f, 28, reel);
                AddBuilder(_plate, reel, new Color(0.45f, 0.45f, 0.42f));
                var film = new MeshBuilder();
                Drum(at + Vector3.left * 0.037f, at + Vector3.right * 0.037f, r == 0 ? 0.22f : 0.12f, 24, film);
                foreach (var side in new[] { -1f, 1f })
                    for (var s = 0; s < 3; s++)
                    {
                        var a = s / 3f * Mathf.PI * 2;
                        film.Box(at + new Vector3(side * 0.037f, Mathf.Sin(a) * 0.17f, Mathf.Cos(a) * 0.17f), new Vector3(0.006f, 0.1f, 0.06f), Quaternion.Euler(-a * Mathf.Rad2Deg, 0, 0));
                    }

                AddBuilder(_box, film, Dark * 1.6f);
            });
        }
    }

    private void BuildFollowSpot()
    {
        // a long-barrelled follow spot on a yoke, its nose in the left-hand port (aim it by its handle: TheatreLife)
        var p = FollowSpotPivot;
        var f = ProjFloor;
        Lathe(_plate, new Vector3(p.x, f, p.z - 0.1f), new[] { new Vector2(0.3f, 0f), new Vector2(0.06f, 0.06f), new Vector2(0.05f, p.y - f - 0.25f), new Vector2(0.08f, p.y - f - 0.2f), new Vector2(0f, p.y - f - 0.19f) }, Iron * 1.3f, 14);
        InGroup("FollowSpot", p, () =>
        {
            var barrel = new MeshBuilder();
            barrel.Tube(new[] { p + new Vector3(0, 0, -0.55f), p + new Vector3(0, 0, 0.35f), p + new Vector3(0, 0, 0.42f) }, new[] { 0.16f, 0.13f, 0.11f }, Vector3.up, 20, true, true);
            barrel.Box(p + new Vector3(0, 0.0f, -0.62f), new Vector3(0.05f, 0.05f, 0.18f), Quaternion.identity); // the handle out of its back
            barrel.Ellipsoid(p + new Vector3(0, 0, -0.72f), new Vector3(0.045f, 0.045f, 0.045f), Quaternion.identity, 10, 6);
            AddBuilder(_plate, barrel, new Color(0.25f, 0.27f, 0.26f));
            var yoke = new MeshBuilder();
            yoke.Tube(new[] { p + new Vector3(-0.2f, 0, 0), p + new Vector3(-0.2f, -0.2f, 0), p + new Vector3(0.2f, -0.2f, 0), p + new Vector3(0.2f, 0, 0) }, new[] { 0.025f, 0.025f, 0.025f, 0.025f }, Vector3.forward, 8, true, true);
            AddBuilder(_plate, yoke, Iron * 1.4f);
        });
        Signs.Add(new SignSpec { Text = "FOLLOW SPOT", Pos = new Vector3(p.x, ProjFloor + 0.25f, p.z + 0.31f), Rot = Quaternion.identity, Height = 0.05f, Width = 0.5f, Color = Cream, Group = null });
    }

    private void BuildSwitchboard()
    {
        // on the left wall: a slate panel, four knife switches with their names, a pilot light over each
        var f = ProjFloor;
        var c = new Vector3(ProjX0 + 0.03f, f + 1.35f, -16.0f);
        Box(_painted, c, new Vector3(0.05f, 0.95f, 1.45f), new Color(0.22f, 0.24f, 0.24f));
        var rot = Quaternion.Euler(0, -90, 0);
        Signs.Add(new SignSpec { Text = "SWITCHBOARD", Pos = c + new Vector3(0.04f, 0.38f, 0), Rot = rot, Height = 0.06f, Width = 1.2f, Color = Cream });
        for (var i = 0; i < SwitchCount; i++)
        {
            var h = SwitchHinge(i);
            Box(_plate, h + new Vector3(-0.04f, 0, 0), new Vector3(0.04f, 0.2f, 0.1f), Gold * 0.8f);
            InGroup("Switch" + i, h, () =>
            {
                var blade = new MeshBuilder();
                blade.Box(h + new Vector3(0.15f, 0, 0), new Vector3(0.3f, 0.02f, 0.05f), Quaternion.identity);
                blade.Tube(new[] { h + new Vector3(0.3f, 0, -0.06f), h + new Vector3(0.3f, 0, 0.06f) }, new[] { 0.022f, 0.022f }, Vector3.up, 10, true, true);
                AddBuilder(_plate, blade, Iron * 1.6f);
            });
            Signs.Add(new SignSpec { Text = SwitchNames[i], Pos = h + new Vector3(0.04f, -0.22f, 0), Rot = rot, Height = 0.04f, Width = 0.28f, Color = Cream });
            Glow(h + new Vector3(0.05f, 0.2f, 0), 0.07f, new Color(1f, 0.85f, 0.5f, 1f), GlowGroup.Steady);
        }
    }

    private void BuildFilmShelf()
    {
        // shelves of film cans on the back wall, a gold reel among them (a fan find)
        var f = ProjFloor;
        var rng = new System.Random(33);
        foreach (var y in new[] { f + 0.9f, f + 1.4f, f + 1.9f })
        {
            Box(_wood, new Vector3(0.8f, y, ProjBackZ + 0.18f), new Vector3(3.4f, 0.03f, 0.34f), WoodDark);
            for (var x = -0.75f; x < 2.45f; x += 0.11f)
            {
                if (y > f + 1.3f && y < f + 1.5f && Mathf.Abs(x - 1.95f) < 0.2f) continue; // the gold reel's place
                // a stack of cans lying flat, then a gap
                var can = new MeshBuilder();
                var count = 1 + rng.Next(4);
                for (var k = 0; k < count; k++)
                {
                    var cy = y + 0.015f + k * 0.045f;
                    var cx = x + 0.17f + ((float)rng.NextDouble() - 0.5f) * 0.03f;
                    Drum(new Vector3(cx, cy, ProjBackZ + 0.18f), new Vector3(cx, cy + 0.04f, ProjBackZ + 0.18f), 0.165f, 20, can);
                }

                AddBuilder(_plate, can, rng.NextDouble() < 0.5 ? new Color(0.55f, 0.55f, 0.52f) : new Color(0.62f, 0.52f, 0.36f));
                x += 0.3f;
            }
        }

        var reel = GoldenReelAt;
        InGroup("GoldenReel", reel, () =>
        {
            var mb = new MeshBuilder();
            Drum(reel + Vector3.left * 0.04f, reel + Vector3.right * 0.04f, 0.17f, 28, mb);
            AddBuilder(_box, mb, new Color(1.35f, 1.05f, 0.45f));
        });
        Glow(reel + Vector3.forward * 0.1f, 0.5f, new Color(1f, 0.85f, 0.4f, 0.25f), GlowGroup.Flicker);
    }

    private void BuildRewindBench()
    {
        // Norman's bench by the door: a rewind with a reel you can spin, a splicer, his notes, a stool
        var f = ProjFloor;
        var c = new Vector3(3.5f, f, -16.5f);
        Box(_wood, c + new Vector3(0, 0.82f, 0), new Vector3(1.4f, 0.05f, 0.6f), WoodDark * 1.2f);
        foreach (var dx in new[] { -0.62f, 0.62f })
        foreach (var dz in new[] { -0.25f, 0.25f })
            Box(_wood, c + new Vector3(dx, 0.4f, dz), new Vector3(0.05f, 0.8f, 0.05f), WoodDark);
        var spindle = c + new Vector3(-0.3f, 1.1f, 0);
        Box(_plate, spindle + Vector3.down * 0.13f, new Vector3(0.04f, 0.22f, 0.04f), Iron * 1.4f);
        InGroup("RewindReel", spindle, () =>
        {
            var reel = new MeshBuilder();
            Drum(spindle + Vector3.left * 0.03f, spindle + Vector3.right * 0.03f, 0.24f, 24, reel);
            reel.Box(spindle + new Vector3(0.04f, 0.16f, 0), new Vector3(0.03f, 0.03f, 0.03f), Quaternion.identity); // its crank knob
            AddBuilder(_plate, reel, new Color(0.5f, 0.5f, 0.47f));
        });
        var note = c + new Vector3(0.35f, 0.851f, 0.05f);
        Patch(_notes, note + new Vector3(-0.14f, 0, -0.1f), note + new Vector3(0.14f, 0, -0.1f), note + new Vector3(0.14f, 0, 0.1f), note + new Vector3(-0.14f, 0, 0.1f), Vector3.up, Cream, 1, 1);
        // his notes on the wall above
        var wall = new Vector3(ProjX1 - 0.01f, f + 1.55f, -16.5f);
        Patch(_notes, wall + new Vector3(0, -0.2f, 0.28f), wall + new Vector3(0, -0.2f, -0.28f), wall + new Vector3(0, 0.2f, -0.28f), wall + new Vector3(0, 0.2f, 0.28f), Vector3.left, Cream, 1, 1);
        var rot = Quaternion.Euler(0, 90, 0);
        Signs.Add(new SignSpec { Text = "Reels changed by Friday", Pos = wall + new Vector3(-0.01f, 0.1f, 0), Rot = rot, Height = 0.035f, Width = 0.5f, Color = new Color(0.12f, 0.07f, 0.04f) });
        Signs.Add(new SignSpec { Text = "or Mr. Drew hears of it.", Pos = wall + new Vector3(-0.01f, 0.03f, 0), Rot = rot, Height = 0.035f, Width = 0.5f, Color = new Color(0.12f, 0.07f, 0.04f) });
        Signs.Add(new SignSpec { Text = "- N.P.", Pos = wall + new Vector3(-0.01f, -0.08f, 0.1f), Rot = rot, Height = 0.035f, Width = 0.3f, Color = new Color(0.12f, 0.07f, 0.04f) });
        var stool = c + new Vector3(0, 0, -0.75f);
        Lathe(_wood, stool, new[] { new Vector2(0.18f, 0f), new Vector2(0.03f, 0.04f), new Vector2(0.03f, 0.6f), new Vector2(0.18f, 0.62f), new Vector2(0.18f, 0.66f), new Vector2(0f, 0.66f) }, WoodDark * 1.2f, 14);
        // a calendar
        var cal = new Vector3(ProjX1 - 0.01f, f + 1.6f, -15.4f);
        Patch(_posterBg, cal + new Vector3(0, -0.2f, 0.15f), cal + new Vector3(0, -0.2f, -0.15f), cal + new Vector3(0, 0.2f, -0.15f), cal + new Vector3(0, 0.2f, 0.15f), Vector3.left, Cream, 1, 1);
        Signs.Add(new SignSpec { Text = "OCTOBER", Pos = cal + new Vector3(-0.01f, 0.12f, 0), Rot = rot, Height = 0.045f, Width = 0.28f, Color = new Color(0.45f, 0.08f, 0.04f) });
        Signs.Add(new SignSpec { Text = "1933", Pos = cal + new Vector3(-0.01f, 0.05f, 0), Rot = rot, Height = 0.04f, Width = 0.28f, Color = new Color(0.12f, 0.07f, 0.04f) });
    }

    private void BuildBoothDoor()
    {
        var hinge = BoothDoorHinge;
        InGroup("BoothDoor", hinge, () =>
        {
            Box(_door, hinge + new Vector3(ProjDoorHalf, ProjDoorHeight * 0.5f, 0), new Vector3(ProjDoorHalf * 2 - 0.02f, ProjDoorHeight - 0.01f, 0.05f), Wood * 0.9f);
            foreach (var side in new[] { -1f, 1f }) Box(_box, hinge + new Vector3(ProjDoorHalf * 2 - 0.12f, 1.0f, side * 0.035f), new Vector3(0.05f, 0.05f, 0.03f), Gold);
        });
        Signs.Add(new SignSpec { Text = "BOOTH  -  STAFF ONLY", Pos = new Vector3(ProjDoorX, ProjFloor + ProjDoorHeight + 0.18f, BackZ + 0.03f), Rot = Quaternion.Euler(0, 180, 0), Height = 0.07f, Width = 1.0f, Color = new Color(1f, 0.84f, 0.5f) });
    }

    private void BuildBoothStair()
    {
        // from the balcony's top tier: five steep steps up along the back wall to a landing and the booth door
        var b = BalconyTop;
        float z0 = ProjStairZ0, z1 = ProjStairZ1, zc = (z0 + z1) * 0.5f, depth = z1 - z0;
        for (var i = 0; i < ProjSteps; i++)
        {
            var x0 = ProjStairX0 + i * ProjStepRun;
            var top = b + (i + 1) * ProjRise;
            Box(_wood, new Vector3(x0 + ProjStepRun * 0.5f, (b + top) * 0.5f, zc), new Vector3(ProjStepRun, top - b, depth), WoodDark * 1.15f);
            Box(_box, new Vector3(x0 + 0.02f, top - 0.012f, zc), new Vector3(0.05f, 0.024f, depth), Gold);
        }

        var lx0 = ProjStairX1;
        var lx1 = ProjDoorX + ProjDoorHalf + 0.05f;
        Box(_wood, new Vector3((lx0 + lx1) * 0.5f, ProjFloor - 0.06f, zc), new Vector3(lx1 - lx0, 0.12f, depth), WoodDark * 1.15f);
        Box(_wood, new Vector3(lx1 - 0.05f, (b + ProjFloor) * 0.5f, z1 - 0.05f), new Vector3(0.1f, ProjFloor - b, 0.1f), WoodDark);
        // the rail on its open side
        Vector3 Tread(int i) => new(ProjStairX0 + (i + 0.5f) * ProjStepRun, b + (i + 1) * ProjRise, z1 + 0.02f);
        var a = Tread(0) + Vector3.up * 0.95f;
        var c = new Vector3(lx1 - 0.05f, ProjFloor + 0.95f, z1 + 0.02f);
        var mid = Tread(ProjSteps - 1) + Vector3.up * 0.95f;
        Box(_wood, (a + mid) * 0.5f, new Vector3(0.05f, 0.05f, (mid - a).magnitude), Quaternion.LookRotation(mid - a), Wood);
        Box(_wood, (mid + c) * 0.5f, new Vector3(0.05f, 0.05f, (c - mid).magnitude), Quaternion.LookRotation(c - mid), Wood);
        for (var i = 0; i < ProjSteps; i++) Box(_box, Tread(i) + Vector3.up * 0.47f, new Vector3(0.025f, 0.94f, 0.025f), GoldDark);
        Box(_box, new Vector3(lx1 - 0.05f, ProjFloor + 0.47f, z1 + 0.02f), new Vector3(0.05f, 0.94f, 0.05f), GoldDark);
    }

    private void PlanBoothLamps()
    {
        var f = ProjFloor;
        // a green-shaded work lamp over the bench, a bulb over the switchboard, the projector's lamp house
        AddLamp(new Vector3(3.5f, f + 1.6f, -16.5f), new Color(1f, 0.88f, 0.65f) * 1.2f, 1.8f, Vector3.down, 0.6f);
        AddLamp(new Vector3(ProjX0 + 0.6f, f + 2.0f, -16.0f), new Color(1f, 0.85f, 0.6f) * 0.9f, 1.8f);
        AddLamp(new Vector3(0.25f, Projector.y, -15.45f), new Color(1f, 0.8f, 0.5f) * 0.9f, 1.3f);
        var shade = new Vector3(3.5f, f + 1.7f, -16.5f);
        Lathe(_box, shade, new[] { new Vector2(0.2f, 0f), new Vector2(0.12f, 0.12f), new Vector2(0.02f, 0.16f), new Vector2(0.01f, ProjCeiling - f - 1.7f) }, new Color(0.25f, 0.45f, 0.3f), 16);
        Glow(shade + Vector3.down * 0.03f, 0.14f, new Color(1f, 0.92f, 0.75f, 1f), GlowGroup.Steady);
        Glow(new Vector3(ProjX0 + 0.6f, f + 2.0f, -16.0f), 0.15f, new Color(1f, 0.9f, 0.7f, 1f), GlowGroup.Flicker);
        Glow(new Vector3(ProjX0 + 0.6f, f + 2.0f, -16.0f), 0.8f, new Color(1f, 0.8f, 0.55f, 0.25f), GlowGroup.Flicker);
    }
}
