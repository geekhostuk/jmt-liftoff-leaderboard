using System;
using System.Collections;
using System.Diagnostics;
using System.Text.RegularExpressions;
using UnityEngine;

namespace JmtLiftoffLeaderboard.Game;

/// <summary>
/// Opens a page in the pilot's own browser.
///
/// On Linux the game runs with the mod loader preloaded into it: <c>LD_PRELOAD</c> names
/// Doorstop's library, <c>LD_LIBRARY_PATH</c> points at its runtime, and the
/// <c>DOORSTOP_</c> variables tell it what to load. Everything the game starts inherits
/// them, the browser included, and Unity's own <c>OpenURL</c> opened nothing. So on Linux
/// <c>xdg-open</c> is started directly, with those cleared first.
/// </summary>
internal static class Browser
{
    /// <summary>Only an ordinary web address is ever handed to another program.</summary>
    private static readonly Regex WebAddress = new(@"^https?://[^\s""'`$\\]+$", RegexOptions.CultureInvariant);

    public static bool IsWebAddress(string url) => WebAddress.IsMatch(url ?? "");

    public static void Open(string url)
    {
        if (!IsWebAddress(url))
            return;
        if (Application.platform == RuntimePlatform.LinuxPlayer && XdgOpen(url))
            return;
        Application.OpenURL(url);
    }

    private static bool XdgOpen(string url)
    {
        try
        {
            var start = new ProcessStartInfo("xdg-open", $"\"{url}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var inherited = new ArrayList(start.EnvironmentVariables.Keys);
            foreach (string name in inherited)
            {
                if (name.Equals("LD_PRELOAD", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("LD_LIBRARY_PATH", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("DOORSTOP_", StringComparison.OrdinalIgnoreCase))
                    start.EnvironmentVariables.Remove(name);
            }
            using var process = Process.Start(start);
            return process != null;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Couldn't open your browser with xdg-open, so Unity's way is tried: {ex.Message}");
            return false;
        }
    }

    /// <summary>Puts text on the clipboard, for a page the pilot has to open themselves.</summary>
    public static void Copy(string text)
    {
        try
        {
            GUIUtility.systemCopyBuffer = text;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Couldn't copy to the clipboard: {ex.Message}");
        }
    }
}
