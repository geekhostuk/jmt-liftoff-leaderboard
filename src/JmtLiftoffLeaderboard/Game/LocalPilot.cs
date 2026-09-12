using System;
using System.Collections.Generic;
using JmtLiftoffLeaderboard.Site;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// Who "you" are on the JMT site: the pilot whose row a board highlights and whose
/// profile JMT Profile opens.
///
/// A <c>PublicId</c> in the .cfg always wins, because the pilot put it there. Otherwise
/// each id the game knows the player by is offered to the site in turn, and the first
/// one it recognises is the answer for the rest of the session.
/// </summary>
internal sealed class LocalPilot
{
    private readonly SiteClient _site;
    private readonly Settings _settings;
    private readonly List<Action<string?>> _waiting = new();
    private string? _found;
    private bool _looking;

    public LocalPilot(SiteClient site, Settings settings)
    {
        _site = site;
        _settings = settings;
    }

    /// <summary>The pilot's public id if already known, without asking the site.</summary>
    public string? Known
    {
        get
        {
            var chosen = _settings.PilotPublicId.Value?.Trim();
            return !string.IsNullOrEmpty(chosen) ? chosen : _found;
        }
    }

    /// <summary>"This is me": remembered in the .cfg, so it holds on the next launch too.</summary>
    public void Choose(string publicId)
    {
        _settings.PilotPublicId.Value = publicId;
        _found = publicId;
    }

    /// <summary>
    /// The pilot's public id, or null if the site knows none of their ids. A miss is not
    /// remembered: the game may go online later and offer an id it didn't have yet.
    /// </summary>
    public void Resolve(Action<string?> done)
    {
        var known = Known;
        if (known != null)
        {
            done(known);
            return;
        }

        _waiting.Add(done);
        if (_looking)
            return;
        _looking = true;
        TryFrom(GameIdentity.Candidates(_settings.LastGameUserId.Value), 0);
    }

    private void TryFrom(List<string> ids, int next)
    {
        if (next >= ids.Count)
        {
            Finish(null);
            return;
        }

        var id = ids[next];
        _site.PilotByGameId(id, result =>
        {
            if (result.Ok)
            {
                _found = result.Value!.Pilot.PublicId;
                if (_settings.LastGameUserId.Value != id)
                    _settings.LastGameUserId.Value = id;
                Finish(_found);
            }
            else if (result.NotFound)
            {
                TryFrom(ids, next + 1);
            }
            else
            {
                // The site is down or unreadable; the other ids would fail the same way.
                Finish(null);
            }
        });
    }

    private void Finish(string? publicId)
    {
        _looking = false;
        var waiting = _waiting.ToArray();
        _waiting.Clear();
        foreach (var callback in waiting)
            callback(publicId);
    }
}
