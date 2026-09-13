using System;
using System.Collections.Generic;
using System.Linq;
using JmtLiftoffLeaderboard.Game;
using UnityEngine;
using UnityEngine.UI;
using static JmtLiftoffLeaderboard.Ui.HudText;

namespace JmtLiftoffLeaderboard.Ui;

/// <summary>
/// The course's JMT board around the pilot's place, for knowing what to aim for next: the
/// few places above theirs with the gap to each, the exact time that takes the next one,
/// and a ruler of lap times with tonight's laps on it, so they can see how close their
/// laps are landing. Their own laps move them on it the moment they're flown, before the
/// site has them, and a rival flying in the same room is marked.
/// </summary>
internal sealed class BoardPanel : HudPanel
{
    private const int MaxRows = 8; // five above, theirs, two below
    private const int RulerDots = 12;
    private const int NameLength = 16;
    private const float RulerLine = 0.35f;

    private readonly Settings _settings;
    private readonly BoardTracker _board;
    private readonly RoomWatch _room;

    private Text _state = null!;
    private Text _position = null!;
    private Text _title = null!;
    private readonly List<PlaceRow> _rows = new();
    private Text _message = null!;
    private Text _target = null!;
    private RectTransform _ruler = null!;
    private readonly List<Image> _ticks = new();
    private readonly List<Text> _tickLabels = new();
    private readonly List<Image> _dots = new();
    private Text _foot = null!;

    private string? _news;
    private Action<int>? _paintAbove;
    private Action<int>? _paintBelow;
    private Action<int>? _paintRuler;

    public BoardPanel(Settings settings, BoardTracker board, RoomWatch room) : base("Board", settings.Board)
    {
        _settings = settings;
        _board = board;
        _room = room;
        board.Changed += MarkDirty;
        board.News += OnNews;
        room.Changed += MarkDirty;
        Watch(settings.BoardAbove);
        Watch(settings.BoardBelow);
        Watch(settings.BoardRuler);
    }

    protected override float Width => 400;

    protected override bool HasContent => _board.State != BoardState.NoTrack;

    private void OnNews(BoardNews news)
    {
        var what = news.Joined ? $"ON THE BOARD AT P{news.Position}"
            : news.PlacesGained > 0 ? $"NEW PB {Lap(news.LapMs)} · UP {Plural(news.PlacesGained, "PLACE", "PLACES")} TO P{news.Position}"
            : $"NEW PB {Lap(news.LapMs)}";
        if (_room.Counting != Counting.Live)
            what += " (PRACTICE)";
        _news = $"<b>{what}</b>";
        ShowNews();
    }

    // ── Building ────────────────────────────────────────────────────────────

    protected override void BuildContent(RectTransform card)
    {
        var top = UiKit.Row(card, 8, name: "Top");
        UiKit.OneLine(UiKit.Caption(top, "Track board"));
        _state = UiKit.OneLine(UiKit.Label(top, "", 12, Theme.Ink600, UiKit.Heading, FontStyle.Bold));
        UiKit.Spacer(top);
        _position = UiKit.OneLine(UiKit.Label(top, "", 20, Theme.Ink200, UiKit.Heading, FontStyle.Bold));

        _title = UiKit.Label(card, "", 13, Theme.Ink400);

        var places = UiKit.Column(card, 2, name: "Places");
        for (var i = 0; i < MaxRows; i++)
            _rows.Add(new PlaceRow(places));

        _message = UiKit.Label(card, "", 15, Theme.Ink400);
        _target = UiKit.Label(card, "", 17, Theme.AccentSoft, UiKit.Heading, FontStyle.Bold);
        BuildRuler(card);
        _foot = UiKit.Label(card, "", 13, Theme.Ink600);
    }

