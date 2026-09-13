using System;
using System.Collections.Generic;

// Pure: no Unity, no game, so a scratch harness can compile it on its own and feed it laps.
namespace JmtLiftoffLeaderboard.Game;

/// <summary>A lap as its gates: which passages, in order, the lap timer at each in milliseconds, and the lap's time.</summary>
internal sealed class LapSplits
{
    public List<string> Gates = new();
    public List<int> Times = new();
    public int LapMs;

    public LapSplits Copy() => new() { Gates = new List<string>(Gates), Times = new List<int>(Times), LapMs = LapMs };
}

/// <summary>
/// One course's splits, kept on the pilot's computer between sessions: the gates a lap
/// passes, the best lap through them, and the best time ever flown for each stretch and
/// each sector of it.
/// </summary>
internal sealed class CourseSplits
{
    public const int CurrentVersion = 1;

    public int Version = CurrentVersion;
    public string Name = "";

    /// <summary>The course's gates, as passage ids in the order a lap passes them.</summary>
    public List<string> Gates = new();

    public LapSplits? Best;
    public DateTimeOffset? BestSetAt;

    /// <summary>
    /// The best time for each stretch of a lap: from the line to the first gate, gate to
    /// gate, and from the last gate to the line. One more than there are gates.
    /// </summary>
    public List<int?> Segments = new();

    /// <summary>The best time for each sector, by how many sectors the lap is split into.</summary>
    public Dictionary<int, List<int?>> Sectors = new();
}

internal enum SectorMark
{
    /// <summary>Not flown yet this lap.</summary>
    Pending,
    /// <summary>The quickest the pilot has ever flown it.</summary>
    Best,
    /// <summary>Quicker than the lap it's measured against.</summary>
    Faster,
    Slower,
}

internal readonly struct SectorView
{
    public SectorView(int? ms, int? deltaMs, SectorMark mark)
    {
        Ms = ms;
        DeltaMs = deltaMs;
        Mark = mark;
    }

    public int? Ms { get; }

    /// <summary>Against the same sector of the lap it's measured against: negative is quicker.</summary>
    public int? DeltaMs { get; }

    public SectorMark Mark { get; }
}

/// <summary>What the delta bar shows now.</summary>
internal sealed class DeltaView
{
    /// <summary>There's a lap to measure against.</summary>
    public bool HasReference;

    public int? ReferenceMs;

    /// <summary>The pilot's best stretches added up: the lap they could fly.</summary>
    public int? OptimalMs;

    /// <summary>A lap is under way and being timed.</summary>
    public bool Running;

    public int? ElapsedMs;

    /// <summary>Ahead (negative) or behind (positive) the lap measured against, or null when it can't be said.</summary>
    public int? DeltaMs;

    /// <summary>-1 when the last stretch gained time, 1 when it lost some.</summary>
    public int Trend;

    public List<SectorView> Sectors = new();

    /// <summary>The lap just finished, held on screen for a moment: its time, and whether it's the new best.</summary>
    public bool Finished;
    public int? FinishedMs;
    public bool NewBest;
}

internal enum LapOutcome
{
    /// <summary>A lap the delta can't learn from: it missed gates, or it wasn't seen from its start.</summary>
    Timed,
    /// <summary>A clean lap, and the best since the game started.</summary>
    BestTonight,
    /// <summary>The best lap the pilot has ever flown on the course.</summary>
    NewBest,
    /// <summary>The course's gates are new or changed: this lap is the first through them.</summary>
    CourseLearned,
}

/// <summary>
/// The delta for one course: follows the gates of the lap being flown, measures it against
/// the best lap, and learns from each lap flown cleanly through every gate.
///
/// The game says which lap each checkpoint was passed on and what its lap timer read there,
/// so a gate's split is the game's own. Between gates the bar still moves: once the lap's
/// clock runs past the time the best lap reached the next gate, the pilot is behind by at
/// least that much.
///
/// The start and finish line can arrive as checkpoints of their own. One at a lap timer of
/// zero only says when the lap began, and one at the lap's time only says when it ended, so
/// neither is kept as a split: laps from the grid and laps flown on through the line then
/// list the same gates.
/// </summary>
internal sealed class DeltaRun
{
    public static readonly int[] SectorCounts = { 3, 4, 5, 6 };

    /// <summary>A checkpoint this close to the lap timer's zero is the start line.</summary>
    private const int StartLineMs = 50;
    /// <summary>A checkpoint this close to the lap's time is the finish line.</summary>
    private const int FinishLineMs = 20;
    /// <summary>How long a finished lap stays on the bar.</summary>
    public const float FinishedSeconds = 3f;

    private readonly CourseSplits _course;
    private LapSplits? _tonight;
    // A different run of gates seen on one clean lap: the course's, if the next clean lap agrees.
    private List<string>? _candidate;

