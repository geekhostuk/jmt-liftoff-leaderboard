using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using JmtLiftoffLeaderboard.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static JmtLiftoffLeaderboard.Ui.HudText;

namespace JmtLiftoffLeaderboard.Ui;

/// <summary>
/// The HUD shown while flying: its panels, the Consistency Rating, the course's board
/// around the pilot, the delta against their best lap and the race in the room, on a
/// canvas of their own under the JMT window.
///
/// It keeps them off the screen while any of the game's in-flight menus is open, and each
/// shows at the start and after each reset for as long as the pilot chose. In edit mode
/// (EditKey, from the air or the pause menu) the panels take the mouse: click one to edit
/// it, drag it, scroll over it to resize it, and the toolbar beside it sets its size,
/// opacity, how long it stays up and anything of its own. Where a panel is left is kept as
/// a corner, or the middle of the top or bottom edge, and an offset from it, so it stays
/// put when the resolution changes. Out of edit mode nothing on the HUD takes a click.
///
/// ReviewKey opens the <see cref="LapReview"/> in the same places; the panels step aside
/// while it's open.
/// </summary>
internal sealed class RaceHud
{
    private const float ScaleStep = 0.1f;
    private const float OpacityStep = 0.1f;
    // A panel dropped with its middle this close to the screen's is kept centred.
    private const float CentreSnap = 60f;
    private static readonly string[] StayLabels = { "Off", "5s", "10s", "20s", "30s", "Always" };

    /// <summary>
    /// The game's in-flight menus, which the HUD keeps out of the way of. These classes kept
    /// their names through the obfuscation. The first is the pause menu, and a scene that
    /// has one is a scene the pilot can fly in.
    /// </summary>
    private static readonly string[] MenuPanels =
    {
        "InGameMenuMainPanel",
        "InGameMenuOptionsSelectionPanel",
        "InGameMenuGraphicsOptionsPanel",
        "InGameMenuAudioOptionsPanel",
        "InGameMenuGameOptionsPanel",
        "InGameMenuSelectSpawnPointPanel",
        "InGameMenuFCSPanel",
        "InGameMenuEditFCSBlueprintPanel",
        "InGameMenuDroneSelection",
        "InGameMenuScoreScreenPanel",
        "InGameMenuRaceScoreScreen",
        "InGameMenuDropoutRaceScoreScreen",
        "InGameMenuFreestyleScoreScreen",
    };

    private readonly Plugin _plugin;
    private readonly Settings _settings;
    private readonly Overlay _overlay;
    private readonly RoomWatch _room;
    private readonly CrTracker _cr;
    private readonly BoardTracker _board;
    private readonly DeltaTracker _delta;
    private readonly RaceTracker _race;
    private readonly List<HudPanel> _panels;
    private readonly LapReview _review;

    private GameObject? _root;
    private Canvas _canvas = null!;
    private CanvasGroup _group = null!;
    private RectTransform _toolbar = null!;
    private Text _sizeValue = null!;
    private Text _opacityValue = null!;
    private Action<int> _paintPanel = null!;
    private Action<int> _paintStay = null!;
    private readonly Dictionary<HudPanel, RectTransform> _options = new();
    private readonly Vector3[] _corners = new Vector3[4];

    private List<Type>? _menuTypes;
    private Type? _pauseType;
    private Behaviour? _pauseMenu;
    private readonly List<Behaviour> _menus = new();
    private readonly HashSet<string> _menusReported = new();
    private float _nextLook;
    private float _nextFind;
    private bool _inFlight;
    private bool _menuOpen;

    private bool _pinned;
    private bool _editing;
    private bool _toolbarDirty;
    private HudPanel _selected;
    private HudPanel? _dragging;
    private bool _cursorWasVisible;
    private CursorLockMode _cursorWasLocked;

