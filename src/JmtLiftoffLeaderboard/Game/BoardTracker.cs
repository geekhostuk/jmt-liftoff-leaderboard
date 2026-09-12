using System;
using System.Collections.Generic;
using System.Linq;
using JmtLiftoffLeaderboard.Site;
using UnityEngine;

namespace JmtLiftoffLeaderboard.Game;

internal enum BoardState
{
    /// <summary>No course loaded, as far as the game says.</summary>
    NoTrack,
    Looking,
    NoBoard,
    Unreachable,
    Ready,
}

/// <summary>
/// The JMT board for the course being flown, around the local pilot, with tonight's laps
/// on it.
///
/// The site is asked for the places around the pilot's (<c>around</c>), a few more above
/// than are shown, so a lap flown here that takes a place or several can be placed
/// straight away, before the room's panel has sent it. The site is asked again every
/// minute, and soon after a counted new best, so the board settles on the site's word.
/// </summary>
internal sealed class BoardTracker
{
    private const float RefreshSeconds = 60f;
    private const float AfterBestSeconds = 20f;
    private const float TrackLookSeconds = 10f;
    private const float NoTrackLookSeconds = 2f;
    private const int RecentCount = 12;
    private const int SpareAbove = 5;

    private readonly SiteClient _site;
    private readonly LocalPilot _me;
    private readonly RoomWatch _room;
    private readonly Settings _settings;

    private TrackRef? _track;
    private string? _trackKey;
    private float _nextTrackLook;
    private float _nextFetch;
    private int _request;
    private bool _fetching;
    private Leaderboard? _board;
    private readonly List<int> _recent = new();
    private int? _bestTonight;
    private int? _lastLap;
    private BoardView? _view;

    public BoardTracker(SiteClient site, LocalPilot me, RoomWatch room, LocalRun run, Settings settings)
    {
        _site = site;
        _me = me;
        _room = room;
        _settings = settings;
        run.Lap += OnLap;
        // A new race may be a new course. The level takes a moment to load.
        run.RaceStarted += () => _nextTrackLook = Mathf.Min(_nextTrackLook, Time.realtimeSinceStartup + 1.5f);
        room.Changed += Invalidate;
        settings.BoardAbove.SettingChanged += (_, _) => Refetch();
        settings.BoardBelow.SettingChanged += (_, _) => Refetch();
    }

    public BoardState State { get; private set; } = BoardState.NoTrack;

    /// <summary>The course being flown, as the game names it.</summary>
    public string TrackName => _track?.Name ?? "";

    public event Action? Changed;
    public event Action<BoardNews>? News;

    public BoardView? View => State == BoardState.Ready && _board != null ? _view ??= Build(_board) : null;

    /// <summary>Called every frame. <paramref name="flying"/> when a course is loaded and the pilot could be on it.</summary>
    public void Tick(bool flying)
    {
        if (!flying)
            return;
        var now = Time.realtimeSinceStartup;
        if (now >= _nextTrackLook)
            LookAtTrack(now);
        if (_track != null && !_fetching && now >= _nextFetch)
            Fetch(now);
    }

    private void LookAtTrack(float now)
    {
        var track = CurrentTrack.Read();
        _nextTrackLook = now + (track == null ? NoTrackLookSeconds : TrackLookSeconds);
        var key = track == null ? null : $"{track.BoardId}|{track.TrackName}|{string.Join("/", track.Environments)}";
        if (key == _trackKey)
            return;

        _trackKey = key;
        _track = track;
        _board = null;
        _recent.Clear();
        _bestTonight = null;
        _lastLap = null;
        _request++;
        _fetching = false;
        _nextFetch = 0;
        State = track == null ? BoardState.NoTrack : BoardState.Looking;
        Invalidate();
    }

    private void Refetch()
    {
        _nextFetch = 0;
        Invalidate();
    }

    // ── Asking the site ─────────────────────────────────────────────────────

    private void Fetch(float now)
    {
        var track = _track!;
        var request = ++_request;
        _fetching = true;
        _nextFetch = now + RefreshSeconds;
        _me.Resolve(me =>
        {
            if (request != _request)
                return;
            if (track.BoardId is { } id)
                Load(track, id, me, request);
            else
                FindByName(track, me, request);
        });
    }

    private void Load(TrackRef track, long id, string? me, int request)
    {
        _site.LeaderboardAround(id, me, _settings.BoardAbove.Value + SpareAbove, _settings.BoardBelow.Value + 1, result =>
        {
            if (request != _request)
                return;
            if (!result.Ok)
            {
                Done(_board != null ? State : result.NotFound ? BoardState.NoBoard : BoardState.Unreachable);
                return;
            }
            var board = result.Value!;
            // A Workshop course the site couldn't match keeps its laps on a board named after it.
            if (board.PilotCount == 0 && id > 0 && !track.LookedUpByName)
            {
                FindByName(track, me, request);
                return;
            }
            _board = board;
            Done(board.PilotCount == 0 ? BoardState.NoBoard : BoardState.Ready);
        });
    }

    /// <summary>One of Liftoff's own courses, or a Workshop one the site had no id for: its board is under its name.</summary>
    private void FindByName(TrackRef track, string? me, int request)
    {
        track.LookedUpByName = true;
        var name = track.TrackName.Trim();
        if (name.Length == 0)
        {
            Done(BoardState.NoBoard);
            return;
        }
        _site.Boards(name, null, "title", 50, 0, result =>
        {
            if (request != _request)
                return;
            if (!result.Ok)
            {
                Done(_board != null ? State : BoardState.Unreachable);
                return;
            }
            var named = result.Value!.Items
                .Where(b => b.PublishedFileId < 0 && string.Equals(b.Title.Trim(), name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var match = named.FirstOrDefault(b => track.InEnvironment(b.Environment, b.EnvironmentDisplay)) ?? named.FirstOrDefault();
            if (match == null)
            {
                Done(BoardState.NoBoard);
                return;
            }
            track.BoardId = match.PublishedFileId;
            Load(track, match.PublishedFileId, me, request);
        });
    }

    private void Done(BoardState state)
    {
        _fetching = false;
        State = state;
        Invalidate();
    }

    // ── Laps flown here ─────────────────────────────────────────────────────

    private void OnLap(int lapMs)
    {
        if (_track == null)
            return;
        _lastLap = lapMs;
        _recent.Add(lapMs);
        if (_recent.Count > RecentCount)
            _recent.RemoveAt(0);

        var before = View?.Me;
        var improved = _bestTonight == null || lapMs < _bestTonight;
        if (improved)
            _bestTonight = lapMs;
        _view = null;
        var after = View?.Me;

        if (improved && after != null && (before == null || after.LapMs < before.LapMs))
        {
            News?.Invoke(new BoardNews(lapMs, after.Position, before == null ? 0 : before.Position - after.Position, before == null));
            if (_room.Counting == Counting.Live)
                _nextFetch = Mathf.Min(_nextFetch, Time.realtimeSinceStartup + AfterBestSeconds);
        }
        Changed?.Invoke();
    }

    private void Invalidate()
    {
        _view = null;
        Changed?.Invoke();
    }

    // ── The view ────────────────────────────────────────────────────────────

    private BoardView Build(Leaderboard board)
    {
        var view = BoardMath.Arrange(board, _me.Known, _bestTonight,
            _settings.BoardAbove.Value, _settings.BoardBelow.Value, _room.IsHere);
        view.LastLapMs = _lastLap;
        view.BestTonightMs = _bestTonight;
        view.Recent = _recent.ToArray();
        return view;
    }
}
