using UnityEngine;

namespace InkVR.UI;

/// <summary>The eight games themselves (see <see cref="Arcade"/> for holding, tickets and prizes).</summary>
public static partial class Arcade
{
    private static float AF => Theatre.RoomFloor(Theatre.Room.Arcade);

    private static readonly Holdable[] TossBalls = new Holdable[3];
    private static readonly Holdable[] Darts = new Holdable[3];
    private static readonly Holdable[,] Skee = new Holdable[2, Theatre.SkeeBalls];
    private static Holdable _gun, _mallet, _whackMallet;
    private static readonly System.Random Rng = new(77);

    private static void SetupGames()
    {
        for (var k = 0; k < 3; k++) TossBalls[k] = Make("TossBall" + k, Vector3.zero, Quaternion.identity, 0.1f, false);
        for (var k = 0; k < 3; k++) Darts[k] = Make("Dart" + k, Vector3.zero, Quaternion.identity, 0.09f, false);
        for (var lane = 0; lane < 2; lane++)
        for (var k = 0; k < Theatre.SkeeBalls; k++)
            Skee[lane, k] = Make($"Skee{lane}_{k}", Vector3.zero, Quaternion.identity, 0.09f, false);
        _gun = Make("CorkGun", new Vector3(0, -0.07f, 0), Quaternion.identity, 0.14f, true);
        _mallet = Make("Mallet", new Vector3(0, 0.85f, 0), Quaternion.Euler(-90, 0, 0), 0.16f, true);
        _whackMallet = Make("WhackMallet", new Vector3(0, 0.08f, 0), Quaternion.Euler(90, 0, 0), 0.13f, true);
        ResetGames();
    }

    private static void ResetGames()
    {
        _tossScore = _tossThrown = 0;
        _tossEndAt = -1;
        _corks = 0;
        _galleryScore = 0;
        _galleryEndAt = -1;
        _dartScore = _dartsThrown = 0;
        _dartsEndAt = -1;
        _strongAt = -10;
        _puck = 0;
        _whackEnd = -1;
        _whackScore = 0;
        _clawStage = 0;
        _pachBall = -1;
        _pachUsed = _pachScore = 0;
        for (var i = 0; i < 2; i++)
        {
            _skeeScore[i] = _skeeThrown[i] = 0;
            _skeeEndAt[i] = -1;
        }

        for (var i = 0; i < Counted.Length; i++) Counted[i] = false;
    }

    private static bool Busy()
    {
        foreach (var o in All)
            if (o.Loose || o.Hand >= 0 || o.BackAt > 0)
                return true;
        return _whackEnd > 0 || _clawStage != 0 || _pachBall >= 0 || _tossEndAt > 0 || _dartsEndAt > 0 || _galleryEndAt > 0;
    }

    private static void Games()
    {
        InkToss();
        Gallery();
        DartsGame();
        StrongMan();
        InkBall();
        Whack();
        Claw();
        Pachinko();
    }

    // which throwables have been counted as thrown this round (index by All position)
    private static readonly bool[] Counted = new bool[64];

    private static bool JustThrown(Holdable o)
    {
        var i = All.IndexOf(o);
        if (i < 0 || i >= Counted.Length) return false;
        if (o.Loose && !Counted[i])
        {
            Counted[i] = true;
            return true;
        }

        if (!o.Loose && o.Hand < 0 && o.BackAt < 0 && (o.Pos - o.Rest).sqrMagnitude < 1e-6f) Counted[i] = false;
        return false;
    }

    private static void Hide(Holdable o, float back)
    {
        o.Loose = false;
        o.BackAt = _now + back;
        o.T.gameObject.SetActive(false);
    }

    private static void Show(Holdable o)
    {
        if (!o.T.gameObject.activeSelf) o.T.gameObject.SetActive(true);
    }

    /// <summary>A free ball's flight, a step: gravity, the floor, the room's walls; true while it's still moving.</summary>
    private static bool Fly(Holdable o, float r, float bounce = 0.35f)
    {
        o.Vel.y -= G * _dt;
        o.Pos += o.Vel * _dt;
        var floor = Theatre.SurfaceUnder(o.Pos);
        if (o.Pos.y < floor + r)
        {
            o.Pos.y = floor + r;
            if (o.Vel.y < 0) o.Vel.y = -o.Vel.y * bounce;
            o.Vel.x *= 0.85f;
            o.Vel.z *= 0.85f;
            if (Mathf.Abs(o.Vel.y) < 0.4f) o.Vel.y = 0;
        }

        Theatre.Contain(ref o.Pos, ref o.Vel, r);
        Place(o);
        return o.Vel.sqrMagnitude > 0.04f || o.Pos.y > floor + r + 0.01f;
    }

    // =====================================================================================================
    // Ink Toss
    // =====================================================================================================

    private static int _tossScore, _tossThrown;
    private static float _tossEndAt;

