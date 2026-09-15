using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.SceneManagement;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>One pilot in the room, and their laps this race as their GMS has told everyone.</summary>
internal sealed class RoomPilot
{
    public RoomPilot(int actor)
    {
        Actor = actor;
    }

    public int Actor { get; }
    public string Nick = "";

    /// <summary>The id the game signed them in with, which the JMT site knows pilots by.</summary>
    public string? UserId;

    public bool IsLocal;
    public bool InRoom = true;

    /// <summary>GS: 2 while they're taking part in the race rather than watching it. Null until the game says.</summary>
    public int? Taking;

    public int? RaceState;

    /// <summary>The laps of their current run as GMS last listed them: what's already counted.</summary>
    public List<int>? Run;

    public int Laps;
    public int? BestMs;
    public int? LastMs;
    public int Resets;
    public DateTime? SpawnAt;
    public DateTime? LastLapAt;

    public void NewRace()
    {
        Laps = 0;
        BestMs = null;
        LastMs = null;
        Resets = 0;
        SpawnAt = null;
        LastLapAt = null;
        RaceState = null;
    }
}

/// <summary>
/// Everyone's laps in the room, for the race panel, read the way <see cref="LocalRun"/> reads
/// the local pilot's: every pilot's GMS reaches every client, so this works in any room,
/// the moment each lap is flown. It only knows laps flown since this client joined; in a
/// JMT room the site fills in the rest.
///
/// A new race is the mod's: a room joined, another track, the race started (SGSO), or any
/// pilot's race state going back from finished. The laps a pilot's GMS already lists when
/// a new race starts are kept as where their run stands, not counted, so a track change
/// doesn't carry the last track's laps over; the laps they list when this client joins are
/// counted, since they were flown in the race being joined.
/// </summary>
internal sealed class RoomLaps : IInRoomCallbacks, IMatchmakingCallbacks
{
    private const string TrackKey = "T";
    private const string StartKey = "SGSO";
    private static readonly TimeSpan RespawnDebounce = TimeSpan.FromSeconds(1);

    private readonly GmsReader _gms;
    private readonly Dictionary<int, RoomPilot> _pilots = new();
    private string? _track;
    private bool _warned;

    public RoomLaps(GmsReader gms)
    {
        _gms = gms;
    }

    public bool Installed { get; private set; }

    public bool InRoom { get; private set; }

    public IEnumerable<RoomPilot> Pilots => _pilots.Values;

    /// <summary>A lap, a reset, someone joining or leaving.</summary>
    public event Action? Changed;