    public RaceHud(Plugin plugin, Settings settings, Overlay overlay, LocalRun run, RoomWatch room, CrTracker cr, BoardTracker board,
        DeltaTracker delta, RaceTracker race)
    {
        _plugin = plugin;
        _settings = settings;
        _overlay = overlay;
        _room = room;
        _cr = cr;
        _board = board;
        _delta = delta;
        _race = race;
        _panels = new List<HudPanel>
        {
            new CrPanel(settings.Consistency, cr),
            new BoardPanel(settings, board, room),
            new DeltaPanel(settings, delta, () => SetReviewing(true)),
            new RacePanel(settings, race, board, room),
        };
        _selected = _panels[0];
        _review = new LapReview(plugin, settings, delta, () => SetReviewing(false));

        run.Spawned += () =>
        {
            foreach (var panel in _panels)
                panel.ShowAtStart();
        };
        foreach (var panel in _panels)
        {
            panel.Settings.Changed += () => _toolbarDirty = true;
            panel.OptionsChanged += () => _toolbarDirty = true;
        }
        SceneManager.sceneLoaded += (_, mode) =>
        {
            if (mode != LoadSceneMode.Single)
                return;
            _pauseMenu = null;
            _menus.Clear();
            _nextLook = _nextFind = 0;
        };
    }

    /// <summary>Called every frame by the plugin.</summary>
    public void Tick()
    {
        var now = Time.realtimeSinceStartup;
        if (now >= _nextLook)
            Look(now);
        _room.Tick(now);
        _cr.Tick(_inFlight);
        _board.Tick(_inFlight);
        _delta.Tick(_inFlight);
        _race.Tick(_inFlight);

        if (_editing && (!_inFlight || _overlay.Visible))
            SetEditing(false);
        else if (_inFlight && !_overlay.Visible && _settings.HudEditKey.Value.IsDown())
            SetEditing(!_editing);

        if (_review.IsOpen && (!_inFlight || _overlay.Visible))
            SetReviewing(false);
        else if (_inFlight && !_overlay.Visible && _settings.HudReviewKey.Value.IsDown())
            SetReviewing(!_review.IsOpen);
        else if (_review.IsOpen && Input.GetKeyDown(KeyCode.Escape))
            SetReviewing(false);

        if (!_editing && _inFlight && _settings.HudPinKey.Value.IsDown())
        {
            _pinned = !_pinned;
            foreach (var panel in _panels)
                panel.ShowAtStart();
        }

        if (_root == null && !_inFlight)
            return;
        Build();
        // The lap review has the screen to itself.
        var allowed = _inFlight && !_menuOpen && !_overlay.Visible && !_review.IsOpen;
        foreach (var panel in _panels)
            panel.Tick(now, allowed, _editing, _pinned, _dragging == panel, _selected == panel);

        if (_review.IsOpen)
        {
            KeepCursor();
            _review.Tick();
        }
        if (!_editing)
            return;
        KeepCursor();
        if (_toolbarDirty)
            RefreshToolbar();
        PlaceToolbar();
    }

    // ── Where the game is ───────────────────────────────────────────────────

    private void Look(float now)
    {
        _nextLook = now + 0.25f;
        if (SceneManager.GetActiveScene().name == MenuHooks.MainMenuScene)
        {
            _inFlight = _menuOpen = false;
            return;
        }

        // Searching the scene is slow, so the menus are found once per scene and then only
        // checked; until the pause menu turns up, once a second.
        if (_pauseMenu == null && now >= _nextFind)
        {
            _nextFind = now + 1f;
            FindMenus();
        }
        _inFlight = _pauseMenu != null;
        _menuOpen = false;
        if (!_inFlight)
            return;

        foreach (var menu in _menus)
        {
            if (menu == null || !menu.isActiveAndEnabled)
                continue;
            _menuOpen = true;
            if (_menusReported.Add(menu.GetType().Name))
                Plugin.Log.LogInfo($"HUD: hidden while {menu.GetType().Name} is open.");
            break;
        }
    }

