using System.Collections.Generic;
using UnityEngine;

namespace InkVR.UI;

/// <summary>The walk map for the rooms added in 1.0 (see <see cref="Walkable"/> for how slabs and blockers work).</summary>
public sealed partial class Theatre
{
    /// <summary>Joey's office opens once every fan find is found.</summary>
    public static bool OfficeOpen;

    /// <summary>What a footstep at this spot sounds on: the aisle and stair runners and the dressing room's rug are carpet.</summary>
    public static TheatreSounds.Floor FloorSound(Vector3 p)
    {
        switch (RoomOf(p.x, p.y + 0.3f, p.z, 0, 0, 0))
        {
            case Room.Basement:
                return TheatreSounds.Floor.Concrete;
            case Room.Arcade:
                return TheatreSounds.Floor.Tile;
            case Room.Dressing:
                return p.x > 4.6f && p.x < 7.8f && p.z > 9.6f && p.z < 12.6f ? TheatreSounds.Floor.Carpet : TheatreSounds.Floor.Wood;
            case Room.Hall:
                if (Mathf.Abs(p.x) < AisleHalf && p.z < ApronZ) return TheatreSounds.Floor.Carpet;
                return TheatreSounds.Floor.Wood;
            case Room.Lobby:
                if (Mathf.Abs(p.x) < 1f) return TheatreSounds.Floor.Carpet;
                if (Mathf.Abs(Mathf.Abs(p.x) - (StairIn + LobbyHalf) * 0.5f) < 0.5f && p.z > StairBottomZ) return TheatreSounds.Floor.Carpet;
                return TheatreSounds.Floor.Wood;
            default:
                return TheatreSounds.Floor.Wood;
        }
    }

    private static float CellarHeight(float z)
    {
        if (z < CellarTopZ) return StageY;
        var i = Mathf.Min(CellarSteps - 1, Mathf.FloorToInt((z - CellarTopZ) / CellarTread));
        return StageY - (i + 1) * CellarRise;
    }

    private static float ProjStairHeight(float x)
    {
        if (x < ProjStairX0) return BalconyTop;
        if (x >= ProjStairX1) return ProjFloor;
        var i = Mathf.Min(ProjSteps - 1, Mathf.FloorToInt((x - ProjStairX0) / ProjStepRun));
        return BalconyTop + (i + 1) * ProjRise;
    }

    private static void RoomSlabs(List<Slab> list)
    {
        // backstage: the wings (behind the house curtain, then behind the proscenium), the crossover, the prop store,
        // round the stairwell, into the dressing room
        foreach (var s in new[] { -1f, 1f })
        {
            list.Add(Mirror(5.0f, 8.75f, 5.62f, 6.05f, FloorKind.Stage, s));
            list.Add(Mirror(5.55f, 8.75f, 5.2f, 6.6f, FloorKind.Stage, s));
        }

        list.Add(new Slab(-8.75f, 8.75f, 6.5f, 8.2f, FloorKind.Stage));
        list.Add(new Slab(-8.35f, -1.0f, 8.0f, 13.78f, FloorKind.Stage));
        list.Add(new Slab(1.0f, DressingX - 0.25f, 8.0f, 13.78f, FloorKind.Stage));
        list.Add(new Slab(-1.0f, 1.0f, 8.0f, CellarTopZ - 0.05f, FloorKind.Stage));
        list.Add(new Slab(DressingDoorX - 0.3f, DressingDoorX + 0.3f, 8.0f, 8.8f, FloorKind.Stage));
        list.Add(new Slab(DressingX + 0.95f, Half - 0.3f, DressingZ + 0.22f, BackstageBackZ - 0.22f, FloorKind.Stage));

        // the cellar stairs and the cellar
        list.Add(new Slab(-CellarHalf + 0.2f, CellarHalf - 0.2f, CellarTopZ - 0.1f, CellarBottomZ + 0.05f, FloorKind.Cellar));
        list.Add(new Slab(-8.75f, -CellarHalf - 0.3f, CellarFrontZ + 0.2f, CellarBackZ - 0.22f, FloorKind.Basement));
        list.Add(new Slab(CellarHalf + 0.3f, 8.75f, CellarFrontZ + 0.2f, CellarBackZ - 0.22f, FloorKind.Basement));
        list.Add(new Slab(-CellarHalf - 0.3f, CellarHalf + 0.3f, CellarFrontZ + 0.2f, CellarTopZ - 0.3f, FloorKind.Basement));
        list.Add(new Slab(-CellarHalf - 0.3f, CellarHalf + 0.3f, CellarBottomZ, CellarBackZ - 0.22f, FloorKind.Basement));

        // up to the booth: its stair along the back wall, the landing, the door, the booth
        list.Add(new Slab(ProjStairX0 - 0.15f, ProjStairX1, -13.6f, -13.3f, FloorKind.ProjStair));
        list.Add(new Slab(ProjStairX1, ProjDoorX + 0.3f, -13.6f, -13.3f, FloorKind.Booth));
        list.Add(new Slab(ProjDoorX - 0.22f, ProjDoorX + 0.22f, LobbyZ - 0.25f, -13.3f, FloorKind.Booth));
        list.Add(new Slab(ProjX0 + 0.22f, ProjX1 - 0.22f, ProjBackZ + 0.22f, LobbyZ - 0.2f, FloorKind.Booth));
    }