    public event Action? RaceStarted;

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
            Installed = true;
            // Installed from inside a room, after a plugin reload: take it as joining.
            if (PhotonNetwork.InRoom)
                OnJoinedRoom();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Couldn't listen to the game's multiplayer events, so the race panel stays empty: {ex.Message}");
        }
    }

    // ── Photon ──────────────────────────────────────────────────────────────

    public void OnJoinedRoom() => Safely(() =>
    {
        _pilots.Clear();
        InRoom = true;
        var props = PhotonNetwork.CurrentRoom?.CustomProperties;
        _track = props != null && props.TryGetValue(TrackKey, out var track) ? track as string : null;
        NewRace();
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (player != null)
                Read(Pilot(player), player.CustomProperties, joining: true);
        }
        Changed?.Invoke();
    });

    public void OnLeftRoom() => Safely(() =>
    {
        _pilots.Clear();
        InRoom = false;
        _track = null;
        NewRace();
    });

    public void OnPlayerEnteredRoom(Player newPlayer) => Safely(() =>
    {
        if (newPlayer == null)
            return;
        var pilot = Pilot(newPlayer);
        pilot.InRoom = true;
        Read(pilot, newPlayer.CustomProperties, joining: true);
        Changed?.Invoke();
    });

    public void OnPlayerLeftRoom(Player otherPlayer) => Safely(() =>
    {
        // Their times were set; they stay on the race, marked gone.
        if (otherPlayer != null && _pilots.TryGetValue(otherPlayer.ActorNumber, out var pilot))
        {
            pilot.InRoom = false;
            Changed?.Invoke();
        }
    });

    public void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged) => Safely(() =>
    {
        if (propertiesThatChanged == null)
            return;
        if (propertiesThatChanged.TryGetValue(TrackKey, out var value) && value is string track && track != _track)
        {
            var changed = _track != null;
            _track = track;
            if (changed)
                NewRace();
        }
        if (PhotonProps.TryInt(propertiesThatChanged, StartKey, out var start) && start == 1)
            NewRace();
    });

    public void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps) => Safely(() =>
    {
        if (targetPlayer == null || changedProps == null)
            return;
        Read(Pilot(targetPlayer), changedProps, joining: false);
        Changed?.Invoke();
    });

    public void OnMasterClientSwitched(Player newMasterClient) { }
    public void OnFriendListUpdate(List<FriendInfo> friendList) { }
    public void OnCreatedRoom() { }
    public void OnCreateRoomFailed(short returnCode, string message) { }
    public void OnJoinRoomFailed(short returnCode, string message) { }
    public void OnJoinRandomFailed(short returnCode, string message) { }

    // ── The race ────────────────────────────────────────────────────────────

    private RoomPilot Pilot(Player player)
    {
        if (!_pilots.TryGetValue(player.ActorNumber, out var pilot))
            _pilots[player.ActorNumber] = pilot = new RoomPilot(player.ActorNumber);
        pilot.Nick = player.NickName ?? "";
        pilot.UserId = string.IsNullOrEmpty(player.UserId) ? null : player.UserId;
        pilot.IsLocal = player.IsLocal;
        return pilot;
    }

    private void Read(RoomPilot pilot, PhotonHashtable? props, bool joining)
    {
        if (props == null)
            return;
        var hasRaceState = PhotonProps.TryInt(props, "RS", out var raceState);
        if (!joining && hasRaceState && pilot.RaceState >= 5 && raceState <= 3)
            NewRace(); // the race was restarted
        if (PhotonProps.TryInt(props, "GS", out var taking))
            pilot.Taking = taking;

        if (props.TryGetValue("GMS", out var gms) && gms != null)
        {
            var laps = _gms.LapList(gms, out var hasList);
            if (laps != null)
                Merge(pilot, laps);
            else if (!hasList && !joining)
                Respawned(pilot);
        }

        if (hasRaceState)
            pilot.RaceState = raceState;
    }

    /// <summary>The laps GMS lists that aren't counted yet. A list that doesn't carry on from the last is a run started again.</summary>
    private static void Merge(RoomPilot pilot, List<int> incoming)
    {
        var run = pilot.Run ?? new List<int>();
        if (LocalRun.IsPrefix(run, incoming))
            return; // nothing new
        if (!LocalRun.IsPrefix(incoming, run))
            run = new List<int>();
        pilot.Run = incoming;
        for (var i = run.Count; i < incoming.Count; i++)
        {
            var ms = incoming[i];
            pilot.Laps++;
            pilot.LastMs = ms;
            pilot.LastLapAt = DateTime.UtcNow;
            if (pilot.BestMs == null || ms < pilot.BestMs)
                pilot.BestMs = ms;
        }
    }

    private static void Respawned(RoomPilot pilot)
    {
        var now = DateTime.UtcNow;
        var lastSpawn = pilot.SpawnAt;
        pilot.SpawnAt = now;
        pilot.Run = null;
        if (lastSpawn != null && now - lastSpawn.Value < RespawnDebounce)
            return;
        if (lastSpawn == null && pilot.LastLapAt == null)
            return; // arriving
        pilot.Resets++;
    }

    private void NewRace()
    {
        var gone = new List<int>();
        foreach (var entry in _pilots)
        {
            if (entry.Value.InRoom)
                entry.Value.NewRace();
            else
                gone.Add(entry.Key);
        }
        foreach (var actor in gone)
            _pilots.Remove(actor);
        RaceStarted?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Photon calls every listener in a loop, the game's own among them: nothing thrown here may reach it.</summary>
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
            Plugin.Log.LogWarning($"HUD: couldn't follow the room's race: {ex}");
        }
    }
}