    private void BuildRuler(RectTransform card)
    {
        _ruler = UiKit.Node("Ruler", card);
        UiKit.Size(_ruler, -1, 34);
        var line = UiKit.Panel(_ruler, Theme.Ink750, 0, "Line");
        line.raycastTarget = false;
        var rect = line.rectTransform;
        rect.anchorMin = new Vector2(0, RulerLine);
        rect.anchorMax = new Vector2(1, RulerLine);
        rect.sizeDelta = new Vector2(0, 3);
        rect.anchoredPosition = Vector2.zero;

        for (var i = 0; i < MaxRows; i++)
        {
            var tick = UiKit.Panel(_ruler, Theme.Ink400, 0, "Tick");
            tick.raycastTarget = false;
            _ticks.Add(tick);
            var label = UiKit.Label(_ruler, "", 11, Theme.Ink600, UiKit.Heading, FontStyle.Bold, TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _tickLabels.Add(label);
        }
        for (var i = 0; i < RulerDots; i++)
        {
            var dot = UiKit.Panel(_ruler, Theme.Ink400, 0, "Lap");
            dot.sprite = UiKit.Circle();
            dot.type = Image.Type.Simple;
            dot.raycastTarget = false;
            _dots.Add(dot);
        }
    }

    // ── The edit toolbar ────────────────────────────────────────────────────

    public override void BuildOptions(Transform section)
    {
        var places = UiKit.Row(section, 8, name: "Places");
        UiKit.OneLine(UiKit.Caption(places, "Places above"));
        _paintAbove = UiKit.Segmented(places, new[] { "1", "2", "3", "4", "5" }, _settings.BoardAbove.Value - 1,
            i => _settings.BoardAbove.Value = i + 1);
        UiKit.Size(UiKit.Node("Gap", places), 10, 1);
        UiKit.OneLine(UiKit.Caption(places, "Below"));
        _paintBelow = UiKit.Segmented(places, new[] { "0", "1", "2" }, _settings.BoardBelow.Value,
            i => _settings.BoardBelow.Value = i);

        var ruler = UiKit.Row(section, 8, name: "Ruler");
        UiKit.OneLine(UiKit.Caption(ruler, "Time ruler"));
        _paintRuler = UiKit.Segmented(ruler, new[] { "On", "Off" }, _settings.BoardRuler.Value ? 0 : 1,
            i => _settings.BoardRuler.Value = i == 0);
    }

    public override void RefreshOptions()
    {
        _paintAbove?.Invoke(_settings.BoardAbove.Value - 1);
        _paintBelow?.Invoke(_settings.BoardBelow.Value);
        _paintRuler?.Invoke(_settings.BoardRuler.Value ? 0 : 1);
    }

    public override void Reset()
    {
        base.Reset();
        _settings.BoardAbove.Value = (int)_settings.BoardAbove.DefaultValue;
        _settings.BoardBelow.Value = (int)_settings.BoardBelow.DefaultValue;
        _settings.BoardRuler.Value = (bool)_settings.BoardRuler.DefaultValue;
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    protected override float Render(float now)
    {
        if (_news != null && now >= NewsUntil)
            _news = null;

        (_state.text, _state.color) = _room.Counting switch
        {
            Counting.Live => ("LIVE", Theme.Signal),
            Counting.Unknown => ("", Theme.Ink600),
            _ => ("PRACTICE", Theme.Ink600),
        };

        var view = _board.View;
        var title = view != null && view.Title.Length > 0 ? view.Title : _board.TrackName;
        SetText(_title, Clip(title, 44));

        if (view == null)
        {
            _position.text = "";
            foreach (var row in _rows)
                row.Back.gameObject.SetActive(false);
            SetText(_target, "");
            SetText(_foot, "");
            _ruler.gameObject.SetActive(false);
            SetText(_message, _board.State switch
            {
                BoardState.NoBoard => "No JMT board for this course yet. Your first lap in a JMT room starts it.",
                BoardState.Unreachable => "Can't reach the JMT site right now.",
                BoardState.NoTrack => "Load a course to see its board.",
                _ => "Finding this course's board...",
            });
            return Again();
        }

        SetText(_message, "");
        _position.text = view.Me == null
            ? $"{Theme.Count(view.PilotCount)} PILOTS"
            : $"P{view.Me.Position}{(view.Provisional ? "*" : "")} / {Theme.Count(view.PilotCount)}";
        _position.color = view.Me == null ? Theme.Ink400 : Theme.Ink200;

        RenderRows(view);
        _target.color = _news != null ? Theme.Signal : Theme.AccentSoft;
        SetText(_target, _news ?? TargetLine(view));
        var ruler = _settings.BoardRuler.Value && view.Places.Count > 0;
        _ruler.gameObject.SetActive(ruler);
        if (ruler)
            RenderRuler(view);
        SetText(_foot, FootLine(view));
        return Again();

        float Again() => _news != null ? NewsUntil : float.PositiveInfinity;
    }

    private void RenderRows(BoardView view)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (i >= view.Places.Count)
            {
                row.Back.gameObject.SetActive(false);
                continue;
            }

            var place = view.Places[i];
            var next = place == view.Next;
            row.Back.gameObject.SetActive(true);
            row.Back.color = place.IsMe ? Theme.Alpha(Theme.Accent, 0.16f)
                : next ? Theme.Alpha(Theme.Ink700, 0.7f)
                : new Color(0, 0, 0, 0);
            row.Position.text = $"P{place.Position}";
            row.Position.color = place.IsMe ? Theme.Accent : place.Position <= 3 ? Theme.Place(place.Position) : Theme.Ink400;
            row.Name.text = place.IsMe ? (view.Provisional ? "You*" : "You") : Clip(place.Name, NameLength);
            row.Name.color = place.IsMe ? Theme.AccentSoft : Theme.Ink200;
            row.Name.fontStyle = place.IsMe || next ? FontStyle.Bold : FontStyle.Normal;
            row.Here.gameObject.SetActive(place.Here);
            row.LapTime.text = Lap(place.LapMs);
            if (view.Me == null || place.IsMe)
            {
                row.Gap.text = "";
            }
            else
            {
                row.Gap.text = Gap(place.LapMs - view.Me.LapMs);
                row.Gap.color = next ? Theme.Accent : Theme.Ink600;
            }
        }
    }

