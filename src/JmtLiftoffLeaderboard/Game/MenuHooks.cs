using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using JmtLiftoffLeaderboard.Ui;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// Where the game's menus hand over to the JMT screens.
///
/// Liftoff's Assembly-CSharp is obfuscated, so nothing here names a renamed type:
/// <list type="bullet">
/// <item>The main menu's Leaderboard button (in the Multiplayer sub-menu) is found by
/// its GameObject name, <c>btnLeaderboards</c>. It opens Liftoff's leaderboard through
/// a small level-loader component beside it, found by its serialized field
/// <c>levelToLoad</c>, which adds its own listener to the button's <c>onClick</c> in
/// <c>Start</c>. The sub-menu is inactive when the main menu loads, so that
/// <c>Start</c> runs later, when the pilot first opens Multiplayer. The loader is
/// therefore switched off (a disabled behaviour never gets its <c>Start</c>), the
/// button gets a fresh <c>onClick</c> of our own, and "Liftoff leaderboard" calls the
/// loader's load method directly, so the game's scene opens exactly as before.</item>
/// <item>The pause menu's <c>InGameMenuMainPanel</c> kept its name and so did
/// <c>OnShowLeaderboardSelection</c>, the method its leaderboard button calls. A prefix
/// opens the JMT board instead, unless the pilot asked for Liftoff's own.</item>
/// </list>
/// The game's leaderboard scene itself is never touched, which is what keeps
/// LiftoffReplayPlus's hooks on it working.
/// </summary>
internal sealed class MenuHooks
{
    internal const string MainMenuScene = "MainMenu";
    private const string LeaderboardButton = "btnLeaderboards";
    private const string ProfileButton = "btnJmtProfile";
    private const string LevelLoaderField = "levelToLoad";

    private static MenuHooks? _instance;
    private static bool _letNativeThrough;

    private readonly Plugin _plugin;
    private readonly Settings _settings;
    private readonly Overlay _overlay;
    private Action? _openNativeFromMainMenu;
    private UnityEngine.Object? _pausePanel;

    public MenuHooks(Plugin plugin, Settings settings, Overlay overlay)
    {
        _plugin = plugin;
        _settings = settings;
        _overlay = overlay;
        _instance = this;
    }

