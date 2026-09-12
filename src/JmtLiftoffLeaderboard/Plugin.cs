using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using JmtLiftoffLeaderboard.Game;
using JmtLiftoffLeaderboard.Site;
using JmtLiftoffLeaderboard.Ui;
using UnityEngine;

namespace JmtLiftoffLeaderboard;

/// <summary>
/// JMT Liftoff Leaderboard: the JMT boards and pilot profiles, inside Liftoff's menus.
///
/// Standalone: it reads the JMT site's public endpoints directly and needs neither
/// JmtLiftoffMod nor Liftoff Control, though it runs happily beside both.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "uk.co.geekhost.jmtliftoffleaderboard";
    public const string PluginName = "JMT Liftoff Leaderboard";
    public const string PluginVersion = BuildInfo.Version;
    public const string BuildMarker = BuildInfo.Marker;

    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Log { get; private set; } = null!;

    private Overlay? _overlay;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        // Some games destroy stray root objects on scene changes; this one must outlive them all.
        DontDestroyOnLoad(gameObject);
        gameObject.hideFlags = HideFlags.HideAndDontSave;

        var settings = new Settings(Config);
        var site = new SiteClient(this, settings, Logger);
        var me = new LocalPilot(site, settings);
        _overlay = new Overlay(this, site, me);
        new MenuHooks(this, settings, _overlay).Install(new Harmony(PluginGuid));

        Logger.LogInfo($"{PluginName} {BuildMarker} loaded; reading {settings.BaseUrl}");
    }

    private void Update() => _overlay?.Tick();
}
