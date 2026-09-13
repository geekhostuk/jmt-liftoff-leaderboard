using System;
using System.Collections.Generic;
using JmtLiftoffLeaderboard.Game;
using UnityEngine;
using UnityEngine.UI;
using static JmtLiftoffLeaderboard.Ui.HudText;

namespace JmtLiftoffLeaderboard.Ui;

/// <summary>
/// The race in the room: everyone's best lap this race, ranked the way the room's timing
/// screen ranks them, with the gap to the quickest or each pilot's last lap. In a JMT room
/// it carries the site's side too: laps from before the pilot joined, and failed attempts.
/// </summary>
internal sealed class RacePanel : HudPanel
{
    private const int MaxRows = 12;
    private const int NameLength = 16;

    private readonly Settings _settings;
    private readonly RaceTracker _race;
    private readonly BoardTracker _board;
    private readonly RoomWatch _room;

    private Text _state = null!;
    private Text _position = null!;
    private Text _title = null!;
    private RectTransform _head = null!;
    private Text _headColumn = null!;
    private Text _headFailed = null!;
    private readonly List<RaceRowView> _rows = new();
    private Text _message = null!;
    private Text _newsLine = null!;

    private string? _news;
    private Color _newsColor;
    private Action<int>? _paintRows;
    private Action<int>? _paintColumn;
    private Action<int>? _paintFailed;

    public RacePanel(Settings settings, RaceTracker race, BoardTracker board, RoomWatch room) : base("Race", settings.Race)
    {
        _settings = settings;
        _race = race;
        _board = board;
        _room = room;
        race.Changed += MarkDirty;
        race.Moved += OnMoved;
        board.Changed += MarkDirty;
        room.Changed += MarkDirty;
        Watch(settings.RaceRows);
        Watch(settings.RaceColumn);
        Watch(settings.RaceFailed);
    }

    protected override float Width => 420;

    protected override bool HasContent => _race.InRoom;

    private void OnMoved(int now, int before)
    {
        (_news, _newsColor) = now < before
            ? ($"<b>UP TO P{now}</b>", Theme.Signal)
            : ($"<b>DOWN TO P{now}</b>", Theme.Alarm);
        ShowNews();
    }

    // ── Building ────────────────────────────────────────────────────────────

    protected override void BuildContent(RectTransform card)
    {
        var top = UiKit.Row(card, 8, name: "Top");
        UiKit.OneLine(UiKit.Caption(top, "Race"));
        _state = UiKit.OneLine(UiKit.Label(top, "", 12, Theme.Ink600, UiKit.Heading, FontStyle.Bold));
        UiKit.Spacer(top);
        _position = UiKit.OneLine(UiKit.Label(top, "", 20, Theme.Ink200, UiKit.Heading, FontStyle.Bold));

        _title = UiKit.Label(card, "", 13, Theme.Ink400);

        var head = UiKit.Panel(card, new Color(0, 0, 0, 0), 0, "Head");
        head.raycastTarget = false;
        _head = head.rectTransform;
        UiKit.Line(head.gameObject, 8, new RectOffset(8, 8, 0, 0));
        UiKit.Size(head, -1, 16);
        Cell(_head, "", 40, TextAnchor.MiddleLeft);
        var name = UiKit.Caption(_head, "", size: 11);
        UiKit.Size(name, 0, -1, 1);
        Cell(_head, "Laps", RaceRowView.LapsWidth, TextAnchor.MiddleRight);
        Cell(_head, "Best", RaceRowView.BestWidth, TextAnchor.MiddleRight);
        _headColumn = Cell(_head, "Gap", RaceRowView.ColumnWidth, TextAnchor.MiddleRight);
        _headFailed = Cell(_head, "Failed", RaceRowView.FailedWidth, TextAnchor.MiddleRight);

        var rows = UiKit.Column(card, 2, name: "Pilots");
        for (var i = 0; i < MaxRows; i++)
            _rows.Add(new RaceRowView(rows));

        _message = UiKit.Label(card, "", 15, Theme.Ink400);
        _newsLine = UiKit.Label(card, "", 17, Theme.Signal, UiKit.Heading, FontStyle.Bold);

        static Text Cell(Transform parent, string text, float width, TextAnchor align)
        {
            var cell = UiKit.Caption(parent, text, size: 11);
            cell.alignment = align;
            UiKit.Size(cell, width, -1);
            return cell;
        }
    }

