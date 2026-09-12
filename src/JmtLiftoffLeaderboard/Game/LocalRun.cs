using System;
using System.Collections.Generic;
using System.Reflection;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.SceneManagement;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// The local pilot's laps and resets, seen the way JmtLiftoffMod sees every pilot's in a
/// JMT room, so the HUD counts them as the room does.
///
/// Liftoff publishes each pilot's run on their Photon player as the custom property
/// <c>GMS</c>: an object whose <c>float[]</c> holds the laps of the current run, in
/// seconds. It grows by one as each lap ends, and the game republishes it with no lap list
/// the moment it respawns the drone. Photon calls <c>OnPlayerPropertiesUpdate</c> for the
/// local player's own properties as well as everyone else's, so this needs nothing from
/// the room. GMS's type and members are obfuscated, so the lap list is found by its type.
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
    // The mod ignores a lap list longer than its lap cap. A pilot's own run can be long.
    private const int MaxLapsInRun = 1000;

    private readonly Dictionary<Type, List<Func<object, object?>>> _members = new();
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

    public bool Installed { get; private set; }

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
        var hasRaceState = TryInt(props, "RS", out var raceState);
        if (hasRaceState && _raceState >= 5 && raceState <= 3)
            NewRace(); // the race was restarted

        if (props.TryGetValue("GMS", out var gms) && gms != null)
        {
            if (!_seen)
            {
                _seen = true;
                Plugin.Log.LogInfo("HUD: reading your laps and resets from the game.");
            }
            var laps = LapList(gms, out var hasList);
            if (laps != null)
                Merge(laps);
            else if (!hasList)
                Respawned();
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
            Lap?.Invoke(incoming[i]);
    }

    private void Respawned()
    {
        var now = DateTime.UtcNow;
        var lastSpawn = _spawnAt;
        var lastLap = _lastLapAt;
        _spawnAt = now;
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

    private void NewRace()
    {
        _run = null;
        _spawnAt = null;
        _lastLapAt = null;
        _raceState = null;
        _complete = false;
        RaceStarted?.Invoke();
    }

    // ── Reading GMS ─────────────────────────────────────────────────────────

    /// <summary>
    /// The laps of the current run, in milliseconds, or null when GMS carries none. A list
    /// the mod would refuse (empty, or a time under a second or over ten minutes) is no
    /// laps, but it still counts as a list: only a GMS with no lap list at all is a respawn.
    /// </summary>
    private List<int>? LapList(object gms, out bool hasList)
    {
        float[]? best = null;
        var sawList = false;
        Visit(gms, 0);
        hasList = sawList;
        if (best == null)
            return null;
        var laps = new List<int>(best.Length);
        foreach (var seconds in best)
            laps.Add((int)Math.Round(seconds * 1000d));
        return laps;

        void Visit(object? value, int depth)
        {
            if (value is float[] list)
            {
                sawList = true;
                if (list.Length > 0 && list.Length <= MaxLapsInRun && AllLapTimes(list) && (best == null || list.Length > best.Length))
                    best = list;
                return;
            }
            if (value == null || depth >= 2 || value is string || value is Array || value is UnityEngine.Object)
                return;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum)
                return;
            foreach (var read in Members(type))
            {
                object? child;
                try
                {
                    child = read(value);
                }
                catch
                {
                    continue;
                }
                Visit(child, depth + 1);
            }
        }
    }

    private static bool AllLapTimes(float[] list)
    {
        foreach (var seconds in list)
        {
            if (seconds <= 1f || seconds > 600f)
                return false;
        }
        return true;
    }

    /// <summary>A type's public fields and readable properties, as the mod's logging walks them.</summary>
    private List<Func<object, object?>> Members(Type type)
    {
        if (_members.TryGetValue(type, out var known))
            return known;
        var members = new List<Func<object, object?>>();
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            members.Add(target => field.GetValue(target));
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0)
                members.Add(target => property.GetValue(target, null));
        }
        _members[type] = members;
        return members;
    }

    /// <summary>Whether <paramref name="list"/> starts with <paramref name="prefix"/>, as the mod's IsPrefix.</summary>
    private static bool IsPrefix(IReadOnlyList<int> list, IReadOnlyList<int> prefix)
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

    private static bool TryInt(PhotonHashtable props, string key, out int value)
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

    /// <summary>
    /// Photon and the scene loader call every listener in a loop, and the game's own are
    /// among them: nothing thrown here may reach either.
    /// </summary>
    private void Safely(Action action)
    {
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
