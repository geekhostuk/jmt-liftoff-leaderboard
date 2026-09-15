using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.SceneManagement;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// The local pilot's laps and resets, seen the way JmtLiftoffMod sees every pilot's in a
/// JMT room, so the HUD counts them as the room does; and the race checkpoints they pass,
/// for the delta bar.
///
/// Liftoff publishes each pilot's run on their Photon player as the custom property
/// <c>GMS</c>: an object whose <c>float[]</c> holds the laps of the current run, in
/// seconds. It grows by one as each lap ends, and the game republishes it with no lap list
/// the moment it respawns the drone. Photon calls <c>OnPlayerPropertiesUpdate</c> for the
/// local player's own properties as well as everyone else's, so this needs nothing from
/// the room. <see cref="GmsReader"/> finds the lap list inside GMS's obfuscated type.
///
/// Checkpoints come from the game's own tracking through <see cref="GateHook"/>, and from
/// GMS in the race modes that publish them there; the two can report the same passage, and
/// it's raised once.
///
/// The rules are the mod's (JmtLiftoffMod.cs: <c>OnGmsRespawn</c> and
/// <c>MergeGmsLapSeries</c>), and must stay the same: a respawn within a second of the
/// last is the same one, the first spawn in a race is arriving rather than resetting, and
/// the attempt a reset abandons ran from the last lap if the pilot flew on from one, else
/// from the last respawn.
/// </summary>
internal sealed class LocalRun : IInRoomCallbacks, IMatchmakingCallbacks
{
    private const string TrackKey = "T";
    private static readonly TimeSpan RespawnDebounce = TimeSpan.FromSeconds(1);
    // How many gates a race logs, to check in the log that they arrive. Temporary.
    private const int GatesLogged = 60;

    private readonly GmsReader _gms = new();
    private bool _seen;
    private bool _warned;

    // The laps of the current run as GMS last listed them; when the drone last respawned
    // and last finished a lap; the pilot's race state (RS), 5 and up once they've finished.
    private List<int>? _run;
    private DateTime? _spawnAt;
    private DateTime? _lastLapAt;
    private int? _raceState;
    private bool _complete;
    private string? _track;

    // The last checkpoint reported: GMS is republished for more than gates, a respawn can
    // carry the one before it, and the game's tracker reports the same passage too.
    private (string Id, int Lap, float Seconds)? _lastGate;
    private int _gatesLogged;

    /// <summary>A lap finished, with its time in milliseconds.</summary>
    public event Action<int>? Lap;

    /// <summary>
    /// The drone was reset part way through the run: how long the abandoned attempt had
    /// run, in milliseconds, or null for a reset only noticed at the next lap.
    /// </summary>
    public event Action<int?>? Reset;

    /// <summary>The drone is at the start: arriving, or reset.</summary>
    public event Action? Spawned;

    /// <summary>A new race: a room joined or left, another track, or the race restarted.</summary>
    public event Action? RaceStarted;

    /// <summary>The pilot passed a race checkpoint.</summary>
    public event Action<GateInfo>? Gate;

    public bool Installed { get; private set; }

    /// <summary>The lap before the one <see cref="Lap"/> is raised for, in the same run; null for the run's first.</summary>
    public int? PreviousLapMs { get; private set; }

    /// <summary>Reads GMS for anyone else who follows the room, so its members are only looked up once.</summary>
    public GmsReader Reader => _gms;

    /// <summary>Starts listening. Called once the main menu is up, when the game has set Photon up itself.</summary>
    public void Install()
    {
        if (Installed)
            return;
        try
        {
            PhotonNetwork.AddCallbackTarget(this);
            SceneManager.sceneLoaded += (_, mode) =>
            {
                if (mode == LoadSceneMode.Single)
                    Safely(NewRace);
            };
            GateHook.Passed += (id, lap, seconds) => Safely(() => Passed((id, lap, seconds), "tracker"));
            Installed = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Couldn't listen to the game's multiplayer events, so the HUD won't count laps: {ex.Message}");
        }
    }

    // ── Photon ──────────────────────────────────────────────────────────────

    public void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
    {
        if (targetPlayer == null || !targetPlayer.IsLocal || changedProps == null)
            return;
        Safely(() => Read(changedProps));
    }

