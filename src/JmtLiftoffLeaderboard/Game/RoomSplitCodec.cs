using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>One lap's gate times as they travel through the room.</summary>
internal sealed class RoomSplit
{
    public int Seq;
    public int LapMs;
    public int? PrevLapMs;
    public List<int> Times = new();
}

/// <summary>
/// How a lap's gate times are put on the pilot's Photon player, for the room's race mod to
/// read: two custom properties, set together once per lap.
///
/// <c>JMTG</c> is the course's gates (<c>string[]</c>, the checkpoints' ids in the order flown),
/// sent only when they change or the room does. <c>JMTS</c> is the lap (<c>int[]</c>):
/// <c>{format, seq, gatesHash, lap_ms, prev_lap_ms (0 for none), t1..tn}</c>, one time per gate,
/// in milliseconds into the lap. The hash ties a lap to the gates it was flown through, so a
/// reader holding an older <c>JMTG</c> can tell. JmtLiftoffMod has the same code, from the same
/// description in its docs/server-protocol.md: the two must agree to the bit.
/// </summary>
internal static class RoomSplitCodec
{
    public const string GatesKey = "JMTG";
    public const string SplitsKey = "JMTS";
    public const int Format = 1;

    /// <summary>An hour: longer is no lap.</summary>
    public const int MaxLapMs = 3_600_000;

    private const int Header = 5;

    /// <summary>
    /// FNV-1a, 32 bits, over the ids' UTF-8 joined by newlines, as a signed int: Photon carries
    /// no unsigned one. <c>["a", "b"]</c> is <c>0x28E4C710</c>.
    /// </summary>
    public static int GatesHash(IReadOnlyList<string> gates)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var b in Encoding.UTF8.GetBytes(string.Join("\n", gates)))
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return (int)hash;
        }
    }

    /// <summary>Whether the room's race mod would take this lap: nothing it would refuse is sent.</summary>
    public static bool CanShare(IReadOnlyList<string> gates, IReadOnlyList<int> times, int lapMs) =>
        lapMs <= MaxLapMs && SplitRules.ValidForUpload(gates, times, lapMs);

    public static int[] Encode(int seq, IReadOnlyList<string> gates, IReadOnlyList<int> times, int lapMs, int? prevLapMs)
    {
        var data = new int[Header + times.Count];
        data[0] = Format;
        data[1] = seq;
        data[2] = GatesHash(gates);
        data[3] = lapMs;
        data[4] = prevLapMs is > 0 and <= MaxLapMs ? prevLapMs.Value : 0;
        for (var i = 0; i < times.Count; i++)
            data[Header + i] = times[i];
        return data;
    }

    /// <summary>A lap read back, checked the way the room's race mod checks it.</summary>
    public static bool TryDecode(int[]? data, IReadOnlyList<string>? gates, out RoomSplit? split)
    {
        split = null;
        if (data == null || gates == null || data.Length <= Header || data[0] != Format)
            return false;
        if (data.Length - Header != gates.Count || data[2] != GatesHash(gates))
            return false;
        var lapMs = data[3];
        var times = data.Skip(Header).ToList();
        if (!CanShare(gates, times, lapMs))
            return false;
        split = new RoomSplit
        {
            Seq = data[1],
            LapMs = lapMs,
            PrevLapMs = data[4] is > 0 and <= MaxLapMs ? data[4] : null,
            Times = times,
        };
        return true;
    }
}