    private Lap _lap = new() { Whole = false };
    // A lap whose gates are done and whose time hasn't reached us yet.
    private Lap? _closing;
    private int? _closedIndex;
    // When the lap timer read zero, on the caller's clock.
    private float? _startedAt;
    private DeltaView? _finished;
    private float _finishedUntil;

    public DeltaRun(CourseSplits course)
    {
        _course = course;
        _tonight = null;
    }

    public CourseSplits Course => _course;

    /// <summary>The course's splits changed and want saving.</summary>
    public bool Dirty { get; set; }

    private sealed class Lap
    {
        public int? Index;
        public readonly List<string> Gates = new();
        public readonly List<int> Times = new();

        /// <summary>Seen from its start, so its gates are all of them.</summary>
        public bool Whole;

        /// <summary>Reset part way.</summary>
        public bool Broken;

        /// <summary>Every gate so far is the course's, in its order.</summary>
        public bool OnLayout = true;
    }

    // ── What the game says ──────────────────────────────────────────────────

    /// <summary>The pilot passed a checkpoint: its passage id, the game's lap number, the lap timer there, and when it reached us.</summary>
    public void Gate(string id, int index, float lapSeconds, float at)
    {
        // The finish line of the lap just timed, reported after its time.
        if (index == _closedIndex && _lap.Index == null)
            return;

        if (_lap.Index != null && index != _lap.Index)
        {
            // Onto the next lap before its time has reached us; a lap number that went back is a run restarted.
            var onward = index > _lap.Index;
            if (onward && _lap.Gates.Count > 0)
                _closing = _lap;
            _lap = new Lap { Whole = onward && !_lap.Broken };
        }
        _lap.Index = index;
        _startedAt = at - lapSeconds;

        var ms = ToMs(lapSeconds);
        if (_lap.Gates.Count == 0 && ms < StartLineMs)
            return; // the start line: it only says when the lap began

        _lap.Gates.Add(id);
        _lap.Times.Add(ms);
        var k = _lap.Gates.Count - 1;
        _lap.OnLayout &= k < _course.Gates.Count && _course.Gates[k] == id;
        if (_lap.Whole && !_lap.Broken && _lap.OnLayout)
            LearnStretch(_lap.Times, k, null);
    }

    /// <summary>A lap finished, with the game's time for it.</summary>
    public LapOutcome Finish(int lapMs, float at, bool tonight, int sectorCount)
    {
        var lap = _closing ?? _lap;
        if (lap == _lap)
        {
            // The next lap starts now. When its gates came first, they already said when it started.
            _lap = new Lap { Whole = true };
            _startedAt = at;
        }
        _closing = null;
        _closedIndex = lap.Index;

        // The finish line, when it arrived before the time did.
        while (lap.Times.Count > 0 && lap.Times[lap.Times.Count - 1] >= lapMs - FinishLineMs)
        {
            lap.Times.RemoveAt(lap.Times.Count - 1);
            lap.Gates.RemoveAt(lap.Gates.Count - 1);
        }
        lap.OnLayout = OnLayout(lap.Gates);

        var reference = Reference(tonight);
        var outcome = Learn(lap, lapMs);
        _finished = FinishedView(lap, lapMs, reference, tonight, sectorCount, outcome);
        _finishedUntil = at + FinishedSeconds;
        return outcome;
    }

    /// <summary>The drone was reset part way through a lap.</summary>
    public void Reset()
    {
        _lap.Broken = true;
        _closing = null;
        _startedAt = null;
    }

    /// <summary>The drone is at the start: the next lap is flown from the line.</summary>
    public void Spawn()
    {
        _lap = new Lap { Whole = true };
        _closing = null;
        _closedIndex = null;
        _startedAt = null;
    }

    /// <summary>A new race: nothing is known about the lap until the drone is at the start.</summary>
    public void NewRace()
    {
        _lap = new Lap { Whole = false };
        _closing = null;
        _closedIndex = null;
        _startedAt = null;
        _finished = null;
    }

    /// <summary>Drops everything learned about the course: its gates, its best lap and its best stretches.</summary>
    public void Forget()
    {
        _course.Gates.Clear();
        _course.Best = null;
        _course.BestSetAt = null;
        _course.Segments.Clear();
        _course.Sectors.Clear();
        _tonight = null;
        _candidate = null;
        _lap.OnLayout = false;
        _finished = null;
        Dirty = true;
    }

    // ── What the bar shows ──────────────────────────────────────────────────