    public void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged) => Safely(() =>
    {
        if (propertiesThatChanged == null || !propertiesThatChanged.TryGetValue(TrackKey, out var value)
            || value is not string track || track == _track)
            return;
        var changed = _track != null;
        _track = track;
        if (changed)
            NewRace();
    });

    public void OnJoinedRoom() => Safely(() =>
    {
        var props = PhotonNetwork.CurrentRoom?.CustomProperties;
        _track = props != null && props.TryGetValue(TrackKey, out var track) ? track as string : null;
        NewRace();
    });

    public void OnLeftRoom() => Safely(() =>
    {
        _track = null;
        NewRace();
    });

    public void OnPlayerEnteredRoom(Player newPlayer) { }
    public void OnPlayerLeftRoom(Player otherPlayer) { }
    public void OnMasterClientSwitched(Player newMasterClient) { }
    public void OnFriendListUpdate(List<FriendInfo> friendList) { }
    public void OnCreatedRoom() { }
    public void OnCreateRoomFailed(short returnCode, string message) { }
    public void OnJoinRoomFailed(short returnCode, string message) { }
    public void OnJoinRandomFailed(short returnCode, string message) { }

    // ── The run ─────────────────────────────────────────────────────────────

    private void Read(PhotonHashtable props)
    {
        var hasRaceState = PhotonProps.TryInt(props, "RS", out var raceState);
        if (hasRaceState && _raceState >= 5 && raceState <= 3)
            NewRace(); // the race was restarted

        if (props.TryGetValue("GMS", out var gms) && gms != null)
        {
            if (!_seen)
            {
                _seen = true;
                Plugin.Log.LogInfo("HUD: reading your laps and resets from the game.");
            }
            var laps = _gms.LapList(gms, out var hasList);
            if (laps != null)
                Merge(laps);
            else if (!hasList)
                Respawned();

            // What a respawn carries is the gate before it, and is only remembered.
            if (_gms.Checkpoint(gms) is { } gate)
            {
                if (laps == null && !hasList)
                    _lastGate = gate;
                else
                    Passed(gate, "GMS");
            }
        }

        if (hasRaceState)
        {
            var racing = _raceState < 5;
            _raceState = raceState;
            // Finished: the mod records nothing more from them this race, so neither does this.
            if (raceState >= 5 && racing)
                _complete = true;
        }
    }

    private void Merge(List<int> incoming)
    {
        if (_complete)
            return;

        var run = _run ?? new List<int>();
        if (IsPrefix(run, incoming))
            return; // nothing new
        if (!IsPrefix(incoming, run))
        {
            // The run started again without the respawn that announces it reaching us.
            Reset?.Invoke(null);
            run = new List<int>();
        }

        _run = incoming;
        _lastLapAt = DateTime.UtcNow;
        for (var i = run.Count; i < incoming.Count; i++)
        {
            if (_gatesLogged < GatesLogged)
                Plugin.Log.LogInfo($"HUD: lap {i + 1} {incoming[i]} ms");
            PreviousLapMs = i > 0 ? incoming[i - 1] : null;
            Lap?.Invoke(incoming[i]);
        }
    }

    private void Respawned()
    {
        var now = DateTime.UtcNow;
        var lastSpawn = _spawnAt;
        var lastLap = _lastLapAt;
        _spawnAt = now;
        // The new run's first checkpoint can be the very one the last run started with.
        _lastGate = null;
        Spawned?.Invoke();

        if (lastSpawn != null && now - lastSpawn.Value < RespawnDebounce)
            return;
        if (lastSpawn == null && lastLap == null)
            return; // arriving: a new track, a room joined
        if (_complete)
            return;

        var fromLap = lastLap != null && (lastSpawn == null || lastLap.Value > lastSpawn.Value);
        var attemptMs = (int)(now - (fromLap ? lastLap!.Value : lastSpawn!.Value)).TotalMilliseconds;
        _run = null;
        Reset?.Invoke(attemptMs);
    }

    /// <summary>
    /// A checkpoint not reported before. The lap timer's value tells one pass of a gate
    /// from the next, so passing the same gate on the same lap after a reset is new.
    /// </summary>
    private void Passed((string Id, int Lap, float Seconds) gate, string source)
    {
        if (_lastGate == gate)
            return;
        _lastGate = gate;
        if (_gatesLogged < GatesLogged)
        {
            _gatesLogged++;
            Plugin.Log.LogInfo($"HUD: gate id={gate.Id} lap={gate.Lap} t={gate.Seconds:0.000} ({source})");
        }
        // The HUD's clock, which it draws the bar by.
        Gate?.Invoke(new GateInfo(gate.Id, gate.Lap, gate.Seconds, UnityEngine.Time.realtimeSinceStartup));
    }

    private void NewRace()
    {
        _run = null;
        _spawnAt = null;
        _lastLapAt = null;
        _raceState = null;
        _complete = false;
        _gatesLogged = 0;
        RaceStarted?.Invoke();
    }

    /// <summary>Whether <paramref name="list"/> starts with <paramref name="prefix"/>, as the mod's IsPrefix.</summary>
    internal static bool IsPrefix(IReadOnlyList<int> list, IReadOnlyList<int> prefix)
    {
        if (prefix.Count > list.Count)
            return false;
        for (var i = 0; i < prefix.Count; i++)
        {
            if (list[i] != prefix[i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Photon, the scene loader and the game's checkpoint tracking call every listener in a
    /// loop, and the game's own are among them: nothing thrown here may reach any of them.
    /// </summary>
    private void Safely(Action action)
    {
        using var probe = FrameProbe.Measure(FrameProbe.Part.Room);
        try
        {
            action();
        }
        catch (Exception ex)
        {
            if (_warned)
                return;
            _warned = true;
            Plugin.Log.LogWarning($"HUD: couldn't follow your run: {ex}");
        }
    }
}

/// <summary>Reading the game's Photon custom properties, whose integers arrive as whatever width the game sent.</summary>
internal static class PhotonProps
{
    public static bool TryInt(PhotonHashtable props, string key, out int value)
    {
        value = 0;
        if (!props.TryGetValue(key, out var raw) || raw == null)
            return false;
        switch (raw)
        {
            case int i: value = i; return true;
            case byte b: value = b; return true;
            case short s: value = s; return true;
            case long l: value = (int)l; return true;
            case Enum e: value = Convert.ToInt32(e); return true;
            default: return false;
        }
    }
}
