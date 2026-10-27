using System;
using InkVR.Rendering;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The fan finds' little props (a ticket stub, Wally's note, Boris's bowl, a sheep's bell, Bertrum's blueprint...),
/// the tally board in the lobby that counts them, Joey Drew's office behind the locked door at the foot of the left-hand
/// stairs (it opens when everything's found), the golden Bendy trophy you can win, and the season's decorations.
/// </summary>
public sealed partial class Theatre
{
    // ---- the finds' props (FanFinds reads these) ----
    public static Vector3 TicketStubAt => new(-(SeatStart + SeatW * 0.5f), RowFloor(8) + 0.49f, RowZ(8) + 0.04f);
    public static Vector3 CoinAt => new(-2.6f, StageY + 0.006f, 4.62f);
    public static Vector3 WallyNoteAt => new(BoothX + 1.01f, LobbyFloor + 1.5f, -23.2f);
    public static Vector3 SheetMusicAt => new(-5.6f, LobbyFloor + 1.08f, PianoZ);
    public static Vector3 SheepBellAt => new(-Half + 0.42f, StageY + 1.32f, 7.2f);
    public static Vector3 BorisBowlAt => new(-6.4f, StageY + 0.6f, 13.3f);
    public static Vector3 ScriptAt => new(6.3f, StageY + 0.755f, 13.55f);
    public static Vector3 KeysAt => new(1.5f, BasementFloor + 0.01f, 14.7f);
    public static Vector3 BlueprintAt => new(-2.2f, ProjFloor + 1.55f, ProjBackZ + 0.012f);
    public static Vector3 LandTicketAt => new(9.05f, LobbyFloor + 1.045f, -20.9f);

    public static Vector3 TallyBoardAt => new(-6.4f, LobbyFloor + 2.3f, LobbyFrontZ + 0.04f);
    public static Vector3 TrophyAt => new(-5.1f, LobbyFloor, LobbyFrontZ + 0.5f);
    public static Vector3 OfficeDoorHinge => new(-LobbyHalf - 0.12f, LobbyFloor, SideDoorZ - SideDoorHalf);
    public static Vector3 BeastAt => new(-13.6f, LobbyFloor, -21.7f);

