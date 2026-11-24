using Battery.Core;

namespace Battery.Tests;

public class StripModelTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    static readonly Dictionary<string, string> NoHints = new();

    static UsageWindow Five(double used, DateTimeOffset resetsAt) =>
        new(WindowKind.FiveHour, used, resetsAt, TimeSpan.FromHours(5));
    static UsageWindow Week(double used, DateTimeOffset resetsAt) =>
        new(WindowKind.Weekly, used, resetsAt, TimeSpan.FromDays(7));
    static UsageWindow Month(double used, DateTimeOffset resetsAt) =>
        new(WindowKind.Monthly, used, resetsAt, TimeSpan.FromDays(30));

    static Dictionary<string, ProviderReading> One(string id, DateTimeOffset readAt, params UsageWindow[] windows) =>
        new() { [id] = new ProviderReading(id, windows, readAt) };

    /// <summary>Claude reading whose reset times are fixed relative to when it was read.</summary>
    static Dictionary<string, ProviderReading> ClaudeReadAt(DateTimeOffset readAt) =>
        One(ProviderIds.Claude, readAt,
            Five(38, readAt.AddHours(2.25)),
            Week(66, readAt.AddHours(75.6)));

    [Fact]
    public void No_reading_gives_missing_bars()
    {
        var apps = StripModel.Build(new Dictionary<string, ProviderReading>(), NoHints, Now);

        Assert.Equal(new[] { ProviderIds.Claude, ProviderIds.Codex, ProviderIds.OpenCode }, apps.Select(a => a.ProviderId));
        Assert.Equal(new[] { "Claude", "Codex", "OpenCode Go" }, apps.Select(a => a.DisplayName));
        Assert.All(apps, a =>
        {
            Assert.True(a.FiveHour.Missing);
            Assert.True(a.Weekly.Missing);
            Assert.Null(a.ReadAt);
        });
        Assert.Empty(apps[2].AllWindows);
    }

    [Fact]
    public void Fresh_reading_maps_fill_clock_state()
    {
        var claude = StripModel.Build(ClaudeReadAt(Now.AddMinutes(-1)), NoHints, Now.AddMinutes(-1))[0];

        Assert.Equal(38, claude.FiveHour.FillPercent);
        Assert.Equal(55, claude.FiveHour.ClockPercent, 3);
        Assert.Equal(BarState.Normal, claude.FiveHour.State);
        Assert.False(claude.FiveHour.Stale);
        Assert.False(claude.FiveHour.Missing);

        Assert.Equal(66, claude.Weekly.FillPercent);
        Assert.Equal(55, claude.Weekly.ClockPercent, 3);
        Assert.Equal(BarState.Ahead, claude.Weekly.State);
    }

    [Fact]
    public void Reading_one_minute_old_is_not_stale()
    {
        var claude = StripModel.Build(ClaudeReadAt(Now.AddMinutes(-1)), NoHints, Now)[0];

        Assert.False(claude.FiveHour.Stale);
        Assert.False(claude.Weekly.Stale);
        Assert.Equal(Now.AddMinutes(-1), claude.ReadAt);
    }

    [Fact]
    public void Old_reading_is_stale_but_keeps_fill_and_moves_clock()
    {
        var claude = StripModel.Build(ClaudeReadAt(Now.AddMinutes(-16)), NoHints, Now)[0];

        Assert.True(claude.FiveHour.Stale);
        Assert.True(claude.Weekly.Stale);
        Assert.Equal(38, claude.FiveHour.FillPercent);
        Assert.True(claude.FiveHour.ClockPercent > 55);
    }

    [Fact]
    public void Exactly_15_minutes_is_not_stale()
    {
        var claude = StripModel.Build(ClaudeReadAt(Now.AddMinutes(-15)), NoHints, Now)[0];

        Assert.False(claude.FiveHour.Stale);
    }

    [Fact]
    public void Passed_reset_gives_empty_stale_bar()
    {
        var readings = One(ProviderIds.Claude, Now.AddMinutes(-5),
            Five(90, Now.AddMinutes(-1)),
            Week(10, Now.AddDays(3)));

        var claude = StripModel.Build(readings, NoHints, Now)[0];

        Assert.Equal(0, claude.FiveHour.FillPercent);
        Assert.Equal(0, claude.FiveHour.ClockPercent);
        Assert.True(claude.FiveHour.Stale);
        Assert.False(claude.FiveHour.Missing);
        Assert.Equal(BarState.Normal, claude.FiveHour.State);
        Assert.False(claude.Weekly.Stale);   // the other window is unaffected
    }

    [Fact]
    public void Absent_window_is_missing_not_zero()
    {
        var readings = One(ProviderIds.Codex, Now.AddMinutes(-1), Week(20, Now.AddDays(3)));

        var codex = StripModel.Build(readings, NoHints, Now)[1];

        Assert.True(codex.FiveHour.Missing);
        Assert.False(codex.Weekly.Missing);
        Assert.Equal(20, codex.Weekly.FillPercent);
    }

    [Fact]
    public void OpenCode_strip_uses_weekly_and_lists_monthly()
    {
        var readings = One(ProviderIds.OpenCode, Now.AddMinutes(-1),
            Month(26, Now.AddDays(20)),
            Five(12, Now.AddHours(4)),
            Week(40, Now.AddDays(5)));

        var opencode = StripModel.Build(readings, NoHints, Now)[2];

        Assert.Equal(WindowKind.Weekly, opencode.Weekly.Kind);
        Assert.Equal(40, opencode.Weekly.FillPercent);
        Assert.Equal(new[] { WindowKind.FiveHour, WindowKind.Weekly, WindowKind.Monthly },
            opencode.AllWindows.Select(b => b.Kind));
    }

    [Fact]
    public void Fill_is_clamped_to_0_100()
    {
        var readings = One(ProviderIds.Claude, Now.AddMinutes(-1),
            Five(140, Now.AddHours(1)),
            Week(-3, Now.AddDays(3)));

        var claude = StripModel.Build(readings, NoHints, Now)[0];

        Assert.Equal(100, claude.FiveHour.FillPercent);
        Assert.Equal(0, claude.Weekly.FillPercent);
    }

    [Fact]
    public void Hint_is_passed_through()
    {
        var hints = new Dictionary<string, string> { [ProviderIds.OpenCode] = "Sign in again" };

        var apps = StripModel.Build(new Dictionary<string, ProviderReading>(), hints, Now);

        Assert.Equal("Sign in again", apps[2].Hint);
        Assert.Null(apps[0].Hint);
    }
}
