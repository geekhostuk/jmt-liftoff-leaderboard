using System;
using System.Collections.Generic;

// Pure, like SplitMath: no Unity, no game, so a scratch harness can compile it on its own.
namespace JmtLiftoffLeaderboard.Game;

/// <summary>A lap flown on a course this session, or an attempt reset part way: what the lap review lists.</summary>
internal sealed class FlownLap
{
    /// <summary>Counts the course's laps and attempts this session, from 1.</summary>
    public int Number;

    /// <summary>The lap's time from the game, or null for an attempt reset part way.</summary>
    public int? LapMs;

    /// <summary>The passages it went through, in order, and the time into the lap at each.</summary>
    public List<string> Gates = new();
    public List<int> Times = new();

    /// <summary>Seen from its start on the run's clock: its gates are all of them, and their times are the lap's.</summary>
    public bool Measured;

    public LapOutcome Outcome;

    public bool Finished => LapMs != null;
}

/// <summary>What the lap review compares a lap with.</summary>
internal enum ReviewCompare
{
    /// <summary>The best lap ever flown on the course.</summary>
    BestEver,
    /// <summary>The best since the game started.</summary>
    Tonight,
    /// <summary>The best time for every stretch, flown as one lap.</summary>
    Possible,
    /// <summary>The last clean lap before it.</summary>
    LapBefore,
}

/// <summary>How a stretch went against the pilot's best for it.</summary>
internal enum StretchTone
{
    None,
    /// <summary>As quick as they've ever flown it.</summary>
    Best,
    /// <summary>Within 3% of it, or 40 ms on a short stretch.</summary>
    Close,
    /// <summary>Within 8%, or 120 ms.</summary>
    Off,
    Lost,
}

/// <summary>One stretch of a lap: from the line or a gate to the next gate, or from the last gate to the line.</summary>
internal sealed class StretchLine
{
    /// <summary>The stretch ending at gate <c>Index</c>, counting from 0; the gate count is the one to the line.</summary>
    public int Index;

    /// <summary>The time into the lap at its end.</summary>
    public int? SplitMs;

    public int? Ms;

    /// <summary>The same stretch on the lap compared with.</summary>
    public int? RefMs;

    /// <summary>Against that stretch: negative is quicker.</summary>
    public int? DeltaMs;

    /// <summary>The delta at its end: how far ahead or behind the lap was there.</summary>
    public int? RunningMs;

    /// <summary>The pilot's best ever for it.</summary>
    public int? BestMs;

    public bool Flown => Ms != null;
}

/// <summary>A lap taken apart, stretch by stretch, against the lap it's compared with.</summary>
internal sealed class LapBreakdown
{
    /// <summary>Why it can't be taken apart, when it can't.</summary>
    public string? Problem;

    public List<StretchLine> Stretches = new();

    /// <summary>There's a lap to compare it with.</summary>
    public bool Compared;

    /// <summary>It is the lap it's compared with.</summary>
    public bool IsReference;

    /// <summary>Ahead or behind at the line, or at the last gate before a reset.</summary>
    public int? DeltaMs;

    /// <summary>The stretches that lost the most time, worst first.</summary>
    public List<int> Worst = new();

    /// <summary>The stretch that gained the most, if any did.</summary>
    public int? BestGain;
}

/// <summary>One stretch over the last few clean laps, each against the pilot's best for it.</summary>
internal sealed class GateTrend
{
    public int Index;
    public int? BestMs;

    /// <summary>What each lap lost there, oldest lap first.</summary>
    public List<int> Lost = new();

    public int? AvgLostMs;
}

/// <summary>
/// The lap review's arithmetic: a lap's stretches against another lap's, the lap made of the
/// pilot's best stretches, and where the last few clean laps lose their time.
/// </summary>
internal static class ReviewMath
{
    public const int WorstShown = 3;

    /// <summary>A whole lap through the course's gates as it has them now.</summary>
    public static bool Clean(FlownLap lap, CourseSplits course) =>
        lap.Finished && lap.Measured && Same(lap.Gates, course.Gates);

    /// <summary>Its gates can be set beside the course's: all of them for a lap, the first few for an attempt reset part way.</summary>
    public static bool Comparable(FlownLap lap, CourseSplits course) =>
        lap.Measured && (lap.Finished ? Same(lap.Gates, course.Gates) : StartsWith(course.Gates, lap.Gates));

