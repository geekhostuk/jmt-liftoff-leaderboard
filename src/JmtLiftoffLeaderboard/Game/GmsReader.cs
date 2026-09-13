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

    /// <summary>When it reached this client, as <c>Time.unscaledTime</c>.</summary>
    public float At { get; }
}

/// <summary>
/// Reads a pilot's <c>GMS</c>, the object Liftoff publishes on their Photon player: the laps
/// of the current run as a <c>float[]</c> of seconds, and the last race checkpoint they
/// passed as a <c>RacePlayerCheckpointInfo</c> (whose name and <c>ID</c>, <c>Lap</c> and
/// <c>Time</c> survived the obfuscation). GMS's own type and members are obfuscated, so
/// both are found by their type.
/// </summary>
internal sealed class GmsReader
{
    // The mod ignores a lap list longer than its lap cap. A pilot's own run can be long.
    private const int MaxLapsInRun = 1000;
    private const string CheckpointType = "RacePlayerCheckpointInfo";

    private readonly Dictionary<Type, List<Func<object, object?>>> _members = new();
    private readonly Dictionary<Type, List<Func<object, object?>>> _checkpoints = new();
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
            {
                object? child;
                try
                {
                    child = read(value);
                }
                catch
                {
                    continue;
                }
                Visit(child, depth + 1);
            }
        }
    }

    /// <summary>
    /// The last checkpoint GMS says the pilot passed, or null when it carries none or an
    /// empty one (a run that hasn't reached a gate yet).
    /// </summary>
    public (string Id, int Lap, float Seconds)? Checkpoint(object gms)
    {
        foreach (var read in Checkpoints(gms.GetType()))
        {
            object? info;
            try
            {
                info = read(gms);
            }
            catch
            {
                continue;
            }
            if (info == null)
                continue;
            var type = info.GetType();
            _id ??= type.GetProperty("ID");
            _lap ??= type.GetProperty("Lap");
            _time ??= type.GetProperty("Time");
            if (_id?.GetValue(info, null) is not string id || id.Length == 0)
                continue;
            var lap = _lap?.GetValue(info, null) is int l ? l : 0;
            var seconds = _time?.GetValue(info, null) is float t ? t : 0f;
            return (id, lap, seconds);
        }
        return null;
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

    /// <summary>Every member of GMS's type, public or not, that holds a checkpoint.</summary>
    private List<Func<object, object?>> Checkpoints(Type type)
    {
        if (_checkpoints.TryGetValue(type, out var known))
            return known;
        var members = new List<Func<object, object?>>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var field in t.GetFields(flags | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType.Name == CheckpointType)
                    members.Add(target => field.GetValue(target));
            }
        }
        if (members.Count == 0)
        {
            foreach (var property in type.GetProperties(flags))
            {
                if (property.PropertyType.Name == CheckpointType && property.CanRead && property.GetIndexParameters().Length == 0)
                    members.Add(target => property.GetValue(target, null));
            }
        }
        _checkpoints[type] = members;
        if (members.Count == 0)
            Plugin.Log.LogWarning($"HUD: the game's run state ({type.Name}) carries no checkpoint, so the delta bar has no gates to compare.");
        return members;
    }
}
