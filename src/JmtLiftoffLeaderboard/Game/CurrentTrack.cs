using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>The course being flown, as the JMT site names its board.</summary>
internal sealed class TrackRef
{
    public TrackRef(long? boardId, string name, string trackName, IReadOnlyList<string> environments)
    {
        BoardId = boardId;
        Name = name;
        TrackName = trackName;
        Environments = environments;
    }

    /// <summary>
    /// The Steam Workshop id of the race being flown, else of its track. A course with
    /// none -- one of Liftoff's own, or a Workshop course the site could not match -- has
    /// a named board instead, and this is set to its (negative) id once it is found.
    /// </summary>
    public long? BoardId { get; set; }

    /// <summary>What to call it: the race's name, else the track's.</summary>
    public string Name { get; }

    /// <summary>The track's in-game name, which is what the site keys a named board on.</summary>
    public string TrackName { get; }

    /// <summary>The environment as the game names it: its asset name and its display name.</summary>
    public IReadOnlyList<string> Environments { get; }

    /// <summary>Set once the board has been looked up by name, so it is only tried once.</summary>
    public bool LookedUpByName { get; set; }

    /// <summary>Whether a board's environment is the one being flown, however either spells it.</summary>
    public bool InEnvironment(params string?[] names) =>
        names.Any(name => !string.IsNullOrWhiteSpace(name) && Environments.Any(mine => Squash(mine) == Squash(name!)));

    private static string Squash(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}

/// <summary>
/// Reads the loaded level from the game's <c>CurrentContentContainer</c>. Its name
/// survived the obfuscation, as did its <c>Environment</c>, <c>Race</c> and
/// <c>Track</c> properties and the <c>ManagedID</c> every piece of shareable content
/// carries. For Workshop content, <c>ManagedID.str</c> is the Steam PublishedFileId
/// (the game parses it with <c>ulong.Parse</c> to query Steam about the item);
/// Liftoff's own courses have none.
/// </summary>
internal static class CurrentTrack
{
    private static Type? _containerType;
    private static UnityEngine.Object? _container;

    public static TrackRef? Read()
    {
        try
        {
            _containerType ??= AccessTools.TypeByName("CurrentContentContainer");
            if (_containerType == null)
                return null;

            // Finding it walks every object the game has loaded, so the live one is kept and
            // only looked for again once it is gone or holds no level. It outlives a track change.
            if (_container == null || Member(_container, "Level") == null)
                _container = FindLive(_containerType);
            var container = _container;
            if (container == null)
                return null;
            var race = Member(container, "Race");
            var track = Member(container, "Track");
            if (race == null && track == null)
                return null;

            var id = WorkshopId(race) ?? WorkshopId(track);
            var trackName = Member(track, "Name") as string ?? "";
            var name = Member(race, "Name") as string ?? trackName;
            return new TrackRef(id, name, trackName, EnvironmentNames(Member(container, "Environment")));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Couldn't read the course being flown: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// FindObjectsOfTypeAll rather than FindObjectOfType: the container may sit on an inactive
    /// object. The live one is whichever has a level loaded.
    /// </summary>
    private static UnityEngine.Object? FindLive(Type containerType)
    {
        foreach (var container in Resources.FindObjectsOfTypeAll(containerType))
        {
            if (container != null && Member(container, "Level") != null)
                return container;
        }
        return null;
    }

    /// <summary>
    /// Where a respawned drone is put: the container's <c>DroneSpawnPoint</c>. Null while no
    /// course is loaded, and then the container is looked for every couple of seconds at most.
    /// </summary>
    public static Vector3? SpawnPoint()
    {
        _containerType ??= AccessTools.TypeByName("CurrentContentContainer");
        if (_containerType == null)
            return null;
        if (_container == null || Member(_container, "Level") == null)
        {
            var now = Time.realtimeSinceStartup;
            if (now < _nextLook)
                return null;
            _container = FindLive(_containerType);
            if (_container == null)
            {
                _nextLook = now + 2f;
                return null;
            }
        }
        return Member(_container, "DroneSpawnPoint") is Component point && point != null
            ? point.transform.position
            : null;
    }

    private static float _nextLook;

    private static IReadOnlyList<string> EnvironmentNames(object? environment)
    {
        var names = new List<string>();
        if (environment is UnityEngine.Object asset && asset != null && !string.IsNullOrWhiteSpace(asset.name))
            names.Add(asset.name);
        if (Member(environment, "DisplayName") is string display && !string.IsNullOrWhiteSpace(display))
            names.Add(display);
        return names;
    }

    private static long? WorkshopId(object? content)
    {
        var managed = Member(content, "ManagedID");
        var raw = managed == null ? null : AccessTools.Field(managed.GetType(), "str")?.GetValue(managed) as string;
        return long.TryParse(raw, out var id) && id > 0 ? id : null;
    }

    private static object? Member(object? target, string name)
    {
        if (target == null)
            return null;
        var type = target.GetType();
        var property = AccessTools.Property(type, name);
        if (property != null && property.GetIndexParameters().Length == 0)
            return property.GetValue(target);
        return AccessTools.Field(type, name)?.GetValue(target);
    }
}
