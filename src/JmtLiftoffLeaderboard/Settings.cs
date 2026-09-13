using System;
using BepInEx.Configuration;
using UnityEngine;

namespace JmtLiftoffLeaderboard;

/// <summary>When a HUD panel is on screen while flying.</summary>
public enum HudMode
{
    Off,
    BetweenAttempts,
    Always,
}

/// <summary>Where a HUD panel keeps to. The centres are added last, so a .cfg written before them still reads.</summary>
public enum HudCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    TopCenter,
    BottomCenter,
}

/// <summary>What the delta bar measures the lap being flown against.</summary>
public enum DeltaCompare
{
    /// <summary>The pilot's best lap on this course ever, kept on this computer.</summary>
    BestEver,
    /// <summary>Their best lap since the game started.</summary>
    Tonight,
}

/// <summary>What the race panel's last column shows.</summary>
public enum RaceColumn
{
    Gap,
    Last,
}

/// <summary>
/// What every HUD panel has in the .cfg: when it shows, where it sits, and how big and how
/// solid it's drawn. Edit mode sets all of it with the mouse.
/// </summary>
internal sealed class HudPanelSettings
{
    /// <summary>How long a panel can stay up after a reset. "Always" is <see cref="HudMode.Always"/>.</summary>
    public static readonly int[] StayOptions = { 5, 10, 20, 30 };

    public HudPanelSettings(ConfigFile config, string section, string what, HudCorner corner,
        HudMode show = HudMode.BetweenAttempts, float offsetX = 24f, float offsetY = 24f)
    {
        Show = config.Bind(section, "Show", show,
            $"{what} while you fly. BetweenAttempts shows it at the start and after each reset for ShowSeconds, then fades it out; Always keeps it up; Off hides it.");
        ShowSeconds = config.Bind(section, "ShowSeconds", 10,
            new ConfigDescription("With Show at BetweenAttempts, how many seconds it stays up after you arrive at the start or reset.", new AcceptableValueList<int>(StayOptions)));
        Corner = config.Bind(section, "Corner", corner,
            "The corner of the screen it keeps to, or the middle of its top or bottom edge. Set by dragging it in edit mode (Hud.EditKey).");
        OffsetX = config.Bind(section, "OffsetX", offsetX,
            "How far it sits in from that corner's side, in pixels at 1080p. Not used in the middle of an edge. Set by dragging it in edit mode.");
        OffsetY = config.Bind(section, "OffsetY", offsetY,
            "How far it sits in from that corner's top or bottom, in pixels at 1080p. Set by dragging it in edit mode.");
        Scale = config.Bind(section, "Scale", 0.8f,
            new ConfigDescription("How big it's drawn: 1 is full size. Scroll over it in edit mode to change it.", new AcceptableValueRange<float>(0.4f, 1.6f)));
        Opacity = config.Bind(section, "Opacity", 0.9f,
            new ConfigDescription("How solid it's drawn, from 0.2 (faint) to 1 (solid).", new AcceptableValueRange<float>(0.2f, 1f)));

        Show.SettingChanged += (_, _) => Changed?.Invoke();
        ShowSeconds.SettingChanged += (_, _) => Changed?.Invoke();
        Corner.SettingChanged += (_, _) => Changed?.Invoke();
        OffsetX.SettingChanged += (_, _) => Changed?.Invoke();
        OffsetY.SettingChanged += (_, _) => Changed?.Invoke();
        Scale.SettingChanged += (_, _) => Changed?.Invoke();
        Opacity.SettingChanged += (_, _) => Changed?.Invoke();
    }

    public ConfigEntry<HudMode> Show { get; }
    public ConfigEntry<int> ShowSeconds { get; }
    public ConfigEntry<HudCorner> Corner { get; }
    public ConfigEntry<float> OffsetX { get; }
    public ConfigEntry<float> OffsetY { get; }
    public ConfigEntry<float> Scale { get; }
    public ConfigEntry<float> Opacity { get; }

    /// <summary>Any of these changed.</summary>
    public event Action? Changed;

    public void Reset()
    {
        Show.Value = (HudMode)Show.DefaultValue;
        ShowSeconds.Value = (int)ShowSeconds.DefaultValue;
        Corner.Value = (HudCorner)Corner.DefaultValue;
        OffsetX.Value = (float)OffsetX.DefaultValue;
        OffsetY.Value = (float)OffsetY.DefaultValue;
        Scale.Value = (float)Scale.DefaultValue;
        Opacity.Value = (float)Opacity.DefaultValue;
    }
}

/// <summary>
/// The plugin's .cfg. Everything has a working default: a pilot who drops the DLL in
/// and changes nothing gets the public JMT site and every menu hook.
/// </summary>
internal sealed class Settings
{
    /// <summary>The delta bar's range each way, in seconds: a delta this big fills the bar.</summary>
    public static readonly float[] DeltaRanges = { 0.5f, 1f, 2f };

    /// <summary>How many sectors the delta bar splits a lap into; 0 hides them.</summary>
    public static readonly int[] DeltaSectorCounts = { 0, 3, 4, 5, 6 };

    public static readonly int[] RaceRowCounts = { 5, 8, 12 };

    public ConfigEntry<string> SiteUrl { get; }
    public ConfigEntry<string> PilotPublicId { get; }
    public ConfigEntry<string> LastGameUserId { get; }
    public ConfigEntry<bool> ReplaceMainMenuLeaderboard { get; }
    public ConfigEntry<bool> ReplacePauseMenuLeaderboard { get; }
    public ConfigEntry<bool> ShowProfileButton { get; }

