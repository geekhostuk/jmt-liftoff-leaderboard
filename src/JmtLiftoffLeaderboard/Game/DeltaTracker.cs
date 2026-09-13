using System;
using System.Collections.Generic;
using UnityEngine;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// The delta bar's side of the game: which course is being flown, its splits loaded from
/// and saved to <see cref="SplitStore"/>, and the local pilot's gates and laps from
/// <see cref="LocalRun"/> handed to that course's <see cref="DeltaRun"/>.
///
/// Every course flown this session keeps its run, so its best tonight survives flying
/// another course and coming back.
/// </summary>
internal sealed class DeltaTracker
{
    private const float TrackLookSeconds = 10f;
    private const float NoTrackLookSeconds = 2f;

    private readonly Settings _settings;
    private readonly Dictionary<string, DeltaRun> _runs = new();
    private DeltaRun? _run;
    private string? _key;
    private string _trackName = "";
    private float _nextTrackLook;

    public DeltaTracker(LocalRun run, Settings settings)
    {
        _settings = settings;
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

    public bool HasCourse => _run != null;

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
        _trackName = track?.Name ?? "";
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
                ? $"HUD: no splits for {_trackName} yet; the first clean lap sets them."
                : $"HUD: splits for {_trackName} loaded: best {best.LapMs} ms through {best.Gates.Count} gates.");
        }
        Changed?.Invoke();
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
