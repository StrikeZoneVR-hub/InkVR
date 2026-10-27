using InkVR.Rendering;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The Fun Hall, through the door at the foot of the lobby's right-hand stairs: a fairground hall of eight games played
/// with your hands (Ink Toss, the shooting gallery, darts, the strong man, two Ink Ball lanes, Whack-a-Searcher, the claw
/// machine and the ink machine pachinko), a prize counter, the high-score board and a photo booth. The games themselves
/// run in <see cref="Arcade"/>; this builds the room and the pieces that move.
/// </summary>
public sealed partial class Theatre
{
    private static float AF => LobbyFloor;

    // ---- where each game's pieces are (the games read these) ----
    public static Vector3 TossBoardAt => new(11.8f, AF + 1.75f, ArcadeZ1 - 0.25f);
    public const float TossBoardSize = 1.8f;
    public static Vector3 TossGrin => TossBoardAt + new Vector3(0, -0.4f, 0);
    public static Vector3 TossBall(int k) => new(11.3f + k * 0.18f, AF + 1.01f, -17.75f);
    public const float TossCounterZ = -17.9f;

    public static float GalleryX0 => 14.1f;
    public static float GalleryX1 => 17.5f;
    public static float GalleryZ => -15.6f;
    public static float GalleryRow(int row) => AF + 1.45f + row * 0.6f;
    public const int GalleryPerRow = 5;
    public static Vector3 CorkGunRest => new(15.8f, AF + 1.06f, -17.72f);

    public static Vector3 DartboardAt => new(19.6f, AF + 1.73f, ArcadeZ1 - 0.08f);
    public const float DartboardRadius = 0.23f;
    public static Vector3 DartRest(int k) => new(19.35f + k * 0.12f, AF + 1.03f, -17.62f);

    public static Vector3 StrongPad => new(21.25f, AF + 0.33f, -18.8f);
    public static float StrongBottom => AF + 0.5f;
    public static float StrongTop => AF + 4.45f;
    public static Vector3 StrongRail => new(22.0f, 0, -18.8f);
    public static Vector3 MalletRest => new(21.05f, AF + 0.02f, -19.55f);

    public static float SkeeLaneZ(int lane) => lane == 0 ? -24.6f : -23.4f;
    public const float SkeeStartX = 16.8f, SkeeRampX = 19.6f, SkeeLipX = 20.2f, SkeeTargetX0 = 20.4f, SkeeTargetX1 = 21.9f;
    public static float SkeeLaneY => AF + 0.8f;
    public static float SkeeLipY => SkeeLaneY + 0.2f;
    public static float SkeeTargetY0 => AF + 0.82f;
    public static float SkeeTargetY1 => AF + 1.9f;
    public const int SkeeBalls = 5;
    public static Vector3 SkeeBallRest(int lane, int k) => new(16.95f + (k % 3) * 0.13f, SkeeLaneY + 0.05f, SkeeLaneZ(lane) - 0.2f + (k / 3) * 0.13f);

    public static Vector3 WhackHole(int i) => new(19.1f + (i % 3) * 0.6f, AF + 0.85f, -26.6f + (i / 3) * 0.38f);
    public static Vector3 WhackMalletRest => new(20.95f, AF + 0.9f, -26.2f);

    public static Vector3 ClawBox0 => new(15.72f, AF + 0.92f, -26.82f);
    public static Vector3 ClawBox1 => new(16.88f, AF + 2.2f, -25.98f);
    public static Vector3 ClawStick => new(16.0f, AF + 0.92f, -25.72f);
    public static Vector3 ClawButton => new(16.55f, AF + 0.92f, -25.72f);
    public static Vector3 ClawChute => new(15.86f, AF + 0.95f, -26.1f);
    public static Vector3 ClawTray => new(15.95f, AF + 0.45f, -25.7f);
    public const int ClawPrizes = 6;

    public static float PachX0 => 12.7f;
    public static float PachX1 => 14.3f;
    public static float PachY0 => AF + 0.7f;
    public static float PachY1 => AF + 2.7f;
    public static float PachZ => ArcadeZ0 + 0.14f;
    public static Vector3 PachPlunger => new(14.2f, AF + 0.62f, ArcadeZ0 + 0.3f);

    public const int PrizeCount = 8;
    public static Vector3 PrizeAt(int i) => new(ArcadeX0 + 0.22f, AF + 1.38f + (i / 4) * 0.62f, -20.9f + (i % 4) * 0.95f);
    public static Vector3 PrizeCounterTop => new(9.0f, AF + 1.0f, -19.3f);

    public static Vector3 PhotoLens => new(9.2f, AF + 1.55f, ArcadeZ1 - 0.12f);
    public static Vector3 PhotoSeat => new(9.2f, AF, -16.2f);

