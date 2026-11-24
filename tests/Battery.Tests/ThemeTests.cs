using Battery.Core;

namespace Battery.Tests;

public class ThemeTests
{
    [Fact]
    public void Dark_system_is_graphite() => Assert.Same(Theme.Graphite, Theme.For(false));

    [Fact]
    public void Light_system_is_daylight() => Assert.Same(Theme.Daylight, Theme.For(true));

    // Frosted acrylic (docs/design/glass-samples.html, style A). #AARRGGBB where translucent.

    [Fact]
    public void Graphite_values()
    {
        var t = Theme.Graphite;
        Assert.Equal(("#A62A2C36", "#1FFFFFFF", "#26FFFFFF", "#D9DBE4", "#FFC55C", "#FF7676", "#FFFFFF", "#C9CBD6"),
            (t.Box, t.BoxBorder, t.Track, t.Normal, t.Ahead, t.Low, t.ClockLine, t.SecondaryText));
        Assert.Equal(("#A62A2C36", "#F1F1F4"), (t.PanelBackground, t.PanelText));
        Assert.Equal(("#14FFFFFF", "#F22A2C36"), (t.BoxHighlight, t.PanelSolidBackground));
    }

    [Fact]
    public void Daylight_values()
    {
        var t = Theme.Daylight;
        Assert.Equal(("#B3FFFFFF", "#1A000000", "#1A000000", "#4D5262", "#C77700", "#D13438", "#111318", "#4A4D57"),
            (t.Box, t.BoxBorder, t.Track, t.Normal, t.Ahead, t.Low, t.ClockLine, t.SecondaryText));
        Assert.Equal(("#B3FFFFFF", "#17181C"), (t.PanelBackground, t.PanelText));
        Assert.Equal(("#CCFFFFFF", "#F7FFFFFF"), (t.BoxHighlight, t.PanelSolidBackground));
    }

    [Fact]
    public void Gel_groove_colours()
    {
        Assert.Equal(("#40000000", "#80000000"), (Theme.Graphite.GrooveTrack, Theme.Graphite.GrooveShade));
        Assert.Equal(("#12000000", "#33000000"), (Theme.Daylight.GrooveTrack, Theme.Daylight.GrooveShade));
    }

    [Fact]
    public void Fill_follows_state()
    {
        Assert.Equal("#D13438", Theme.Daylight.FillFor(BarState.Low));
        Assert.Equal("#C77700", Theme.Daylight.FillFor(BarState.Ahead));
        Assert.Equal("#4D5262", Theme.Daylight.FillFor(BarState.Normal));
        Assert.Equal("#FFC55C", Theme.Graphite.FillFor(BarState.Ahead));
    }

    [Fact]
    public void Panel_border_in_each_theme()
    {
        Assert.Equal("#1FFFFFFF", Theme.Graphite.PanelBorder);
        Assert.Equal("#1A000000", Theme.Daylight.PanelBorder);
    }

    [Fact]
    public void Identity_dots_are_the_same_in_both_themes()
    {
        Assert.Equal("#E08A5B", Theme.DotFor(ProviderIds.Claude));
        Assert.Equal("#7FB0FF", Theme.DotFor(ProviderIds.Codex));
        Assert.Equal("#5CC8B0", Theme.DotFor(ProviderIds.OpenCode));
    }
}
