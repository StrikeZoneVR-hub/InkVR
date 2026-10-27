using BepInEx.Configuration;

namespace InkVR;

/// <summary>InkVR settings (BepInEx/config/com.strikezonevr.inkvr.cfg), shared with the in-game VR menu and the launcher.</summary>
public static class ModConfig
{
    // VR
    public static ConfigEntry<bool> VREnabled;
    public static ConfigEntry<float> WorldScale;
    public static ConfigEntry<bool> AutoWorldScale;
    public static ConfigEntry<float> MeasuredEyeHeight;
    public static ConfigEntry<bool> VolumetricLightVR;
    public static ConfigEntry<float> HeightOffset;

    // Comfort / controls
    public static ConfigEntry<bool> SnapTurn;
    public static ConfigEntry<float> SnapTurnAngle;
    public static ConfigEntry<float> SmoothTurnSpeed;
    public static ConfigEntry<bool> LeftHanded;
    public static ConfigEntry<bool> InteractWithHand;
    public static ConfigEntry<bool> PhysicalMelee;
    public static ConfigEntry<bool> AimGunsWithHand;
    public static ConfigEntry<bool> PhysicalInteractions;
    public static ConfigEntry<bool> PhysicalDoors;
    public static ConfigEntry<bool> SeatedMode;
    public static ConfigEntry<bool> GravityPull;
    public static ConfigEntry<bool> WeaponWheel;
    public static ConfigEntry<bool> InventoryButton;
    public static ConfigEntry<bool> PhysicalThrow;
    public static ConfigEntry<bool> TwoHandedGuns;
    public static ConfigEntry<bool> Holster;
    public static ConfigEntry<bool> Backpack;
    public static ConfigEntry<bool> ReduceFlashes;
    public static ConfigEntry<bool> CloseUpNotes;

    // Body
    public static ConfigEntry<string> BodyMode;
    public static ConfigEntry<string> BodyStyle;
    public static ConfigEntry<string> Watch;

    /// <summary>A full body is shown.</summary>
    public static bool FullBody => BodyMode.Value == "Full";

    /// <summary>Arms are shown (full body, or arms and hands).</summary>
    public static bool ArmsShown => BodyMode.Value == "Full" || BodyMode.Value == "Arms";

    /// <summary>Any of Henry is shown.</summary>
    public static bool HandsShown => BodyMode.Value != "None";

    public static ConfigEntry<float> HeldItemPitch;
    public static ConfigEntry<bool> InkOnHands;
    public static ConfigEntry<bool> PlayerShadows;
    public static ConfigEntry<bool> NaturalGrips;
    public static ConfigEntry<bool> SolidWeapons;
    public static ConfigEntry<float> InkFadeMinutes;

    // Graphics
    public static ConfigEntry<bool> VRDisableMotionBlur;
    public static ConfigEntry<bool> VRDisableDepthOfField;
    public static ConfigEntry<bool> VRAODownsample;
    public static ConfigEntry<float> RenderScale;
    public static ConfigEntry<bool> DynamicResolution;
    public static ConfigEntry<float> MinRenderScale;
    public static ConfigEntry<bool> LaserPointer;
    public static ConfigEntry<bool> ExploreTheatre;

    // Debug
    public static ConfigEntry<bool> PerfLog;

    public static ConfigFile File { get; private set; }

