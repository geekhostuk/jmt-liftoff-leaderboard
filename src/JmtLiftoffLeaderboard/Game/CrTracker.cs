using System;
using System.Collections.Generic;
using JmtLiftoffLeaderboard.Site;
using UnityEngine;

namespace JmtLiftoffLeaderboard.Game;

internal enum PipKind
{
    Lap,
    Failed,
}

/// <summary>One attempt on the HUD's strip: a lap, or a failed attempt, and whether the room counted it.</summary>
internal readonly struct Pip
{
    public Pip(PipKind kind, bool counted)
    {
        Kind = kind;
        Counted = counted;
    }

    public PipKind Kind { get; }
    public bool Counted { get; }
}

internal enum CrState
{
    Looking,
    NoPilot,
    Unreachable,
    Ready,
}

/// <summary>A counted lap or failed attempt moved the CR, and perhaps the license.</summary>
internal readonly struct Pulse
{
    public Pulse(double delta, bool failed, string? promoted, string? demoted)
    {
        Delta = delta;
        Failed = failed;
        Promoted = promoted;
        Demoted = demoted;
    }

    public double Delta { get; }
    public bool Failed { get; }
    public string? Promoted { get; }
    public string? Demoted { get; }
}

/// <summary>
/// The local pilot's Consistency Rating as it moves while they fly.
///
/// The site answers with the races their CR is taken over. Every lap and failed attempt
/// flown since, in a room whose panel reports to the JMT site, goes in front of those,
/// and the CR is worked out again as the site will work it out once the panel has sent
/// them. In any other room nothing counts, so nothing is added.
///
/// The site is asked again only after a few quiet minutes, by when the panel has long
/// since sent everything flown, so nothing is counted twice.
/// </summary>
internal sealed class CrTracker
{
    public const int PipCount = 20;
    private const float RetrySeconds = 60f;
    private static readonly TimeSpan Settled = TimeSpan.FromMinutes(3);

    private readonly SiteClient _site;
    private readonly LocalPilot _me;
    private readonly RoomWatch _room;

    // The site's races, newest first; and in front of them, the races flown here since it
    // answered, the one under way first.
    private List<CrRace>? _siteRaces;
    private readonly List<CrRace> _flown = new() { new CrRace() };
    private DateTime _answeredAt;
    private DateTime _lastCountedAt = DateTime.MinValue;
    private bool _asking;
    private float _nextAskAt;
    private readonly List<Pip> _pips = new();
    private Standing? _now;
    private Forecast? _forecast;

    public CrTracker(SiteClient site, LocalPilot me, LocalRun run, RoomWatch room)
    {
        _site = site;
        _me = me;
        _room = room;
        run.Lap += OnLap;
        run.Reset += OnReset;
        run.RaceStarted += OnRaceStarted;
        room.Changed += Invalidate;
    }

    /// <summary>Anything shown changed.</summary>
    public event Action? Changed;

    public event Action<Pulse>? Pulsed;

    public CrModel Model { get; private set; } = new();
    public CrState State { get; private set; } = CrState.Looking;
    public Counting Counting => _room.Counting;

    /// <summary>The CR the site gave when first asked this session, which "tonight" is measured from.</summary>
    public double? NightStartCr { get; private set; }

    public int NightFailed { get; private set; }
    public int NightLaps { get; private set; }

    /// <summary>Laps since the last failed attempt, counted or not.</summary>
    public int Streak { get; private set; }
    public int BestStreak { get; private set; }

    /// <summary>The latest attempts, oldest first.</summary>
    public IReadOnlyList<Pip> Pips => _pips;

    /// <summary>The CR with everything counted so far. Null until the site has answered.</summary>
    public Standing? Now => _siteRaces == null ? null : _now ??= CrMath.Stand(Model, Races());

    public Forecast? Forecast
    {
        get
        {
            var now = Now;
            return now == null ? null : _forecast ??= CrMath.Forecast(Model, Races(), now);
        }
    }

    /// <summary>Called every frame. <paramref name="flying"/> when a course is loaded and the pilot could be on it.</summary>
    public void Tick(bool flying)
    {
        if (flying && _siteRaces == null && !_asking && Time.realtimeSinceStartup >= _nextAskAt)
            Ask();
    }

    // ── What's flown ────────────────────────────────────────────────────────

    private void OnLap(int lapMs)
    {
        AddPip(PipKind.Lap);
        Streak++;
        BestStreak = Math.Max(BestStreak, Streak);
        if (Counting != Counting.Live)
        {
            Invalidate();
            return;
        }

        NightLaps++;
        _lastCountedAt = DateTime.UtcNow;
        if (Now is { } before)
        {
            _flown[0].Laps++;
            Moved(before, failed: false);
        }
        else
        {
            Invalidate();
        }
    }

