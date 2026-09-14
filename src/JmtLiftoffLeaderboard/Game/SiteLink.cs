using System;
using System.Collections;
using JmtLiftoffLeaderboard.Site;
using UnityEngine;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// "Link your JMT account": the same flow as Liftoff Control's Link to JMT. The plugin
/// asks the site for a link, the site's page opens in the browser with a code filled in,
/// and once the pilot approves it there, signed in with Steam, the plugin's next question
/// is answered with a token for their account. It goes in the plugin's own .cfg, on the
/// pilot's computer, and lets the plugin send the gate times of their own laps.
///
/// Linking never claims the pilot's game id for their account: anyone in a room can see
/// it, so it proves nothing. A pilot whose game id isn't theirs on the site yet types a
/// claim code in a JMT room once, as everyone does.
/// </summary>
internal sealed class SiteLink
{
    private const float MinIntervalSeconds = 2f;

    private readonly MonoBehaviour _host;
    private readonly SiteClient _site;
    private readonly Settings _settings;
    // Goes up with each start or cancel, so a question still out for an old link is ignored.
    private int _attempt;

    public SiteLink(MonoBehaviour host, SiteClient site, Settings settings)
    {
        _host = host;
        _site = site;
        _settings = settings;
        settings.SiteToken.SettingChanged += (_, _) => Changed?.Invoke();
    }

    public bool Linked => Token.Length > 0;

    public string Token => (_settings.SiteToken.Value ?? "").Trim();

    /// <summary>The account it's linked to, as the site named it.</summary>
    public string LinkedAs => _settings.SiteLinkedAs.Value ?? "";

    /// <summary>Waiting for the pilot to approve the code on the site.</summary>
    public bool Waiting { get; private set; }

    public string UserCode { get; private set; } = "";

    /// <summary>The site's link page, to enter the code on by hand: "test.geekhost.uk/link".</summary>
    public string Page { get; private set; } = "";

    /// <summary>Something the pilot should know: a turned-down link, a refused token.</summary>
    public string Problem { get; private set; } = "";

    public event Action? Changed;

    public void Start()
    {
        var attempt = ++_attempt;
        Waiting = true;
        UserCode = "";
        Problem = "";
        Changed?.Invoke();

        var gameId = GameIdentity.PhotonUserId();
        if (string.IsNullOrEmpty(gameId))
            gameId = _settings.LastGameUserId.Value;
        _site.LinkStart($"JMT Liftoff Leaderboard on {SystemInfo.deviceName}", Plugin.PluginVersion,
            string.IsNullOrEmpty(gameId) ? null : gameId, result =>
            {
                if (attempt != _attempt)
                    return;
                if (!result.Ok)
                {
                    Fail(result.NotFound ? "The JMT site doesn't link accounts yet." : result.Error ?? "Couldn't start the link.");
                    return;
                }
                var started = result.Value!;
                UserCode = started.UserCode;
                // Where to go by hand, if the browser doesn't open: the address without its scheme.
                Page = started.VerificationUri.Replace("https://", "").Replace("http://", "");
                var page = started.VerificationUriComplete;
                if (Browser.IsWebAddress(page))
                {
                    Browser.Copy(page);
                    Browser.Open(page);
                }
                Plugin.Log.LogInfo($"HUD: linking to the JMT site: approve code {UserCode} at {page} (copied to the clipboard).");
                Changed?.Invoke();
                _host.StartCoroutine(Poll(attempt, started));
            });
    }

    public void Cancel()
    {
        _attempt++;
        Waiting = false;
        UserCode = "";
        Changed?.Invoke();
    }

    /// <summary>Forgets the account: the token is revoked on the site and cleared here.</summary>
    public void Unlink()
    {
        var token = Token;
        Cancel();
        Problem = "";
        _settings.SiteToken.Value = "";
        _settings.SiteLinkedAs.Value = "";
        if (token.Length > 0)
            _site.RevokeToken(token, _ => { });
        Plugin.Log.LogInfo("HUD: unlinked from the JMT site.");
        Changed?.Invoke();
    }

    /// <summary>The site turned the token away, so the pilot has to link again.</summary>
    public void Refused(string why)
    {
        Problem = why;
        Changed?.Invoke();
    }

    private IEnumerator Poll(int attempt, LinkStarted started)
    {
        var interval = Mathf.Max(MinIntervalSeconds, started.Interval);
        var until = Time.realtimeSinceStartup + Mathf.Max(30, started.ExpiresIn);
        while (attempt == _attempt)
        {
            yield return new WaitForSecondsRealtime(interval);
            if (attempt != _attempt)
                yield break;
            if (Time.realtimeSinceStartup > until)
            {
                Fail("The code ran out before it was approved. Link again.");
                yield break;
            }

            Result<LinkPollAnswer>? result = null;
            _site.LinkPoll(started.DeviceCode, answer => result = answer);
            while (result == null)
                yield return null;
            if (attempt != _attempt)
                yield break;

            if (result.Status == 429)
            {
                interval += 1f; // asked to slow down
                continue;
            }
            if (result.Status == 404)
            {
                Fail("The JMT site no longer knows this link. Link again.");
                yield break;
            }
            // Anything else unanswered is the network, and the next question may get through.
            if (!result.Ok)
                continue;
            switch (result.Value!.Status)
            {
                case "approved":
                    Collect(result.Value);
                    yield break;
                case "denied":
                    Fail("The link was turned down on the site.");
                    yield break;
                case "expired":
                    Fail("The code ran out before it was approved. Link again.");
                    yield break;
                case "consumed":
                    Fail("That link was already used. Link again.");
                    yield break;
            }
        }
    }

    private void Collect(LinkPollAnswer answer)
    {
        if (string.IsNullOrEmpty(answer.Token))
        {
            Fail("The site approved the link but sent no key. Link again.");
            return;
        }
        Waiting = false;
        UserCode = "";
        Problem = "";
        _settings.SiteLinkedAs.Value = answer.Owner?.PersonaName ?? answer.Owner?.SteamId ?? "";
        _settings.SiteToken.Value = answer.Token!;
        Plugin.Log.LogInfo($"HUD: linked to the JMT site as {LinkedAs}.");
        Changed?.Invoke();
    }

    private void Fail(string why)
    {
        Waiting = false;
        UserCode = "";
        Problem = why;
        Plugin.Log.LogInfo($"HUD: link to the JMT site: {why}");
        Changed?.Invoke();
    }
}