    private void BuildArcade()
    {
        float f = AF, x0 = ArcadeX0, x1 = ArcadeX1, z0 = ArcadeZ0, z1 = ArcadeZ1, top = ArcadeCeiling;
        var door = new[] { new Vector4(SideDoorZ - SideDoorHalf, f - 1f, SideDoorZ + SideDoorHalf, f + SideDoorHeight) };
        var stripe = new Color(1f, 0.92f, 0.86f);
        // floor tiles, a striped tent of a room, a gold scallop round the top, a dark sky of a ceiling with lights strung
        FloorRect(_checker, x0, x1, z0, z1, f, new Color(0.95f, 0.9f, 0.82f), 2f);
        WallX(_wood, x0, z0, z1, f, f + 1.0f, Vector3.right, WoodDark, door, 1.6f, 1.6f);
        WallX(_tent, x0, z0, z1, f + 1.0f, top - 1.0f, Vector3.right, stripe, door, 2.4f, 2.4f);
        WallX(_wood, x1, z0, z1, f, f + 1.0f, Vector3.left, WoodDark, null, 1.6f, 1.6f);
        WallX(_tent, x1, z0, z1, f + 1.0f, top - 1.0f, Vector3.left, stripe, null, 2.4f, 2.4f);
        WallZ(_wood, z0, x0, x1, f, f + 1.0f, Vector3.forward, WoodDark, null, 1.6f, 1.6f);
        WallZ(_tent, z0, x0, x1, f + 1.0f, top - 1.0f, Vector3.forward, stripe, null, 2.4f, 2.4f);
        WallZ(_wood, z1, x0, x1, f, f + 1.0f, Vector3.back, WoodDark, null, 1.6f, 1.6f);
        WallZ(_tent, z1, x0, x1, f + 1.0f, top - 1.0f, Vector3.back, stripe, null, 2.4f, 2.4f);
        WallX(_painted, x0, z0, z1, top - 1.0f, top, Vector3.right, new Color(0.2f, 0.16f, 0.2f), null, 1f, 1f);
        WallX(_painted, x1, z0, z1, top - 1.0f, top, Vector3.left, new Color(0.2f, 0.16f, 0.2f), null, 1f, 1f);
        WallZ(_painted, z0, x0, x1, top - 1.0f, top, Vector3.forward, new Color(0.2f, 0.16f, 0.2f), null, 1f, 1f);
        WallZ(_painted, z1, x0, x1, top - 1.0f, top, Vector3.back, new Color(0.2f, 0.16f, 0.2f), null, 1f, 1f);
        CeilingRect(_painted, x0, x1, z0, z1, top, new Color(0.14f, 0.11f, 0.16f), 1f);
        foreach (var (a, b, n) in new[] { (new Vector3(x0 + 0.08f, top - 1.0f, z0), new Vector3(x0 + 0.08f, top - 1.0f, z1), Vector3.right), (new Vector3(x1 - 0.08f, top - 1.0f, z1), new Vector3(x1 - 0.08f, top - 1.0f, z0), Vector3.left),
                     (new Vector3(x1, top - 1.0f, z0 + 0.08f), new Vector3(x0, top - 1.0f, z0 + 0.08f), Vector3.forward), (new Vector3(x0, top - 1.0f, z1 - 0.08f), new Vector3(x1, top - 1.0f, z1 - 0.08f), Vector3.back) })
            Band(_ornament, a, b, Vector3.up, 0.35f, n, Gold, 1.4f);

        // strings of bulbs across the ceiling, chasing
        var bulb = 0;
        for (var k = 0; k < 5; k++)
        {
            var z = Mathf.Lerp(z0 + 1.5f, z1 - 1.5f, k / 4f);
            for (var x = x0 + 0.6f; x < x1 - 0.5f; x += 0.55f)
            {
                var sag = Mathf.Sin(Mathf.InverseLerp(x0, x1, x) * Mathf.PI) * 0.6f;
                var col = (bulb % 3) switch { 0 => new Color(1f, 0.85f, 0.5f, 1f), 1 => new Color(1f, 0.55f, 0.45f, 1f), _ => new Color(0.7f, 0.85f, 1f, 1f) };
                Glow(new Vector3(x, top - 0.25f - sag, z), 0.16f, col, GlowGroup.Chase, bulb++);
            }
        }

        // the doorway, both sides, and its signs
        Doorway(new Vector3(x0 + 0.002f, f, SideDoorZ), Vector3.back, Vector3.right, SideDoorHalf * 2, SideDoorHeight, false);
        Doorway(new Vector3(LobbyHalf - 0.002f, f, SideDoorZ), Vector3.forward, Vector3.left, SideDoorHalf * 2, SideDoorHeight, false);
        RevealX(new Vector4(SideDoorZ - SideDoorHalf, f, SideDoorZ + SideDoorHalf, f + SideDoorHeight), LobbyHalf, x0);
        var sign = new Vector3(LobbyHalf - 0.06f, f + 2.75f, SideDoorZ);
        Box(_box, sign, new Vector3(0.06f, 0.42f, 1.8f), Dark);
        Signs.Add(new SignSpec { Text = "THE FUN HALL", Pos = sign + Vector3.left * 0.04f, Rot = Quaternion.Euler(0, 90, 0), Height = 0.22f, Width = 1.7f, Color = new Color(1f, 0.84f, 0.5f) });
        for (var z = -0.85f; z <= 0.86f; z += 0.17f)
        {
            Glow(sign + new Vector3(-0.05f, 0.25f, z), 0.12f, new Color(1f, 0.85f, 0.55f, 1f), GlowGroup.Chase, bulb++);
            Glow(sign + new Vector3(-0.05f, -0.25f, z), 0.12f, new Color(1f, 0.85f, 0.55f, 1f), GlowGroup.Chase, bulb++);
        }

        var big = new Vector3(x1 - 0.08f, top - 1.55f, -21.0f);
        Signs.Add(new SignSpec { Text = "THE FUN HALL", Pos = big, Rot = Quaternion.Euler(0, 90, 0), Height = 0.7f, Width = 7f, Color = new Color(1f, 0.84f, 0.5f) });
        Signs.Add(new SignSpec { Text = "games of skill  -  win tickets  -  prizes!", Pos = big + Vector3.down * 0.55f, Rot = Quaternion.Euler(0, 90, 0), Height = 0.18f, Width = 6f, Color = Cream });

        BuildInkToss();
        BuildGallery();
        BuildDarts();
        BuildStrongMan();
        BuildSkee();
        BuildWhack();
        BuildClaw();
        BuildPachinko();
        BuildPrizeCounter();
        BuildScoreBoard();
        BuildPhotoBooth();
        PlanArcadeLamps();
    }

    /// <summary>The four faces lining a hole through a wall between x0 and x1 (z0, y0, z1, y1).</summary>
    private void RevealX(Vector4 h, float xa, float xb)
    {
        Patch(_box, new Vector3(xb, h.y, h.x), new Vector3(xa, h.y, h.x), new Vector3(xa, h.w, h.x), new Vector3(xb, h.w, h.x), Vector3.forward, WoodDark, 1, 1);
        Patch(_box, new Vector3(xa, h.y, h.z), new Vector3(xb, h.y, h.z), new Vector3(xb, h.w, h.z), new Vector3(xa, h.w, h.z), Vector3.back, WoodDark, 1, 1);
        Patch(_box, new Vector3(xa, h.w, h.x), new Vector3(xb, h.w, h.x), new Vector3(xb, h.w, h.z), new Vector3(xa, h.w, h.z), Vector3.down, WoodDark, 1, 1);
        Patch(_wood, new Vector3(xa, h.y + 0.004f, h.z), new Vector3(xb, h.y + 0.004f, h.z), new Vector3(xb, h.y + 0.004f, h.x), new Vector3(xa, h.y + 0.004f, h.x), Vector3.up, Wood, 1, 1);
    }

    /// <summary>A game's booth front: a counter, two striped posts and a header with its name in bulbs.</summary>
    private void GameBooth(Vector3 counterMid, float width, Vector3 facing, string name, float headerY, ref int bulb, bool counter = true)
    {
        var right = Vector3.Cross(Vector3.up, facing).normalized;
        var rot = Quaternion.LookRotation(-facing);
        if (counter)
        {
            Box(_wood, counterMid, Abs(right * width + Vector3.up * 0.06f + facing * 0.45f), new Color(0.78f, 0.55f, 0.32f));
            Box(_tent, counterMid + Vector3.down * 0.5f + facing * 0.18f, Abs(right * (width - 0.06f) + Vector3.up * 0.95f + facing * 0.06f), new Color(1f, 0.92f, 0.88f));
        }

        foreach (var s in new[] { -1f, 1f })
        {
            var post = counterMid + right * (s * width * 0.5f) + Vector3.up * (headerY - counterMid.y) * 0.5f;
            Box(_tent, post, Abs(right * 0.12f + Vector3.up * (headerY - counterMid.y + 1.0f) + facing * 0.12f), new Color(1f, 0.9f, 0.85f));
        }

        var header = new Vector3(counterMid.x, headerY, counterMid.z) - facing * 0.15f;
        Box(_box, header, Abs(right * (width + 0.2f) + Vector3.up * 0.4f + facing * 0.08f), Dark);
        Signs.Add(new SignSpec { Text = name, Pos = header + facing * 0.05f, Rot = rot, Height = 0.22f, Width = width, Color = new Color(1f, 0.84f, 0.5f) });
        for (var t = -0.5f; t <= 0.501f; t += 0.07f)
        {
            Glow(header + right * (t * (width + 0.1f)) + Vector3.up * 0.23f + facing * 0.06f, 0.11f, new Color(1f, 0.86f, 0.55f, 1f), GlowGroup.Chase, bulb++);
            Glow(header + right * (t * (width + 0.1f)) - Vector3.up * 0.23f + facing * 0.06f, 0.11f, new Color(1f, 0.86f, 0.55f, 1f), GlowGroup.Chase, bulb++);
        }
    }

    private int _arcadeBulb = 500;

    // =====================================================================================================
    // The games
    // =====================================================================================================

