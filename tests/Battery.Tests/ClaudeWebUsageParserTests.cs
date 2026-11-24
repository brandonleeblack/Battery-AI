using Battery.Core;
using Battery.Core.Providers;

namespace Battery.Tests;

public class ClaudeWebUsageParserTests
{
    static readonly DateTimeOffset ReadAt = new(2026, 11, 24, 7, 30, 0, TimeSpan.Zero);

    static string Usage(string fiveHour, string sevenDay) =>
        "{\"five_hour\":" + fiveHour + ",\"seven_day\":" + sevenDay + ",\"seven_day_opus\":null,\"extra_usage\":null}";

    const string Week = "{\"utilization\":33.0,\"resets_at\":\"2026-11-25T19:59:59.791417+00:00\"}";

    [Fact]
    public void Parses_fixture()
    {
        var result = ClaudeWebUsageParser.Parse(Fixture.Text("claude-web-usage.json"), ReadAt);

        Assert.Equal(ClaudeWebParseStatus.Ok, result.Status);
        var reading = result.Reading!;
        Assert.Equal(ProviderIds.Claude, reading.ProviderId);
        Assert.Equal(ReadAt, reading.ReadAt);
        Assert.Equal(new[] { WindowKind.FiveHour, WindowKind.Weekly }, reading.Windows.Select(w => w.Kind));

        var five = reading.Windows[0];
        Assert.Equal(51, five.UsedPercent);
        Assert.Equal(TimeSpan.FromHours(5), five.Length);
        Assert.Equal(DateTimeOffset.Parse("2026-11-24T11:59:59.791395+00:00"), five.ResetsAt);
        Assert.False(five.NotStarted);

        var week = reading.Windows[1];
        Assert.Equal(33, week.UsedPercent);
        Assert.Equal(TimeSpan.FromDays(7), week.Length);
        Assert.Equal(DateTimeOffset.Parse("2026-11-25T19:59:59.791417+00:00"), week.ResetsAt);
    }

    [Fact]
    public void Absent_window_is_left_out_not_zero()
    {
        var reading = ClaudeWebUsageParser.Parse(Usage("null", Week), ReadAt).Reading!;

        Assert.Equal(WindowKind.Weekly, Assert.Single(reading.Windows).Kind);
    }

    [Fact]
    public void Unused_window_with_no_reset_time_is_not_started()
    {
        var reading = ClaudeWebUsageParser.Parse(Usage("{\"utilization\":0,\"resets_at\":null}", Week), ReadAt).Reading!;

        var five = reading.Windows[0];
        Assert.True(five.NotStarted);
        Assert.Equal(0, five.UsedPercent);
        Assert.Equal(ReadAt.AddHours(5), five.ResetsAt);
    }

    [Fact]
    public void Used_window_with_no_reset_time_is_dropped_rather_than_guessed()
    {
        var reading = ClaudeWebUsageParser.Parse(Usage("{\"utilization\":40,\"resets_at\":null}", Week), ReadAt).Reading!;

        Assert.Equal(WindowKind.Weekly, Assert.Single(reading.Windows).Kind);
    }

    [Fact]
    public void Over_limit_caps_at_100()
    {
        var reading = ClaudeWebUsageParser.Parse(
            Usage("{\"utilization\":130,\"resets_at\":\"2026-11-24T11:59:59Z\"}", Week), ReadAt).Reading!;

        Assert.Equal(100, reading.Windows[0].UsedPercent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<!DOCTYPE html><html><title>Just a moment...</title></html>")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"five_hour\":null,\"seven_day\":null}")]
    [InlineData("{\"type\":\"error\",\"error\":{\"type\":\"permission_error\"}}")]
    public void Garbage_gives_Unrecognised(string payload)
    {
        var result = ClaudeWebUsageParser.Parse(payload, ReadAt);

        Assert.Equal(ClaudeWebParseStatus.Unrecognised, result.Status);
        Assert.Null(result.Reading);
    }

    [Theory]
    [InlineData("https://claude.ai/api/organizations/0f0e2a7c-1111-2222-3333-444455556666/usage", true)]
    [InlineData("https://claude.ai/api/organizations/abc/usage?x=1", true)]
    [InlineData("https://claude.ai/api/organizations/abc/usage/extra", false)]
    [InlineData("https://claude.ai/api/organizations/abc/prepaid/credits", false)]
    [InlineData("https://claude.ai/api/claude_code/organizations/abc/usage", false)]
    [InlineData("https://evil.example/api/organizations/abc/usage", false)]
    [InlineData("nonsense", false)]
    public void Recognises_the_usage_request(string url, bool expected) =>
        Assert.Equal(expected, ClaudeWebUsageParser.IsUsageRequest(url));

    [Theory]
    [InlineData("https://claude.ai/login", true)]
    [InlineData("https://claude.ai/login?returnTo=%2Fsettings%2Fusage", true)]
    [InlineData("https://accounts.google.com/o/oauth2/auth", true)]
    [InlineData("https://claude.ai/settings/usage", false)]
    [InlineData("https://claude.ai/new", false)]
    [InlineData("about:blank", false)]
    public void Recognises_being_sent_to_sign_in(string url, bool expected) =>
        Assert.Equal(expected, ClaudeWebUsageParser.IsSignInPage(url));
}