    private void FindMenus()
    {
        if (_menuTypes == null)
        {
            _menuTypes = MenuPanels
                .Select(name => AccessTools.TypeByName(name))
                .Where(type => type != null && typeof(Behaviour).IsAssignableFrom(type))
                .ToList();
            _pauseType = _menuTypes.FirstOrDefault(type => type.Name == MenuPanels[0]);
            if (_pauseType == null)
                Plugin.Log.LogWarning("HUD: the pause menu wasn't found, so the HUD can't tell when you're flying and stays off. A game update may have renamed it.");
        }
        if (_pauseType == null)
            return;

        _pauseMenu = Loaded(_pauseType).FirstOrDefault();
        if (_pauseMenu == null)
            return;
        _menus.Clear();
        foreach (var type in _menuTypes)
            _menus.AddRange(Loaded(type));
    }

    /// <summary>The components of a type in a loaded scene, inactive ones included.</summary>
    private static IEnumerable<Behaviour> Loaded(Type type) =>
        Resources.FindObjectsOfTypeAll(type)
            .OfType<Behaviour>()
            .Where(found => found != null && found.gameObject.scene.IsValid());

    // ── Edit mode ───────────────────────────────────────────────────────────

    private void SetEditing(bool on)
    {
        if (on == _editing)
            return;
        Build();
        if (on)
        {
            SetReviewing(false);
            TakeMouse();
        }
        _editing = on;
        _dragging = null;
        _group.blocksRaycasts = on;
        _group.interactable = on;
        _toolbar.gameObject.SetActive(on);
        foreach (var panel in _panels)
            panel.MarkDirty();
        if (on)
        {
            RefreshToolbar();
            Plugin.Log.LogInfo("HUD: edit mode. Click a panel to edit it, drag it, scroll over it to resize it.");
        }
        else
        {
            GiveMouseBack();
            foreach (var panel in _panels)
                panel.ShowAtStart();
        }
    }

    /// <summary>The lap review opens over everything, edit mode included, and the panels come back when it closes.</summary>
    private void SetReviewing(bool on)
    {
        if (on == _review.IsOpen)
            return;
        if (on)
        {
            SetEditing(false);
            TakeMouse();
            _review.Open();
            Plugin.Log.LogInfo("HUD: lap review open.");
        }
        else
        {
            _review.Close();
            GiveMouseBack();
            foreach (var panel in _panels)
                panel.ShowAtStart();
        }
    }

    private bool TakesMouse => _editing || _review.IsOpen;

    /// <summary>Remembers the cursor as the game had it, before edit mode or the review first takes it.</summary>
    private void TakeMouse()
    {
        if (TakesMouse)
            return;
        _cursorWasVisible = Cursor.visible;
        _cursorWasLocked = Cursor.lockState;
    }

    /// <summary>
    /// Once neither wants the mouse: the pause menu wants its cursor; in the air, the game
    /// had it the way it was.
    /// </summary>
    private void GiveMouseBack()
    {
        if (TakesMouse || _menuOpen)
            return;
        Cursor.visible = _cursorWasVisible;
        Cursor.lockState = _cursorWasLocked;
    }

    /// <summary>The game hides the cursor while flying; edit mode needs it.</summary>
    private static void KeepCursor()
    {
        if (!Cursor.visible)
            Cursor.visible = true;
        if (Cursor.lockState != CursorLockMode.None)
            Cursor.lockState = CursorLockMode.None;
    }

    public void Select(HudPanel panel)
    {
        if (!_editing || _selected == panel)
            return;
        _selected = panel;
        _toolbarDirty = true;
    }

    public void Drag(HudPanel panel, PointerEventData drag)
    {
        if (!_editing)
            return;
        Select(panel);
        _dragging = panel;
        panel.Card.anchoredPosition += drag.delta / Mathf.Max(0.01f, _canvas.scaleFactor);
    }