    private void BuildInkToss()
    {
        var b = TossBoardAt;
        var h = TossBoardSize * 0.5f;
        // the board, Bendy's face; behind the grin a dark cavity the balls fall into
        Box(_wood, b + Vector3.forward * 0.06f, new Vector3(TossBoardSize + 0.1f, TossBoardSize + 0.1f, 0.06f), WoodDark);
        var p = NewPart("TossBoard", TheatreArt.TossBoard(), false, 2001);
        Patch(p, b + new Vector3(-h, -h, 0), b + new Vector3(h, -h, 0), b + new Vector3(h, h, 0), b + new Vector3(-h, h, 0), Vector3.back, new Color(1.1f, 1.1f, 1.1f), 4, 4);
        Box(_box, TossGrin + Vector3.forward * 0.3f, new Vector3(1.0f, 0.42f, 0.5f), Dark * 0.5f);
        GameBooth(new Vector3(b.x, AF + 0.97f, TossCounterZ + 0.22f), 2.6f, Vector3.back, "INK TOSS", AF + 2.95f, ref _arcadeBulb);
        for (var k = 0; k < 3; k++)
        {
            var at = TossBall(k);
            InGroup("TossBall" + k, at, () =>
            {
                var mb = new MeshBuilder();
                mb.Ellipsoid(at, Vector3.one * 0.055f, Quaternion.identity, 14, 10);
                AddBuilder(_painted, mb, Dark * 2.4f);
            });
        }

        Signs.Add(new SignSpec { Text = "3 balls  -  into the grin!", Pos = new Vector3(b.x, AF + 0.75f, TossCounterZ - 0.01f), Rot = Quaternion.identity, Height = 0.07f, Width = 2.2f, Color = Cream, Id = "TossInfo" });
    }

    private void BuildGallery()
    {
        float x0 = GalleryX0, x1 = GalleryX1, mid = (x0 + x1) * 0.5f;
        // the painted backdrop and the two rails the targets run along
        var p = NewPart("GalleryBack", TheatreArt.Gallery(), false, 2001);
        Patch(p, new Vector3(x0, AF + 1.0f, ArcadeZ1 - 0.03f), new Vector3(x1, AF + 1.0f, ArcadeZ1 - 0.03f), new Vector3(x1, AF + 3.0f, ArcadeZ1 - 0.03f), new Vector3(x0, AF + 3.0f, ArcadeZ1 - 0.03f), Vector3.back, new Color(1.05f, 1.05f, 1.05f), 6, 3);
        for (var row = 0; row < 2; row++)
        {
            var y = GalleryRow(row);
            Box(_plate, new Vector3(mid, y - 0.2f, GalleryZ + 0.04f), new Vector3(x1 - x0, 0.06f, 0.1f), Iron * 1.5f);
            for (var k = 0; k < GalleryPerRow; k++)
            {
                var i = row * GalleryPerRow + k;
                var at = new Vector3(x0 + (k + 0.5f) * (x1 - x0) / GalleryPerRow, y - 0.2f, GalleryZ);
                var face = (i * 3 + row) % 4;
                InGroup("GalTarget" + i, at, () =>
                {
                    Box(_plate, at + Vector3.up * 0.1f, new Vector3(0.025f, 0.2f, 0.025f), Iron * 1.6f);
                    var tp = TargetPart();
                    var c = at + Vector3.up * 0.32f;
                    Patch(tp, c + new Vector3(0.13f, -0.13f, -0.01f), c + new Vector3(-0.13f, -0.13f, -0.01f), c + new Vector3(-0.13f, 0.13f, -0.01f), c + new Vector3(0.13f, 0.13f, -0.01f), Vector3.back, new Color(1.1f, 1.1f, 1.1f), 1, 1,
                        uvRect: new Vector4((face + 1) * 0.25f, 0, face * 0.25f, 1));
                });
            }
        }

        GameBooth(new Vector3(mid, AF + 0.97f, TossCounterZ + 0.22f), x1 - x0 + 0.1f, Vector3.back, "SHOOTING GALLERY", AF + 3.3f, ref _arcadeBulb);
        // the cork gun, on a chain to the counter
        var g = CorkGunRest;
        InGroup("CorkGun", g, () =>
        {
            var gun = new MeshBuilder();
            gun.Tube(new[] { g + new Vector3(0, 0, -0.05f), g + new Vector3(0, 0, 0.42f) }, new[] { 0.018f, 0.015f }, Vector3.up, 12, true, true); // the barrel
            gun.Box(g + new Vector3(0, -0.02f, -0.12f), new Vector3(0.05f, 0.08f, 0.2f), Quaternion.identity); // the stock
            gun.Box(g + new Vector3(0, -0.07f, 0.0f), new Vector3(0.035f, 0.1f, 0.04f), Quaternion.Euler(-20, 0, 0)); // the grip
            AddBuilder(_wood, gun, new Color(0.62f, 0.42f, 0.26f));
            var cork = new MeshBuilder();
            cork.Ellipsoid(g + new Vector3(0, 0, 0.43f), new Vector3(0.016f, 0.016f, 0.02f), Quaternion.identity, 10, 6);
            AddBuilder(_crate, cork, new Color(0.85f, 0.7f, 0.5f));
        });
        Signs.Add(new SignSpec { Text = "10 corks  -  pull the trigger  -  don't hit Bendy!", Pos = new Vector3(mid, AF + 0.75f, TossCounterZ - 0.01f), Rot = Quaternion.identity, Height = 0.065f, Width = 3f, Color = Cream, Id = "GalleryInfo" });
    }

    private Part _targetPart;

    private Part TargetPart() => _targetPart ??= NewPart("Targets", TheatreArt.Targets(), false, 2001);

    private void BuildDarts()
    {
        var d = DartboardAt;
        var r = DartboardRadius * 256f / 230f;
        Lathe(_wood, d + Vector3.forward * 0.04f, new[] { new Vector2(r + 0.06f, 0f), new Vector2(r + 0.06f, 0.04f), new Vector2(0f, 0.04f) }, WoodDark, 32);
        var p = NewPart("Dartboard", TheatreArt.Dartboard(), false, 2001);
        var face = new MeshBuilder();
        face.Disc(d, new Vector3(r, 0, 0), new Vector3(0, r, 0), 48);
        AddBuilder(p, face, new Color(1.1f, 1.1f, 1.1f));
        GameBooth(new Vector3(d.x, AF + 0.97f, TossCounterZ + 0.4f), 1.8f, Vector3.back, "DARTS", AF + 2.95f, ref _arcadeBulb);
        for (var k = 0; k < 3; k++)
        {
            var at = DartRest(k);
            InGroup("Dart" + k, at, () =>
            {
                var dart = new MeshBuilder();
                dart.Tube(new[] { at + new Vector3(0, 0, -0.07f), at + new Vector3(0, 0, 0.05f), at + new Vector3(0, 0, 0.09f) }, new[] { 0.007f, 0.008f, 0.001f }, Vector3.up, 8, true, true);
                foreach (var a in new[] { 0f, 90f }) dart.Box(at + new Vector3(0, 0, -0.08f), new Vector3(0.04f, 0.002f, 0.05f), Quaternion.Euler(0, 0, a));
                AddBuilder(_plate, dart, new Color(0.8f, 0.75f, 0.6f));
            });
        }

        Signs.Add(new SignSpec { Text = "3 darts  -  throw from the line", Pos = new Vector3(d.x, AF + 0.75f, TossCounterZ + 0.17f), Rot = Quaternion.identity, Height = 0.065f, Width = 1.7f, Color = Cream, Id = "DartsInfo" });
    }