    public void Install(Harmony harmony)
    {
        var panel = AccessTools.TypeByName("InGameMenuMainPanel");
        var method = panel == null ? null : AccessTools.Method(panel, "OnShowLeaderboardSelection");
        if (method == null)
        {
            Plugin.Log.LogWarning("Pause menu leaderboard not found; it keeps opening Liftoff's own. A game update may have moved it.");
        }
        else
        {
            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(MenuHooks), nameof(PauseLeaderboardPrefix)));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not hook the pause menu leaderboard: {ex.Message}");
            }
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
        // The plugin can load after the main menu already has.
        if (SceneManager.GetActiveScene().name == MainMenuScene)
            _plugin.StartCoroutine(HookMainMenu());
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
            _overlay.Hide();
        if (scene.name == MainMenuScene)
            _plugin.StartCoroutine(HookMainMenu());
    }

    private IEnumerator HookMainMenu()
    {
        // The menu builds its panels over the first frames after the scene loads.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var button = FindSceneButton(LeaderboardButton);
            if (button != null)
            {
                Hook(button);
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.1f);
        }
        Plugin.Log.LogWarning($"Main menu '{LeaderboardButton}' not found; it keeps opening Liftoff's own leaderboard. A game update may have renamed it.");
    }

    /// <summary>A button in a loaded scene by GameObject name, inactive menu panels included.</summary>
    private static Button? FindSceneButton(string name)
    {
        foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (button != null && button.name == name && button.gameObject.scene.IsValid())
                return button;
        }
        return null;
    }

    private void Hook(Button leaderboard)
    {
        UiKit.AdoptGameFont(leaderboard.GetComponentInChildren<Text>(true)?.font);

        if (_settings.ReplaceMainMenuLeaderboard.Value && leaderboard.GetComponent<HookedByJmt>() == null)
        {
            var original = leaderboard.onClick;
            var loader = LevelLoaders(leaderboard.gameObject).FirstOrDefault();
            if (loader != null)
            {
                // Disabled before its Start runs, it never adds its listener; if it
                // already has, that listener is on the event replaced below.
                loader.enabled = false;
                var load = LoadMethod(loader.GetType());
                var level = AccessTools.Field(loader.GetType(), LevelLoaderField)?.GetValue(loader);
                Plugin.Log.LogInfo($"Leaderboard button loads '{level}' through {(load == null ? "an unrecognised loader" : "its level loader")}.");
                _openNativeFromMainMenu = () =>
                {
                    if (load != null)
                        load.Invoke(loader, null);
                    else
                        original.Invoke();
                };
            }
            else
            {
                Plugin.Log.LogWarning("Leaderboard button has no level loader beside it; \"Liftoff leaderboard\" replays its original click.");
                _openNativeFromMainMenu = original.Invoke;
            }

            var replacement = new Button.ButtonClickedEvent();
            replacement.AddListener(() => _overlay.ShowBoards(OpenMainMenuNative));
            leaderboard.onClick = replacement;
            leaderboard.gameObject.AddComponent<HookedByJmt>();
            Plugin.Log.LogInfo("Main menu Leaderboard now opens the JMT board.");
        }

        if (_settings.ShowProfileButton.Value)
            AddProfileButton(leaderboard);
    }

    private void OpenMainMenuNative()
    {
        _overlay.Hide();
        try
        {
            _openNativeFromMainMenu?.Invoke();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Could not open Liftoff's own leaderboard: {ex}");
        }
    }

    /// <summary>The components on a button that load a level when it is clicked.</summary>
    private static MonoBehaviour[] LevelLoaders(GameObject button) =>
        button.GetComponents<MonoBehaviour>()
            .Where(c => c != null && AccessTools.Field(c.GetType(), LevelLoaderField)?.FieldType == typeof(string))
            .ToArray();

    /// <summary>
    /// The loader's load method. Its name is obfuscated, but its shape is not: the only
    /// parameterless void method it declares, apart from Unity's <c>Start</c> and the
    /// compiler-generated body of the listener that calls it.
    /// </summary>
    private static MethodInfo? LoadMethod(Type loaderType)
    {
        var candidates = loaderType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(void)
                        && m.GetParameters().Length == 0
                        && m.Name != "Start"
                        && !m.IsSpecialName
                        && !m.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .ToArray();
        if (candidates.Length == 1)
            return candidates[0];
        Plugin.Log.LogWarning($"Level loader has {candidates.Length} candidate load methods; expected one. A game update may have changed it.");
        return null;
    }

    /// <summary>A copy of the Leaderboard button, so it looks like it was always there.</summary>
    private void AddProfileButton(Button leaderboard)
    {
        var parent = leaderboard.transform.parent;
        if (parent == null || parent.Find(ProfileButton) != null)
            return;

        var copy = UnityEngine.Object.Instantiate(leaderboard.gameObject, parent);
        copy.name = ProfileButton;
        copy.transform.SetSiblingIndex(leaderboard.transform.GetSiblingIndex() + 1);

        // The copy brings the level loader with it, which would open Liftoff's
        // leaderboard from JMT Profile too; and localisation, which would put
        // "Leaderboards" back on the label on the next language refresh.
        foreach (var loader in LevelLoaders(copy))
            UnityEngine.Object.DestroyImmediate(loader);
        foreach (var component in copy.GetComponentsInChildren<Component>(true))
        {
            if (component != null && component.GetType().FullName is "I2.Loc.Localize" or "I2.Loc.LocalizationParamsManager")
                UnityEngine.Object.DestroyImmediate(component);
        }

        foreach (var label in copy.GetComponentsInChildren<Text>(true))
            label.text = "JMT Profile";
        foreach (var component in copy.GetComponentsInChildren<Component>(true))
        {
            // TextMeshPro, if the game ever moves this button over to it.
            if (component != null && component.GetType().FullName is "TMPro.TextMeshProUGUI" or "TMPro.TextMeshPro")
                AccessTools.Property(component.GetType(), "text")?.SetValue(component, "JMT Profile");
        }

        var button = copy.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => _overlay.ShowMyProfile(OpenMainMenuNative));
        Plugin.Log.LogInfo("JMT Profile added to the main menu.");
    }

    private static bool PauseLeaderboardPrefix(object __instance)
    {
        var hooks = _instance;
        if (_letNativeThrough || hooks == null || !hooks._settings.ReplacePauseMenuLeaderboard.Value)
            return true;

        try
        {
            hooks._pausePanel = __instance as UnityEngine.Object;
            hooks._overlay.ShowCurrentTrackBoard(hooks.OpenPauseNative);
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"JMT board failed to open from the pause menu; showing Liftoff's own: {ex}");
            return true;
        }
    }

    private void OpenPauseNative()
    {
        _overlay.Hide();
        if (_pausePanel == null)
            return;
        _letNativeThrough = true;
        try
        {
            AccessTools.Method(_pausePanel.GetType(), "OnShowLeaderboardSelection")?.Invoke(_pausePanel, null);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Could not open Liftoff's own leaderboard: {ex}");
        }
        finally
        {
            _letNativeThrough = false;
        }
    }
}

/// <summary>Marks a game button this plugin has already rewired, so a second pass leaves it be.</summary>
internal sealed class HookedByJmt : MonoBehaviour
{
}
