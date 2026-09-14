using System;
using System.Collections.Generic;
using System.Linq;
using JmtLiftoffLeaderboard.Site;
using UnityEngine;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// Sends the gate times of the pilot's own laps to the JMT site, once they've linked their
/// account. Only laps flown in a JMT room: the site matches each to the lap the room
/// reported, and a lap flown anywhere else has nothing to match. Only whole laps measured
/// from the line, through every gate, that the site would take.
///
/// They go through <see cref="SplitOutbox"/>, so nothing is lost to the network, and a
/// lap whose pilot the site can't take yet (their game id isn't claimed, or no room has
/// reported them) is asked about again later rather than dropped.
/// </summary>
internal sealed class SplitUploader
{
    private const int BatchSize = 50;
    private const float EverySeconds = 10f;
    private const float BackoffMinSeconds = 5f;
    private const float BackoffMaxSeconds = 300f;
    private static readonly TimeSpan NotYet = TimeSpan.FromMinutes(30);

    private readonly SiteClient _site;
    private readonly Settings _settings;
    private readonly SiteLink _link;
    private readonly RoomWatch _room;
    private readonly SplitOutbox _outbox;
    // Laps flown before it was known whether the room counts, and the room they were flown in.
    private readonly List<(string Room, SplitUpload Split)> _held = new();

    private float _next;
    private float _backoff = BackoffMinSeconds;
    private bool _sending;
    // The token the site last refused. Nothing goes until the pilot links again.
    private string _refused = "";
    private int _sent;

    public SplitUploader(SiteClient site, Settings settings, SiteLink link, RoomWatch room, DeltaTracker delta, SplitOutbox outbox)
    {
        _site = site;
        _settings = settings;
        _link = link;
        _room = room;
        _outbox = outbox;
        delta.LapFinished += OnLap;
        link.Changed += () => _next = 0;
    }

    private void OnLap(FlownLap lap, long? boardId, int? prevLapMs)
    {
        if (!_settings.SplitsUpload.Value || !_link.Linked)
            return;
        if (lap.LapMs is not { } lapMs || !lap.Measured || !SplitRules.ValidForUpload(lap.Gates, lap.Times, lapMs))
            return;
        var key = GameIdentity.PhotonUserId();
        if (string.IsNullOrEmpty(key))
            return;

        var split = new SplitUpload
        {
            ClientRef = Guid.NewGuid().ToString("N"),
            PilotKey = key!,
            BoardId = boardId,
            PanelId = _room.PanelId,
            Gates = new List<string>(lap.Gates),
            Times = new List<int>(lap.Times),
            LapMs = lapMs,
            PrevLapMs = prevLapMs,
            FlownAt = DateTimeOffset.UtcNow,
        };
        switch (_room.Counting)
        {
            case Counting.Live:
                _outbox.Add(split);
                break;
            case Counting.Unknown:
                _held.Add((_room.Room ?? "", split));
                break;
        }
    }

    /// <summary>Called every frame, flying or not: the outbox drains from the menus too.</summary>
    public void Tick(float now)
    {
        Release();
        if (_sending || now < _next || _outbox.Count == 0 || !_link.Linked)
            return;
        var token = _link.Token;
        if (token == _refused)
            return;
        var due = _outbox.Due(DateTimeOffset.UtcNow, BatchSize);
        if (due.Count == 0)
        {
            _next = now + EverySeconds;
            return;
        }
        _sending = true;
        _site.UploadSplits(token, new SplitBatch { Splits = due.Select(e => e.Split).ToList() },
            result => Answered(result, due, token));
    }

    /// <summary>Held laps go once the room is known to count, and are dropped if it doesn't.</summary>
    private void Release()
    {
        if (_held.Count == 0 || _room.Counting == Counting.Unknown)
            return;
        foreach (var (room, split) in _held)
        {
            if (_room.Counting == Counting.Live && room == (_room.Room ?? ""))
            {
                split.PanelId ??= _room.PanelId;
                _outbox.Add(split);
            }
        }
        _held.Clear();
    }

    private void Answered(Result<SplitBatchAnswer> result, List<SplitOutbox.Entry> sent, string token)
    {
        _sending = false;
        var now = Time.realtimeSinceStartup;
        if (result.Ok)
        {
            _backoff = BackoffMinSeconds;
            _next = now + 1f;
            var answers = result.Value!.Results
                .GroupBy(r => r.ClientRef)
                .ToDictionary(g => g.Key, g => g.First().Status);
            var done = sent.Where(e => answers.TryGetValue(e.Split.ClientRef, out var s) && (s == "accepted" || s == "duplicate")).ToList();
            var later = sent.Except(done).ToList();
            _outbox.Remove(done);
            if (later.Count > 0)
                _outbox.Later(later, DateTimeOffset.UtcNow + NotYet);
            if (_sent == 0 && done.Count > 0)
                Plugin.Log.LogInfo("HUD: your gate splits are reaching the JMT site.");
            if (later.Count > 0 && answers.Values.Contains("not_linked"))
                Plugin.Log.LogInfo("HUD: the JMT site won't take gate splits for this game id until it's yours there: type a claim code in a JMT room.");
            _sent += done.Count;
            return;
        }

        switch (result.Status)
        {
            case 401:
            case 403:
                _refused = token;
                _link.Refused(result.Error ?? "The JMT site refused this computer's link. Link again.");
                break;
            case 404:
                // A site from before gate splits.
                _outbox.Later(sent, DateTimeOffset.UtcNow + NotYet);
                _next = now + EverySeconds;
                break;
            case 422:
                if (sent.Count > 1)
                {
                    _outbox.SendAlone(sent);
                }
                else
                {
                    _outbox.Remove(sent);
                    Plugin.Log.LogWarning($"HUD: the JMT site refused a lap's gate splits, so they're dropped: {result.Error}");
                }
                _next = now + 1f;
                break;
            default:
                _backoff = Mathf.Min(_backoff * 2f, BackoffMaxSeconds);
                _next = now + _backoff;
                break;
        }
    }
}