    // ── The edit toolbar ────────────────────────────────────────────────────

    public override void BuildOptions(Transform section)
    {
        var rows = UiKit.Row(section, 8, name: "Rows");
        UiKit.OneLine(UiKit.Caption(rows, "Pilots shown"));
        _paintRows = UiKit.Segmented(rows, new[] { "5", "8", "12" }, RowsIndex(),
            i => _settings.RaceRows.Value = JmtLiftoffLeaderboard.Settings.RaceRowCounts[i]);

        var columns = UiKit.Row(section, 8, name: "Columns");
        UiKit.OneLine(UiKit.Caption(columns, "Last column"));
        _paintColumn = UiKit.Segmented(columns, new[] { "Gap", "Last lap" }, ColumnIndex(),
            i => _settings.RaceColumn.Value = i == 0 ? RaceColumn.Gap : RaceColumn.Last);
        UiKit.Size(UiKit.Node("Gap", columns), 10, 1);
        UiKit.OneLine(UiKit.Caption(columns, "Failed"));
        _paintFailed = UiKit.Segmented(columns, new[] { "On", "Off" }, _settings.RaceFailed.Value ? 0 : 1,
            i => _settings.RaceFailed.Value = i == 0);
    }

    public override void RefreshOptions()
    {
        _paintRows?.Invoke(RowsIndex());
        _paintColumn?.Invoke(ColumnIndex());
        _paintFailed?.Invoke(_settings.RaceFailed.Value ? 0 : 1);
    }

    public override void Reset()
    {
        base.Reset();
        _settings.RaceRows.Value = (int)_settings.RaceRows.DefaultValue;
        _settings.RaceColumn.Value = (RaceColumn)_settings.RaceColumn.DefaultValue;
        _settings.RaceFailed.Value = (bool)_settings.RaceFailed.DefaultValue;
    }

    private int RowsIndex()
    {
        var index = Array.IndexOf(JmtLiftoffLeaderboard.Settings.RaceRowCounts, _settings.RaceRows.Value);
        return index < 0 ? 1 : index;
    }

    private int ColumnIndex() => _settings.RaceColumn.Value == RaceColumn.Gap ? 0 : 1;

    // ── Rendering ───────────────────────────────────────────────────────────

    protected override float Render(float now)
    {
        if (_news != null && now >= NewsUntil)
            _news = null;

        var sample = Editing && (!_race.InRoom || _race.View.Rows.Count == 0);
        var view = sample ? Sample() : _race.View;
        var live = sample || _room.Counting == Counting.Live;
        (_state.text, _state.color) = live ? ("LIVE", Theme.Signal)
            : _room.Counting == Counting.Unknown ? ("", Theme.Ink600)
            : ("ROOM", Theme.Ink600);
        SetText(_title, Clip(sample ? "Hollow Oak" : _board.TrackName, 44));

        var rows = RaceMath.Visible(view.Rows, Math.Min(MaxRows, _settings.RaceRows.Value));
        _position.text = view.MyPosition is { } mine
            ? $"P{mine} / {Theme.Count(view.Timed)}"
            : rows.Count > 0 ? Plural(view.Rows.Count, "PILOT", "PILOTS") : "";
        _position.color = view.MyPosition != null ? Theme.Ink200 : Theme.Ink400;

        var gap = _settings.RaceColumn.Value == RaceColumn.Gap;
        var failed = _settings.RaceFailed.Value && view.FromSite;
        _head.gameObject.SetActive(rows.Count > 0);
        _headColumn.text = gap ? "GAP" : "LAST";
        _headFailed.gameObject.SetActive(failed);
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].Show(i < rows.Count ? rows[i] : null, gap, failed);

