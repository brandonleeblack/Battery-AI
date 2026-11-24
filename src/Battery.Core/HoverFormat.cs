using System.Globalization;

namespace Battery.Core;

/// <summary>Text for the hover panel.</summary>
public static class HoverFormat
{
    /// <summary>"47m", "2h 14m", "3d 4h", "12d"; "now" when the reset is due or past.</summary>
    public static string FormatReset(TimeSpan untilReset)
    {
        if (untilReset <= TimeSpan.Zero) return "now";
        var inv = CultureInfo.InvariantCulture;
        var days = (int)untilReset.TotalDays;
        if (days >= 10) return string.Create(inv, $"{days}d");
        if (days >= 1) return string.Create(inv, $"{days}d {untilReset.Hours}h");
        if (untilReset.Hours >= 1) return string.Create(inv, $"{untilReset.Hours}h {untilReset.Minutes:00}m");
        return string.Create(inv, $"{Math.Max(1, untilReset.Minutes)}m");
    }

    /// <summary>"just now" under a minute, then "5m ago", "3h ago", "2d ago".</summary>
    public static string FormatAge(TimeSpan age)
    {
        var inv = CultureInfo.InvariantCulture;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return string.Create(inv, $"{(int)age.TotalMinutes}m ago");
        if (age < TimeSpan.FromDays(1)) return string.Create(inv, $"{(int)age.TotalHours}h ago");
        return string.Create(inv, $"{(int)age.TotalDays}d ago");
    }

    public static string Label(WindowKind kind) => kind switch
    {
        WindowKind.FiveHour => "5-hour",
        WindowKind.Weekly => "Week",
        WindowKind.Monthly => "Month",
        _ => kind.ToString(),
    };
}
