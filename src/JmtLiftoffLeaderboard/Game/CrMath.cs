using System;
using System.Collections.Generic;
using JmtLiftoffLeaderboard.Site;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>A pilot's place on the license ladder, and the next step up.</summary>
internal sealed class Rung
{
    public Rung(string name, string? nextName = null, double? nextAt = null, int? nextLaps = null)
    {
        Name = name;
        NextName = nextName;
        NextAt = nextAt;
        NextLaps = nextLaps;
    }

    public string Name { get; }

    /// <summary>The license above and the CR that earns it. Null at the top, and for a Rookie, whose next step is laps.</summary>
    public string? NextName { get; }
    public double? NextAt { get; }

    /// <summary>Laps the next step also asks for.</summary>
    public int? NextLaps { get; }
}

/// <summary>A CR, what it was taken over, and the license it earns.</summary>
internal sealed class Standing
{
    public Standing(int failed, int laps, double cr, Rung rung)
    {
        Failed = failed;
        Laps = laps;
        Cr = cr;
        Rung = rung;
    }

    public int Failed { get; }
    public int Laps { get; }
    public double Cr { get; }
    public Rung Rung { get; }
}

/// <summary>What stands between a pilot and the next license, and how close the one they hold is to slipping.</summary>
internal sealed class Forecast
{
    public Forecast(int? cleanLaps, int? crashCost, int? crashesToDrop, string? dropsTo)
    {
        CleanLaps = cleanLaps;
        CrashCost = crashCost;
        CrashesToDrop = crashesToDrop;
        DropsTo = dropsTo;
    }

    /// <summary>Clean laps from now that earn the next license; for a Rookie, laps of any kind. Null at the top, or when it's out of sight.</summary>
    public int? CleanLaps { get; }

    /// <summary>How many more of those one failed attempt would add.</summary>
    public int? CrashCost { get; }

    /// <summary>Failed attempts in a row that would lose the license held, when that's a handful, and the one they'd drop to.</summary>
    public int? CrashesToDrop { get; }
    public string? DropsTo { get; }
}

/// <summary>
/// The site's Consistency Rating and licenses (api/app/services/tiers.py, and the window
/// in services/timing.py), with the numbers the site sends, so a CR worked out here is the
/// one the site will show once the room's panel has sent the same laps. Change it there
/// first.
/// </summary>
internal static class CrMath
{
    public const string Rookie = "Rookie";
    private const string Pro = "Pro";
    private const int LookAhead = 1000;
    private const int DropLookAhead = 3;

    /// <summary>CR = max · m / (m + fpl), failed attempts per lap taken with the prior laps flown at the midpoint.</summary>
    public static double Rating(CrModel m, int failed, int laps)
    {
        var seen = laps + m.PriorLaps;
        var fpl = seen > 0 ? (failed + m.MidpointFpl * m.PriorLaps) / seen : m.MidpointFpl;
        return Math.Round(m.Max * m.MidpointFpl / (m.MidpointFpl + fpl), 2);
    }

    public static Rung License(CrModel m, double cr, int laps)
    {
        if (laps < m.LicenseMinLaps)
            return new Rung(Rookie, nextLaps: m.LicenseMinLaps);
        var ladder = m.Licenses;
        for (var i = 0; i < ladder.Count; i++)
        {
            var rung = ladder[i];
            if (rung.Name == Pro && laps < m.ProMinLaps)
                continue;
            if (cr < rung.Floor)
                continue;
            if (i == 0)
                return new Rung(rung.Name);
            var above = ladder[i - 1];
            return new Rung(rung.Name, above.Name, above.Floor,
                above.Name == Pro && laps < m.ProMinLaps ? m.ProMinLaps : null);
        }
        return new Rung(ladder.Count > 0 ? ladder[ladder.Count - 1].Name : Rookie);
    }

    /// <summary>How far up the ladder: Rookie 0, then one step per license.</summary>
    public static int Rank(CrModel m, string name)
    {
        var index = m.Licenses.FindIndex(rung => rung.Name == name);
        return index < 0 ? 0 : m.Licenses.Count - index;
    }

    /// <summary>The CR a license starts at.</summary>
    public static double Floor(CrModel m, string name) =>
        m.Licenses.Find(rung => rung.Name == name)?.Floor ?? 0;

    /// <summary>Failed attempts and laps over whole races, newest first, until the window is covered.</summary>
    public static (int Failed, int Laps) Sample(CrModel m, IReadOnlyList<CrRace> newestFirst)
    {
        int failed = 0, laps = 0;
        foreach (var race in newestFirst)
        {
            if (laps >= m.WindowLaps)
                break;
            laps += race.Laps;
            failed += race.Failed;
        }
        return (failed, laps);
    }

    public static Standing Stand(CrModel m, IReadOnlyList<CrRace> newestFirst)
    {
        var (failed, laps) = Sample(m, newestFirst);
        var cr = Rating(m, failed, laps);
        return new Standing(failed, laps, cr, License(m, cr, laps));
    }

    /// <summary>
    /// Flies the laps ahead: clean ones added to the race under way (the first of
    /// <paramref name="newestFirst"/>) until the license goes up, and failed attempts until
    /// it would come down. Old races fall out of the window as new laps come in, exactly as
    /// on the site, which is why this is flown lap by lap rather than solved.
    /// </summary>
    public static Forecast Forecast(CrModel m, IReadOnlyList<CrRace> newestFirst, Standing now)
    {
        var rank = Rank(m, now.Rung.Name);
        var atTop = now.Rung.Name != Rookie && now.Rung.NextName == null;
        var clean = atTop ? null : LapsAbove(m, newestFirst, rank, 0);
        int? cost = null;
        if (clean != null && now.Rung.Name != Rookie && LapsAbove(m, newestFirst, rank, 1) is { } worse)
            cost = worse - clean.Value;
        var (drops, to) = CrashesToDrop(m, newestFirst, rank);
        return new Forecast(clean, cost, drops, to);
    }

    private static int? LapsAbove(CrModel m, IReadOnlyList<CrRace> newestFirst, int rank, int extraFailed)
    {
        var trial = Copy(newestFirst);
        trial[0].Failed += extraFailed;
        for (var n = 0; n <= LookAhead; n++)
        {
            if (n > 0)
                trial[0].Laps++;
            if (Rank(m, Stand(m, trial).Rung.Name) > rank)
                return n;
        }
        return null;
    }

    private static (int? Crashes, string? To) CrashesToDrop(CrModel m, IReadOnlyList<CrRace> newestFirst, int rank)
    {
        var trial = Copy(newestFirst);
        for (var k = 1; k <= DropLookAhead; k++)
        {
            trial[0].Failed++;
            var rung = Stand(m, trial).Rung;
            if (Rank(m, rung.Name) < rank)
                return (k, rung.Name);
        }
        return (null, null);
    }

    private static List<CrRace> Copy(IReadOnlyList<CrRace> races)
    {
        var copy = new List<CrRace>(races.Count + 1);
        foreach (var race in races)
            copy.Add(new CrRace { Failed = race.Failed, Laps = race.Laps });
        if (copy.Count == 0)
            copy.Add(new CrRace());
        return copy;
    }
}
