using System;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JmtLiftoffLeaderboard.Ui;

/// <summary>
/// One panel of the flying HUD: a card of its own, faded in and out on its own timer, and
/// drawn where the pilot put it at the size and strength they chose. What goes on it is
/// the subclass's; the edit mode that moves it is <see cref="RaceHud"/>'s.
/// </summary>
internal abstract class HudPanel
{
    private const float FadePerSecond = 4f;
    protected const float NewsSeconds = 8f;

    private CanvasGroup _group = null!;
    private Image _rule = null!;
    private bool _outlined;
    private float _fade;
    private float _shownUntil;
    private float _renderAgainAt = float.PositiveInfinity;
    private bool _dirty = true;

    protected HudPanel(string name, HudPanelSettings settings)
    {
        Name = name;
        Settings = settings;
        settings.Changed += MarkDirty;
    }

    /// <summary>What the edit toolbar calls it.</summary>
    public string Name { get; }

    public HudPanelSettings Settings { get; }
    public RectTransform Card { get; private set; } = null!;

    /// <summary>One of the panel's own settings changed, so the edit toolbar repaints them.</summary>
    public event Action? OptionsChanged;

    protected virtual float Width => 380;

    /// <summary>Whether it has anything to show outside edit mode.</summary>
    protected virtual bool HasContent => true;

    /// <summary>In edit mode: a panel with nothing to show yet draws an example, so it can be placed and sized.</summary>
    protected bool Editing { get; private set; }

    /// <summary>Until when something worth seeing keeps it up whatever its timer says: a license earned, a new best.</summary>
    protected float NewsUntil { get; private set; }

    public void Build(Transform root, RaceHud hud)
    {
        Card = Blocks.CardColumn(root, 8, 16, 12, Name);
        _rule = Card.GetComponent<Image>();
        Card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        // The pilot's opacity, and the fade, on this card alone.
        _group = Card.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0;
        var handle = Card.gameObject.AddComponent<HudHandle>();
        handle.Pressed = _ => hud.Select(this);
        handle.Dragged = drag => hud.Drag(this, drag);
        handle.Ended = _ => hud.Drop(this);
        handle.Scrolled = scroll => hud.Scroll(this, scroll);
        UiKit.Stripe(Card, Theme.Accent, RectTransform.Edge.Top, 2, Theme.RadiusCard);
        BuildContent(Card);
        Card.gameObject.SetActive(false);
    }

    protected abstract void BuildContent(RectTransform card);

    /// <summary>Draws what it shows; returns when it wants drawing again whether or not anything changes, for a message that expires.</summary>
    protected abstract float Render(float now);

    /// <summary>Rows for the edit toolbar, for settings only this panel has.</summary>
    public virtual void BuildOptions(Transform section)
    {
    }

    /// <summary>Repaints those rows from the settings.</summary>
    public virtual void RefreshOptions()
    {
    }

    /// <summary>Back to how it came: where it sits, how it looks, when it shows, and anything of its own.</summary>
    public virtual void Reset() => Settings.Reset();

    public void MarkDirty() => _dirty = true;

    /// <summary>Something the panel's rows in the edit toolbar show changed, other than a setting.</summary>
    protected void OptionsTouched()
    {
        MarkDirty();
        OptionsChanged?.Invoke();
    }

    /// <summary>One of the panel's own settings: redraw it, and the toolbar's row for it, when it changes, however it changed.</summary>
    protected void Watch<T>(ConfigEntry<T> entry) => entry.SettingChanged += (_, _) =>
    {
        MarkDirty();
        OptionsChanged?.Invoke();
    };

    public void Show(float seconds) => _shownUntil = Mathf.Max(_shownUntil, Time.realtimeSinceStartup + seconds);

    /// <summary>The drone is at the start: up for as long as the pilot asked.</summary>
    public void ShowAtStart() => Show(Mathf.Max(1, Settings.ShowSeconds.Value));

    protected void ShowNews()
    {
        NewsUntil = Time.realtimeSinceStartup + NewsSeconds;
        MarkDirty();
    }