    private static void InkToss()
    {
        var board = Theatre.TossBoardAt;
        foreach (var b in TossBalls)
        {
            if (b == null) continue;
            if (JustThrown(b)) _tossThrown++;
            if (!b.Loose) continue;
            Show(b);
            var prevZ = b.Pos.z;
            var moving = Fly(b, 0.055f);
            if (prevZ < board.z - 0.055f && b.Pos.z >= board.z - 0.055f && Mathf.Abs(b.Pos.x - board.x) < 0.95f && Mathf.Abs(b.Pos.y - board.y) < 0.95f)
            {
                var d = b.Pos - Theatre.TossGrin;
                if (d.x * d.x / 0.25f + d.y * d.y / 0.045f < 1f)
                {
                    // in the grin!
                    _tossScore += 100;
                    TheatreSounds.Play(TheatreSounds.Ding, Theatre.TossGrin, 0.6f);
                    TheatreSounds.Play(TheatreSounds.Boing, Theatre.TossGrin, 0.35f, 0.8f);
                    Hide(b, 1.2f);
                    continue;
                }

                b.Pos.z = board.z - 0.056f;
                b.Vel.z = -Mathf.Abs(b.Vel.z) * 0.35f;
                b.Vel.x *= 0.7f;
                TheatreSounds.Play(TheatreSounds.Knock, b.Pos, 0.25f, 1.8f);
                Place(b);
            }

            if (!moving) Hide(b, 0.6f);
        }

        SetSign("TossInfo", _tossThrown == 0 ? "3 balls  -  into the grin!" : $"SCORE {_tossScore}   ({_tossThrown} of 3)");
        var done = _tossThrown >= 3;
        foreach (var b in TossBalls)
            if (b != null && (b.Loose || b.Hand >= 0))
                done = false;
        if (done && _tossEndAt < 0) _tossEndAt = _now + 1.0f;
        if (_tossEndAt > 0 && _now >= _tossEndAt)
        {
            var t = Award(0, _tossScore, 50, board);
            SetSign("TossInfo", $"SCORE {_tossScore}  -  {t} TICKETS!");
            _tossScore = _tossThrown = 0;
            _tossEndAt = -1;
        }
    }

    // =====================================================================================================
    // The shooting gallery
    // =====================================================================================================

    private static int _corks, _galleryScore;
    private static float _galleryEndAt;
    private static readonly bool[] TriggerWas = new bool[2];
    private static readonly float[] TargetDown = new float[2 * Theatre.GalleryPerRow];
    private static readonly float[] TargetUpAt = new float[2 * Theatre.GalleryPerRow];
    private static Transform _cork;
    private static Vector3 _corkFrom, _corkTo;
    private static float _corkT = 1f;

    private static void Gallery()
    {
        // the targets ride their rails (back row one way, front row the other) and flip down when hit
        float x0 = Theatre.GalleryX0 + 0.15f, x1 = Theatre.GalleryX1 - 0.15f;
        for (var i = 0; i < TargetDown.Length; i++)
        {
            var piece = _t.Piece("GalTarget" + i);
            if (piece == null) continue;
            var row = i / Theatre.GalleryPerRow;
            var k = i % Theatre.GalleryPerRow;
            var f = Mathf.Repeat(k / (float)Theatre.GalleryPerRow + _now * (row == 0 ? 0.07f : -0.1f), 1f);
            var x = Mathf.Lerp(x0, x1, f);
            var rest = _t.PivotOf("GalTarget" + i);
            piece.localPosition = new Vector3(x, rest.y, rest.z);
            var want = TargetUpAt[i] > _now ? 1f : 0f;
            TargetDown[i] = Mathf.MoveTowards(TargetDown[i], want, _dt * 7f);
            piece.localRotation = Quaternion.Euler(TargetDown[i] * 85f, 0, 0);
        }

        var gun = _gun;
        if (gun == null) return;
        if (Held(gun) && _corks == 0 && _galleryEndAt < 0)
        {
            _corks = 10;
            _galleryScore = 0;
        }

        for (var h = 0; h < 2; h++)
        {
            var pull = Trigger(h);
            if (pull && !TriggerWas[h] && gun.Hand == h && _corks > 0) Fire(gun);
            TriggerWas[h] = pull;
        }

        // the cork in flight
        if (_corkT < 1f && _cork != null)
        {
            _corkT = Mathf.Min(1f, _corkT + _dt * 10f);
            _cork.localPosition = Vector3.Lerp(_corkFrom, _corkTo, _corkT);
            if (_corkT >= 1f) _cork.gameObject.SetActive(false);
        }

        if (_corks > 0 || _galleryEndAt > 0) SetSign("GalleryInfo", $"CORKS {_corks}   SCORE {_galleryScore}");
        if (_galleryEndAt > 0 && _now >= _galleryEndAt)
        {
            var t = Award(1, Mathf.Max(0, _galleryScore), 20, Theatre.CorkGunRest);
            SetSign("GalleryInfo", $"SCORE {_galleryScore}  -  {t} TICKETS!");
            _galleryEndAt = -1;
            _galleryScore = 0;
        }
    }