    /// <summary>The last <paramref name="count"/> clean laps, oldest first.</summary>
    public static List<FlownLap> Recent(IReadOnlyList<FlownLap> laps, CourseSplits course, int count)
    {
        var recent = new List<FlownLap>();
        for (var i = laps.Count - 1; i >= 0 && recent.Count < count; i--)
        {
            if (Clean(laps[i], course))
                recent.Add(laps[i]);
        }
        recent.Reverse();
        return recent;
    }

    /// <summary>The pilot's best time for every stretch, as one lap: null until every stretch has one.</summary>
    public static LapSplits? Possible(CourseSplits course)
    {
        var gates = course.Gates.Count;
        if (course.Best == null || course.Segments.Count != gates + 1)
            return null;
        var lap = new LapSplits { Gates = new List<string>(course.Gates) };
        var sum = 0;
        for (var k = 0; k <= gates; k++)
        {
            if (course.Segments[k] is not { } ms)
                return null;
            sum += ms;
            if (k < gates)
                lap.Times.Add(sum);
        }
        lap.LapMs = sum;
        return lap;
    }

    /// <summary>The last clean lap flown before <paramref name="lap"/>.</summary>
    public static FlownLap? LapBefore(IReadOnlyList<FlownLap> laps, FlownLap lap, CourseSplits course)
    {
        for (var i = IndexOf(laps, lap) - 1; i >= 0; i--)
        {
            if (Clean(laps[i], course))
                return laps[i];
        }
        return null;
    }

    public static LapSplits Splits(FlownLap lap) =>
        new() { Gates = new List<string>(lap.Gates), Times = new List<int>(lap.Times), LapMs = lap.LapMs.GetValueOrDefault() };