    private static readonly Blocker[] RoomBlockers =
    {
        // backstage: crates, barrels, the costume rack, the sheep, the letters, the ghost light
        new(-8.8f, -6.0f, 12.6f, 14f, StageY), new(-6.75f, -5.0f, 12.3f, 14f, StageY), new(-4.8f, -3.3f, 12.7f, 14f, StageY), new(-4.3f, -3.4f, 8.9f, 9.7f, StageY),
        new(-6.3f, -5.7f, 8.9f, 11.5f, StageY), new(-7.4f, -6.6f, 11.0f, 12.2f, StageY), new(-3.9f, -3.4f, 10.0f, 11.8f, StageY), new(0.3f, 0.9f, 7.0f, 7.6f, StageY),
        // the dressing room: the vanity, the folding screen, the halo's stand
        new(4.5f, 6.7f, 13.0f, 14f, StageY), new(7.8f, 8.8f, 12.1f, 13.8f, StageY), new(8.15f, 8.65f, 8.75f, 9.25f, StageY),
        // the cellar: the machine and its lever, the shrine, crates, the bench, barrels, the pipes, the gear
        new(-6.0f, -3.25f, 9.85f, 12.55f, BasementFloor), new(-2.75f, -2.25f, 12.35f, 12.85f, BasementFloor), new(6.7f, 8.1f, 15.2f, 16.5f, BasementFloor),
        new(-8.8f, -7.5f, 14.0f, 16.2f, BasementFloor), new(2.9f, 5.1f, 15.25f, 16.5f, BasementFloor), new(-8.8f, -7.1f, 6.5f, 8.2f, BasementFloor),
        new(5.5f, 6.5f, 6.5f, 7.5f, BasementFloor), new(7.8f, 8.8f, 9.1f, 10.1f, BasementFloor), new(8.3f, 8.8f, 7.1f, 8.9f, BasementFloor), new(8.2f, 8.8f, 11.1f, 12.5f, BasementFloor),
        // the booth: the projectors, the follow spot, the shelf, the bench
        new(-0.3f, 0.3f, -15.7f, -14.2f, ProjFloor), new(1.4f, 2.0f, -15.7f, -14.2f, ProjFloor), new(-2.05f, -1.35f, -15.4f, -14.2f, ProjFloor),
        new(-1.0f, 2.6f, -17.4f, -16.95f, ProjFloor), new(2.75f, 4.25f, -16.85f, -16.15f, ProjFloor),
        // under the booth's stair and landing, from the balcony
        new(ProjStairX0 + ProjStepRun, ProjDoorX + 0.6f, -13.8f, -13.05f, 5.72f)
    };

    private static float RoomHeight(FloorKind kind, float x, float z) => kind switch
    {
        FloorKind.Cellar => CellarHeight(z),
        FloorKind.Basement => BasementFloor,
        FloorKind.ProjStair => ProjStairHeight(x),
        FloorKind.Booth => ProjFloor,
        FloorKind.Arcade => LobbyFloor,
        FloorKind.Office => LobbyFloor,
        _ => StageY
    };

    /// <summary>The floor under a point in the rooms added in 1.0, or NaN (the hall and lobby work it out themselves).</summary>
    private static float RoomSurfaceUnder(Vector3 p)
    {
        var room = RoomOf(p.x, p.y, p.z, 0, 0, 0);
        switch (room)
        {
            case Room.Basement:
                return BasementFloor;
            case Room.Booth:
                return ProjFloor;
            case Room.Arcade:
            case Room.Office:
                return LobbyFloor;
            case Room.Dressing:
                if (p.x > 4.65f && p.x < 6.55f && p.z > 13.38f && p.y > StageY + 0.74f) return StageY + 0.75f; // the dressing table
                return StageY;
            case Room.Backstage:
                if (Mathf.Abs(p.x) < CellarHalf && p.z > CellarTopZ) return CellarHeight(p.z);
                return StageY;
            default:
                return float.NaN;
        }
    }

    /// <summary>The box a thrown thing is kept inside, in the rooms added in 1.0 (false for the hall and lobby).</summary>
    private static bool RoomBounds(Vector3 p, out Vector3 min, out Vector3 max)
    {
        switch (RoomOf(p.x, p.y, p.z, 0, 0, 0))
        {
            case Room.Backstage:
                min = new Vector3(-Half, StageY, ProsZ + 0.1f);
                max = new Vector3(Half, BackstageCeiling, BackstageBackZ);
                return true;
            case Room.Dressing:
                min = new Vector3(DressingX, StageY, DressingZ);
                max = new Vector3(Half, DressingCeiling, BackstageBackZ);
                return true;
            case Room.Basement:
                min = new Vector3(-Half, BasementFloor, CellarFrontZ);
                max = new Vector3(Half, BasementTop, CellarBackZ);
                return true;
            case Room.Booth:
                min = new Vector3(ProjX0, ProjFloor, ProjBackZ);
                max = new Vector3(ProjX1, ProjCeiling, LobbyZ);
                return true;
            case Room.Arcade:
                min = new Vector3(ArcadeX0, LobbyFloor, ArcadeZ0);
                max = new Vector3(ArcadeX1, ArcadeCeiling, ArcadeZ1);
                return true;
            case Room.Office:
                min = new Vector3(OfficeX0, LobbyFloor, OfficeZ0);
                max = new Vector3(OfficeX1, OfficeCeiling, OfficeZ1);
                return true;
            default:
                min = max = Vector3.zero;
                return false;
        }
    }
}