    /// <summary>
    /// Whether this session starts in VR. The InkVR Launcher sets INKVR_MODE=vr|flat for the game it starts,
    /// so "Play flat" never rewrites the saved setting; launching from Steam uses [VR] Enabled.
    /// </summary>
    public static bool StartInVR
    {
        get
        {
            var mode = System.Environment.GetEnvironmentVariable("INKVR_MODE");
            if (string.Equals(mode, "vr", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(mode, "flat", System.StringComparison.OrdinalIgnoreCase)) return false;
            return VREnabled.Value;
        }
    }

    public static void Bind(ConfigFile cfg)
    {
        File = cfg;
        VREnabled = cfg.Bind("VR", "Enabled", true, "Start the game in VR (OpenXR). Off = flat-screen with the mod idle.");
        WorldScale = cfg.Bind("VR", "WorldScale", 2.95f, "Game units per real-world metre. Henry's eye is 5.03 units above the floor (= 1.70 m at 2.95); doors are 7.4 units (2.5 m).");
        AutoWorldScale = cfg.Bind("VR", "AutoWorldScale", true, "Size the world to you: your standing eye height (measured as each level starts) becomes Henry's eye height. Off = use WorldScale.");
        MeasuredEyeHeight = cfg.Bind("VR", "MeasuredEyeHeight", 0f, "Your standing eye height in metres, last measured by AutoWorldScale (0 = not yet).");
        HeightOffset = cfg.Bind("VR", "HeightOffset", 0f, "Metres added to your tracked head height (seated play / height calibration).");

        SeatedMode = cfg.Bind("Comfort", "SeatedMode", false, "Play sitting down: your view is lifted to standing height (measured as you sit), the world keeps its size, and gravity pull brings things out of reach to your hand.");
        SnapTurn = cfg.Bind("Comfort", "SnapTurn", true, "Turn in steps with the right stick (off = smooth turning).");
        SnapTurnAngle = cfg.Bind("Comfort", "SnapTurnAngle", 45f, "Degrees per snap turn.");
        SmoothTurnSpeed = cfg.Bind("Comfort", "SmoothTurnSpeed", 120f, "Degrees per second for smooth turning.");
        LeftHanded = cfg.Bind("Controls", "LeftHanded", false, "Swap the controller roles (move on the right stick, attack with the left trigger).");
        InteractWithHand = cfg.Bind("Controls", "InteractWithHand", true, "Point at things with either hand, or touch them, to interact (grip or X); with neither hand on or pointing at anything, what you look at. Off = only what you look at, like the flat game.");
        PhysicalMelee = cfg.Bind("Controls", "PhysicalMelee", true, "Swing your arm to hit with melee weapons (the axe, the pipe...). The attack button still swings too.");
        AimGunsWithHand = cfg.Bind("Controls", "AimGunsWithHand", true, "Guns fire where your hand points (off = where you look, like the flat game).");
        PhysicalDoors = cfg.Bind("Controls", "PhysicalDoors", true, "Doors swing by hand: push them, grip and pull them, fling them shut; locked doors rattle (off = they open as the game opens them).");
        PhysicalInteractions = cfg.Bind("Controls", "PhysicalInteractions", true, "By hand: grip a valve and turn it (it turns with your hand), a lever and pull it (it swings), a lid or drawer and lift or pull it; take Bacon Soup, pull its lid off with your other hand and tip it to your mouth; grip an item to take it; press buttons, tape recorders and keys with a fingertip (off = grip just presses them). X always presses.");
        PhysicalThrow = cfg.Bind("Controls", "PhysicalThrow", true, "Throwing by arm: Ch4's soup cans, Ch1's darts and Ch4's ball toss (hold the trigger, swing, let go), and anything you're carrying: Bacon Soup off the shelf, gears, batteries and the like (let go mid-swing). Off = the trigger throws straight away.");
        TwoHandedGuns = cfg.Bind("Controls", "TwoHandedGuns", true, "Two hands: grip the front of a gun to aim it with both hands, or the axe's handle to swing it with both.");
        Holster = cfg.Bind("Controls", "Holster", true, "Reach over your shoulder and grip to put your weapon away; do it again to draw it.");
        Backpack = cfg.Bind("Controls", "Backpack", false, "Optional, off unless you turn it on. A satchel over your left shoulder: what you pick up stays in your hand until you put it away (let go of it behind your left shoulder); reach over your left shoulder and grip to bring the satchel round and take things out; hold one to what it's for and let go to use it, or throw it (it comes back). Off: what you pick up goes into your pockets when you let go of it, as before.");
        GravityPull = cfg.Bind("Controls", "GravityPull", true, "Point a hand at an item, grip, then flick your hand back (or keep holding the grip) and it flies into your hand, trailing ink.");
        WeaponWheel = cfg.Bind("Controls", "WeaponWheel", true, "Click the right stick for a wheel at your hand: draw your weapon, put it away, use the Seeing Tool or see your inventory. Flick the stick to a choice (or point and pull the trigger); B closes it.");
        InventoryButton = cfg.Bind("Controls", "InventoryButton", true, "Y shows your current objective and what you've collected, on a card by your hand.");
        ReduceFlashes = cfg.Bind("Comfort", "ReduceFlashes", false, "Calm the theatre's projector flicker and film scratches.");
        CloseUpNotes = cfg.Bind("Comfort", "CloseUpNotes", true, "A tape's note (who's speaking and what they say) comes up close in front of you to read, instead of small in the corner of the far HUD.");

        BodyMode = cfg.Bind("Body", "Mode", "Full", new ConfigDescription(
            "How much of Henry you see: Full (full body), Arms (arms and hands), Hands (hands only), None.",
            new AcceptableValueList<string>("Full", "Arms", "Hands", "None")));
        BodyStyle = cfg.Bind("Body", "Style", "Lifelike", new ConfigDescription(
            "Who you are, head to toe (the head shows in the theatre's mirror and in your shadow): Lifelike (Henry: real hands, waistcoat and tie), Toon (Henry as a cartoon: braces, big shoes, white gloves), Bendy, Boris, Alice, InkDemon, Sammy, Projectionist, BruteBoris, Searcher, Joey, Wally or BeastBendy.",
            new AcceptableValueList<string>(Body.Styles.Keys)));
        Watch = cfg.Bind("Body", "Watch", "Tank", new ConfigDescription(
            "The watch on your left wrist, keeping your real time: Tank (Henry's), Bendy (his character watch), Studio (gold, the studio's), Gauge (an ink-machine gauge whose vial fills chapter by chapter), Angel (Alice's, a halo floating over it), Demon (dripping), or None.",
            new AcceptableValueList<string>("Tank", "Bendy", "Studio", "Gauge", "Angel", "Demon", "None")));
        HeldItemPitch = cfg.Bind("Body", "HeldItemPitch", 0f, "Extra pitch (degrees) applied to held weapons and items.");
        InkOnHands = cfg.Bind("Body", "InkOnHands", true, "Ink from spills, splats, pools, pours, drips and ink bursts gets onto Henry (hands, sleeves, and with the full body his clothes and shoes), and inky shoes leave footprints.");
        PlayerShadows = cfg.Bind("Body", "PlayerShadows", true, "Henry casts a real-time shadow that moves with you: his body, arms and hands, and his head (which you never see).");
        SolidWeapons = cfg.Bind("Body", "SolidWeapons", true, "The weapon in your hand is part of the world: drawn with it (your fingers close over its handle, walls hide it), stopped by walls and floors instead of sinking into them (your hand stops with it), and its swing knocks loose objects about. Off: drawn over everything, as the game does on a monitor.");
        NaturalGrips = cfg.Bind("Body", "NaturalGrips", true, "Weapons and things you carry sit in your hand by their handle, your fingers closed round them, and your other hand goes onto the axe's handle or the gun's barrel when you take hold of it (off = they follow the controller as before).");
        InkFadeMinutes = cfg.Bind("Body", "InkFadeMinutes", 3f, "How long fresh ink takes to dry and fade back to the chapter's usual stains.");

        VRDisableMotionBlur = cfg.Bind("Graphics", "DisableMotionBlurInVR", true, "Motion blur smears the whole view when you turn your head.");
        VRDisableDepthOfField = cfg.Bind("Graphics", "DisableDepthOfFieldInVR", true, "Your eyes focus naturally in VR; screen-space depth of field blurs what you look at.");
        VRAODownsample = cfg.Bind("Graphics", "HalfResAmbientOcclusionInVR", true, "Render ambient occlusion at half resolution (saves ~1.5 ms per frame in VR).");
        RenderScale = cfg.Bind("Graphics", "RenderScale", 1.0f, "Headset render resolution multiplier (1.0 = the runtime's recommended resolution). With DynamicResolution this is the most it will use.");
        DynamicResolution = cfg.Bind("Graphics", "DynamicResolution", true, "Lower the resolution a little when the graphics card can't keep up with the headset's refresh rate, and raise it again when it can (smooth frame rate first).");
        MinRenderScale = cfg.Bind("Graphics", "MinRenderScale", 0.7f, "The lowest DynamicResolution may go.");
        VolumetricLightVR = cfg.Bind("Graphics", "VolumetricLightInVR", true, "The game's light beams through the air, rendered per eye in VR (off saves GPU time).");
        LaserPointer = cfg.Bind("Controls", "LaserPointer", true, "A laser from your hand to point and click in menus: the main menu, the pause menu, Options and VR SETTINGS (in the theatre the move stick can walk you about, so the laser picks). The thumbstick and A still work when you aren't walking.");
        ExploreTheatre = cfg.Bind("Controls", "ExploreTheatre", true, "In the menu theatre, click the move stick to walk around it (the stage, the lobby, the stairs, the balcony; there are things to find, and a mirror in the lobby to change your style and watch), and click again to use the menu. With the laser on, pushing the stick while you aren't pointing at the screen walks too.");

        PerfLog = cfg.Bind("Debug", "LogPerformance", true, "Write VR frame timing to the BepInEx log every 5 seconds (helps tuning).");
    }
}
