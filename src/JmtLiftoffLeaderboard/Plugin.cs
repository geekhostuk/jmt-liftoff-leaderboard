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
/// on screen while the pilot flies: their Consistency Rating, the course's board, their
/// delta against their best lap, and the race in the room.
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
    private LocalRun? _run;

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
        var harmony = new Harmony(PluginGuid);
        new MenuHooks(this, settings, _overlay).Install(harmony);
        // The local pilot's race checkpoints, for the delta bar.
        GateHook.Install(harmony);

        // Photon is the game's to set up, so the room is followed from the main menu on.
        var run = _run = new LocalRun();
        var laps = new RoomLaps(run.Reader);
        void Install()
        {
            run.Install();
            laps.Install();
        }
        SceneManager.sceneLoaded += (scene, _) =>
        {
            if (scene.name == MenuHooks.MainMenuScene)
                Install();
        };
        if (SceneManager.GetActiveScene().name == MenuHooks.MainMenuScene)
            Install();
        var room = new RoomWatch(site, me, run);
        var board = new BoardTracker(site, me, room, run, settings);
        var delta = new DeltaTracker(run, settings, site, board.BoardIdFor, () => me.Known);
        // The gate times of the pilot's own laps go to the room, for a JMT room's host to send on.
        new SplitPublisher(settings, delta);
        _hud = new RaceHud(this, settings, _overlay, run, room,
            new CrTracker(site, me, run, room),
            board,
            delta,
            new RaceTracker(site, me, room, laps));

        FrameProbe.Install();
        Logger.LogInfo($"{PluginName} {BuildMarker} loaded; reading {settings.BaseUrl}");
    }

    // Splits are written in the background; the last lap's shouldn't be lost to quitting.
    private void OnApplicationQuit() => SplitStore.Flush();

    private void Update()
    {
        FrameProbe.Tick(counting: _hud?.InFlight == true);
        using var probe = FrameProbe.Measure(FrameProbe.Part.Frame);
        _overlay?.Tick();
        _run?.Tick();
        try
        {
            _hud?.Tick();
        }
        catch (Exception ex)
        {
            // A HUD that throws every frame would flood the log and cost frames mid-flight.
            _hud = null;
            Logger.LogError($"The flying HUD failed and is off until the game restarts: {ex}");
        }
    }
}
