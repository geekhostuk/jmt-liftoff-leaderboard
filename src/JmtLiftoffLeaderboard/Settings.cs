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

public enum HudCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// What every HUD panel has in the .cfg: when it shows, where it sits, and how big and how
/// solid it's drawn. Edit mode sets all of it with the mouse.
/// </summary>
internal sealed class HudPanelSettings
{
    /// <summary>How long a panel can stay up after a reset. "Always" is <see cref="HudMode.Always"/>.</summary>
    public static readonly int[] StayOptions = { 5, 10, 20, 30 };

    public HudPanelSettings(ConfigFile config, string section, string what, HudCorner corner)
    {
        Show = config.Bind(section, "Show", HudMode.BetweenAttempts,
            $"{what} while you fly. BetweenAttempts shows it at the start and after each reset for ShowSeconds, then fades it out; Always keeps it up; Off hides it.");
        ShowSeconds = config.Bind(section, "ShowSeconds", 10,
            new ConfigDescription("With Show at BetweenAttempts, how many seconds it stays up after you arrive at the start or reset.", new AcceptableValueList<int>(StayOptions)));
        Corner = config.Bind(section, "Corner", corner,
            "The corner of the screen it keeps to. Set by dragging it in edit mode (Hud.EditKey).");
        OffsetX = config.Bind(section, "OffsetX", 24f,
            "How far it sits in from that corner's side, in pixels at 1080p. Set by dragging it in edit mode.");
        OffsetY = config.Bind(section, "OffsetY", 24f,
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
    }

    /// <summary>The site's address with no trailing slash, ready for a path to be added.</summary>
    public string BaseUrl => (SiteUrl.Value ?? "").Trim().TrimEnd('/');
}
