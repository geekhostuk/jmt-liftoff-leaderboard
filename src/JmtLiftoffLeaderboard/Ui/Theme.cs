using System;
using System.Globalization;
using UnityEngine;

namespace JmtLiftoffLeaderboard.Ui;

/// <summary>
/// The JMT site's look, as numbers. Colours are the tokens in the site's
/// packages/ui/src/tokens.css; the tier tones are web/src/lib/tiers.ts; the time
/// formats are web/src/lib/format.ts. Change them there first, then here.
/// </summary>
internal static class Theme
{
    // Surfaces, darkest first.
    public static readonly Color Ink950 = Hex(0x0b0f14);
    public static readonly Color Ink900 = Hex(0x0f1620);
    public static readonly Color Ink850 = Hex(0x121923);
    public static readonly Color Ink800 = Hex(0x1a2430);
    public static readonly Color Ink750 = Hex(0x1e2a38);
    public static readonly Color Ink700 = Hex(0x263241);

    // Text.
    public static readonly Color Ink600 = Hex(0x6b7a8d);
    public static readonly Color Ink400 = Hex(0xb6c2cf);
    public static readonly Color Ink200 = Hex(0xf8fafc);

    public static readonly Color Accent = Hex(0xff7a00);
    public static readonly Color AccentSoft = Hex(0xff9a3d);
    public static readonly Color Cyan = Hex(0x00d1ff);
    public static readonly Color Signal = Hex(0x22c55e);
    public static readonly Color Alarm = Hex(0xef4444);
    public static readonly Color Gold = Hex(0xf59e0b);
    public static readonly Color Silver = Hex(0x94a3b8);
    public static readonly Color Bronze = Hex(0xb45309);

    // Tailwind shades the tier pills borrow.
    private static readonly Color Amber600 = Hex(0xd97706);
    private static readonly Color Teal300 = Hex(0x5eead4);
    private static readonly Color Teal400 = Hex(0x2dd4bf);
    private static readonly Color Lime300 = Hex(0xbef264);
    private static readonly Color Lime400 = Hex(0xa3e635);
    private static readonly Color Red400 = Hex(0xf87171);
    private static readonly Color Violet300 = Hex(0xc4b5fd);
    private static readonly Color Violet400 = Hex(0xa78bfa);

    /// <summary>A pilot's best ever sector, in motorsport's colour for it. After Violet400, which it is.</summary>
    public static readonly Color Purple = Violet400;
    /// <summary>A sector slower than the lap it's measured against: motorsport's yellow.</summary>
    public static readonly Color Slower = Gold;

    // Corner radii, in reference pixels: cards, buttons and inputs, badges.
    public const int RadiusCard = 12;
    public const int RadiusControl = 8;
    public const int RadiusBadge = 4;

    public static Color Hex(uint rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, alpha);

    public static Color Alpha(Color color, float alpha) => new(color.r, color.g, color.b, alpha);

    /// <summary>For Unity rich text: <c>&lt;color=#rrggbb&gt;</c>.</summary>
    public static string Html(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

    /// <summary>P1, P2, P3 in their metals; everyone else muted.</summary>
    public static Color Place(int position) => position switch
    {
        1 => Gold,
        2 => Silver,
        3 => Bronze,
        _ => Ink600,
    };

    /// <summary>A placing on a board of <paramref name="field"/> pilots, as the profile colours it.</summary>
    public static Color Placing(int position, int field)
    {
        if (position <= 3)
            return Place(position);
        var topPct = TopPct(position, field);
        return topPct <= 10 ? AccentSoft : topPct <= 25 ? Ink400 : Ink600;
    }

    public static double TopPct(int position, int field) => position / (double)Math.Max(1, field) * 100;

    public static (Color Fill, Color Text) Division(string? name) => name switch
    {
        "Bronze" => (Alpha(Bronze, 0.2f), Amber600),
        "Silver" => (Alpha(Silver, 0.15f), Silver),
        "Gold" => (Alpha(Gold, 0.15f), Gold),
        "Platinum" => (Alpha(Teal400, 0.15f), Teal300),
        "Diamond" => (Alpha(Cyan, 0.15f), Cyan),
        "Alien" => (Alpha(Lime400, 0.15f), Lime300),
        _ => (Ink750, Ink400),
    };

    public static (Color Fill, Color Text) License(string? name) => name switch
    {
        "Rookie" => (Alpha(Alarm, 0.15f), Red400),
        "D" => (Alpha(Accent, 0.15f), AccentSoft),
        "C" => (Alpha(Gold, 0.15f), Gold),
        "B" => (Alpha(Signal, 0.15f), Signal),
        "A" => (Alpha(Cyan, 0.15f), Cyan),
        "Pro" => (Alpha(Violet400, 0.15f), Violet300),
        _ => (Ink750, Ink400),
    };

    /// <summary>"A license", "Rookie": the letters read as a grade, Rookie as a name.</summary>
    public static string LicenseLabel(string name) => name == "Rookie" ? "Rookie" : $"{name} license";

    /// <summary>Lap times read as m:ss.mmm in the room, so they read that way here too.</summary>
    public static string LapTime(int? ms)
    {
        if (ms == null)
            return "—";
        var minutes = ms.Value / 60_000;
        var seconds = (ms.Value % 60_000 / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
        return minutes == 0 ? $"{seconds}s" : $"{minutes}:{seconds.PadLeft(6, '0')}";
    }

    /// <summary>"+1.234": a gap reads as a signed offset, never as a lap time.</summary>
    public static string Gap(int? ms) =>
        ms == null ? "" : "+" + (ms.Value / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);

    public static string Seconds(int ms, string format = "0.000") =>
        (ms / 1000.0).ToString(format, CultureInfo.InvariantCulture);

    /// <summary>"9s ago", "4m ago", "2h ago", then whole days and beyond.</summary>
    public static string SinceNow(DateTimeOffset? at)
    {
        if (at == null)
            return "never";
        var seconds = Math.Max(0, (int)Math.Round((DateTimeOffset.UtcNow - at.Value).TotalSeconds));
        if (seconds < 60)
            return $"{seconds}s ago";
        if (seconds < 3600)
            return $"{seconds / 60}m ago";
        if (seconds < 86_400)
            return $"{seconds / 3600}h ago";
        return RelativeDate(at);
    }

    public static string RelativeDate(DateTimeOffset? at)
    {
        if (at == null)
            return "unknown";
        var days = (int)Math.Round((DateTimeOffset.UtcNow - at.Value).TotalDays);
        if (days < 1)
            return "today";
        if (days < 30)
            return $"{days}d ago";
        if (days < 365)
            return $"{(int)Math.Round(days / 30.0)}mo ago";
        return $"{(int)Math.Round(days / 365.0)}y ago";
    }

    /// <summary>"Mar 2024".</summary>
    public static string Month(DateTimeOffset at) =>
        at.ToLocalTime().ToString("MMM yyyy", CultureInfo.GetCultureInfo("en-GB"));

    /// <summary>"12 Mar".</summary>
    public static string Day(DateTimeOffset at) =>
        at.ToLocalTime().ToString("d MMM", CultureInfo.GetCultureInfo("en-GB"));

    public static string Count(int value) => value.ToString("N0", CultureInfo.GetCultureInfo("en-GB"));

    /// <summary>Two letters for an avatar that has no picture.</summary>
    public static string Initials(string name)
    {
        var parts = name.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
        var letters = name.Trim();
        return letters.Length == 0 ? "?" : letters.Substring(0, Math.Min(2, letters.Length)).ToUpperInvariant();
    }
}