    private void BuildStrongMan()
    {
        var rail = StrongRail;
        float z = rail.z, f = AF;
        // the base and its pad, the tower with its scale, the bell at the top
        Box(_wood, new Vector3(21.55f, f + 0.15f, z), new Vector3(1.1f, 0.3f, 1.0f), new Color(0.7f, 0.25f, 0.2f));
        Lathe(_plate, StrongPad + Vector3.down * 0.05f, new[] { new Vector2(0.19f, 0f), new Vector2(0.19f, 0.05f), new Vector2(0f, 0.06f) }, new Color(0.85f, 0.2f, 0.15f), 20);
        Box(_wood, new Vector3(22.12f, f + 2.5f, z), new Vector3(0.16f, 4.6f, 0.5f), new Color(0.85f, 0.75f, 0.55f));
        var sp = NewPart("StrongScale", TheatreArt.StrongScale(), false, 2001);
        Patch(sp, new Vector3(22.03f, StrongBottom, z + 0.2f), new Vector3(22.03f, StrongBottom, z - 0.2f), new Vector3(22.03f, StrongTop, z - 0.2f), new Vector3(22.03f, StrongTop, z + 0.2f), Vector3.left, new Color(1.05f, 1.05f, 1.05f), 1, 6);
        Box(_plate, new Vector3(rail.x, (StrongBottom + StrongTop) * 0.5f, z), new Vector3(0.03f, StrongTop - StrongBottom, 0.03f), Iron * 1.6f);
        var bellAt = new Vector3(rail.x - 0.02f, StrongTop + 0.15f, z);
        Lathe(_box, bellAt, new[] { new Vector2(0.2f, 0f), new Vector2(0.19f, 0.06f), new Vector2(0.13f, 0.18f), new Vector2(0.05f, 0.24f), new Vector2(0f, 0.25f) }, Gold * 1.1f, 20);
        Glow(bellAt + Vector3.up * 0.1f, 0.9f, new Color(1f, 0.85f, 0.5f, 0.25f), GlowGroup.Flicker);
        InGroup("Puck", new Vector3(rail.x, StrongBottom, z), () => Box(_plate, new Vector3(rail.x - 0.05f, StrongBottom, z), new Vector3(0.12f, 0.08f, 0.12f), new Color(0.2f, 0.2f, 0.2f)));
        // the mallet, standing on its head by the base
        var m = MalletRest;
        InGroup("Mallet", m, () =>
        {
            var mb = new MeshBuilder();
            mb.Tube(new[] { m + Vector3.up * 0.1f, m + Vector3.up * 0.95f }, new[] { 0.022f, 0.02f }, Vector3.forward, 10, false, true); // the handle
            Drum(m + new Vector3(0, 0.06f, -0.14f), m + new Vector3(0, 0.06f, 0.14f), 0.085f, 16, mb); // the head
            AddBuilder(_wood, mb, new Color(0.72f, 0.5f, 0.3f));
        });
        var top = new Vector3(21.3f, StrongTop + 0.75f, z);
        Box(_box, top + Vector3.right * 0.7f, new Vector3(0.08f, 0.45f, 1.7f), Dark);
        Signs.Add(new SignSpec { Text = "THE STRONG MAN", Pos = top + Vector3.right * 0.65f, Rot = Quaternion.Euler(0, 90, 0), Height = 0.24f, Width = 1.6f, Color = new Color(1f, 0.84f, 0.5f) });
        Signs.Add(new SignSpec { Text = "ring the bell!", Pos = new Vector3(21.0f, f + 0.6f, z - 0.51f), Rot = Quaternion.identity, Height = 0.08f, Width = 1f, Color = Cream, Id = "StrongInfo" });
    }

    private void BuildSkee()
    {
        var skee = NewPart("Skee", TheatreArt.SkeeTarget(), false, 2001);
        for (var lane = 0; lane < 2; lane++)
        {
            var z = SkeeLaneZ(lane);
            float y = SkeeLaneY, half = 0.36f;
            // the lane, its ramp and lip, the tilted target face, rails either side
            Box(_wood, new Vector3((SkeeStartX + SkeeRampX) * 0.5f, y - 0.4f, z), new Vector3(SkeeRampX - SkeeStartX, 0.8f, half * 2 + 0.12f), new Color(0.75f, 0.3f, 0.25f));
            Patch(_wood, new Vector3(SkeeStartX, y, z + half), new Vector3(SkeeStartX, y, z - half), new Vector3(SkeeRampX, y, z - half), new Vector3(SkeeRampX, y, z + half), Vector3.up, Wood * 1.1f, 2, 6, 1.2f, 1.2f);
            Patch(_wood, new Vector3(SkeeRampX, y, z + half), new Vector3(SkeeRampX, y, z - half), new Vector3(SkeeLipX, SkeeLipY, z - half), new Vector3(SkeeLipX, SkeeLipY, z + half), new Vector3(-0.3f, 1f, 0).normalized, Wood, 1, 2, 1.2f, 1.2f);
            Patch(skee, new Vector3(SkeeTargetX0, SkeeTargetY0, z + half), new Vector3(SkeeTargetX0, SkeeTargetY0, z - half), new Vector3(SkeeTargetX1, SkeeTargetY1, z - half), new Vector3(SkeeTargetX1, SkeeTargetY1, z + half),
                new Vector3(-0.6f, 0.8f, 0).normalized, new Color(1.1f, 1.1f, 1.1f), 2, 4, uvRect: new Vector4(0, 0, 1, 1));
            Box(_wood, new Vector3((SkeeTargetX0 + SkeeTargetX1) * 0.5f, AF + 0.4f, z), new Vector3(SkeeTargetX1 - SkeeTargetX0 + 0.3f, 0.8f, half * 2 + 0.12f), new Color(0.75f, 0.3f, 0.25f));
            foreach (var s in new[] { -1f, 1f })
            {
                Box(_wood, new Vector3((SkeeStartX + SkeeTargetX1) * 0.5f, y + 0.12f, z + s * (half + 0.04f)), new Vector3(SkeeTargetX1 - SkeeStartX, 0.24f, 0.06f), new Color(0.85f, 0.75f, 0.55f));
                Box(_box, new Vector3(SkeeTargetX1 - 0.4f, AF + 2.0f, z + s * (half + 0.04f)), new Vector3(0.9f, 1.2f, 0.05f), new Color(0.85f, 0.75f, 0.55f));
            }

            for (var k = 0; k < SkeeBalls; k++)
            {
                var at = SkeeBallRest(lane, k);
                InGroup($"Skee{lane}_{k}", at, () =>
                {
                    var mb = new MeshBuilder();
                    mb.Ellipsoid(at, Vector3.one * 0.05f, Quaternion.identity, 14, 10);
                    AddBuilder(_painted, mb, new Color(0.9f, 0.85f, 0.75f));
                });
            }

            Signs.Add(new SignSpec { Text = "SCORE 0", Pos = new Vector3(SkeeTargetX1 - 0.1f, AF + 2.45f, z), Rot = Quaternion.Euler(0, 90, 0), Height = 0.13f, Width = 0.7f, Color = new Color(1f, 0.84f, 0.5f), Id = "SkeeScore" + lane });
            Box(_box, new Vector3(SkeeTargetX1 - 0.04f, AF + 2.45f, z), new Vector3(0.05f, 0.25f, 0.74f), Dark);
        }

        var sign = new Vector3(ArcadeX1 - 0.08f, AF + 3.2f, (SkeeLaneZ(0) + SkeeLaneZ(1)) * 0.5f);
        Signs.Add(new SignSpec { Text = "INK BALL", Pos = sign, Rot = Quaternion.Euler(0, 90, 0), Height = 0.3f, Width = 2f, Color = new Color(1f, 0.84f, 0.5f) });
        Signs.Add(new SignSpec { Text = "roll them up the lane", Pos = new Vector3(SkeeStartX - 0.01f, SkeeLaneY - 0.25f, (SkeeLaneZ(0) + SkeeLaneZ(1)) * 0.5f), Rot = Quaternion.Euler(0, 90, 0), Height = 0.06f, Width = 1.6f, Color = Cream });
    }

