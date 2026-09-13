using System;
using System.Collections.Generic;

// Pure: no Unity, no game, so a scratch harness can compile it on its own and feed it laps.
namespace JmtLiftoffLeaderboard.Game;

/// <summary>A lap as its gates: which passages, in order, the time into the lap at each in milliseconds, and the lap's time.</summary>
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
    public SectorView(int? ms, int? deltaMs, SectorMark mark, int refMs)
    {
        Ms = ms;
        DeltaMs = deltaMs;
        Mark = mark;
        RefMs = refMs;
    }

    public int? Ms { get; }

    /// <summary>Against the same sector of the lap it's measured against: negative is quicker.</summary>
    public int? DeltaMs { get; }

    public SectorMark Mark { get; }

    /// <summary>The sector on the lap it's measured against, which says how much of the lap it is.</summary>
    public int RefMs { get; }
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
/// The game says which lap each checkpoint was passed on, and when, on the run's clock:
/// it starts at the line after a spawn and runs on through every lap, so lap 2's gates read
/// 25.9 s and up. Each lap's gates are taken from where that lap started, which is where the
/// last one finished: the game reports the finish as a passage of its own at the lap's time,
/// and the lap's time from the game settles it. The clock going back to zero, or the start
/// line on the lap under way, is the run starting again, after a reset whether or not its
/// respawn has been heard of yet.
///
/// Between gates the bar still moves: once the lap's clock runs past the time the best lap
/// reached the next gate, the pilot is behind by at least that much.
///
/// The start and finish passages aren't kept as splits: one at the start of a lap only says
/// when it began, and one at its time only says when it ended. Laps from the grid and laps
/// flown on through the line then list the same gates.
/// </summary>
internal sealed class DeltaRun
{
    public static readonly int[] SectorCounts = { 3, 4, 5, 6 };

    /// <summary>A sector count that gives every stretch between gates a sector of its own, however many the course has.</summary>
    public const int EveryGate = int.MaxValue;

    /// <summary>A checkpoint this soon into a lap is the start line: the game reports it about 20 ms after the last lap's finish.</summary>
    private const int StartLineMs = 50;
    /// <summary>A checkpoint this close to the lap's time is the finish line.</summary>
    private const int FinishLineMs = 20;
    /// <summary>How long a finished lap stays on the bar.</summary>
    public const float FinishedSeconds = 3f;
    /// <summary>How many laps and attempts the lap review keeps for a course.</summary>
    public const int KeptLaps = 20;

    private readonly CourseSplits _course;
    private LapSplits? _tonight;
    private readonly List<FlownLap> _laps = new();
    private int _attempts;
    // A different run of gates seen on one clean lap: the course's, if the next clean lap agrees.
    private List<string>? _candidate;

    private Lap _lap = new() { Whole = false };
    // A lap whose gates are done and whose time hasn't reached us yet.
    private Lap? _closing;
    private int? _closedIndex;
    // When the lap under way started, on the caller's clock.
    private float? _startedAt;
    private DeltaView? _finished;
    private float _finishedUntil;

    // The run's clock: where the lap under way started on it, whether that's known, where the
    // lap just timed ended on it, and the last checkpoint's lap number and time. The start
    // passage's id, once seen, marks the start of every lap.
    private int _base;
    private bool _clocked;
    private int? _pendingBase;
    private int? _lastIndex;
    private int _lastRaw;
    private string? _startId;

    public DeltaRun(CourseSplits course)
    {
        _course = course;
        _tonight = null;
    }

    public CourseSplits Course => _course;

    /// <summary>The best lap since the game started, through the course's gates.</summary>
    public LapSplits? Tonight => _tonight;

    /// <summary>The laps flown on the course this session, and the attempts reset part way, oldest first: the last <see cref="KeptLaps"/>.</summary>
    public IReadOnlyList<FlownLap> Laps => _laps;

    /// <summary>Goes up each time a lap or attempt joins <see cref="Laps"/>.</summary>
    public int LapsVersion { get; private set; }

    /// <summary>The course's splits changed and want saving.</summary>
    public bool Dirty { get; set; }

    private sealed class Lap
    {
        public int? Index;
        public readonly List<string> Gates = new();
        public readonly List<int> Times = new();

        /// <summary>Where the lap started on the run's clock, and whether that's known.</summary>
        public int Base;
        public bool Clocked;

        /// <summary>Seen from its start, so its gates are all of them.</summary>
        public bool Whole;

