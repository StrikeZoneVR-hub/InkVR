namespace InkVR.UI;

/// <summary>
/// Where the rooms added in 1.0 are (theatre metres: +z towards the screen, +x to the right, the hall's centre aisle
/// at the origin):
/// - backstage behind the screen, through the wings either side of the stage: a crossover corridor, the prop store
///   and fly gallery on the left, Alice's dressing room on the right, the stairs down to the basement in the middle;
/// - the basement under the backstage, with the ink machine;
/// - the projection booth, hung over the lobby doors behind the hall's back wall, up a short stair from the balcony;
/// - the Fun Hall (the arcade) through a door at the foot of the lobby's right-hand stairs;
/// - Joey's office through the matching door on the left, locked until every fan find is found.
/// </summary>
public sealed partial class Theatre
{
    // ---- backstage (floor at the stage's height) ----
    private const float BackstageZ = 6.32f; // behind the masking (the screen is at 6.0, the masking at 6.25)
    private const float BackstageBackZ = 14.0f;
    private const float BackstageCeiling = 8.6f;
    private const float WingZ = ProsZ + 0.02f; // the wings: beside the stage, behind the proscenium wall
    private const float CrossoverZ = 8.4f; // the corridor behind the screen runs from BackstageZ to here
    private const float DressingX = 3.2f, DressingZ = CrossoverZ, DressingCeiling = 3.4f; // Alice's room: x 3.2..9, z 8.4..14
    private const float DressingDoorX = 4.6f, DressingDoorHalf = 0.5f, DoorHeight = 2.15f;
    private const float PropsX = -3.2f; // the prop store and fly gallery: x -9..-3.2, z 8.4..14 (open to the corridor)

    // ---- the stairs down, and the basement ----
    private const float BasementFloor = -3.2f, BasementTop = 0.3f;
    private const int CellarSteps = 18;
    private const float CellarTread = 0.29f, CellarHalf = 0.75f, CellarTopZ = 8.9f;
    private static float CellarRise => (StageY - BasementFloor) / CellarSteps;
    private static float CellarBottomZ => CellarTopZ + CellarSteps * CellarTread;

    // ---- the projection booth (its floor 1.6 m above the balcony's top tier) ----
    private const float ProjX0 = -3.0f, ProjX1 = 4.4f, ProjBackZ = -17.4f, ProjFloor = 7.3f, ProjCeiling = 9.52f;
    private const float ProjDoorX = 3.55f, ProjDoorHalf = 0.4f, ProjDoorHeight = 2.05f;
    private const int ProjSteps = 5;
    private const float ProjStepRun = 0.36f, ProjStairX0 = 1.1f, ProjStairZ0 = -13.78f, ProjStairZ1 = -13.12f;
    private static float ProjRise => (ProjFloor - BalconyTop) / ProjSteps;
    private static float ProjStairX1 => ProjStairX0 + ProjSteps * ProjStepRun;

    // ---- the Fun Hall ----
    private const float ArcadeX0 = 8.25f, ArcadeX1 = 22.25f, ArcadeZ0 = -27.0f, ArcadeZ1 = -15.0f, ArcadeCeiling = 6.4f;
    private const float SideDoorZ = -23.05f, SideDoorHalf = 0.55f, SideDoorHeight = 2.3f;

    /// <summary>A flat-ended cylinder from a to b (a reel, a can, a gauge's bezel): MeshBuilder's tubes have rounded caps.</summary>
    private static void Drum(UnityEngine.Vector3 a, UnityEngine.Vector3 b, float r, int sides, Rendering.MeshBuilder mb)
    {
        var axis = (b - a).normalized;
        var up = UnityEngine.Mathf.Abs(axis.y) < 0.9f ? UnityEngine.Vector3.up : UnityEngine.Vector3.forward;
        mb.Tube(new[] { a, b }, new[] { r, r }, up, sides, false, false);
        var u = UnityEngine.Vector3.Cross(axis, up).normalized * r;
        var v = UnityEngine.Vector3.Cross(axis, u).normalized * r;
        // each disc faces out of its end (Disc faces Cross(up, right))
        mb.Disc(b, v, u, sides);
        mb.Disc(a, u, v, sides);
    }

    // ---- Joey's office ----
    private const float OfficeX0 = -14.5f, OfficeX1 = -8.25f, OfficeZ0 = -25.0f, OfficeZ1 = -18.5f, OfficeCeiling = 4.4f;
}