        SetText(_message, !_race.InRoom && !sample ? "Join a room to see its race."
            : rows.Count == 0 ? "Nobody has flown a lap in this race yet."
            : "");
        _newsLine.color = _newsColor;
        SetText(_newsLine, _news ?? "");
        return _news != null ? NewsUntil : float.PositiveInfinity;
    }

    /// <summary>What edit mode shows outside a room, so the panel can be placed and sized as it will look.</summary>
    private static RaceView Sample()
    {
        var rows = new List<RaceRow>
        {
            new() { Name = "Hawk", InRoom = true, Laps = 14, BestMs = 41_020, LastMs = 41_300, Failed = 1 },
            new() { Name = "You", IsMe = true, InRoom = true, Laps = 11, BestMs = 41_402, LastMs = 41_655, Failed = 3 },
            new() { Name = "Vortex", InRoom = true, Laps = 9, BestMs = 41_910, LastMs = 42_210, Failed = 0 },
            new() { Name = "Rotor", InRoom = false, Laps = 6, BestMs = 42_311, LastMs = 42_311, Failed = 2 },
            new() { Name = "Newcomer", InRoom = true },
        };
        for (var i = 0; i < 4; i++)
        {
            rows[i].Position = i + 1;
            rows[i].GapMs = i == 0 ? null : rows[i].BestMs - rows[0].BestMs;
        }
        rows[0].Fastest = true;
        return new RaceView { Rows = rows, FromSite = true, MyPosition = 2, Timed = 4 };
    }

    /// <summary>One pilot: place, name, laps, best lap, and the gap or last lap, and failed attempts in a JMT room.</summary>
    private sealed class RaceRowView
    {
        public const float LapsWidth = 36;
        public const float BestWidth = 74;
        public const float ColumnWidth = 64;
        public const float FailedWidth = 44;

        private readonly Image _back;
        private readonly Text _position;
        private readonly Text _name;
        private readonly Text _laps;
        private readonly Text _best;
        private readonly Text _column;
        private readonly Text _failed;

        public RaceRowView(Transform parent)
        {
            _back = UiKit.Panel(parent, new Color(0, 0, 0, 0), 6, "Pilot");
            _back.raycastTarget = false;
            UiKit.Line(_back.gameObject, 8, new RectOffset(8, 8, 2, 2));
            UiKit.Size(_back, -1, 26);
            _position = UiKit.Label(_back.transform, "", 15, Theme.Ink400, UiKit.Heading, FontStyle.Bold);
            UiKit.Size(_position, 40, -1);
            _name = UiKit.Label(_back.transform, "", 15, Theme.Ink200);
            UiKit.Size(_name, 0, -1, 1);
            _laps = Number(LapsWidth, 13);
            _best = Number(BestWidth, 15);
            _column = Number(ColumnWidth, 13);
            _failed = Number(FailedWidth, 13);

            Text Number(float width, int size)
            {
                var label = UiKit.Label(_back.transform, "", size, Theme.Ink600, UiKit.Mono, FontStyle.Normal, TextAnchor.MiddleRight);
                UiKit.Size(label, width, -1);
                return label;
            }
        }

        public void Show(RaceRow? row, bool gap, bool failed)
        {
            _back.gameObject.SetActive(row != null);
            if (row == null)
                return;

            _back.color = row.IsMe ? Theme.Alpha(Theme.Accent, 0.16f) : new Color(0, 0, 0, 0);
            _position.text = row.Position is { } place ? $"P{place}" : "–";
            _position.color = row.IsMe ? Theme.Accent : row.Position is { } p && p <= 3 ? Theme.Place(p) : Theme.Ink400;
            _name.text = row.IsMe ? "You" : Clip(row.Name.Length > 0 ? row.Name : "Unnamed pilot", NameLength);
            _name.color = row.IsMe ? Theme.AccentSoft : row.InRoom ? Theme.Ink200 : Theme.Ink600;
            _name.fontStyle = row.IsMe ? FontStyle.Bold : FontStyle.Normal;
            _laps.text = row.Laps > 0 ? Theme.Count(row.Laps) : "";
            _best.text = row.BestMs is { } best ? Lap(best) : "—";
            _best.color = row.Fastest ? Theme.Purple : row.IsMe ? Theme.AccentSoft : row.InRoom ? Theme.Ink200 : Theme.Ink600;
            _column.text = gap
                ? row.GapMs is { } off ? Theme.Gap(off) : ""
                : row.LastMs is { } last ? Lap(last) : "";
            _column.color = row.IsMe ? Theme.AccentSoft : Theme.Ink600;
            _failed.gameObject.SetActive(failed);
            _failed.text = row.Failed is { } count ? Theme.Count(count) : "";
            _failed.color = row.Failed > 0 ? Theme.Alarm : Theme.Ink600;
        }
    }
}