        /// <summary>Begun by the start line itself, rather than by a respawn or the last lap's time.</summary>
        public bool FromLine;

        /// <summary>Reset part way.</summary>
        public bool Broken;

        /// <summary>Every gate so far is the course's, in its order.</summary>
        public bool OnLayout = true;
    }

    // ── What the game says ──────────────────────────────────────────────────

    /// <summary>The pilot passed a checkpoint: its passage id, the game's lap number, the run's clock there, and when it reached us.</summary>
    public void Gate(string id, int index, float runSeconds, float at)
    {
        var raw = ToMs(runSeconds);

        // The finish line of the lap just timed, reported after its time.
        if (index == _closedIndex && _lap.Index == null && _pendingBase is { } end && Math.Abs(raw - end) <= StartLineMs)
            return;

        if (_lastIndex is { } last && (index < last || (index == last && raw + StartLineMs < _lastRaw)))
        {
            // The run started again, and its clock with it.
            _base = 0;
            _clocked = true;
            _pendingBase = null;
            _closing = null;
            _closedIndex = null;
            GaveUp(_lap);
            _lap = new Lap { Whole = true };
        }
        else if (_lastIndex is { } previous && index > previous)
        {
            // On to the next lap: it started where the last one finished.
            _base = _pendingBase ?? _lastRaw;
            _clocked = index == previous + 1;
            _pendingBase = null;
        }
        else if (_lastIndex == null && raw < StartLineMs)
        {
            // The first checkpoint heard is a run's start.
            _base = 0;
            _clocked = true;
        }
        _lastIndex = index;
        _lastRaw = raw;

        if (_lap.Index != null && index != _lap.Index)
        {
            // Onto the next lap before its time has reached us.
            if (_lap.Gates.Count > 0)
                _closing = _lap;
            _lap = new Lap { Whole = !_lap.Broken };
        }
        _lap.Index = index;
        _lap.Base = _base;
        _lap.Clocked = _clocked;

        var ms = raw - _base;
        _startedAt = _clocked ? at - ms / 1000f : null;

        if (ms < StartLineMs && index == 0 && raw < StartLineMs)
            _startId = id;
        if (ms < StartLineMs || id == _startId)
        {
            // The start line: the lap begins here, so it's seen from its start. One already
            // under way was given up: the run started again.
            if (_lap.Gates.Count > 0 || _lap.Broken)
                _lap = new Lap { Index = index, Base = _base, Clocked = _clocked };
            _lap.Whole = true;
            _lap.FromLine = true;
            return;
        }
        // The same passage twice running is one passage reported twice.
        if (_lap.Gates.Count > 0 && _lap.Gates[_lap.Gates.Count - 1] == id)
            return;

        _lap.Gates.Add(id);
        _lap.Times.Add(ms);
        var k = _lap.Gates.Count - 1;
        _lap.OnLayout &= k < _course.Gates.Count && _course.Gates[k] == id;
        if (_lap.Whole && _lap.Clocked && !_lap.Broken && _lap.OnLayout)
            LearnStretch(_lap.Times, k, null);
    }

