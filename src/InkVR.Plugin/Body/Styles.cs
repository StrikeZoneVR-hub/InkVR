using System.Collections.Generic;

namespace InkVR.Body;

/// <summary>Who you are: Henry two ways, or one of the studio's characters, head to toe (the head shows in mirrors and your shadow).</summary>
public enum BodyStyle
{
    /// <summary>Same style as the hands: chunky, rounded, cartoon shoes, red braces.</summary>
    Toon,

    /// <summary>Lifelike proportions, clothes (waistcoat, tie, pinstripes, boots) and hands, still inked.</summary>
    Lifelike,

    Bendy,
    Boris,
    Alice,
    InkDemon,
    Sammy,
    Projectionist,
    BruteBoris,
    Searcher,
    Joey,
    Wally,
    BeastBendy
}

/// <summary>The shape of the hands: lifelike (one smooth skinned hand) or the cartoon glove.</summary>
public enum HandKind
{
    Lifelike,
    Toon
}

/// <summary>What covers the arms: Henry's rolled shirt sleeves, a full sleeve to the wrist (a jacket), or none (a cartoon's bare arms).</summary>
public enum SleeveKind
{
    Rolled,
    Full,
    Bare
}

/// <summary>The watch on your left wrist.</summary>
public enum WatchKind
{
    Tank,
    Bendy,
    Studio,
    Gauge,
    Angel,
    Demon,
    None
}

public sealed class StyleDef
{
    public BodyStyle Id;

    /// <summary>The setting's value (Body.Style) and what the menus show.</summary>
    public string Key, Label, Blurb;

    public HandKind Hands;
    public SleeveKind Sleeves;

    /// <summary>Arm thickness (Brute Boris's are huge).</summary>
    public float ArmBulk = 1f;

    /// <summary>Henry's wedding band on the left ring finger.</summary>
    public bool WeddingBand;

    /// <summary>Hand and arm materials swapped for this style's own (glove stays glove, skin becomes ink...).</summary>
    public Dictionary<string, string> Remap = new();

    /// <summary>Round cartoon shoes leave the cartoon print; otherwise a boot's.</summary>
    public bool CartoonFeet;

    /// <summary>Ink drips off you all the time (drops a second), and your footprints are always inky.</summary>
    public float Drips;

    /// <summary>The game's own character model for this style (a Resources path in the player's game), worn as the body.</summary>
    public string GameModel;

    /// <summary>Parts of that model left off (a weapon in its hand, a loose prop).</summary>
    public string[] ModelHide;

    /// <summary>Or a statue in the Archives scene to read the model from (characters only in a chapter's own files).</summary>
    public string ArchiveModel;
}

public static class Styles
{
    private static Dictionary<string, string> Map(params string[] pairs)
    {
        var d = new Dictionary<string, string>();
        for (var i = 0; i + 1 < pairs.Length; i += 2) d[pairs[i]] = pairs[i + 1];
        return d;
    }

    // inky hands and arms, for the studio's ink creatures
    private static Dictionary<string, string> Inky(string skin, params string[] more)
    {
        var d = Map("lifeSkin", skin + "Skin", "lifePalm", skin + "Palm", "lifeNail", skin + "Nail", "lifeForearm", skin + "Forearm", "sleeve", skin + "Forearm", "sleeveRolled", skin + "Forearm");
        for (var i = 0; i + 1 < more.Length; i += 2) d[more[i]] = more[i + 1];
        return d;
    }

