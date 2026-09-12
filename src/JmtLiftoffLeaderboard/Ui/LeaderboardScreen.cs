using System.Collections.Generic;
using System.Linq;
using JmtLiftoffLeaderboard.Game;
using JmtLiftoffLeaderboard.Site;
using UnityEngine;
using UnityEngine.UI;

namespace JmtLiftoffLeaderboard.Ui;

/// <summary>
/// A JMT board, laid out like a track page on the site (web/src/routes/ItemDetail.tsx):
/// the course, its stat strip, the podium, the board itself and the latest laps. The
/// rail on the left lists every course that has a board.
/// </summary>
internal sealed class LeaderboardScreen
{
    private static readonly int[] Limits = { 10, 25, 500 };
    private static readonly string[] Sorts = { "recent", "pilots", "title" };
    private const int PageSize = 50;

    // Board columns: Pos | Pilot | Best | Gap | Laps, in the site's proportions.
    private const float PosWidth = 96;
    private const float BestWidth = 130;
    private const float GapWidth = 110;
    private const float LapsWidth = 80;

    private readonly SiteClient _site;
    private readonly LocalPilot _me;
    private readonly Overlay _overlay;
    private readonly Debouncer _listSearch = new();
    private readonly Debouncer _pilotSearch = new();

    private RectTransform _list = null!;
    private Text _listStatus = null!;
    private RectTransform _main = null!;
    private ScrollRect _mainScroll = null!;
    private GameObject? _loadMore;

    private readonly List<BoardSummary> _boards = new();
    private readonly Dictionary<long, Image> _entryMarks = new();
    private string _listQuery = "";
    private int _sortIndex;
    private int _listRequest;
    private bool _listLoaded;
    private TrackRef? _flying;

    private long? _boardId;
    private long? _renderedFor;
    private int _limitIndex = 1;
    private string _pilotQuery = "";
    private int _boardRequest;
    private Leaderboard? _board;
    private ItemInfo? _item;
    private string? _mePublicId;
    private string? _expanded;

    private RectTransform? _hero;
    private RectTransform? _rows;
    private RectTransform? _standing;
    private Text? _showing;

    public LeaderboardScreen(RectTransform root, SiteClient site, LocalPilot me, Overlay overlay)
    {
        _site = site;
        _me = me;
        _overlay = overlay;
        Build(root);
    }

    /// <summary>
    /// Show the list, and a board: <paramref name="boardId"/> if given, else the course
    /// being flown, else whatever was open last, else the top of the list.
    /// </summary>
    public void Open(TrackRef? flying, long? boardId)
    {
        if (flying != null)
            _flying = flying;
        if (!_listLoaded)
            LoadList(reset: true);

        var target = boardId ?? flying?.BoardId;
        if (target != null)
            ShowBoard(target.Value);
        else if (flying != null)
            FindBoardByName(flying);
        else if (_boardId != null)
            LoadBoard();
    }

    private void Build(RectTransform root)
    {
        var layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 20;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        var rail = UiKit.Card(root, name: "Courses");
        UiKit.Size(rail, 380, -1, 0, 1);
        var railLayout = rail.gameObject.AddComponent<VerticalLayoutGroup>();
        railLayout.padding = new RectOffset(16, 16, 16, 12);
        railLayout.spacing = 12;
        railLayout.childControlWidth = true;
        railLayout.childControlHeight = true;
        railLayout.childForceExpandWidth = true;
        railLayout.childForceExpandHeight = false;

        UiKit.Caption(rail, "Courses with a JMT board");
        UiKit.Search(rail, "Find a course…", value =>
        {
            _listQuery = value;
            _listSearch.Run(() => LoadList(reset: true));
        }, 340);
        var sortRow = UiKit.Row(rail, 0);
        UiKit.Segmented(sortRow, new[] { "Recent", "Busiest", "A–Z" }, 0, index =>
        {
            _sortIndex = index;
            LoadList(reset: true);
        });
        _list = UiKit.Scroll(rail, out _, 6);
        UiKit.Size(_list.parent, -1, -1, 1, 1);
        _listStatus = UiKit.Label(rail, "", 14, Theme.Ink600);
        UiKit.Size(_listStatus, -1, 22);

        _main = UiKit.Scroll(root, out _mainScroll, 18);
        UiKit.Size(_main.parent, -1, -1, 1, 1);
    }

