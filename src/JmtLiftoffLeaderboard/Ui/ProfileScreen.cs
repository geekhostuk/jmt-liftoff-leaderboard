using System;
using System.Collections.Generic;
using System.Linq;
using JmtLiftoffLeaderboard.Game;
using JmtLiftoffLeaderboard.Site;
using UnityEngine;
using UnityEngine.UI;

namespace JmtLiftoffLeaderboard.Ui;

/// <summary>
/// A pilot's profile, laid out like the site's (web/src/routes/PilotDetail.tsx): who
/// they are and their two ratings, a stat strip, where they place, their latest
/// personal bests, and their best lap on every course they have flown.
/// </summary>
internal sealed class ProfileScreen
{
    private static readonly string[] Tabs = { "All", "Podium", "Fastest" };
    private static readonly string[] SortNames = { "Recent", "Position", "Field", "Laps" };

    // Course table columns.
    private const float PosWidth = 100;
    private const float FieldWidth = 210;
    private const float LapsWidth = 70;
    private const float SetWidth = 100;
    private const float BestWidth = 120;

    // The three rating cards sit side by side beside the hero, all one height.
    private const float RatingWidth = 330;

    private readonly SiteClient _site;
    private readonly LocalPilot _me;
    private readonly Overlay _overlay;
    private readonly RectTransform _content;
    private readonly ScrollRect _scroll;
    private readonly Debouncer _search = new();

    private int _request;
    private PilotProfile? _profile;
    private int _tab;
    private int _sort;
    private string _query = "";
    private RectTransform? _table;

    public ProfileScreen(RectTransform root, SiteClient site, LocalPilot me, Overlay overlay)
    {
        _site = site;
        _me = me;
        _overlay = overlay;
        _content = UiKit.Scroll(root, out _scroll, 18);
        UiKit.Fill((RectTransform)_content.parent);
    }

    public void ShowMe()
    {
        var request = ++_request;
        Notice("Finding you on the JMT site…");
        _me.Resolve(id =>
        {
            if (request != _request)
                return;
            if (id == null)
                ShowNotFound();
            else
                Load(id, request);
        });
    }

    public void Show(string publicId) => Load(publicId, ++_request);

    private void Load(string publicId, int request)
    {
        Notice("Loading the profile…");
        _site.Pilot(publicId, result =>
        {
            if (request != _request)
                return;
            if (result.Ok)
            {
                _profile = result.Value;
                _tab = 0;
                _sort = 0;
                _query = "";
                Render(_profile!);
            }
            else if (result.NotFound)
            {
                UiKit.Clear(_content);
                Blocks.Message(_content, "That pilot isn't on the JMT site", "Their profile may have been removed.");
            }
            else
            {
                UiKit.Clear(_content);
                Blocks.Message(_content, "Couldn't load the profile", result.Error ?? "Try again in a moment.", ("Try again", () =>
                {
                    _site.Forget();
                    Load(publicId, ++_request);
                }));
            }
        });
    }

    private void Notice(string text)
    {
        UiKit.Clear(_content);
        Blocks.Loading(_content, text);
    }

    private void ShowNotFound()
    {
        UiKit.Clear(_content);
        Blocks.Message(_content, "We haven't found you on the JMT site yet",
            "A profile appears once you've flown a lap in a JMT room. If you have, open a board " +
            "you've flown, click your row and press \"This is me\". It's remembered from then on.",
            ("Open the leaderboard", () => _overlay.ShowBoard(0)));
    }

