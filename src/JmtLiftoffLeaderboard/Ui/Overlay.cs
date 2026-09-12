using System;
using System.Collections;
using System.IO;
using BepInEx;
using JmtLiftoffLeaderboard.Game;
using JmtLiftoffLeaderboard.Site;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JmtLiftoffLeaderboard.Ui;

/// <summary>
/// The JMT window over the game's menus: a header in the site's style with the two
/// tabs, and the board and profile screens beneath it.
///
/// It is its own canvas, drawn above everything the game draws, with an opaque
/// backdrop that takes every click, so nothing in the menu behind shows through or
/// reacts while it is open. It is built the first time it opens and then only shown
/// and hidden.
/// </summary>
internal sealed class Overlay
{
    private const float HeaderHeight = 64;

    private readonly Plugin _plugin;
    private readonly SiteClient _site;
    private readonly LocalPilot _me;

    private GameObject? _root;
    private RectTransform _boardPage = null!;
    private RectTransform _profilePage = null!;
    private LeaderboardScreen _board = null!;
    private ProfileScreen _profile = null!;
    private Text _boardTab = null!;
    private Text _profileTab = null!;
    private Image _boardUnderline = null!;
    private Image _profileUnderline = null!;
    private Action? _openNative;
    private bool _touring;

    public Overlay(Plugin plugin, SiteClient site, LocalPilot me)
    {
        _plugin = plugin;
        _site = site;
        _me = me;
    }

    public bool Visible => _root != null && _root.activeSelf;

    /// <summary>From the main menu: the course list, opened on the most recently flown board.</summary>
    public void ShowBoards(Action openNative)
    {
        Open(openNative);
        SelectBoards();
        _board.Open(null, null);
    }

    /// <summary>From the pause menu: straight to the board for the course being flown.</summary>
    public void ShowCurrentTrackBoard(Action openNative)
    {
        Open(openNative);
        SelectBoards();
        _board.Open(CurrentTrack.Read(), null);
    }

    public void ShowMyProfile(Action openNative)
    {
        Open(openNative);
        SelectProfile();
        _profile.ShowMe();
    }

    public void ShowPilot(string publicId)
    {
        SelectProfile();
        _profile.Show(publicId);
    }

    public void ShowBoard(long publishedFileId)
    {
        SelectBoards();
        _board.Open(null, publishedFileId == 0 ? null : publishedFileId);
    }

    /// <summary>Hand over to Liftoff's own leaderboard, the way the menu the pilot came from would have.</summary>
    public void OpenNative()
    {
        var open = _openNative;
        Hide();
        open?.Invoke();
    }

    public void Hide()
    {
        if (_root != null)
            _root.SetActive(false);
    }

    /// <summary>Called every frame by the plugin.</summary>
    public void Tick()
    {
        if (_touring)
            return;
        if (Visible && Input.GetKeyDown(KeyCode.Escape))
            Hide();

        var control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (control && shift && Input.GetKeyDown(KeyCode.F9))
            _plugin.StartCoroutine(Tour());
    }