    public static int IndexOf(IReadOnlyList<FlownLap> laps, FlownLap lap)
    {
        for (var i = 0; i < laps.Count; i++)
        {
            if (laps[i] == lap)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// <paramref name="lap"/> stretch by stretch against <paramref name="reference"/>, which must
    /// go through the course's gates; any other is as good as none. An attempt reset part way
    /// has the stretches it flew.
    /// </summary>
    public static LapBreakdown Breakdown(FlownLap lap, CourseSplits course, LapSplits? reference)
    {
        var breakdown = new LapBreakdown();
        if (!lap.Measured)
        {
            breakdown.Problem = "This lap wasn't seen from its start, so its gates weren't all timed.";
            return breakdown;
        }
        if (!Comparable(lap, course))
        {
            breakdown.Problem = "This lap went through other gates than the course has now.";
            return breakdown;
        }

        var gates = course.Gates.Count;
        if (reference != null && (!Same(reference.Gates, course.Gates) || reference.Times.Count != gates))
            reference = null;
        breakdown.Compared = reference != null;
        breakdown.IsReference = reference != null && lap.LapMs == reference.LapMs && Same(lap.Times, reference.Times);

        for (var k = 0; k <= gates; k++)
        {
            var line = new StretchLine { Index = k, BestMs = k < course.Segments.Count ? course.Segments[k] : null };
            breakdown.Stretches.Add(line);
            var flown = k < gates ? k < lap.Times.Count : lap.Finished;
            if (!flown)
                continue;
            var split = At(lap.Times, k, lap.LapMs.GetValueOrDefault());
            line.SplitMs = split;
            line.Ms = split - At(lap.Times, k - 1, 0);
            if (reference == null)
                continue;
            var refSplit = At(reference.Times, k, reference.LapMs);
            line.RefMs = refSplit - At(reference.Times, k - 1, 0);
            line.DeltaMs = line.Ms - line.RefMs;
            line.RunningMs = split - refSplit;
            breakdown.DeltaMs = line.RunningMs;
        }

        if (!breakdown.Compared || breakdown.IsReference)
            return breakdown;
        var losses = breakdown.Stretches.FindAll(s => s.DeltaMs > 0);
        losses.Sort((a, b) => b.DeltaMs!.Value != a.DeltaMs!.Value ? b.DeltaMs.Value.CompareTo(a.DeltaMs.Value) : a.Index.CompareTo(b.Index));
        for (var i = 0; i < losses.Count && i < WorstShown; i++)
            breakdown.Worst.Add(losses[i].Index);
        foreach (var line in breakdown.Stretches)
        {
            if (line.DeltaMs < 0 && (breakdown.BestGain is not { } gain || line.DeltaMs < breakdown.Stretches[gain].DeltaMs))
                breakdown.BestGain = line.Index;
        }
        return breakdown;
    }

    /// <summary>
    /// Each stretch over <paramref name="clean"/> (clean laps, oldest first): what each lap lost
    /// there against the pilot's best for it, and what they lose there on average.
    /// </summary>
    public static List<GateTrend> Trends(List<FlownLap> clean, CourseSplits course)
    {
        var gates = course.Gates.Count;
        var trends = new List<GateTrend>();
        for (var k = 0; k <= gates; k++)
        {
            var trend = new GateTrend { Index = k, BestMs = k < course.Segments.Count ? course.Segments[k] : null };
            trends.Add(trend);
            if (clean.Count == 0)
                continue;
            var stretches = new List<int>();
            foreach (var lap in clean)
                stretches.Add(At(lap.Times, k, lap.LapMs.GetValueOrDefault()) - At(lap.Times, k - 1, 0));
            // A best that none of these laps is held against: the quickest of them stands in.
            var best = trend.BestMs ?? Min(stretches);
            trend.BestMs = best;
            var sum = 0L;
            foreach (var ms in stretches)
            {
                var lost = Math.Max(0, ms - best);
                trend.Lost.Add(lost);
                sum += lost;
            }
            trend.AvgLostMs = (int)Math.Round(sum / (double)stretches.Count);
        }
        return trends;
    }

    /// <summary>The stretches that lose the most on average, worst first; only those that lose anything.</summary>
    public static List<int> MostLost(List<GateTrend> trends, int count)
    {
        var losing = trends.FindAll(t => t.AvgLostMs > 0);
        losing.Sort((a, b) => b.AvgLostMs!.Value != a.AvgLostMs!.Value ? b.AvgLostMs.Value.CompareTo(a.AvgLostMs.Value) : a.Index.CompareTo(b.Index));
        var most = new List<int>();
        for (var i = 0; i < losing.Count && i < count; i++)
            most.Add(losing[i].Index);
        return most;
    }

    /// <summary>The laps' average time, and how far they spread from it (the standard deviation) once there are two.</summary>
    public static (int MeanMs, int? SpreadMs)? Spread(List<FlownLap> clean)
    {
        if (clean.Count == 0)
            return null;
        var mean = 0d;
        foreach (var lap in clean)
            mean += lap.LapMs.GetValueOrDefault();
        mean /= clean.Count;
        if (clean.Count < 2)
            return ((int)Math.Round(mean), null);
        var squares = 0d;
        foreach (var lap in clean)
        {
            var off = lap.LapMs.GetValueOrDefault() - mean;
            squares += off * off;
        }
        return ((int)Math.Round(mean), (int)Math.Round(Math.Sqrt(squares / clean.Count)));
    }

    /// <summary>How a stretch that lost <paramref name="lostMs"/> against a best of <paramref name="bestMs"/> went.</summary>
    public static StretchTone Tone(int lostMs, int bestMs)
    {
        if (lostMs <= 0)
            return StretchTone.Best;
        if (lostMs <= Math.Max(40, bestMs * 3 / 100))
            return StretchTone.Close;
        if (lostMs <= Math.Max(120, bestMs * 8 / 100))
            return StretchTone.Off;
        return StretchTone.Lost;
    }

    /// <summary>The time into the lap at gate <paramref name="index"/>: zero before the first, the lap's time past the last.</summary>
    private static int At(List<int> times, int index, int lapMs) =>
        index < 0 ? 0 : index >= times.Count ? lapMs : times[index];

    private static int Min(List<int> values)
    {
        var min = int.MaxValue;
        foreach (var value in values)
            min = Math.Min(min, value);
        return min;
    }

    private static bool Same<T>(List<T> a, List<T> b)
    {
        if (a.Count != b.Count)
            return false;
        var equal = EqualityComparer<T>.Default;
        for (var i = 0; i < a.Count; i++)
        {
            if (!equal.Equals(a[i], b[i]))
                return false;
        }
        return true;
    }

    private static bool StartsWith(List<string> list, List<string> prefix)
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
}
