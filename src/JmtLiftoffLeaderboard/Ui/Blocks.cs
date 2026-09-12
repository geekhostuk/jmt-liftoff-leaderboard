using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace JmtLiftoffLeaderboard.Ui;

/// <summary>The site's recurring pieces, built from <see cref="UiKit"/>: cards, stat strips, headings, notices.</summary>
internal static class Blocks
{
    /// <summary>A card whose content stacks top to bottom. The padding is inside the card's 1px rule.</summary>
    public static RectTransform CardColumn(Transform parent, float spacing = 12, int padX = 20, int padY = 18, string name = "Card")
    {
        var card = UiKit.Card(parent, name: name);
        UiKit.Stack(card.gameObject, spacing, new RectOffset(padX + 1, padX + 1, padY + 1, padY + 1));
        return card;
    }

    /// <summary>The site's StatStrip: big figures over small-caps labels, divided by thin rules.</summary>
    public static void StatStrip(Transform parent, params (string Label, string Value, Color? Tone)[] stats)
    {
        var card = UiKit.Card(parent, name: "StatStrip");
        var row = UiKit.Line(card.gameObject, 0, new RectOffset(1, 1, 1, 1));
        row.childForceExpandHeight = true;

        for (var i = 0; i < stats.Length; i++)
        {
            if (i > 0)
            {
                var rule = UiKit.Panel(card, Theme.Ink750, 0, "Rule");
                rule.raycastTarget = false;
                UiKit.Size(rule, 1);
            }
            var cell = UiKit.Column(card, 4, 20, 14, stats[i].Label);
            UiKit.Size(cell, 0, -1, 1);
            UiKit.Caption(cell, stats[i].Label);
            var value = UiKit.Label(cell, stats[i].Value, 30, stats[i].Tone ?? Theme.Ink200, UiKit.Heading, FontStyle.Bold);
            UiKit.Size(value, -1, 38);
        }
    }

    /// <summary>A section heading with the site's small orange square, and an optional note on the right.</summary>
    public static void Heading(Transform parent, string title, string? note = null)
    {
        var row = UiKit.Row(parent, 10, name: title);
        UiKit.Size(row, -1, 34);
        var square = UiKit.Panel(row, Theme.Accent, 0, "Square");
        UiKit.Size(square, 10, 10);
        UiKit.OneLine(UiKit.Label(row, title, 24, Theme.Ink200, UiKit.Heading, FontStyle.Bold));
        if (note == null)
            return;
        UiKit.Spacer(row);
        UiKit.OneLine(UiKit.Label(row, note, 14, Theme.Ink600));
    }

    /// <summary>A notice in place of content: an empty board, a stock track, the site being down.</summary>
    public static void Message(Transform parent, string title, string body, (string Text, UnityAction Action)? button = null)
    {
        var card = CardColumn(parent, 12, 28, 26, "Message");
        UiKit.Label(card, title, 26, Theme.Ink200, UiKit.Heading, FontStyle.Bold);
        var text = UiKit.Label(card, body, 17, Theme.Ink400);
        text.verticalOverflow = VerticalWrapMode.Overflow;
        if (button == null)
            return;
        var row = UiKit.Row(card, 10);
        UiKit.Button(row, button.Value.Text, button.Value.Action, UiKit.ButtonKind.Primary);
    }

    public static void Loading(Transform parent, string text)
    {
        var label = UiKit.Label(parent, text, 17, Theme.Ink600);
        UiKit.Size(label, -1, 40);
    }

    /// <summary>A small fact: caption above, value below.</summary>
    public static void Fact(Transform parent, string caption, string value, Color? tone = null)
    {
        var column = UiKit.Column(parent, 2, name: caption);
        UiKit.Rigid(column);
        UiKit.OneLine(UiKit.Caption(column, caption, size: 12));
        UiKit.OneLine(UiKit.Label(column, value, 16, tone ?? Theme.Ink200, UiKit.Mono));
    }

    /// <summary>"▲2" green, "▼1" red, "–" held, or nothing when there is no earlier standing to move from.</summary>
    public static string Movement(int? movement) => movement switch
    {
        null => "",
        > 0 => $"<color={Theme.Html(Theme.Signal)}>▲{movement}</color>",
        < 0 => $"<color={Theme.Html(Theme.Alarm)}>▼{-movement}</color>",
        _ => $"<color={Theme.Html(Theme.Ink600)}>–</color>",
    };

    /// <summary>A table's header row: small caps over each column, widths matching the rows below.</summary>
    public static void TableHeader(Transform parent, params (string Label, float Width, TextAnchor Align)[] columns)
    {
        var head = UiKit.Panel(parent, Theme.Ink800, 0, "Head");
        head.raycastTarget = false;
        UiKit.Size(head, -1, 34);
        var row = UiKit.Line(head.gameObject, 0, new RectOffset(18, 18, 0, 0));
        row.childForceExpandHeight = true;
        foreach (var (label, width, align) in columns)
        {
            var cell = UiKit.Caption(head.transform, label, size: 12);
            cell.alignment = align;
            if (width > 0)
                UiKit.Size(cell, width);
            else
                UiKit.Size(cell, 0, -1, 1);
        }
    }
}

/// <summary>Runs an action once typing pauses, so a search box doesn't send a request per key.</summary>
internal sealed class Debouncer
{
    private int _ticket;

    public void Run(Action action, float seconds = 0.35f)
    {
        var ticket = ++_ticket;
        Plugin.Instance.StartCoroutine(After(seconds, () =>
        {
            if (ticket == _ticket)
                action();
        }));
    }

    private static IEnumerator After(float seconds, Action action)
    {
        yield return new WaitForSecondsRealtime(seconds);
        action();
    }
}
