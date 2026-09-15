using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// The local pilot's race checkpoints, from the game's own checkpoint tracking.
///
/// Every race mode keeps a tracker per drone. When the drone passes a checkpoint in the
/// right order, the tracker records it as a <c>RacePlayerCheckpointInfo</c>: the passage's
/// id, the lap and the lap timer there. Only some modes put that into the GMS the room
/// sees, so this reads it where it's made: a postfix on every method that takes a
/// <c>RaceCheckpoint</c> and a <c>RaceCheckpointPassage</c>, in a class that keeps a
/// checkpoint. Those names, and <c>DroneHUD.CurrentDrone</c>, survived the obfuscation;
/// the methods' own names didn't, so they're found by their parameters.
///
/// A tracker that holds a drone other than the one on the pilot's HUD belongs to someone
/// else, and is ignored. Nothing thrown here reaches the game.
/// </summary>
internal static class GateHook
{
    private const string CheckpointType = "RacePlayerCheckpointInfo";
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<Type, List<FieldInfo>> Infos = new();
    private static readonly Dictionary<Type, List<FieldInfo>> Drones = new();
    private static Type? _infoType;
    private static Type? _hudType;
    private static Type? _droneType;
    private static PropertyInfo? _currentDrone;
    private static UnityEngine.Object? _hud;
    private static float _nextHudLook;
    private static PropertyInfo? _id;
    private static PropertyInfo? _lap;
    private static PropertyInfo? _time;
    private static bool _warned;

    /// <summary>The local pilot passed a checkpoint: its passage id, the game's lap number, and the lap timer there.</summary>
    public static event Action<string, int, float>? Passed;

    public static void Install(Harmony harmony)
    {
        try
        {
            var checkpoint = AccessTools.TypeByName("RaceCheckpoint");
            var passage = AccessTools.TypeByName("RaceCheckpointPassage");
            _infoType = AccessTools.TypeByName(CheckpointType);
            if (checkpoint == null || passage == null || _infoType == null)
            {
                Plugin.Log.LogWarning("HUD: the game's race checkpoints weren't found, so the delta bar has no gates to compare. A game update may have renamed them.");
                return;
            }
            _hudType = AccessTools.TypeByName("DroneHUD");
            _currentDrone = _hudType == null ? null : AccessTools.Property(_hudType, "CurrentDrone");
            _droneType = _currentDrone?.PropertyType;

            var postfix = new HarmonyMethod(typeof(GateHook), nameof(Postfix));
            var patched = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(_infoType.Assembly))
            {
                if (type.IsInterface || type.ContainsGenericParameters || !Keeps(type))
                    continue;
                foreach (var method in type.GetMethods(Declared))
                {
                    var parameters = method.GetParameters();
                    if (method.IsAbstract || method.ContainsGenericParameters || parameters.Length != 2
                        || parameters[0].ParameterType != checkpoint || parameters[1].ParameterType != passage)
                        continue;
                    harmony.Patch(method, postfix: postfix);
                    patched++;
                }
            }
            Plugin.Log.LogInfo(patched == 0
                ? "HUD: none of the game's checkpoint trackers were found, so the delta bar has no gates to compare. A game update may have changed them."
                : $"HUD: following your checkpoints through {patched} of the game's trackers.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"HUD: couldn't follow the game's checkpoints, so the delta bar has no gates to compare: {ex.Message}");
        }
    }

    /// <summary>Whether a class keeps a checkpoint in a field of its own.</summary>
    private static bool Keeps(Type type)
    {
        foreach (var field in type.GetFields(Declared))
        {
            if (field.FieldType == _infoType)
                return true;
        }
        return false;
    }

    private static void Postfix(object __instance)
    {
        using var probe = FrameProbe.Measure(FrameProbe.Part.Gates);
        try
        {
            if (__instance == null || !Mine(__instance))
                return;
            foreach (var field in Fields(Infos, __instance.GetType(), _infoType))
            {
                var info = field.GetValue(__instance);
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
                Passed?.Invoke(id, lap, seconds);
                return;
            }
        }
        catch (Exception ex)
        {
            if (_warned)
                return;
            _warned = true;
            Plugin.Log.LogWarning($"HUD: couldn't read a checkpoint: {ex}");
        }
    }

    /// <summary>A tracker holding a drone that isn't the one on the pilot's HUD is someone else's.</summary>
    private static bool Mine(object tracker)
    {
        if (_droneType == null || _currentDrone == null || _hudType == null)
            return true;
        // A search of the scene, so a scene with no HUD is searched once a second rather than
        // at every gate of every drone. A HUD gone with its scene is looked for straight away.
        var now = UnityEngine.Time.realtimeSinceStartup;
        if (_hud == null && now >= _nextHudLook)
        {
            _hud = UnityEngine.Object.FindObjectOfType(_hudType);
            if (_hud == null)
                _nextHudLook = now + 1f;
        }
        var current = _hud != null ? _currentDrone.GetValue(_hud, null) : null;
        if (current == null)
            return true;
        foreach (var field in Fields(Drones, tracker.GetType(), _droneType))
        {
            var drone = field.GetValue(tracker);
            if (drone != null && !ReferenceEquals(drone, current))
                return false;
        }
        return true;
    }

    /// <summary>A class's fields of one type, its base classes' included, looked up once.</summary>
    private static List<FieldInfo> Fields(Dictionary<Type, List<FieldInfo>> cache, Type type, Type? of)
    {
        if (cache.TryGetValue(type, out var known))
            return known;
        var fields = new List<FieldInfo>();
        for (var t = type; of != null && t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var field in t.GetFields(Declared))
            {
                if (field.FieldType == of)
                    fields.Add(field);
            }
        }
        cache[type] = fields;
        return fields;
    }
}
