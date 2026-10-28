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

    /// <summary>Or InkVR's own model of this style (an .inkmodel in the plugin's models folder: Henry, built in Blender).</summary>
    public string ModelFile;

    /// <summary>The model's own head is left off for InkVR's drawn one (the concept Bendy statue's head is a cutaway).</summary>
    public bool DrawnHead;

    /// <summary>How much bigger than Henry-sized the drawn head is drawn on that model (a cartoon's big head).</summary>
    public float DrawnHeadScale = 1f;

    /// <summary>A projector's lens light from the eyes, where you look (the Projectionist).</summary>
    public bool LensLight;
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

    // Only Henry (1.2), his whole body built in Blender: the other characters (the game's own models, the drawn Bendy,
    // Boris, Alice, Joey, Wally and Beast Bendy, and Toon Henry) were taken out. A saved choice of one comes back as Henry.
    public static readonly StyleDef[] All =
    {
        new() { Id = BodyStyle.Lifelike, Key = "Lifelike", Label = "HENRY", Blurb = "Henry Stein: waistcoat, tie, rolled sleeves and his tank watch.", Hands = HandKind.Lifelike, Sleeves = SleeveKind.Rolled, WeddingBand = true, ModelFile = "henry.inkmodel" },
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
