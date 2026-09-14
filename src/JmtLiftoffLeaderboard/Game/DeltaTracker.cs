using System;
using System.Collections.Generic;
using JmtLiftoffLeaderboard.Site;
using UnityEngine;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// The delta bar's side of the game: which course is being flown, its splits loaded from
/// and saved to <see cref="SplitStore"/>, and the local pilot's gates and laps from
/// <see cref="LocalRun"/> handed to that course's <see cref="DeltaRun"/>.
///
/// A course with no best lap on this computer is seeded from the JMT site: its gates, the
/// pilot's best there, and the course's quickest lap to measure against, so the bar works
/// from the first lap.
///
/// Every course flown this session keeps its run, so its best tonight and its laps for the
/// lap review survive flying another course and coming back.
/// </summary>
internal sealed class DeltaTracker
{
    private const float TrackLookSeconds = 10f;
    private const float NoTrackLookSeconds = 2f;
    private const float SeedRetrySeconds = 5f;
    // About ten minutes of waiting for the board tracker to name a course's board.
    private const int SeedTries = 120;

    private readonly LocalRun _localRun;
    private readonly Settings _settings;
    private readonly SiteClient _site;
    private readonly Func<TrackRef, long?> _boardIdFor;
    private readonly Func<string?> _me;
    private readonly Dictionary<string, DeltaRun> _runs = new();
    private readonly HashSet<string> _seeded = new();
    private DeltaRun? _run;
    private TrackRef? _track;
    private string? _key;
    private string _trackName = "";
    private float _nextTrackLook;
    private float _nextSeed;
    private int _seedTries;

    public DeltaTracker(LocalRun run, Settings settings, SiteClient site, Func<TrackRef, long?> boardIdFor, Func<string?> me)
    {
        _localRun = run;
        _settings = settings;
        _site = site;
        _boardIdFor = boardIdFor;
        _me = me;
        run.Gate += gate =>
        {
            _run?.Gate(gate.Id, gate.Lap, gate.RunSeconds, gate.At);
            Changed?.Invoke();
        };
        run.Lap += OnLap;
        // A respawn gives up the lap under way and starts the next from the line. The reset
        // the game reports just after it isn't passed on: it would land on the fresh lap. A
        // run started again without a respawn shows in the gates: the start line, or a lap
        // number going back.
        run.Spawned += () =>
        {
            _run?.Spawn();
            Changed?.Invoke();
        };
        run.RaceStarted += () =>
        {
            _run?.NewRace();
            // A new race may be a new course. The level takes a moment to load.
            _nextTrackLook = Mathf.Min(_nextTrackLook, Time.realtimeSinceStartup + 1.5f);
            Changed?.Invoke();
        };
        settings.DeltaCompare.SettingChanged += (_, _) => Changed?.Invoke();
        settings.DeltaSectors.SettingChanged += (_, _) => Changed?.Invoke();
    }

    /// <summary>Something the bar shows changed. It also redraws on its own while a lap runs.</summary>
    public event Action? Changed;

    /// <summary>A lap finished that the bar has something to say about: a best, or a course learned.</summary>
    public event Action<LapOutcome>? News;

    /// <summary>A lap finished: the lap as the delta measured it, the course's board, and the lap before it in the run.</summary>
    public event Action<FlownLap, long?, int?>? LapFinished;

    public bool HasCourse => _run != null;

    /// <summary>The course being flown, with its laps this session, for the lap review.</summary>
    public DeltaRun? Run => _run;

    public string TrackName => _trackName;

    private bool Tonight => _settings.DeltaCompare.Value == DeltaCompare.Tonight;

    /// <summary>Called every frame. <paramref name="flying"/> when a course is loaded and the pilot could be on it.</summary>
    public void Tick(bool flying)
    {
        if (!flying)
            return;
        var now = Time.realtimeSinceStartup;
        if (now >= _nextTrackLook)
            LookAtTrack(now);
        if (_key != null && !_seeded.Contains(_key) && now >= _nextSeed)
            TrySeed(now);
    }

    public DeltaView? View(float now) => _run?.View(now, Tonight, _settings.DeltaSectorCount);

    /// <summary>"Forget this course's best": the next clean lap is the new one to beat.</summary>
    public void Forget()
    {
        if (_run == null || _key == null)
            return;
        _run.Forget();
        Save();
        Plugin.Log.LogInfo($"HUD: forgot your splits for {_trackName}.");
        Changed?.Invoke();
    }

