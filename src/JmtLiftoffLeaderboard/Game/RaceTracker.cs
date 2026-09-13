using System;
using System.Collections.Generic;
using System.Linq;
using JmtLiftoffLeaderboard.Site;
using UnityEngine;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>The race in the room, as the race panel draws it.</summary>
internal sealed class RaceView
{
    public List<RaceRow> Rows = new();

    /// <summary>The JMT site's side of the race is in: its laps from before this client joined, and the failed attempts.</summary>
    public bool FromSite;

    public int? MyPosition;
    public int Timed;
}

/// <summary>
/// The race in the room for the race panel: <see cref="RoomLaps"/> for every lap the moment
/// it's flown, and in a JMT room the site's timing screen for that room, asked every five
/// seconds, for the rest.
///
/// The site hears about each race a little after the room: its panel sends laps every
/// fifteen seconds or so, and a new race only once its first laps arrive. So when the room
/// starts a new race, the site's rows are left out until its race changes too, rather than
/// showing the last race's times on this one.
///
/// To put the site's rows beside the room's pilots, the id each pilot's game signed in with
/// is looked up on the site, as the plugin looks up the local pilot's, once per pilot and
/// only in a JMT room; a pilot the site doesn't know is matched by name.
/// </summary>
internal sealed class RaceTracker
{
    private const float PollSeconds = 5f;

    private readonly SiteClient _site;
    private readonly LocalPilot _me;
    private readonly RoomWatch _room;
    private readonly RoomLaps _laps;

    // The game's id for each pilot, and who the site says they are: null when it doesn't know them.
    private readonly Dictionary<string, string?> _ids = new();
    private string? _asking;
    private LiveBoard? _board;
    private string? _panel;
    private bool _waiting = true;
    private string? _staleRace;
    private float _nextPoll;
    private bool _polling;
    private int _request;
    private RaceView _view = new();
    private int? _position;

    public RaceTracker(SiteClient site, LocalPilot me, RoomWatch room, RoomLaps laps)
    {
        _site = site;
        _me = me;
        _room = room;
        _laps = laps;
        laps.Changed += Rebuild;
        laps.RaceStarted += () =>
        {
            _waiting = true;
            _staleRace = _board?.RaceId;
            _position = null;
            Rebuild();
        };
        room.Changed += () =>
        {
            if (room.PanelId != _panel)
            {
                _panel = room.PanelId;
                _board = null;
                _request++;
                _polling = false;
                _nextPoll = 0;
            }
            Rebuild();
        };
    }

    public bool InRoom => _laps.InRoom;

    public RaceView View => _view;

    public event Action? Changed;

    /// <summary>The local pilot's place in the race changed: to, and from.</summary>
    public event Action<int, int>? Moved;

    /// <summary>Called every frame. <paramref name="flying"/> when a course is loaded and the pilot could be on it.</summary>
    public void Tick(bool flying)
    {
        if (!flying || !_laps.InRoom || _room.Counting != Counting.Live)
            return;
        LookUpNext();
        var now = Time.realtimeSinceStartup;
        if (!_polling && now >= _nextPoll && _room.PanelId is { } panel)
            Poll(panel, now);
    }

    private void Poll(string panel, float now)
    {
        var request = ++_request;
        _polling = true;
        _nextPoll = now + PollSeconds;
        _site.LiveRoom(panel, result =>
        {
            if (request != _request)
                return;
            _polling = false;
            if (!result.Ok)
            {
                // A blip keeps the last answer; a room the site no longer has drops it.
                if (result.NotFound)
                    _board = null;
                Rebuild();
                return;
            }
            var board = result.Value!;
            if (_waiting && board.RaceId != null && board.RaceId != _staleRace)
                _waiting = false;
            _board = board;
            Rebuild();
        });
    }

    /// <summary>Asks the site who one more pilot is. One at a time: a room is a handful of pilots, and none of this is urgent.</summary>
    private void LookUpNext()
    {
        if (_asking != null)
            return;
        foreach (var pilot in _laps.Pilots)
        {
            if (pilot.IsLocal || pilot.UserId == null || _ids.ContainsKey(pilot.UserId))
                continue;
            var id = _asking = pilot.UserId;
            _site.PilotByGameId(id, result =>
            {
                _asking = null;
                // Not found, or the site can't be reached: matched by name instead, for the rest of the session.
                _ids[id] = result.Ok ? result.Value!.Pilot.PublicId : null;
                Rebuild();
            });
            return;
        }
    }

    private void Rebuild()
    {
        var me = _me.Known;
        var room = _laps.Pilots.Select(pilot => new RacePilotIn
        {
            Name = pilot.Nick,
            PublicId = pilot.IsLocal ? me : pilot.UserId != null && _ids.TryGetValue(pilot.UserId, out var id) ? id : null,
            IsLocal = pilot.IsLocal,
            InRoom = pilot.InRoom,
            Taking = pilot.Taking == null ? null : pilot.Taking == 2,
            Laps = pilot.Laps,
            BestMs = pilot.BestMs,
            LastMs = pilot.LastMs,
        });

        var current = _board != null && _board.Online && _board.RaceId != null && !_waiting && _room.Counting == Counting.Live;
        var site = current
            ? _board!.Rows.Select(row => new RaceSiteIn
            {
                PublicId = row.Pilot.PublicId,
                Name = row.Pilot.Name,
                InRoom = row.InRoom,
                Laps = row.Laps,
                BestMs = row.BestLapMs,
                LastMs = row.LastLapMs,
                Failed = row.FailedAttempts,
            })
            : null;

        var rows = RaceMath.Merge(room, site, me);
        var mine = rows.FirstOrDefault(r => r.IsMe)?.Position;
        _view = new RaceView
        {
            Rows = rows,
            FromSite = site != null,
            MyPosition = mine,
            Timed = rows.Count(r => r.Position != null),
        };

        var before = _position;
        _position = mine;
        if (mine != null && before != null && mine != before)
            Moved?.Invoke(mine.Value, before.Value);
        Changed?.Invoke();
    }
}