    /// <summary>A lap finished, with the game's time for it.</summary>
    public LapOutcome Finish(int lapMs, float at, bool tonight, int sectorCount)
    {
        var lap = _closing ?? _lap;
        if (lap == _lap)
        {
            // The next lap starts now, where this one ends on the run's clock.
            _lap = new Lap { Whole = true };
            _startedAt = at;
            _pendingBase = lap.Clocked ? lap.Base + lapMs : null;
        }
        else if (lap.Clocked && _lap.Clocked)
        {
            // The lap under way took its start from the last checkpoint of this one; the lap's
            // time says exactly where it was.
            var shift = _lap.Base - (lap.Base + lapMs);
            if (shift != 0)
            {
                for (var i = 0; i < _lap.Times.Count; i++)
                    _lap.Times[i] += shift;
                _lap.Base -= shift;
                _base = _lap.Base;
                if (_startedAt != null)
                    _startedAt -= shift / 1000f;
            }
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
        Record(lap, lapMs, outcome);
        _finished = FinishedView(lap, lapMs, reference, tonight, sectorCount, outcome);
        _finishedUntil = at + FinishedSeconds;
        return outcome;
    }

    /// <summary>
    /// The lap under way was given up. A lap that hasn't started isn't touched: the game
    /// reports a reset just after the respawn that has already started the lap again.
    /// </summary>
    public void Reset()
    {
        if (_lap.Gates.Count == 0 && _startedAt == null)
            return;
        _lap.Broken = true;
        _closing = null;
        _startedAt = null;
    }

    /// <summary>The drone is at the start: the next lap is flown from the line, on a run clock back at zero.</summary>
    public void Spawn()
    {
        // The start line got here first: the respawn that led to it is old news.
        if (_lap.FromLine && _lap.Gates.Count == 0 && !_lap.Broken)
            return;
        GaveUp(_lap);
        _lap = new Lap { Whole = true };
        _closing = null;
        _closedIndex = null;
        _startedAt = null;
        _base = 0;
        _clocked = true;
        _pendingBase = null;
        _lastIndex = null;
        _lastRaw = 0;
    }

    /// <summary>A new race: nothing is known about the lap, or the run's clock, until the drone is at the start.</summary>
    public void NewRace()
    {
        _lap = new Lap { Whole = false };
        _closing = null;
        _closedIndex = null;
        _startedAt = null;
        _finished = null;
        _base = 0;
        _clocked = false;
        _pendingBase = null;
        _lastIndex = null;
        _lastRaw = 0;
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

    // ── The laps for the review ─────────────────────────────────────────────

    /// <summary>
    /// The lap under way was given up: the run started again. It's kept for the review when it
    /// was flown from the line and got through a gate, so the stretches before the reset show.
    /// </summary>
    private void GaveUp(Lap lap)
    {
        if (lap.Gates.Count == 0 || !lap.Whole || !lap.Clocked || lap.Broken)
            return;
        Record(lap, null, LapOutcome.Timed);
    }

    /// <summary>A lap finished, with its time, or an attempt given up, without: the newest in <see cref="Laps"/>.</summary>
    private void Record(Lap lap, int? lapMs, LapOutcome outcome)
    {
        _laps.Add(new FlownLap
        {
            Number = ++_attempts,
            LapMs = lapMs,
            Gates = new List<string>(lap.Gates),
            Times = new List<int>(lap.Times),
            Measured = lap.Whole && lap.Clocked && !lap.Broken,
            Outcome = outcome,
        });
        if (_laps.Count > KeptLaps)
            _laps.RemoveAt(0);
        LapsVersion++;
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
        if (reference == null || !_lap.OnLayout || !_lap.Clocked)
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
        if (reference != null && lap.Clocked && lap.OnLayout && lap.Gates.Count == reference.Gates.Count)
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
        // A sector per stretch is a stretch: its best is the stretch's.
        List<int?>? bests = ends.Length == gates + 1 ? _course.Segments
            : _course.Sectors.TryGetValue(count, out var kept) ? kept : null;
        for (var i = 0; i < ends.Length; i++)
        {
            var end = ends[i];
            var from = i > 0 ? ends[i - 1] : -1;
            var refMs = At(reference.Times, end, reference.LapMs, gates) - At(reference.Times, from, 0, gates);
            var done = end < gates ? end < times.Count : lapMs != null;
            if (!done)
            {
                sectors.Add(new SectorView(null, null, SectorMark.Pending, refMs));
                continue;
            }
            var ms = At(times, end, lapMs.GetValueOrDefault(), gates) - At(times, from, 0, gates);
            var best = bests != null && i < bests.Count ? bests[i] : null;
            var mark = best != null && ms <= best ? SectorMark.Best : ms < refMs ? SectorMark.Faster : SectorMark.Slower;
            sectors.Add(new SectorView(ms, ms - refMs, mark, refMs));
        }
        return sectors;
    }

    // ── Learning ────────────────────────────────────────────────────────────

    private LapOutcome Learn(Lap lap, int lapMs)
    {
        if (!lap.Whole || !lap.Clocked || lap.Broken)
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
    /// last ends at the line, given as the gate count. Fewer sectors when there are fewer
    /// stretches, so <see cref="EveryGate"/> gives each stretch its own.
    /// </summary>
    public static int[] SectorEnds(int gates, int count)
    {
        var stretches = gates + 1;
        var n = Math.Max(0, Math.Min(count, stretches));
        var ends = new int[n];
        for (var i = 0; i < n - 1; i++)
            ends[i] = (int)Math.Round((i + 1) * (double)stretches / n, MidpointRounding.AwayFromZero) - 1;
        if (n > 0)
            ends[n - 1] = gates;
        return ends;
    }

    /// <summary>The time into the lap at gate <paramref name="index"/>: zero before the first, the lap's time at the line.</summary>
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
