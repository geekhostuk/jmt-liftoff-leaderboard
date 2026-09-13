using System;
using System.Collections.Generic;
using System.Globalization;
using JmtLiftoffLeaderboard.Game;
using UnityEngine;
using UnityEngine.UI;
using static JmtLiftoffLeaderboard.Ui.HudText;

namespace JmtLiftoffLeaderboard.Ui;

/// <summary>
/// How far ahead or behind the lap being flown is against the pilot's best on this course:
/// the running delta, a bar that fills left of centre when ahead and right when behind,
/// the lap's sectors coloured purple for a best ever, green for quicker and yellow for
/// slower, and a line with the lap so far, the lap to beat and the best possible lap.
///
/// A few sectors are boxes that each say their delta. A sector per gate is a strip that
/// grows with the course, one segment per stretch between gates, each as wide as that
/// stretch is long on the lap being chased, with the last stretch flown spelled out under it.
/// </summary>
internal sealed class DeltaPanel : HudPanel
{
    /// <summary>Up to this many sectors, each box says its delta; more, and they're a strip.</summary>
    private const int LabelledSectors = 6;

    private readonly Settings _settings;
    private readonly DeltaTracker _delta;

    private Text _state = null!;
    private Text _trend = null!;
    private Text _value = null!;
    private RectTransform _bar = null!;
    private RectTransform _fill = null!;
    private Image _fillImage = null!;
    private RectTransform _sectorRow = null!;
    private HorizontalLayoutGroup _sectorLayout = null!;
    private readonly List<SectorBox> _sectors = new();
    private Text _stretch = null!;
    private Text _line = null!;
    private Text _message = null!;

    private string? _news;
    private Action<int>? _paintCompare;
    private Action<int>? _paintBar;
    private Action<int>? _paintLine;
    private Action<int>? _paintSectors;
    private Action<int>? _paintRange;

    public DeltaPanel(Settings settings, DeltaTracker delta) : base("Delta", settings.Delta)
    {
        _settings = settings;
        _delta = delta;
        delta.Changed += MarkDirty;
        delta.News += OnNews;
        Watch(settings.DeltaCompare);
        Watch(settings.DeltaBar);
        Watch(settings.DeltaSectors);
        Watch(settings.DeltaLapLine);
        Watch(settings.DeltaRange);
    }

    protected override float Width => 400;

    protected override bool HasContent => _delta.HasCourse;

    private bool Tonight => _settings.DeltaCompare.Value == DeltaCompare.Tonight;

    private void OnNews(LapOutcome outcome)
    {
        if (outcome == LapOutcome.CourseLearned)
            _news = "SPLITS SET FOR THIS COURSE";
        else if (outcome == LapOutcome.NewBest || (Tonight && outcome == LapOutcome.BestTonight))
            _news = null; // the finished lap says so itself
        else
            return;
        ShowNews();
    }

    // ── Building ────────────────────────────────────────────────────────────

    protected override void BuildContent(RectTransform card)
    {
        var top = UiKit.Row(card, 8, name: "Top");
        UiKit.OneLine(UiKit.Caption(top, "Delta"));
        _state = UiKit.OneLine(UiKit.Label(top, "", 12, Theme.Ink600, UiKit.Heading, FontStyle.Bold));
        UiKit.Spacer(top);
        _trend = UiKit.OneLine(UiKit.Label(top, "", 16, Theme.Ink600, UiKit.Heading, FontStyle.Bold));

        _value = UiKit.Label(card, "", 40, Theme.Ink200, UiKit.Mono, FontStyle.Bold, TextAnchor.MiddleCenter);
        UiKit.Size(_value, -1, 46);

        _bar = UiKit.Node("Bar", card);
        UiKit.Size(_bar, -1, 10);
        var track = UiKit.Panel(_bar, Theme.Ink750, 4, "Track");
        track.raycastTarget = false;
        UiKit.Fill(track.rectTransform);
        _fillImage = UiKit.Panel(_bar, Theme.Signal, 0, "Fill");
        _fillImage.raycastTarget = false;
        _fill = _fillImage.rectTransform;
        _fill.offsetMin = _fill.offsetMax = Vector2.zero;
        var centre = UiKit.Panel(_bar, Theme.Ink200, 0, "Centre");
        centre.raycastTarget = false;
        var mark = centre.rectTransform;
        mark.anchorMin = new Vector2(0.5f, 0);
        mark.anchorMax = new Vector2(0.5f, 1);
        mark.offsetMin = new Vector2(-1, -3);
        mark.offsetMax = new Vector2(1, 3);

        _sectorRow = UiKit.Row(card, 4, name: "Sectors");
        _sectorLayout = _sectorRow.GetComponent<HorizontalLayoutGroup>();
        _stretch = UiKit.Label(card, "", 13, Theme.Ink400);
        _line = UiKit.Label(card, "", 14, Theme.Ink400);
        _message = UiKit.Label(card, "", 14, Theme.Ink400);
    }

