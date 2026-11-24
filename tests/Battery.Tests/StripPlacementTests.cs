using Battery.Core;

namespace Battery.Tests;

public class StripPlacementTests
{
    static readonly RectD Taskbar = new(0, 1032, 1920, 48);
    static readonly RectD Tray = new(1700, 1032, 220, 48);
    static readonly RectD Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void Default_sits_8px_left_of_tray_centred()
    {
        var r = StripPlacement.Compute(Taskbar, Tray, Screen, 140, 32, null);

        Assert.Equal(new RectD(1552, 1040, 140, 32), r);
    }

    [Fact]
    public void Saved_offset_is_used()
    {
        var r = StripPlacement.Compute(Taskbar, Tray, Screen, 140, 32, 300);

        Assert.Equal(1260, r.Left);
        Assert.Equal(1040, r.Top);
    }

    [Fact]
    public void Clamps_to_taskbar_left()
    {
        var r = StripPlacement.Compute(Taskbar, Tray, Screen, 140, 32, 5000);

        Assert.Equal(0, r.Left);
    }

    [Fact]
    public void Negative_offset_clamps_to_taskbar_right()
    {
        var r = StripPlacement.Compute(Taskbar, Tray, Screen, 140, 32, -5000);

        Assert.Equal(1920 - 140, r.Left);
    }

    [Fact]
    public void Missing_tray_falls_back_to_right_edge()
    {
        var r = StripPlacement.Compute(Taskbar, null, Screen, 140, 32, null);

        Assert.Equal(1920 - 8, r.Left + r.Width);
        Assert.Equal(1040, r.Top);
    }

    [Fact]
    public void Missing_taskbar_falls_back_to_screen_bottom_right()
    {
        var r = StripPlacement.Compute(null, null, Screen, 140, 32, null);

        Assert.Equal(new RectD(1920 - 8 - 140, 1080 - 8 - 32, 140, 32), r);
    }

    [Fact]
    public void Strip_wider_than_taskbar_stays_on_screen()
    {
        var r = StripPlacement.Compute(Taskbar, Tray, Screen, 3000, 32, null);

        Assert.Equal(0, r.Left);
    }

    [Fact]
    public void Taskbar_that_is_not_on_the_primary_screen_falls_back_to_screen_bottom_right()
    {
        var secondaryTaskbar = new RectD(1920, 1032, 1920, 48);
        var secondaryTray = new RectD(3620, 1032, 220, 48);

        var r = StripPlacement.Compute(secondaryTaskbar, secondaryTray, Screen, 140, 32, null);

        Assert.Equal(new RectD(1920 - 8 - 140, 1080 - 8 - 32, 140, 32), r);
    }

    [Fact]
    public void Vertical_taskbar_falls_back_to_screen_bottom_right()
    {
        var r = StripPlacement.Compute(new RectD(0, 0, 62, 1080), null, Screen, 140, 32, null);

        Assert.Equal(new RectD(1920 - 8 - 140, 1080 - 8 - 32, 140, 32), r);
    }

    [Fact]
    public void Tray_rectangle_outside_the_taskbar_is_ignored()
    {
        var r = StripPlacement.Compute(Taskbar, new RectD(0, 0, 0, 0), Screen, 140, 32, null);

        Assert.Equal(1920 - 8, r.Left + r.Width);
    }

    [Fact]
    public void Fallback_never_leaves_a_tiny_screen()
    {
        var r = StripPlacement.Compute(null, null, new RectD(0, 0, 100, 20), 140, 32, null);

        Assert.Equal(0, r.Left);
        Assert.Equal(0, r.Top);
    }

    [Fact]
    public void Works_with_a_screen_origin_that_is_not_zero()
    {
        var screen = new RectD(-1920, 0, 1920, 1080);
        var taskbar = new RectD(-1920, 1032, 1920, 48);
        var tray = new RectD(-220, 1032, 220, 48);

        var r = StripPlacement.Compute(taskbar, tray, screen, 140, 32, null);

        Assert.Equal(new RectD(-220 - 8 - 140, 1040, 140, 32), r);
    }
}
