using System;
using System.Collections.Generic;
using System.Linq;
using JmtLiftoffLeaderboard.Site;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>A place on the board as the HUD shows it: the site's, moved for a lap flown here that the site hasn't got yet.</summary>
internal sealed class BoardPlace
{
    public BoardPlace(int position, string name, int lapMs, bool isMe, bool here)
    {
        Position = position;
        Name = name;
        LapMs = lapMs;
        IsMe = isMe;
        Here = here;
    }

    public int Position { get; }
    public string Name { get; }
    public int LapMs { get; }
    public bool IsMe { get; }

    /// <summary>In the pilot's room right now.</summary>
    public bool Here { get; }
}

/// <summary>The board around the pilot, ready to draw.</summary>
internal sealed class BoardView
{
    public string Title = "";
    public int PilotCount;

    /// <summary>Top to bottom: the places above theirs, theirs, and those below.</summary>
    public List<BoardPlace> Places = new();

    /// <summary>Null for a pilot not on the board, even counting tonight.</summary>
    public BoardPlace? Me;

    /// <summary>The place to take next: the nearest above, or for a pilot not on the board, its last.</summary>
    public BoardPlace? Next;

    /// <summary>The nearest below.</summary>
    public BoardPlace? Chaser;

    /// <summary>Their place stands on a lap flown here that the site doesn't have (yet, or ever, in practice).</summary>
    public bool Provisional;

    public int? LastLapMs;
    public int? BestTonightMs;

    /// <summary>Tonight's laps on this course, oldest first.</summary>
    public IReadOnlyList<int> Recent = Array.Empty<int>();
}

/// <summary>A lap that moved the pilot on the board, as far as the HUD can tell.</summary>
internal readonly struct BoardNews
{
    public BoardNews(int lapMs, int position, int placesGained, bool joined)
    {
        LapMs = lapMs;
        Position = position;
        PlacesGained = placesGained;
        Joined = joined;
    }

    public int LapMs { get; }
    public int Position { get; }
    public int PlacesGained { get; }

    /// <summary>Their first lap on this board.</summary>
    public bool Joined { get; }
}

/// <summary>
/// Where a pilot stands on a board once tonight's best is counted, by the site's rule: best
/// lap first, and of two equal laps the earlier one ahead, so a lap only takes a place by
/// being quicker. Pure, so it can be checked without the game.
/// </summary>
internal static class BoardMath
{
    /// <summary>
    /// The places around <paramref name="me"/>: <paramref name="above"/> over theirs and
    /// <paramref name="below"/> under, or the board's last <paramref name="above"/> for a
    /// pilot with no lap on it. <paramref name="board"/> holds the site's rows, which
    /// needn't be the whole board, only the places around theirs.
    /// </summary>
    public static BoardView Arrange(Leaderboard board, string? me, int? bestTonight, int above, int below, Func<string, bool> here)
    {
        var rows = board.Rows.OrderBy(r => r.Position).ToList();
        var mine = me == null ? null : rows.FirstOrDefault(r => r.Pilot.PublicId == me);

        int? best = mine?.LapMs;
        var provisional = false;
        if (bestTonight is { } tonight && (best == null || tonight < best))
        {
            best = tonight;
            provisional = true;
        }

        var view = new BoardView
        {
            Title = board.Title,
            PilotCount = board.PilotCount + (mine == null && best != null ? 1 : 0),
            Provisional = provisional,
        };

        // Everyone else, where they'd stand with this pilot's best among them: one place
        // down for each who was ahead and is now slower.
        var places = new List<BoardPlace>();
        foreach (var row in rows)
        {
            if (row == mine)
                continue;
            var position = row.Position;
            if (best != null && row.LapMs > best && (mine == null || row.Position < mine.Position))
                position++;
            places.Add(new BoardPlace(position, row.Pilot.Name, row.LapMs, false, here(row.Pilot.PublicId)));
        }

        if (best == null)
        {
            // Not on the board: its last few places, the easiest to take.
            view.Places = places.Skip(Math.Max(0, places.Count - above)).ToList();
            view.Next = view.Places.LastOrDefault();
            return view;
        }

        int myPosition;
        if (!provisional)
        {
            myPosition = mine!.Position;
        }
        else
        {
            // Behind the last who is at least as quick; failing that, in the place of the
            // first who is slower, which the rows may start below if the lap jumped far.
            var ahead = places.Where(p => p.LapMs <= best).ToList();
            var behind = places.Where(p => p.LapMs > best).ToList();
            myPosition = ahead.Count > 0 ? ahead.Max(p => p.Position) + 1
                : behind.Count > 0 ? behind.Min(p => p.Position) - 1
                : 1;
        }

        var mePlace = new BoardPlace(myPosition, "You", best.Value, true, false);
        places.Add(mePlace);
        places.Sort((a, b) => a.Position != b.Position ? a.Position.CompareTo(b.Position)
            : a.IsMe == b.IsMe ? 0
            : a.IsMe ? -1 : 1);

        var i = places.IndexOf(mePlace);
        var start = Math.Max(0, i - above);
        var end = Math.Min(places.Count - 1, i + below);
        view.Places = places.GetRange(start, end - start + 1);
        view.Me = mePlace;
        view.Next = i > 0 ? places[i - 1] : null;
        view.Chaser = i + 1 < places.Count ? places[i + 1] : null;
        return view;
    }
}