    // ── The edit toolbar ────────────────────────────────────────────────────

    public override void BuildOptions(Transform section)
    {
        var compare = UiKit.Row(section, 8, name: "Compare");
        UiKit.OneLine(UiKit.Caption(compare, "Measured against"));
        _paintCompare = UiKit.Segmented(compare, new[] { "Best ever", "Tonight" }, CompareIndex(),
            i => _settings.DeltaCompare.Value = i == 0 ? DeltaCompare.BestEver : DeltaCompare.Tonight);

        var parts = UiKit.Row(section, 8, name: "Parts");
        UiKit.OneLine(UiKit.Caption(parts, "Bar"));
        _paintBar = UiKit.Segmented(parts, new[] { "On", "Off" }, _settings.DeltaBar.Value ? 0 : 1,
            i => _settings.DeltaBar.Value = i == 0);
        UiKit.Size(UiKit.Node("Gap", parts), 10, 1);
        UiKit.OneLine(UiKit.Caption(parts, "Lap line"));
        _paintLine = UiKit.Segmented(parts, new[] { "On", "Off" }, _settings.DeltaLapLine.Value ? 0 : 1,
            i => _settings.DeltaLapLine.Value = i == 0);

        var sectors = UiKit.Row(section, 8, name: "Sectors");
        UiKit.OneLine(UiKit.Caption(sectors, "Sectors"));
        _paintSectors = UiKit.Segmented(sectors, JmtLiftoffLeaderboard.Settings.DeltaSectorChoices, SectorsIndex(),
            i => _settings.DeltaSectors.Value = JmtLiftoffLeaderboard.Settings.DeltaSectorChoices[i]);

        var range = UiKit.Row(section, 8, name: "Range");
        UiKit.OneLine(UiKit.Caption(range, "Bar range"));
        _paintRange = UiKit.Segmented(range, new[] { "±0.5s", "±1s", "±2s" }, RangeIndex(),
            i => _settings.DeltaRange.Value = JmtLiftoffLeaderboard.Settings.DeltaRanges[i]);

        var forget = UiKit.Row(section, 8, name: "Forget");
        UiKit.OneLine(UiKit.Label(forget, "Your splits are kept for each course on this computer.", 13, Theme.Ink600));
        UiKit.Spacer(forget);
        UiKit.Button(forget, "Forget this course's best", _delta.Forget, UiKit.ButtonKind.Outline, 14, 30);
    }

    public override void RefreshOptions()
    {
        _paintCompare?.Invoke(CompareIndex());
        _paintBar?.Invoke(_settings.DeltaBar.Value ? 0 : 1);
        _paintLine?.Invoke(_settings.DeltaLapLine.Value ? 0 : 1);
        _paintSectors?.Invoke(SectorsIndex());
        _paintRange?.Invoke(RangeIndex());
    }

    public override void Reset()
    {
        base.Reset();
        _settings.DeltaCompare.Value = (DeltaCompare)_settings.DeltaCompare.DefaultValue;
        _settings.DeltaBar.Value = (bool)_settings.DeltaBar.DefaultValue;
        _settings.DeltaSectors.Value = (string)_settings.DeltaSectors.DefaultValue;
        _settings.DeltaLapLine.Value = (bool)_settings.DeltaLapLine.DefaultValue;
        _settings.DeltaRange.Value = (float)_settings.DeltaRange.DefaultValue;
    }

    private int CompareIndex() => Tonight ? 1 : 0;

    private int SectorsIndex() => Math.Max(0, Array.IndexOf(JmtLiftoffLeaderboard.Settings.DeltaSectorChoices, _settings.DeltaSectors.Value));

