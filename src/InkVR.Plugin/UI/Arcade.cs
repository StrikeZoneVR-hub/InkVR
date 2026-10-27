using System.Collections.Generic;
using InkVR.Input;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The Fun Hall's games, played with your hands: what you can pick up (balls, darts, the cork gun, the mallets), what
/// it does when it flies, how each game scores, and the tickets, high scores and prizes that come of it (kept in
/// InkVR's own <see cref="Progress"/> file). Everything here is in the theatre's own space.
/// </summary>
public static partial class Arcade
{
    public const int GameCount = 8;
    public static readonly string[] GameNames = { "INK TOSS", "SHOOTING GALLERY", "DARTS", "STRONG MAN", "INK BALL", "WHACK-A-SEARCHER", "THE CLAW", "PACHINKO" };
    public static readonly string[] PrizeNames = { "BENDY PLUSH", "BORIS PLUSH", "ALICE PLUSH", "BENDY BALLOON", "PARTY HAT", "INK LASER", "GOLDEN BENDY", "DEMON'S BOW TIE" };
    public static readonly int[] PrizeCost = { 10, 10, 10, 20, 25, 30, 60, 40 };

    private const float G = 9.81f;
    private static Cinema _cinema;
    private static Theatre _t;
    private static float _now, _dt;

    // =====================================================================================================
    // Things you pick up
    // =====================================================================================================

    private sealed class Holdable
    {
        public string Name;
        public Transform T;
        public Vector3 Rest;
        public Vector3 GripLocal;
        public Quaternion HoldRot = Quaternion.identity;
        public float Reach = 0.12f;
        public bool Tool; // goes back to its place when you let go (otherwise: thrown)

        public int Hand = -1;
        public Vector3 Pos, Vel;
        public Quaternion Rot = Quaternion.identity;
        public bool Loose;
        public float BackAt = -1;

        public Vector3 GripWorld => Pos + Rot * GripLocal;
        public Vector3 Point(Vector3 local) => Pos + Rot * local;
    }

    private static readonly List<Holdable> All = new();

    private static Holdable Make(string name, Vector3 gripLocal, Quaternion holdRot, float reach, bool tool)
    {
        var piece = _t.Piece(name);
        if (piece == null) return null;
        var h = new Holdable { Name = name, T = piece, Rest = _t.PivotOf(name), GripLocal = gripLocal, HoldRot = holdRot, Reach = reach, Tool = tool };
        h.Pos = h.Rest;
        All.Add(h);
        return h;
    }

    /// <summary>Hold, carry and let go: what's held follows the hand; tools go home, everything else is the game's to fly.</summary>
    private static void Carry()
    {
        foreach (var o in All)
        {
            if (o.Hand >= 0)
            {
                if (TheatreLife.HolderOf(o.Name) != o.Hand)
                {
                    // let go
                    o.Hand = -1;
                    if (o.Tool) o.BackAt = _now + 0.8f;
                    else o.Loose = true;
                    continue;
                }

                var was = o.Pos;
                o.Rot = TheatreToys.HandRotOf(o.Hand) * o.HoldRot;
                o.Pos = TheatreToys.PalmOf(o.Hand) - o.Rot * o.GripLocal;
                o.Vel = Vector3.Lerp(o.Vel, (o.Pos - was) / Mathf.Max(_dt, 1e-3f), 0.5f);
                if (!o.Tool) o.Vel = Vector3.Lerp(o.Vel, TheatreToys.PalmVelOf(o.Hand) * 1.15f, 0.6f);
                Place(o);
                continue;
            }

            if (o.BackAt > 0 && _now >= o.BackAt)
            {
                Home(o);
                continue;
            }

            if (o.Loose) continue;
            if (o.Name.StartsWith("Prize") && !Owns(o.Name[5] - '0')) continue; // a prize: yours once you've bought it
            var h = TheatreLife.Grab(o.GripWorld, o.Reach, o.Name);
            if (h < 0) continue;
            o.Hand = h;
            o.Loose = false;
            o.BackAt = -1;
        }
    }

    private static void Home(Holdable o)
    {
        if (!o.T.gameObject.activeSelf) o.T.gameObject.SetActive(true);
        o.Pos = o.Rest;
        o.Rot = Quaternion.identity;
        o.Vel = Vector3.zero;
        o.Loose = false;
        o.BackAt = -1;
        o.Hand = -1;
        Place(o);
    }

    private static void Place(Holdable o)
    {
        o.T.localPosition = o.Pos;
        o.T.localRotation = o.Rot;
    }

    private static bool Held(Holdable o) => o != null && o.Hand >= 0;

    private static bool Trigger(int hand)
    {
        if (hand == 1 && XrInput.DevRightTrigger.HasValue) return XrInput.DevRightTrigger.Value > 0.6f;
        return XrInput.Ready && XrInput.Get((Hand)hand).Trigger > 0.6f;
    }

    // =====================================================================================================
    // Tickets, high scores, prizes
    // =====================================================================================================

