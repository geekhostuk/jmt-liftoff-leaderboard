using System;
using System.Collections.Generic;
using System.Linq;
using JmtLiftoffLeaderboard.Site;
using Photon.Pun;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>Whether what the pilot flies now reaches the JMT site.</summary>
internal enum Counting
{
    /// <summary>In a room, not yet known whether it reports to the JMT site.</summary>
    Unknown,
    Live,
    NotCounted,
}

/// <summary>
/// The Photon room the pilot is in, as the JMT site sees it: whether a Liftoff Control
/// panel reports it, and so whether anything flown in it counts; and who that panel says
/// is in it, so the HUD can point out a rival flying here now.
///
/// A panel reports the Photon room name its bot is in, which is the name the game gives
/// this client too. A roster match covers a panel that reported no room name.
/// </summary>
internal sealed class RoomWatch
{
    private const float EverySeconds = 30f;

    private readonly SiteClient _site;
    private readonly LocalPilot _me;
    private readonly LocalRun _run;
    private readonly HashSet<string> _pilots = new();
    private string? _room;
    private float _next;

    public RoomWatch(SiteClient site, LocalPilot me, LocalRun run)
    {
        _site = site;
        _me = me;
        _run = run;
    }

    public Counting Counting { get; private set; } = Counting.NotCounted;

    /// <summary>The site's id for the panel reporting this room, for its timing screen; null when none does.</summary>
    public string? PanelId { get; private set; }

    /// <summary>Whether counting changed, or who's in the room.</summary>
    public event Action? Changed;

    /// <summary>Whether a pilot, by public id, is in this room, as its panel last said.</summary>
    public bool IsHere(string publicId) => _pilots.Contains(publicId);

    /// <summary>Called every frame; asks the site at most every half minute.</summary>
    public void Tick(float now)
    {
        // Photon is the game's to set up; LocalRun waits for it.
        if (!_run.Installed)
            return;
        var room = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom?.Name : null;
        if (room != _room)
        {
            _room = room;
            _next = 0;
            _pilots.Clear();
            PanelId = null;
            Set(room == null ? Counting.NotCounted : Counting.Unknown);
        }
        if (room == null || now < _next)
            return;

        _next = now + EverySeconds;
        _site.Live(result =>
        {
            if (_room != room || !result.Ok)
                return;
            var me = _me.Known;
            var panels = result.Value!;
            var panel = panels.FirstOrDefault(p => p.InRoom && p.RoomName == room)
                        ?? panels.FirstOrDefault(p => string.IsNullOrEmpty(p.RoomName) && me != null && p.Pilots.Any(pilot => pilot.PublicId == me));
            _pilots.Clear();
            PanelId = string.IsNullOrEmpty(panel?.PublicId) ? null : panel!.PublicId;
            if (panel != null)
            {
                foreach (var pilot in panel.Pilots)
                    _pilots.Add(pilot.PublicId);
            }
            Set(panel != null ? Counting.Live : Counting.NotCounted);
            Changed?.Invoke();
        });
    }

    private void Set(Counting counting)
    {
        if (Counting == counting)
            return;
        Counting = counting;
        if (counting == Counting.Live)
            Plugin.Log.LogInfo("HUD: this room reports to the JMT site; your laps and failed attempts count.");
        else if (counting == Counting.NotCounted && _room != null)
            Plugin.Log.LogInfo("HUD: this room doesn't report to the JMT site; nothing flown here counts.");
        Changed?.Invoke();
    }
}
