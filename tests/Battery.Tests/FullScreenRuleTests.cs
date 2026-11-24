using Battery.Core;

namespace Battery.Tests;

public class FullScreenRuleTests
{
    static readonly RectD Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void Borderless_window_covering_the_screen_is_full_screen() =>
        Assert.True(FullScreenRule.IsFullScreen(new RectD(0, 0, 1920, 1080), Screen, isMaximized: false, hasCaption: false));

    [Fact]
    public void Window_smaller_than_the_screen_is_not_full_screen() =>
        Assert.False(FullScreenRule.IsFullScreen(new RectD(0, 0, 1920, 1032), Screen, isMaximized: false, hasCaption: false));

    [Fact]
    public void Maximised_normal_window_over_an_auto_hide_taskbar_is_not_full_screen() =>
        // With auto-hide the work area is the whole monitor, so a maximised window overhangs every edge.
        Assert.False(FullScreenRule.IsFullScreen(new RectD(-8, -8, 1936, 1096), Screen, isMaximized: true, hasCaption: true));

    [Fact]
    public void Maximised_window_that_dropped_its_caption_is_full_screen() =>
        // Browsers going full screen for a video drop the caption, maximised or not.
        Assert.True(FullScreenRule.IsFullScreen(new RectD(0, 0, 1920, 1080), Screen, isMaximized: true, hasCaption: false));

    [Fact]
    public void Captioned_window_dragged_to_cover_the_screen_but_not_maximised_is_full_screen() =>
        Assert.True(FullScreenRule.IsFullScreen(new RectD(0, 0, 1920, 1080), Screen, isMaximized: false, hasCaption: true));
}
