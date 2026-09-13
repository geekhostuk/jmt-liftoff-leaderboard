using System;
using System.Collections.Generic;
using JmtLiftoffLeaderboard.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static JmtLiftoffLeaderboard.Ui.HudText;

namespace JmtLiftoffLeaderboard.Ui;

/// <summary>
/// The lap review (Hud.ReviewKey, Ctrl+F7), while flying or from the pause menu: the laps
/// flown on the course this session, newest first, and the one picked taken apart against
/// the lap it's compared with. It has the time each stretch between gates gained or lost,
/// and the delta as it ran through the lap, drawn over the other recent laps' so an odd one
/// out shows. For each stretch there's also how the last ten clean laps did against the
/// pilot's best for it, which says where the time goes lap after lap rather than on the one.
///
/// It's a window of its own over the game, with a backdrop that takes every click, and the
/// HUD's panels step aside while it's open. It follows the laps as they're flown: the newest
/// is shown until the pilot picks another. <see cref="RaceHud"/> opens and closes it.
/// </summary>
internal sealed class LapReview
{
    /// <summary>How many clean laps the trends are taken over.</summary>
    private const int TrendLaps = 10;
    /// <summary>How many other laps are drawn faintly behind the trace.</summary>
    private const int OtherTraces = 6;

    private const float ListWidth = 330;
    // The gate table's columns.
    private const float GateWidth = 70;
    private const float TimeWidth = 72;
    private const float DeltaWidth = 74;
    private const float HeatCell = 9;
    private const float HeatGap = 2;
    private const float HeatWidth = TrendLaps * HeatCell + (TrendLaps - 1) * HeatGap;

    private static readonly string[] CompareLabels = { "Best ever", "Tonight", "Possible", "Lap before" };
    /// <summary>The trace's scale either way of zero, in milliseconds: the smallest that fits.</summary>
    private static readonly int[] ChartRanges = { 100, 200, 250, 500, 1000, 2000, 5000, 10_000, 30_000, 60_000 };

    private readonly Plugin _plugin;
    private readonly Settings _settings;
    private readonly DeltaTracker _delta;
    private readonly Action _close;

    private GameObject? _root;
    private Text _course = null!;
    private Action<int> _paintCompare = null!;
    private Text _best = null!, _tonight = null!, _possible = null!, _average = null!, _averageCaption = null!, _spread = null!, _clean = null!;
    private RectTransform _lapList = null!;
    private Text _notice = null!;
    private RectTransform _detail = null!;
    private Text _title = null!, _titleDelta = null!, _against = null!, _summary = null!;
    private RectTransform _analysis = null!;
    private Text _trend = null!;
    private TraceGraphic _trace = null!;
    private Text _axisTop = null!, _axisBottom = null!, _axisZero = null!, _behind = null!, _ahead = null!, _chartNote = null!;
    private Text _headDelta = null!, _headHeat = null!;
    private RectTransform _rowList = null!;
    private readonly List<GateRow> _rows = new();

    private bool _opened;
    private ReviewCompare _compare;
    private FlownLap? _picked;
    private DeltaRun? _shownRun;
    private int _shownVersion = -1;

    public LapReview(Plugin plugin, Settings settings, DeltaTracker delta, Action close)
    {
        _plugin = plugin;
        _settings = settings;
        _delta = delta;
        _close = close;
    }

    public bool IsOpen => _root != null && _root.activeSelf;