    private void Render(PilotProfile profile)
    {
        UiKit.Clear(_content);
        _scroll.verticalNormalizedPosition = 1;

        var top = UiKit.Row(_content, 14, align: TextAnchor.UpperLeft, name: "Top");
        RenderHero(top, profile);
        RenderRating(top, profile);
        RenderConsistency(top, profile);
        RenderPace(top, profile);

        var tracks = profile.Tracks;
        var fastest = tracks.Count(t => t.Position == 1);
        var podiums = tracks.Count(t => t.Position <= 3);
        var (activeValue, activeUnit) = ActiveFor(profile.FirstSeenAt);
        Blocks.StatStrip(_content,
            ("Laps flown", Theme.Count(profile.TotalLaps), null),
            ("Courses", Theme.Count(profile.TrackCount), null),
            ("Fastest", Theme.Count(fastest), fastest > 0 ? Theme.Gold : null),
            ("Podiums", Theme.Count(podiums), null),
            ("Median placing", MedianPlacing(tracks), null),
            ("Active for", $"{activeValue} {activeUnit}", null));

        var pair = UiKit.Row(_content, 18, align: TextAnchor.UpperLeft, name: "Pair");
        RenderBands(pair, tracks);
        RenderFindingTime(pair, tracks);

        RenderCourses(profile);
    }

    // ── Hero and ratings ────────────────────────────────────────────────────

    private void RenderHero(Transform parent, PilotProfile profile)
    {
        var pilot = profile.Pilot;
        var isMe = pilot.PublicId == _me.Known;

        var card = UiKit.Card(parent, name: "Hero");
        UiKit.Size(card, 0, -1, 1, 1);
        UiKit.Stripe(card, Theme.Accent, RectTransform.Edge.Left, 4, Theme.RadiusCard);
        var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(28, 24, 24, 24);
        row.spacing = 22;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        UiKit.Avatar(card, 88, pilot, _site, profile.LiveRoom != null ? Theme.Signal : null);

        var text = UiKit.Column(card, 8, name: "Who");
        UiKit.Size(text, 0, -1, 1);
        var name = UiKit.Label(text, pilot.Name, 40, isMe ? Theme.Accent : Theme.Ink200, UiKit.Heading, FontStyle.Bold);
        UiKit.Size(name, -1, 48);
        if (!string.IsNullOrWhiteSpace(pilot.PersonaName) && pilot.PersonaName != pilot.DisplayName)
            UiKit.Label(text, $"flying as {pilot.PersonaName}", 15, Theme.Ink600);

        var pills = UiKit.Row(text, 8, name: "Pills");
        if (profile.LiveRoom != null)
            UiKit.Pill(pills, "In lobby", Theme.Alpha(Theme.Signal, 0.15f), Theme.Signal);
        if (profile.JrDivision != null && !profile.JrProvisional)
        {
            var (fill, tone) = Theme.Division(profile.JrDivision);
            UiKit.Pill(pills, profile.JrDivision, fill, tone);
        }
        if (profile.CrLicense != null)
        {
            var (fill, tone) = Theme.License(profile.CrLicense);
            UiKit.Pill(pills, Theme.LicenseLabel(profile.CrLicense), fill, tone);
        }
        if (profile.PaceTopPct != null)
            UiKit.Pill(pills, $"Top {profile.PaceTopPct.Value:0}% pace", Theme.Ink750, Theme.Ink400);
        if (pilot.IsClaimed)
            UiKit.Pill(pills, "Steam linked", Theme.Ink750, Theme.Ink400);

        UiKit.Label(text, $"Flying here since {Theme.Month(profile.FirstSeenAt)} · last lap {Theme.SinceNow(profile.LastSeenAt)}", 15, Theme.Ink400);
        if (profile.LiveRoom != null)
            UiKit.Label(text, $"Currently in {profile.LiveRoom}", 15, Theme.Signal);

        var buttons = UiKit.Row(text, 10, name: "Buttons");
        var url = $"{_site.BaseUrl}/pilot/{pilot.PublicId}";
        UiKit.Button(buttons, "Open on the site", () => Application.OpenURL(url), UiKit.ButtonKind.Outline, 15, 34);
        if (!isMe)
        {
            UiKit.Button(buttons, "This is me", () =>
            {
                _me.Choose(pilot.PublicId);
                Render(profile);
            }, UiKit.ButtonKind.Ghost, 15, 34);
        }
    }