    private void BuildFinds()
    {
        // a ticket stub left on a front-row seat... no: row nine, the aisle seat
        var t = TicketStubAt;
        var tp = NewPart("TicketStub", TheatreArt.Ticket(), false, 2001);
        Patch(tp, t + new Vector3(-0.05f, 0, -0.025f), t + new Vector3(0.05f, 0, -0.025f), t + new Vector3(0.05f, 0, 0.025f), t + new Vector3(-0.05f, 0, 0.025f), Vector3.up, new Color(1.1f, 1.1f, 1.1f), 1, 1);
        // a Bendy coin dropped on the apron
        var coin = new MeshBuilder();
        Drum(CoinAt, CoinAt + Vector3.up * 0.004f, 0.022f, 18, coin);
        AddBuilder(_box, coin, Gold * 1.2f);
        // Wally's note, pinned to the ticket booth's side
        var w = WallyNoteAt;
        Patch(_notes, w + new Vector3(0, -0.11f, 0.08f), w + new Vector3(0, -0.11f, -0.08f), w + new Vector3(0, 0.11f, -0.08f), w + new Vector3(0, 0.11f, 0.08f), Vector3.right, Cream, 1, 1);
        Signs.Add(new SignSpec { Text = "I'M OUTTA HERE!", Pos = w + new Vector3(0.006f, 0.03f, 0), Rot = Quaternion.Euler(0, -90, 0), Height = 0.026f, Width = 0.15f, Color = new Color(0.12f, 0.07f, 0.04f) });
        Signs.Add(new SignSpec { Text = "- W.F.", Pos = w + new Vector3(0.006f, -0.04f, 0.03f), Rot = Quaternion.Euler(0, -90, 0), Height = 0.022f, Width = 0.1f, Color = new Color(0.12f, 0.07f, 0.04f) });
        // a sheep's bell on the fly rail
        var b = SheepBellAt;
        Lathe(_box, b, new[] { new Vector2(0.035f, 0f), new Vector2(0.03f, 0.03f), new Vector2(0.018f, 0.06f), new Vector2(0f, 0.065f) }, Gold * 0.9f, 12);
        // Boris's soup bowl and spoon
        var bowl = BorisBowlAt;
        Lathe(_box, bowl, new[] { new Vector2(0.04f, 0f), new Vector2(0.09f, 0.05f), new Vector2(0.1f, 0.06f), new Vector2(0.085f, 0.06f), new Vector2(0.03f, 0.012f), new Vector2(0f, 0.012f) }, Cream, 18);
        Box(_plate, bowl + new Vector3(0.05f, 0.07f, 0.02f), new Vector3(0.012f, 0.008f, 0.16f), Quaternion.Euler(-25, 30, 0), new Color(0.75f, 0.72f, 0.68f));
        // Susie's script on the dressing table
        var s = ScriptAt;
        Box(_posterBg, s, new Vector3(0.21f, 0.012f, 0.28f), Quaternion.Euler(0, 12, 0), Cream);
        Signs.Add(new SignSpec { Text = "ALICE ANGEL", Pos = s + new Vector3(0, 0.008f, 0.05f), Rot = Quaternion.Euler(90, 12, 0), Height = 0.025f, Width = 0.18f, Color = new Color(0.45f, 0.08f, 0.04f) });
        Signs.Add(new SignSpec { Text = "lines: S. Campbell", Pos = s + new Vector3(0, 0.008f, -0.02f), Rot = Quaternion.Euler(90, 12, 0), Height = 0.016f, Width = 0.18f, Color = new Color(0.12f, 0.07f, 0.04f) });
        // Wally's keys, dropped at the foot of the cellar stairs
        var k = KeysAt;
        var keys = new MeshBuilder();
        keys.Torus(k + Vector3.up * 0.004f, Vector3.up, 0.03f, 0.004f, 18, 6);
        for (var i = 0; i < 4; i++) keys.Box(k + new Vector3(0.04f + i * 0.004f, 0.004f, -0.02f + i * 0.014f), new Vector3(0.06f, 0.004f, 0.012f), Quaternion.Euler(0, 15 * i - 20, 0));
        AddBuilder(_box, keys, Gold * 0.9f);
        // Bertrum's blueprint, pinned in the booth
        var bp = BlueprintAt;
        var blue = new Color(0.35f, 0.5f, 0.75f);
        Patch(_notes, bp + new Vector3(-0.28f, -0.2f, 0), bp + new Vector3(0.28f, -0.2f, 0), bp + new Vector3(0.28f, 0.2f, 0), bp + new Vector3(-0.28f, 0.2f, 0), Vector3.forward, blue, 1, 1);
        Signs.Add(new SignSpec { Text = "BENDY LAND", Pos = bp + new Vector3(0, 0.12f, 0.004f), Rot = Quaternion.Euler(0, 180, 0), Height = 0.05f, Width = 0.5f, Color = Cream });
        Signs.Add(new SignSpec { Text = "B. Piedmont, architect", Pos = bp + new Vector3(0, -0.14f, 0.004f), Rot = Quaternion.Euler(0, 180, 0), Height = 0.022f, Width = 0.5f, Color = Cream });
        // a Bendy Land ticket on the prize counter
        var lt = LandTicketAt;
        Patch(tp, lt + new Vector3(-0.05f, 0, -0.025f), lt + new Vector3(0.05f, 0, -0.025f), lt + new Vector3(0.05f, 0, 0.025f), lt + new Vector3(-0.05f, 0, 0.025f), Vector3.up, new Color(0.9f, 1.1f, 1.2f), 1, 1);

        BuildTallyBoard();
        BuildTrophy();
        BuildOffice();
        BuildSeason();
    }

    private void BuildTallyBoard()
    {
        // on the lobby's street wall, left of the ticket booth: FAN FINDS, the count, a card for each find
        var c = TallyBoardAt;
        Box(_box, c, new Vector3(2.5f, 1.9f, 0.06f), Gold);
        Box(_box, c + Vector3.forward * 0.02f, new Vector3(2.36f, 1.76f, 0.06f), Dark * 1.4f);
        var rot = Quaternion.Euler(0, 180, 0);
        Signs.Add(new SignSpec { Text = "FAN FINDS", Pos = c + new Vector3(0, 0.74f, 0.06f), Rot = rot, Height = 0.17f, Width = 2.2f, Color = new Color(1f, 0.84f, 0.5f) });
        Signs.Add(new SignSpec { Text = "0 OF " + FanFinds.Count, Pos = c + new Vector3(0, 0.56f, 0.06f), Rot = rot, Height = 0.1f, Width = 2.2f, Color = Cream, Id = "FindCount" });
        for (var i = 0; i < FanFinds.Count; i++)
        {
            var col = i % 3;
            var row = i / 3;
            var at = c + new Vector3(0.75f - col * 0.75f, 0.4f - row * 0.098f, 0.06f);
            Signs.Add(new SignSpec { Text = "?", Pos = at, Rot = rot, Height = 0.045f, Width = 0.72f, Color = Cream * 0.7f, Id = "FindCard" + i });
        }

        Signs.Add(new SignSpec { Text = "find them all and Mr. Drew's office opens", Pos = c + new Vector3(0, -0.84f, 0.06f), Rot = rot, Height = 0.04f, Width = 2.2f, Color = Cream * 0.85f });
        Box(_box, c + new Vector3(0, 1.08f, 0.15f), new Vector3(0.6f, 0.07f, 0.12f), Gold, 1.3f);
        Glow(c + new Vector3(0, 1.03f, 0.22f), 0.5f, new Color(1f, 0.85f, 0.6f, 0.5f), GlowGroup.Steady);
    }