    private void LookAtTrack(float now)
    {
        var track = CurrentTrack.Read();
        _nextTrackLook = now + (track == null ? NoTrackLookSeconds : TrackLookSeconds);
        var key = track == null ? null : SplitStore.Key(track);
        if (key == _key)
            return;

        _key = key;
        _track = track;
        _trackName = track?.Name ?? "";
        _nextSeed = now;
        _seedTries = 0;
        if (key == null)
        {
            _run = null;
        }
        else if (!_runs.TryGetValue(key, out _run))
        {
            _run = new DeltaRun(SplitStore.Load(key, track!.Name));
            _runs[key] = _run;
            var best = _run.Course.Best;
            Plugin.Log.LogInfo(best == null
                ? $"HUD: no splits for {_trackName} on this computer yet."
                : $"HUD: splits for {_trackName} loaded: best {best.LapMs} ms through {best.Gates.Count} gates.");
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// A course with no best here asks the JMT site for its gates and times, once the board
    /// tracker has named the course's board. Once per course per session.
    /// </summary>
    private void TrySeed(float now)
    {
        _nextSeed = now + SeedRetrySeconds;
        var key = _key!;
        if (_run == null || _track == null || _run.Course.Best != null || !_settings.SplitsFromSite.Value || ++_seedTries > SeedTries)
        {
            _seeded.Add(key);
            return;
        }
        var boardId = _boardIdFor(_track);
        if (boardId == null)
            return;

        _seeded.Add(key);
        var run = _run;
        var name = _trackName;
        _site.CourseSplits(boardId.Value, _me(), result =>
        {
            if (!result.Ok || run != _run)
                return;
            var seed = ToSeed(result.Value!);
            if (seed == null)
            {
                Plugin.Log.LogInfo($"HUD: the JMT site has no gate splits for {name} yet.");
                return;
            }
            if (!run.Seed(seed))
                return;
            Save();
            Plugin.Log.LogInfo(run.Course.Best != null
                ? $"HUD: splits for {name} from the JMT site: your best, {run.Course.Best.LapMs} ms through {seed.Gates.Count} gates."
                : $"HUD: splits for {name} from the JMT site: {seed.Gates.Count} gates, measured against {seed.ReferenceName}'s {seed.Reference!.LapMs} ms.");
            Changed?.Invoke();
        });
    }

    private static CourseSeed? ToSeed(SiteCourseSplits splits)
    {
        var gates = splits.Gates;
        if (gates.Count == 0)
            return null;
        LapSplits? Lap(SiteSplitLap? lap) =>
            lap != null && lap.Times.Count == gates.Count
                ? new LapSplits { Gates = new List<string>(gates), Times = new List<int>(lap.Times), LapMs = lap.LapMs }
                : null;
        var seed = new CourseSeed
        {
            Gates = new List<string>(gates),
            Best = Lap(splits.Pilot?.Pb),
            Reference = Lap(splits.BoardBest),
            ReferenceName = splits.BoardBest?.Pilot.DisplayName,
        };
        if (splits.Pilot?.StretchBests is { } bests && bests.Count == gates.Count + 1)
            seed.Segments = new List<int?>(bests);
        return seed.Best == null && seed.Reference == null ? null : seed;
    }

    private void OnLap(int lapMs)
    {
        if (_run == null)
            return;
        var outcome = _run.Finish(lapMs, Time.realtimeSinceStartup, Tonight, _settings.DeltaSectorCount);
        if (_run.Dirty)
            Save();
        if (outcome == LapOutcome.CourseLearned)
            Plugin.Log.LogInfo($"HUD: learned {_trackName}'s gates: {_run.Course.Gates.Count} of them.");
        if (outcome != LapOutcome.Timed)
            News?.Invoke(outcome);
        if (_run.Laps.Count > 0)
            LapFinished?.Invoke(_run.Laps[_run.Laps.Count - 1], _track == null ? null : _boardIdFor(_track), _localRun.PreviousLapMs);
        Changed?.Invoke();
    }

    private void Save()
    {
        if (_run == null || _key == null)
            return;
        _run.Dirty = false;
        SplitStore.Save(_key, _run.Course);
    }
}