    /// <summary>The Consistency Rating panel. Its keys live in [Hud], beside the two keys every panel shares.</summary>
    public HudPanelSettings Consistency { get; }
    public ConfigEntry<KeyboardShortcut> HudPinKey { get; }
    public ConfigEntry<KeyboardShortcut> HudEditKey { get; }

    /// <summary>The track board panel, in [HudBoard].</summary>
    public HudPanelSettings Board { get; }
    public ConfigEntry<int> BoardAbove { get; }
    public ConfigEntry<int> BoardBelow { get; }
    public ConfigEntry<bool> BoardRuler { get; }

    /// <summary>The delta bar, in [HudDelta].</summary>
    public HudPanelSettings Delta { get; }
    public ConfigEntry<DeltaCompare> DeltaCompare { get; }
    public ConfigEntry<bool> DeltaBar { get; }
    public ConfigEntry<int> DeltaSectors { get; }
    public ConfigEntry<bool> DeltaLapLine { get; }
    public ConfigEntry<float> DeltaRange { get; }

    /// <summary>The race in the room, in [HudRace].</summary>
    public HudPanelSettings Race { get; }
    public ConfigEntry<int> RaceRows { get; }
    public ConfigEntry<RaceColumn> RaceColumn { get; }
    public ConfigEntry<bool> RaceFailed { get; }

    public Settings(ConfigFile config)
    {
        SiteUrl = config.Bind("Site", "Url", "https://test.geekhost.uk",
            "The JMT site to read leaderboards and pilot profiles from.");

        PilotPublicId = config.Bind("Pilot", "PublicId", "",
            "Your JMT pilot id. Leave it empty to be found automatically; pressing \"This is me\" on a board fills it in.");
        LastGameUserId = config.Bind("Pilot", "LastGameUserId", "",
            "Filled in automatically: the id the game last signed you in with, so you can be found before the game has gone online. No need to edit it.");

        ReplaceMainMenuLeaderboard = config.Bind("Menu", "ReplaceMainMenuLeaderboard", true,
            "The main menu's Leaderboard button opens the JMT board. Liftoff's own leaderboard stays one click away inside it.");
        ReplacePauseMenuLeaderboard = config.Bind("Menu", "ReplacePauseMenuLeaderboard", true,
            "The pause menu's leaderboard button opens the JMT board for the course being flown.");
        ShowProfileButton = config.Bind("Menu", "ShowProfileButton", true,
            "Add a JMT Profile button to the main menu.");

        Consistency = new HudPanelSettings(config, "Hud", "Your Consistency Rating", HudCorner.TopRight);
        HudPinKey = config.Bind("Hud", "PinKey", new KeyboardShortcut(KeyCode.F8),
            "Keeps the HUD panels on screen while you fly. Press it again to let them fade.");
        HudEditKey = config.Bind("Hud", "EditKey", new KeyboardShortcut(KeyCode.F8, KeyCode.LeftControl),
            "Move, resize, fade and set up the HUD panels with the mouse, while flying or from the pause menu. Press it again, or Done, to finish.");

        Board = new HudPanelSettings(config, "HudBoard", "The course's JMT board around your place", HudCorner.TopLeft);
        BoardAbove = config.Bind("HudBoard", "Above", 3,
            new ConfigDescription("Places shown above yours: the times to aim for.", new AcceptableValueList<int>(1, 2, 3, 4, 5)));
        BoardBelow = config.Bind("HudBoard", "Below", 0,
            new ConfigDescription("Places shown below yours: who's chasing you.", new AcceptableValueList<int>(0, 1, 2)));
        BoardRuler = config.Bind("HudBoard", "Ruler", true,
            "A line of lap times under the board: the places above yours as ticks and your laps tonight as dots, so you can see how close they're landing.");

        Delta = new HudPanelSettings(config, "HudDelta", "How far ahead or behind your best lap you are", HudCorner.TopCenter,
            HudMode.Always, 0f, 24f);
        DeltaCompare = config.Bind("HudDelta", "Compare", JmtLiftoffLeaderboard.DeltaCompare.BestEver,
            "What the lap you're flying is measured against: BestEver is your best lap on this course, kept on this computer; Tonight is your best since the game started.");
        DeltaBar = config.Bind("HudDelta", "Bar", true,
            "A bar that fills left of centre when you're ahead and right when you're behind.");
        DeltaSectors = config.Bind("HudDelta", "Sectors", 4,
            new ConfigDescription("How many sectors to split the lap into, each coloured purple for your best ever, green for quicker than the lap you're measured against, yellow for slower. 0 hides them.",
                new AcceptableValueList<int>(DeltaSectorCounts)));
        DeltaLapLine = config.Bind("HudDelta", "LapLine", true,
            "A line under the bar: the lap time so far, the lap you're measured against, and your best possible lap from your best sectors.");
        DeltaRange = config.Bind("HudDelta", "Range", 1f,
            new ConfigDescription("How many seconds ahead or behind fill the bar.", new AcceptableValueList<float>(DeltaRanges)));

        Race = new HudPanelSettings(config, "HudRace", "The race in the room you're in", HudCorner.BottomLeft);
        RaceRows = config.Bind("HudRace", "Rows", 8,
            new ConfigDescription("Most pilots shown. With more in the race, the top places and yours are shown.", new AcceptableValueList<int>(RaceRowCounts)));
        RaceColumn = config.Bind("HudRace", "Column", JmtLiftoffLeaderboard.RaceColumn.Gap,
            "The last column: Gap to the quickest lap of the race, or each pilot's Last lap.");
        RaceFailed = config.Bind("HudRace", "Failed", true,
            "In a JMT room, how many attempts each pilot has given up part way this race.");
    }

    /// <summary>The site's address with no trailing slash, ready for a path to be added.</summary>
    public string BaseUrl => (SiteUrl.Value ?? "").Trim().TrimEnd('/');
}
