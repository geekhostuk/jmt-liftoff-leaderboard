using System;
using System.Collections.Generic;
using System.Reflection;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>A gate the game has checked off for the local pilot: which passage, on which lap, and the lap timer there.</summary>
internal readonly struct GateInfo
{
    public GateInfo(string id, int lap, float lapSeconds, float at)
    {
        Id = id;
        Lap = lap;
        LapSeconds = lapSeconds;
        At = at;
    }

    /// <summary>The passage's id. One gate can be passed more than once a lap, and each passage has its own.</summary>
    public string Id { get; }

    public int Lap { get; }

    /// <summary>The game's lap timer when the gate was passed.</summary>
    public float LapSeconds { get; }

    /// <summary>When it reached this client, as <c>Time.realtimeSinceStartup</c>.</summary>
    public float At { get; }
}

/// <summary>
/// Reads a pilot's <c>GMS</c>, the object Liftoff publishes on their Photon player: the laps
/// of the current run as a <c>float[]</c> of seconds, and, in the race modes that publish
/// it, the last race checkpoint they passed as a <c>RacePlayerCheckpointInfo</c> (whose
/// name and <c>ID</c>, <c>Lap</c> and <c>Time</c> survived the obfuscation). GMS's own
/// types and members are obfuscated, and differ from one race mode to the next, so both
/// are found by their type. The modes that don't publish the checkpoint are covered by
/// <see cref="GateHook"/>.
/// </summary>
internal sealed class GmsReader
{
    // The mod ignores a lap list longer than its lap cap. A pilot's own run can be long.
    private const int MaxLapsInRun = 1000;
    private const string CheckpointType = "RacePlayerCheckpointInfo";

    private readonly Dictionary<Type, List<Func<object, object?>>> _members = new();
    private PropertyInfo? _id;
    private PropertyInfo? _lap;
    private PropertyInfo? _time;

    /// <summary>
    /// The laps of the current run, in milliseconds, or null when GMS carries none. A list
    /// the mod would refuse (empty, or a time under a second or over ten minutes) is no
    /// laps, but it still counts as a list: only a GMS with no lap list at all is a respawn.
    /// </summary>
    public List<int>? LapList(object gms, out bool hasList)
    {
        float[]? best = null;
        var sawList = false;
        Visit(gms, 0);
        hasList = sawList;
        if (best == null)
            return null;
        var laps = new List<int>(best.Length);
        foreach (var seconds in best)
            laps.Add((int)Math.Round(seconds * 1000d));
        return laps;

        void Visit(object? value, int depth)
        {
            if (value is float[] list)
            {
                sawList = true;
                if (list.Length > 0 && list.Length <= MaxLapsInRun && AllLapTimes(list) && (best == null || list.Length > best.Length))
                    best = list;
                return;
            }
            if (value == null || depth >= 2 || value is string || value is Array || value is UnityEngine.Object)
                return;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum)
                return;
            foreach (var read in Members(type))
                Visit(Get(read, value), depth + 1);
        }
    }

    /// <summary>
    /// The last checkpoint GMS says the pilot passed, or null when it carries none: a race
    /// mode that doesn't publish it, or a run that hasn't reached a gate yet. Looked for as
    /// the lap list is, in GMS's members and in the members of what they hold.
    /// </summary>
    public (string Id, int Lap, float Seconds)? Checkpoint(object gms)
    {
        return Find(gms, 0);

        (string, int, float)? Find(object? value, int depth)
        {
            if (value == null || value is string || value is Array || value is UnityEngine.Object)
                return null;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum)
                return null;
            if (type.Name == CheckpointType)
                return Read(value);
            if (depth >= 2)
                return null;
            foreach (var read in Members(type))
            {
                if (Find(Get(read, value), depth + 1) is { } found)
                    return found;
            }
            return null;
        }
    }

    private (string, int, float)? Read(object info)
    {
        var type = info.GetType();
        _id ??= type.GetProperty("ID");
        _lap ??= type.GetProperty("Lap");
        _time ??= type.GetProperty("Time");
        if (_id?.GetValue(info, null) is not string id || id.Length == 0)
            return null;
        var lap = _lap?.GetValue(info, null) is int l ? l : 0;
        var seconds = _time?.GetValue(info, null) is float t ? t : 0f;
        return (id, lap, seconds);
    }

    private static object? Get(Func<object, object?> read, object target)
    {
        try
        {
            return read(target);
        }
        catch
        {
            return null;
        }
    }

    private static bool AllLapTimes(float[] list)
    {
        foreach (var seconds in list)
        {
            if (seconds <= 1f || seconds > 600f)
                return false;
        }
        return true;
    }

    /// <summary>A type's public fields and readable properties, as the mod's logging walks them.</summary>
    private List<Func<object, object?>> Members(Type type)
    {
        if (_members.TryGetValue(type, out var known))
            return known;
        var members = new List<Func<object, object?>>();
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            members.Add(target => field.GetValue(target));
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0)
                members.Add(target => property.GetValue(target, null));
        }
        _members[type] = members;
        return members;
    }
}
