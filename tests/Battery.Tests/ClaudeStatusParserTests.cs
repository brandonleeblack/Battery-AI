using Battery.Core;
using Battery.Core.Providers;

namespace Battery.Tests;

public class ClaudeStatusParserTests
{
    static readonly DateTimeOffset ReadAt = new(2026, 11, 24, 6, 51, 0, TimeSpan.Zero);

    [Fact]
    public void Parses_fixture()
    {
        var reading = ClaudeStatusParser.Parse(Fixture.Text("claude-status.json"), ReadAt);

        Assert.NotNull(reading);
        Assert.Equal(ProviderIds.Claude, reading.ProviderId);
        Assert.Equal(ReadAt, reading.ReadAt);
        Assert.Equal(2, reading.Windows.Count);

        var five = reading.Windows.Single(w => w.Kind == WindowKind.FiveHour);
        Assert.Equal(94, five.UsedPercent);
        Assert.Equal(TimeSpan.FromHours(5), five.Length);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795503600), five.ResetsAt);

        var week = reading.Windows.Single(w => w.Kind == WindowKind.Weekly);
        Assert.Equal(26, week.UsedPercent);
        Assert.Equal(TimeSpan.FromDays(7), week.Length);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795636800), week.ResetsAt);
    }

    [Fact]
    public void Resets_at_as_unix_seconds()
    {
        var json = """{"rate_limits":{"five_hour":{"used_percentage":40,"resets_at":1795503600}}}""";

        var reading = ClaudeStatusParser.Parse(json, ReadAt);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795503600), Assert.Single(reading!.Windows).ResetsAt);
    }

    [Fact]
    public void Resets_at_as_iso_string()
    {
        var json = """{"rate_limits":{"five_hour":{"used_percentage":40,"resets_at":"2026-11-24T07:00:00Z"}}}""";

        var reading = ClaudeStatusParser.Parse(json, ReadAt);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795503600), Assert.Single(reading!.Windows).ResetsAt);
    }

    [Fact]
    public void No_rate_limits_returns_null() => Assert.Null(ClaudeStatusParser.Parse("""{"model":{}}""", ReadAt));

    [Fact]
    public void Empty_rate_limits_returns_null() =>
        Assert.Null(ClaudeStatusParser.Parse("""{"rate_limits":{}}""", ReadAt));

    [Fact]
    public void Only_seven_day_present_returns_one_window()
    {
        var json = """{"rate_limits":{"seven_day":{"used_percentage":26,"resets_at":1795636800}}}""";

        var reading = ClaudeStatusParser.Parse(json, ReadAt);

        Assert.Equal(WindowKind.Weekly, Assert.Single(reading!.Windows).Kind);
    }

    [Fact]
    public void Window_missing_a_field_is_dropped()
    {
        var json = """{"rate_limits":{"five_hour":{"used_percentage":40},"seven_day":{"used_percentage":26,"resets_at":1795636800}}}""";

        var reading = ClaudeStatusParser.Parse(json, ReadAt);

        Assert.Equal(WindowKind.Weekly, Assert.Single(reading!.Windows).Kind);
    }

    [Fact]
    public void Truncated_json_returns_null()
    {
        var text = Fixture.Text("claude-status.json");

        Assert.Null(ClaudeStatusParser.Parse(text[..(text.Length / 2)], ReadAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"rate_limits\":null}")]
    [InlineData("{\"rate_limits\":\"x\"}")]
    public void Odd_input_returns_null_without_throwing(string text) => Assert.Null(ClaudeStatusParser.Parse(text, ReadAt));

    [Fact]
    public void Byte_order_mark_is_tolerated()
    {
        var reading = ClaudeStatusParser.Parse("﻿" + Fixture.Text("claude-status.json"), ReadAt);

        Assert.Equal(2, reading!.Windows.Count);
    }
}