    // ── The course list ─────────────────────────────────────────────────────

    private void LoadList(bool reset)
    {
        var request = ++_listRequest;
        var offset = reset ? 0 : _boards.Count;
        _listStatus.text = "Loading courses…";
        _site.Boards(_listQuery, null, Sorts[_sortIndex], PageSize, offset, result =>
        {
            if (request != _listRequest)
                return;
            if (!result.Ok)
            {
                _listStatus.text = result.Error ?? "Couldn't load the courses.";
                return;
            }

            _listLoaded = true;
            var page = result.Value!;
            if (reset)
            {
                _boards.Clear();
                _entryMarks.Clear();
                UiKit.Clear(_list);
                AddFlyingEntry();
            }
            if (_loadMore != null)
                Object.Destroy(_loadMore);

            _boards.AddRange(page.Items);
            foreach (var board in page.Items)
                AddEntry(board.PublishedFileId, board.Title, ListMeta(board));

            _listStatus.text = page.Total == 0
                ? string.IsNullOrWhiteSpace(_listQuery) ? "No boards yet." : "No course matches that."
                : $"{Theme.Count(_boards.Count)} of {Theme.Count(page.Total)} courses";
            if (_boards.Count < page.Total)
            {
                var more = UiKit.Button(_list, "Load more", () => LoadList(reset: false), UiKit.ButtonKind.Ghost, 15, 34);
                _loadMore = more.gameObject;
            }

            if (_boardId == null && _flying == null && _boards.Count > 0)
                ShowBoard(_boards[0].PublishedFileId);
        });
    }

    private static string ListMeta(BoardSummary board)
    {
        var parts = new List<string>();
        if (board.Kind == "stock")
            parts.Add($"<color={Theme.Html(Theme.Cyan)}>Liftoff</color>");
        else if (board.Kind == "unlinked")
            parts.Add($"<color={Theme.Html(Theme.Ink400)}>Unlinked</color>");
        if (!string.IsNullOrEmpty(board.EnvironmentDisplay))
            parts.Add(board.EnvironmentDisplay!);
        parts.Add(board.PilotCount == 1 ? "1 pilot" : $"{Theme.Count(board.PilotCount)} pilots");
        parts.Add($"<color={Theme.Html(Theme.Accent)}>{Theme.LapTime(board.FastestLapMs)}</color>");
        return string.Join(" · ", parts);
    }

    /// <summary>The course being flown, pinned above the list when it has a board.</summary>
    private void AddFlyingEntry()
    {
        if (_flying?.BoardId == null || !string.IsNullOrWhiteSpace(_listQuery))
            return;
        AddEntry(_flying.BoardId.Value, _flying.Name, $"<color={Theme.Html(Theme.Signal)}>Flying now</color>");
    }

    private void AddEntry(long id, string title, string meta)
    {
        var entry = UiKit.Panel(_list, Theme.Ink800, Theme.RadiusControl, "Course");
        var layout = entry.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 12, 8, 8);
        layout.spacing = 2;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var mark = UiKit.Stripe(entry.transform, Theme.Accent, RectTransform.Edge.Left, 3);
        mark.enabled = id == _boardId;
        var name = UiKit.Label(entry.transform, string.IsNullOrWhiteSpace(title) ? $"Workshop item {id}" : title, 16, Theme.Ink200, UiKit.Body, FontStyle.Bold);
        UiKit.Size(name, -1, 22);
        var info = UiKit.Label(entry.transform, meta, 13, Theme.Ink600);
        UiKit.Size(info, -1, 18);
        UiKit.Clickable(entry, () => ShowBoard(id));

