using Battery.Core;

namespace Battery.Tests;

public class HoverFormatTests
{
    static TimeSpan T(int d = 0, int h = 0, int m = 0, int s = 0) => new(d, h, m, s);

    [Fact] public void Reset_minutes() => Assert.Equal("47m", HoverFormat.FormatReset(T(m: 47)));
    [Fact] public void Reset_hours_and_minutes() => Assert.Equal("2h 14m", HoverFormat.FormatReset(T(h: 2, m: 14)));
    [Fact] public void Reset_pads_minutes_after_hours() => Assert.Equal("4h 02m", HoverFormat.FormatReset(T(h: 4, m: 2)));
    [Fact] public void Reset_days_and_hours() => Assert.Equal("3d 4h", HoverFormat.FormatReset(T(d: 3, h: 4)));
    [Fact] public void Reset_whole_days_keeps_zero_hours() => Assert.Equal("5d 0h", HoverFormat.FormatReset(T(d: 5, m: 30)));
    [Fact] public void Reset_ten_days_or_more_is_days_only() => Assert.Equal("12d", HoverFormat.FormatReset(T(d: 12, h: 3)));
    [Fact] public void Reset_under_a_minute_is_one_minute() => Assert.Equal("1m", HoverFormat.FormatReset(T(s: 20)));
    [Fact] public void Reset_zero_is_now() => Assert.Equal("now", HoverFormat.FormatReset(TimeSpan.Zero));
    [Fact] public void Reset_negative_is_now() => Assert.Equal("now", HoverFormat.FormatReset(T(m: -5)));

    [Fact] public void Age_under_a_minute() => Assert.Equal("just now", HoverFormat.FormatAge(T(s: 59)));
    [Fact] public void Age_negative_is_just_now() => Assert.Equal("just now", HoverFormat.FormatAge(T(m: -3)));
    [Fact] public void Age_minutes() => Assert.Equal("5m ago", HoverFormat.FormatAge(T(m: 5)));
    [Fact] public void Age_hours() => Assert.Equal("3h ago", HoverFormat.FormatAge(T(h: 3, m: 20)));
    [Fact] public void Age_days() => Assert.Equal("2d ago", HoverFormat.FormatAge(T(d: 2, h: 1)));

    [Theory]
    [InlineData(WindowKind.FiveHour, "5-hour")]
    [InlineData(WindowKind.Weekly, "Week")]
    [InlineData(WindowKind.Monthly, "Month")]
    public void Window_labels(WindowKind kind, string expected) => Assert.Equal(expected, HoverFormat.Label(kind));
}