    /// <summary>The time that takes the next place. A tie doesn't: the site keeps the earlier of two equal laps ahead.</summary>
    private static string TargetLine(BoardView view)
    {
        if (view.Me == null)
        {
            return view.Next == null
                ? "Any lap puts you on the board"
                : $"Any lap puts you on the board · beat {Lap(view.Next.LapMs)} for P{view.Next.Position}";
        }
        if (view.Next == null && view.Me.Position > 1)
        {
            // A lap that jumped past every place the HUD had: the site's next answer has them.
            return $"Up to P{view.Me.Position} · the places above show on the next refresh";
        }
        if (view.Next == null)
        {
            return view.Chaser == null
                ? "You hold P1"
                : $"You hold P1 · {Theme.Seconds(view.Chaser.LapMs - view.Me.LapMs)} clear of P2";
        }
        return $"Beat {Lap(view.Next.LapMs)} for P{view.Next.Position} · {Theme.Seconds(view.Me.LapMs - view.Next.LapMs)} to find";
    }

    private string FootLine(BoardView view)
    {
        var parts = new List<string>();
        if (view.LastLapMs is { } last)
        {
            var lap = $"Last lap {Lap(last)}";
            if (view.Next != null)
                lap += $" ({Gap(last - view.Next.LapMs)} to P{view.Next.Position})";
            parts.Add(lap);
        }
        if (view.BestTonightMs is { } best && view.Recent.Count > 1)
            parts.Add($"best tonight {Lap(best)}");
        var line = string.Join(" · ", parts);
        if (view.Provisional)
        {
            var note = _room.Counting == Counting.Live
                ? "* on tonight's lap, until the site has it"
                : "* on a lap flown in practice, which the board won't get";
            line = line.Length > 0 ? $"{line}\n{note}" : note;
        }
        return line;
    }