    private void BuildWhack()
    {
        // a cabinet: six holes in its top, a Searcher in each (they come up: Arcade), a mallet on a hook
        var c = new Vector3(19.7f, AF, -26.42f);
        Box(_wood, c + new Vector3(0, 0.42f, 0), new Vector3(2.0f, 0.84f, 1.0f), new Color(0.3f, 0.45f, 0.35f));
        Box(_box, c + new Vector3(0, 0.86f, 0), new Vector3(2.06f, 0.04f, 1.06f), Gold * 0.8f);
        for (var i = 0; i < 6; i++)
        {
            var h = WhackHole(i);
            Ring(_box, h + Vector3.up * 0.02f, 0.13f, 0.02f, Dark * 2f, 24, 4);
            Patch(_box, h + new Vector3(-0.12f, 0.025f, -0.12f), h + new Vector3(0.12f, 0.025f, -0.12f), h + new Vector3(0.12f, 0.025f, 0.12f), h + new Vector3(-0.12f, 0.025f, 0.12f), Vector3.up, Dark * 0.4f, 1, 1, unlit: true);
            InGroup("Searcher" + i, h, () =>
            {
                var head = new MeshBuilder();
                head.Ellipsoid(h + Vector3.up * 0.0f, new Vector3(0.1f, 0.13f, 0.1f), Quaternion.identity, 14, 10);
                for (var k = 0; k < 4; k++) head.Ellipsoid(h + new Vector3(Mathf.Cos(k * 1.7f) * 0.07f, 0.06f + k * 0.015f, Mathf.Sin(k * 1.7f) * 0.07f), Vector3.one * 0.04f, Quaternion.identity, 8, 6);
                AddBuilder(_painted, head, Dark * 1.6f);
                var eyes = new MeshBuilder();
                foreach (var s in new[] { -1f, 1f }) eyes.Ellipsoid(h + new Vector3(s * 0.035f, 0.03f, 0.09f), new Vector3(0.018f, 0.026f, 0.01f), Quaternion.identity, 8, 6);
                AddBuilder(_painted, eyes, Cream * 1.2f);
            });
        }

        var m = WhackMalletRest;
        InGroup("WhackMallet", m, () =>
        {
            var mb = new MeshBuilder();
            mb.Tube(new[] { m, m + Vector3.up * 0.42f }, new[] { 0.016f, 0.015f }, Vector3.forward, 10, true, true);
            Drum(m + new Vector3(-0.09f, 0.45f, 0), m + new Vector3(0.09f, 0.45f, 0), 0.06f, 16, mb);
            AddBuilder(_painted, mb, new Color(0.85f, 0.2f, 0.15f));
        });
        GameBooth(new Vector3(c.x, AF + 0.97f, -25.86f), 2.1f, Vector3.forward, "WHACK-A-SEARCHER", AF + 2.7f, ref _arcadeBulb, false);
        Signs.Add(new SignSpec { Text = "press START", Pos = new Vector3(c.x + 0.75f, AF + 0.92f, -25.92f), Rot = Quaternion.Euler(0, 180, 0), Height = 0.06f, Width = 0.6f, Color = Cream, Id = "WhackInfo" });
        InGroup("WhackStart", new Vector3(c.x + 0.75f, AF + 0.89f, -26.1f), () => Lathe(_box, new Vector3(c.x + 0.75f, AF + 0.86f, -26.1f), new[] { new Vector2(0.06f, 0f), new Vector2(0.055f, 0.04f), new Vector2(0f, 0.045f) }, new Color(0.9f, 0.2f, 0.15f), 14));
    }