    /// <summary>
    /// Kept as the nearest corner and the distance in from it, so it stays on screen at any
    /// resolution; or, dropped near the middle, centred on the top or bottom edge.
    /// </summary>
    public void Drop(HudPanel panel)
    {
        if (!_editing || _dragging != panel)
            return;
        _dragging = null;
        var root = (RectTransform)_root!.transform;
        var (min, max) = Bounds(panel.Card, root);
        var size = root.rect.size;
        var half = size / 2;
        var middle = (min.x + max.x) / 2;
        var centre = Mathf.Abs(middle) <= CentreSnap;
        var left = middle < 0;
        var bottom = (min.y + max.y) / 2 < 0;
        var offsetX = Mathf.Clamp(left ? min.x + half.x : half.x - max.x, 0, Mathf.Max(0, size.x - (max.x - min.x)));
        var offsetY = Mathf.Clamp(bottom ? min.y + half.y : half.y - max.y, 0, Mathf.Max(0, size.y - (max.y - min.y)));
        var settings = panel.Settings;
        settings.Corner.Value = centre
            ? bottom ? HudCorner.BottomCenter : HudCorner.TopCenter
            : left
                ? bottom ? HudCorner.BottomLeft : HudCorner.TopLeft
                : bottom ? HudCorner.BottomRight : HudCorner.TopRight;
        settings.OffsetX.Value = centre ? 0 : Mathf.Round(offsetX);
        settings.OffsetY.Value = Mathf.Round(offsetY);
        panel.MarkDirty();
    }

    public void Scroll(HudPanel panel, PointerEventData scroll)
    {
        if (!_editing || scroll.scrollDelta.y == 0)
            return;
        Select(panel);
        Nudge(panel.Settings.Scale, Math.Sign(scroll.scrollDelta.y) * ScaleStep);
    }

    private static void Nudge(ConfigEntry<float> entry, float by) =>
        entry.Value = Mathf.Round((entry.Value + by) * 100f) / 100f;

    private static int StayIndex(HudPanelSettings settings) => settings.Show.Value switch
    {
        HudMode.Off => 0,
        HudMode.Always => StayLabels.Length - 1,
        _ => 1 + Math.Max(0, Array.IndexOf(HudPanelSettings.StayOptions, settings.ShowSeconds.Value)),
    };

    private void PickStay(int index)
    {
        var settings = _selected.Settings;
        if (index == 0)
        {
            settings.Show.Value = HudMode.Off;
        }
        else if (index == StayLabels.Length - 1)
        {
            settings.Show.Value = HudMode.Always;
        }
        else
        {
            settings.ShowSeconds.Value = HudPanelSettings.StayOptions[index - 1];
            settings.Show.Value = HudMode.BetweenAttempts;
        }
    }

    private void RefreshToolbar()
    {
        _toolbarDirty = false;
        var settings = _selected.Settings;
        _paintPanel(_panels.IndexOf(_selected));
        _sizeValue.text = Percent(settings.Scale.Value);
        _opacityValue.text = Percent(settings.Opacity.Value);
        _paintStay(StayIndex(settings));
        foreach (var entry in _options)
            entry.Value.gameObject.SetActive(entry.Key == _selected && entry.Value.childCount > 0);
        _selected.RefreshOptions();
    }

    /// <summary>Beside the panel being edited, below it unless it's near the bottom of the screen.</summary>
    private void PlaceToolbar()
    {
        var root = (RectTransform)_root!.transform;
        var (min, max) = Bounds(_selected.Card, root);
        var half = root.rect.size / 2;
        var size = _toolbar.rect.size;
        var below = min.y - 8 - size.y > -half.y;
        _toolbar.anchorMin = _toolbar.anchorMax = new Vector2(0.5f, 0.5f);
        _toolbar.pivot = new Vector2(0.5f, below ? 1f : 0f);
        var x = Mathf.Clamp((min.x + max.x) / 2, -half.x + size.x / 2, Mathf.Max(-half.x + size.x / 2, half.x - size.x / 2));
        _toolbar.anchoredPosition = new Vector2(x, below ? min.y - 8 : max.y + 8);
    }

