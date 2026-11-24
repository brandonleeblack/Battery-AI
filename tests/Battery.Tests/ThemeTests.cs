using Battery.Core;

namespace Battery.Tests;

public class ThemeTests
{
    [Fact]
    public void Dark_system_is_graphite() => Assert.Same(Theme.Graphite, Theme.For(false));

    [Fact]
    public void Light_system_is_daylight() => Assert.Same(Theme.Daylight, Theme.For(true));

    [Fact]
    public void Graphite_values()
    {
        var t = Theme.Graphite;
        Assert.Equal(("#2D2F37", null, "#4A4C58", "#C4C7D2", "#F2B84B", "#FF6B6B", "#FFFFFF", "#A6A8B3"),
            (t.Box, t.BoxBorder, t.Track, t.Normal, t.Ahead, t.Low, t.ClockLine, t.SecondaryText));
        Assert.Equal(("#26272E", "#EDEDF0"), (t.PanelBackground, t.PanelText));
    }

    [Fact]
    public void Daylight_values()
    {
        var t = Theme.Daylight;
        Assert.Equal(("#FFFFFF", "#CFD2DA", "#D5D8E0", "#5A5F6E", "#C77700", "#D13438", "#111318", "#454852"),
            (t.Box, t.BoxBorder, t.Track, t.Normal, t.Ahead, t.Low, t.ClockLine, t.SecondaryText));
        Assert.Equal(("#FFFFFF", "#111318"), (t.PanelBackground, t.PanelText));
    }

    [Fact]
    public void Fill_follows_state()
    {
        Assert.Equal("#D13438", Theme.Daylight.FillFor(BarState.Low));
        Assert.Equal("#C77700", Theme.Daylight.FillFor(BarState.Ahead));
        Assert.Equal("#5A5F6E", Theme.Daylight.FillFor(BarState.Normal));
        Assert.Equal("#F2B84B", Theme.Graphite.FillFor(BarState.Ahead));
    }

    [Fact]
    public void Identity_dots_are_the_same_in_both_themes()
    {
        Assert.Equal("#E08A5B", Theme.DotFor(ProviderIds.Claude));
        Assert.Equal("#7FB0FF", Theme.DotFor(ProviderIds.Codex));
        Assert.Equal("#5CC8B0", Theme.DotFor(ProviderIds.OpenCode));
    }
}
