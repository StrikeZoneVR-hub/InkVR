using System.Collections.Generic;
using InkVR.Rendering;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The parts of the picture palace you can walk to: the lobby through the doors at the back (a ticket booth with a
/// desk bell, the concession counter and its Bacon Soup, an upright piano, the posters and a plaque), the twin
/// staircases up to the balcony, the steps up onto the stage, and a few things tucked away to find. Also the
/// walk map: where your head may go, and how high the floor is there.
/// </summary>
public sealed partial class Theatre
{
    // ---- layout ----

    private const float WallThick = 0.25f;
    private const float LobbyZ = BackZ - WallThick; // the lobby's face of the back wall
    private const float LobbyFrontZ = -24f, LobbyHalf = 8f, LobbyCeiling = 9.6f;
    private const float LobbyDoorHalf = 1.1f, LobbyDoorHeight = 2.3f;
    private const float BalconyDoorX = 7.0f, BalconyDoorHalf = 0.6f, BalconyDoorHeight = 2.2f;
    private const float StairIn = 6.2f, StairTread = 0.27f;
    private const int StairSteps = 26;
    private const float StepsOuter = 7.4f, StepW = 0.4f;
    private const float BoothX = -3.6f, BoothFrontZ = -22.7f;
    private const float CounterX = 3.2f, CounterFrontZ = -22.9f;
    private const float PianoZ = -17.9f;
    private const float WritingZ = -17.0f;
    private const float BenchX = 2.7f, BenchZ = -19.1f;

    private static float LobbyFloor => RowFloor(Rows - 1);
    private static float BalconyTop => BalconyFloor(BalconyRows - 1);
    private static float StairRise => (BalconyTop - LobbyFloor) / StairSteps;
    private static float StairBottomZ => LobbyZ - 1.27f - (StairSteps - 1) * StairTread;

    private static float StairHeight(float z)
    {
        if (z < StairBottomZ) return LobbyFloor;
        var i = Mathf.Min(StairSteps - 1, Mathf.FloorToInt((z - StairBottomZ) / StairTread));
        return LobbyFloor + (i + 1) * StairRise;
    }

    private static float StageStepHeight(float ax) =>
        ax <= StepsOuter - 3 * StepW ? StageY : StageY * Mathf.Clamp(Mathf.Ceil((StepsOuter - ax) / StepW - 1e-4f), 0, 3) / 3f;

    private static float BalconyFloorAt(float z)
    {
        for (var b = 0; b < BalconyRows; b++)
        {
            var z1 = b == 0 ? BalconyZ - 0.3f : BalconyRowZ(b) + RowGap * 0.5f;
            var z0 = b == BalconyRows - 1 ? BackZ : BalconyRowZ(b) - RowGap * 0.5f;
            if (z >= z0 - 1e-4f && z <= z1 + 1e-4f) return BalconyFloor(b);
        }

        return BalconyTop;
    }

    private static bool InLobby(Vector3 p, Vector3 n) => p.z < BackZ - 0.12f || (p.z < BackZ - 0.01f && n.z < -0.5f);

    private static Vector4[] BalconyDoors(float top) => new[]
    {
        new Vector4(-BalconyDoorX - BalconyDoorHalf, top, -BalconyDoorX + BalconyDoorHalf, top + BalconyDoorHeight),
        new Vector4(BalconyDoorX - BalconyDoorHalf, top, BalconyDoorX + BalconyDoorHalf, top + BalconyDoorHeight)
    };

    // =====================================================================================================
    // The walk map
    // =====================================================================================================

    /// <summary>The highest step you can climb (or step down) in one go: a balcony tier is 38 cm.</summary>
    public const float StepUp = 0.42f;

    private enum FloorKind
    {
        Hall,
        Level,
        Stage,
        StageSteps,
        Lobby,
        Stairs,
        BalconyTop,
        Balcony
    }

    private readonly struct Slab
    {
        public readonly float X0, X1, Z0, Z1;
        public readonly FloorKind Kind;

        public Slab(float x0, float x1, float z0, float z1, FloorKind kind)
        {
            X0 = x0;
            X1 = x1;
            Z0 = z0;
            Z1 = z1;
            Kind = kind;
        }
    }

    private readonly struct Blocker
    {
        public readonly float X0, X1, Z0, Z1, Floor;

        public Blocker(float x0, float x1, float z0, float z1, float floor)
        {
            X0 = x0;
            X1 = x1;
            Z0 = z0;
            Z1 = z1;
            Floor = floor;
        }
    }

    // Where the centre of your head may be (20 cm or so clear of walls, seats and rails).
    private static readonly Slab[] Slabs = MakeSlabs();

    private static Slab[] MakeSlabs()
    {
        var list = new List<Slab>
        {
            // the main floor: the centre aisle, the cross-aisles front and back
            new(-0.88f, 0.88f, -13.75f, 4.0f, FloorKind.Hall),
            new(-8.5f, 8.5f, 3.45f, 4.0f, FloorKind.Level),
            new(-8.5f, 8.5f, -13.75f, -10.8f, FloorKind.Hall),
            // the stage: the apron and the boards in front of the screen
            new(-6.0f, 6.0f, 4.3f, 5.0f, FloorKind.Stage),
            new(-5.2f, 5.2f, 5.0f, 5.75f, FloorKind.Stage),
            // through the doors into the lobby
            new(-0.9f, 0.9f, LobbyZ - 0.2f, -13.75f, FloorKind.Lobby),
            new(-6.0f, 6.0f, LobbyFrontZ + 0.25f, LobbyZ - 0.2f, FloorKind.Lobby),
            // the balcony: across the back, down the aisles
            new(-8.5f, 8.5f, -13.75f, -12.55f, FloorKind.BalconyTop),
            new(-0.88f, 0.88f, -13.75f, BalconyZ - 0.5f, FloorKind.Balcony)
        };
        foreach (var s in new[] { -1f, 1f })
        {
            list.Add(Mirror(8.05f, 8.5f, -13.75f, 4.0f, FloorKind.Hall, s)); // side aisles
            list.Add(Mirror(7.4f, 8.5f, 3.45f, 4.85f, FloorKind.Level, s)); // by the stage steps
            list.Add(Mirror(6.0f, 7.4f, 4.0f, 4.85f, FloorKind.StageSteps, s));
            list.Add(Mirror(6.0f, 7.8f, LobbyFrontZ + 0.25f, StairBottomZ - 0.25f, FloorKind.Lobby, s)); // the foot of the stairs
            list.Add(Mirror(6.45f, 7.6f, StairBottomZ - 0.25f, LobbyZ - 0.2f, FloorKind.Stairs, s));
            list.Add(Mirror(BalconyDoorX - BalconyDoorHalf + 0.25f, BalconyDoorX + BalconyDoorHalf - 0.25f, LobbyZ - 0.2f, -13.75f, FloorKind.BalconyTop, s));
            list.Add(Mirror(8.05f, 8.5f, -13.75f, BalconyZ - 0.5f, FloorKind.Balcony, s));
        }

        return list.ToArray();
    }

    private static Slab Mirror(float x0, float x1, float z0, float z1, FloorKind kind, float s) =>
        s > 0 ? new Slab(x0, x1, z0, z1, kind) : new Slab(-x1, -x0, z0, z1, kind);

    private static readonly Blocker[] Blockers =
    {
        new(BoothX - 1.2f, BoothX + 1.2f, LobbyFrontZ, BoothFrontZ + 0.42f, 1.2f),
        new(CounterX - 1.6f, CounterX + 1.6f, LobbyFrontZ, CounterFrontZ + 0.2f, 1.2f),
        new(-6.0f, -5.12f, PianoZ - 0.82f, PianoZ + 0.82f, 1.2f),
        new(BenchX - 0.47f, BenchX + 0.47f, BenchZ - 1.0f, BenchZ + 1.0f, 1.2f),
        new(-BenchX - 0.47f, -BenchX + 0.47f, BenchZ - 1.0f, BenchZ + 1.0f, 1.2f),
        new(ApronHalf - 1.3f, ApronHalf - 0.2f, ApronZ, ApronZ + 0.75f, StageY),
        new(-ApronHalf + 0.2f, -ApronHalf + 1.3f, ApronZ, ApronZ + 0.75f, StageY)
    };

    private static float HeightOf(FloorKind kind, float x, float z) => kind switch
    {
        FloorKind.Hall => FloorAt(z),
        FloorKind.Level => 0f,
        FloorKind.Stage => StageY,
        FloorKind.StageSteps => StageStepHeight(Mathf.Abs(x)),
        FloorKind.Lobby => LobbyFloor,
        FloorKind.Stairs => StairHeight(z),
        FloorKind.BalconyTop => BalconyTop,
        _ => BalconyFloorAt(z)
    };

    /// <summary>
    /// Can your head be over (x, z) when you're standing at <paramref name="level"/>, and how high is the floor
    /// there? Of the floors at that spot, the highest within a step of where you are (so the balcony and the
    /// floor under it are kept apart); none, or a drop, and the answer is no.
    /// </summary>
    public static bool Walkable(float x, float z, float level, out float floor)
    {
        floor = level;
        var found = false;
        foreach (var s in Slabs)
        {
            if (x < s.X0 || x > s.X1 || z < s.Z0 || z > s.Z1) continue;
            var h = HeightOf(s.Kind, x, z);
            if (h > level + StepUp || (found && h <= floor)) continue;
            floor = h;
            found = true;
        }

        if (!found || floor < level - StepUp) return false;
        foreach (var b in Blockers)
            if (x > b.X0 && x < b.X1 && z > b.Z0 && z < b.Z1 && Mathf.Abs(floor - b.Floor) < 0.1f)
                return false;
        return true;
    }

    /// <summary>What's underfoot at p, for your footsteps: the carpet runners (the aisles, the lobby, the stairs), else boards.</summary>
    public static TheatreSounds.Floor FloorSound(Vector3 p)
    {
        var ax = Mathf.Abs(p.x);
        if (p.z > BackZ) return ax < AisleHalf && p.z < ApronZ ? TheatreSounds.Floor.Carpet : TheatreSounds.Floor.Wood;
        if (ax < 1f) return TheatreSounds.Floor.Carpet;
        if (Mathf.Abs(ax - (StairIn + LobbyHalf) * 0.5f) < 0.5f && p.z > StairBottomZ) return TheatreSounds.Floor.Carpet;
        return TheatreSounds.Floor.Wood;
    }

    /// <summary>The screen can be seen (and pointed at) from the hall, the stage and the balcony, not the lobby.</summary>
    public static bool SeesScreen(Vector3 eye) => eye.z > BackZ - 0.1f;

    /// <summary>The surface something dropped at p comes to rest on (floors, steps, the counter, the balcony rail...).</summary>
    public static float SurfaceUnder(Vector3 p)
    {
        var best = float.NegativeInfinity;
        void Try(float h)
        {
            if (h <= p.y + 0.02f && h > best) best = h;
        }

        float ax = Mathf.Abs(p.x), z = p.z;
        if (z > BackZ && z < ApronZ && ax < Half) Try(FloorAt(z));
        if (z >= ApronZ && z < ProsZ && ax < Half) Try(ax <= ApronHalf ? StageY : StageStepHeight(ax));
        if (z >= ProsZ && z < ScreenZ + 0.3f && ax < OpenHalf) Try(StageY);
        if (z > BackZ && z < BalconyZ - 0.3f && ax < Half) Try(BalconyFloorAt(z));
        if (z > BalconyZ - 0.37f && z < BalconyZ + 0.07f && ax < Half) Try(ParapetTop + 0.08f);
        if (z <= BackZ && z > LobbyFrontZ && ax < LobbyHalf)
        {
            Try(LobbyFloor);
            if (ax > StairIn) Try(StairHeight(z));
            if (Mathf.Abs(p.x - CounterX) < 1.45f && z < CounterFrontZ + 0.05f) Try(LobbyFloor + 1.05f);
        }

        return float.IsNegativeInfinity(best) ? FloorAt(z) : best;
    }

    /// <summary>Keeps something thrown inside the rooms: off the walls, through the doorways only.</summary>
    public static void Contain(ref Vector3 p, ref Vector3 v, float r)
    {
        var lobby = p.z < BackZ - WallThick * 0.5f;
        var halfX = (lobby ? LobbyHalf : Half) - r;
        if (Mathf.Abs(p.x) > halfX)
        {
            p.x = Mathf.Sign(p.x) * halfX;
            v.x = -v.x * 0.4f;
        }

        var top = (lobby ? LobbyCeiling : CeilingY) - r;
        if (p.y > top)
        {
            p.y = top;
            v.y = -v.y * 0.3f;
        }

        if (!lobby && p.z > ScreenZ - r)
        {
            p.z = ScreenZ - r;
            v.z = -v.z * 0.4f;
        }

        if (lobby && p.z < LobbyFrontZ + r)
        {
            p.z = LobbyFrontZ + r;
            v.z = -v.z * 0.4f;
        }

        // the back wall: only through a doorway
        if (p.z < BackZ + r && p.z > LobbyZ - r)
        {
            var throughDoor = Mathf.Abs(p.x) < LobbyDoorHalf - r && p.y < LobbyFloor + LobbyDoorHeight - r;
            var throughUpper = Mathf.Abs(Mathf.Abs(p.x) - BalconyDoorX) < BalconyDoorHalf - r && p.y > BalconyTop && p.y < BalconyTop + BalconyDoorHeight - r;
            if (!throughDoor && !throughUpper)
            {
                var toHall = v.z < 0 ? false : true;
                p.z = lobby ? LobbyZ - r : BackZ + r;
                if (lobby == toHall) v.z = -v.z * 0.4f;
            }
        }
    }

    // =====================================================================================================
    // Things to play with (TheatreToys moves them)
    // =====================================================================================================

    public struct KeyInfo
    {
        public int Note, V0, V1;
        public bool Black;
        public float Z, HalfW, X0, X1, Top;
    }

    /// <summary>The piano's 88 keys, A0 to C8 (positions in the theatre; vertex ranges in <see cref="KeysMesh"/>).</summary>
    public readonly List<KeyInfo> Keys = new();

    private readonly Dictionary<string, Mesh> _groupMeshes = new();

    public Mesh KeysMesh => _groupMeshes.TryGetValue("Trim_Keys", out var m) ? m : null;

    public static Vector3 BellBase => new(BoothX + 0.28f, LobbyFloor + 1.04f, BoothFrontZ + 0.13f);
    public static Vector3 BellTop => BellBase + Vector3.up * 0.057f;
    public static Vector3 PianoCentre => new(-5.6f, LobbyFloor + 0.8f, PianoZ);
    public static Vector3 DollAt => new(0.55f, ParapetTop + 0.08f, BalconyZ - 0.16f);
    public static Vector3 WritingAt => new(StairIn - 0.065f, LobbyFloor + 1.85f, WritingZ);
    public static Vector3 LobbyDoorway => new(0, LobbyFloor, (BackZ + LobbyZ) * 0.5f);
    public const int CanCount = 6;
    public const float CanRadius = 0.037f, CanHeight = 0.105f;

    /// <summary>Where each can on the counter stands (the bottom of the can).</summary>
    public static Vector3 CanRest(int i)
    {
        var start = new Vector3(CounterX - 0.75f, LobbyFloor + 1.05f, CounterFrontZ - 0.16f);
        return i switch
        {
            < 3 => start + Vector3.right * (i * 0.078f),
            < 5 => start + new Vector3(0.039f + (i - 3) * 0.078f, CanHeight, 0),
            _ => start + new Vector3(0.078f, CanHeight * 2, 0)
        };
    }

    /// <summary>The cut-out's hiding places (foot, facing).</summary>
    public static readonly (Vector3 foot, float yaw)[] CutoutSpots =
    {
        (new Vector3(-3.0f, 1.2f, -15.2f), 200f),
        (new Vector3(-3.2f, 1.2f, -16.9f), 120f),
        (new Vector3(-1.8f, 1.2f, -22.1f), 30f),
        (new Vector3(5.1f, 1.2f, -20.4f), -60f),
        (new Vector3(7.25f, 5.72f, -15.0f), 180f),
        (new Vector3(1.9f, 1.2f, -15.2f), 160f)
    };

    /// <summary>Doors that are locked: (bottom centre, facing, across, half width, height).</summary>
    public static readonly (Vector3 bottom, Vector3 n, Vector3 right, float halfW, float h)[] LockedDoors =
    {
        (new Vector3(Half - 0.02f, 0, 2.0f), Vector3.left, Vector3.forward, 0.75f, 2.4f),
        (new Vector3(-Half + 0.02f, 0, 2.0f), Vector3.right, Vector3.forward, 0.75f, 2.4f),
        (new Vector3(6f, 1.2f, BackZ + 0.02f), Vector3.forward, Vector3.right, 0.65f, 2.1f),
        (new Vector3(-6f, 1.2f, BackZ + 0.02f), Vector3.forward, Vector3.right, 0.65f, 2.1f),
        (new Vector3(0, 1.2f, LobbyFrontZ + 0.02f), Vector3.forward, Vector3.right, 1.3f, 2.6f)
    };

    // =====================================================================================================
    // Building
    // =====================================================================================================

    private Part _tin, _label, _drips, _sheet;
    private static readonly Color Ivory = new(0.98f, 0.95f, 0.86f);
    private static readonly Color TinColor = new(0.8f, 0.77f, 0.72f);

    private void BuildLobby()
    {
        _tin = NewPart("Tin", TheatreArt.Tin());
        _label = NewPart("CanLabel", TheatreArt.CanLabel());
        _drips = NewPart("Drips", TheatreArt.Drips(), true, 2955);
        _sheet = NewPart("Sheet", TheatreArt.SheetMusic());
        BuildDoorways();
        BuildStageSteps();
        BuildLobbyRoom();
        BuildStairs();
        BuildBooth();
        BuildBenches();
        BuildCounter();
        BuildPiano();
        BuildMirror();
        BuildLobbyWallArt();
        BuildWriting();
        BuildHallSecrets();
        InGroup("Cutout", CutoutSpots[0].foot, () => Standee(CutoutSpots[0].foot, "bendy_pose_14", 0f));
    }

    private void PlanLobbyLamps()
    {
        var f = LobbyFloor;
        var warm = new Color(1f, 0.8f, 0.52f);
        AddLamp(new Vector3(0, 5.6f, -19f), warm * 0.55f, 8f);
        AddLamp(new Vector3(0, 6.6f, -19.1f), warm * 1.0f, 4f);
        foreach (var (at, n) in LobbySconces()) AddLamp(at + n * 0.5f + Vector3.up * 0.25f, new Color(1f, 0.66f, 0.32f) * 1.5f, 1.8f, Vector3.up, 0.5f);
        AddLamp(new Vector3(BoothX, f + 1.85f, BoothFrontZ - 0.3f), new Color(1f, 0.85f, 0.6f) * 1.1f, 1.0f);
        AddLamp(new Vector3(BoothX, f + 2.9f, BoothFrontZ + 0.6f), warm * 0.8f, 1.6f, Vector3.down, 0.5f);
        AddLamp(new Vector3(CounterX, f + 2.3f, CounterFrontZ), warm * 1.1f, 1.8f, Vector3.down, 0.4f);
        AddLamp(Candle + Vector3.up * 0.3f, new Color(1f, 0.7f, 0.38f) * 1.0f, 1.3f);
        foreach (var x in new[] { -3.6f, 3.6f }) AddLamp(new Vector3(x, f + 2.9f, LobbyZ - 0.45f), new Color(1f, 0.85f, 0.6f) * 1.2f, 0.95f, Vector3.forward + Vector3.down, 0.4f);
        foreach (var s in new[] { -1f, 1f })
        {
            AddLamp(new Vector3(s * BalconyDoorX, BalconyTop + 2.5f, LobbyZ - 0.4f), warm * 0.8f, 1.6f);
            AddLamp(new Vector3(s * BalconyDoorX, BalconyTop + 2.4f, BackZ + 0.4f), warm * 0.7f, 1.5f); // (the hall side)
        }

        AddLamp(new Vector3(0, f + 3.1f, LobbyFrontZ + 0.4f), warm * 0.7f, 1.5f);
        AddLamp(new Vector3(StairIn - 1.2f, f + 2.1f, WritingZ), new Color(1f, 0.7f, 0.42f) * 0.6f, 1.4f);
        AddLamp(new Vector3(-4.9f, f + 2.4f, MirrorZ + 0.4f), warm * 0.9f, 2.4f); // in front of the mirror
        AddLamp(MirrorAt + new Vector3(0.35f, MirrorH * 0.5f + 0.1f, 0), new Color(1f, 0.85f, 0.6f) * 0.9f, 1.2f, Vector3.down, 0.5f);
    }

    private static Vector3 Candle => new(-5.86f, LobbyFloor + 1.4f, PianoZ - 0.55f);

    private static IEnumerable<(Vector3 at, Vector3 n)> LobbySconces()
    {
        var f = LobbyFloor;
        foreach (var s in new[] { -1f, 1f })
        {
            yield return (new Vector3(s * 2.15f, f + 2.3f, LobbyFrontZ + 0.02f), Vector3.forward);
            yield return (new Vector3(s * 2.2f, f + 2.3f, LobbyZ - 0.02f), Vector3.back);
            foreach (var z in new[] { -20.2f, -16.9f })
                yield return (new Vector3(s * (LobbyHalf - 0.02f), StairHeight(z) + 2.0f, z), new Vector3(-s, 0, 0));
        }
    }

    private void SconceN(Vector3 at, Vector3 n)
    {
        var right = Vector3.Cross(Vector3.up, n).normalized;
        Box(_box, at + n * 0.06f + Vector3.down * 0.03f, Abs(right * 0.2f + Vector3.up * 0.06f + n * 0.12f), Gold);
        Fan(_box, at + n * 0.1f, right, 0.3f, 7, new Color(1f, 0.86f, 0.58f));
        Glow(at + n * 0.2f + Vector3.up * 0.14f, 1.1f, new Color(1f, 0.7f, 0.36f, 0.5f), GlowGroup.Flicker);
        Glow(at + n * 0.14f + Vector3.up * 0.1f, 0.38f, new Color(1f, 0.9f, 0.7f, 0.85f), GlowGroup.Flicker);
    }

    /// <summary>A wall in the plane origin + right * u + up * v, with rectangular holes (u0, v0, u1, v1).</summary>
    private void WallWithHoles(Part p, Vector3 origin, Vector3 right, float u0, float u1, float v0, float v1, Vector3 n, Color tint, Vector4[] holes, float tileU, float tileV)
    {
        var cuts = new List<float> { u0, u1 };
        foreach (var h in holes)
        {
            if (h.x > u0 && h.x < u1) cuts.Add(h.x);
            if (h.z > u0 && h.z < u1) cuts.Add(h.z);
        }

        cuts.Sort();
        for (var i = 0; i < cuts.Count - 1; i++)
        {
            float ua = cuts[i], ub = cuts[i + 1];
            if (ub - ua < 1e-3f) continue;
            var mid = (ua + ub) * 0.5f;
            var spans = new List<(float a, float b)> { (v0, v1) };
            foreach (var h in holes)
            {
                if (mid < h.x || mid > h.z) continue;
                var next = new List<(float, float)>();
                foreach (var (a, b) in spans)
                {
                    if (h.w <= a || h.y >= b)
                    {
                        next.Add((a, b));
                        continue;
                    }

                    if (h.y > a) next.Add((a, h.y));
                    if (h.w < b) next.Add((h.w, b));
                }

                spans = next;
            }

            foreach (var (a, b) in spans)
            {
                if (b - a < 1e-3f) continue;
                var pa = origin + right * ua + Vector3.up * a;
                var pb = origin + right * ub + Vector3.up * a;
                Patch(p, pa, pb, pb + Vector3.up * (b - a), pa + Vector3.up * (b - a), n, tint, Sub(ub - ua, 1.2f), Sub(b - a, 1.2f), tileU, tileV);
            }
        }
    }

    /// <summary>The sides, head and threshold of an opening through the back wall.</summary>
    private void Reveal(float x, float floor, float half, float height, bool carpet)
    {
        float z0 = BackZ, z1 = LobbyZ, x0 = x - half, x1 = x + half, y0 = floor, y1 = floor + height;
        Patch(_box, new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.right, WoodDark, 1, 2);
        Patch(_box, new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), Vector3.left, WoodDark, 1, 2);
        Patch(_box, new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.down, WoodDark, 2, 1);
        Patch(carpet ? _carpet : _wood, new Vector3(x0, y0 + 0.004f, z1), new Vector3(x1, y0 + 0.004f, z1), new Vector3(x1, y0 + 0.004f, z0), new Vector3(x0, y0 + 0.004f, z0),
            Vector3.up, carpet ? Carpet : Wood, 2, 1);
    }

    private void BuildDoorways()
    {
        var f = LobbyFloor;
        Reveal(0, f, LobbyDoorHalf, LobbyDoorHeight, true);
        Doorway(new Vector3(0, f, LobbyZ - 0.02f), Vector3.right, Vector3.back, LobbyDoorHalf * 2, LobbyDoorHeight, false);
        foreach (var s in new[] { -1f, 1f })
        {
            Reveal(s * BalconyDoorX, BalconyTop, BalconyDoorHalf, BalconyDoorHeight, false);
            Doorway(new Vector3(s * BalconyDoorX, BalconyTop, LobbyZ - 0.02f), Vector3.right, Vector3.back, BalconyDoorHalf * 2, BalconyDoorHeight, false);

            // The auditorium doors: two leaves that swing open into the lobby as you come near.
            var hinge = new Vector3(s * LobbyDoorHalf, f, (BackZ + LobbyZ) * 0.5f);
            InGroup(s < 0 ? "DoorL" : "DoorR", hinge, () =>
            {
                Box(_door, hinge + new Vector3(-s * LobbyDoorHalf * 0.5f, LobbyDoorHeight * 0.5f, 0), new Vector3(LobbyDoorHalf - 0.012f, LobbyDoorHeight - 0.01f, 0.05f), Wood);
                foreach (var side in new[] { -1f, 1f })
                    Box(_box, hinge + new Vector3(-s * (LobbyDoorHalf - 0.13f), 1.08f, side * 0.033f), new Vector3(0.07f, 0.26f, 0.012f), Gold);
            });
        }
    }

    private void BuildStageSteps()
    {
        var depth = ProsZ - ApronZ;
        foreach (var s in new[] { -1f, 1f })
        {
            // the floor beside the apron, and three steps up onto it
            Patch(_wood, new Vector3(s * ApronHalf, 0, ApronZ), new Vector3(s * Half, 0, ApronZ), new Vector3(s * Half, 0, ProsZ), new Vector3(s * ApronHalf, 0, ProsZ), Vector3.up, Wood, 3, 1, 2.2f, 2.2f);
            for (var k = 1; k <= 3; k++)
            {
                var h = StageY * k / 3f;
                var xo = StepsOuter - StepW * (k - 1);
                Box(_wood, new Vector3(s * (xo - StepW * 0.5f), h * 0.5f, ApronZ + depth * 0.5f), new Vector3(StepW, h, depth - 0.02f), WoodDark * 1.15f);
                Box(_box, new Vector3(s * (xo - 0.02f), h - 0.012f, ApronZ + depth * 0.5f), new Vector3(0.04f, 0.024f, depth - 0.02f), Gold);
            }
        }
    }

    private void BuildLobbyRoom()
    {
        float f = LobbyFloor, zf = LobbyFrontZ, zb = LobbyZ, hx = LobbyHalf, top = LobbyCeiling, cornice = top - 0.6f;
        var up = Vector3.up;

        // Floorboards, and the carpet runner from the auditorium doors to the street doors.
        foreach (var s in new[] { -1f, 1f })
            Patch(_wood, new Vector3(s * 1.0f, f, zf), new Vector3(s * hx, f, zf), new Vector3(s * hx, f, zb), new Vector3(s * 1.0f, f, zb), up, Wood, Sub(hx - 1f, 1.2f), Sub(zb - zf, 1.2f), 2.2f, 2.2f);
        Patch(_carpet, new Vector3(-1f, f + 0.004f, zf), new Vector3(1f, f + 0.004f, zf), new Vector3(1f, f + 0.004f, zb), new Vector3(-1f, f + 0.004f, zb), up, Carpet, 2, Sub(zb - zf, 0.8f),
            uvRect: new Vector4(0, zf / 1.6f, 1, zb / 1.6f));

        // Bendy's face set into the floor in the middle, ringed in brass.
        var inlay = new Vector3(0, f + 0.007f, -19.1f);
        for (var k = 0; k < 32; k++)
        {
            var a0 = k / 32f * Mathf.PI * 2;
            var a1 = (k + 1) / 32f * Mathf.PI * 2;
            var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
            var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
            Patch(_box, inlay + d0 * 0.9f, inlay + d1 * 0.9f, inlay + d1 * 1.0f, inlay + d0 * 1.0f, up, Gold, 1, 1, uvRect: new Vector4(0.3f, 0.02f, 0.7f, 0.98f));
            Patch(_posterBg, inlay + d0 * 0.001f - up * 0.001f, inlay + d1 * 0.001f - up * 0.001f, inlay + d1 * 0.9f - up * 0.001f, inlay + d0 * 0.9f - up * 0.001f, up, Dark * 2.2f, 1, 1, uvRect: new Vector4(0.45f, 0.45f, 0.55f, 0.55f));
        }

        if (_bendyFace != null)
        {
            var p = NewPart("Art_FloorFace", _bendyFace, true, 2965);
            var h = 0.62f;
            var o = inlay + up * 0.003f;
            Patch(p, o + new Vector3(-h, 0, h), o + new Vector3(h, 0, h), o + new Vector3(h, 0, -h), o + new Vector3(-h, 0, -h), up, new Color(0.9f, 0.85f, 0.78f), 2, 2);
        }

        // The side walls: panelling, a brass rail, wallpaper, the cornice.
        foreach (var s in new[] { -1f, 1f })
        {
            var x = s * hx;
            var n = new Vector3(-s, 0, 0);
            Patch(_wood, new Vector3(x, f, zf), new Vector3(x, f, zb), new Vector3(x, f + 1.1f, zb), new Vector3(x, f + 1.1f, zf), n, WoodDark, Sub(zb - zf, 1.2f), 1, 1.6f, 1.6f);
            Patch(_box, new Vector3(x - s * 0.03f, f + 1.1f, zf), new Vector3(x - s * 0.03f, f + 1.1f, zb), new Vector3(x - s * 0.03f, f + 1.2f, zb), new Vector3(x - s * 0.03f, f + 1.2f, zf), n, GoldDark, Sub(zb - zf, 1.2f), 1);
            Patch(_wall, new Vector3(x, f + 1.2f, zf), new Vector3(x, f + 1.2f, zb), new Vector3(x, cornice, zb), new Vector3(x, cornice, zf), n, Paper, Sub(zb - zf, 1.2f), Sub(cornice - f, 1.2f), 1.2f, 1.2f);
            Band(_ornament, new Vector3(x - s * 0.12f, cornice, zf), new Vector3(x - s * 0.12f, cornice, zb), up, 0.6f, n, Gold, 1.6f);
        }

        // The street wall (the entrance, boarded up) and the auditorium wall (open doorways), the same way.
        var noHoles = new Vector4[0];
        var doors = new List<Vector4> { new(-LobbyDoorHalf, f, LobbyDoorHalf, f + LobbyDoorHeight) };
        doors.AddRange(BalconyDoors(BalconyTop));
        foreach (var (z, n, holes) in new[] { (zf, Vector3.forward, noHoles), (zb, Vector3.back, doors.ToArray()) })
        {
            WallWithHoles(_wood, new Vector3(0, 0, z), Vector3.right, -hx, hx, f, f + 1.1f, n, WoodDark, holes, 1.6f, 1.6f);
            WallWithHoles(_box, new Vector3(0, 0, z + n.z * 0.03f), Vector3.right, -hx, hx, f + 1.1f, f + 1.2f, n, GoldDark, holes, 0, 0);
            WallWithHoles(_wall, new Vector3(0, 0, z), Vector3.right, -hx, hx, f + 1.2f, cornice, n, Paper, holes, 1.2f, 1.2f);
            Band(_ornament, new Vector3(-hx, cornice, z + n.z * 0.12f), new Vector3(hx, cornice, z + n.z * 0.12f), up, 0.6f, n, Gold, 1.6f);
        }

        Doorway(new Vector3(0, f, zf + 0.02f), Vector3.left, Vector3.forward, 2.6f, 2.6f);
        Box(_wood, new Vector3(0, f + 1.25f, zf + 0.09f), new Vector3(3.0f, 0.17f, 0.04f), Quaternion.Euler(0, 0, 17), WoodDark * 1.2f);
        Box(_wood, new Vector3(0, f + 1.25f, zf + 0.13f), new Vector3(3.0f, 0.17f, 0.04f), Quaternion.Euler(0, 0, -17), WoodDark * 1.2f);
        Box(_box, new Vector3(0, f + 3.2f, zf + 0.05f), new Vector3(2.9f, 0.5f, 0.08f), Dark);
        Signs.Add(new SignSpec { Text = "CLOSED FOR RENOVATION", Pos = new Vector3(0, f + 3.26f, zf + 0.1f), Rot = Quaternion.Euler(0, 180, 0), Height = 0.15f, Width = 2.7f, Color = Cream });
        Signs.Add(new SignSpec { Text = "by order of J. Drew", Pos = new Vector3(0, f + 3.06f, zf + 0.1f), Rot = Quaternion.Euler(0, 180, 0), Height = 0.07f, Width = 2.0f, Color = new Color(1f, 0.84f, 0.5f) });

        // Signs over the doorways.
        Box(_box, new Vector3(0, f + 2.75f, zb - 0.04f), new Vector3(1.9f, 0.32f, 0.06f), Dark);
        Signs.Add(new SignSpec { Text = "AUDITORIUM", Pos = new Vector3(0, f + 2.75f, zb - 0.08f), Rot = Quaternion.identity, Height = 0.16f, Width = 1.8f, Color = new Color(1f, 0.84f, 0.5f) });
        foreach (var s in new[] { -1f, 1f })
        {
            Box(_box, new Vector3(s * BalconyDoorX, BalconyTop + BalconyDoorHeight + 0.32f, zb - 0.04f), new Vector3(1.2f, 0.3f, 0.06f), Dark);
            Signs.Add(new SignSpec { Text = "BALCONY", Pos = new Vector3(s * BalconyDoorX, BalconyTop + BalconyDoorHeight + 0.32f, zb - 0.08f), Rot = Quaternion.identity, Height = 0.15f, Width = 1.1f, Color = new Color(1f, 0.84f, 0.5f) });
        }

        // A coffered ceiling and a chandelier.
        var xs = new[] { -8f, -4f, 0f, 4f, 8f };
        var zs = new[] { zf, -21.5f, -19f, -16.5f, zb };
        for (var i = 0; i < xs.Length - 1; i++)
        for (var k = 0; k < zs.Length - 1; k++)
            Patch(_coffer, new Vector3(xs[i], top, zs[k]), new Vector3(xs[i + 1], top, zs[k]), new Vector3(xs[i + 1], top, zs[k + 1]), new Vector3(xs[i], top, zs[k + 1]), Vector3.down, Plaster, 3, 3);
        foreach (var x in xs)
            if (Mathf.Abs(x) < hx - 0.1f)
                for (var k = 0; k < zs.Length - 1; k++)
                    Box(_box, new Vector3(x, top - 0.18f, (zs[k] + zs[k + 1]) * 0.5f), new Vector3(0.28f, 0.36f, zs[k + 1] - zs[k]), GoldDark, 1f, true);
        foreach (var z in zs)
            if (z > zf + 0.1f && z < zb - 0.1f)
                for (var i = 0; i < xs.Length - 1; i++)
                    Box(_box, new Vector3((xs[i] + xs[i + 1]) * 0.5f, top - 0.16f, z), new Vector3(xs[i + 1] - xs[i], 0.32f, 0.28f), GoldDark, 1f, true);
        Chandelier(new Vector3(0, 6.9f, -19.1f), top - 0.05f);

        foreach (var (at, n) in LobbySconces()) SconceN(at, n);
    }

    private void BuildStairs()
    {
        var f = LobbyFloor;
        var rise = StairRise;
        foreach (var s in new[] { -1f, 1f })
        {
            float xin = s * StairIn, xout = s * LobbyHalf, xc = (xin + xout) * 0.5f, w = LobbyHalf - StairIn;
            var prevTop = f;
            for (var i = 0; i < StairSteps; i++)
            {
                var z0 = StairBottomZ + i * StairTread;
                var z1 = i == StairSteps - 1 ? LobbyZ : z0 + StairTread;
                var top = f + (i + 1) * rise;
                Box(_wood, new Vector3(xc, (f + top) * 0.5f, (z0 + z1) * 0.5f), new Vector3(w, top - f, z1 - z0), WoodDark * 1.15f);
                // a carpet runner up the middle, and a brass nosing on every step
                Patch(_carpet, new Vector3(xc - 0.5f, top + 0.004f, z0), new Vector3(xc + 0.5f, top + 0.004f, z0), new Vector3(xc + 0.5f, top + 0.004f, z1), new Vector3(xc - 0.5f, top + 0.004f, z1),
                    Vector3.up, Carpet, 2, Sub(z1 - z0, 0.8f), uvRect: new Vector4(0, 0, 1, (z1 - z0) / 1.6f));
                Patch(_carpet, new Vector3(xc - 0.5f, prevTop, z0 - 0.004f), new Vector3(xc + 0.5f, prevTop, z0 - 0.004f), new Vector3(xc + 0.5f, top, z0 - 0.004f), new Vector3(xc - 0.5f, top, z0 - 0.004f),
                    Vector3.back, Carpet * 0.8f, 2, 1, uvRect: new Vector4(0, 0, 1, 0.1f));
                Box(_box, new Vector3(xc, top - 0.012f, z0 + 0.02f), new Vector3(w, 0.024f, 0.05f), Gold);
                prevTop = top;
            }

            // A closed stringer on the open side: a panel under the line of the steps, with a brass edge.
            var xs = s * (StairIn - 0.04f);
            float Line(float z) => Mathf.Max(f, Mathf.Min(BalconyTop, f + (z - StairBottomZ) / StairTread * rise) - 0.07f);
            for (var i = 0; i < StairSteps; i++)
            {
                var z0 = StairBottomZ + i * StairTread;
                var z1 = i == StairSteps - 1 ? LobbyZ : z0 + StairTread;
                Patch(_wood, new Vector3(xs, f, z0), new Vector3(xs, f, z1), new Vector3(xs, Line(z1), z1), new Vector3(xs, Line(z0), z0), new Vector3(-s, 0, 0), WoodDark * 0.95f, 1, Sub(Line(z1) - f, 1.2f), 1.6f, 1.6f);
            }

            var e0 = new Vector3(xs, Line(StairBottomZ + StairTread), StairBottomZ + StairTread);
            var e1 = new Vector3(xs, Line(StairBottomZ + (StairSteps - 1) * StairTread), StairBottomZ + (StairSteps - 1) * StairTread);
            Box(_box, (e0 + e1) * 0.5f, new Vector3(0.05f, 0.06f, (e1 - e0).magnitude), Quaternion.LookRotation(e1 - e0), Gold);

            // The balustrade on the open side: a baluster on every step, posts, a sloping rail, along the landing too.
            var xr = s * (StairIn + 0.05f);
            Vector3 Tread(int i) => new(xr, f + (i + 1) * rise, StairBottomZ + (i + 0.5f) * StairTread);
            for (var i = 0; i < StairSteps - 1; i++) Box(_box, Tread(i) + Vector3.up * 0.45f, new Vector3(0.022f, 0.9f, 0.022f), GoldDark);
            foreach (var i in new[] { 0, 8, 16, 24 }) Box(_wood, Tread(i) + Vector3.up * 0.5f, new Vector3(0.08f, 1.0f, 0.08f), WoodDark);
            var a = Tread(0) + Vector3.up * 0.95f;
            var b = Tread(24) + Vector3.up * 0.95f;
            Box(_box, (a + b) * 0.5f, new Vector3(0.06f, 0.05f, (b - a).magnitude), Quaternion.LookRotation(b - a), Wood);
            var landing0 = StairBottomZ + (StairSteps - 1) * StairTread;
            Box(_box, new Vector3(xr, BalconyTop + 0.95f, (landing0 + LobbyZ) * 0.5f), new Vector3(0.06f, 0.05f, LobbyZ - landing0), Wood);
            for (var z = landing0 + 0.12f; z < LobbyZ - 0.05f; z += 0.14f) Box(_box, new Vector3(xr, BalconyTop + 0.47f, z), new Vector3(0.022f, 0.94f, 0.022f), GoldDark);
            Box(_wood, new Vector3(xr, BalconyTop + 0.5f, LobbyZ - 0.06f), new Vector3(0.08f, 1.0f, 0.08f), WoodDark);
        }
    }

    private void BuildBenches()
    {
        var f = LobbyFloor;
        foreach (var x in new[] { -BenchX, BenchX })
        {
            Box(_wood, new Vector3(x, f + 0.18f, BenchZ), new Vector3(0.5f, 0.36f, 1.6f), WoodDark);
            Box(_fabric, new Vector3(x, f + 0.41f, BenchZ), new Vector3(0.56f, 0.11f, 1.68f), Velvet);
            foreach (var dz in new[] { -0.74f, 0.74f })
            foreach (var dx in new[] { -0.2f, 0.2f })
                Box(_box, new Vector3(x + dx, f + 0.03f, BenchZ + dz), new Vector3(0.06f, 0.06f, 0.06f), Gold);
        }
    }

    private void BuildBooth()
    {
        float f = LobbyFloor, cx = BoothX, zf = BoothFrontZ, zb = LobbyFrontZ + 0.02f, zc = (zf + zb) * 0.5f, depth = zf - zb;
        var up = Vector3.up;
        var fwd = Vector3.forward;
        var facing = Quaternion.Euler(0, 180, 0); // signs read from the lobby

        Box(_wood, new Vector3(cx, f + 0.5f, zc), new Vector3(2.0f, 1.0f, depth), WoodDark * 1.1f);
        Band(_ornament, new Vector3(cx - 1.0f, f + 0.76f, zf + 0.004f), new Vector3(cx + 1.0f, f + 0.76f, zf + 0.004f), up, 0.16f, fwd, GoldDark, 1.0f);

        // The glazed cabin: brass corner posts, panels either side of the window, a transom with the sign.
        foreach (var sx in new[] { -1f, 1f })
        {
            Box(_box, new Vector3(cx + sx * 0.96f, f + 1.68f, zf - 0.04f), new Vector3(0.08f, 1.36f, 0.08f), Gold);
            Box(_wood, new Vector3(cx + sx * 0.98f, f + 1.68f, zc), new Vector3(0.04f, 1.36f, depth - 0.08f), WoodDark);
            Box(_wood, new Vector3(cx + sx * 0.72f, f + 1.68f, zf - 0.02f), new Vector3(0.44f, 1.36f, 0.04f), WoodDark);
        }

        Box(_wood, new Vector3(cx, f + 2.16f, zf - 0.02f), new Vector3(1.0f, 0.4f, 0.04f), WoodDark);
        Signs.Add(new SignSpec { Text = "TICKETS", Pos = new Vector3(cx, f + 2.16f, zf + 0.005f), Rot = facing, Height = 0.2f, Width = 0.9f, Color = Cream });

        // Inside the window: the booth's back, sides and ceiling, a roll of tickets, a little notice.
        float wz = zf - 0.6f, y0 = f + 1.0f, y1 = f + 1.96f;
        Patch(_wood, new Vector3(cx - 0.5f, y0, wz), new Vector3(cx + 0.5f, y0, wz), new Vector3(cx + 0.5f, y1, wz), new Vector3(cx - 0.5f, y1, wz), fwd, WoodDark * 0.8f, 2, 2);
        Patch(_wood, new Vector3(cx - 0.5f, y0, zf), new Vector3(cx + 0.5f, y0, zf), new Vector3(cx + 0.5f, y0, wz), new Vector3(cx - 0.5f, y0, wz), up, WoodDark * 0.9f, 2, 1);
        Patch(_wood, new Vector3(cx - 0.5f, y1, wz), new Vector3(cx + 0.5f, y1, wz), new Vector3(cx + 0.5f, y1, zf), new Vector3(cx - 0.5f, y1, zf), Vector3.down, WoodDark * 0.7f, 2, 1);
        foreach (var sx in new[] { -1f, 1f })
            Patch(_wood, new Vector3(cx + sx * 0.5f, y0, zf), new Vector3(cx + sx * 0.5f, y0, wz), new Vector3(cx + sx * 0.5f, y1, wz), new Vector3(cx + sx * 0.5f, y1, zf), new Vector3(-sx, 0, 0), WoodDark * 0.8f, 1, 2);
        Lathe(_posterBg, new Vector3(cx + 0.24f, y0, zf - 0.35f), new[] { new Vector2(0.065f, 0f), new Vector2(0.065f, 0.1f), new Vector2(0f, 0.1f) }, Cream * 0.95f, 14);
        Signs.Add(new SignSpec { Text = "NO REFUNDS", Pos = new Vector3(cx - 0.1f, f + 1.72f, wz + 0.005f), Rot = facing, Height = 0.06f, Width = 0.5f, Color = new Color(0.12f, 0.07f, 0.04f) });

        // Brass bars across the window (a gap at the bottom to pass the money through), and the ledge.
        for (var bx = -0.45f; bx <= 0.451f; bx += 0.06f) Box(_box, new Vector3(cx + bx, f + 1.66f, zf + 0.01f), new Vector3(0.012f, 0.6f, 0.012f), Gold);
        Box(_box, new Vector3(cx, f + 1.36f, zf + 0.01f), new Vector3(1.0f, 0.025f, 0.02f), Gold);
        Box(_box, new Vector3(cx, f + 1.02f, zf + 0.1f), new Vector3(1.1f, 0.04f, 0.3f), Gold);
        Signs.Add(new SignSpec { Text = "RING FOR SERVICE", Pos = new Vector3(cx + 0.28f, f + 1.02f, zf + 0.252f), Rot = facing, Height = 0.024f, Width = 0.36f, Color = new Color(0.12f, 0.07f, 0.04f) });

        // The roof and its dome.
        Box(_box, new Vector3(cx, f + 2.43f, zc + 0.05f), new Vector3(2.3f, 0.14f, depth + 0.2f), Gold);
        Lathe(_box, new Vector3(cx, f + 2.5f, zc), new[] { new Vector2(0.7f, 0f), new Vector2(0.6f, 0.18f), new Vector2(0.35f, 0.36f), new Vector2(0.1f, 0.46f), new Vector2(0f, 0.5f) }, GoldDark, 24);
        Lathe(_box, new Vector3(cx, f + 3.0f, zc), new[] { new Vector2(0f, 0f), new Vector2(0.06f, 0.04f), new Vector2(0.07f, 0.1f), new Vector2(0.04f, 0.16f), new Vector2(0f, 0.18f) }, Gold, 12);

        // The desk bell (its plunger is a moving piece).
        var bell = BellBase;
        Lathe(_box, bell, new[] { new Vector2(0f, 0f), new Vector2(0.05f, 0f), new Vector2(0.055f, 0.006f), new Vector2(0.05f, 0.012f), new Vector2(0f, 0.012f) }, Dark * 2f, 18);
        Lathe(_box, bell + up * 0.012f, new[] { new Vector2(0.047f, 0f), new Vector2(0.044f, 0.012f), new Vector2(0.034f, 0.026f), new Vector2(0.018f, 0.036f), new Vector2(0f, 0.039f) }, Gold * 1.05f, 20);
        InGroup("Bell", BellTop, () =>
            Lathe(_box, bell + up * 0.036f, new[] { new Vector2(0.004f, 0f), new Vector2(0.004f, 0.012f), new Vector2(0.009f, 0.014f), new Vector2(0.009f, 0.019f), new Vector2(0f, 0.021f) }, Dark * 2.5f, 10));
    }

    private void Can(Vector3 bottom)
    {
        Lathe(_tin, bottom, new[] { new Vector2(0f, 0f), new Vector2(0.034f, 0f), new Vector2(CanRadius, 0.004f), new Vector2(CanRadius, 0.012f) }, TinColor, 14);
        Lathe(_label, bottom, new[] { new Vector2(CanRadius + 0.0003f, 0.012f), new Vector2(CanRadius + 0.0003f, 0.093f) }, Color.white, 20, uRepeat: 1f);
        Lathe(_tin, bottom, new[] { new Vector2(CanRadius, 0.093f), new Vector2(CanRadius, 0.101f), new Vector2(0.035f, CanHeight), new Vector2(0f, CanHeight) }, TinColor, 14);
    }

    private void BuildCounter()
    {
        float f = LobbyFloor, cx = CounterX, zfr = CounterFrontZ, zc = (zfr + LobbyFrontZ) * 0.5f, depth = zfr - LobbyFrontZ - 0.05f;
        var up = Vector3.up;
        Box(_wood, new Vector3(cx, f + 0.5f, zc), new Vector3(2.8f, 1.0f, depth), WoodDark * 1.1f);
        Band(_ornament, new Vector3(cx - 1.4f, f + 0.82f, zfr + 0.004f), new Vector3(cx + 1.4f, f + 0.82f, zfr + 0.004f), up, 0.14f, Vector3.forward, GoldDark, 1.0f);
        Box(_box, new Vector3(cx, f + 1.025f, zc + 0.03f), new Vector3(2.9f, 0.05f, depth + 0.08f), Gold);

        // Shelves on the wall behind, stocked with soup.
        foreach (var y in new[] { f + 1.55f, f + 1.95f })
        {
            Box(_box, new Vector3(cx, y, LobbyFrontZ + 0.14f), new Vector3(2.6f, 0.03f, 0.24f), GoldDark);
            for (var k = 0; k < 10; k++) Can(new Vector3(cx - 1.15f + k * 0.255f, y + 0.015f, LobbyFrontZ + 0.14f));
        }

        // The sign, ringed with chasing bulbs.
        var sign = new Vector3(cx, f + 2.65f, LobbyFrontZ + 0.07f);
        Box(_box, sign, new Vector3(2.5f, 0.46f, 0.08f), Dark);
        Signs.Add(new SignSpec { Text = "CONCESSIONS", Pos = sign + Vector3.forward * 0.05f, Rot = Quaternion.Euler(0, 180, 0), Height = 0.26f, Width = 2.3f, Color = new Color(1f, 0.84f, 0.5f) });
        var bulb = 0;
        for (var x = -1.2f; x <= 1.21f; x += 0.2f) Glow(sign + new Vector3(x, 0.26f, 0.06f), 0.14f, new Color(1f, 0.88f, 0.6f, 1f), GlowGroup.Chase, bulb++);
        for (var x = 1.2f; x >= -1.21f; x -= 0.2f) Glow(sign + new Vector3(x, -0.26f, 0.06f), 0.14f, new Color(1f, 0.88f, 0.6f, 1f), GlowGroup.Chase, bulb++);

        // A price card...
        var card = Quaternion.Euler(-12, 0, 0);
        var cardAt = new Vector3(cx + 0.85f, f + 1.12f, zfr - 0.14f);
        Box(_posterBg, cardAt, new Vector3(0.34f, 0.15f, 0.008f), card, Cream);
        var cardFacing = card * Quaternion.Euler(0, 180, 0);
        var cardN = card * Vector3.forward;
        Signs.Add(new SignSpec { Text = "BACON SOUP", Pos = cardAt + cardN * 0.006f + card * Vector3.up * 0.025f, Rot = cardFacing, Height = 0.045f, Width = 0.32f, Color = new Color(0.12f, 0.07f, 0.04f) });
        Signs.Add(new SignSpec { Text = "5 CENTS", Pos = cardAt + cardN * 0.006f - card * Vector3.up * 0.035f, Rot = cardFacing, Height = 0.032f, Width = 0.32f, Color = new Color(0.12f, 0.07f, 0.04f) });

        // ...and the cans you can take.
        for (var i = 0; i < CanCount; i++)
        {
            var bottom = CanRest(i);
            InGroup("Can" + i, bottom + up * (CanHeight * 0.5f), () => Can(bottom));
        }
    }

    private void BuildPiano()
    {
        float f = LobbyFloor, z = PianoZ;
        var wood = WoodDark * 0.72f;
        var up = Vector3.up;

        Box(_wood, new Vector3(-5.9f, f + 0.68f, z), new Vector3(0.6f, 1.36f, 1.52f), wood);
        Box(_box, new Vector3(-5.89f, f + 1.38f, z), new Vector3(0.66f, 0.04f, 1.58f), wood * 1.1f);
        Box(_wood, new Vector3(-5.44f, f + 0.7f, z), new Vector3(0.33f, 0.05f, 1.42f), wood);
        foreach (var sz in new[] { -1f, 1f })
        {
            Box(_box, new Vector3(-5.44f, f + 0.77f, z + sz * 0.68f), new Vector3(0.33f, 0.14f, 0.05f), wood * 1.1f);
            Box(_box, new Vector3(-5.36f, f + 0.34f, z + sz * 0.64f), new Vector3(0.06f, 0.68f, 0.06f), wood * 1.1f);
        }

        Box(_box, new Vector3(-5.59f, f + 0.83f, z), new Vector3(0.03f, 0.16f, 1.32f), wood);
        foreach (var pz in new[] { -0.05f, 0.05f }) Box(_box, new Vector3(-5.55f, f + 0.04f, z + pz), new Vector3(0.12f, 0.02f, 0.035f), Gold);

        // The music desk, leaning back, with a song open on it.
        var desk = Quaternion.Euler(0, 0, 12);
        var deskAt = new Vector3(-5.62f, f + 1.06f, z);
        Box(_box, deskAt, new Vector3(0.025f, 0.3f, 0.86f), desk, wood * 1.1f);
        var n = desk * Vector3.right;
        var dup = desk * up;
        foreach (var side in new[] { -1f, 1f })
        {
            var c = deskAt + n * 0.016f + Vector3.forward * (side * 0.165f);
            var r = Vector3.forward * 0.15f;
            var u = dup * 0.125f;
            Patch(_sheet, c - r - u, c + r - u, c + r + u, c - r + u, n, Color.white, 1, 1);
        }

        Signs.Add(new SignSpec { Text = "S. Lawrence  -  No. 6", Pos = deskAt + n * 0.02f + dup * 0.105f - Vector3.forward * 0.165f, Rot = Quaternion.LookRotation(-n, dup), Height = 0.022f, Width = 0.28f, Color = new Color(0.12f, 0.07f, 0.04f) });

        // A candle on top.
        var candle = Candle;
        Lathe(_box, candle, new[] { new Vector2(0.06f, 0f), new Vector2(0.05f, 0.02f), new Vector2(0.015f, 0.04f), new Vector2(0.015f, 0.16f), new Vector2(0.035f, 0.18f), new Vector2(0f, 0.19f) }, Gold, 12);
        Lathe(_posterBg, candle + up * 0.19f, new[] { new Vector2(0.012f, 0f), new Vector2(0.012f, 0.08f), new Vector2(0f, 0.085f) }, Cream, 8);
        Glow(candle + up * 0.3f, 0.1f, new Color(1f, 0.88f, 0.55f, 1f), GlowGroup.Flicker);
        Glow(candle + up * 0.3f, 0.55f, new Color(1f, 0.7f, 0.35f, 0.35f), GlowGroup.Flicker);

        // All 88 keys, low notes to your left (one piece; pressed keys are moved vertex by vertex).
        InGroup("Keys", Vector3.zero, () =>
        {
            const float w = 0.0235f;
            var z0 = z - 52 * w * 0.5f;
            var top = f + 0.745f;
            const float xFront = -5.255f, xBack = xFront - 0.15f;
            var note = 0;
            for (var k = 0; k < 52; k++)
            {
                var letter = "ABCDEFG"[k % 7];
                var zc = z0 + (k + 0.5f) * w;
                var v0 = VertexCount(_box);
                Box(_box, new Vector3((xFront + xBack) * 0.5f, top - 0.011f, zc), new Vector3(0.15f, 0.022f, w - 0.0012f), Ivory);
                Keys.Add(new KeyInfo { Note = note++, V0 = v0, V1 = VertexCount(_box), Black = false, Z = zc, HalfW = w * 0.5f, X0 = xBack, X1 = xFront, Top = top });
                if (k >= 51 || "ACDFG".IndexOf(letter) < 0) continue;
                var zk = z0 + (k + 1) * w;
                var bv0 = VertexCount(_box);
                const float bx1 = xBack + 0.095f;
                Box(_box, new Vector3((xBack + bx1) * 0.5f, top + 0.006f, zk), new Vector3(0.095f, 0.024f, 0.0135f), Dark * 1.4f);
                Keys.Add(new KeyInfo { Note = note++, V0 = bv0, V1 = VertexCount(_box), Black = true, Z = zk, HalfW = 0.00675f, X0 = xBack, X1 = bx1, Top = top + 0.018f });
            }
        });
    }

    private void BuildLobbyWallArt()
    {
        var f = LobbyFloor;
        var n = Vector3.back;
        var right = Vector3.right;
        var rot = Quaternion.identity;
        var gold = new Color(1f, 0.84f, 0.5f);

        void Frame(Vector3 c, float w, float h)
        {
            const float t = 0.09f;
            Box(_box, c + Vector3.up * (h * 0.5f + t * 0.5f) + n * 0.03f, Abs(right * (w + 2 * t) + Vector3.up * t + n * 0.06f), Gold);
            Box(_box, c - Vector3.up * (h * 0.5f + t * 0.5f) + n * 0.03f, Abs(right * (w + 2 * t) + Vector3.up * t + n * 0.06f), Gold);
            Box(_box, c + right * (w * 0.5f + t * 0.5f) + n * 0.03f, Abs(right * t + Vector3.up * h + n * 0.06f), Gold);
            Box(_box, c - right * (w * 0.5f + t * 0.5f) + n * 0.03f, Abs(right * t + Vector3.up * h + n * 0.06f), Gold);
            var lamp = c + Vector3.up * (h * 0.5f + 0.2f) + n * 0.18f;
            Box(_box, lamp, Abs(right * 0.5f + Vector3.up * 0.07f + n * 0.12f), Gold, 1.3f);
            Glow(lamp - Vector3.up * 0.04f + n * 0.06f, 0.5f, new Color(1f, 0.85f, 0.6f, 0.5f), GlowGroup.Steady);
        }

        void Sheet(Vector3 c, float w, float h, Color tint)
        {
            var a = c - right * (w * 0.5f) - Vector3.up * (h * 0.5f) + n * 0.01f;
            var b = c + right * (w * 0.5f) - Vector3.up * (h * 0.5f) + n * 0.01f;
            Patch(_posterBg, a, b, b + Vector3.up * h, a + Vector3.up * h, n, tint, 2, 3);
        }

        void Line(Vector3 at, string text, float h, float w, Color col) =>
            Signs.Add(new SignSpec { Text = text, Pos = at + n * 0.025f, Rot = rot, Height = h, Width = w, Color = col });

        // Coming soon.
        var poster = new Vector3(-3.6f, f + 1.8f, LobbyZ - 0.01f);
        Frame(poster, 1.0f, 1.45f);
        Sheet(poster, 1.0f, 1.45f, Dark * 2.2f);
        var splat = poster + Vector3.up * 0.05f + n * 0.015f;
        Patch(_ink, splat - right * 0.45f - Vector3.up * 0.45f, splat + right * 0.45f - Vector3.up * 0.45f, splat + right * 0.45f + Vector3.up * 0.45f, splat - right * 0.45f + Vector3.up * 0.45f,
            n, new Color(0.02f, 0.015f, 0.01f, 0.9f), 2, 2, unlit: true);
        Line(poster + Vector3.up * 0.58f, "COMING SOON", 0.08f, 0.9f, gold);
        Line(poster + Vector3.up * 0.24f, "DARK", 0.2f, 0.9f, Cream);
        Line(poster + Vector3.up * 0.03f, "REVIVAL", 0.17f, 0.9f, Cream);
        Line(poster - Vector3.up * 0.2f, "VR", 0.2f, 0.9f, gold);
        Line(poster - Vector3.up * 0.58f, "a new VR mod by StrikeZoneVR", 0.042f, 0.9f, Cream * 0.9f);

        // The plaque.
        var plaque = new Vector3(3.6f, f + 1.8f, LobbyZ - 0.01f);
        Frame(plaque, 1.15f, 1.0f);
        Sheet(plaque, 1.15f, 1.0f, Dark * 1.8f);
        Line(plaque + Vector3.up * 0.3f, "InkVR", 0.17f, 1.0f, gold);
        Line(plaque + Vector3.up * 0.12f, "a VR mod for Bendy and the Ink Machine", 0.038f, 1.05f, Cream);
        Line(plaque - Vector3.up * 0.04f, "made by StrikeZoneVR", 0.065f, 1.05f, Cream);
        Line(plaque - Vector3.up * 0.22f, "thank you for playing", 0.05f, 1.05f, gold);
        Line(plaque - Vector3.up * 0.36f, "and thanks to the testers who helped", 0.035f, 1.05f, Cream * 0.9f);
    }

    // ---- the mirror by the piano: see yourself, head to toe, and change who you are ----

    private const float MirrorZ = -15.85f, MirrorW = 0.95f, MirrorH = 2.0f, PanelZ = -14.85f;

    /// <summary>The face of the stair's side panel the mirror hangs on (it faces into the lobby, +x).</summary>
    private static float MirrorWallX => -(StairIn - 0.04f);

    /// <summary>The mirror glass's centre.</summary>
    public static Vector3 MirrorAt => new(MirrorWallX + 0.03f, LobbyFloor + 0.3f + MirrorH * 0.5f, MirrorZ);

    /// <summary>The panel's buttons (0, 1: STYLE back and next; 2, 3: WATCH back and next): the centre of each face.</summary>
    public static Vector3 MirrorButton(int i) => new(MirrorWallX + 0.068f, LobbyFloor + (i < 2 ? 1.5f : 1.29f), PanelZ + (i % 2 == 0 ? -0.17f : 0.17f));

    public const float MirrorButtonHalf = 0.05f;

    private void BuildMirror()
    {
        var f = LobbyFloor;
        var x = MirrorWallX;
        var n = Vector3.right;
        var rot = Quaternion.LookRotation(-n);
        var c = MirrorAt;
        const float t = 0.08f;

        // the glass in a gilt frame
        Box(_box, new Vector3(x + 0.035f, c.y + MirrorH * 0.5f + t * 0.5f, c.z), new Vector3(0.07f, t, MirrorW + 2 * t), Gold);
        Box(_box, new Vector3(x + 0.035f, c.y - MirrorH * 0.5f - t * 0.5f, c.z), new Vector3(0.07f, t, MirrorW + 2 * t), Gold);
        foreach (var s in new[] { -1f, 1f }) Box(_box, new Vector3(x + 0.035f, c.y, c.z + s * (MirrorW * 0.5f + t * 0.5f)), new Vector3(0.07f, MirrorH, t), Gold);
        new Mirror(_root, c, Quaternion.Euler(0, 90, 0), MirrorW, MirrorH, new Color(0.93f, 0.92f, 0.9f));

        // over it: who you are tonight
        Signs.Add(new SignSpec { Text = "WHO ARE YOU TONIGHT?", Pos = new Vector3(x + 0.03f, c.y + MirrorH * 0.5f + 0.38f, c.z), Rot = rot, Height = 0.06f, Width = 1.3f, Color = Cream });
        Signs.Add(new SignSpec { Text = Body.Styles.Current.Label, Pos = new Vector3(x + 0.03f, c.y + MirrorH * 0.5f + 0.2f, c.z), Rot = rot, Height = 0.12f, Width = 1.5f, Color = new Color(1f, 0.84f, 0.5f), Id = "MirrorStyle" });

        // beside it, a brass panel: press the arrows to change your style and your watch
        var pc = new Vector3(x + 0.015f, f + 1.4f, PanelZ);
        var name = new Color(1f, 0.84f, 0.5f);
        Box(_box, pc, new Vector3(0.03f, 0.52f, 0.5f), GoldDark);
        Box(_box, pc + n * 0.018f, new Vector3(0.01f, 0.46f, 0.44f), Dark * 2.2f);
        Signs.Add(new SignSpec { Text = "PRESS THE ARROWS", Pos = new Vector3(x + 0.04f, f + 1.6f, PanelZ), Rot = rot, Height = 0.024f, Width = 0.42f, Color = Cream * 0.8f });
        Signs.Add(new SignSpec { Text = "STYLE", Pos = new Vector3(x + 0.04f, f + 1.5f, PanelZ), Rot = rot, Height = 0.045f, Width = 0.22f, Color = Cream });
        Signs.Add(new SignSpec { Text = Body.Styles.Current.Label, Pos = new Vector3(x + 0.04f, f + 1.415f, PanelZ), Rot = rot, Height = 0.03f, Width = 0.42f, Color = name, Id = "MirrorStyle2" });
        Signs.Add(new SignSpec { Text = "WATCH", Pos = new Vector3(x + 0.04f, f + 1.29f, PanelZ), Rot = rot, Height = 0.045f, Width = 0.22f, Color = Cream });
        Signs.Add(new SignSpec { Text = WatchLabel(), Pos = new Vector3(x + 0.04f, f + 1.205f, PanelZ), Rot = rot, Height = 0.03f, Width = 0.42f, Color = name, Id = "MirrorWatch" });
        for (var i = 0; i < 4; i++)
        {
            var b = MirrorButton(i);
            var left = i % 2 == 0;
            InGroup("MirrorBtn" + i, b, () =>
            {
                Box(_box, b - n * 0.0175f, new Vector3(0.035f, MirrorButtonHalf * 2, MirrorButtonHalf * 2), Gold);
                // an arrow on its face: back points to your left (-z), next to your right
                var a = b + n * 0.002f;
                Vector3 P(float dz, float dy) => a + new Vector3(0, dy, dz);
                if (left) Patch(_box, P(-0.026f, 0), P(0.016f, -0.026f), P(0.016f, 0.026f), P(-0.026f, 0), n, Dark, unlit: true);
                else Patch(_box, P(-0.016f, -0.026f), P(0.026f, 0), P(0.026f, 0), P(-0.016f, 0.026f), n, Dark, unlit: true);
            });
        }
    }

    /// <summary>The watch you're wearing, as the panel names it.</summary>
    public static string WatchLabel()
    {
        foreach (var w in Body.Styles.Watches)
            if (w.Key == ModConfig.Watch.Value)
                return w.Label;
        return Body.Styles.Watches[0].Label;
    }

    private void BuildWriting()
    {
        // Ink writing on the side of the stairs, which seeps in when you look at it (TheatreToys fades it in).
        var at = WritingAt;
        var n = Vector3.left;
        var ink = new Color(0.04f, 0.03f, 0.025f, 0f);
        Signs.Add(new SignSpec { Text = "THE CREATOR", Pos = at + Vector3.up * 0.17f, Rot = Quaternion.LookRotation(-n), Height = 0.27f, Width = 2.2f, Color = ink, Id = "Writing1" });
        Signs.Add(new SignSpec { Text = "LIED TO US", Pos = at - Vector3.up * 0.16f, Rot = Quaternion.LookRotation(-n), Height = 0.27f, Width = 2.2f, Color = ink, Id = "Writing2" });
        var top = at - Vector3.up * 0.3f + n * 0.002f;
        InGroup("Drips", top, () =>
        {
            var r = Vector3.forward * 1.05f;
            var h = 1.05f;
            Patch(_drips, top - r - Vector3.up * h, top + r - Vector3.up * h, top + r, top - r, n, new Color(0.035f, 0.025f, 0.02f, 0.92f), 2, 2, unlit: true);
        });
    }

    /// <summary>Builds a <see cref="MeshBuilder"/> shape into a part (lit like the rest).</summary>
    private void AddBuilder(Part p, MeshBuilder mb, Color tint)
    {
        p = Route(p);
        var v = mb.VertexArray;
        var nrm = mb.NormalArray;
        var uv = mb.UvArray;
        var start = p.V.Count;
        for (var i = 0; i < v.Length; i++)
        {
            p.V.Add(v[i]);
            p.UV.Add(uv[i]);
            p.C.Add(Lit(v[i], nrm[i], tint, 1f));
        }

        foreach (var i in mb.TriangleArray) p.T.Add(start + i);
    }

    private void BuildHallSecrets()
    {
        // A seat in the front row, kept for someone.
        var seat = new Vector3(-(SeatStart + SeatW * 0.5f), 0, RowZ(0));
        var card = Quaternion.Euler(-15, 0, 0);
        var cardAt = seat + new Vector3(0, 0.535f, 0.1f);
        Box(_posterBg, cardAt, new Vector3(0.2f, 0.11f, 0.006f), card, Cream);
        var cardN = card * Vector3.forward;
        var cardUp = card * Vector3.up;
        var facing = card * Quaternion.Euler(0, 180, 0);
        Signs.Add(new SignSpec { Text = "RESERVED", Pos = cardAt + cardN * 0.005f + cardUp * 0.015f, Rot = facing, Height = 0.036f, Width = 0.19f, Color = new Color(0.45f, 0.08f, 0.04f) });
        Signs.Add(new SignSpec { Text = "H. Stein", Pos = cardAt + cardN * 0.005f - cardUp * 0.025f, Rot = facing, Height = 0.026f, Width = 0.19f, Color = new Color(0.12f, 0.07f, 0.04f) });

        // A little Bendy doll, sat on the balcony rail, watching whoever comes down the aisle.
        var at = DollAt;
        InGroup("Doll", at, () =>
        {
            Vector3 P(float x, float y, float z) => at + new Vector3(x, y, z);
            var black = new MeshBuilder();
            black.Ellipsoid(P(0, 0.085f, 0), new Vector3(0.05f, 0.07f, 0.042f), Quaternion.identity, 14, 10);
            black.Ellipsoid(P(0, 0.205f, 0), new Vector3(0.068f, 0.062f, 0.062f), Quaternion.identity, 16, 12);
            foreach (var sx in new[] { -1f, 1f })
            {
                black.Tube(new[] { P(sx * 0.04f, 0.25f, 0.005f), P(sx * 0.07f, 0.29f, 0f), P(sx * 0.09f, 0.32f, -0.012f) }, new[] { 0.02f, 0.011f, 0.003f }, Vector3.up, 8, false);
                black.Tube(new[] { P(sx * 0.045f, 0.13f, 0f), P(sx * 0.075f, 0.075f, -0.02f), P(sx * 0.085f, 0.05f, -0.05f) }, new[] { 0.013f, 0.012f, 0.011f }, Vector3.up, 8);
                black.Tube(new[] { P(sx * 0.025f, 0.03f, 0f), P(sx * 0.03f, 0.02f, -0.06f), P(sx * 0.032f, 0f, -0.11f) }, new[] { 0.016f, 0.015f, 0.014f }, Vector3.up, 8);
                black.Ellipsoid(P(sx * 0.033f, 0f, -0.125f), new Vector3(0.022f, 0.018f, 0.034f), Quaternion.identity, 10, 8);
            }

            AddBuilder(_fabric, black, Dark * 1.9f);
            var white = new MeshBuilder();
            white.Ellipsoid(P(0, 0.198f, -0.03f), new Vector3(0.052f, 0.046f, 0.04f), Quaternion.identity, 14, 10);
            foreach (var sx in new[] { -1f, 1f })
            {
                white.Ellipsoid(P(sx * 0.085f, 0.05f, -0.055f), new Vector3(0.02f, 0.02f, 0.02f), Quaternion.identity, 10, 8);
                white.Ellipsoid(P(sx * 0.015f, 0.152f, -0.04f), new Vector3(0.016f, 0.01f, 0.008f), Quaternion.identity, 8, 6);
            }

            AddBuilder(_fabric, white, Cream);
            var face = new MeshBuilder();
            foreach (var sx in new[] { -1f, 1f }) face.Ellipsoid(P(sx * 0.019f, 0.212f, -0.066f), new Vector3(0.011f, 0.016f, 0.005f), Quaternion.Euler(0, 0, -sx * 12), 8, 6);
            face.Ellipsoid(P(0, 0.183f, -0.066f), new Vector3(0.024f, 0.006f, 0.004f), Quaternion.identity, 10, 4);
            AddBuilder(_box, face, Dark);
        });
    }
}
