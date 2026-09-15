using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BepInEx.Logging;
using JmtLiftoffLeaderboard.Game;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace JmtLiftoffLeaderboard.Site;

/// <summary>
/// What a request came back as. A 404 is its own case rather than an error: an unknown
/// pilot or an unflown board is an answer, and the screens say so instead of "try again".
/// </summary>
public sealed class Result<T> where T : class
{
    public T? Value;
    public long Status;
    public string? Error;

    public bool Ok => Value != null;
    public bool NotFound => Status == 404;
}

/// <summary>
/// Reads the JMT site's public endpoints. Every call is a coroutine on the plugin, so
/// callbacks arrive on Unity's main thread and may touch UI directly.
///
/// Answers are kept for a minute: flipping between a board and a profile and back is
/// the normal way to use this, and each of those should not be a fresh request.
/// </summary>
internal sealed class SiteClient
{
    private const int TimeoutSeconds = 15;
    private const float FreshForSeconds = 60f;
    private const int MaxTextures = 256;

    private static readonly JsonSerializerSettings Json = new()
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
    };

    private readonly MonoBehaviour _host;
    private readonly Settings _settings;
    private readonly ManualLogSource _log;
    private readonly Dictionary<string, (float At, object Value)> _answers = new();
    private readonly Dictionary<string, Texture2D> _textures = new();
    private readonly Dictionary<string, List<Action<Texture2D?>>> _texturesWaiting = new();

    public SiteClient(MonoBehaviour host, Settings settings, ManualLogSource log)
    {
        _host = host;
        _settings = settings;
        _log = log;
    }

    /// <summary>The site's address, for links that open in the browser.</summary>
    public string BaseUrl => _settings.BaseUrl;

    public void Leaderboard(long publishedFileId, int limit, string? query, Action<Result<Leaderboard>> done)
    {
        var path = $"/api/timing/leaderboard/{publishedFileId}?limit={limit}";
        if (!string.IsNullOrWhiteSpace(query))
            path += "&q=" + Uri.EscapeDataString(query!.Trim());
        Get(path, done);
    }

    public void Boards(string? query, string? environment, string sort, int limit, int offset, Action<Result<BoardPage>> done)
    {
        var path = $"/api/timing/boards?sort={sort}&limit={limit}&offset={offset}";
        if (!string.IsNullOrWhiteSpace(query))
            path += "&q=" + Uri.EscapeDataString(query!.Trim());
        if (!string.IsNullOrWhiteSpace(environment))
            path += "&environment=" + Uri.EscapeDataString(environment!);
        Get(path, done);
    }

    public void Pilot(string publicId, Action<Result<PilotProfile>> done) =>
        Get($"/api/pilots/{Uri.EscapeDataString(publicId)}", done);

    /// <summary>The profile behind an id the game gives a player. 404 when nobody by that id has flown in a JMT room.</summary>
    public void PilotByGameId(string gameId, Action<Result<PilotProfile>> done) =>
        Get($"/api/pilots/by-key/{Uri.EscapeDataString(gameId)}", done);

    public void Item(long publishedFileId, Action<Result<ItemInfo>> done) =>
        Get($"/api/items/{publishedFileId}", done);

    /// <summary>
    /// A board around one pilot, for the HUD: the places above and below theirs, or its last
    /// places when they aren't on it. Always asked afresh. A site from before <c>around</c>
    /// ignores it and answers with its top 500, which hold the same places on all but the
    /// biggest boards.
    /// </summary>
    public void LeaderboardAround(long publishedFileId, string? publicId, int above, int below, Action<Result<Leaderboard>> done)
    {
        var path = $"/api/timing/leaderboard/{publishedFileId}?limit=500";
        if (!string.IsNullOrEmpty(publicId))
            path += $"&around={Uri.EscapeDataString(publicId)}&above={above}&below={below}";
        Get(path, done, fresh: true);
    }

    /// <summary>A pilot's CR race by race, for the HUD. Always asked afresh. 404 from a site older than the HUD, too.</summary>
    public void Consistency(string publicId, Action<Result<PilotConsistency>> done) =>
        Get($"/api/pilots/{Uri.EscapeDataString(publicId)}/consistency", done, fresh: true);

    /// <summary>The rooms being flown right now, to tell whether the one the pilot is in counts. Always asked afresh.</summary>
    public void Live(Action<Result<List<LivePanel>>> done) =>
        Get("/api/timing/live", done, fresh: true);

    /// <summary>One room's timing screen, for the race panel. Always asked afresh. 404 once the room's panel is gone.</summary>
    public void LiveRoom(string panelId, Action<Result<LiveBoard>> done) =>
        Get($"/api/timing/live/{Uri.EscapeDataString(panelId)}", done, fresh: true);

    /// <summary>A course's gates and quickest times, and the pilot's, to seed the delta bar with. Always asked afresh.</summary>
    public void CourseSplits(long boardId, string? publicId, Action<Result<SiteCourseSplits>> done)
    {
        var path = $"/api/timing/splits/{boardId}";
        if (!string.IsNullOrEmpty(publicId))
            path += "?pilot=" + Uri.EscapeDataString(publicId);
        Get(path, done, fresh: true);
    }

    /// <summary>Drop every kept answer, for a Refresh button.</summary>
    public void Forget() => _answers.Clear();

    public void Texture(string? url, Action<Texture2D?> done)
    {
        if (string.IsNullOrEmpty(url))
        {
            done(null);
            return;
        }
        if (_textures.TryGetValue(url!, out var cached) && cached != null)
        {
            done(cached);
            return;
        }
        if (_texturesWaiting.TryGetValue(url!, out var waiting))
        {
            waiting.Add(done);
            return;
        }
        _texturesWaiting[url!] = new List<Action<Texture2D?>> { done };
        _host.StartCoroutine(FetchTexture(url!));
    }

    private void Get<T>(string path, Action<Result<T>> done, bool fresh = false) where T : class
    {
        var url = _settings.BaseUrl + path;
        if (!fresh && _answers.TryGetValue(url, out var kept) && Time.realtimeSinceStartup - kept.At < FreshForSeconds)
        {
            Deliver(done, new Result<T> { Value = (T)kept.Value, Status = 200 });
            return;
        }
        _host.StartCoroutine(Fetch(url, done));
    }

    private IEnumerator Fetch<T>(string url, Action<Result<T>> done) where T : class
    {
        using var request = UnityWebRequest.Get(url);
        request.timeout = TimeoutSeconds;
        request.SetRequestHeader("Accept", "application/json");
        yield return request.SendWebRequest();

        var result = new Result<T> { Status = request.responseCode };
        if (request.result == UnityWebRequest.Result.Success)
        {
            // Read on a worker thread: several of these arrive a minute while the pilot flies,
            // and a board can run to tens of kilobytes. The answer is picked up here, on the
            // main thread, a frame or so later.
            var text = request.downloadHandler.text;
            var parse = Task.Run(() => JsonConvert.DeserializeObject<T>(text, Json));
            while (!parse.IsCompleted)
                yield return null;
            if (parse.Status == TaskStatus.RanToCompletion)
            {
                result.Value = parse.Result;
                if (result.Value != null)
                    _answers[url] = (Time.realtimeSinceStartup, result.Value);
            }
            else
            {
                result.Error = "The JMT site sent something this version can't read. An update may fix it.";
                _log.LogWarning($"Could not read {url}: {parse.Exception?.GetBaseException().Message}");
            }
        }
        else if (request.responseCode != 404)
        {
            result.Error = request.responseCode == 0
                ? "Can't reach the JMT site. Check your connection."
                : $"The JMT site answered {request.responseCode}. Try again in a moment.";
            _log.LogWarning($"GET {url}: {request.responseCode} {request.error}");
        }

        Deliver(done, result);
    }

    private IEnumerator FetchTexture(string url)
    {
        using var request = UnityWebRequestTexture.GetTexture(url, nonReadable: true);
        request.timeout = TimeoutSeconds;
        yield return request.SendWebRequest();

        Texture2D? texture = null;
        if (request.result == UnityWebRequest.Result.Success)
        {
            texture = DownloadHandlerTexture.GetContent(request);
            texture.wrapMode = TextureWrapMode.Clamp;
            // Unity unloads textures nothing uses on the next scene change; dropping
            // the references is enough to keep a long session from holding every
            // avatar it ever showed.
            if (_textures.Count >= MaxTextures)
                _textures.Clear();
            _textures[url] = texture;
        }

        var waiting = _texturesWaiting[url];
        _texturesWaiting.Remove(url);
        foreach (var callback in waiting)
            Deliver(callback, texture);
    }

    private void Deliver<TArg>(Action<TArg> callback, TArg value)
    {
        using var probe = FrameProbe.Measure(FrameProbe.Part.Site);
        try
        {
            callback(value);
        }
        catch (Exception ex)
        {
            _log.LogError($"A screen failed to show what the site sent: {ex}");
        }
    }
}