    private void BuildClaw()
    {
        var a = ClawBox0;
        var b = ClawBox1;
        var mid = (a + b) * 0.5f;
        // the cabinet: a base, glass sides (faint), a lit top, the controls on the front
        Box(_wood, new Vector3(mid.x, AF + 0.45f, mid.z), new Vector3(b.x - a.x + 0.1f, 0.9f, b.z - a.z + 0.1f), new Color(0.25f, 0.3f, 0.55f));
        Box(_wood, new Vector3(mid.x, b.y + 0.2f, mid.z), new Vector3(b.x - a.x + 0.1f, 0.4f, b.z - a.z + 0.1f), new Color(0.25f, 0.3f, 0.55f));
        foreach (var (x, z) in new[] { (a.x, a.z), (b.x, a.z), (a.x, b.z), (b.x, b.z) }) Box(_plate, new Vector3(x, (a.y + b.y) * 0.5f, z), new Vector3(0.04f, b.y - a.y, 0.04f), Gold * 0.7f);
        Signs.Add(new SignSpec { Text = "THE CLAW", Pos = new Vector3(mid.x, b.y + 0.2f, b.z + 0.06f), Rot = Quaternion.Euler(0, 180, 0), Height = 0.18f, Width = 1f, Color = new Color(1f, 0.84f, 0.5f) });
        var glass = NewPart("ClawGlass", Textures.SoftDot, true, 3010);
        var gc = new Color(0.75f, 0.85f, 0.9f, 0.12f);
        _noShadows = true;
        Patch(glass, new Vector3(b.x, a.y, b.z), new Vector3(a.x, a.y, b.z), new Vector3(a.x, b.y, b.z), new Vector3(b.x, b.y, b.z), Vector3.forward, gc, 1, 1, unlit: true, uvRect: new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
        Patch(glass, new Vector3(a.x, a.y, a.z), new Vector3(a.x, a.y, b.z), new Vector3(a.x, b.y, b.z), new Vector3(a.x, b.y, a.z), Vector3.left, gc, 1, 1, unlit: true, uvRect: new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
        Patch(glass, new Vector3(b.x, a.y, b.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, b.y, a.z), new Vector3(b.x, b.y, b.z), Vector3.right, gc, 1, 1, unlit: true, uvRect: new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
        _noShadows = false;
        // the prizes: little plush heads (Bendy, Boris, Alice) in a heap
        for (var i = 0; i < ClawPrizes; i++)
        {
            var at = new Vector3(a.x + 0.3f + (i % 3) * 0.28f, a.y + 0.08f, a.z + 0.25f + (i / 3) * 0.3f);
            var kind = i % 3;
            InGroup("ClawPrize" + i, at, () => Plush(at, kind, 0.07f));
        }

        // the claw, on its gantry, and its controls
        var top = b.y - 0.05f;
        Box(_plate, new Vector3(mid.x, top, mid.z), new Vector3(b.x - a.x, 0.03f, 0.04f), Iron * 1.5f);
        InGroup("Claw", new Vector3(mid.x, top, mid.z), () =>
        {
            var mb = new MeshBuilder();
            mb.Tube(new[] { new Vector3(mid.x, top, mid.z), new Vector3(mid.x, top - 0.25f, mid.z) }, new[] { 0.006f, 0.006f }, Vector3.forward, 6, false, false);
            mb.Ellipsoid(new Vector3(mid.x, top - 0.27f, mid.z), new Vector3(0.04f, 0.03f, 0.04f), Quaternion.identity, 10, 6);
            for (var k = 0; k < 3; k++)
            {
                var ang = k / 3f * Mathf.PI * 2;
                var d = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                mb.Tube(new[] { new Vector3(mid.x, top - 0.28f, mid.z) + d * 0.03f, new Vector3(mid.x, top - 0.33f, mid.z) + d * 0.07f, new Vector3(mid.x, top - 0.39f, mid.z) + d * 0.04f }, new[] { 0.006f, 0.006f, 0.004f }, Vector3.up, 6, true, true);
            }

            AddBuilder(_plate, mb, Gold * 0.9f);
        });
        var stick = ClawStick;
        Lathe(_plate, stick, new[] { new Vector2(0.05f, 0f), new Vector2(0.03f, 0.02f), new Vector2(0f, 0.025f) }, Iron * 1.5f, 12);
        InGroup("ClawStick", stick, () =>
        {
            var mb = new MeshBuilder();
            mb.Tube(new[] { stick, stick + Vector3.up * 0.12f }, new[] { 0.008f, 0.008f }, Vector3.forward, 8, false, false);
            mb.Ellipsoid(stick + Vector3.up * 0.14f, Vector3.one * 0.025f, Quaternion.identity, 12, 8);
            AddBuilder(_painted, mb, new Color(0.85f, 0.2f, 0.15f));
        });
        InGroup("ClawButton", ClawButton, () => Lathe(_box, ClawButton, new[] { new Vector2(0.045f, 0f), new Vector2(0.04f, 0.03f), new Vector2(0f, 0.035f) }, new Color(0.95f, 0.75f, 0.2f), 14));
        Box(_box, ClawTray + Vector3.down * 0.05f, new Vector3(0.3f, 0.04f, 0.2f), Dark);
        Signs.Add(new SignSpec { Text = "move  -  then press", Pos = new Vector3(mid.x, AF + 0.75f, b.z + 0.06f), Rot = Quaternion.Euler(0, 180, 0), Height = 0.05f, Width = 1f, Color = Cream });
    }

    /// <summary>A little plush head: Bendy (0), Boris (1) or Alice (2).</summary>
    private void Plush(Vector3 at, int kind, float r)
    {
        var body = new MeshBuilder();
        body.Ellipsoid(at, new Vector3(r, r * 0.95f, r), Quaternion.identity, 14, 10);
        var col = kind == 1 ? new Color(0.78f, 0.77f, 0.74f) : Dark * 2.2f;
        if (kind == 0 || kind == 2)
        {
            foreach (var s in new[] { -1f, 1f })
                body.Tube(new[] { at + new Vector3(s * r * 0.5f, r * 0.7f, 0), at + new Vector3(s * r * 0.9f, r * 1.3f, 0), at + new Vector3(s * r * 0.75f, r * 1.6f, 0) }, new[] { r * 0.25f, r * 0.12f, r * 0.02f }, Vector3.forward, 8, true, true);
        }
        else
        {
            foreach (var s in new[] { -1f, 1f }) body.Ellipsoid(at + new Vector3(s * r * 0.85f, r * 0.2f, 0), new Vector3(r * 0.25f, r * 0.6f, r * 0.3f), Quaternion.Euler(0, 0, s * 20), 10, 6);
        }

        AddBuilder(_fabric, body, col);
        var face = new MeshBuilder();
        face.Ellipsoid(at + new Vector3(0, -r * 0.1f, r * 0.55f), new Vector3(r * 0.7f, r * 0.55f, r * 0.45f), Quaternion.identity, 12, 8);
        AddBuilder(_fabric, face, kind == 1 ? new Color(0.92f, 0.9f, 0.86f) : Cream);
        var eyes = new MeshBuilder();
        foreach (var s in new[] { -1f, 1f }) eyes.Ellipsoid(at + new Vector3(s * r * 0.25f, r * 0.05f, r * 0.95f), new Vector3(r * 0.12f, r * 0.18f, r * 0.05f), Quaternion.identity, 8, 6);
        AddBuilder(_box, eyes, Dark);
        if (kind == 2)
        {
            var halo = new MeshBuilder();
            halo.Torus(at + Vector3.up * r * 1.45f, Vector3.up, r * 0.6f, r * 0.08f, 16, 4);
            AddBuilder(_box, halo, Gold * 1.2f);
        }
    }

    private void BuildPachinko()
    {
        float x0 = PachX0, x1 = PachX1, y0 = PachY0, y1 = PachY1, z = PachZ;
        Box(_wood, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f + 0.1f, z - 0.06f), new Vector3(x1 - x0 + 0.3f, y1 - y0 + 0.5f, 0.12f), new Color(0.55f, 0.3f, 0.2f));
        Box(_wood, new Vector3((x0 + x1) * 0.5f, AF + 0.35f, z + 0.15f), new Vector3(x1 - x0 + 0.3f, 0.7f, 0.42f), new Color(0.55f, 0.3f, 0.2f));
        var p = NewPart("Pachinko", TheatreArt.Pachinko(), false, 2001);
        Patch(p, new Vector3(x1, y0, z), new Vector3(x0, y0, z), new Vector3(x0, y1, z), new Vector3(x1, y1, z), Vector3.forward, new Color(1.08f, 1.08f, 1.08f), 4, 5);
        // the pins (brass), the slot walls, the launch channel up the right
        var pins = new MeshBuilder();
        foreach (var pin in PachPins()) pins.Tube(new[] { new Vector3(pin.x, pin.y, z), new Vector3(pin.x, pin.y, z + 0.04f) }, new[] { 0.007f, 0.007f }, Vector3.up, 6, false, true);
        AddBuilder(_plate, pins, Gold * 1.05f);
        for (var i = 0; i <= 7; i++)
        {
            var x = Mathf.Lerp(x0, x1 - 0.12f, i / 7f);
            Box(_wood, new Vector3(x, y0 + 0.12f, z + 0.02f), new Vector3(0.012f, 0.24f, 0.04f), WoodDark);
        }

        Box(_wood, new Vector3(x1 - 0.12f, (y0 + y1) * 0.5f - 0.15f, z + 0.02f), new Vector3(0.015f, y1 - y0 - 0.3f, 0.04f), WoodDark);
        var glass = NewPart("PachGlass", Textures.SoftDot, true, 3011);
        _noShadows = true;
        Patch(glass, new Vector3(x0, y0, z + 0.05f), new Vector3(x1, y0, z + 0.05f), new Vector3(x1, y1, z + 0.05f), new Vector3(x0, y1, z + 0.05f), Vector3.forward, new Color(0.8f, 0.9f, 0.95f, 0.08f), 1, 1, unlit: true, uvRect: new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
        _noShadows = false;
        var pl = PachPlunger;
        InGroup("Plunger", pl, () =>
        {
            var mb = new MeshBuilder();
            mb.Tube(new[] { pl, pl + Vector3.forward * 0.12f }, new[] { 0.01f, 0.01f }, Vector3.up, 8, false, false);
            Drum(pl + Vector3.forward * 0.12f, pl + Vector3.forward * 0.15f, 0.035f, 14, mb);
            AddBuilder(_plate, mb, new Color(0.85f, 0.2f, 0.15f));
        });
        for (var k = 0; k < 4; k++)
        {
            var at = new Vector3(x1 + 0.05f, y0 - 0.18f, z + 0.1f + k * 0.05f);
            InGroup("PachBall" + k, at, () =>
            {
                var mb = new MeshBuilder();
                mb.Ellipsoid(at, Vector3.one * 0.02f, Quaternion.identity, 10, 6);
                AddBuilder(_plate, mb, new Color(0.85f, 0.85f, 0.88f));
            });
        }

        Signs.Add(new SignSpec { Text = "INK MACHINE PACHINKO", Pos = new Vector3((x0 + x1) * 0.5f, y1 + 0.24f, z + 0.01f), Rot = Quaternion.Euler(0, 180, 0), Height = 0.12f, Width = 1.7f, Color = new Color(1f, 0.84f, 0.5f) });
        Signs.Add(new SignSpec { Text = "pull back and let go", Pos = new Vector3((x0 + x1) * 0.5f, AF + 0.55f, z + 0.37f), Rot = Quaternion.Euler(0, 180, 0), Height = 0.05f, Width = 1.4f, Color = Cream, Id = "PachInfo" });
    }

    /// <summary>The pachinko board's pins (x, y in the board's plane), in staggered rows.</summary>
    public static System.Collections.Generic.IEnumerable<Vector2> PachPins()
    {
        float x0 = PachX0 + 0.08f, x1 = PachX1 - 0.2f;
        for (var row = 0; row < 11; row++)
        {
            var y = PachY1 - 0.32f - row * 0.13f;
            var offset = row % 2 == 0 ? 0f : 0.06f;
            for (var x = x0 + offset; x <= x1; x += 0.12f) yield return new Vector2(x, y);
        }
    }

    private void BuildPrizeCounter()
    {
        var c = PrizeCounterTop;
        Box(_wood, new Vector3(c.x, AF + 0.5f, c.z), new Vector3(0.7f, 1.0f, 4.0f), new Color(0.55f, 0.3f, 0.2f));
        Box(_box, new Vector3(c.x, AF + 1.02f, c.z), new Vector3(0.78f, 0.04f, 4.08f), Gold);
        Box(_box, new Vector3(ArcadeX0 + 0.04f, AF + 2.2f, c.z), new Vector3(0.08f, 1.9f, 4.0f), new Color(0.3f, 0.22f, 0.18f));
        foreach (var y in new[] { AF + 1.3f, AF + 1.92f }) Box(_box, new Vector3(ArcadeX0 + 0.22f, y, c.z), new Vector3(0.36f, 0.04f, 4.0f), Gold * 0.8f);
        var names = Arcade.PrizeNames;
        for (var i = 0; i < PrizeCount; i++)
        {
            var at = PrizeAt(i);
            var k = i;
            InGroup("Prize" + i, at, () => PrizeModel(k, at));
            Signs.Add(new SignSpec { Text = names[i], Pos = at + new Vector3(0.19f, -0.06f, 0), Rot = Quaternion.Euler(0, -90, 0), Height = 0.035f, Width = 0.6f, Color = Cream });
            Signs.Add(new SignSpec { Text = Arcade.PrizeCost[i] + " TICKETS", Pos = at + new Vector3(0.19f, -0.1f, 0), Rot = Quaternion.Euler(0, -90, 0), Height = 0.03f, Width = 0.6f, Color = new Color(1f, 0.84f, 0.5f), Id = "PrizeTag" + i });
        }

        var sign = new Vector3(ArcadeX0 + 0.1f, AF + 3.45f, c.z);
        Box(_box, sign, new Vector3(0.06f, 0.4f, 2.6f), Dark);
        Signs.Add(new SignSpec { Text = "PRIZES", Pos = sign + Vector3.right * 0.04f, Rot = Quaternion.Euler(0, -90, 0), Height = 0.25f, Width = 2.4f, Color = new Color(1f, 0.84f, 0.5f) });
        Signs.Add(new SignSpec { Text = "YOUR TICKETS: 0", Pos = new Vector3(c.x + 0.36f, AF + 0.75f, c.z), Rot = Quaternion.Euler(0, -90, 0), Height = 0.12f, Width = 2.2f, Color = new Color(1f, 0.84f, 0.5f), Id = "TicketCount" });
        Signs.Add(new SignSpec { Text = "take a prize off the shelf", Pos = new Vector3(c.x + 0.36f, AF + 0.55f, c.z), Rot = Quaternion.Euler(0, -90, 0), Height = 0.05f, Width = 2.2f, Color = Cream });
    }

    /// <summary>A prize on the shelf: plushes, a balloon, a party hat, a gold laser pen, a golden Bendy, an ink-stained bow.</summary>
    private void PrizeModel(int i, Vector3 at)
    {
        switch (i)
        {
            case 0:
            case 1:
            case 2:
                Plush(at + Vector3.up * 0.1f, i, 0.1f);
                break;
            case 3: // a Bendy balloon
            {
                var mb = new MeshBuilder();
                mb.Ellipsoid(at + Vector3.up * 0.28f, new Vector3(0.12f, 0.14f, 0.12f), Quaternion.identity, 16, 10);
                foreach (var s in new[] { -1f, 1f }) mb.Tube(new[] { at + new Vector3(0, 0.36f, s * 0.07f), at + new Vector3(0, 0.45f, s * 0.12f), at + new Vector3(0, 0.5f, s * 0.1f) }, new[] { 0.03f, 0.015f, 0.002f }, Vector3.right, 8, true, true);
                mb.Tube(new[] { at + Vector3.up * 0.14f, at }, new[] { 0.003f, 0.003f }, Vector3.right, 4, false, false);
                AddBuilder(_painted, mb, Dark * 2.2f);
                break;
            }
            case 4: // a party hat
            {
                Lathe(_tent, at, new[] { new Vector2(0.09f, 0f), new Vector2(0.002f, 0.22f), new Vector2(0f, 0.22f) }, new Color(1f, 0.9f, 0.85f), 16);
                var pom = new MeshBuilder();
                pom.Ellipsoid(at + Vector3.up * 0.23f, Vector3.one * 0.025f, Quaternion.identity, 8, 6);
                AddBuilder(_fabric, pom, Gold);
                break;
            }
            case 5: // a gold laser pen
            {
                var mb = new MeshBuilder();
                mb.Tube(new[] { at + new Vector3(0, 0.05f, -0.1f), at + new Vector3(0, 0.05f, 0.1f) }, new[] { 0.012f, 0.012f }, Vector3.up, 10, true, true);
                AddBuilder(_box, mb, Gold * 1.2f);
                break;
            }
            case 6: // the golden Bendy
            {
                var mb = new MeshBuilder();
                mb.Ellipsoid(at + Vector3.up * 0.25f, new Vector3(0.08f, 0.08f, 0.07f), Quaternion.identity, 14, 10);
                mb.Tube(new[] { at + Vector3.up * 0.04f, at + Vector3.up * 0.18f }, new[] { 0.05f, 0.04f }, Vector3.right, 12, true, true);
                foreach (var s in new[] { -1f, 1f })
                {
                    mb.Tube(new[] { at + new Vector3(0, 0.3f, s * 0.04f), at + new Vector3(0, 0.36f, s * 0.07f), at + new Vector3(0, 0.39f, s * 0.06f) }, new[] { 0.02f, 0.01f, 0.002f }, Vector3.right, 8, true, true);
                    mb.Tube(new[] { at + new Vector3(0, 0.03f, s * 0.025f), at + new Vector3(0, 0f, s * 0.03f) }, new[] { 0.015f, 0.015f }, Vector3.right, 8, true, true);
                }

                Lathe(_box, at, new[] { new Vector2(0.09f, 0f), new Vector2(0.09f, 0.02f), new Vector2(0f, 0.02f) }, WoodDark, 16);
                AddBuilder(_box, mb, new Color(1.3f, 1.0f, 0.45f));
                break;
            }
            default: // the Ink Demon's bow tie, dripping
            {
                var mb = new MeshBuilder();
                foreach (var s in new[] { -1f, 1f }) mb.Ellipsoid(at + new Vector3(0, 0.12f, s * 0.06f), new Vector3(0.02f, 0.045f, 0.06f), Quaternion.identity, 10, 8);
                mb.Ellipsoid(at + new Vector3(0, 0.12f, 0), Vector3.one * 0.022f, Quaternion.identity, 8, 6);
                mb.Tube(new[] { at + new Vector3(0, 0.1f, 0.03f), at + new Vector3(0, 0.03f, 0.035f) }, new[] { 0.008f, 0.004f }, Vector3.right, 6, false, true);
                AddBuilder(_painted, mb, new Color(0.88f, 0.84f, 0.75f));
                break;
            }
        }
    }

    private void BuildScoreBoard()
    {
        // on the wall by the door: the best score at every game (Arcade fills it in)
        var c = new Vector3(ArcadeX0 + 0.05f, AF + 3.0f, -25.4f);
        Box(_box, c, new Vector3(0.06f, 2.0f, 2.3f), Gold);
        Box(_box, c + Vector3.right * 0.02f, new Vector3(0.06f, 1.84f, 2.14f), Dark);
        var rot = Quaternion.Euler(0, -90, 0);
        Signs.Add(new SignSpec { Text = "HIGH SCORES", Pos = c + new Vector3(0.06f, 0.75f, 0), Rot = rot, Height = 0.2f, Width = 2f, Color = new Color(1f, 0.84f, 0.5f) });
        for (var i = 0; i < Arcade.GameCount; i++)
            Signs.Add(new SignSpec { Text = Arcade.GameNames[i] + "  ......  0", Pos = c + new Vector3(0.06f, 0.45f - i * 0.17f, 0), Rot = rot, Height = 0.075f, Width = 2.0f, Color = Cream, Id = "HighScore" + i });
    }

    private void BuildPhotoBooth()
    {
        // a booth in the corner: a curtain across its front, a stool, the camera's lens in the back wall, a sign
        float x0 = ArcadeX0 + 0.12f, x1 = 10.2f, z0 = -16.95f, z1 = ArcadeZ1 - 0.02f, top = AF + 2.3f;
        WallX(_painted, x1, z0, z1, AF, top, Vector3.right, new Color(0.3f, 0.45f, 0.6f), null, 1f, 1f);
        WallX(_painted, x1 - 0.05f, z0, z1, AF, top, Vector3.left, new Color(0.85f, 0.8f, 0.7f), null, 1f, 1f);
        CeilingRect(_painted, x0, x1, z0, z1, top, new Color(0.85f, 0.8f, 0.7f), 1f);
        Patch(_painted, new Vector3(x0, top, z0), new Vector3(x1, top, z0), new Vector3(x1, top, z1), new Vector3(x0, top, z1), Vector3.up, new Color(0.3f, 0.45f, 0.6f), 1, 1);
        Box(_box, new Vector3((x0 + x1) * 0.5f, top + 0.25f, z0 - 0.02f), new Vector3(x1 - x0 + 0.1f, 0.5f, 0.06f), Dark);
        Signs.Add(new SignSpec { Text = "PHOTOS", Pos = new Vector3((x0 + x1) * 0.5f, top + 0.25f, z0 - 0.06f), Rot = Quaternion.identity, Height = 0.24f, Width = 1.6f, Color = new Color(1f, 0.84f, 0.5f) });
        for (var x = x0 + 0.1f; x < x1; x += 0.18f) Glow(new Vector3(x, top + 0.55f, z0 - 0.05f), 0.12f, new Color(1f, 0.86f, 0.55f, 1f), GlowGroup.Chase, _arcadeBulb++);
        // the curtain, drawn to one side
        var curtain = new Vector3[11 * 9];
        for (var j = 0; j <= 8; j++)
        for (var i = 0; i <= 10; i++)
        {
            var fu = i / 10f;
            curtain[j * 11 + i] = new Vector3(Mathf.Lerp(x0, x0 + 0.45f, fu), Mathf.Lerp(AF + 0.25f, top - 0.05f, j / 8f), z0 + Mathf.Sin(fu * Mathf.PI * 6) * 0.04f);
        }

        Surface(_curtain, curtain, 10, 8, new Color(0.6f, 0.15f, 0.2f), 2f, 1.5f);
        // the lens, a flash bulb, the stool, a card of how it works
        var lens = PhotoLens;
        Box(_box, lens + Vector3.forward * 0.04f, new Vector3(0.36f, 0.3f, 0.06f), Dark * 1.5f);
        var glassLens = new MeshBuilder();
        Drum(lens + Vector3.forward * 0.02f, lens + Vector3.back * 0.02f, 0.05f, 18, glassLens);
        AddBuilder(_plate, glassLens, new Color(0.25f, 0.3f, 0.35f));
        Glow(lens + new Vector3(0, 0.25f, -0.02f), 0.12f, new Color(1f, 0.95f, 0.85f, 1f), GlowGroup.Steady);
        Lathe(_fabric, PhotoSeat + Vector3.up * 0.45f, new[] { new Vector2(0.18f, 0f), new Vector2(0.17f, 0.05f), new Vector2(0f, 0.06f) }, new Color(0.6f, 0.15f, 0.2f), 14);
        Lathe(_plate, PhotoSeat, new[] { new Vector2(0.15f, 0f), new Vector2(0.02f, 0.03f), new Vector2(0.02f, 0.45f), new Vector2(0f, 0.45f) }, Iron * 1.5f, 10);
        Signs.Add(new SignSpec { Text = "LOOK AT THE LENS", Pos = lens + new Vector3(0, -0.28f, -0.01f), Rot = Quaternion.identity, Height = 0.05f, Width = 0.8f, Color = new Color(0.12f, 0.07f, 0.04f), Id = "PhotoInfo" });
    }

    private void PlanArcadeLamps()
    {
        var warm = new Color(1f, 0.85f, 0.6f);
        foreach (var x in new[] { 11.5f, 15.2f, 19.0f })
        foreach (var z in new[] { -24.5f, -18.0f })
        {
            AddLamp(new Vector3(x, ArcadeCeiling - 0.8f, z), warm * 1.0f, 3.6f);
            Lathe(_box, new Vector3(x, ArcadeCeiling - 0.75f, z), new[] { new Vector2(0.35f, 0f), new Vector2(0.2f, 0.2f), new Vector2(0.02f, 0.25f), new Vector2(0.01f, 0.75f) }, new Color(0.85f, 0.25f, 0.2f), 18);
            Glow(new Vector3(x, ArcadeCeiling - 0.82f, z), 0.25f, new Color(1f, 0.92f, 0.75f, 1f), GlowGroup.Steady);
            Glow(new Vector3(x, ArcadeCeiling - 0.85f, z), 1.6f, new Color(1f, 0.8f, 0.55f, 0.18f), GlowGroup.Steady);
        }

        AddLamp(new Vector3(11.8f, AF + 2.8f, -16.6f), warm * 0.9f, 2.0f, Vector3.forward + Vector3.down, 0.4f); // over the toss board
        AddLamp(new Vector3(15.8f, AF + 3.0f, -16.6f), warm * 0.9f, 2.4f, Vector3.forward + Vector3.down, 0.4f); // the gallery
        AddLamp(new Vector3(19.6f, AF + 2.9f, -16.6f), warm * 0.9f, 1.8f, Vector3.forward + Vector3.down, 0.4f); // the darts
        AddLamp(new Vector3(21.0f, AF + 3.5f, -18.8f), warm * 0.9f, 2.4f, Vector3.right, 0.3f); // the strong man
        AddLamp(new Vector3(20.5f, AF + 2.6f, -24.0f), warm * 0.9f, 2.2f, Vector3.right + Vector3.down, 0.4f); // ink ball
        AddLamp(new Vector3(19.7f, AF + 2.4f, -25.4f), warm * 0.8f, 1.8f, Vector3.back + Vector3.down, 0.4f); // whack
        AddLamp(new Vector3(16.3f, AF + 2.0f, -26.4f), new Color(0.8f, 0.9f, 1f) * 0.9f, 1.0f); // inside the claw
        AddLamp(new Vector3(13.5f, AF + 2.8f, -25.6f), warm * 0.8f, 1.8f, Vector3.back + Vector3.down, 0.4f); // pachinko
        AddLamp(new Vector3(9.4f, AF + 2.6f, -19.3f), warm * 1.0f, 2.2f, Vector3.left + Vector3.down, 0.4f); // the prizes
        AddLamp(new Vector3(9.2f, AF + 2.1f, -16.0f), new Color(1f, 0.95f, 0.85f) * 0.8f, 1.2f); // in the photo booth
    }
}