    public static readonly StyleDef[] All =
    {
        new() { Id = BodyStyle.Lifelike, Key = "Lifelike", Label = "HENRY", Blurb = "Henry Stein: waistcoat, tie, rolled sleeves and his tank watch.", Hands = HandKind.Lifelike, Sleeves = SleeveKind.Rolled, WeddingBand = true },
        new() { Id = BodyStyle.Toon, Key = "Toon", Label = "TOON HENRY", Blurb = "Henry drawn like a cartoon: braces, big shoes and white gloves.", Hands = HandKind.Toon, Sleeves = SleeveKind.Rolled, CartoonFeet = true },
        new()
        {
            Id = BodyStyle.Bendy, Key = "Bendy", Label = "BENDY", Blurb = "The dancing demon himself: bow tie, white gloves, and a tail that swishes as you move.",
            Hands = HandKind.Toon, Sleeves = SleeveKind.Bare, CartoonFeet = true, Remap = Map("toonForearm", "bendyArm", "sleeve", "bendyArm", "sleeveRolled", "bendyArm")
        },
        new()
        {
            Id = BodyStyle.Boris, Key = "Boris", Label = "BORIS", Blurb = "Boris the wolf: overalls, floppy ears and a bushy tail.",
            Hands = HandKind.Toon, Sleeves = SleeveKind.Bare, CartoonFeet = true, Remap = Map("toonForearm", "borisArm", "sleeve", "borisArm", "sleeveRolled", "borisArm"),
            ArchiveModel = "Boris"
        },
        new()
        {
            Id = BodyStyle.Alice, Key = "Alice", Label = "ALICE ANGEL", Blurb = "Alice Angel: her dress, her bow and a halo that glows over your head.",
            Hands = HandKind.Toon, Sleeves = SleeveKind.Bare, CartoonFeet = true, Remap = Map("toonForearm", "aliceArm", "sleeve", "aliceArm", "sleeveRolled", "aliceArm"),
            ArchiveModel = "Alice"
        },
        new()
        {
            Id = BodyStyle.InkDemon, Key = "InkDemon", Label = "THE INK DEMON", Blurb = "Bendy as the studio made him: ink drips off you and every step leaves a print.",
            Hands = HandKind.Lifelike, Sleeves = SleeveKind.Bare, ArmBulk = 1.08f, Drips = 1.5f, Remap = Inky("demon"),
            GameModel = "gameplay/characters/ai_bendy"
        },
        new()
        {
            Id = BodyStyle.Sammy, Key = "Sammy", Label = "SAMMY LAWRENCE", Blurb = "The prophet: inky overalls, and his Bendy cut-out mask tied on with rope.",
            Hands = HandKind.Lifelike, Sleeves = SleeveKind.Bare, Drips = 0.6f, Remap = Inky("sammy"),
            GameModel = "gameplay/characters/ai_sammylawrence", ModelHide = new[] { "Model_SammyMaskPhysics" }
        },
        new()
        {
            Id = BodyStyle.Projectionist, Key = "Projectionist", Label = "THE PROJECTIONIST", Blurb = "Norman Polk, the projectionist: a projector for a head, and its lens throws light where you look.",
            Hands = HandKind.Lifelike, Sleeves = SleeveKind.Rolled, ArmBulk = 1.12f, Drips = 0.8f,
            Remap = Inky("norman", "sleeve", "normanSleeve", "sleeveRolled", "normanSleeveRolled"),
            GameModel = "gameplay/characters/ai_projectionist"
        },
        new()
        {
            Id = BodyStyle.BruteBoris, Key = "BruteBoris", Label = "BRUTE BORIS", Blurb = "Boris after the ink: huge arms, a torn vest and one stitched eye.",
            Hands = HandKind.Lifelike, Sleeves = SleeveKind.Bare, ArmBulk = 1.45f, Drips = 0.5f, Remap = Inky("brute"),
            ArchiveModel = "BruteBoris"
        },
        new()
        {
            Id = BodyStyle.Searcher, Key = "Searcher", Label = "A SEARCHER", Blurb = "One of the lost ones from the ink: dripping goo and two pale eyes.",
            Hands = HandKind.Lifelike, Sleeves = SleeveKind.Bare, Drips = 1.2f, Remap = Inky("goo"),
            GameModel = "gameplay/characters/ai_searcher"
        },
        new()
        {
            Id = BodyStyle.Joey, Key = "Joey", Label = "JOEY DREW", Blurb = "The boss: a three-piece suit, the studio's pin, a signet ring and a moustache.",
            Hands = HandKind.Lifelike, Sleeves = SleeveKind.Full, Remap = Map("sleeve", "joeySleeve", "sleeveRolled", "joeySleeve")
        },
        new()
        {
            Id = BodyStyle.Wally, Key = "Wally", Label = "WALLY FRANKS", Blurb = "The janitor: cap, overalls, work gloves and a ring of keys that jangles. He's outta here.",
            Hands = HandKind.Lifelike, Sleeves = SleeveKind.Rolled,
            Remap = Map("lifeSkin", "workGlove", "lifePalm", "workGlovePalm", "lifeNail", "workGlove", "sleeve", "wallySleeve", "sleeveRolled", "wallySleeveRolled")
        },
        new()
        {
            Id = BodyStyle.BeastBendy, Key = "BeastBendy", Label = "BEAST BENDY", Blurb = "The Ink Demon at his most monstrous: a huge chest, great horns, ink pouring off him.",
            Hands = HandKind.Lifelike, Sleeves = SleeveKind.Bare, ArmBulk = 1.55f, Drips = 2.2f, Remap = Inky("demon")
            // (the game's own model is ready to wear, GameModel = "gameplay/characters/beastbendy_ai"; it stays drawn
            // until StrikeZoneVR has finished the game: he's a late-chapter character)
        },
    };

    public static StyleDef Of(BodyStyle id)
    {
        foreach (var s in All)
            if (s.Id == id)
                return s;
        return All[0];
    }

    public static StyleDef ByKey(string key)
    {
        foreach (var s in All)
            if (s.Key == key)
                return s;
        return All[0];
    }

    /// <summary>The style chosen in the settings.</summary>
    public static StyleDef Current => ByKey(ModConfig.BodyStyle.Value);

    public static string MapKey(StyleDef style, string key) => style.Remap.TryGetValue(key, out var k) ? k : key;

    public static string[] Keys
    {
        get
        {
            var k = new string[All.Length];
            for (var i = 0; i < All.Length; i++) k[i] = All[i].Key;
            return k;
        }
    }

    public static string[] Labels
    {
        get
        {
            var k = new string[All.Length];
            for (var i = 0; i < All.Length; i++) k[i] = All[i].Label;
            return k;
        }
    }

    public static readonly (WatchKind Kind, string Key, string Label)[] Watches =
    {
        (WatchKind.Tank, "Tank", "TANK (HENRY'S)"),
        (WatchKind.Bendy, "Bendy", "BENDY CHARACTER"),
        (WatchKind.Studio, "Studio", "STUDIO GOLD"),
        (WatchKind.Gauge, "Gauge", "INK GAUGE"),
        (WatchKind.Angel, "Angel", "ANGEL HALO"),
        (WatchKind.Demon, "Demon", "INK DEMON"),
        (WatchKind.None, "None", "NONE")
    };

    public static WatchKind CurrentWatch
    {
        get
        {
            foreach (var w in Watches)
                if (w.Key == ModConfig.Watch.Value)
                    return w.Kind;
            return WatchKind.Tank;
        }
    }
}
