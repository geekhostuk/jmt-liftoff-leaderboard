using BepInEx.Configuration;

namespace JmtLiftoffLeaderboard;

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
    }

    /// <summary>The site's address with no trailing slash, ready for a path to be added.</summary>
    public string BaseUrl => (SiteUrl.Value ?? "").Trim().TrimEnd('/');
}
