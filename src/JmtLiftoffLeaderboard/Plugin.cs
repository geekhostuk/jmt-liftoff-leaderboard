using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using JmtLiftoffLeaderboard.Game;
using JmtLiftoffLeaderboard.Site;
using JmtLiftoffLeaderboard.Ui;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JmtLiftoffLeaderboard;

/// <summary>
/// JMT Liftoff Leaderboard: the JMT boards and pilot profiles, inside Liftoff's menus, and
/// the pilot's Consistency Rating and the course's board on screen while they fly.
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
    private RaceHud? _hud;

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

        // Photon is the game's to set up, so the run is followed from the main menu on.
        var run = new LocalRun();
        SceneManager.sceneLoaded += (scene, _) =>
        {
            if (scene.name == MenuHooks.MainMenuScene)
                run.Install();
        };
        if (SceneManager.GetActiveScene().name == MenuHooks.MainMenuScene)
            run.Install();
        var room = new RoomWatch(site, me, run);
        _hud = new RaceHud(this, settings, _overlay, run, room,
            new CrTracker(site, me, run, room),
            new BoardTracker(site, me, room, run, settings));

        Logger.LogInfo($"{PluginName} {BuildMarker} loaded; reading {settings.BaseUrl}");
    }

    private void Update()
    {
        _overlay?.Tick();
        try
        {
            _hud?.Tick();
        }
        catch (Exception ex)
        {
            // A HUD that throws every frame would flood the log and cost frames mid-flight.
            _hud = null;
            Logger.LogError($"The Consistency Rating HUD failed and is off until the game restarts: {ex}");
        }
    }
}
