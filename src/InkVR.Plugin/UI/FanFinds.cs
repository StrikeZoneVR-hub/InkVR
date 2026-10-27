using System.Collections.Generic;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The fan finds: twenty-six things in the theatre to find or do, a nod to every chapter and to the people of Joey
/// Drew Studios. Each one chimes and turns up on the lobby's tally board; find them all and Mr. Drew's office opens.
/// Found finds are kept in InkVR's own <see cref="Progress"/> file.
/// </summary>
public static class FanFinds
{
    public enum How
    {
        Touch, // a hand on it (or a grip on it)
        Look, // looked at, from close, for a moment
        Do // something you did (TheatreToys/TheatreLife/Arcade report it)
    }

    public sealed class Find
    {
        public string Id, Title;
        public How How;
        public System.Func<Vector3> At;
        public float Radius = 0.25f;
    }

    public static readonly Find[] All =
    {
        new() { Id = "reserved", Title = "A seat kept for Henry", How = How.Touch, At = () => new Vector3(-(1.1f + 0.28f), 0.535f, 3.1f), Radius = 0.25f },
        new() { Id = "stub", Title = "Opening night's ticket", How = How.Touch, At = () => Theatre.TicketStubAt },
        new() { Id = "coin", Title = "A Bendy coin", How = How.Touch, At = () => Theatre.CoinAt, Radius = 0.22f },
        new() { Id = "doll", Title = "The little Bendy doll", How = How.Do },
        new() { Id = "cutout", Title = "The cut-out that moves", How = How.Do },
        new() { Id = "wally", Title = "Wally's note", How = How.Look, At = () => Theatre.WallyNoteAt },
        new() { Id = "soup", Title = "Bacon Soup", How = How.Do },
        new() { Id = "music", Title = "Sammy's sheet music", How = How.Look, At = () => Theatre.SheetMusicAt },
        new() { Id = "writing", Title = "The creator lied", How = How.Do },
        new() { Id = "bell", Title = "Ring for service", How = How.Do },
        new() { Id = "ghost", Title = "The ghost light", How = How.Touch, At = () => Theatre.GhostLight + Vector3.up * 1.72f, Radius = 0.3f },
        new() { Id = "sheep", Title = "A sheep's bell", How = How.Touch, At = () => Theatre.SheepBellAt, Radius = 0.2f },
        new() { Id = "bowl", Title = "Boris's soup bowl", How = How.Touch, At = () => Theatre.BorisBowlAt },
        new() { Id = "costume", Title = "Dressed for the show", How = How.Do },
        new() { Id = "halo", Title = "Alice's halo", How = How.Touch, At = () => Theatre.AliceHaloAt, Radius = 0.3f },
        new() { Id = "mirror", Title = "Who are you tonight?", How = How.Do },
        new() { Id = "script", Title = "Susie's script", How = How.Look, At = () => Theatre.ScriptAt },
        new() { Id = "machine", Title = "The ink machine runs", How = How.Do },
        new() { Id = "gear", Title = "A gear from the Abyss", How = How.Touch, At = () => Theatre.GearAt, Radius = 0.6f },
        new() { Id = "shrine", Title = "He will set us free", How = How.Do },
        new() { Id = "keys", Title = "Wally's lost keys", How = How.Touch, At = () => Theatre.KeysAt },
        new() { Id = "reel", Title = "The golden reel", How = How.Touch, At = () => Theatre.GoldenReelAt, Radius = 0.3f },
        new() { Id = "blueprint", Title = "Bertrum's plans", How = How.Look, At = () => Theatre.BlueprintAt },
        new() { Id = "spot", Title = "In the spotlight", How = How.Do },
        new() { Id = "landticket", Title = "A ticket to Bendy Land", How = How.Touch, At = () => Theatre.LandTicketAt },
        new() { Id = "photo", Title = "Say cheese", How = How.Do }
    };

    public static int Count => All.Length;

    public static int Found
    {
        get
        {
            var n = 0;
            foreach (var f in All)
                if (Progress.Has("find." + f.Id))
                    n++;
            return n;
        }
    }

    public static bool Complete => Found >= Count;