    private void BuildTrophy()
    {
        // a pedestal by the board; the golden Bendy stands on it once you've won it in the Fun Hall
        var p = TrophyAt;
        Lathe(_wood, p, new[] { new Vector2(0.25f, 0f), new Vector2(0.22f, 0.06f), new Vector2(0.16f, 0.1f), new Vector2(0.14f, 0.9f), new Vector2(0.2f, 0.95f), new Vector2(0.2f, 1.0f), new Vector2(0f, 1.0f) }, WoodDark * 1.2f, 18);
        var top = p + Vector3.up * 1.0f;
        InGroup("Trophy", top, () =>
        {
            var mb = new MeshBuilder();
            mb.Ellipsoid(top + Vector3.up * 0.38f, new Vector3(0.12f, 0.12f, 0.11f), Quaternion.identity, 16, 12);
            mb.Tube(new[] { top + Vector3.up * 0.06f, top + Vector3.up * 0.28f }, new[] { 0.07f, 0.06f }, Vector3.forward, 14, true, true);
            foreach (var s in new[] { -1f, 1f })
            {
                mb.Tube(new[] { top + new Vector3(s * 0.06f, 0.46f, 0), top + new Vector3(s * 0.11f, 0.55f, 0), top + new Vector3(s * 0.09f, 0.6f, 0.01f) }, new[] { 0.03f, 0.015f, 0.003f }, Vector3.forward, 8, true, true);
                mb.Tube(new[] { top + new Vector3(s * 0.04f, 0.06f, 0), top + new Vector3(s * 0.045f, 0f, 0.02f) }, new[] { 0.022f, 0.022f }, Vector3.forward, 8, true, true);
                mb.Tube(new[] { top + new Vector3(s * 0.07f, 0.24f, 0), top + new Vector3(s * 0.16f, 0.3f, 0.04f) }, new[] { 0.018f, 0.015f }, Vector3.up, 8, true, true);
            }

            AddBuilder(_box, mb, new Color(1.3f, 1.0f, 0.45f));
        });
        Signs.Add(new SignSpec { Text = "THE GOLDEN BENDY", Pos = p + new Vector3(0, 0.6f, 0.16f), Rot = Quaternion.Euler(0, 180, 0), Height = 0.04f, Width = 0.4f, Color = new Color(1f, 0.84f, 0.5f), Id = "TrophyTag" });
    }

    // =====================================================================================================
    // Joey Drew's office
    // =====================================================================================================

