using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using JmtLiftoffLeaderboard.Site;
using Newtonsoft.Json;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// Gate splits waiting to go to the JMT site, kept on disk so a lap flown with the site
/// unreachable, or the game closed before it went, still gets there. A week at most:
/// long enough for a pilot to claim their game id, after which the site takes them.
/// </summary>
internal sealed class SplitOutbox
{
    public const int MaxEntries = 500;
    private static readonly TimeSpan Keep = TimeSpan.FromDays(7);

    private static readonly JsonSerializerSettings Json = new()
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        Formatting = Formatting.Indented,
    };

    internal sealed class Entry
    {
        [JsonProperty("split")] public SplitUpload Split = new();
        [JsonProperty("queued_at")] public DateTimeOffset QueuedAt;
        [JsonProperty("retry_at")] public DateTimeOffset? RetryAt;

        /// <summary>Sent on its own: a batch it was in was refused, and it may be why.</summary>
        [JsonProperty("alone")] public bool Alone;
    }

    private readonly List<Entry> _entries = new();

    public SplitOutbox()
    {
        Load();
    }

    private static string FilePath =>
        Path.Combine(Path.Combine(Paths.ConfigPath, "JmtLiftoffLeaderboard"), "splits-outbox.json");

    public int Count => _entries.Count;

    public void Add(SplitUpload split)
    {
        _entries.Add(new Entry { Split = split, QueuedAt = DateTimeOffset.UtcNow });
        Trim();
        Save();
    }

    /// <summary>What can go now: up to <paramref name="max"/> of them, or one that has to go on its own.</summary>
    public List<Entry> Due(DateTimeOffset now, int max)
    {
        if (Trim())
            Save();
        var due = _entries.Where(e => e.RetryAt == null || e.RetryAt <= now).ToList();
        var alone = due.FirstOrDefault(e => e.Alone);
        return alone != null ? new List<Entry> { alone } : due.Take(max).ToList();
    }

    public void Remove(IEnumerable<Entry> done)
    {
        var gone = false;
        foreach (var entry in done.ToList())
            gone |= _entries.Remove(entry);
        if (gone)
            Save();
    }

    public void Later(IEnumerable<Entry> entries, DateTimeOffset at)
    {
        foreach (var entry in entries)
            entry.RetryAt = at;
        Save();
    }

    /// <summary>A batch was refused: each goes on its own next, so only the one at fault is lost.</summary>
    public void SendAlone(IEnumerable<Entry> entries)
    {
        foreach (var entry in entries)
            entry.Alone = true;
        Save();
    }

    private bool Trim()
    {
        var before = _entries.Count;
        var oldest = DateTimeOffset.UtcNow - Keep;
        _entries.RemoveAll(e => e.QueuedAt < oldest);
        if (_entries.Count > MaxEntries)
            _entries.RemoveRange(0, _entries.Count - MaxEntries);
        return _entries.Count != before;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;
            var kept = JsonConvert.DeserializeObject<List<Entry>>(File.ReadAllText(FilePath, Encoding.UTF8), Json);
            if (kept != null)
                _entries.AddRange(kept.Where(e => e?.Split != null && e.Split.ClientRef.Length > 0));
            if (_entries.Count > 0)
                Plugin.Log.LogInfo($"HUD: {_entries.Count} lap(s) of gate splits waiting to go to the JMT site.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"HUD: couldn't read the gate splits waiting to be sent, so they're dropped: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            JsonFile.Write(FilePath, JsonConvert.SerializeObject(_entries, Json));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"HUD: couldn't save the gate splits waiting to be sent: {ex.Message}");
        }
    }
}