    private static Cinema _cinema;
    private static readonly Dictionary<string, float> LookTime = new();
    private static TMPro.TextMeshPro _toast;
    private static float _toastUntil;

    /// <summary>Something's been found (or done): a chime, its card on the board, and maybe the office.</summary>
    public static void Mark(string id)
    {
        if (Progress.Has("find." + id)) return;
        Find find = null;
        foreach (var f in All)
            if (f.Id == id)
                find = f;
        if (find == null) return;
        Progress.Set("find." + id, 1);
        var n = Found;
        TheatreSounds.Play(TheatreSounds.Chime, TheatreWalk.Eye + TheatreWalk.Forward, 0.6f);
        Toast($"FAN FIND {n} OF {Count}:  {find.Title.ToUpperInvariant()}");
        Plugin.L.LogInfo($"Fan find {n}/{Count}: {find.Title}");
        Refresh();
        if (n >= Count)
        {
            Theatre.OfficeOpen = true;
            TheatreSounds.Play(TheatreSounds.Fanfare, TheatreWalk.Eye + TheatreWalk.Forward, 0.7f);
            Toast("EVERYTHING FOUND!  MR. DREW'S OFFICE IS OPEN...");
        }
    }

    public static void Refresh()
    {
        if (_cinema == null) return;
        var n = Found;
        Theatre.OfficeOpen = n >= Count;
        Set("FindCount", n + " OF " + Count);
        for (var i = 0; i < All.Length; i++) Set("FindCard" + i, Progress.Has("find." + All[i].Id) ? All[i].Title.ToUpperInvariant() : "?");
    }

    private static void Set(string id, string text)
    {
        var tmp = _cinema.Sign(id);
        if (tmp != null && tmp.text != text) tmp.text = text;
    }

    private static void Toast(string text)
    {
        if (_cinema == null) return;
        _toast = _cinema.Toast(text);
        _toastUntil = Time.unscaledTime + 3.5f;
    }

    public static void Update(Cinema cinema, float dt)
    {
        if (_cinema != cinema)
        {
            _cinema = cinema;
            Refresh();
        }

        // the message: hangs in front of you, fades
        if (_toast != null)
        {
            var left = _toastUntil - Time.unscaledTime;
            var c = _toast.color;
            var a = Mathf.Clamp01(left / 0.6f);
            if (Mathf.Abs(c.a - a) > 1e-3f) _toast.color = new Color(c.r, c.g, c.b, a);
            var eye = TheatreWalk.Eye;
            _toast.transform.localPosition = Vector3.Lerp(_toast.transform.localPosition, eye + TheatreWalk.Forward * 1.4f + Vector3.down * 0.35f, 1f - Mathf.Exp(-dt * 6f));
            _toast.transform.localRotation = Quaternion.LookRotation(TheatreWalk.Forward);
            if (left <= 0) _toast.gameObject.SetActive(false);
        }

        foreach (var f in All)
        {
            if (f.How == How.Do || Progress.Has("find." + f.Id)) continue;
            var at = f.At();
            if (f.How == How.Touch)
            {
                for (var h = 0; h < 2; h++)
                    if (TheatreToys.HandOk(h) && ((TheatreToys.PalmOf(h) - at).sqrMagnitude < f.Radius * f.Radius || (TheatreToys.TipOf(h) - at).sqrMagnitude < f.Radius * f.Radius * 0.5f))
                    {
                        Mark(f.Id);
                        TheatreToys.BuzzOf(h, 0.4f, 0.05f);
                        break;
                    }

                continue;
            }

            // looked at, from near, for a moment
            LookTime.TryGetValue(f.Id, out var t);
            t = TheatreToys.Sees(at, 14f, 2.2f) ? t + dt : 0f;
            LookTime[f.Id] = t;
            if (t > 0.9f) Mark(f.Id);
        }

        // the cut-out: catch hold of it
        var cut = cinema.Theatre?.Piece("Cutout");
        if (cut != null)
            for (var h = 0; h < 2; h++)
                if (TheatreToys.HandOk(h) && (TheatreToys.PalmOf(h) - (cut.localPosition + Vector3.up * 0.7f)).sqrMagnitude < 0.5f * 0.5f)
                    Mark("cutout");
    }
}