    private void BuildOffice()
    {
        float f = LobbyFloor, x0 = OfficeX0, x1 = OfficeX1, z0 = OfficeZ0, z1 = OfficeZ1, top = OfficeCeiling;
        var door = new[] { new Vector4(SideDoorZ - SideDoorHalf, f - 1f, SideDoorZ + SideDoorHalf, f + SideDoorHeight) };
        var green = new Color(0.55f, 0.7f, 0.55f);
        FloorRect(_wood, x0, x1, z0, z1, f, Wood * 0.9f);
        Patch(_carpet, new Vector3(-13.5f, f + 0.005f, -23.8f), new Vector3(-9.2f, f + 0.005f, -23.8f), new Vector3(-9.2f, f + 0.005f, -19.6f), new Vector3(-13.5f, f + 0.005f, -19.6f), Vector3.up, new Color(0.35f, 0.15f, 0.12f), 3, 3, uvRect: new Vector4(0, 0, 3, 2));
        WallX(_wood, x1, z0, z1, f, f + 1.2f, Vector3.left, WoodDark, door, 1.6f, 1.6f);
        WallX(_wall, x1, z0, z1, f + 1.2f, top, Vector3.left, green, door, 1.2f, 1.2f);
        WallX(_wood, x0, z0, z1, f, f + 1.2f, Vector3.right, WoodDark, null, 1.6f, 1.6f);
        WallX(_wall, x0, z0, z1, f + 1.2f, top, Vector3.right, green, null, 1.2f, 1.2f);
        WallZ(_wood, z0, x0, x1, f, f + 1.2f, Vector3.forward, WoodDark, null, 1.6f, 1.6f);
        WallZ(_wall, z0, x0, x1, f + 1.2f, top, Vector3.forward, green, null, 1.2f, 1.2f);
        WallZ(_wood, z1, x0, x1, f, f + 1.2f, Vector3.back, WoodDark, null, 1.6f, 1.6f);
        WallZ(_wall, z1, x0, x1, f + 1.2f, top, Vector3.back, green, null, 1.2f, 1.2f);
        CeilingRect(_coffer, x0, x1, z0, z1, top, Plaster * 0.9f, 2f);
        RevealX(new Vector4(SideDoorZ - SideDoorHalf, f, SideDoorZ + SideDoorHalf, f + SideDoorHeight), x1, -LobbyHalf);
        Doorway(new Vector3(-LobbyHalf + 0.002f, f, SideDoorZ), Vector3.back, Vector3.right, SideDoorHalf * 2, SideDoorHeight, false);
        Doorway(new Vector3(x1 - 0.002f, f, SideDoorZ), Vector3.forward, Vector3.left, SideDoorHalf * 2, SideDoorHeight, false);
        var hinge = OfficeDoorHinge;
        InGroup("OfficeDoor", hinge, () =>
        {
            Box(_door, hinge + new Vector3(0, SideDoorHeight * 0.5f, SideDoorHalf), new Vector3(0.05f, SideDoorHeight - 0.01f, SideDoorHalf * 2 - 0.02f), WoodDark * 1.3f);
            foreach (var side in new[] { -1f, 1f }) Box(_box, hinge + new Vector3(side * 0.035f, 1.05f, SideDoorHalf * 2 - 0.12f), new Vector3(0.03f, 0.05f, 0.05f), Gold);
        });
        var plate = new Vector3(-LobbyHalf + 0.1f, f + 1.7f, SideDoorZ);
        Box(_box, plate, new Vector3(0.03f, 0.18f, 0.5f), Gold);
        Signs.Add(new SignSpec { Text = "J. DREW  -  PRIVATE", Pos = plate + Vector3.right * 0.02f, Rot = Quaternion.Euler(0, -90, 0), Height = 0.045f, Width = 0.48f, Color = Dark, Group = null });

        // his desk, his chair, a lamp; shelves of reels and books; the motto; his portrait; a safe
        var desk = new Vector3(-12.3f, f, -21.7f);
        Box(_wood, desk + new Vector3(0, 0.76f, 0), new Vector3(1.0f, 0.06f, 2.0f), new Color(0.45f, 0.26f, 0.15f));
        foreach (var dz in new[] { -0.75f, 0.75f }) Box(_wood, desk + new Vector3(0, 0.37f, dz), new Vector3(0.9f, 0.74f, 0.45f), new Color(0.42f, 0.24f, 0.14f));
        Box(_fabric, desk + new Vector3(-0.15f, 0.8f, 0), new Vector3(0.5f, 0.01f, 0.7f), new Color(0.2f, 0.35f, 0.25f));
        var chair = desk + new Vector3(-0.85f, 0, 0);
        Box(_fabric, chair + new Vector3(0, 0.48f, 0), new Vector3(0.55f, 0.12f, 0.6f), new Color(0.5f, 0.1f, 0.1f));
        Box(_fabric, chair + new Vector3(-0.27f, 0.95f, 0), new Vector3(0.12f, 0.85f, 0.62f), new Color(0.5f, 0.1f, 0.1f));
        var lamp = desk + new Vector3(0.25f, 0.79f, 0.65f);
        Lathe(_box, lamp, new[] { new Vector2(0.08f, 0f), new Vector2(0.02f, 0.03f), new Vector2(0.015f, 0.32f), new Vector2(0.14f, 0.36f), new Vector2(0.1f, 0.46f), new Vector2(0f, 0.48f) }, new Color(0.25f, 0.45f, 0.3f), 16);
        Glow(lamp + Vector3.up * 0.36f, 0.6f, new Color(1f, 0.85f, 0.6f, 0.4f), GlowGroup.Steady);
        for (var y = f + 0.5f; y < f + 2.6f; y += 0.55f)
        {
            Box(_wood, new Vector3(x0 + 0.2f, y, -21.7f), new Vector3(0.36f, 0.04f, 3.2f), WoodDark);
            var rng = new System.Random((int)(y * 100));
            for (var z = -23.2f; z < -20.2f; z += 0.07f)
            {
                var h = 0.28f + (float)rng.NextDouble() * 0.15f;
                Box(_posterBg, new Vector3(x0 + 0.2f, y + 0.02f + h * 0.5f, z), new Vector3(0.24f, h, 0.05f), (float)rng.NextDouble() < 0.5 ? new Color(0.5f, 0.15f, 0.12f) : new Color(0.2f, 0.3f, 0.25f));
            }
        }

        var motto = new Vector3(-11.4f, f + 2.9f, z0 + 0.03f);
        Box(_box, motto, new Vector3(2.4f, 0.5f, 0.05f), Gold);
        Box(_box, motto + Vector3.forward * 0.02f, new Vector3(2.25f, 0.38f, 0.05f), Dark);
        Signs.Add(new SignSpec { Text = "DREAMS COME TRUE", Pos = motto + Vector3.forward * 0.05f, Rot = Quaternion.Euler(0, 180, 0), Height = 0.22f, Width = 2.2f, Color = new Color(1f, 0.84f, 0.5f) });
        var portrait = new Vector3(-11.4f, f + 2.0f, z1 - 0.03f);
        Box(_box, portrait, new Vector3(0.9f, 1.15f, 0.06f), Gold);
        Patch(_posterBg, portrait + new Vector3(0.38f, -0.5f, -0.035f), portrait + new Vector3(-0.38f, -0.5f, -0.035f), portrait + new Vector3(-0.38f, 0.5f, -0.035f), portrait + new Vector3(0.38f, 0.5f, -0.035f), Vector3.back, new Color(0.35f, 0.3f, 0.25f), 1, 1);
        var head = new MeshBuilder();
        head.Ellipsoid(portrait + new Vector3(0, 0.15f, -0.04f), new Vector3(0.16f, 0.2f, 0.01f), Quaternion.identity, 16, 8);
        head.Ellipsoid(portrait + new Vector3(0, -0.32f, -0.04f), new Vector3(0.3f, 0.2f, 0.01f), Quaternion.identity, 16, 8);
        AddBuilder(_painted, head, Dark * 2f);
        Signs.Add(new SignSpec { Text = "JOSEPH DREW", Pos = portrait + new Vector3(0, -0.66f, -0.04f), Rot = Quaternion.identity, Height = 0.05f, Width = 0.8f, Color = new Color(1f, 0.84f, 0.5f) });
        Box(_plate, new Vector3(x0 + 0.4f, f + 0.45f, -24.3f), new Vector3(0.7f, 0.9f, 0.7f), new Color(0.25f, 0.27f, 0.25f));

        // the reward, on a stand under a spotlight: a costume no one else has, and a note
        var beast = BeastAt;
        Lathe(_wood, beast, new[] { new Vector2(0.45f, 0f), new Vector2(0.45f, 0.12f), new Vector2(0f, 0.12f) }, WoodDark, 24);
        InGroup("BeastCostume", beast + Vector3.up * 0.12f, () => BeastMannequin(beast + Vector3.up * 0.12f));
        var note = beast + new Vector3(1.0f, 0.79f, 0.4f);
        Patch(_notes, note + new Vector3(-0.1f, 0, -0.14f), note + new Vector3(0.1f, 0, -0.14f), note + new Vector3(0.1f, 0, 0.14f), note + new Vector3(-0.1f, 0, 0.14f), Vector3.up, Cream, 1, 1);
        Signs.Add(new SignSpec { Text = "To the one who found it all:", Pos = desk + new Vector3(0.0f, 0.82f, 0.05f), Rot = Quaternion.Euler(90, -90, 0), Height = 0.03f, Width = 0.6f, Color = new Color(0.12f, 0.07f, 0.04f) });
        Signs.Add(new SignSpec { Text = "wear him well.  - J.D.", Pos = desk + new Vector3(0.06f, 0.82f, 0.05f), Rot = Quaternion.Euler(90, -90, 0), Height = 0.03f, Width = 0.6f, Color = new Color(0.12f, 0.07f, 0.04f) });
        AddLamp(new Vector3(-11.4f, top - 0.4f, -21.7f), new Color(1f, 0.82f, 0.55f) * 1.0f, 3.2f);
        AddLamp(lamp + Vector3.up * 0.3f, new Color(1f, 0.9f, 0.7f) * 1.0f, 1.4f);
        AddLamp(beast + Vector3.up * 3.0f, new Color(1f, 0.9f, 0.75f) * 1.4f, 2.4f, Vector3.down, 0.7f);
        Lathe(_box, new Vector3(-11.4f, top - 0.32f, -21.7f), new[] { new Vector2(0.3f, 0f), new Vector2(0.2f, 0.18f), new Vector2(0.01f, 0.3f) }, Gold, 18);
        Glow(new Vector3(-11.4f, top - 0.4f, -21.7f), 1.2f, new Color(1f, 0.85f, 0.6f, 0.3f), GlowGroup.Steady);
    }

