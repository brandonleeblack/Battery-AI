namespace Battery.Core;

/// <summary>
/// Every colour the strip and hover panel draw with, as #RRGGBB. All colours live here so another
/// theme is one more static instance. The Windows accent colour is deliberately not used.
/// </summary>
public record Theme(
    string Name,
    string Box,
    string? BoxBorder,
    string Track,
    string Normal,
    string Ahead,
    string Low,
    string ClockLine,
    string SecondaryText,
    string PanelBackground,
    string PanelText)
{
    /// <summary>Outline of the hover panel. Not in the constructor so the plan's signature stays as written.</summary>
    public string PanelBorder { get; init; } = "#34363F";

    public static Theme Graphite { get; } = new(
        Name: "Graphite",
        Box: "#2D2F37",
        BoxBorder: null,
        Track: "#4A4C58",
        Normal: "#C4C7D2",
        Ahead: "#F2B84B",
        Low: "#FF6B6B",
        ClockLine: "#FFFFFF",
        SecondaryText: "#A6A8B3",
        PanelBackground: "#26272E",
        PanelText: "#EDEDF0");

    public static Theme Daylight { get; } = new(
        Name: "Daylight",
        Box: "#FFFFFF",
        BoxBorder: "#CFD2DA",
        Track: "#D5D8E0",
        Normal: "#5A5F6E",
        Ahead: "#C77700",
        Low: "#D13438",
        ClockLine: "#111318",
        SecondaryText: "#454852",
        PanelBackground: "#FFFFFF",
        PanelText: "#111318")
    {
        PanelBorder = "#CFD2DA",
    };

    /// <summary>Graphite when the Windows (taskbar) mode is dark, Daylight when it is light.</summary>
    public static Theme For(bool systemUsesLightTheme) => systemUsesLightTheme ? Daylight : Graphite;

    /// <summary>App identity dot; the same in every theme.</summary>
    public static string DotFor(string providerId) => providerId switch
    {
        ProviderIds.Claude => "#E08A5B",
        ProviderIds.Codex => "#7FB0FF",
        ProviderIds.OpenCode => "#5CC8B0",
        _ => "#888888",
    };
}

public static class ThemeExtensions
{
    public static string FillFor(this Theme t, BarState s) => s switch
    {
        BarState.Low => t.Low,
        BarState.Ahead => t.Ahead,
        _ => t.Normal,
    };
}