    private static void RenderRating(Transform parent, PilotProfile p)
    {
        var card = Blocks.CardColumn(parent, 6, 20, 16, "JmtRating");
        UiKit.Size(card, RatingWidth, -1, 0, 1);
        UiKit.Caption(card, "JMT Rating");
        if (p.JrRating == null)
        {
            UiKit.Label(card, "Not rated yet", 24, Theme.Ink400, UiKit.Heading, FontStyle.Bold);
            UiKit.Label(card, "A rating comes from racing others in JMT rooms.", 14, Theme.Ink600);
            return;
        }

        var line = UiKit.Row(card, 12);
        UiKit.OneLine(UiKit.Label(line, p.JrRating.Value.ToString(), 56, Theme.Accent, UiKit.Heading, FontStyle.Bold));
        if (p.JrMovement is { } movement && movement != 0)
            UiKit.OneLine(UiKit.Label(line, Blocks.Movement(movement), 18, Theme.Ink600, UiKit.Mono));
        UiKit.Spacer(line);
        if (p.JrProvisional)
        {
            UiKit.Pill(line, $"Placement {p.JrRaces}/{p.JrPlacementRaces}", Theme.Ink750, Theme.Ink400);
        }
        else if (p.JrDivision != null)
        {
            var (fill, tone) = Theme.Division(p.JrDivision);
            UiKit.Pill(line, p.JrDivision, fill, tone);
        }

        var facts = new List<string>();
        if (p.JrRank != null)
            facts.Add($"#{p.JrRank} of {Theme.Count(p.JrField)}");
        if (p.JrPeakRating != null)
            facts.Add($"peak {p.JrPeakRating}");
        facts.Add(p.JrRaces == 1 ? "1 race" : $"{Theme.Count(p.JrRaces)} races");
        UiKit.Label(card, string.Join(" · ", facts), 14, Theme.Ink400);
        if (p.JrNextDivision != null && p.JrNextDivisionAt != null)
            UiKit.Label(card, $"Next: {p.JrNextDivision} at {p.JrNextDivisionAt}", 14, Theme.Ink600);
    }

    private static void RenderConsistency(Transform parent, PilotProfile p)
    {
        var card = Blocks.CardColumn(parent, 6, 20, 16, "Consistency");
        UiKit.Size(card, RatingWidth, -1, 0, 1);
        UiKit.Caption(card, "Consistency Rating");
        if (p.CrRating == null)
        {
            UiKit.Label(card, "Not enough laps yet", 24, Theme.Ink400, UiKit.Heading, FontStyle.Bold);
            return;
        }

        var line = UiKit.Row(card, 12);
        UiKit.OneLine(UiKit.Label(line, p.CrRating.Value.ToString("0.00"), 44, Theme.Ink200, UiKit.Heading, FontStyle.Bold));
        UiKit.Spacer(line);
        if (p.CrLicense != null)
        {
            var (fill, tone) = Theme.License(p.CrLicense);
            UiKit.Pill(line, Theme.LicenseLabel(p.CrLicense), fill, tone);
        }

        var perLap = p.CrLaps == 0 ? 0 : p.CrFailed / (double)p.CrLaps;
        UiKit.Label(card, $"{Theme.Count(p.CrFailed)} failed attempts in {Theme.Count(p.CrLaps)} laps · {perLap:0.00} a lap", 14, Theme.Ink400);
        if (p.CrNextLicense != null && p.CrNextLicenseAt != null)
        {
            var next = $"Next: {Theme.LicenseLabel(p.CrNextLicense)} at {p.CrNextLicenseAt.Value:0.00}";
            if (p.CrNextLicenseLaps != null)
                next += $" and {Theme.Count(p.CrNextLicenseLaps.Value)} laps";
            UiKit.Label(card, next, 14, Theme.Ink600);
        }
    }