    private int RangeIndex()
    {
        var index = Array.FindIndex(JmtLiftoffLeaderboard.Settings.DeltaRanges, range => Mathf.Approximately(range, _settings.DeltaRange.Value));
        return index < 0 ? 1 : index;
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    protected override float Render(float now)
    {
        if (_news != null && now >= NewsUntil)
            _news = null;

        var view = _delta.View(now);
        if (Editing && (view == null || (!view.Running && !view.Finished)))
            view = Sample();
        if (view == null)
        {
            _state.text = _trend.text = _value.text = "";
            _bar.gameObject.SetActive(false);
            _sectorRow.gameObject.SetActive(false);
            SetText(_stretch, "");
            SetText(_line, "");
            SetText(_message, "Load a course to measure your laps.");
            return float.PositiveInfinity;
        }

        RenderState(view);
        RenderValue(view);
        RenderBar(view);
        RenderSectors(view);
        SetText(_line, _settings.DeltaLapLine.Value ? LapLine(view) : "");
        SetText(_message, view.HasReference || view.Finished
            ? ""
            : Tonight
                ? "Fly a clean lap through every gate to set tonight's lap to beat."
                : "Fly a clean lap through every gate to set your lap to beat.");

        // A running lap's clock and bar move on their own; a finished lap gives way to the next.
        if (view.Running || view.Finished)
            return now + 0.05f;
        return _news != null ? NewsUntil : float.PositiveInfinity;
    }

    private void RenderState(DeltaView view)
    {
        if (view.Finished && view.NewBest)
        {
            (_state.text, _state.color) = (Tonight ? "BEST TONIGHT" : "NEW BEST", Theme.Purple);
        }
        else if (view.Finished)
        {
            (_state.text, _state.color) = ($"LAP {Lap(view.FinishedMs.GetValueOrDefault())}", Theme.Ink400);
        }
        else if (_news != null)
        {
            (_state.text, _state.color) = (_news, Theme.Signal);
        }
        else
        {
            (_state.text, _state.color) = (!view.HasReference ? "" : Tonight ? "VS TONIGHT" : "VS YOUR BEST", Theme.Ink600);
        }

        (_trend.text, _trend.color) = view.Running && view.DeltaMs != null
            ? view.Trend switch
            {
                < 0 => ("▼ GAINING", Theme.Signal),
                > 0 => ("▲ LOSING", Theme.Alarm),
                _ => ("", Theme.Ink600),
            }
            : ("", Theme.Ink600);
    }

    private void RenderValue(DeltaView view)
    {
        if (view.DeltaMs is { } delta)
        {
            _value.text = Gap(delta);
            _value.color = delta < 0 ? Theme.Signal : delta > 0 ? Theme.Alarm : Theme.Ink200;
        }
        else if (view.Finished && view.FinishedMs is { } lap)
        {
            // A first lap has nothing to be measured against: its time is the news.
            _value.text = Lap(lap);
            _value.color = view.NewBest ? Theme.Purple : Theme.Ink200;
        }
        else
        {
            _value.text = "—";
            _value.color = Theme.Ink600;
        }
    }

    private void RenderBar(DeltaView view)
    {
        var show = _settings.DeltaBar.Value && view.HasReference;
        _bar.gameObject.SetActive(show);
        if (!show)
            return;
        var delta = view.DeltaMs ?? 0;
        var range = Mathf.Max(0.1f, _settings.DeltaRange.Value) * 1000f;
        var half = Mathf.Clamp01(Math.Abs(delta) / range) * 0.5f;
        _fill.anchorMin = new Vector2(delta < 0 ? 0.5f - half : 0.5f, 0);
        _fill.anchorMax = new Vector2(delta < 0 ? 0.5f : 0.5f + half, 1);
        _fillImage.color = delta < 0 ? Theme.Signal : Theme.Alarm;
    }

    private void RenderSectors(DeltaView view)
    {
        var count = view.Sectors.Count;
        var show = _settings.DeltaSectorCount > 0 && count > 0;
        _sectorRow.gameObject.SetActive(show);
        if (!show)
        {
            SetText(_stretch, "");
            return;
        }

        while (_sectors.Count < count)
            _sectors.Add(new SectorBox(_sectorRow));
        var labelled = count <= LabelledSectors;
        _sectorLayout.spacing = labelled ? 4 : 1;
        var next = view.Sectors.FindIndex(s => s.Mark == SectorMark.Pending);
        var last = next < 0 ? count - 1 : next - 1;

        for (var i = 0; i < _sectors.Count; i++)
        {
            var box = _sectors[i];
            if (i >= count)
            {
                box.Hide();
                continue;
            }
            var sector = view.Sectors[i];
            var ahead = view.Running && i == next;
            box.Show(sector, labelled, ahead, labelled && sector.DeltaMs is { } ms ? $"S{i + 1} {Short(ms)}" : $"S{i + 1}");
        }

        // A strip's segments are too narrow to say anything: the last one flown is spelled out.
        if (labelled || last < 0)
        {
            SetText(_stretch, "");
            return;
        }
        var flown = view.Sectors[last];
        var where = last < count - 1 ? $"Gate {last + 1} of {count - 1}" : "To the line";
        _stretch.color = Tone(flown.Mark);
        SetText(_stretch, flown.DeltaMs is { } delta ? $"{where} · {Short(delta)}" : where);
    }

    private string LapLine(DeltaView view)
    {
        var parts = new List<string>();
        if (view.Running && view.ElapsedMs is { } elapsed)
            parts.Add($"Lap {Clock(elapsed)}");
        if (view.ReferenceMs is { } reference)
            parts.Add($"{(Tonight ? "Tonight" : "Best")} {Lap(reference)}");
        if (view.OptimalMs is { } optimal)
            parts.Add($"Possible {Lap(optimal)}");
        return string.Join(" · ", parts);
    }

    private static Color Tone(SectorMark mark) => mark switch
    {
        SectorMark.Best => Theme.Purple,
        SectorMark.Faster => Theme.Signal,
        SectorMark.Slower => Theme.Slower,
        _ => Theme.Ink600,
    };

    /// <summary>A lap clock to a tenth: it's read at a glance, mid-flight.</summary>
    private static string Clock(int ms) =>
        ms < 60_000 ? Theme.Seconds(Math.Max(0, ms), "0.0") : $"{ms / 60_000}:{Theme.Seconds(ms % 60_000, "00.0")}";

    /// <summary>"-0.12": a sector's delta, to a hundredth, to fit its box.</summary>
    private static string Short(int ms) =>
        (ms >= 0 ? "+" : "-") + (Math.Abs(ms) / 1000.0).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>What edit mode shows with no lap under way, so the panel can be placed and sized as it will look.</summary>
    private DeltaView Sample()
    {
        var count = _settings.DeltaSectorCount;
        var sectors = new List<SectorView>();
        if (count == DeltaRun.EveryGate)
        {
            int[] refs = { 2100, 2600, 1900, 3300, 2800, 2400, 3100, 2200, 2700, 1800, 2500, 3000, 2300, 2600, 2900, 3400 };
            int[] deltas = { -60, -30, -10, 40, -20, -50, 30, -15, -5 };
            SectorMark[] marks = { SectorMark.Best, SectorMark.Faster, SectorMark.Faster, SectorMark.Slower, SectorMark.Faster,
                SectorMark.Best, SectorMark.Slower, SectorMark.Faster, SectorMark.Faster };
            for (var i = 0; i < refs.Length; i++)
            {
                sectors.Add(i < deltas.Length
                    ? new SectorView(refs[i] + deltas[i], deltas[i], marks[i], refs[i])
                    : new SectorView(null, null, SectorMark.Pending, refs[i]));
            }
        }
        else if (count > 0)
        {
            var sample = new List<SectorView>
            {
                new(8210, -120, SectorMark.Best, 8330),
                new(10004, -80, SectorMark.Faster, 10084),
                new(9771, 30, SectorMark.Slower, 9741),
                new(null, null, SectorMark.Pending, 6700),
                new(null, null, SectorMark.Pending, 6735),
                new(null, null, SectorMark.Pending, 6000),
            };
            sectors = sample.GetRange(0, Math.Min(count, sample.Count));
        }
        return new DeltaView
        {
            HasReference = true,
            ReferenceMs = 41_590,
            OptimalMs = 41_204,
            Running = true,
            ElapsedMs = 23_410,
            DeltaMs = -234,
            Trend = -1,
            Sectors = sectors,
        };
    }

    /// <summary>One sector: a box that says its delta when there are few, a strip segment as wide as its stretch when there are many.</summary>
    private sealed class SectorBox
    {
        private readonly Image _box;
        private readonly Text _label;
        private readonly LayoutElement _size;
        private bool? _labelled;

        public SectorBox(Transform row)
        {
            _box = UiKit.Panel(row, Theme.Ink800, 6, "Sector");
            _box.raycastTarget = false;
            // Every box starts from nothing and takes its share of the row.
            _size = UiKit.Size(_box, 0, 28, 1);
            _label = UiKit.Label(_box.transform, "", 13, Theme.Ink600, UiKit.Mono, FontStyle.Bold, TextAnchor.MiddleCenter);
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Fill(_label.rectTransform);
        }

        public void Hide() => _box.gameObject.SetActive(false);

        public void Show(SectorView sector, bool labelled, bool ahead, string text)
        {
            _box.gameObject.SetActive(true);
            if (_labelled != labelled)
            {
                _labelled = labelled;
                _box.sprite = UiKit.Rounded(labelled ? 6 : 2);
                _size.minHeight = _size.preferredHeight = labelled ? 28 : 12;
                _label.gameObject.SetActive(labelled);
            }
            // Boxes share the row equally; a strip's segments by how long each stretch is.
            _size.flexibleWidth = labelled ? 1 : Math.Max(1, sector.RefMs) / 1000f;

            var tone = Tone(sector.Mark);
            _box.color = sector.Mark == SectorMark.Pending
                ? ahead ? Theme.Ink700 : Theme.Ink800
                : Theme.Alpha(tone, labelled ? 0.2f : 0.9f);
            if (!labelled)
                return;
            _label.color = tone;
            _label.text = text;
        }
    }
}
