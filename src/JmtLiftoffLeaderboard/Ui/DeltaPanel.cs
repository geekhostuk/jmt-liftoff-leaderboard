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
/// </summary>
internal sealed class DeltaPanel : HudPanel
{
    private const int MaxSectors = 6;

    private readonly Settings _settings;
    private readonly DeltaTracker _delta;

    private Text _state = null!;
    private Text _trend = null!;
    private Text _value = null!;
    private RectTransform _bar = null!;
    private RectTransform _fill = null!;
    private Image _fillImage = null!;
    private RectTransform _sectorRow = null!;
    private readonly List<(Image Box, Text Label)> _sectors = new();
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
        for (var i = 0; i < MaxSectors; i++)
        {
            var box = UiKit.Panel(_sectorRow, Theme.Ink800, 6, "Sector");
            box.raycastTarget = false;
            // Every box starts from nothing and takes an equal share of the row.
            UiKit.Size(box, 0, 28, 1);
            var label = UiKit.Label(box.transform, "", 13, Theme.Ink600, UiKit.Mono, FontStyle.Bold, TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Fill(label.rectTransform);
            _sectors.Add((box, label));
        }

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
        _paintSectors = UiKit.Segmented(sectors, new[] { "Off", "3", "4", "5", "6" }, SectorsIndex(),
            i => _settings.DeltaSectors.Value = JmtLiftoffLeaderboard.Settings.DeltaSectorCounts[i]);
        UiKit.Size(UiKit.Node("Gap", sectors), 10, 1);
        UiKit.OneLine(UiKit.Caption(sectors, "Bar range"));
        _paintRange = UiKit.Segmented(sectors, new[] { "±0.5s", "±1s", "±2s" }, RangeIndex(),
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
        _settings.DeltaSectors.Value = (int)_settings.DeltaSectors.DefaultValue;
        _settings.DeltaLapLine.Value = (bool)_settings.DeltaLapLine.DefaultValue;
        _settings.DeltaRange.Value = (float)_settings.DeltaRange.DefaultValue;
    }

    private int CompareIndex() => Tonight ? 1 : 0;

    private int SectorsIndex() => Math.Max(0, Array.IndexOf(JmtLiftoffLeaderboard.Settings.DeltaSectorCounts, _settings.DeltaSectors.Value));

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
        var count = Math.Min(view.Sectors.Count, MaxSectors);
        var show = _settings.DeltaSectors.Value > 0 && count > 0;
        _sectorRow.gameObject.SetActive(show);
        if (!show)
            return;
        for (var i = 0; i < _sectors.Count; i++)
        {
            var (box, label) = _sectors[i];
            box.gameObject.SetActive(i < count);
            if (i >= count)
                continue;
            var sector = view.Sectors[i];
            var tone = sector.Mark switch
            {
                SectorMark.Best => Theme.Purple,
                SectorMark.Faster => Theme.Signal,
                SectorMark.Slower => Theme.Slower,
                _ => Theme.Ink600,
            };
            box.color = sector.Mark == SectorMark.Pending ? Theme.Ink800 : Theme.Alpha(tone, 0.2f);
            label.color = tone;
            label.text = sector.DeltaMs is { } ms ? $"S{i + 1} {Short(ms)}" : $"S{i + 1}";
        }
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

    /// <summary>A lap clock to a tenth: it's read at a glance, mid-flight.</summary>
    private static string Clock(int ms) =>
        ms < 60_000 ? Theme.Seconds(Math.Max(0, ms), "0.0") : $"{ms / 60_000}:{Theme.Seconds(ms % 60_000, "00.0")}";

    /// <summary>"-0.12": a sector's delta, to a hundredth, to fit its box.</summary>
    private static string Short(int ms) =>
        (ms >= 0 ? "+" : "-") + (Math.Abs(ms) / 1000.0).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>What edit mode shows with no lap under way, so the panel can be placed and sized as it will look.</summary>
    private DeltaView Sample()
    {
        var sectors = new List<SectorView>
        {
            new(8210, -120, SectorMark.Best),
            new(10004, -80, SectorMark.Faster),
            new(9771, 30, SectorMark.Slower),
            new(null, null, SectorMark.Pending),
            new(null, null, SectorMark.Pending),
            new(null, null, SectorMark.Pending),
        };
        var count = Math.Max(0, _settings.DeltaSectors.Value);
        return new DeltaView
        {
            HasReference = true,
            ReferenceMs = 41_590,
            OptimalMs = 41_204,
            Running = true,
            ElapsedMs = 23_410,
            DeltaMs = -234,
            Trend = -1,
            Sectors = sectors.GetRange(0, Math.Min(count, sectors.Count)),
        };
    }
}