        // The pinned "Flying now" entry and its list twin share an id; both light up.
        if (_entryMarks.TryGetValue(id, out var twin) && twin != null)
            _entryMarks[-id] = twin;
        _entryMarks[id] = mark;
    }

    private void MarkSelected()
    {
        foreach (var pair in _entryMarks)
        {
            if (pair.Value != null)
                pair.Value.enabled = System.Math.Abs(pair.Key) == _boardId;
        }
    }

    // ── One board ───────────────────────────────────────────────────────────

    public void ShowBoard(long id)
    {
        if (_boardId != id)
        {
            _expanded = null;
            _pilotQuery = "";
            _renderedFor = null;
        }
        _boardId = id;
        MarkSelected();
        LoadBoard();
    }

    private void LoadBoard()
    {
        if (_boardId == null)
            return;
        var id = _boardId.Value;
        var request = ++_boardRequest;

        if (_renderedFor != id)
        {
            UiKit.Clear(_main);
            Blocks.Loading(_main, "Loading the board…");
            _item = null;
            // A named board has no catalogue item behind it; the board says what it is.
            if (id > 0)
            {
                _site.Item(id, result =>
                {
                    if (request != _boardRequest && _boardId != id)
                        return;
                    _item = result.Value;
                    RenderHero();
                });
            }
        }

        _me.Resolve(me =>
        {
            if (_mePublicId == me)
                return;
            _mePublicId = me;
            if (_renderedFor == id)
                RenderRows();
        });

        _site.Leaderboard(id, Limits[_limitIndex], _pilotQuery, result =>
        {
            if (request != _boardRequest)
                return;
            if (!result.Ok)
            {
                _renderedFor = null;
                UiKit.Clear(_main);
                Blocks.Message(_main, "Couldn't load this board", result.Error ?? "Try again in a moment.", ("Try again", () =>
                {
                    _site.Forget();
                    LoadBoard();
                }));
                return;
            }

            _board = result.Value!;
            // A Workshop course the site couldn't match keeps its laps on a board
            // named after it, so an empty Workshop board is worth one look by name.
            if (_board.PilotCount == 0 && id > 0 && _flying?.BoardId == id && !_flying.LookedUpByName)
            {
                FindBoardByName(_flying);
                return;
            }
            if (_renderedFor == id)
            {
                // Same board, new search or size: only the rows change, so the search box keeps focus.
                RenderRows();
            }
            else
            {
                Render(id);
            }
        });
    }

    private void Render(long id)
    {
        var board = _board!;
        _renderedFor = id;
        UiKit.Clear(_main);
        _mainScroll.verticalNormalizedPosition = 1;

        _hero = UiKit.Column(_main, 0, name: "Hero");
        RenderHero();

        if (board.PilotCount == 0)
        {
            _rows = null;
            Blocks.Message(_main, "Nobody has flown this in a JMT room yet",
                "Laps flown on this course in a JMT room show up here, fastest first.");
            return;
        }

        Blocks.StatStrip(_main,
            ("Pilots", Theme.Count(board.PilotCount), null),
            ("Laps", Theme.Count(board.LapCount), null),
            ("Fastest lap", Theme.LapTime(board.FastestLapMs), Theme.Accent),
            ("Median", Theme.LapTime(board.MedianLapMs), null),
            ("Set today", board.PbsToday == 1 ? "1 PB" : $"{board.PbsToday} PBs", board.PbsToday > 0 ? Theme.Signal : null));

        var last = board.Recent.Count > 0 ? board.Recent[0].RecordedAt : board.UpdatedAt;
        Blocks.Heading(_main, "Fastest laps", last == null ? null : $"Last lap {Theme.SinceNow(last)}");
        RenderPodium(board);
        RenderBoardCard();
        if (board.Recent.Count > 0)
            RenderRecent(board);
    }

    private void RenderHero()
    {
        if (_hero == null)
            return;
        UiKit.Clear(_hero);

        var title = !string.IsNullOrWhiteSpace(_item?.Title) ? _item!.Title
            : !string.IsNullOrWhiteSpace(_board?.Title) ? _board!.Title
            : $"Workshop item {_boardId}";

        var card = UiKit.Card(_hero, name: "Course");
        UiKit.Stripe(card, Theme.Accent, RectTransform.Edge.Top, 2, Theme.RadiusCard);
        var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(18, 18, 18, 18);
        row.spacing = 22;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        if (_boardId > 0)
            UiKit.Picture(card, _item?.PreviewUrl, _site, 288, 162);

        var text = UiKit.Column(card, 6, name: "Text");
        UiKit.Size(text, 0, -1, 1);
        var where = _item?.EnvironmentDisplay ?? _board?.EnvironmentDisplay ?? _board?.Environment;
        if (!string.IsNullOrEmpty(where))
            UiKit.Caption(text, where!);
        var name = UiKit.Label(text, title, 38, Theme.Ink200, UiKit.Heading, FontStyle.Bold);
        name.verticalOverflow = VerticalWrapMode.Overflow;

        var facts = new List<string>();
        if (!string.IsNullOrEmpty(_item?.Creator?.PersonaName))
            facts.Add($"by {_item!.Creator!.PersonaName}");
        if (_item?.ContentType is "race" or "track")
            facts.Add(_item.ContentType == "race" ? "Race" : "Track");
        if (_flying?.BoardId == _boardId)
            facts.Add($"<color={Theme.Html(Theme.Signal)}>you're flying this</color>");
        if (facts.Count > 0)
            UiKit.Label(text, string.Join(" · ", facts), 16, Theme.Ink400);
        if (_boardId > 0)
        {
            UiKit.Label(text, $"Workshop id {_boardId}", 13, Theme.Ink600, UiKit.Mono);
            return;
        }

        // A named board: say what it is, since there is no Workshop page to.
        var stock = _board?.Kind == "stock";
        var tags = UiKit.Row(text, 8);
        UiKit.Pill(tags, stock ? "Liftoff track" : "Not on the Steam Workshop",
            stock ? Theme.Alpha(Theme.Cyan, 0.15f) : Theme.Ink750, stock ? Theme.Cyan : Theme.Ink400);
        UiKit.Label(text, stock
            ? "One of Liftoff's own tracks. It comes with the game, so there's nothing to subscribe to."
            : "Flown in a JMT room without a Steam Workshop id the site could match, so it's listed under the name the game gave it.",
            15, Theme.Ink600);
    }

    private void RenderPodium(Leaderboard board)
    {
        var top = board.Rows.Where(r => r.Position <= 3).OrderBy(r => r.Position).ToList();
        if (top.Count == 0 || !string.IsNullOrWhiteSpace(_pilotQuery))
            return;

        var row = UiKit.Row(_main, 16, align: TextAnchor.UpperLeft, name: "Podium");
        foreach (var entry in top)
        {
            var place = entry.Position;
            var colour = Theme.Place(place);
            var card = Blocks.CardColumn(row, 6, 18, 16, $"P{place}");
            UiKit.Size(card, 0, -1, 1, 1);
            UiKit.Stripe(card, colour, RectTransform.Edge.Top, 2, Theme.RadiusCard);

            var head = UiKit.Row(card, 10);
            UiKit.OneLine(UiKit.Label(head, $"P{place}", 30, colour, UiKit.Heading, FontStyle.Bold));
            UiKit.OneLine(UiKit.Caption(head, place switch { 1 => "Track record", 2 => "Silver", _ => "Bronze" }, colour));

            var who = UiKit.Row(card, 10);
            UiKit.Avatar(who, 28, entry.Pilot, _site);
            var name = UiKit.Label(who, entry.Pilot.Name, 17, entry.Pilot.PublicId == _mePublicId ? Theme.Accent : Theme.Ink200, UiKit.Body, FontStyle.Bold);
            UiKit.Size(name, 0, 24, 1);

            var time = UiKit.Label(card, Theme.LapTime(entry.LapMs), 36, Theme.Ink200, UiKit.Heading, FontStyle.Bold);
            UiKit.Size(time, -1, 44);
            var laps = entry.Laps == 1 ? "1 lap" : $"{Theme.Count(entry.Laps)} laps";
            UiKit.Label(card, place == 1 ? laps : $"{Theme.Gap(entry.LapMs - board.FastestLapMs)} · {laps}", 14, Theme.Ink600, UiKit.Mono);

            var id = entry.Pilot.PublicId;
            UiKit.Clickable(card.GetComponent<Image>(), () => _overlay.ShowPilot(id));
        }
    }

    private void RenderBoardCard()
    {
        var card = Blocks.CardColumn(_main, 0, 0, 0, "Board");

        var toolbar = UiKit.Row(card, 12, 18, 12, name: "Toolbar");
        _showing = UiKit.OneLine(UiKit.Label(toolbar, "", 15, Theme.Ink400));
        UiKit.Spacer(toolbar);
        UiKit.Search(toolbar, "Find a pilot…", value =>
        {
            _pilotQuery = value;
            _pilotSearch.Run(LoadBoard);
        }, 240);
        UiKit.Segmented(toolbar, new[] { "Top 10", "Top 25", "All" }, _limitIndex, index =>
        {
            _limitIndex = index;
            LoadBoard();
        });

        _standing = UiKit.Column(card, 0, name: "Standing");

        Blocks.TableHeader(card,
            ("Pos", PosWidth, TextAnchor.MiddleLeft),
            ("Pilot", 0, TextAnchor.MiddleLeft),
            ("Best", BestWidth, TextAnchor.MiddleRight),
            ("Gap", GapWidth, TextAnchor.MiddleRight),
            ("Laps", LapsWidth, TextAnchor.MiddleRight));

        _rows = UiKit.Column(card, 0, name: "Rows");

        var footer = UiKit.Label(card, "Gap is measured from P1: the time to beat.", 13, Theme.Ink600);
        UiKit.Size(footer, -1, 40);
        footer.alignment = TextAnchor.MiddleCenter;

        RenderRows();
    }

    private void RenderRows()
    {
        if (_rows == null || _board == null)
            return;
        var board = _board;
        UiKit.Clear(_rows);

        if (_showing != null)
        {
            _showing.text = !string.IsNullOrWhiteSpace(_pilotQuery)
                ? $"{Theme.Count(board.MatchCount)} of {Theme.Count(board.PilotCount)} pilots match"
                : board.Rows.Count >= board.PilotCount
                    ? $"All {Theme.Count(board.PilotCount)} pilots"
                    : $"Showing {Theme.Count(board.Rows.Count)} of {Theme.Count(board.PilotCount)} pilots";
        }

        if (board.Rows.Count == 0)
        {
            var none = UiKit.Label(_rows, "No pilot by that name on this board.", 16, Theme.Ink600, align: TextAnchor.MiddleCenter);
            UiKit.Size(none, -1, 60);
        }

        foreach (var row in board.Rows)
        {
            AddRow(row, board.FastestLapMs);
            if (row.Pilot.PublicId == _expanded)
                AddDetail(row);
        }

        RenderStanding();
    }

    private void AddRow(LeaderboardRow row, int? leaderMs)
    {
        var id = row.Pilot.PublicId;
        var isMe = id == _mePublicId;
        var podium = row.Position <= 3;
        var fill = isMe ? Color.Lerp(Theme.Ink850, Theme.Accent, 0.1f) : podium ? Theme.Ink900 : Theme.Ink850;

        var line = UiKit.Panel(_rows!, fill, 0, $"P{row.Position}");
        UiKit.Size(line, -1, 48);
        var layout = line.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 0, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        var markColour = isMe ? Theme.Accent : podium ? Theme.Place(row.Position) : new Color(0, 0, 0, 0);
        UiKit.Stripe(line.transform, markColour, RectTransform.Edge.Left, 3);
        UiKit.Stripe(line.transform, Theme.Ink750, RectTransform.Edge.Bottom, 1);

        var pos = UiKit.Row(line.transform, 6, name: "Pos");
        UiKit.Size(pos, PosWidth);
        UiKit.OneLine(UiKit.Label(pos, $"P{row.Position}", 18,
            podium ? Theme.Place(row.Position) : isMe ? Theme.Accent : Theme.Ink600, UiKit.Heading, FontStyle.Bold));
        UiKit.OneLine(UiKit.Label(pos, Blocks.Movement(row.Movement), 12, Theme.Ink600, UiKit.Mono));

        var who = UiKit.Row(line.transform, 10, name: "Pilot");
        UiKit.Size(who, 0, -1, 1);
        UiKit.Avatar(who, 26, row.Pilot, _site);
        var name = UiKit.Label(who, row.Pilot.Name, 17, isMe ? Theme.Accent : Theme.Ink200, UiKit.Body, isMe ? FontStyle.Bold : FontStyle.Normal);
        // The name takes its own width, so a badge sits right beside it, as on the site.
        UiKit.Size(name, -1, 24);
        if (isMe)
            UiKit.Badge(who, "You", Theme.Accent, Theme.Ink950);
        else if (!row.Pilot.IsClaimed)
            UiKit.Badge(who, "Guest", Theme.Ink750, Theme.Ink600);
        UiKit.Spacer(who);

        var best = UiKit.Label(line.transform, Theme.LapTime(row.LapMs), 20, Theme.Ink200, UiKit.Heading, FontStyle.Bold, TextAnchor.MiddleRight);
        UiKit.Size(best, BestWidth);
        var leader = row.Position == 1;
        var gap = UiKit.Label(line.transform, leader ? "leader" : Theme.Gap(row.LapMs - leaderMs), 14,
            leader ? Theme.Accent : Theme.Ink400, UiKit.Mono, FontStyle.Normal, TextAnchor.MiddleRight);
        UiKit.Size(gap, GapWidth);
        var laps = UiKit.Label(line.transform, Theme.Count(row.Laps), 14, Theme.Ink600, UiKit.Mono, FontStyle.Normal, TextAnchor.MiddleRight);
        UiKit.Size(laps, LapsWidth);

        UiKit.Clickable(line, () =>
        {
            _expanded = _expanded == id ? null : id;
            RenderRows();
        });
    }

    private void AddDetail(LeaderboardRow row)
    {
        var id = row.Pilot.PublicId;
        var detail = UiKit.Panel(_rows!, Theme.Ink900, 0, "Detail");
        var layout = detail.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset((int)PosWidth + 18, 18, 12, 14);
        layout.spacing = 32;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        UiKit.Stripe(detail.transform, Theme.Ink750, RectTransform.Edge.Bottom, 1);

        Blocks.Fact(detail.transform, "Laps flown", Theme.Count(row.Laps));
        Blocks.Fact(detail.transform, "Best set", Theme.SinceNow(row.SetAt));
        if (row.ImprovementMs is > 0)
            Blocks.Fact(detail.transform, "Improvement", $"{Theme.Seconds(row.ImprovementMs.Value)}s quicker", Theme.Signal);
        if (row.ConsistencyMs != null)
            Blocks.Fact(detail.transform, "Consistency", $"±{Theme.Seconds(row.ConsistencyMs.Value)}s");

        UiKit.Spacer(detail.transform);
        UiKit.Button(detail.transform, "View profile", () => _overlay.ShowPilot(id), UiKit.ButtonKind.Outline, 15, 34);
        if (id != _mePublicId)
        {
            UiKit.Button(detail.transform, "This is me", () =>
            {
                _me.Choose(id);
                _mePublicId = id;
                RenderRows();
            }, UiKit.ButtonKind.Ghost, 15, 34);
        }
    }

    /// <summary>
    /// "Your standing", when you are on this board but outside the rows shown. Looked up
    /// from the whole board, which the site answers from cache after the first time.
    /// </summary>
    private void RenderStanding()
    {
        if (_standing == null || _board == null || _boardId == null)
            return;
        UiKit.Clear(_standing);
        var me = _mePublicId;
        if (me == null || !string.IsNullOrWhiteSpace(_pilotQuery) || _board.Rows.Any(r => r.Pilot.PublicId == me))
            return;
        if (_board.Rows.Count >= _board.PilotCount)
            return;

        var id = _boardId.Value;
        _site.Leaderboard(id, Limits[Limits.Length - 1], null, result =>
        {
            var mine = result.Value?.Rows.FirstOrDefault(r => r.Pilot.PublicId == me);
            if (mine == null || _standing == null || _boardId != id || _mePublicId != me)
                return;
            UiKit.Clear(_standing);
            var band = UiKit.Panel(_standing, Color.Lerp(Theme.Ink850, Theme.Accent, 0.1f), 0, "YourStanding");
            UiKit.Size(band, -1, 44);
            UiKit.Stripe(band.transform, Theme.Accent, RectTransform.Edge.Left, 3);
            var row = band.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(18, 18, 0, 0);
            row.spacing = 18;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            UiKit.OneLine(UiKit.Caption(band.transform, "Your standing", Theme.Accent));
            UiKit.OneLine(UiKit.Label(band.transform, $"P{mine.Position}", 18, Theme.Accent, UiKit.Heading, FontStyle.Bold));
            UiKit.OneLine(UiKit.Label(band.transform, Theme.LapTime(mine.LapMs), 18, Theme.Ink200, UiKit.Heading, FontStyle.Bold));
            UiKit.OneLine(UiKit.Label(band.transform, $"{Theme.Gap(mine.LapMs - _board!.FastestLapMs)} · {Theme.Count(mine.Laps)} laps", 14, Theme.Ink400, UiKit.Mono));
        });
    }

    private void RenderRecent(Leaderboard board)
    {
        Blocks.Heading(_main, "Recent laps");
        var card = Blocks.CardColumn(_main, 0, 0, 6, "Recent");
        foreach (var lap in board.Recent)
        {
            var line = UiKit.Row(card, 12, 18, 0, name: "Lap");
            UiKit.Size(line, -1, 44);
            UiKit.Avatar(line, 24, lap.Pilot, _site);
            var name = UiKit.Label(line, lap.Pilot.Name, 16, lap.Pilot.PublicId == _mePublicId ? Theme.Accent : Theme.Ink200);
            UiKit.Size(name, 0, 22, 1);

            var time = Theme.LapTime(lap.LapMs) + (lap.IsPersonalBest ? " · PB" : "");
            UiKit.OneLine(UiKit.Label(line, time, 16, lap.IsPersonalBest ? Theme.Signal : Theme.Ink200, UiKit.Mono));
            var delta = lap.DeltaMs switch
            {
                null => "",
                < 0 => $"−{Theme.Seconds(-lap.DeltaMs.Value)}",
                _ => $"+{Theme.Seconds(lap.DeltaMs.Value)}",
            };
            var deltaLabel = UiKit.Label(line, delta, 13, lap.DeltaMs < 0 ? Theme.Signal : Theme.Ink600, UiKit.Mono, FontStyle.Normal, TextAnchor.MiddleRight);
            UiKit.Size(deltaLabel, 80);
            var ago = UiKit.Label(line, Theme.SinceNow(lap.RecordedAt), 13, Theme.Ink600, align: TextAnchor.MiddleRight);
            UiKit.Size(ago, 90);

            var id = lap.Pilot.PublicId;
            var surface = line.gameObject.AddComponent<Image>();
            surface.color = Theme.Ink850;
            UiKit.Stripe(line, Theme.Ink750, RectTransform.Edge.Bottom, 1);
            UiKit.Clickable(surface, () => _overlay.ShowPilot(id));
        }
    }

    /// <summary>
    /// A course with no Workshop id -- one of Liftoff's own, or a Workshop course that
    /// reached the site without one -- has a board under its track name. Found by
    /// searching the list for that name, preferring the one in this environment.
    /// </summary>
    private void FindBoardByName(TrackRef flying)
    {
        flying.LookedUpByName = true;
        var name = flying.TrackName.Trim();
        if (name.Length == 0)
        {
            ShowNoBoard(flying);
            return;
        }

        UiKit.Clear(_main);
        Blocks.Loading(_main, $"Finding {name}…");
        var request = ++_boardRequest;
        _site.Boards(name, null, "title", 50, 0, result =>
        {
            if (request != _boardRequest)
                return;
            var named = (result.Value?.Items ?? new List<BoardSummary>())
                .Where(b => b.PublishedFileId < 0 && string.Equals(b.Title.Trim(), name, System.StringComparison.OrdinalIgnoreCase))
                .ToList();
            var match = named.FirstOrDefault(b => flying.InEnvironment(b.Environment, b.EnvironmentDisplay)) ?? named.FirstOrDefault();
            if (match == null)
            {
                ShowNoBoard(flying);
                return;
            }
            flying.BoardId = match.PublishedFileId;
            ShowBoard(match.PublishedFileId);
            // Pin it at the top of the list as the course being flown.
            if (_listLoaded)
                LoadList(reset: true);
        });
    }

    /// <summary>A course nobody has flown in a JMT room yet, so it has no board to show.</summary>
    private void ShowNoBoard(TrackRef flying)
    {
        _boardId = null;
        _renderedFor = null;
        _rows = null;
        MarkSelected();
        UiKit.Clear(_main);
        var name = string.IsNullOrWhiteSpace(flying.Name) ? "This course" : flying.Name;
        Blocks.Message(_main, $"Nobody has flown {name} in a JMT room yet",
            "Its JMT board starts with the first lap flown on it in a JMT room. Until then, " +
            "Liftoff's own leaderboard has it, and any course on the left has a JMT board.",
            ("Liftoff leaderboard", _overlay.OpenNative));
    }
}