    private void Open(Action openNative)
    {
        Build();
        _openNative = openNative;
        _root!.SetActive(true);
        // The game's menu keeps keyboard and pad focus otherwise, and would act on it behind us.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void SelectBoards()
    {
        Build();
        _boardPage.gameObject.SetActive(true);
        _profilePage.gameObject.SetActive(false);
        PaintTabs(boards: true);
    }

    private void SelectProfile()
    {
        Build();
        _boardPage.gameObject.SetActive(false);
        _profilePage.gameObject.SetActive(true);
        PaintTabs(boards: false);
    }

    private void PaintTabs(bool boards)
    {
        _boardTab.color = boards ? Theme.Ink200 : Theme.Ink600;
        _profileTab.color = boards ? Theme.Ink600 : Theme.Ink200;
        _boardUnderline.enabled = boards;
        _profileUnderline.enabled = !boards;
    }

    private void Build()
    {
        if (_root != null)
            return;

        _root = new GameObject("JmtLeaderboard", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _root.transform.SetParent(_plugin.transform, false);
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // The highest order Unity allows: the game draws version text and corner
        // widgets on canvases of its own, and none of it should show over this.
        canvas.sortingOrder = short.MaxValue;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        var root = (RectTransform)_root.transform;

        var backdrop = UiKit.Panel(root, Theme.Ink950, 0, "Backdrop");
        UiKit.Fill(backdrop.rectTransform);

        var window = UiKit.Column(root, 0, name: "Window");
        UiKit.Fill(window, 36, 24, 36, 24);

        BuildHeader(window);

        var body = UiKit.Node("Body", window);
        UiKit.Size(body, -1, -1, 1, 1);
        _boardPage = UiKit.Fill(UiKit.Node("Board", body), 0, 18, 0, 0);
        _profilePage = UiKit.Fill(UiKit.Node("Profile", body), 0, 18, 0, 0);

        _board = new LeaderboardScreen(_boardPage, _site, _me, this);
        _profile = new ProfileScreen(_profilePage, _site, _me, this);
        _root.SetActive(false);
    }

    private void BuildHeader(Transform window)
    {
        var header = UiKit.Panel(window, Theme.Ink900, Theme.RadiusCard, "Header");
        UiKit.Size(header, -1, HeaderHeight);
        UiKit.Stripe(header.transform, Theme.Accent, RectTransform.Edge.Bottom, 2);
        UiKit.Line(header.gameObject, 12, new RectOffset(20, 16, 0, 0));

        // The site's mark: an orange "JMT" square, then "FPV".
        var mark = UiKit.Panel(header.transform, Theme.Accent, Theme.RadiusBadge, "Mark");
        UiKit.Size(mark, 44, 34);
        var jmt = UiKit.Label(mark.transform, "JMT", 17, Theme.Ink950, UiKit.Heading, FontStyle.Bold, TextAnchor.MiddleCenter);
        UiKit.Fill(jmt.rectTransform);
        UiKit.OneLine(UiKit.Label(header.transform, "FPV", 24, Theme.Ink200, UiKit.Heading, FontStyle.Bold));

        var gap = UiKit.Node("Gap", header.transform);
        UiKit.Size(gap, 20, 1);

        (_boardTab, _boardUnderline) = Tab(header.transform, "Leaderboard", () =>
        {
            SelectBoards();
            _board.Open(null, null);
        });
        (_profileTab, _profileUnderline) = Tab(header.transform, "My profile", () =>
        {
            SelectProfile();
            _profile.ShowMe();
        });

        UiKit.Spacer(header.transform);
        UiKit.Button(header.transform, "Liftoff leaderboard", OpenNative, UiKit.ButtonKind.Outline, 15, 36);
        UiKit.Button(header.transform, "Close", Hide, UiKit.ButtonKind.Ghost, 15, 36);
    }

    private static (Text Label, Image Underline) Tab(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
    {
        var tab = UiKit.Panel(parent, new Color(0, 0, 0, 0), 0, text);
        UiKit.Line(tab.gameObject, 0, new RectOffset(14, 14, 0, 0), TextAnchor.MiddleCenter);
        var label = UiKit.OneLine(UiKit.Label(tab.transform, text.ToUpperInvariant(), 17, Theme.Ink600, UiKit.Heading, FontStyle.Bold));
        UiKit.Size(tab, label.preferredWidth + 28, HeaderHeight - 2);
        var underline = UiKit.Stripe(tab.transform, Theme.Accent, RectTransform.Edge.Bottom, 3);
        UiKit.Clickable(tab, onClick);
        return (label, underline);
    }

    // ── Layout tour ─────────────────────────────────────────────────────────

    /// <summary>
    /// Ctrl+Shift+F9: opens the board and then the pilot's profile, scrolls each to the
    /// top, middle and bottom, and saves a screenshot of every step to
    /// <c>BepInEx/JmtLeaderboardScreens/</c>. For checking the layout without clicking
    /// through it by hand.
    /// </summary>
    private IEnumerator Tour()
    {
        _touring = true;
        var folder = Path.Combine(Paths.BepInExRootPath, "JmtLeaderboardScreens");
        Directory.CreateDirectory(folder);
        Plugin.Log.LogInfo($"Layout tour: saving screenshots to {folder}");

        Open(_openNative ?? (() => { }));
        SelectBoards();
        _board.Open(null, null);
        yield return new WaitForSecondsRealtime(5f);
        yield return Shots(folder, "board", _boardPage);

        SelectProfile();
        _profile.ShowMe();
        yield return new WaitForSecondsRealtime(5f);
        yield return Shots(folder, "profile", _profilePage);

        Hide();
        _touring = false;
        Plugin.Log.LogInfo("Layout tour finished.");
    }

    private static IEnumerator Shots(string folder, string name, RectTransform page)
    {
        var scrolls = page.GetComponentsInChildren<ScrollRect>();
        var stops = new[] { 1f, 0.5f, 0f };
        for (var i = 0; i < stops.Length; i++)
        {
            foreach (var scroll in scrolls)
                scroll.verticalNormalizedPosition = stops[i];
            yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForEndOfFrame();

            var shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            shot.Apply();
            try
            {
                File.WriteAllBytes(Path.Combine(folder, $"{name}-{i + 1}.png"), shot.EncodeToPNG());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Layout tour could not save a screenshot: {ex.Message}");
            }
            UnityEngine.Object.Destroy(shot);
        }
    }
}