    /// <summary>
    /// Lap times along a line, quickest on the left: a tick for each place shown, and
    /// tonight's laps as dots, older ones fainter. A lap quick enough for the next place
    /// shows in green, so a pilot can see when they're landing laps that would do it.
    /// </summary>
    private void RenderRuler(BoardView view)
    {
        var shown = view.Places;
        var quickest = shown.Min(p => p.LapMs);
        var slowest = shown.Max(p => p.LapMs);
        var span = Math.Max(slowest - quickest, 400);
        var from = quickest - span * 0.12;
        var to = slowest + span * 0.4;
        float X(int ms) => Mathf.Clamp01((float)((ms - from) / (to - from)));

        for (var i = 0; i < _ticks.Count; i++)
        {
            var show = i < shown.Count;
            _ticks[i].gameObject.SetActive(show);
            _tickLabels[i].gameObject.SetActive(show);
            if (!show)
                continue;
            var place = shown[i];
            var next = place == view.Next;
            var x = X(place.LapMs);
            _ticks[i].color = place.IsMe ? Theme.Accent : next ? Theme.AccentSoft : Theme.Ink400;
            Put(_ticks[i].rectTransform, x, RulerLine, place.IsMe ? new Vector2(3, 16) : new Vector2(2, 12));
            _tickLabels[i].text = place.IsMe ? "YOU" : $"P{place.Position}";
            _tickLabels[i].color = place.IsMe ? Theme.Accent : next ? Theme.AccentSoft : Theme.Ink600;
            Put(_tickLabels[i].rectTransform, x, 0.86f, new Vector2(34, 14));
        }

        var recent = view.Recent;
        var first = Math.Max(0, recent.Count - _dots.Count);
        for (var i = 0; i < _dots.Count; i++)
        {
            var index = first + i;
            var show = index < recent.Count;
            _dots[i].gameObject.SetActive(show);
            if (!show)
                continue;
            var ms = recent[index];
            var latest = index == recent.Count - 1;
            var takes = view.Next != null && ms < view.Next.LapMs;
            var age = (index - first + 1) / (float)(recent.Count - first);
            var color = takes ? Theme.Signal : latest ? Theme.Ink200 : Theme.Ink400;
            _dots[i].color = Theme.Alpha(color, latest ? 1f : 0.25f + 0.5f * age);
            Put(_dots[i].rectTransform, X(ms), RulerLine, latest ? new Vector2(10, 10) : new Vector2(6, 6));
        }
    }

    private static void Put(RectTransform rect, float x, float y, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(x, y);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
    }

    /// <summary>One place on the board: position, name, "Here", lap time and the gap to the pilot's.</summary>
    private sealed class PlaceRow
    {
        public PlaceRow(Transform parent)
        {
            Back = UiKit.Panel(parent, new Color(0, 0, 0, 0), 6, "Place");
            Back.raycastTarget = false;
            UiKit.Line(Back.gameObject, 8, new RectOffset(8, 8, 2, 2));
            UiKit.Size(Back, -1, 26);
            Position = UiKit.Label(Back.transform, "", 15, Theme.Ink400, UiKit.Heading, FontStyle.Bold);
            UiKit.Size(Position, 40, -1);
            Name = UiKit.Label(Back.transform, "", 15, Theme.Ink200);
            UiKit.Size(Name, 0, -1, 1);
            Here = UiKit.Badge(Back.transform, "Here", Theme.Alpha(Theme.Signal, 0.15f), Theme.Signal);
            LapTime = UiKit.Label(Back.transform, "", 15, Theme.Ink200, UiKit.Mono, FontStyle.Normal, TextAnchor.MiddleRight);
            UiKit.Size(LapTime, 74, -1);
            Gap = UiKit.Label(Back.transform, "", 13, Theme.Ink600, UiKit.Mono, FontStyle.Normal, TextAnchor.MiddleRight);
            UiKit.Size(Gap, 60, -1);
        }

        public Image Back { get; }
        public Text Position { get; }
        public Text Name { get; }
        public RectTransform Here { get; }
        public Text LapTime { get; }
        public Text Gap { get; }
    }
}
