using System;
using Photon.Pun;
using UnityEngine;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>How an abandoned attempt was timed, the way the room's host times it.</summary>
internal readonly struct Attempt
{
    public Attempt(int ms, string from)
    {
        Ms = ms;
        From = from;
    }

    /// <summary>How long it ran, from <see cref="From"/> to the reset.</summary>
    public int Ms { get; }

    /// <summary>
    /// "lap" when flying on from a finished lap; "start" from the moment the drone left the
    /// start; "respawn" from the respawn itself, when the drone couldn't be followed.
    /// </summary>
    public string From { get; }
}

/// <summary>A drone followed from the start after a respawn: when it left, or null when it never did.</summary>
internal readonly struct StartSeen
{
    public StartSeen(DateTime? leftAt)
    {
        LeftAt = leftAt;
    }

    public DateTime? LeftAt { get; }
}

/// <summary>
/// Whether the pilot's own drone has left the start since it last respawned, and when: the
/// rule JmtLiftoffMod 1.5.0 applies to every pilot in the room, so the CR worked out here
/// matches the site's.
///
/// After a respawn the drone waits at the spawn point until it is armed and the countdown
/// runs. An attempt starts when it gets further than <see cref="StartMetres"/> from there. The
/// drone is the PhotonView whose id is the player's <c>DID</c> property, and it has to be seen
/// at the start after a respawn before leaving it counts.
/// </summary>
internal sealed class StartWatch
{
    private const float StartMetres = 5f;
    private const float PollSeconds = 0.1f;

    private enum Stage
    {
        None,
        Respawning,
        AtStart,
        Left,
    }

    private Stage _stage;
    private DateTime? _leftAt;
    private float _nextPoll;
    private bool _failed;

    /// <summary>
    /// The drone has just respawned. Returns when the attempt it ended left the start (null
    /// inside when it never did), or null when the drone wasn't followed from the start.
    /// </summary>
    public StartSeen? Respawned()
    {
        StartSeen? began = _stage is Stage.AtStart or Stage.Left ? new StartSeen(_leftAt) : null;
        _stage = Stage.Respawning;
        _leftAt = null;
        return began;
    }

    public void Forget()
    {
        _stage = Stage.None;
        _leftAt = null;
    }

    /// <summary>Called every frame; looks at the drone ten times a second while it's at the start.</summary>
    public void Poll()
    {
        if (_failed || _stage is Stage.None or Stage.Left || !PhotonNetwork.InRoom)
            return;
        var now = Time.realtimeSinceStartup;
        if (now < _nextPoll)
            return;
        _nextPoll = now + PollSeconds;
        try
        {
            if (CurrentTrack.SpawnPoint() is not { } spawn || Drone() is not { } at)
                return;
            var away = Vector3.Distance(at, spawn);
            if (_stage == Stage.Respawning)
            {
                if (away <= StartMetres)
                    _stage = Stage.AtStart;
            }
            else if (away > StartMetres)
            {
                _stage = Stage.Left;
                _leftAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _failed = true;
            Plugin.Log.LogWarning($"HUD: couldn't follow your drone from the start, so resets are timed from the respawn: {ex.Message}");
        }
    }

    private static Vector3? Drone()
    {
        var me = PhotonNetwork.LocalPlayer;
        if (me?.CustomProperties == null || !me.CustomProperties.TryGetValue("DID", out var did) || did is not int id || id <= 0)
            return null;
        var view = PhotonView.Find(id);
        return view != null ? view.transform.position : null;
    }
}