    /// <summary>A round's over: tickets for the score, and the high-score board if it's a best.</summary>
    private static int Award(int game, int score, int perTicket, Vector3 at)
    {
        var tickets = Mathf.Max(score > 0 ? 1 : 0, score / perTicket);
        if (tickets > 0)
        {
            Progress.Add("tickets", tickets);
            TheatreSounds.Play(TheatreSounds.Tickets, at, 0.5f);
        }

        var key = "score." + game;
        if (score > Progress.Get(key))
        {
            Progress.Set(key, score);
            if (score > 0) TheatreSounds.Play(TheatreSounds.Fanfare, at, 0.55f);
        }

        Plugin.L.LogInfo($"Fun Hall: {GameNames[game]} scored {score}, {tickets} tickets (total {Progress.Get("tickets")})");
        RefreshBoards();
        return tickets;
    }

    private static void RefreshBoards()
    {
        for (var i = 0; i < GameCount; i++) SetSign("HighScore" + i, GameNames[i] + "  ......  " + Progress.Get("score." + i));
        SetSign("TicketCount", "YOUR TICKETS: " + Progress.Get("tickets"));
        for (var i = 0; i < Theatre.PrizeCount; i++) SetSign("PrizeTag" + i, Progress.Has("prize." + i) ? "YOURS!" : PrizeCost[i] + " TICKETS");
    }

    private static void SetSign(string id, string text)
    {
        var tmp = _cinema.Sign(id);
        if (tmp != null && tmp.text != text) tmp.text = text;
    }

    public static bool Owns(int prize) => Progress.Has("prize." + prize);

    private static void Prizes()
    {
        for (var i = 0; i < Theatre.PrizeCount; i++)
        {
            var at = Theatre.PrizeAt(i) + Vector3.up * 0.15f;
            var id = "PrizeShelf" + i;
            if (Owns(i))
            {
                // yours: the plushes come off the shelf to play with (Toys); the rest you wear or see, and a touch puts
                // them away or back on
                if (i <= 2) continue;
                if (TheatreLife.Grab(at, 0.16f, id) < 0) continue;
                Progress.Set("wear." + i, Progress.Get("wear." + i, 1) == 1 ? 2 : 1);
                TheatreSounds.Play(TheatreSounds.Click, at, 0.5f, 1.3f);
                continue;
            }

            if (TheatreLife.Grab(at, 0.16f, id) < 0) continue;
            var tickets = Progress.Get("tickets");
            if (tickets < PrizeCost[i])
            {
                TheatreSounds.Play(TheatreSounds.Buzzer, at, 0.3f, 1.4f);
                SetSign("PrizeTag" + i, "NEED " + (PrizeCost[i] - tickets) + " MORE");
                continue;
            }

            Progress.Add("tickets", -PrizeCost[i]);
            Progress.Set("prize." + i, 1);
            TheatreSounds.Play(TheatreSounds.Fanfare, at, 0.6f);
            Plugin.L.LogInfo($"Fun Hall: bought the {PrizeNames[i]}");
            RefreshBoards();
        }
    }

    /// <summary>Is this prize being worn or shown (bought, and not put away)?</summary>
    public static bool Wearing(int prize) => Owns(prize) && Progress.Get("wear." + prize, 1) == 1;

    // =====================================================================================================
    // Each frame
    // =====================================================================================================

    private static bool _ready;

    public static void Reset()
    {
        foreach (var o in All)
            if (o.T != null)
                Home(o);
        ResetGames();
    }

    /// <summary>Dev: set one of the Fun Hall's things flying from where it is (or from a point), at a velocity.</summary>
    public static string DevThrow(string name, Vector3 vel, Vector3? from)
    {
        foreach (var o in All)
        {
            if (o.Name != name) continue;
            if (from.HasValue) o.Pos = from.Value;
            o.Vel = vel;
            o.Rot = vel.sqrMagnitude > 0.01f ? Quaternion.LookRotation(vel) : o.Rot;
            o.Loose = true;
            o.Hand = -1;
            o.BackAt = -1;
            return "thrown " + name;
        }

        return "no " + name;
    }

    public static string Describe()
    {
        var parts = new List<string>();
        foreach (var o in All)
            if (o.Loose || o.Hand >= 0 || o.BackAt > 0)
                parts.Add($"{o.Name}{(o.Loose ? " flying" : "")}{(o.Hand >= 0 ? " held" + o.Hand : "")} ({o.Pos.x:F2},{o.Pos.y:F2},{o.Pos.z:F2})");
        return $"tickets={Progress.Get("tickets")} toss={_tossScore}/{_tossThrown} corks={_corks} gallery={_galleryScore} darts={_dartScore}/{_dartsThrown} whack={_whackScore} claw={_clawStage} pach={_pachUsed}:{_pachScore} [{string.Join("; ", parts)}]";
    }

    public static void Update(Cinema cinema, float dt, float now)
    {
        if (cinema == null || cinema.Theatre == null) return;
        if (!_ready || _cinema != cinema || _t != cinema.Theatre)
        {
            _cinema = cinema;
            _t = cinema.Theatre;
            All.Clear();
            SetupGames();
            RefreshBoards();
            _ready = true;
        }

        _now = now;
        _dt = dt;
        Carry();
        Prizes();
        PrizeEffects();
        if (Theatre.RoomOf(TheatreWalk.Eye, Vector3.zero) == Theatre.Room.Arcade) PhotoBooth();
        // the games only run while you're in the Fun Hall (or something's still flying there)
        var here = Theatre.RoomOf(TheatreWalk.Eye, Vector3.zero) == Theatre.Room.Arcade;
        if (here || Busy()) Games();
    }
}