    public void Open()
    {
        if (!_opened)
        {
            // It first compares with what the delta bar does.
            _opened = true;
            _compare = _settings.DeltaCompare.Value == DeltaCompare.Tonight ? ReviewCompare.Tonight : ReviewCompare.BestEver;
        }
        Build();
        _root!.SetActive(true);
        _picked = null;
        Render();
        // The pause menu keeps keyboard and pad focus otherwise, and would act on it behind us.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void Close()
    {
        if (_root != null)
            _root.SetActive(false);
    }

    /// <summary>Called every frame while it's open: a lap flown, or another course, redraws it.</summary>
    public void Tick()
    {
        var run = _delta.Run;
        if (run != _shownRun || (run?.LapsVersion ?? -1) != _shownVersion)
            Render();
    }

    // ── Building ────────────────────────────────────────────────────────────

    private void Build()
    {
        if (_root != null)
            return;

        _root = new GameObject("JmtLapReview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _root.transform.SetParent(_plugin.transform, false);
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Over the game and the HUD, under the JMT window.
        canvas.sortingOrder = short.MaxValue - 1;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        var root = (RectTransform)_root.transform;

        // Takes every click, so nothing in the menu behind reacts.
        var backdrop = UiKit.Panel(root, Theme.Alpha(Theme.Ink950, 0.88f), 0, "Backdrop");
        UiKit.Fill(backdrop.rectTransform);

        var window = Blocks.CardColumn(root, 14, 22, 18, "LapReview");
        UiKit.Fill(window, 140, 60, 140, 60);
        UiKit.Stripe(window, Theme.Accent, RectTransform.Edge.Top, 2, Theme.RadiusCard);

        BuildHeader(window);
        BuildFacts(window);
        var body = UiKit.Row(window, 18, 0, 0, TextAnchor.UpperLeft, "Body");
        UiKit.Size(body, -1, -1, 1, 1);
        BuildLapList(body);
        BuildDetail(body);
        _root.SetActive(false);
    }

    private void BuildHeader(Transform window)
    {
        var head = UiKit.Row(window, 12, name: "Head");
        UiKit.Size(head, -1, 40);
        var square = UiKit.Panel(head, Theme.Accent, 0, "Square");
        UiKit.Size(square, 10, 10);
        UiKit.OneLine(UiKit.Label(head, "Lap review", 26, Theme.Ink200, UiKit.Heading, FontStyle.Bold));
        _course = UiKit.OneLine(UiKit.Label(head, "", 17, Theme.Ink400));
        UiKit.Spacer(head);
        UiKit.OneLine(UiKit.Caption(head, "Compared with"));
        _paintCompare = UiKit.Segmented(head, CompareLabels, (int)_compare, i =>
        {
            _compare = (ReviewCompare)i;
            Render();
        });
        UiKit.Button(head, "Close", () => _close(), UiKit.ButtonKind.Ghost, 15, 34);
    }

    private void BuildFacts(Transform window)
    {
        var row = UiKit.Row(window, 36, name: "Facts");
        _best = Fact(row, "Your best", out _);
        _tonight = Fact(row, "Best tonight", out _);
        _possible = Fact(row, "Possible", out _);
        _average = Fact(row, "Average", out _averageCaption);
        _spread = Fact(row, "Spread", out _);
        _clean = Fact(row, "Clean laps", out _);
    }

    private static Text Fact(Transform parent, string caption, out Text captionLabel)
    {
        var column = UiKit.Column(parent, 2, name: caption);
        UiKit.Rigid(column);
        captionLabel = UiKit.OneLine(UiKit.Caption(column, caption, size: 12));
        return UiKit.OneLine(UiKit.Label(column, "—", 20, Theme.Ink200, UiKit.Mono, FontStyle.Bold));
    }

    private void BuildLapList(Transform body)
    {
        var column = UiKit.Column(body, 8, name: "Laps");
        UiKit.Size(column, ListWidth, -1, -1, 1);
        UiKit.OneLine(UiKit.Caption(column, "This session, newest first"));
        _lapList = UiKit.Scroll(column, out _, 4);
        UiKit.Size(_lapList.parent, -1, -1, 1, 1);
    }

    private void BuildDetail(Transform body)
    {
        var column = UiKit.Column(body, 10, name: "Detail");
        UiKit.Size(column, 0, -1, 1, 1);

        _notice = UiKit.Label(column, "", 17, Theme.Ink400);
        _notice.verticalOverflow = VerticalWrapMode.Overflow;

        _detail = UiKit.Column(column, 10, name: "Lap");
        UiKit.Size(_detail, -1, -1, -1, 1);
        var title = UiKit.Row(_detail, 14, name: "Title");
        _title = UiKit.OneLine(UiKit.Label(title, "", 24, Theme.Ink200, UiKit.Heading, FontStyle.Bold));
        _titleDelta = UiKit.OneLine(UiKit.Label(title, "", 24, Theme.Ink200, UiKit.Mono, FontStyle.Bold));
        UiKit.Spacer(title);
        _against = UiKit.OneLine(UiKit.Label(title, "", 15, Theme.Ink600));
        _summary = UiKit.Label(_detail, "", 16, Theme.Ink400);
        _summary.verticalOverflow = VerticalWrapMode.Overflow;

        _analysis = UiKit.Column(_detail, 10, name: "Analysis");
        UiKit.Size(_analysis, -1, -1, -1, 1);
        _trend = UiKit.Label(_analysis, "", 16, Theme.Ink400);
        _trend.verticalOverflow = VerticalWrapMode.Overflow;
        BuildChart(_analysis);
        BuildTable(_analysis);
        var legend = UiKit.Label(_analysis,
            $"Last {TrendLaps}: each clean lap's stretch against your best for it, oldest on the left: "
            + $"{Colored("purple", Theme.Purple)} your best, {Colored("green", Theme.Signal)} within 3%, "
            + $"{Colored("yellow", Theme.Slower)} within 8%, {Colored("red", Theme.Alarm)} more. Avg lost is what they give away there on average.",
            13, Theme.Ink600);
        legend.verticalOverflow = VerticalWrapMode.Overflow;
    }

    /// <summary>The delta through the lap: behind above the line, ahead below it, a mark at each gate.</summary>
    private void BuildChart(Transform parent)
    {
        var box = UiKit.Panel(parent, Theme.Ink900, Theme.RadiusControl, "Trace");
        box.raycastTarget = false;
        UiKit.Size(box, -1, 150);
        var plot = UiKit.Node("Plot", box.transform);
        UiKit.Fill(plot, 62, 12, 70, 12);
        _trace = plot.gameObject.AddComponent<TraceGraphic>();
        _trace.raycastTarget = false;

        _axisTop = Axis(box.transform, 1f, -12f, Theme.Alarm, TextAnchor.MiddleRight, 0);
        _axisZero = Axis(box.transform, 0.5f, 0f, Theme.Ink600, TextAnchor.MiddleRight, 0);
        _axisBottom = Axis(box.transform, 0f, 12f, Theme.Signal, TextAnchor.MiddleRight, 0);
        _behind = Axis(box.transform, 1f, -12f, Theme.Alarm, TextAnchor.MiddleLeft, 1);
        _ahead = Axis(box.transform, 0f, 12f, Theme.Signal, TextAnchor.MiddleLeft, 1);
        _behind.text = "BEHIND";
        _ahead.text = "AHEAD";
        _chartNote = UiKit.Label(box.transform, "", 15, Theme.Ink600, align: TextAnchor.MiddleCenter);
        UiKit.Fill(_chartNote.rectTransform);
    }

    private static Text Axis(Transform box, float y, float offset, Color color, TextAnchor align, float side)
    {
        var label = UiKit.Label(box, "", 12, color, side == 0 ? UiKit.Mono : UiKit.Heading, FontStyle.Bold, align);
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        var rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(side, y);
        rect.pivot = new Vector2(side, 0.5f);
        rect.sizeDelta = new Vector2(54, 16);
        rect.anchoredPosition = new Vector2(side == 0 ? 2 : -8, offset);
        return label;
    }

    private void BuildTable(Transform parent)
    {
        var head = UiKit.Panel(parent, Theme.Ink800, Theme.RadiusControl, "Head");
        head.raycastTarget = false;
        UiKit.Size(head, -1, 30);
        UiKit.Line(head.gameObject, 8, new RectOffset(12, 12, 0, 0)).childForceExpandHeight = true;
        Head(head.transform, "Gate", GateWidth, TextAnchor.MiddleLeft);
        Head(head.transform, "Split", TimeWidth, TextAnchor.MiddleRight);
        Head(head.transform, "Stretch", TimeWidth, TextAnchor.MiddleRight);
        Head(head.transform, "◂ gained · lost ▸", 0, TextAnchor.MiddleCenter);
        _headDelta = Head(head.transform, "", DeltaWidth, TextAnchor.MiddleRight);
        Head(head.transform, "Running", DeltaWidth, TextAnchor.MiddleRight);
        Head(head.transform, "Your best", TimeWidth, TextAnchor.MiddleRight);
        _headHeat = Head(head.transform, "", HeatWidth, TextAnchor.MiddleLeft);
        Head(head.transform, "Avg lost", DeltaWidth, TextAnchor.MiddleRight);

        _rowList = UiKit.Scroll(parent, out _, 1);
        UiKit.Size(_rowList.parent, -1, -1, 1, 1);
    }

    private static Text Head(Transform head, string text, float width, TextAnchor align)
    {
        var cell = UiKit.Caption(head, text, size: 12);
        cell.alignment = align;
        cell.horizontalOverflow = HorizontalWrapMode.Overflow;
        if (width > 0)
            UiKit.Size(cell, width);
        else
            UiKit.Size(cell, 0, -1, 1);
        return cell;
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    private void Render()
    {
        if (_root == null)
            return;
        var run = _delta.Run;
        if (run != _shownRun)
            _picked = null;
        _shownRun = run;
        _shownVersion = run?.LapsVersion ?? -1;
        _paintCompare((int)_compare);
        _course.text = run == null ? "" : Clip(_delta.TrackName, 44);

        if (run == null)
        {
            RenderFacts(null, new List<FlownLap>());
            UiKit.Clear(_lapList);
            Notice("Load a course and fly it to review your laps.");
            return;
        }

        var laps = run.Laps;
        var clean = ReviewMath.Recent(laps, run.Course, TrendLaps);
        RenderFacts(run, clean);
        var lap = _picked != null && ReviewMath.IndexOf(laps, _picked) >= 0 ? _picked : laps.Count > 0 ? laps[laps.Count - 1] : null;
        RenderLapList(run, lap);
        if (lap == null)
        {
            Notice(run.Course.Best == null
                ? "Your laps on this course show here as you fly them, gate by gate. The first clean lap through every gate becomes the one to beat."
                : "Your laps on this course show here as you fly them, gate by gate. Your best lap is kept on this computer; the laps here are kept until the game closes.");
            return;
        }
        RenderLap(run, lap, clean);
    }

    private void Notice(string text)
    {
        _detail.gameObject.SetActive(false);
        SetText(_notice, text);
    }

    private void RenderFacts(DeltaRun? run, List<FlownLap> clean)
    {
        var course = run?.Course;
        Time(_best, course?.Best?.LapMs, Theme.Purple);
        Time(_tonight, run?.Tonight?.LapMs, Theme.Ink200);
        Time(_possible, course == null ? null : ReviewMath.Possible(course)?.LapMs, Theme.Ink200);
        var spread = ReviewMath.Spread(clean);
        _averageCaption.text = (clean.Count > 1 ? $"Average, last {clean.Count}" : "Average").ToUpperInvariant();
        Time(_average, spread?.MeanMs, Theme.Ink200);
        _spread.text = spread?.SpreadMs is { } ms ? "±" + Theme.Seconds(ms) : "—";
        _spread.color = spread?.SpreadMs != null ? Theme.Ink200 : Theme.Ink600;

        var laps = run?.Laps;
        var cleanCount = 0;
        if (laps != null)
        {
            foreach (var lap in laps)
            {
                if (ReviewMath.Clean(lap, course!))
                    cleanCount++;
            }
        }
        _clean.text = laps == null || laps.Count == 0 ? "—" : $"{cleanCount} of {laps.Count}";
        _clean.color = laps == null || laps.Count == 0 ? Theme.Ink600 : Theme.Ink200;

        static void Time(Text label, int? ms, Color tone)
        {
            label.text = ms is { } lap ? Lap(lap) : "—";
            label.color = ms != null ? tone : Theme.Ink600;
        }
    }

    private void RenderLapList(DeltaRun run, FlownLap? picked)
    {
        UiKit.Clear(_lapList);
        for (var i = run.Laps.Count - 1; i >= 0; i--)
            LapRow(run, run.Laps[i], run.Laps[i] == picked);
    }

    private void LapRow(DeltaRun run, FlownLap lap, bool selected)
    {
        var row = UiKit.Panel(_lapList, selected ? Theme.Ink700 : Theme.Ink800, Theme.RadiusControl, $"Lap {lap.Number}");
        UiKit.Line(row.gameObject, 8, new RectOffset(14, 12, 0, 0));
        UiKit.Size(row, -1, 36);
        if (selected)
            UiKit.Stripe(row.transform, Theme.Accent, RectTransform.Edge.Left, 3, Theme.RadiusControl);

        var number = UiKit.Label(row.transform, $"#{lap.Number}", 14, Theme.Ink600, UiKit.Heading, FontStyle.Bold);
        UiKit.Size(number, 34, -1);
        UiKit.OneLine(UiKit.Label(row.transform, lap.LapMs is { } ms ? Lap(ms) : "Reset", 17,
            lap.Finished ? Theme.Ink200 : Theme.Ink600, UiKit.Mono, FontStyle.Bold));
        if (Tag(run.Course, lap) is { } tag)
            UiKit.Badge(row.transform, tag.Text, tag.Fill, tag.Color);
        UiKit.Spacer(row.transform);

        var breakdown = ReviewMath.Breakdown(lap, run.Course, Reference(run, lap).Splits);
        var delta = UiKit.Label(row.transform, "", 15, Theme.Ink600, UiKit.Mono, FontStyle.Normal, TextAnchor.MiddleRight);
        UiKit.Size(delta, 70, -1);
        if (breakdown.IsReference)
            delta.text = "REF";
        else if (breakdown.DeltaMs is { } gap)
            (delta.text, delta.color) = (Gap(gap), DeltaTone(gap));

        UiKit.Clickable(row, () =>
        {
            _picked = lap;
            Render();
        });
    }

    private static (string Text, Color Fill, Color Color)? Tag(CourseSplits course, FlownLap lap)
    {
        if (!lap.Measured)
            return ("Partial", Theme.Ink750, Theme.Ink400);
        if (!ReviewMath.Comparable(lap, course))
            return ("Other gates", Theme.Ink750, Theme.Ink400);
        if (!lap.Finished)
            return ($"At gate {lap.Gates.Count}", Theme.Alpha(Theme.Alarm, 0.15f), Theme.Alarm);
        return lap.Outcome switch
        {
            LapOutcome.NewBest => ("PB", Theme.Alpha(Theme.Purple, 0.18f), Theme.Purple),
            LapOutcome.CourseLearned => ("First", Theme.Ink750, Theme.Ink400),
            _ => null,
        };
    }

    /// <summary>The lap <paramref name="lap"/> is compared with; what to call it; and what to say when there's none.</summary>
    private (LapSplits? Splits, string Name, string Missing) Reference(DeltaRun run, FlownLap lap)
    {
        var course = run.Course;
        switch (_compare)
        {
            case ReviewCompare.Tonight:
                return (run.Tonight, "tonight's best", "no clean lap tonight yet");
            case ReviewCompare.Possible:
                return (ReviewMath.Possible(course), "your possible lap", "no possible lap until every stretch has a best");
            case ReviewCompare.LapBefore:
                var before = ReviewMath.LapBefore(run.Laps, lap, course);
                return (before == null ? null : ReviewMath.Splits(before), before == null ? "the lap before" : $"lap #{before.Number}",
                    "no clean lap before this one");
            default:
                return (course.Best, "your best", "no best lap yet");
        }
    }

    private void RenderLap(DeltaRun run, FlownLap lap, List<FlownLap> clean)
    {
        SetText(_notice, "");
        _detail.gameObject.SetActive(true);
        var course = run.Course;
        var gates = course.Gates.Count;
        var (reference, name, missing) = Reference(run, lap);
        var breakdown = ReviewMath.Breakdown(lap, course, reference);

        _title.text = lap.LapMs is { } ms
            ? $"#{lap.Number} · {Lap(ms)}"
            : $"#{lap.Number} · reset after gate {lap.Gates.Count}{(breakdown.Problem == null ? $" of {gates}" : "")}";
        if (breakdown.DeltaMs is { } delta && !breakdown.IsReference)
            (_titleDelta.text, _titleDelta.color) = (Gap(delta), DeltaTone(delta));
        else
            _titleDelta.text = "";
        _against.text = breakdown.Problem != null ? ""
            : reference == null ? missing
            : breakdown.IsReference ? $"this is {name}"
            : $"against {name}, {Lap(reference.LapMs)}";

        if (breakdown.Problem != null)
        {
            SetText(_summary, breakdown.Problem);
            _analysis.gameObject.SetActive(false);
            return;
        }
        SetText(_summary, LapSummary(lap, breakdown, gates, name));
        _analysis.gameObject.SetActive(true);

        var trends = ReviewMath.Trends(clean, course);
        SetText(_trend, TrendSummary(clean, trends, gates));
        var others = new List<LapBreakdown>();
        for (var i = clean.Count - 1; i >= 0 && others.Count < OtherTraces; i--)
        {
            if (clean[i] != lap)
                others.Add(ReviewMath.Breakdown(clean[i], course, reference));
        }
        RenderChart(breakdown, reference, gates, others);
        RenderTable(breakdown, trends, clean, lap, gates, name);
    }

    private string LapSummary(FlownLap lap, LapBreakdown breakdown, int gates, string name)
    {
        if (!breakdown.Compared)
            return "";
        if (breakdown.IsReference)
            return _compare == ReviewCompare.Possible ? "" : "This is the lap it's compared with. Pick Possible to see what's left in it.";
        var sentences = new List<string>();
        var lost = new List<string>();
        foreach (var k in breakdown.Worst)
            lost.Add($"{StretchName(k, gates)} {Colored(Gap(breakdown.Stretches[k].DeltaMs!.Value), Theme.Alarm)}");
        if (lost.Count > 0)
            sentences.Add($"Lost most at {string.Join(", ", lost.ToArray())}.");
        else
            sentences.Add(lap.Finished ? $"Quicker than {name} on every stretch." : "Quicker on every stretch before the reset.");
        if (breakdown.BestGain is { } gain)
            sentences.Add($"Gained most at {StretchName(gain, gates)} {Colored(Gap(breakdown.Stretches[gain].DeltaMs!.Value), Theme.Signal)}.");
        return string.Join(" ", sentences.ToArray());
    }

    private static string TrendSummary(List<FlownLap> clean, List<GateTrend> trends, int gates)
    {
        if (clean.Count < 2)
            return "Fly a few clean laps and this says where the time goes lap after lap.";
        var total = 0;
        foreach (var trend in trends)
            total += trend.AvgLostMs.GetValueOrDefault();
        var most = new List<string>();
        foreach (var k in ReviewMath.MostLost(trends, ReviewMath.WorstShown))
            most.Add($"{StretchName(k, gates)} {Colored(Gap(trends[k].AvgLostMs!.Value), Theme.Slower)}");
        var text = $"Your last {clean.Count} clean laps give away {Colored(Theme.Seconds(total), Theme.Ink200)} a lap to your best stretches";
        return most.Count > 0 ? $"{text}, most at {string.Join(", ", most.ToArray())}." : $"{text}.";
    }

    /// <summary>
    /// The delta at each gate, placed along the lap by where the lap compared with reached it:
    /// red where a stretch lost time, green where it gained, a mark at each gate coloured by
    /// how it went against the pilot's best, and the other recent laps faintly behind.
    /// </summary>
    private void RenderChart(LapBreakdown breakdown, LapSplits? reference, int gates, List<LapBreakdown> others)
    {
        _trace.Clear();
        var compared = reference != null && breakdown.Compared;
        _axisTop.gameObject.SetActive(compared);
        _axisZero.gameObject.SetActive(compared);
        _axisBottom.gameObject.SetActive(compared);
        _behind.gameObject.SetActive(compared);
        _ahead.gameObject.SetActive(compared);
        _chartNote.text = compared ? "" : "Nothing to compare this lap with.";
        if (!compared)
        {
            _trace.Redraw();
            return;
        }

        var lapMs = Math.Max(1, reference!.LapMs);
        float X(int k) => k >= gates ? 1f : Mathf.Clamp01(reference.Times[k] / (float)lapMs);
        var biggest = 0;
        foreach (var line in breakdown.Stretches)
            biggest = Math.Max(biggest, Math.Abs(line.RunningMs.GetValueOrDefault()));
        foreach (var other in others)
        {
            foreach (var line in other.Stretches)
                biggest = Math.Max(biggest, Math.Abs(line.RunningMs.GetValueOrDefault()));
        }
        var range = ChartRanges[ChartRanges.Length - 1];
        foreach (var candidate in ChartRanges)
        {
            if (candidate >= biggest * 1.1f)
            {
                range = candidate;
                break;
            }
        }
        float Y(int ms) => 0.5f + Mathf.Clamp(ms / (2f * range), -0.5f, 0.5f);
        _axisTop.text = "+" + Theme.Seconds(range, "0.00");
        _axisBottom.text = "-" + Theme.Seconds(range, "0.00");
        _axisZero.text = "0";

        for (var k = 0; k < gates; k++)
            _trace.Line(new Vector2(X(k), 0), new Vector2(X(k), 1), Theme.Alpha(Theme.Ink700, 0.5f), 1);
        _trace.Line(new Vector2(0, 0.5f), new Vector2(1, 0.5f), Theme.Ink600, 1);

        foreach (var other in others)
            Trace(other, Theme.Alpha(Theme.Ink400, 0.28f), 1.5f, false);
        Trace(breakdown, Color.white, 3f, true);
        _trace.Redraw();

        void Trace(LapBreakdown lap, Color faint, float width, bool main)
        {
            var from = new Vector2(0, Y(0));
            var fromMs = 0;
            foreach (var line in lap.Stretches)
            {
                if (line.RunningMs is not { } running)
                    break;
                var to = new Vector2(X(line.Index), Y(running));
                var color = !main ? faint : running > fromMs ? Theme.Alarm : running < fromMs ? Theme.Signal : Theme.Ink400;
                _trace.Line(from, to, color, width);
                if (main && line.Ms is { } ms && line.BestMs is { } best)
                    _trace.Dot(to, HeatColor(ReviewMath.Tone(ms - best, best)), 7);
                from = to;
                fromMs = running;
            }
        }
    }

    private void RenderTable(LapBreakdown breakdown, List<GateTrend> trends, List<FlownLap> clean, FlownLap lap, int gates, string name)
    {
        _headDelta.text = (_compare switch
        {
            ReviewCompare.BestEver => "vs best",
            ReviewCompare.Tonight => "vs tonight",
            ReviewCompare.Possible => "vs possible",
            _ => name.StartsWith("lap #", StringComparison.Ordinal) ? $"vs {name.Substring(4)}" : "vs lap before",
        }).ToUpperInvariant();
        _headHeat.text = (clean.Count > 0 ? $"Last {clean.Count}" : $"Last {TrendLaps}").ToUpperInvariant();

        var count = gates + 1;
        while (_rows.Count < count)
            _rows.Add(new GateRow(_rowList));
        var scale = 100;
        foreach (var line in breakdown.Stretches)
            scale = Math.Max(scale, Math.Abs(line.DeltaMs.GetValueOrDefault()));
        var mostLost = clean.Count >= 2 ? ReviewMath.MostLost(trends, ReviewMath.WorstShown) : new List<int>();
        var pickedIndex = clean.IndexOf(lap);

        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (i >= count)
            {
                row.Hide();
                continue;
            }
            var line = breakdown.Stretches[i];
            var trend = i < trends.Count ? trends[i] : null;
            var worst = breakdown.Worst.Contains(i);
            row.Show(worst ? Theme.Alpha(Theme.Alarm, 0.1f) : i % 2 == 1 ? Theme.Alpha(Theme.Ink800, 0.6f) : new Color(0, 0, 0, 0));

            row.Name.text = StretchName(i, gates);
            row.Split.text = line.SplitMs is { } split ? Lap(split) : "—";
            row.Split.color = line.Flown ? Theme.Ink400 : Theme.Ink600;
            var best = line.Ms is { } stretch && line.BestMs is { } bestMs && stretch <= bestMs;
            row.Stretch.text = line.Ms is { } ms ? Theme.Seconds(ms) : "—";
            row.Stretch.color = !line.Flown ? Theme.Ink600 : best ? Theme.Purple : Theme.Ink200;
            row.Bar(line.DeltaMs, scale);
            row.Delta.text = line.DeltaMs is { } delta ? Gap(delta) : "";
            row.Delta.color = DeltaTone(line.DeltaMs);
            row.Delta.fontStyle = worst ? FontStyle.Bold : FontStyle.Normal;
            row.Running.text = line.RunningMs is { } running ? Gap(running) : "";
            row.Running.color = Theme.Alpha(DeltaTone(line.RunningMs), 0.75f);
            row.Best.text = line.BestMs is { } bestTime ? Theme.Seconds(bestTime) : "—";
            row.Heat(trend, pickedIndex);
            var lost = trend?.AvgLostMs is { } avg && clean.Count >= 2;
            row.AvgLost.text = lost ? Gap(trend!.AvgLostMs!.Value) : "—";
            row.AvgLost.color = !lost ? Theme.Ink600 : mostLost.Contains(i) ? Theme.Slower : Theme.Ink400;
            row.AvgLost.fontStyle = mostLost.Contains(i) ? FontStyle.Bold : FontStyle.Normal;
        }
    }

    private static string StretchName(int k, int gates) => k >= gates ? "Finish" : $"Gate {k + 1}";

    private static Color DeltaTone(int? ms) => ms switch
    {
        null => Theme.Ink600,
        < 0 => Theme.Signal,
        > 0 => Theme.Alarm,
        _ => Theme.Ink400,
    };

    private static Color HeatColor(StretchTone tone) => tone switch
    {
        StretchTone.Best => Theme.Purple,
        StretchTone.Close => Theme.Signal,
        StretchTone.Off => Theme.Slower,
        StretchTone.Lost => Theme.Alarm,
        _ => Theme.Ink700,
    };

    private static string Colored(string text, Color color) => $"<color={Theme.Html(color)}>{text}</color>";

    /// <summary>One stretch of the lap in the gate table.</summary>
    private sealed class GateRow
    {
        private readonly Image _back;
        private readonly RectTransform _fill;
        private readonly Image _fillImage;
        private readonly List<Image> _heat = new();
        private readonly List<LayoutElement> _heatSize = new();

        public GateRow(Transform parent)
        {
            _back = UiKit.Panel(parent, new Color(0, 0, 0, 0), 4, "Stretch");
            _back.raycastTarget = false;
            UiKit.Line(_back.gameObject, 8, new RectOffset(12, 12, 0, 0)).childForceExpandHeight = true;
            UiKit.Size(_back, -1, 26);

            Name = Cell(GateWidth, TextAnchor.MiddleLeft, UiKit.Heading, 15);
            Split = Cell(TimeWidth, TextAnchor.MiddleRight, UiKit.Mono, 14);
            Stretch = Cell(TimeWidth, TextAnchor.MiddleRight, UiKit.Mono, 14);

            var bar = UiKit.Node("Bar", _back.transform);
            UiKit.Size(bar, 0, -1, 1);
            var centre = UiKit.Panel(bar, Theme.Ink600, 0, "Centre");
            centre.raycastTarget = false;
            var mark = centre.rectTransform;
            mark.anchorMin = new Vector2(0.5f, 0.15f);
            mark.anchorMax = new Vector2(0.5f, 0.85f);
            mark.offsetMin = new Vector2(-0.5f, 0);
            mark.offsetMax = new Vector2(0.5f, 0);
            _fillImage = UiKit.Panel(bar, Theme.Signal, 2, "Fill");
            _fillImage.raycastTarget = false;
            _fill = _fillImage.rectTransform;

            Delta = Cell(DeltaWidth, TextAnchor.MiddleRight, UiKit.Mono, 14);
            Running = Cell(DeltaWidth, TextAnchor.MiddleRight, UiKit.Mono, 14);
            Best = Cell(TimeWidth, TextAnchor.MiddleRight, UiKit.Mono, 14);
            Best.color = Theme.Alpha(Theme.Purple, 0.8f);

            var heat = UiKit.Node("Heat", _back.transform);
            UiKit.Line(heat.gameObject, HeatGap, new RectOffset(0, 0, 0, 0));
            UiKit.Size(heat, HeatWidth, -1);
            for (var i = 0; i < TrendLaps; i++)
            {
                var cell = UiKit.Panel(heat, Theme.Ink700, 2, "Lap");
                cell.raycastTarget = false;
                _heatSize.Add(UiKit.Size(cell, HeatCell, 12));
                _heat.Add(cell);
            }

            AvgLost = Cell(DeltaWidth, TextAnchor.MiddleRight, UiKit.Mono, 14);
        }

        public Text Name { get; }
        public Text Split { get; }
        public Text Stretch { get; }
        public Text Delta { get; }
        public Text Running { get; }
        public Text Best { get; }
        public Text AvgLost { get; }

        public void Hide() => _back.gameObject.SetActive(false);

        public void Show(Color back)
        {
            _back.gameObject.SetActive(true);
            _back.color = back;
        }

        /// <summary>A bar from the middle: left and green for time gained, right and red for time lost, full at <paramref name="scale"/>.</summary>
        public void Bar(int? delta, int scale)
        {
            var show = delta is { } d && d != 0;
            _fill.gameObject.SetActive(show);
            if (!show)
                return;
            var ms = delta!.Value;
            var half = Mathf.Clamp01(Math.Abs(ms) / (float)Math.Max(1, scale)) * 0.5f;
            _fill.anchorMin = new Vector2(ms < 0 ? 0.5f - half : 0.5f, 0.3f);
            _fill.anchorMax = new Vector2(ms < 0 ? 0.5f : 0.5f + half, 0.7f);
            _fill.offsetMin = _fill.offsetMax = Vector2.zero;
            _fillImage.color = ms < 0 ? Theme.Signal : Theme.Alarm;
        }

        /// <summary>A cell per recent clean lap, oldest first, coloured by how it went here; the lap picked stands taller.</summary>
        public void Heat(GateTrend? trend, int picked)
        {
            for (var i = 0; i < _heat.Count; i++)
            {
                var show = trend != null && trend.BestMs != null && i < trend.Lost.Count;
                _heat[i].gameObject.SetActive(show);
                if (!show)
                    continue;
                _heat[i].color = Theme.Alpha(HeatColor(ReviewMath.Tone(trend!.Lost[i], trend.BestMs!.Value)), i == picked ? 1f : 0.75f);
                _heatSize[i].minHeight = _heatSize[i].preferredHeight = i == picked ? 20 : 12;
            }
        }

        private Text Cell(float width, TextAnchor align, Font font, int size)
        {
            var label = UiKit.Label(_back.transform, "", size, Theme.Ink200, font, FontStyle.Normal, align);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Size(label, width, -1);
            return label;
        }
    }
}

/// <summary>
/// Straight lines and square marks in a rect's own 0 to 1 space, for the lap review's trace.
/// uGUI has no line of its own, so this builds the quads.
/// </summary>
internal sealed class TraceGraphic : MaskableGraphic
{
    private readonly List<(Vector2 From, Vector2 To, Color Color, float Width)> _lines = new();
    private readonly List<(Vector2 At, Color Color, float Size)> _dots = new();

    public void Clear()
    {
        _lines.Clear();
        _dots.Clear();
    }

    public void Line(Vector2 from, Vector2 to, Color color, float width) => _lines.Add((from, to, color, width));

    public void Dot(Vector2 at, Color color, float size) => _dots.Add((at, color, size));

    public void Redraw() => SetVerticesDirty();

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var rect = rectTransform.rect;
        Vector2 Map(Vector2 p) => new(rect.xMin + p.x * rect.width, rect.yMin + p.y * rect.height);

        foreach (var (from, to, color, width) in _lines)
        {
            var a = Map(from);
            var b = Map(to);
            var along = b - a;
            if (along.sqrMagnitude < 0.0001f)
                continue;
            var side = new Vector2(-along.y, along.x).normalized * (width / 2);
            Quad(vh, a - side, a + side, b + side, b - side, color);
        }
        foreach (var (at, color, size) in _dots)
        {
            var c = Map(at);
            var h = size / 2;
            Quad(vh, c + new Vector2(-h, -h), c + new Vector2(-h, h), c + new Vector2(h, h), c + new Vector2(h, -h), color);
        }
    }

    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        var start = vh.currentVertCount;
        var vertex = UIVertex.simpleVert;
        vertex.color = color;
        foreach (var corner in new[] { a, b, c, d })
        {
            vertex.position = corner;
            vh.AddVert(vertex);
        }
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 3, start);
    }
}