    /// <summary>The reward: Beast Bendy, on a stand. Take hold of him and he's yours to wear.</summary>
    private void BeastMannequin(Vector3 at)
    {
        var body = new MeshBuilder();
        body.Ellipsoid(at + Vector3.up * 1.25f, new Vector3(0.36f, 0.42f, 0.28f), Quaternion.identity, 18, 12); // the great chest
        body.Ellipsoid(at + Vector3.up * 0.85f, new Vector3(0.24f, 0.22f, 0.2f), Quaternion.identity, 16, 10);
        foreach (var s in new[] { -1f, 1f })
        {
            body.Tube(new[] { at + new Vector3(s * 0.12f, 0.8f, 0), at + new Vector3(s * 0.16f, 0.45f, 0.04f), at + new Vector3(s * 0.15f, 0.05f, 0.02f) }, new[] { 0.11f, 0.09f, 0.1f }, Vector3.forward, 12, true, true);
            body.Tube(new[] { at + new Vector3(s * 0.36f, 1.5f, 0), at + new Vector3(s * 0.52f, 1.05f, 0.08f), at + new Vector3(s * 0.55f, 0.55f, 0.15f) }, new[] { 0.12f, 0.1f, 0.13f }, Vector3.forward, 12, true, true);
            body.Tube(new[] { at + new Vector3(s * 0.16f, 2.05f, -0.05f), at + new Vector3(s * 0.32f, 2.3f, -0.02f), at + new Vector3(s * 0.38f, 2.55f, 0.05f), at + new Vector3(s * 0.3f, 2.7f, 0.12f) }, new[] { 0.07f, 0.05f, 0.03f, 0.005f }, Vector3.forward, 10, true, true);
        }

        body.Ellipsoid(at + Vector3.up * 1.9f, new Vector3(0.22f, 0.24f, 0.22f), Quaternion.identity, 16, 12);
        AddBuilder(_painted, body, Dark * 1.8f);
        var grin = new MeshBuilder();
        grin.Ellipsoid(at + new Vector3(0, 1.82f, 0.19f), new Vector3(0.13f, 0.04f, 0.04f), Quaternion.identity, 12, 6);
        AddBuilder(_painted, grin, Cream * 1.2f);
    }