    public DeltaView View(float now, bool tonight, int sectorCount)
    {
        if (_finished != null && now < _finishedUntil)
            return _finished;

        var reference = Reference(tonight);
        var view = new DeltaView
        {
            HasReference = reference != null,
            ReferenceMs = reference?.LapMs,
            OptimalMs = Optimal(),
        };
        if (_startedAt is not { } started || _lap.Broken)
            return view;

        view.Running = true;
        var elapsed = ToMs(now - started);
        view.ElapsedMs = elapsed;
        if (reference == null || !_lap.OnLayout)
            return view;

        var k = _lap.Gates.Count - 1;
        var atGate = k >= 0 ? _lap.Times[k] - reference.Times[k] : 0;
        var next = k + 1 < reference.Times.Count ? reference.Times[k + 1] : reference.LapMs;
        view.DeltaMs = Math.Max(atGate, elapsed - next);
        view.Trend = k >= 0 ? Math.Sign(Stretch(_lap.Times, k, null) - Stretch(reference.Times, k, null)) : 0;
        view.Sectors = Sectors(_lap.Times, null, reference, sectorCount);
        return view;
    }

    private LapSplits? Reference(bool tonight) => tonight ? _tonight : _course.Best;

    private int? Optimal()
    {
        if (_course.Segments.Count != _course.Gates.Count + 1 || _course.Best == null)
            return null;
        var sum = 0;
        foreach (var best in _course.Segments)
        {
            if (best == null)
                return null;
            sum += best.Value;
        }
        return sum;
    }

    private DeltaView FinishedView(Lap lap, int lapMs, LapSplits? reference, bool tonight, int sectorCount, LapOutcome outcome)
    {
        var view = new DeltaView
        {
            Finished = true,
            FinishedMs = lapMs,
            HasReference = reference != null,
            ReferenceMs = reference?.LapMs,
            OptimalMs = Optimal(),
            DeltaMs = reference != null ? lapMs - reference.LapMs : null,
            NewBest = outcome == LapOutcome.NewBest || (tonight && outcome == LapOutcome.BestTonight),
        };
        if (reference != null && lap.OnLayout && lap.Gates.Count == reference.Gates.Count)
            view.Sectors = Sectors(lap.Times, lapMs, reference, sectorCount);
        return view;
    }

    private List<SectorView> Sectors(List<int> times, int? lapMs, LapSplits reference, int count)
    {
        var sectors = new List<SectorView>();
        if (count <= 0)
            return sectors;
        var gates = reference.Gates.Count;
        var ends = SectorEnds(gates, count);
        _course.Sectors.TryGetValue(count, out var bests);
        for (var i = 0; i < ends.Length; i++)
        {
            var end = ends[i];
            var done = end < gates ? end < times.Count : lapMs != null;
            if (!done)
            {
                sectors.Add(new SectorView(null, null, SectorMark.Pending));
                continue;
            }
            var from = i > 0 ? ends[i - 1] : -1;
            var ms = At(times, end, lapMs!.GetValueOrDefault(), gates) - At(times, from, 0, gates);
            var refMs = At(reference.Times, end, reference.LapMs, gates) - At(reference.Times, from, 0, gates);
            var best = bests != null && i < bests.Count ? bests[i] : null;
            var mark = best != null && ms <= best ? SectorMark.Best : ms < refMs ? SectorMark.Faster : SectorMark.Slower;
            sectors.Add(new SectorView(ms, ms - refMs, mark));
        }
        return sectors;
    }

    // ── Learning ────────────────────────────────────────────────────────────

    private LapOutcome Learn(Lap lap, int lapMs)
    {
        if (!lap.Whole || lap.Broken)
            return LapOutcome.Timed;

        if (_course.Best == null && _course.Gates.Count == 0)
            return Adopt(lap, lapMs, LapOutcome.NewBest);

        if (Same(lap.Gates, _course.Gates))
        {
            LearnStretch(lap.Times, lap.Gates.Count, lapMs);
            var outcome = LapOutcome.Timed;
            if (_tonight == null || lapMs < _tonight.LapMs)
            {
                _tonight = Splits(lap, lapMs);
                outcome = LapOutcome.BestTonight;
            }
            if (_course.Best == null || lapMs < _course.Best.LapMs)
            {
                _course.Best = Splits(lap, lapMs);
                _course.BestSetAt = DateTimeOffset.UtcNow;
                Dirty = true;
                outcome = LapOutcome.NewBest;
            }
            return outcome;
        }

        // Other gates. A lap that saw none never replaces a course that has some: its
        // checkpoints didn't reach us. Otherwise the course has changed if this lap shares
        // none of its gates, or the last clean lap went through the same new ones.
        if (lap.Gates.Count == 0 && _course.Gates.Count > 0)
            return LapOutcome.Timed;
        var unlike = lap.Gates.Count >= 2 && !Overlaps(lap.Gates, _course.Gates);
        if (unlike || (_candidate != null && Same(lap.Gates, _candidate)))
            return Adopt(lap, lapMs, LapOutcome.CourseLearned);
        _candidate = new List<string>(lap.Gates);
        return LapOutcome.Timed;
    }

