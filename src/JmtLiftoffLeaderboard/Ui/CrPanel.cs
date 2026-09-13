using System;
using System.Collections.Generic;
using JmtLiftoffLeaderboard.Game;
using UnityEngine;
using UnityEngine.UI;
using static JmtLiftoffLeaderboard.Ui.HudText;

namespace JmtLiftoffLeaderboard.Ui;

/// <summary>
/// The Consistency Rating while flying: the rating with tonight's flying counted in, the
/// license it earns and how far off the next one is, and the last twenty attempts as pips,
/// a lap in green and a failed attempt in red.
/// </summary>
internal sealed class CrPanel : HudPanel
{
    private const float ToastSeconds = 2.5f;

    private readonly CrTracker _cr;

    private Text _state = null!;
    private Text _rating = null!;
    private Text _delta = null!;
    private RectTransform _pill = null!;
    private string? _pillName;
    private RectTransform _barFill = null!;
    private Image _barFillImage = null!;
    private RectTransform _barMark = null!;
    private Text _barFrom = null!;
    private Text _barTo = null!;
    private readonly List<Image> _pips = new();
    private Text _next = null!;
    private Text _night = null!;

    private string? _toast;
    private Color _toastColor;
    private float _toastUntil;
    private string? _news;
    private Color _newsColor;

    public CrPanel(HudPanelSettings settings, CrTracker cr) : base("CR", settings)
    {
        _cr = cr;
        cr.Changed += MarkDirty;
        cr.Pulsed += OnPulse;
    }

    private void OnPulse(Pulse pulse)
    {
        if (Math.Abs(pulse.Delta) >= 0.005)
        {
            _toast = Signed(pulse.Delta);
            _toastColor = pulse.Delta > 0 ? Theme.Signal : Theme.Alarm;
            _toastUntil = Time.realtimeSinceStartup + ToastSeconds;
        }
        if (pulse.Promoted != null)
        {
            _news = $"<b>{Theme.LicenseLabel(pulse.Promoted).ToUpperInvariant()} EARNED</b>";
            _newsColor = Theme.License(pulse.Promoted).Text;
            ShowNews();
        }
        else if (pulse.Demoted != null)
        {
            _news = $"<b>Down to {Theme.LicenseLabel(pulse.Demoted)}</b>";
            _newsColor = Theme.Alarm;
            ShowNews();
        }
        MarkDirty();
    }

    protected override void BuildContent(RectTransform card)
    {
        var top = UiKit.Row(card, 8, name: "Top");
        UiKit.OneLine(UiKit.Caption(top, "Consistency Rating"));
        UiKit.Spacer(top);
        _state = UiKit.OneLine(UiKit.Label(top, "", 12, Theme.Ink600, UiKit.Heading, FontStyle.Bold));

        var main = UiKit.Row(card, 10, name: "Rating");
        _rating = UiKit.OneLine(UiKit.Label(main, "", 44, Theme.Ink200, UiKit.Heading, FontStyle.Bold));
        _delta = UiKit.OneLine(UiKit.Label(main, "", 17, Theme.Ink600, UiKit.Heading, FontStyle.Bold));
        UiKit.Spacer(main);
        _pill = UiKit.Row(main, 0, name: "License");
        UiKit.Rigid(_pill);

        var bar = UiKit.Node("Bar", card);
        UiKit.Size(bar, -1, 8);
        var track = UiKit.Panel(bar, Theme.Ink750, 4, "Track");
        track.raycastTarget = false;
        UiKit.Fill(track.rectTransform);
        _barFillImage = UiKit.Panel(bar, Theme.Signal, 4, "Fill");
        _barFillImage.raycastTarget = false;
        _barFill = _barFillImage.rectTransform;
        _barFill.anchorMin = Vector2.zero;
        _barFill.anchorMax = new Vector2(0, 1);
        _barFill.offsetMin = _barFill.offsetMax = Vector2.zero;
        var mark = UiKit.Panel(bar, Theme.Alpha(Theme.Ink200, 0.85f), 0, "Tonight");
        mark.raycastTarget = false;
        _barMark = mark.rectTransform;

        var ends = UiKit.Row(card, 8, name: "Ends");
        _barFrom = UiKit.OneLine(UiKit.Label(ends, "", 12, Theme.Ink600, UiKit.Heading, FontStyle.Bold));
        UiKit.Spacer(ends);
        _barTo = UiKit.OneLine(UiKit.Label(ends, "", 12, Theme.Ink600, UiKit.Heading, FontStyle.Bold));

        var pips = UiKit.Row(card, 5, name: "Attempts");
        for (var i = 0; i < CrTracker.PipCount; i++)
        {
            var pip = UiKit.Panel(pips, Theme.Ink750, 0, "Pip");
            pip.sprite = UiKit.Circle();
            pip.type = Image.Type.Simple;
            pip.raycastTarget = false;
            UiKit.Size(pip, 12, 12);
            _pips.Add(pip);
        }

        _next = UiKit.Label(card, "", 15, Theme.Ink400);
        _night = UiKit.Label(card, "", 13, Theme.Ink600);
    }

