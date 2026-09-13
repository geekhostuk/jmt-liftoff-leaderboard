using System;
using System.Collections.Generic;
using System.Linq;

// Pure: no Unity, no game, so a scratch harness can compile it on its own.
namespace JmtLiftoffLeaderboard.Game;

/// <summary>A pilot in the room as Photon has them: laps flown since this client joined.</summary>
internal sealed class RacePilotIn
{
    public string Name = "";

    /// <summary>Their JMT id, when the site has said who they are.</summary>
    public string? PublicId;

    public bool IsLocal;
    public bool InRoom = true;

    /// <summary>Taking part in the race rather than watching it, or null when the game hasn't said.</summary>
    public bool? Taking;

    public int Laps;
    public int? BestMs;
    public int? LastMs;
}

/// <summary>A pilot on the room's timing screen on the JMT site: this race, from its start.</summary>
internal sealed class RaceSiteIn
{
    public string PublicId = "";
    public string Name = "";
    public bool InRoom;
    public int Laps;
    public int? BestMs;
    public int? LastMs;
    public int Failed;
}

internal sealed class RaceRow
{
    /// <summary>Null for a pilot with no lap yet.</summary>
    public int? Position;

    public string Name = "";
    public bool IsMe;
    public bool InRoom;
    public int Laps;
    public int? BestMs;
    public int? LastMs;

    /// <summary>Off the quickest lap of the race. Null for the leader and anyone with no lap.</summary>
    public int? GapMs;

    /// <summary>Failed attempts, from the site; null when it has nothing to say.</summary>
    public int? Failed;

    /// <summary>Holds the quickest lap of the race.</summary>
    public bool Fastest;
}

/// <summary>
/// The race in the room, put together from the two things that know about it: Photon, which
/// has every lap the moment it's flown but only since this client joined, and the JMT
/// site, which has the whole race and the failed attempts but hears about each lap up to a
/// quarter of a minute later. It's ranked the way the site's timing screen ranks it: by
/// best lap, then the pilots with no lap yet, by name.
/// </summary>
internal static class RaceMath
{
    public static List<RaceRow> Merge(IEnumerable<RacePilotIn> room, IEnumerable<RaceSiteIn>? site, string? me)
    {
        var pilots = room.ToList();
        var unmatched = site?.ToList() ?? new List<RaceSiteIn>();
        // A room whose game never says who's taking part can't hide those watching.
        var anyTaking = pilots.Any(p => p.Taking != null);
        var rows = new List<RaceRow>();

        foreach (var pilot in pilots)
        {
            var match = pilot.PublicId != null ? unmatched.FirstOrDefault(s => s.PublicId == pilot.PublicId) : null;
            match ??= unmatched.FirstOrDefault(s => SameName(s.Name, pilot.Name));
            if (match != null)
                unmatched.Remove(match);

            var best = Min(pilot.BestMs, match?.BestMs);
            var laps = Math.Max(pilot.Laps, match?.Laps ?? 0);
            if (best == null && laps == 0)
            {
                // No lap: shown only while they're in it, and flying rather than watching or the room's bot.
                if (!pilot.InRoom || (anyTaking && pilot.Taking != true && !pilot.IsLocal))
                    continue;
            }
            rows.Add(new RaceRow
            {
                Name = pilot.Name,
                IsMe = pilot.IsLocal,
                InRoom = pilot.InRoom,
                Laps = laps,
                BestMs = best,
                LastMs = pilot.LastMs ?? match?.LastMs,
                Failed = match?.Failed,
            });
        }

        // Pilots the site has from before this client joined, who have since left. The
        // site's roster also lists everyone in the room with no lap, the bot among them:
        // those are Photon's to show.
        foreach (var row in unmatched)
        {
            if (row.BestMs == null)
                continue;
            rows.Add(new RaceRow
            {
                Name = row.Name,
                IsMe = me != null && row.PublicId == me,
                InRoom = false,
                Laps = row.Laps,
                BestMs = row.BestMs,
                LastMs = row.LastMs,
                Failed = row.Failed,
            });
        }

        var timed = rows.Where(r => r.BestMs != null)
            .OrderBy(r => r.BestMs)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (var i = 0; i < timed.Count; i++)
        {
            timed[i].Position = i + 1;
            timed[i].GapMs = i == 0 ? null : timed[i].BestMs - timed[0].BestMs;
            timed[i].Fastest = i == 0;
        }
        var untimed = rows.Where(r => r.BestMs == null).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
        return timed.Concat(untimed).ToList();
    }

    /// <summary>At most <paramref name="max"/> rows: the top ones, with the pilot's own in the last place when they're further down.</summary>
    public static List<RaceRow> Visible(List<RaceRow> rows, int max)
    {
        if (rows.Count <= max)
            return rows;
        var me = rows.FindIndex(r => r.IsMe);
        if (me < max)
            return rows.Take(max).ToList();
        var shown = rows.Take(max - 1).ToList();
        shown.Add(rows[me]);
        return shown;
    }

    private static int? Min(int? a, int? b) => a == null ? b : b == null ? a : Math.Min(a.Value, b.Value);

    private static bool SameName(string a, string b) =>
        a.Trim().Length > 0 && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
