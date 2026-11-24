namespace Battery.Core;

/// <summary>
/// Every colour the strip and hover panel draw with, as #RRGGBB or #AARRGGBB where translucent. All
/// colours live here so another theme is one more static instance. The Windows accent colour is
/// deliberately not used. The look is frosted acrylic (docs/design/glass-samples.html, style A): the
/// strip is a translucent tint over the taskbar's own blur, and the hover panel a tint over a real
/// Windows acrylic backdrop (PanelSolidBackground where that backdrop is not available).
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
    /// <summary>Outline of the hover panel.</summary>
    public string PanelBorder { get; init; } = "#1FFFFFFF";

    /// <summary>One-pixel light line along the inside top edge of the strip, like light catching glass.</summary>
    public string BoxHighlight { get; init; } = "#14FFFFFF";

    /// <summary>Hover panel background when Windows cannot blur behind it (no acrylic backdrop).</summary>
    public string PanelSolidBackground { get; init; } = "#F22A2C36";

    /// <summary>Glass gel bars: the track pressed into the glass, and the shadow along its top inside edge.</summary>
    public string GrooveTrack { get; init; } = "#40000000";
    public string GrooveShade { get; init; } = "#80000000";

    public static Theme Graphite { get; } = new(
        Name: "Graphite",
        Box: "#A62A2C36",
        BoxBorder: "#1FFFFFFF",
        Track: "#26FFFFFF",
        Normal: "#D9DBE4",
        Ahead: "#FFC55C",
        Low: "#FF7676",
        ClockLine: "#FFFFFF",
        SecondaryText: "#C9CBD6",
        PanelBackground: "#A62A2C36",
        PanelText: "#F1F1F4");

    public static Theme Daylight { get; } = new(
        Name: "Daylight",
        Box: "#B3FFFFFF",
        BoxBorder: "#1A000000",
        Track: "#1A000000",
        Normal: "#4D5262",
        Ahead: "#C77700",
        Low: "#D13438",
        ClockLine: "#111318",
        SecondaryText: "#4A4D57",
        PanelBackground: "#B3FFFFFF",
        PanelText: "#17181C")
    {
        PanelBorder = "#1A000000",
        BoxHighlight = "#CCFFFFFF",
        PanelSolidBackground = "#F7FFFFFF",
        GrooveTrack = "#12000000",
        GrooveShade = "#33000000",
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
