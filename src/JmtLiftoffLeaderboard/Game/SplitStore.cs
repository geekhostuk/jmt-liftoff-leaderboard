using System;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Newtonsoft.Json;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// Each course's splits, one JSON file per course in the plugin's own folder under
/// BepInEx/config. Only this computer ever sees them.
/// </summary>
internal static class SplitStore
{
    private static readonly JsonSerializerSettings Json = new()
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Include,
        Formatting = Formatting.Indented,
    };

    private static string Folder => Path.Combine(Path.Combine(Paths.ConfigPath, "JmtLiftoffLeaderboard"), "splits");

    /// <summary>
    /// A file name for the course: its Workshop id when it has one, else its names, with a
    /// hash of them so two courses whose names read the same once cleaned up stay apart.
    /// </summary>
    public static string Key(TrackRef track)
    {
        if (track.BoardId is > 0 and var id)
            return $"workshop-{id}";
        var names = $"{track.Name}|{track.TrackName}|{string.Join("/", track.Environments)}";
        return $"{Slug(track.Name)}-{Slug(track.Environments.FirstOrDefault() ?? "")}-{Fnv(names):x8}";
    }

    public static CourseSplits Load(string key, string name)
    {
        var path = PathFor(key);
        try
        {
            if (File.Exists(path))
            {
                var course = JsonConvert.DeserializeObject<CourseSplits>(File.ReadAllText(path, Encoding.UTF8), Json);
                if (course != null && course.Version == CourseSplits.CurrentVersion)
                {
                    course.Name = name;
                    if (Sound(course))
                        return course;
                    Plugin.Log.LogWarning($"HUD: your splits for {name} don't add up, so it starts again.");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"HUD: couldn't read your splits for {name}, so it starts again: {ex.Message}");
        }
        return new CourseSplits { Name = name };
    }

    /// <summary>Written beside the old file and swapped in, so a crash mid-write never loses the best lap.</summary>
    public static void Save(string key, CourseSplits course)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = PathFor(key);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(course, Json), Encoding.UTF8);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"HUD: couldn't save your splits for {course.Name}: {ex.Message}");
        }
    }

    private static string PathFor(string key) => Path.Combine(Folder, key + ".json");

    /// <summary>
    /// Whether a saved best lap is one: a time at every gate, each later than the last and all
    /// inside the lap. Plugin builds before 0.3.0 was released could save a lap's gates on the
    /// run's clock rather than the lap's, and a lap like that would put every lap after it
    /// seconds ahead.
    /// </summary>
    private static bool Sound(CourseSplits course)
    {
        var best = course.Best;
        if (best == null)
            return course.Gates.Count == 0;
        if (best.LapMs <= 0 || best.Gates.Count != best.Times.Count || best.Gates.Count != course.Gates.Count)
            return false;
        var previous = 0;
        foreach (var ms in best.Times)
        {
            if (ms <= previous || ms >= best.LapMs)
                return false;
            previous = ms;
        }
        return true;
    }

    private static string Slug(string text)
    {
        var slug = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                slug.Append(c);
            else if (slug.Length > 0 && slug[slug.Length - 1] != '_')
                slug.Append('_');
            if (slug.Length >= 40)
                break;
        }
        return slug.ToString().Trim('_');
    }

    /// <summary>FNV-1a: stable across runs and machines, unlike string.GetHashCode.</summary>
    private static uint Fnv(string text)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 16777619u;
        }
        return hash;
    }
}
