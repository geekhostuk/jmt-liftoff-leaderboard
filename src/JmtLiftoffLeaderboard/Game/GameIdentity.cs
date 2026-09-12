using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Photon.Pun;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// The ids the game knows the local player by, in the order worth asking the site about.
///
/// The JMT site keys a pilot on the Photon user id their laps arrive under: either
/// <c>steam_&lt;SteamID64&gt;</c> or a bare LuGus account number. The game builds that id
/// from its player-info service when it goes online, so before then it is read from the
/// service directly; the Steam id comes last because it only finds pilots whose key
/// carries it or who have claimed their profile.
/// </summary>
internal static class GameIdentity
{
    /// <summary>What a pilot key looks like. Anything else a service offers (a display name, a GUID) is not asked about.</summary>
    private static readonly Regex LooksLikeAnId = new(@"^(steam_)?\d{3,20}$", RegexOptions.CultureInvariant);

    public static List<string> Candidates(string? remembered)
    {
        var ids = new List<string>();
        void Add(string? id)
        {
            id = id?.Trim();
            if (!string.IsNullOrEmpty(id) && LooksLikeAnId.IsMatch(id!) && !ids.Contains(id!))
                ids.Add(id!);
        }

        Add(PhotonUserId());
        foreach (var id in PlayerInfoIds())
            Add(id);
        Add(remembered);
        Add(SteamId());
        return ids;
    }

    /// <summary>The id the game signed in to Photon with, once it has gone online this session.</summary>
    public static string? PhotonUserId()
    {
        try
        {
            var fromAuth = PhotonNetwork.AuthValues?.UserId;
            return !string.IsNullOrEmpty(fromAuth) ? fromAuth : PhotonNetwork.LocalPlayer?.UserId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Every string the game's player-info service offers. The game hands one of them to
    /// Photon as the user id, but the service's members are obfuscated, so which one is
    /// not readable from its name; all of them are offered, and only those shaped like an
    /// id survive <see cref="LooksLikeAnId"/>.
    /// </summary>
    private static IEnumerable<string> PlayerInfoIds()
    {
        var found = new List<string>();
        try
        {
            var providerType = AccessTools.TypeByName("Liftoff.Platform.PlatformProvider");
            var provider = providerType == null ? null : AccessTools.Property(providerType, "Instance")?.GetValue(null);
            var serviceProperty = providerType == null ? null : AccessTools.Property(providerType, "PlayerInfoService");
            var service = provider == null ? null : serviceProperty?.GetValue(provider);
            if (service == null)
                return found;

            // Read through the interface the provider declares, not the concrete class:
            // an explicit interface implementation is only reachable that way.
            foreach (var property in serviceProperty!.PropertyType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.PropertyType != typeof(string) || property.GetIndexParameters().Length != 0)
                    continue;
                try
                {
                    if (property.GetValue(service) is string value)
                        found.Add(value);
                }
                catch
                {
                    // One member failing to read says nothing about the others.
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Player info service not readable: {ex.Message}");
        }
        return found;
    }

    private static string? SteamId()
    {
        try
        {
            var id = Steamworks.SteamUser.GetSteamID().m_SteamID;
            return id == 0 ? null : id.ToString();
        }
        catch
        {
            // Not a Steam copy, or Steam not initialised yet.
            return null;
        }
    }
}