    // =====================================================================================================
    // The season
    // =====================================================================================================

    public enum Season
    {
        None,
        Halloween,
        Christmas,
        Birthday
    }

    /// <summary>What the theatre's dressed for today: Halloween (InkVR came out on 24 October), Christmas, Bendy's birthday (10 February, the day the game came out).</summary>
    public static Season Today
    {
        get
        {
            var d = DateTime.Now;
            if ((d.Month == 10 && d.Day >= 15) || (d.Month == 11 && d.Day <= 2)) return Season.Halloween;
            if (d.Month == 12 || (d.Month == 1 && d.Day <= 6)) return Season.Christmas;
            if (d.Month == 2 && d.Day >= 8 && d.Day <= 14) return Season.Birthday;
            return Season.None;
        }
    }

    private void BuildSeason()
    {
        switch (Today)
        {
            case Season.Halloween:
                // jack-o'-lanterns with Bendy's grin carved in them, cobwebs in the corners
                foreach (var p in new[] { new Vector3(-ApronHalf + 0.4f, StageY, ApronZ + 0.2f), new Vector3(ApronHalf - 0.4f, StageY, ApronZ + 0.2f), new Vector3(CounterX + 1.2f, LobbyFloor + 1.05f, CounterFrontZ - 0.2f),
                             new Vector3(BoothX - 0.8f, LobbyFloor + 1.04f, BoothFrontZ + 0.15f), new Vector3(-1.3f, LobbyFloor, LobbyZ - 0.4f), new Vector3(1.3f, LobbyFloor, LobbyZ - 0.4f), new Vector3(12.0f, LobbyFloor + 1.0f, -17.7f), new Vector3(-6.5f, BasementFloor, 15.6f) })
                    Pumpkin(p);
                foreach (var (c, a, b) in new[] { (new Vector3(-Half, CorniceY, BackZ), Vector3.right, Vector3.forward), (new Vector3(Half, CorniceY, BackZ), Vector3.left, Vector3.forward), (new Vector3(-LobbyHalf, LobbyCeiling - 0.6f, LobbyFrontZ), Vector3.right, Vector3.forward), (new Vector3(LobbyHalf, LobbyCeiling - 0.6f, LobbyFrontZ), Vector3.left, Vector3.forward) })
                    Cobweb(c, a, b);
                Signs.Add(new SignSpec { Text = "HAPPY HALLOWEEN", Pos = new Vector3(0, LobbyFloor + 3.3f, LobbyZ - 0.06f), Rot = Quaternion.identity, Height = 0.18f, Width = 2.6f, Color = new Color(1f, 0.6f, 0.2f) });
                break;
            case Season.Christmas:
                ChristmasTree(new Vector3(-3.6f, LobbyFloor, -17.2f));
                foreach (var p in new[] { new Vector3(0, LobbyFloor + LobbyDoorHeight + 0.35f, LobbyZ - 0.05f), new Vector3(0, LobbyFloor + 3.6f, LobbyFrontZ + 0.05f) }) Wreath(p);
                Signs.Add(new SignSpec { Text = "SEASON'S GREETINGS", Pos = new Vector3(0, LobbyFloor + 3.3f, LobbyZ - 0.06f), Rot = Quaternion.identity, Height = 0.16f, Width = 2.6f, Color = new Color(0.9f, 0.95f, 0.8f) });
                break;
            case Season.Birthday:
                Cake(new Vector3(CounterX - 0.2f, LobbyFloor + 1.05f, CounterFrontZ - 0.25f));
                foreach (var p in new[] { new Vector3(-2.2f, LobbyFloor, -15.4f), new Vector3(2.2f, LobbyFloor, -15.4f), new Vector3(-ApronHalf + 0.6f, StageY, ApronZ + 0.3f), new Vector3(ApronHalf - 0.6f, StageY, ApronZ + 0.3f) })
                    Balloons(p);
                Signs.Add(new SignSpec { Text = "HAPPY BIRTHDAY, BENDY!", Pos = new Vector3(0, LobbyFloor + 3.3f, LobbyZ - 0.06f), Rot = Quaternion.identity, Height = 0.18f, Width = 3f, Color = new Color(1f, 0.84f, 0.5f) });
                break;
        }
    }