    private static void Fire(Holdable gun)
    {
        _corks--;
        if (_corks == 0) _galleryEndAt = _now + 1.2f;
        var from = gun.Point(new Vector3(0, 0, 0.45f));
        var dir = gun.Rot * Vector3.forward;
        TheatreSounds.Play(TheatreSounds.Pop, from, 0.45f, Random.Range(0.95f, 1.1f));
        TheatreToys.BuzzOf(gun.Hand, 0.6f, 0.04f);
        // the first target the cork meets
        var best = -1;
        var bestT = 99f;
        var planeZ = Theatre.GalleryZ - 0.01f;
        if (dir.z > 0.05f)
        {
            var t = (planeZ - from.z) / dir.z;
            var p = from + dir * t;
            for (var i = 0; i < TargetDown.Length; i++)
            {
                var piece = _t.Piece("GalTarget" + i);
                if (piece == null || TargetDown[i] > 0.1f) continue;
                var c = piece.localPosition + Vector3.up * 0.32f;
                if (new Vector2(p.x - c.x, p.y - c.y).sqrMagnitude < 0.13f * 0.13f && t < bestT)
                {
                    best = i;
                    bestT = t;
                }
            }

            _corkTo = best >= 0 ? from + dir * bestT : from + dir * Mathf.Min(t, 6f);
        }
        else _corkTo = from + dir * 4f;

        if (best >= 0)
        {
            var face = (best * 3 + best / Theatre.GalleryPerRow) % 4;
            var points = face switch { 0 => 10, 1 => 20, 2 => 30, _ => -20 };
            _galleryScore += points;
            TargetUpAt[best] = _now + 2.2f;
            TheatreSounds.Play(points > 0 ? TheatreSounds.Ding : TheatreSounds.Buzzer, _corkTo, points > 0 ? 0.4f : 0.3f, points > 0 ? 1.2f : 1f);
        }

        if (_cork == null) _cork = MakeCork();
        _cork.gameObject.SetActive(true);
        _corkFrom = from;
        _corkT = 0f;
        _cork.localPosition = from;
    }