    /// <summary>
    /// Called every frame. <paramref name="allowed"/> when the pilot is flying with no menu
    /// or JMT window open; in edit mode it shows regardless, faintly if it's switched off.
    /// </summary>
    public void Tick(float now, bool allowed, bool editing, bool pinned, bool dragging, bool selected)
    {
        Editing = editing;
        var mode = Settings.Show.Value;
        var wanted = editing
                     || (allowed && mode != HudMode.Off && HasContent
                         && (mode == HudMode.Always || pinned || now < _shownUntil || now < NewsUntil));
        _fade = editing ? 1f : Mathf.MoveTowards(_fade, wanted ? 1f : 0f, Time.unscaledDeltaTime * FadePerSecond);
        var visible = wanted || _fade > 0f;
        if (Card.gameObject.activeSelf != visible)
            Card.gameObject.SetActive(visible);
        if (!visible)
            return;

        var alpha = _fade * Mathf.Clamp(Settings.Opacity.Value, 0.2f, 1f) * (editing && mode == HudMode.Off ? 0.4f : 1f);
        if (!Mathf.Approximately(_group.alpha, alpha))
            _group.alpha = alpha;
        var outlined = editing && selected;
        if (outlined != _outlined)
        {
            _outlined = outlined;
            _rule.color = outlined ? Theme.Accent : Theme.Ink700;
        }

        if (!_dirty && now < _renderAgainAt)
            return;
        _dirty = false;
        Place(dragging);
        _renderAgainAt = Render(now);
    }

    private void Place(bool dragging)
    {
        Card.localScale = Vector3.one * Mathf.Clamp(Settings.Scale.Value, 0.4f, 1.6f);
        // Mid-drag the pilot has it; where it's dropped is saved when they let go.
        if (dragging)
            return;
        var corner = Settings.Corner.Value;
        var centre = corner is HudCorner.TopCenter or HudCorner.BottomCenter;
        var x = centre ? 0.5f : corner is HudCorner.TopLeft or HudCorner.BottomLeft ? 0f : 1f;
        var y = corner is HudCorner.BottomLeft or HudCorner.BottomRight or HudCorner.BottomCenter ? 0f : 1f;
        var offsetX = Mathf.Max(0, Settings.OffsetX.Value);
        var offsetY = Mathf.Max(0, Settings.OffsetY.Value);
        Card.anchorMin = Card.anchorMax = Card.pivot = new Vector2(x, y);
        Card.anchoredPosition = new Vector2(centre ? 0f : x == 0 ? offsetX : -offsetX, y == 0 ? offsetY : -offsetY);
        Card.sizeDelta = new Vector2(Width, Card.sizeDelta.y);
    }
}

/// <summary>The HUD's small formatting rules, shared by its panels.</summary>
internal static class HudText
{
    public static string Number(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Signed(double value) => (value >= 0 ? "+" : "-") + Number(Math.Abs(value));

    public static string Percent(float value) => $"{Mathf.RoundToInt(value * 100)}%";

    public static string Plural(int count, string one, string? many = null) =>
        count == 1 ? $"1 {one}" : $"{Theme.Count(count)} {many ?? one + "s"}";

    /// <summary>A lap time, without the "s" a board puts on one under a minute.</summary>
    public static string Lap(int ms) => ms < 60_000 ? Theme.Seconds(ms) : Theme.LapTime(ms);

    /// <summary>"+0.112", "-0.050": how far one lap is off another.</summary>
    public static string Gap(int ms) => (ms >= 0 ? "+" : "-") + Theme.Seconds(Math.Abs(ms));

    public static string Clip(string text, int max) =>
        text.Length <= max ? text : text.Substring(0, max - 1).TrimEnd() + "…";

    /// <summary>An empty label would still take a line in a card.</summary>
    public static void SetText(Text label, string text)
    {
        label.text = text;
        label.gameObject.SetActive(text.Length > 0);
    }
}

/// <summary>Hands the clicks, drags and scrolls on a HUD panel to the HUD. It only gets any in edit mode, when the HUD takes the mouse.</summary>
internal sealed class HudHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    public Action<PointerEventData>? Pressed;
    public Action<PointerEventData>? Dragged;
    public Action<PointerEventData>? Ended;
    public Action<PointerEventData>? Scrolled;

    public void OnPointerDown(PointerEventData eventData) => Pressed?.Invoke(eventData);

    // Declared so Unity starts a drag here rather than on whatever is behind.
    public void OnBeginDrag(PointerEventData eventData)
    {
    }

    public void OnDrag(PointerEventData eventData) => Dragged?.Invoke(eventData);

    public void OnEndDrag(PointerEventData eventData) => Ended?.Invoke(eventData);

    public void OnScroll(PointerEventData eventData) => Scrolled?.Invoke(eventData);
}