    /// <summary>A lap through gates the course didn't have: from now on they're the course's, and it's the lap to beat.</summary>
    private LapOutcome Adopt(Lap lap, int lapMs, LapOutcome outcome)
    {
        _course.Gates = new List<string>(lap.Gates);
        _course.Best = Splits(lap, lapMs);
        _course.BestSetAt = DateTimeOffset.UtcNow;
        _course.Segments = new List<int?>();
        _course.Sectors = new Dictionary<int, List<int?>>();
        _tonight = Splits(lap, lapMs);
        _candidate = null;
        for (var k = 0; k <= lap.Gates.Count; k++)
            LearnStretch(lap.Times, k, lapMs);

        // The lap under way may already be through some of them.
        _lap.OnLayout = OnLayout(_lap.Gates);
        Dirty = true;
        return outcome;
    }

    /// <summary>Whether <paramref name="gates"/> are the course's first gates, in its order.</summary>
    private bool OnLayout(List<string> gates)
    {
        if (gates.Count > _course.Gates.Count)
            return false;
        for (var i = 0; i < gates.Count; i++)
        {
            if (gates[i] != _course.Gates[i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// The stretch ending at gate <paramref name="k"/> (at the line, when <paramref name="k"/>
    /// is the gate count) was flown cleanly: keep it if it's the quickest yet, and the sector
    /// it closes, for each way of splitting the lap.
    /// </summary>
    private void LearnStretch(List<int> times, int k, int? lapMs)
    {
        var gates = _course.Gates.Count;
        if (k > gates || (k == gates && lapMs == null))
            return;
        var lap = lapMs.GetValueOrDefault();
        while (_course.Segments.Count < gates + 1)
            _course.Segments.Add(null);
        Keep(_course.Segments, k, Stretch(times, k, lapMs));

        foreach (var count in SectorCounts)
        {
            var ends = SectorEnds(gates, count);
            var i = Array.IndexOf(ends, k);
            if (i < 0)
                continue;
            if (!_course.Sectors.TryGetValue(count, out var bests))
                _course.Sectors[count] = bests = new List<int?>();
            while (bests.Count < ends.Length)
                bests.Add(null);
            var from = i > 0 ? ends[i - 1] : -1;
            Keep(bests, i, At(times, k, lap, gates) - At(times, from, 0, gates));
        }
    }

    private void Keep(List<int?> bests, int index, int ms)
    {
        if (ms <= 0 || (bests[index] is { } best && best <= ms))
            return;
        bests[index] = ms;
        Dirty = true;
    }

    // ── Arithmetic ──────────────────────────────────────────────────────────

    /// <summary>
    /// The gate each sector ends at, splitting a lap through <paramref name="gates"/> gates
    /// into <paramref name="count"/> sectors of as equal a number of stretches as can be. The
    /// last ends at the line, given as the gate count. Fewer sectors when there are fewer stretches.
    /// </summary>
    public static int[] SectorEnds(int gates, int count)
    {
        var stretches = gates + 1;
        var n = Math.Max(0, Math.Min(count, stretches));
        var ends = new int[n];
        for (var i = 0; i < n - 1; i++)
            ends[i] = (int)Math.Round((i + 1) * stretches / (double)n, MidpointRounding.AwayFromZero) - 1;
        if (n > 0)
            ends[n - 1] = gates;
        return ends;
    }

    /// <summary>The lap timer at gate <paramref name="index"/>: zero before the first, the lap's time at the line.</summary>
    private static int At(List<int> times, int index, int lapMs, int gates) =>
        index < 0 ? 0 : index >= gates ? lapMs : times[index];

    /// <summary>The stretch ending at gate <paramref name="k"/>, or at the line when <paramref name="k"/> is past the last gate.</summary>
    private static int Stretch(List<int> times, int k, int? lapMs)
    {
        var end = k < times.Count ? times[k] : lapMs.GetValueOrDefault();
        return end - (k > 0 ? times[k - 1] : 0);
    }

    private static LapSplits Splits(Lap lap, int lapMs) =>
        new() { Gates = new List<string>(lap.Gates), Times = new List<int>(lap.Times), LapMs = lapMs };

    private static bool Same(List<string> a, List<string> b)
    {
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    private static bool Overlaps(List<string> a, List<string> b)
    {
        var set = new HashSet<string>(b);
        foreach (var id in a)
        {
            if (set.Contains(id))
                return true;
        }
        return false;
    }

    private static int ToMs(float seconds) => (int)Math.Round(seconds * 1000d);
}