    private static void RenderPace(Transform parent, PilotProfile p)
    {
        var card = Blocks.CardColumn(parent, 6, 20, 16, "Pace");
        UiKit.Size(card, RatingWidth, -1, 0, 1);
        UiKit.Caption(card, "Repeatable pace");
        if (p.PaceTopPct == null)
        {
            UiKit.Label(card, "Not enough boards yet", 24, Theme.Ink400, UiKit.Heading, FontStyle.Bold);
            return;
        }
        var line = UiKit.Row(card, 12);
        UiKit.OneLine(UiKit.Label(line, $"Top {p.PaceTopPct.Value:0}%", 36, Theme.Ink200, UiKit.Heading, FontStyle.Bold));
        UiKit.OneLine(UiKit.Label(line, p.PaceBoards == 1 ? "over 1 board" : $"over {Theme.Count(p.PaceBoards)} boards", 15, Theme.Ink400));
    }

    // ── Where they place, and finding time ──────────────────────────────────

    /// <summary>
    /// How a pilot's courses split by placing. The bands are disjoint and add up to the
    /// course count, and each bar is scaled against the biggest band, as on the site.
    /// </summary>
    private static void RenderBands(Transform parent, List<PilotTrackRow> tracks)
    {
        var card = Blocks.CardColumn(parent, 12, 22, 18, "Bands");
        UiKit.Size(card, 0, -1, 1, 1);
        Blocks.Heading(card, "Where they place");

        var fastest = tracks.Count(t => t.Position == 1);
        var podiums = tracks.Count(t => t.Position <= 3);
        var top10 = tracks.Count(t => Theme.TopPct(t.Position, t.PilotCount) <= 10);
        var top25 = tracks.Count(t => Theme.TopPct(t.Position, t.PilotCount) <= 25);
        var bands = new (string Label, int Count, Color Bar, Color Text)[]
        {
            ("Fastest on the board", fastest, Theme.Gold, Theme.Gold),
            ("Second or third", podiums - fastest, Theme.Accent, Theme.Accent),
            ("Rest of the top 10%", top10 - podiums, Theme.AccentSoft, Theme.AccentSoft),
            ("Rest of the top quarter", top25 - top10, Theme.Ink400, Theme.Ink400),
            ("Outside the top quarter", tracks.Count - top25, Theme.Ink700, Theme.Ink600),
        };
        var widest = Math.Max(1, bands.Max(b => b.Count));

        foreach (var band in bands)
        {
            var block = UiKit.Column(card, 5, name: band.Label);
            var head = UiKit.Row(block, 8);
            var label = UiKit.Label(head, band.Label, 15, Theme.Ink400);
            UiKit.Size(label, 0, 20, 1);
            UiKit.OneLine(UiKit.Label(head, band.Count.ToString(), 15, band.Text, UiKit.Mono));

            var track = UiKit.Panel(block, Theme.Ink800, Theme.RadiusBadge, "Bar");
            UiKit.Size(track, -1, 8);
            if (band.Count == 0)
                continue;
            var fill = UiKit.Panel(track.transform, band.Bar, Theme.RadiusBadge, "Fill");
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(band.Count / (float)widest, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        }
    }

    /// <summary>The six newest personal bests: what this pilot has been improving lately.</summary>
    private void RenderFindingTime(Transform parent, List<PilotTrackRow> tracks)
    {
        var card = Blocks.CardColumn(parent, 4, 22, 18, "FindingTime");
        UiKit.Size(card, 0, -1, 1, 1);
        Blocks.Heading(card, "Finding time");
        if (tracks.Count == 0)
        {
            UiKit.Label(card, "No laps on a JMT board yet.", 15, Theme.Ink600);
            return;
        }

        foreach (var track in tracks.OrderByDescending(t => t.SetAt).Take(6))
        {
            var line = UiKit.Row(card, 12, name: "Pb");
            UiKit.Size(line, -1, 34);
            var day = UiKit.Label(line, Theme.Day(track.SetAt), 13, Theme.Ink600, UiKit.Mono);
            UiKit.Size(day, 64);
            var course = UiKit.Label(line, Title(track), 15, Theme.Ink200);
            UiKit.Size(course, 0, 20, 1);
            UiKit.OneLine(UiKit.Label(line, $"P{track.Position}", 15, Theme.Placing(track.Position, track.PilotCount), UiKit.Heading, FontStyle.Bold));
            UiKit.OneLine(UiKit.Label(line, Theme.LapTime(track.LapMs), 15, Theme.Signal, UiKit.Mono));

            var id = track.PublishedFileId;
            var surface = line.gameObject.AddComponent<Image>();
            surface.color = Theme.Ink850;
            UiKit.Stripe(line, Theme.Ink750, RectTransform.Edge.Bottom, 1);
            UiKit.Clickable(surface, () => _overlay.ShowBoard(id));
        }
    }

    // ── Best lap per course ─────────────────────────────────────────────────

    private void RenderCourses(PilotProfile profile)
    {
        Blocks.Heading(_content, "Best lap per course", $"{Theme.Count(profile.Tracks.Count)} courses");
        var card = Blocks.CardColumn(_content, 0, 0, 0, "Courses");

        var toolbar = UiKit.Row(card, 12, 18, 12, name: "Toolbar");
        UiKit.Segmented(toolbar, Tabs, _tab, index =>
        {
            _tab = index;
            RenderTable();
        });
        UiKit.OneLine(UiKit.Caption(toolbar, "Sort"));
        UiKit.Segmented(toolbar, SortNames, _sort, index =>
        {
            _sort = index;
            RenderTable();
        });
        UiKit.Spacer(toolbar);
        UiKit.Search(toolbar, "Find a course…", value =>
        {
            _query = value;
            _search.Run(RenderTable, 0.2f);
        }, 240);

        Blocks.TableHeader(card,
            ("Pos", PosWidth, TextAnchor.MiddleLeft),
            ("Course", 0, TextAnchor.MiddleLeft),
            ("Vs field", FieldWidth, TextAnchor.MiddleLeft),
            ("Laps", LapsWidth, TextAnchor.MiddleRight),
            ("Set", SetWidth, TextAnchor.MiddleRight),
            ("Best lap", BestWidth, TextAnchor.MiddleRight));

        _table = UiKit.Column(card, 0, name: "Rows");
        RenderTable();
    }

    private void RenderTable()
    {
        if (_table == null || _profile == null)
            return;
        UiKit.Clear(_table);

        IEnumerable<PilotTrackRow> rows = _profile.Tracks;
        rows = _tab switch
        {
            1 => rows.Where(t => t.Position <= 3),
            2 => rows.Where(t => t.Position == 1),
            _ => rows,
        };
        if (!string.IsNullOrWhiteSpace(_query))
            rows = rows.Where(t => Title(t).IndexOf(_query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        rows = _sort switch
        {
            1 => rows.OrderBy(t => Theme.TopPct(t.Position, t.PilotCount)),
            2 => rows.OrderByDescending(t => t.PilotCount),
            3 => rows.OrderByDescending(t => t.Laps),
            _ => rows.OrderByDescending(t => t.SetAt),
        };

        var shown = rows.ToList();
        if (shown.Count == 0)
        {
            var none = UiKit.Label(_table, "No course matches.", 16, Theme.Ink600, align: TextAnchor.MiddleCenter);
            UiKit.Size(none, -1, 60);
            return;
        }
        foreach (var track in shown)
            AddCourseRow(track);
    }

    private void AddCourseRow(PilotTrackRow track)
    {
        var tone = Theme.Placing(track.Position, track.PilotCount);
        var line = UiKit.Panel(_table!, Theme.Ink850, 0, "Course");
        UiKit.Size(line, -1, 44);
        var layout = line.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 0, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        UiKit.Stripe(line.transform, Theme.Ink750, RectTransform.Edge.Bottom, 1);

        var pos = UiKit.Label(line.transform, $"{track.Position}<color={Theme.Html(Theme.Ink600)}>/{track.PilotCount}</color>", 17, tone, UiKit.Heading, FontStyle.Bold);
        UiKit.Size(pos, PosWidth);
        var course = UiKit.Label(line.transform, Title(track), 16, Theme.Ink200);
        UiKit.Size(course, 0, 22, 1);
        course.verticalOverflow = VerticalWrapMode.Truncate;
        if (track.Kind != "workshop")
            UiKit.Badge(line.transform, track.Kind == "stock" ? "Liftoff" : "Unlinked", Theme.Ink750, Theme.Ink400);

        FieldMarker(line.transform, track, tone);

        var laps = UiKit.Label(line.transform, Theme.Count(track.Laps), 14, Theme.Ink600, UiKit.Mono, FontStyle.Normal, TextAnchor.MiddleRight);
        UiKit.Size(laps, LapsWidth);
        var set = UiKit.Label(line.transform, Theme.RelativeDate(track.SetAt), 14, Theme.Ink600, align: TextAnchor.MiddleRight);
        UiKit.Size(set, SetWidth);
        var best = UiKit.Label(line.transform, Theme.LapTime(track.LapMs), 18, track.Position == 1 ? Theme.Gold : Theme.Signal,
            UiKit.Heading, FontStyle.Bold, TextAnchor.MiddleRight);
        UiKit.Size(best, BestWidth);

        var id = track.PublishedFileId;
        UiKit.Clickable(line, () => _overlay.ShowBoard(id));
    }

    /// <summary>
    /// Where on the field this placing sits, front on the left. Square-rooted, as on the
    /// site: a linear scale crushes every good placing into the first few pixels.
    /// </summary>
    private static void FieldMarker(Transform parent, PilotTrackRow track, Color tone)
    {
        var topPct = Theme.TopPct(track.Position, track.PilotCount);
        var cell = UiKit.Row(parent, 10, name: "Field");
        UiKit.Size(cell, FieldWidth);

        var bar = UiKit.Node("Bar", cell);
        UiKit.Size(bar, 140, 12);
        var line = UiKit.Panel(bar, Theme.Ink800, 2, "Track");
        line.rectTransform.anchorMin = new Vector2(0, 0.5f);
        line.rectTransform.anchorMax = new Vector2(1, 0.5f);
        line.rectTransform.sizeDelta = new Vector2(0, 4);

        var left = Mathf.Clamp(Mathf.Sqrt((float)(topPct / 100)) * 100, 4, 96) / 100f;
        var mark = UiKit.Panel(bar, tone, 0, "Mark");
        mark.sprite = UiKit.Circle();
        mark.rectTransform.anchorMin = mark.rectTransform.anchorMax = new Vector2(left, 0.5f);
        mark.rectTransform.sizeDelta = new Vector2(10, 10);

        UiKit.OneLine(UiKit.Label(cell, topPct < 1 ? "<1%" : $"{topPct:0}%", 13, Theme.Ink600, UiKit.Mono));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A course flown in a workshop item that has since been unpublished has no title in
    /// the catalogue; its id is still a true name for it.
    /// </summary>
    private static string Title(PilotTrackRow track) =>
        string.IsNullOrWhiteSpace(track.Title) ? $"Workshop item {track.PublishedFileId}" : track.Title;

    /// <summary>"Top 12%": the middle of where this pilot places.</summary>
    private static string MedianPlacing(List<PilotTrackRow> tracks)
    {
        if (tracks.Count == 0)
            return "—";
        var sorted = tracks.Select(t => Theme.TopPct(t.Position, t.PilotCount)).OrderBy(p => p).ToList();
        return $"Top {sorted[sorted.Count / 2]:0}%";
    }

    /// <summary>How long they have flown here, as a duration: "2.1 years" rather than a date.</summary>
    private static (string Value, string Unit) ActiveFor(DateTimeOffset since)
    {
        var days = Math.Max(0, (int)Math.Round((DateTimeOffset.UtcNow - since).TotalDays));
        if (days < 60)
            return (days.ToString(), days == 1 ? "day" : "days");
        if (days < 365)
            return (Math.Round(days / 30.0).ToString("0"), "months");
        return ((days / 365.0).ToString("0.0"), "years");
    }
}