    private void OnReset(int? attemptMs)
    {
        var failed = attemptMs is { } ms && ms >= Model.TooShortMs && ms <= Model.IdleMs;
        Plugin.Log.LogInfo(attemptMs == null
            ? "HUD: a reset noticed late, with no time for the attempt; not a failed attempt."
            : $"HUD: reset {attemptMs.Value / 1000.0:0.0}s into the attempt; {(failed ? "a failed attempt" : "not a failed attempt")}.");
        if (!failed)
            return;

        AddPip(PipKind.Failed);
        Streak = 0;
        if (Counting != Counting.Live)
        {
            Invalidate();
            return;
        }

        NightFailed++;
        _lastCountedAt = DateTime.UtcNow;
        if (Now is { } before)
        {
            _flown[0].Failed++;
            Moved(before, failed: true);
        }
        else
        {
            Invalidate();
        }
    }

    private void OnRaceStarted()
    {
        NewHead();
        var now = DateTime.UtcNow;
        if (_siteRaces != null && !_asking && now - _lastCountedAt > Settled && now - _answeredAt > Settled)
            Ask();
        Invalidate();
    }

    private void NewHead()
    {
        if (_flown[0].Laps > 0 || _flown[0].Failed > 0)
            _flown.Insert(0, new CrRace());
    }

    private void AddPip(PipKind kind)
    {
        _pips.Add(new Pip(kind, Counting == Counting.Live));
        if (_pips.Count > PipCount)
            _pips.RemoveAt(0);
    }

    private void Moved(Standing before, bool failed)
    {
        _now = null;
        _forecast = null;
        var after = Now!;
        var was = CrMath.Rank(Model, before.Rung.Name);
        var @is = CrMath.Rank(Model, after.Rung.Name);
        Pulsed?.Invoke(new Pulse(after.Cr - before.Cr, failed,
            @is > was ? after.Rung.Name : null,
            @is < was ? after.Rung.Name : null));
        Changed?.Invoke();
    }

    private void Invalidate()
    {
        _now = null;
        _forecast = null;
        Changed?.Invoke();
    }

    private List<CrRace> Races()
    {
        var races = new List<CrRace>(_flown.Count + (_siteRaces?.Count ?? 0));
        races.AddRange(_flown);
        if (_siteRaces != null)
            races.AddRange(_siteRaces);
        return races;
    }

    // ── Asking the site ─────────────────────────────────────────────────────

    private void Ask()
    {
        _asking = true;
        // Anything flown while the question is out goes in a race of its own, kept when
        // the answer replaces the rest.
        NewHead();
        _me.Resolve(id =>
        {
            if (id == null)
            {
                Failed(_me.SiteUnreachable ? CrState.Unreachable : CrState.NoPilot);
                return;
            }
            _site.Consistency(id, answer =>
            {
                if (answer.Ok)
                    Adopt(answer.Value!.Model ?? new CrModel(), answer.Value.Races ?? new List<CrRace>());
                else if (answer.NotFound)
                    FromProfile(id);
                else
                    Failed(CrState.Unreachable);
            });
        });
    }

    /// <summary>A site from before the HUD: the profile's totals, as one race, with the numbers this version knows.</summary>
    private void FromProfile(string id) => _site.Pilot(id, profile =>
    {
        if (!profile.Ok)
        {
            Failed(profile.NotFound ? CrState.NoPilot : CrState.Unreachable);
            return;
        }
        var p = profile.Value!;
        var races = new List<CrRace>();
        if (p.CrRating != null)
            races.Add(new CrRace { Failed = p.CrFailed, Laps = p.CrLaps });
        Adopt(new CrModel(), races);
    });

    private void Adopt(CrModel model, List<CrRace> races)
    {
        _asking = false;
        Model = model;
        _siteRaces = races;
        var head = _flown[0];
        _flown.Clear();
        _flown.Add(head);
        _answeredAt = DateTime.UtcNow;
        State = CrState.Ready;
        _now = null;
        _forecast = null;
        var now = Now!;
        NightStartCr ??= now.Cr;
        Plugin.Log.LogInfo($"HUD: CR {now.Cr:0.00} ({now.Rung.Name}) over {races.Count} races, {now.Laps} laps and {now.Failed} failed attempts.");
        Invalidate();
    }

    private void Failed(CrState state)
    {
        _asking = false;
        _nextAskAt = Time.realtimeSinceStartup + RetrySeconds;
        // Asking again after a quiet spell keeps what the last answer said.
        if (_siteRaces == null)
            State = state;
        Invalidate();
    }
}