    /// <summary>A rect's corners in the canvas's own space, whose origin is the middle of the screen.</summary>
    private (Vector2 Min, Vector2 Max) Bounds(RectTransform rect, RectTransform root)
    {
        rect.GetWorldCorners(_corners);
        return (root.InverseTransformPoint(_corners[0]), root.InverseTransformPoint(_corners[2]));
    }

    // ── Building ────────────────────────────────────────────────────────────

    private void Build()
    {
        if (_root != null)
            return;

        _root = new GameObject("JmtHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        _root.transform.SetParent(_plugin.transform, false);
        _canvas = _root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Over the game's HUD, under the JMT window.
        _canvas.sortingOrder = short.MaxValue - 1;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        // It takes clicks only in edit mode.
        _group = _root.GetComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;

        foreach (var panel in _panels)
            panel.Build(_root.transform, this);
        BuildToolbar(_root.transform);
    }

    private void BuildToolbar(Transform root)
    {
        var bar = UiKit.Panel(root, Theme.Ink900, Theme.RadiusCard, "HudEditor");
        _toolbar = bar.rectTransform;
        UiKit.Stack(bar.gameObject, 8, new RectOffset(14, 14, 12, 12));
        var fit = bar.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        UiKit.Stripe(bar.transform, Theme.Accent, RectTransform.Edge.Top, 2, Theme.RadiusCard);

        UiKit.OneLine(UiKit.Label(bar.transform, "Click a panel to edit it · drag it to move it · scroll over it to resize it", 14, Theme.Ink400));

        var which = UiKit.Row(bar.transform, 8, name: "Panel");
        UiKit.OneLine(UiKit.Caption(which, "Editing"));
        _paintPanel = UiKit.Segmented(which, _panels.Select(panel => panel.Name).ToArray(), 0, i =>
        {
            _selected = _panels[i];
            _toolbarDirty = true;
        });

        var looks = UiKit.Row(bar.transform, 6, name: "Looks");
        UiKit.OneLine(UiKit.Caption(looks, "Size"));
        UiKit.Button(looks, "-", () => Nudge(_selected.Settings.Scale, -ScaleStep), UiKit.ButtonKind.Outline, 18, 30);
        _sizeValue = Value(looks);
        UiKit.Button(looks, "+", () => Nudge(_selected.Settings.Scale, ScaleStep), UiKit.ButtonKind.Outline, 18, 30);
        Gap(looks);
        UiKit.OneLine(UiKit.Caption(looks, "Opacity"));
        UiKit.Button(looks, "-", () => Nudge(_selected.Settings.Opacity, -OpacityStep), UiKit.ButtonKind.Outline, 18, 30);
        _opacityValue = Value(looks);
        UiKit.Button(looks, "+", () => Nudge(_selected.Settings.Opacity, OpacityStep), UiKit.ButtonKind.Outline, 18, 30);

        var stay = UiKit.Row(bar.transform, 8, name: "Stays");
        UiKit.OneLine(UiKit.Caption(stay, "Stays on screen"));
        _paintStay = UiKit.Segmented(stay, StayLabels, 2, PickStay);

        foreach (var panel in _panels)
        {
            var section = UiKit.Column(bar.transform, 8, name: $"{panel.Name} options");
            panel.BuildOptions(section);
            _options[panel] = section;
        }

        var actions = UiKit.Row(bar.transform, 8, name: "Actions");
        UiKit.Spacer(actions);
        UiKit.Button(actions, "Reset this panel", () => _selected.Reset(), UiKit.ButtonKind.Ghost, 15, 30);
        UiKit.Button(actions, "Done", () => SetEditing(false), UiKit.ButtonKind.Primary, 15, 30);

        bar.gameObject.SetActive(false);

        static Text Value(Transform parent)
        {
            var label = UiKit.OneLine(UiKit.Label(parent, "", 15, Theme.Ink200, UiKit.Heading, FontStyle.Bold, TextAnchor.MiddleCenter));
            UiKit.Size(label, 46, -1);
            return label;
        }

        static void Gap(Transform parent) => UiKit.Size(UiKit.Node("Gap", parent), 10, 1);
    }
}