    protected override float Render(float now)
    {
        if (_toast != null && now >= _toastUntil)
            _toast = null;
        if (_news != null && now >= NewsUntil)
            _news = null;

        (_state.text, _state.color) = _cr.Counting switch
        {
            Counting.Live => ("LIVE", Theme.Signal),
            Counting.Unknown => ("CHECKING ROOM", Theme.Ink600),
            _ => ("PRACTICE · NOT COUNTED", Theme.Ink600),
        };

        RenderPips();
        SetText(_night, NightLine());

        var standing = _cr.Now;
        if (standing == null)
        {
            _rating.text = "—";
            _delta.text = "";
            SetPill(null);
            SetBar(0, null, "", "");
            _next.color = Theme.Ink400;
            SetText(_next, _cr.State switch
            {
                CrState.NoPilot => "Fly a lap in a JMT room to get a rating.",
                CrState.Unreachable => "Can't reach the JMT site right now.",
                _ => "Finding you on the JMT site...",
            });
            return Again();
        }

        _rating.text = Number(standing.Cr);
        if (_toast != null)
        {
            _delta.text = _toast;
            _delta.color = _toastColor;
        }
        else if (_cr.NightStartCr is { } start && Math.Abs(standing.Cr - start) >= 0.005)
        {
            _delta.text = $"{Signed(standing.Cr - start)} tonight";
            _delta.color = standing.Cr > start ? Theme.Signal : Theme.Alarm;
        }
        else
        {
            _delta.text = "";
        }

        SetPill(standing.Rung.Name);
        RenderBar(standing);

        if (_news != null)
        {
            _next.color = _newsColor;
            SetText(_next, _news);
        }
        else
        {
            _next.color = Theme.Ink400;
            SetText(_next, NextLine(standing, _cr.Forecast));
        }
        return Again();

        float Again() => Mathf.Min(_toast != null ? _toastUntil : float.PositiveInfinity,
            _news != null ? NewsUntil : float.PositiveInfinity);
    }

    private void RenderBar(Standing standing)
    {
        var m = _cr.Model;
        var rung = standing.Rung;
        if (rung.Name == CrMath.Rookie)
        {
            SetBar(standing.Laps / (double)Math.Max(1, m.LicenseMinLaps), null, "ROOKIE", $"{m.LicenseMinLaps} LAPS");
            return;
        }

        var from = CrMath.Floor(m, rung.Name);
        var to = rung.NextAt ?? m.Max;
        double Fraction(double cr) => (cr - from) / Math.Max(0.01, to - from);
        double? mark = _cr.NightStartCr is { } start ? Fraction(start) : null;
        SetBar(Fraction(standing.Cr), mark,
            $"{rung.Name.ToUpperInvariant()} {Number(from)}",
            rung.NextName == null ? Number(to) : $"{rung.NextName.ToUpperInvariant()} {Number(to)}");
        _barFillImage.color = Theme.License(rung.Name).Text;
    }

    private void SetBar(double fraction, double? mark, string from, string to)
    {
        _barFill.anchorMax = new Vector2(Mathf.Clamp01((float)fraction), 1);
        _barFillImage.color = Theme.License(CrMath.Rookie).Text;
        _barFrom.text = from;
        _barTo.text = to;

        // Where tonight started, so the bar says which way tonight has gone.
        var showMark = mark is >= 0 and <= 1;
        _barMark.gameObject.SetActive(showMark);
        if (!showMark)
            return;
        var x = (float)mark!.Value;
        _barMark.anchorMin = new Vector2(x, 0);
        _barMark.anchorMax = new Vector2(x, 1);
        _barMark.offsetMin = new Vector2(-1, -3);
        _barMark.offsetMax = new Vector2(1, 3);
    }

    private void RenderPips()
    {
        var pips = _cr.Pips;
        var empty = _pips.Count - pips.Count;
        for (var i = 0; i < _pips.Count; i++)
        {
            if (i < empty)
            {
                _pips[i].color = Theme.Alpha(Theme.Ink750, 0.6f);
                continue;
            }
            var pip = pips[i - empty];
            var color = pip.Kind == PipKind.Lap ? Theme.Signal : Theme.Alarm;
            _pips[i].color = pip.Counted ? color : Theme.Alpha(color, 0.35f);
        }
    }

    private static string NextLine(Standing standing, Forecast? forecast)
    {
        var rung = standing.Rung;
        string line;
        if (rung.Name == CrMath.Rookie)
            line = forecast?.CleanLaps is { } laps ? $"{Plural(laps, "lap")} to your first license" : "Laps earn your first license";
        else if (rung.NextName == null)
            line = "Top license";
        else if (forecast?.CleanLaps is { } clean)
            line = $"{Plural(clean, "clean lap")} to {Theme.LicenseLabel(rung.NextName)}";
        else
            line = $"{Theme.LicenseLabel(rung.NextName)} is a long way off";

        if (forecast?.CrashesToDrop is { } crashes && forecast.DropsTo != null)
            line += $" · <color={Theme.Html(Theme.Gold)}>{Plural(crashes, "crash", "crashes")} from {Theme.LicenseLabel(forecast.DropsTo)}</color>";
        else if (forecast?.CrashCost is > 0 and var cost)
            line += $" · a crash costs {cost} more";
        return line;
    }

    private string NightLine()
    {
        var parts = new List<string>();
        if (_cr.NightLaps > 0 || _cr.NightFailed > 0)
            parts.Add($"Tonight {Theme.Count(_cr.NightFailed)} failed in {Plural(_cr.NightLaps, "lap")}");
        if (_cr.BestStreak > 0)
        {
            var streak = _cr.BestStreak > _cr.Streak ? $"streak {_cr.Streak} (best {_cr.BestStreak})" : $"streak {_cr.Streak}";
            parts.Add(parts.Count == 0 ? char.ToUpperInvariant(streak[0]) + streak.Substring(1) : streak);
        }
        return string.Join(" · ", parts);
    }

    private void SetPill(string? name)
    {
        if (name == _pillName)
            return;
        _pillName = name;
        UiKit.Clear(_pill);
        if (name == null)
            return;
        var (fill, tone) = Theme.License(name);
        UiKit.Pill(_pill, Theme.LicenseLabel(name), fill, tone, 14);
    }
}