    private static Transform MakeCork()
    {
        var mb = new Rendering.MeshBuilder();
        mb.Ellipsoid(Vector3.zero, new Vector3(0.016f, 0.016f, 0.02f), Quaternion.identity, 10, 6);
        mb.VertexColor = new Color(0.85f, 0.7f, 0.5f);
        var go = new GameObject("InkVR_Cork") { layer = VRLayers.UI };
        go.transform.SetParent(_cinema.Root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("InkVR_Cork");
        var mat = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.8f, 0.65f, 0.45f) };
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go.transform;
    }

    // =====================================================================================================
    // Darts
    // =====================================================================================================

    private static int _dartScore, _dartsThrown;
    private static float _dartsEndAt;
    private static readonly int[] DartNumbers = { 20, 1, 18, 4, 13, 6, 10, 15, 2, 17, 3, 19, 7, 16, 8, 11, 14, 9, 12, 5 };

    private static int DartPoints(float dx, float dy)
    {
        var rn = Mathf.Sqrt(dx * dx + dy * dy) / Theatre.DartboardRadius;
        if (rn > 1f) return 0;
        if (rn < 0.037f) return 50;
        if (rn < 0.094f) return 25;
        var a = Mathf.Repeat(Mathf.Atan2(dx, dy) / (Mathf.PI * 2) + 1f + 0.025f, 1f);
        var n = DartNumbers[Mathf.Clamp((int)(a * 20), 0, 19)];
        if (rn > 0.582f && rn < 0.629f) return n * 3;
        if (rn > 0.953f) return n * 2;
        return n;
    }

    private static void DartsGame()
    {
        var board = Theatre.DartboardAt;
        foreach (var d in Darts)
        {
            if (d == null) continue;
            if (JustThrown(d)) _dartsThrown++;
            if (!d.Loose) continue;
            var prevZ = d.Pos.z;
            d.Vel.y -= G * _dt;
            d.Pos += d.Vel * _dt;
            if (d.Vel.sqrMagnitude > 0.01f) d.Rot = Quaternion.LookRotation(d.Vel);
            var tip = d.Point(new Vector3(0, 0, 0.09f));
            if (prevZ < board.z - 0.1f && tip.z >= board.z - 0.005f)
            {
                var dx = tip.x - board.x;
                var dy = tip.y - board.y;
                if (dx * dx + dy * dy < Theatre.DartboardRadius * Theatre.DartboardRadius * 1.25f)
                {
                    var pts = DartPoints(dx, dy);
                    _dartScore += pts;
                    TheatreSounds.Play(TheatreSounds.Knock, tip, 0.2f, 2.4f);
                    SetSign("DartsInfo", pts > 0 ? $"{pts}!   SCORE {_dartScore}" : $"MISSED   SCORE {_dartScore}");
                }

                d.Pos += Vector3.back * (tip.z - (board.z - 0.005f)); // in the board (or the wall round it)
                d.Loose = false;
                d.BackAt = 1e9f; // stays in until the round's done
            }
            else if (d.Pos.y < AF + 0.02f || d.Pos.z > Theatre.DartboardAt.z || Mathf.Abs(d.Pos.x - board.x) > 3f)
            {
                d.Pos.y = Mathf.Max(d.Pos.y, AF + 0.02f);
                d.Loose = false;
                d.BackAt = 1e9f;
            }

            Place(d);
        }

        var stuck = 0;
        foreach (var d in Darts)
            if (d != null && d.BackAt > 1e8f)
                stuck++;
        if (_dartsThrown >= 3 && stuck >= 3 && _dartsEndAt < 0) _dartsEndAt = _now + 1.5f;
        if (_dartsEndAt > 0 && _now >= _dartsEndAt)
        {
            var t = Award(2, _dartScore, 15, board);
            SetSign("DartsInfo", $"SCORE {_dartScore}  -  {t} TICKETS!");
            foreach (var d in Darts)
                if (d != null)
                    d.BackAt = _now + 1.0f;
            _dartScore = _dartsThrown = 0;
            _dartsEndAt = -1;
        }
    }

    // =====================================================================================================
    // The strong man
    // =====================================================================================================

    private static float _strongAt, _puck, _puckGoal;
    private static Vector3 _headWas;

    private static void StrongMan()
    {
        var m = _mallet;
        var pad = Theatre.StrongPad;
        if (Held(m))
        {
            var head = m.Point(new Vector3(0, 0.06f, 0));
            var v = (head - _headWas) / Mathf.Max(_dt, 1e-3f);
            _headWas = head;
            if (_now - _strongAt > 2.5f && new Vector2(head.x - pad.x, head.z - pad.z).magnitude < 0.3f && head.y < pad.y + 0.12f && v.y < -1.2f)
            {
                var power = Mathf.Clamp01((-v.y - 1.2f) / 5.0f);
                _strongAt = _now;
                _puckGoal = power;
                TheatreSounds.Play(TheatreSounds.Whack, pad, 0.6f, 0.8f);
                TheatreToys.BuzzOf(m.Hand, 1f, 0.15f);
                var score = Mathf.RoundToInt(power * 100);
                if (power > 0.97f)
                {
                    TheatreSounds.Play(TheatreSounds.Bell, new Vector3(Theatre.StrongRail.x, Theatre.StrongTop, pad.z), 1f, 0.75f);
                    score = 100;
                }

                var t = Award(3, score, 15, pad);
                SetSign("StrongInfo", score >= 100 ? $"DING!  {t} TICKETS!" : $"{score}  -  {t} TICKETS");
            }
        }
        else _headWas = m != null ? m.Point(new Vector3(0, 0.06f, 0)) : Vector3.zero;

        // the puck flies up the rail and falls back
        var since = _now - _strongAt;
        var h = since < 0.5f ? _puckGoal * Mathf.Sin(since / 0.5f * Mathf.PI * 0.5f) : since < 1.6f ? _puckGoal * (1f - Mathf.Pow((since - 0.5f) / 1.1f, 2f)) : 0f;
        _puck = Mathf.Max(0, h);
        var puck = _t.Piece("Puck");
        if (puck != null) puck.localPosition = _t.PivotOf("Puck") + Vector3.up * (_puck * (Theatre.StrongTop - Theatre.StrongBottom - 0.1f));
    }

    // =====================================================================================================
    // Ink Ball
    // =====================================================================================================

    private static readonly int[] _skeeScore = new int[2], _skeeThrown = new int[2];
    private static readonly float[] _skeeEndAt = { -1, -1 };
    private static readonly float[] RingRadius = { 121, 91, 63, 38.5f, 18.7f };
    private static readonly int[] RingPoints = { 10, 20, 30, 40, 50 };

    private static void InkBall()
    {
        const float r = 0.05f, half = 0.32f;
        for (var lane = 0; lane < 2; lane++)
        {
            var z0 = Theatre.SkeeLaneZ(lane);
            for (var k = 0; k < Theatre.SkeeBalls; k++)
            {
                var b = Skee[lane, k];
                if (b == null) continue;
                if (JustThrown(b)) _skeeThrown[lane]++;
                if (!b.Loose) continue;
                Show(b);
                var onLane = b.Pos.x > Theatre.SkeeStartX - 0.1f && b.Pos.x < Theatre.SkeeLipX && Mathf.Abs(b.Pos.z - z0) < half + 0.05f;
                var surface = b.Pos.x < Theatre.SkeeRampX ? Theatre.SkeeLaneY : Mathf.Lerp(Theatre.SkeeLaneY, Theatre.SkeeLipY, (b.Pos.x - Theatre.SkeeRampX) / (Theatre.SkeeLipX - Theatre.SkeeRampX));
                if (onLane && b.Pos.y < surface + r + 0.02f)
                {
                    // rolling: held to the lane, slowed a little (and by the ramp's climb), kept between the rails
                    b.Pos.y = surface + r;
                    if (b.Vel.y < 0) b.Vel.y = 0;
                    b.Vel.x *= Mathf.Exp(-_dt * 0.3f);
                    b.Vel.z *= Mathf.Exp(-_dt * 0.6f);
                    if (b.Pos.x > Theatre.SkeeRampX) b.Vel.x -= G * 0.31f * _dt;
                    b.Pos += new Vector3(b.Vel.x, 0, b.Vel.z) * _dt;
                    if (Mathf.Abs(b.Pos.z - z0) > half - r)
                    {
                        b.Pos.z = z0 + Mathf.Sign(b.Pos.z - z0) * (half - r);
                        b.Vel.z = -b.Vel.z * 0.5f;
                    }

                    if (b.Pos.x >= Theatre.SkeeLipX && b.Vel.x > 0.3f)
                    {
                        // off the lip and up towards the rings
                        b.Vel.y = b.Vel.x * 0.62f;
                        b.Vel.x *= 0.9f;
                        b.Pos.y += 0.01f;
                    }

                    if (b.Vel.x < 0.05f && b.Vel.x > -0.05f || b.Pos.x < Theatre.SkeeStartX - 0.05f) Hide(b, 1.5f);
                    Place(b);
                    continue;
                }

                // flying: does it land in the target face?
                b.Vel.y -= G * _dt;
                b.Pos += b.Vel * _dt;
                if (b.Pos.x > Theatre.SkeeTargetX0 && b.Pos.x < Theatre.SkeeTargetX1 && Mathf.Abs(b.Pos.z - z0) < half)
                {
                    var v = (b.Pos.x - Theatre.SkeeTargetX0) / (Theatre.SkeeTargetX1 - Theatre.SkeeTargetX0);
                    var faceY = Mathf.Lerp(Theatre.SkeeTargetY0, Theatre.SkeeTargetY1, v);
                    if (b.Pos.y < faceY + r)
                    {
                        var u = (z0 + half - b.Pos.z) / (half * 2);
                        var pts = SkeePoints(u * 256f, v * 512f);
                        _skeeScore[lane] += pts;
                        TheatreSounds.Play(pts >= 100 ? TheatreSounds.Fanfare : pts > 0 ? TheatreSounds.Ding : TheatreSounds.Knock, b.Pos, pts > 0 ? 0.45f : 0.25f, pts > 0 ? 1f + pts / 200f : 1.5f);
                        SetSign("SkeeScore" + lane, $"SCORE {_skeeScore[lane]}");
                        Hide(b, 1.5f);
                        continue;
                    }
                }

                if (b.Pos.y < AF + r || b.Pos.x > Theatre.SkeeTargetX1 + 0.3f || Mathf.Abs(b.Pos.z - z0) > 2f)
                {
                    Hide(b, 1.5f);
                    continue;
                }

                Place(b);
            }

            var done = _skeeThrown[lane] >= Theatre.SkeeBalls;
            for (var k = 0; k < Theatre.SkeeBalls; k++)
                if (Skee[lane, k] != null && (Skee[lane, k].Loose || Skee[lane, k].Hand >= 0))
                    done = false;
            if (done && _skeeEndAt[lane] < 0) _skeeEndAt[lane] = _now + 1.2f;
            if (_skeeEndAt[lane] > 0 && _now >= _skeeEndAt[lane])
            {
                var t = Award(4, _skeeScore[lane], 40, new Vector3(Theatre.SkeeTargetX1, AF + 2f, z0));
                SetSign("SkeeScore" + lane, $"{_skeeScore[lane]}  -  {t} TICKETS");
                _skeeScore[lane] = _skeeThrown[lane] = 0;
                _skeeEndAt[lane] = -1;
            }
        }
    }

    /// <summary>Points for a ball landing at texture pixel (x, y) of the target face (256 by 512).</summary>
    private static int SkeePoints(float x, float y)
    {
        foreach (var (hx, hy) in new[] { (40f, 470f), (216f, 470f) })
            if ((x - hx) * (x - hx) + (y - hy) * (y - hy) < 26f * 26f)
                return 100;
        var d = Mathf.Sqrt((x - 128) * (x - 128) + (y - 200) * (y - 200));
        var pts = 0;
        for (var i = 0; i < RingRadius.Length; i++)
            if (d < RingRadius[i])
                pts = RingPoints[i];
        return pts;
    }

    // =====================================================================================================
    // Whack-a-Searcher
    // =====================================================================================================

    private static float _whackEnd = -1;
    private static int _whackScore;
    private static readonly float[] Pop = new float[6], PopUntil = new float[6];
    private static readonly Vector3[] HitterWas = new Vector3[3];

    private static void Whack()
    {
        var start = _t.Piece("WhackStart");
        var startAt = _t.PivotOf("WhackStart");
        var running = _whackEnd > 0;
        // the start button: press it down with a fingertip (or the mallet)
        if (!running && start != null)
        {
            var pressed = false;
            for (var h = 0; h < 2; h++)
                if (TheatreToys.HandOk(h))
                {
                    var tip = TheatreToys.TipOf(h);
                    if (new Vector2(tip.x - startAt.x, tip.z - startAt.z).magnitude < 0.07f && tip.y < startAt.y + 0.04f && tip.y > startAt.y - 0.08f) pressed = true;
                }

            start.localPosition = startAt + Vector3.down * (pressed ? 0.02f : 0f);
            if (pressed)
            {
                _whackEnd = _now + 30f;
                _whackScore = 0;
                TheatreSounds.Play(TheatreSounds.Ding, startAt, 0.5f, 0.8f);
            }
        }

        // hitters: the mallet's head, and both hands (a bare-handed whack counts too)
        var hitters = 0;
        var hit = new Vector3[3];
        var vel = new Vector3[3];
        if (Held(_whackMallet))
        {
            hit[hitters] = _whackMallet.Point(new Vector3(0, 0.45f, 0));
            hitters++;
        }

        for (var h = 0; h < 2; h++)
            if (TheatreToys.HandOk(h) && !(Held(_whackMallet) && _whackMallet.Hand == h))
                hit[hitters++] = TheatreToys.PalmOf(h);
        for (var i = 0; i < hitters; i++)
        {
            vel[i] = (hit[i] - HitterWas[i]) / Mathf.Max(_dt, 1e-3f);
            HitterWas[i] = hit[i];
        }

        for (var i = 0; i < 6; i++)
        {
            var piece = _t.Piece("Searcher" + i);
            if (piece == null) continue;
            if (running && Pop[i] <= 0.01f && PopUntil[i] < _now && Rng.NextDouble() < _dt * 0.35)
                PopUntil[i] = _now + 0.7f + (float)Rng.NextDouble() * 0.6f;
            var want = running && PopUntil[i] > _now ? 1f : 0f;
            Pop[i] = Mathf.MoveTowards(Pop[i], want, _dt * (want > 0 ? 8f : 12f));
            var hole = Theatre.WhackHole(i);
            var head = hole + Vector3.up * (Pop[i] * 0.22f - 0.08f);
            if (Pop[i] > 0.5f)
                for (var k = 0; k < hitters; k++)
                    if ((hit[k] - head).sqrMagnitude < 0.15f * 0.15f && vel[k].y < -0.6f)
                    {
                        _whackScore += 10;
                        PopUntil[i] = 0;
                        Pop[i] = 0.3f;
                        TheatreSounds.Play(TheatreSounds.Whack, head, 0.5f, Random.Range(0.9f, 1.15f));
                        break;
                    }

            piece.localPosition = _t.PivotOf("Searcher" + i) + Vector3.up * (Pop[i] * 0.22f - 0.2f);
        }

        if (running)
        {
            var left = Mathf.CeilToInt(_whackEnd - _now);
            SetSign("WhackInfo", $"SCORE {_whackScore}   {left}s");
            if (_now >= _whackEnd)
            {
                var t = Award(5, _whackScore, 20, startAt);
                SetSign("WhackInfo", $"SCORE {_whackScore}  -  {t} TICKETS!");
                _whackEnd = -1;
            }
        }
    }

    // =====================================================================================================
    // The claw
    // =====================================================================================================

    private static int _clawStage, _clawPrize = -1;
    private static float _clawT;
    private static Vector3 _clawPos; // offset from the claw's pivot (x, z), and its drop (y, down)
    private static bool _buttonWas;
    private static readonly float[] PrizeBackAt = new float[Theatre.ClawPrizes];

    private static void Claw()
    {
        var claw = _t.Piece("Claw");
        var stick = _t.Piece("ClawStick");
        if (claw == null) return;
        var pivot = _t.PivotOf("Claw");
        var lo = Theatre.ClawBox0;
        var hi = Theatre.ClawBox1;
        var knob = Theatre.ClawStick + Vector3.up * 0.14f;
        var tilt = Vector2.zero;
        var h = TheatreLife.HolderOf("ClawStick");
        if (h < 0) h = TheatreLife.Grab(knob, 0.1f, "ClawStick");
        if (h >= 0)
        {
            var d = TheatreToys.PalmOf(h) - knob;
            tilt = new Vector2(Mathf.Clamp(d.x / 0.07f, -1f, 1f), Mathf.Clamp(d.z / 0.07f, -1f, 1f));
        }

        if (stick != null) stick.localRotation = Quaternion.Euler(tilt.y * 25f, 0, -tilt.x * 25f);
        var minX = lo.x + 0.1f - pivot.x;
        var maxX = hi.x - 0.1f - pivot.x;
        var minZ = lo.z + 0.1f - pivot.z;
        var maxZ = hi.z - 0.1f - pivot.z;
        var dropTo = (pivot.y - 0.33f) - (lo.y + 0.12f);

        // the button: a press with a fingertip
        var button = Theatre.ClawButton;
        var pressed = false;
        for (var k = 0; k < 2; k++)
            if (TheatreToys.HandOk(k))
            {
                var tip = TheatreToys.TipOf(k);
                if (new Vector2(tip.x - button.x, tip.z - button.z).magnitude < 0.06f && tip.y < button.y + 0.05f && tip.y > button.y - 0.08f) pressed = true;
            }

        var press = pressed && !_buttonWas;
        _buttonWas = pressed;
        var btn = _t.Piece("ClawButton");
        if (btn != null) btn.localPosition = _t.PivotOf("ClawButton") + Vector3.down * (pressed ? 0.015f : 0f);

        switch (_clawStage)
        {
            case 0: // steering
                _clawPos.x = Mathf.Clamp(_clawPos.x + tilt.x * 0.3f * _dt, minX, maxX);
                _clawPos.z = Mathf.Clamp(_clawPos.z + tilt.y * 0.3f * _dt, minZ, maxZ);
                if (press)
                {
                    _clawStage = 1;
                    _clawT = 0;
                    TheatreSounds.Play(TheatreSounds.Click, button, 0.5f);
                }

                break;
            case 1: // down
                _clawT += _dt;
                _clawPos.y = Mathf.Min(dropTo, _clawT * 0.3f);
                if (_clawPos.y >= dropTo)
                {
                    _clawStage = 2;
                    _clawT = 0;
                    // what's under it?
                    var at = pivot + new Vector3(_clawPos.x, -0.33f - _clawPos.y, _clawPos.z);
                    _clawPrize = -1;
                    for (var i = 0; i < Theatre.ClawPrizes; i++)
                    {
                        if (PrizeBackAt[i] > 0) continue;
                        var p = _t.PivotOf("ClawPrize" + i);
                        if (new Vector2(p.x - at.x, p.z - at.z).magnitude < 0.09f && Rng.NextDouble() < 0.7) _clawPrize = i;
                    }
                }

                break;
            case 2: // close, then up
                _clawT += _dt;
                if (_clawT > 0.5f) _clawPos.y = Mathf.Max(0, dropTo - (_clawT - 0.5f) * 0.3f);
                if (_clawT > 0.5f && _clawPos.y <= 0)
                {
                    _clawStage = 3;
                    _clawT = 0;
                }

                break;
            case 3: // over to the chute
            {
                var chute = Theatre.ClawChute - pivot;
                var to = new Vector2(chute.x, chute.z);
                var cur = Vector2.MoveTowards(new Vector2(_clawPos.x, _clawPos.z), to, _dt * 0.25f);
                _clawPos.x = cur.x;
                _clawPos.z = cur.y;
                if ((cur - to).sqrMagnitude < 1e-5f)
                {
                    _clawStage = 0;
                    if (_clawPrize >= 0)
                    {
                        var t = Award(6, 100, 10, Theatre.ClawTray);
                        SetSign("TicketCount", "YOUR TICKETS: " + Progress.Get("tickets"));
                        TheatreSounds.Play(TheatreSounds.Ding, Theatre.ClawTray, 0.6f);
                        PrizeBackAt[_clawPrize] = _now + 4f;
                        Plugin.L.LogInfo($"The claw won a prize ({t} tickets)");
                    }

                    _clawPrize = -1;
                }

                break;
            }
        }

        claw.localPosition = pivot + new Vector3(_clawPos.x, -_clawPos.y, _clawPos.z);
        for (var i = 0; i < Theatre.ClawPrizes; i++)
        {
            var piece = _t.Piece("ClawPrize" + i);
            if (piece == null) continue;
            var rest = _t.PivotOf("ClawPrize" + i);
            if (i == _clawPrize && _clawStage >= 2 && _clawT > 0.5f) piece.localPosition = claw.localPosition + Vector3.down * 0.42f;
            else if (PrizeBackAt[i] > 0)
            {
                piece.localPosition = Theatre.ClawTray;
                if (_now >= PrizeBackAt[i])
                {
                    PrizeBackAt[i] = 0;
                    piece.localPosition = rest;
                }
            }
            else piece.localPosition = rest;
        }
    }

    // =====================================================================================================
    // Pachinko
    // =====================================================================================================

    private static int _pachBall = -1, _pachUsed, _pachScore;
    private static float _pull;
    private static Vector2 _pachPos, _pachVel;
    private static bool _pachRising;
    private static readonly int[] SlotPoints = { 10, 50, 20, 100, 20, 50, 10 };
    private static Vector2[] _pins;

    private static void Pachinko()
    {
        _pins ??= new System.Collections.Generic.List<Vector2>(Theatre.PachPins()).ToArray();
        var plunger = _t.Piece("Plunger");
        if (plunger == null) return;
        var rest = _t.PivotOf("Plunger");
        var knob = rest + Vector3.forward * 0.135f;
        var h = TheatreLife.HolderOf("Plunger");
        if (h < 0 && _pachBall < 0) h = TheatreLife.Grab(knob + Vector3.forward * _pull, 0.1f, "Plunger");
        if (h >= 0) _pull = Mathf.Clamp(TheatreToys.PalmOf(h).z - knob.z, 0f, 0.12f);
        else if (_pull > 0.005f && _pachBall < 0)
        {
            // let go: the ball goes
            if (_pachUsed >= 4)
            {
                _pachUsed = 0;
                _pachScore = 0;
            }

            _pachBall = _pachUsed++;
            var strength = _pull / 0.12f;
            _pachPos = new Vector2(Theatre.PachX1 - 0.06f, Theatre.PachY0 + 0.05f);
            _pachVel = new Vector2(0, 2.0f + 2.0f * strength);
            _pachRising = true;
            _pachLaunch = strength;
            TheatreSounds.Play(TheatreSounds.Boing, Theatre.PachPlunger, 0.4f, 1.6f);
            _pull = 0;
        }
        else _pull = Mathf.MoveTowards(_pull, 0, _dt * 0.6f);

        plunger.localPosition = rest + Vector3.forward * _pull;
        if (_pachBall >= 0) PachStep();
        for (var k = 0; k < 4; k++)
        {
            var ball = _t.Piece("PachBall" + k);
            if (ball == null) continue;
            if (k == _pachBall) ball.localPosition = new Vector3(_pachPos.x, _pachPos.y, Theatre.PachZ + 0.03f);
            else ball.localPosition = k < _pachUsed ? Theatre.PachPlunger + new Vector3(-0.3f - k * 0.05f, -0.1f, 0.25f) : _t.PivotOf("PachBall" + k);
        }

        SetSign("PachInfo", _pachUsed == 0 ? "pull back and let go" : $"BALL {_pachUsed} OF 4   SCORE {_pachScore}");
    }

    private static float _pachLaunch;

    private static void PachStep()
    {
        float x0 = Theatre.PachX0 + 0.02f, x1 = Theatre.PachX1 - 0.15f, y0 = Theatre.PachY0, y1 = Theatre.PachY1 - 0.03f;
        const float r = 0.02f;
        // a few substeps: the pins are small
        for (var s = 0; s < 4; s++)
        {
            var dt = _dt / 4f;
            if (_pachRising)
            {
                _pachVel.y -= G * 0.45f * dt;
                _pachPos += _pachVel * dt;
                if (_pachPos.y >= y1 - 0.05f || _pachVel.y <= 0.2f)
                {
                    // over the top and out into the pins
                    _pachRising = false;
                    _pachVel = new Vector2(-(0.4f + 1.1f * _pachLaunch), -0.1f);
                    _pachPos.x = x1 - 0.02f;
                    _pachPos.y = Mathf.Min(_pachPos.y, y1 - 0.06f);
                }

                continue;
            }

            _pachVel.y -= G * 0.45f * dt;
            _pachPos += _pachVel * dt;
            foreach (var pin in _pins)
            {
                var d = _pachPos - pin;
                var dist = d.magnitude;
                if (dist > r + 0.007f || dist < 1e-5f) continue;
                var n = d / dist;
                _pachPos = pin + n * (r + 0.0071f);
                var vn = Vector2.Dot(_pachVel, n);
                if (vn < 0) _pachVel -= n * vn * 1.5f;
                _pachVel += new Vector2(((float)Rng.NextDouble() - 0.5f) * 0.08f, 0);
                if (Rng.NextDouble() < 0.2) TheatreSounds.Play(TheatreSounds.Click, new Vector3(pin.x, pin.y, Theatre.PachZ), 0.06f, 2.5f);
            }

            if (_pachPos.x < x0 + r)
            {
                _pachPos.x = x0 + r;
                _pachVel.x = Mathf.Abs(_pachVel.x) * 0.5f;
            }

            if (_pachPos.x > x1 - r)
            {
                _pachPos.x = x1 - r;
                _pachVel.x = -Mathf.Abs(_pachVel.x) * 0.5f;
            }

            if (_pachPos.y < y0 + 0.2f)
            {
                var slot = Mathf.Clamp((int)((_pachPos.x - Theatre.PachX0) / ((Theatre.PachX1 - 0.12f - Theatre.PachX0) / 7f)), 0, 6);
                var pts = SlotPoints[slot];
                _pachScore += pts;
                TheatreSounds.Play(pts >= 100 ? TheatreSounds.Fanfare : TheatreSounds.Ding, new Vector3(_pachPos.x, _pachPos.y, Theatre.PachZ), 0.4f, 0.9f + pts / 150f);
                _pachBall = -1;
                if (_pachUsed >= 4)
                {
                    var t = Award(7, _pachScore, 25, Theatre.PachPlunger);
                    SetSign("PachInfo", $"SCORE {_pachScore}  -  {t} TICKETS!");
                }

                return;
            }
        }
    }
}