    private void Pumpkin(Vector3 at)
    {
        var mb = new MeshBuilder();
        for (var k = 0; k < 6; k++)
        {
            var a = k / 6f * Mathf.PI * 2;
            mb.Ellipsoid(at + new Vector3(Mathf.Cos(a) * 0.05f, 0.13f, Mathf.Sin(a) * 0.05f), new Vector3(0.11f, 0.12f, 0.11f), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), 14, 10);
        }

        AddBuilder(_painted, mb, new Color(0.95f, 0.5f, 0.12f));
        var stem = new MeshBuilder();
        stem.Tube(new[] { at + Vector3.up * 0.24f, at + new Vector3(0.01f, 0.31f, 0.01f) }, new[] { 0.018f, 0.014f }, Vector3.forward, 8, false, true);
        AddBuilder(_painted, stem, new Color(0.35f, 0.45f, 0.2f));
        // Bendy's face carved in it, glowing
        var face = new MeshBuilder();
        foreach (var s in new[] { -1f, 1f }) face.Ellipsoid(at + new Vector3(s * 0.045f, 0.16f, 0.14f), new Vector3(0.022f, 0.032f, 0.008f), Quaternion.identity, 8, 6);
        face.Ellipsoid(at + new Vector3(0, 0.08f, 0.145f), new Vector3(0.07f, 0.02f, 0.008f), Quaternion.identity, 10, 6);
        AddBuilder(_posterBg, face, new Color(1.4f, 1.05f, 0.4f));
        Glow(at + new Vector3(0, 0.13f, 0.18f), 0.45f, new Color(1f, 0.6f, 0.2f, 0.4f), GlowGroup.Flicker);
    }

    private void Cobweb(Vector3 corner, Vector3 along1, Vector3 along2)
    {
        var web = NewPart("Cobwebs", Textures.SoftDot, true, 2990);
        for (var k = 1; k <= 4; k++)
        {
            var r = k * 0.18f;
            Patch(web, corner + along1 * r + Vector3.down * 0.01f, corner + along2 * r + Vector3.down * 0.01f, corner + along2 * (r + 0.02f), corner + along1 * (r + 0.02f), Vector3.down, new Color(0.85f, 0.85f, 0.8f, 0.35f), 1, 1, unlit: true, uvRect: new Vector4(0.45f, 0.45f, 0.55f, 0.55f));
        }

        for (var k = 0; k < 4; k++)
        {
            var dir = Vector3.Lerp(along1, along2, k / 3f).normalized;
            Patch(web, corner, corner + dir * 0.75f, corner + dir * 0.75f + Vector3.down * 0.01f, corner + Vector3.down * 0.01f, Vector3.down, new Color(0.85f, 0.85f, 0.8f, 0.35f), 1, 1, unlit: true, uvRect: new Vector4(0.45f, 0.45f, 0.55f, 0.55f));
        }
    }

    private void ChristmasTree(Vector3 at)
    {
        Lathe(_wood, at, new[] { new Vector2(0.3f, 0f), new Vector2(0.3f, 0.3f), new Vector2(0.08f, 0.32f), new Vector2(0.08f, 0.5f), new Vector2(0f, 0.5f) }, new Color(0.6f, 0.2f, 0.15f), 16);
        for (var k = 0; k < 4; k++)
        {
            var y = 0.45f + k * 0.55f;
            var r = 1.0f - k * 0.22f;
            Lathe(_fabric, at + Vector3.up * y, new[] { new Vector2(r, 0f), new Vector2(r * 0.4f, 0.75f), new Vector2(0f, 0.8f) }, new Color(0.2f, 0.45f, 0.25f), 20);
        }

        var rng = new System.Random(12);
        for (var i = 0; i < 26; i++)
        {
            var y = 0.55f + (float)rng.NextDouble() * 2.0f;
            var r = Mathf.Lerp(0.95f, 0.15f, (y - 0.45f) / 2.4f);
            var a = (float)rng.NextDouble() * Mathf.PI * 2;
            var p = at + new Vector3(Mathf.Cos(a) * r * 0.8f, y, Mathf.Sin(a) * r * 0.8f);
            Glow(p, 0.1f, i % 2 == 0 ? new Color(1f, 0.85f, 0.5f, 1f) : new Color(0.9f, 0.4f, 0.3f, 1f), GlowGroup.Chase, i);
        }

        Star(at + Vector3.up * 2.75f, 0.16f, Vector3.back, Gold * 1.2f);
    }

    private void Wreath(Vector3 at)
    {
        var mb = new MeshBuilder();
        mb.Torus(at, Vector3.forward, 0.3f, 0.08f, 28, 8);
        AddBuilder(_fabric, mb, new Color(0.2f, 0.42f, 0.22f));
        for (var k = 0; k < 8; k++)
        {
            var a = k / 8f * Mathf.PI * 2;
            Glow(at + new Vector3(Mathf.Cos(a) * 0.3f, Mathf.Sin(a) * 0.3f, -0.08f), 0.08f, new Color(1f, 0.35f, 0.3f, 1f), GlowGroup.Steady);
        }
    }

    private void Cake(Vector3 at)
    {
        Lathe(_posterBg, at, new[] { new Vector2(0.22f, 0f), new Vector2(0.22f, 0.14f), new Vector2(0.15f, 0.14f), new Vector2(0.15f, 0.25f), new Vector2(0f, 0.25f) }, Cream, 20);
        for (var k = 0; k < 5; k++)
        {
            var a = k / 5f * Mathf.PI * 2;
            var p = at + new Vector3(Mathf.Cos(a) * 0.09f, 0.25f, Mathf.Sin(a) * 0.09f);
            Lathe(_box, p, new[] { new Vector2(0.008f, 0f), new Vector2(0.008f, 0.07f), new Vector2(0f, 0.07f) }, new Color(0.9f, 0.5f, 0.6f), 6);
            Glow(p + Vector3.up * 0.09f, 0.06f, new Color(1f, 0.85f, 0.5f, 1f), GlowGroup.Flicker);
        }

        Signs.Add(new SignSpec { Text = "B", Pos = at + new Vector3(0, 0.07f, -0.225f), Rot = Quaternion.identity, Height = 0.1f, Width = 0.2f, Color = Dark });
    }

    private void Balloons(Vector3 at)
    {
        for (var k = 0; k < 3; k++)
        {
            var top = at + new Vector3((k - 1) * 0.18f, 1.6f + k * 0.15f, 0);
            var mb = new MeshBuilder();
            mb.Ellipsoid(top, new Vector3(0.12f, 0.15f, 0.12f), Quaternion.identity, 14, 10);
            mb.Tube(new[] { top + Vector3.down * 0.15f, at }, new[] { 0.003f, 0.003f }, Vector3.forward, 4, false, false);
            AddBuilder(_painted, mb, k == 1 ? Dark * 2.2f : k == 0 ? new Color(0.9f, 0.3f, 0.3f) : Cream);
        }
    }
}
