using Battery.Core;
using Battery.Core.Providers;

namespace Battery.Tests;

public class OpenCodeUsageParserTests
{
    static readonly DateTimeOffset ReadAt = new(2026, 11, 24, 6, 52, 0, TimeSpan.Zero);

    static string Meters(string fiveHour, string week, string month) =>
        "{\"product\":\"go\",\"access\":{\"startsAt\":\"2026-11-17T17:55:26.000Z\",\"endsAt\":\"2026-12-17T17:55:26.000Z\"," +
        "\"meters\":{\"fiveHour\":" + fiveHour + ",\"week\":" + week + ",\"month\":" + month + "}}}";

    const string Week = "{\"startsAt\":\"2026-11-23T00:00:00.000Z\",\"resetsAt\":\"2026-11-30T00:00:00.000Z\",\"limitMicroCents\":\"3000000000\",\"usedMicroCents\":\"0\"}";
    const string Month = "{\"resetsAt\":\"2026-12-17T17:55:26.000Z\",\"limitMicroCents\":\"6000000000\",\"usedMicroCents\":\"0\"}";

    [Fact]
    public void Parses_fixture()
    {
        var result = OpenCodeUsageParser.Parse(Fixture.Text("opencode-go-usage.json"), ReadAt);

        Assert.Equal(OpenCodeParseStatus.Ok, result.Status);
        var reading = result.Reading!;
        Assert.Equal(ProviderIds.OpenCode, reading.ProviderId);
        Assert.Equal(ReadAt, reading.ReadAt);
        Assert.Equal(new[] { WindowKind.FiveHour, WindowKind.Weekly, WindowKind.Monthly }, reading.Windows.Select(w => w.Kind));

        var week = reading.Windows[1];
        Assert.Equal(2413179.0 / 3000000000 * 100, week.UsedPercent, 9);
        Assert.Equal(DateTimeOffset.Parse("2026-11-30T00:00:00Z"), week.ResetsAt);
        Assert.Equal(TimeSpan.FromDays(7), week.Length);

        var month = reading.Windows[2];
        Assert.Equal(3002413179.0 / 6000000000 * 100, month.UsedPercent, 9);
        Assert.Equal(DateTimeOffset.Parse("2026-12-17T17:55:26Z"), month.ResetsAt);
        Assert.Equal(TimeSpan.FromDays(30), month.Length);   // the billing period, Nov 17 to Dec 17
    }

    [Fact]
    public void Five_hour_window_not_running_reads_as_unused_with_a_window_starting_now()
    {
        // Real data: with no 5-hour window open, fiveHour has startsAt and resetsAt null and nothing used.
        var reading = OpenCodeUsageParser.Parse(Fixture.Text("opencode-go-usage.json"), ReadAt).Reading!;

        var five = reading.Windows[0];
        Assert.Equal(0, five.UsedPercent);
        Assert.Equal(TimeSpan.FromHours(5), five.Length);
        Assert.Equal(ReadAt.AddHours(5), five.ResetsAt);
        Assert.Equal(0, PaceCalculator.ElapsedPercent(five, ReadAt));
        Assert.True(five.NotStarted);
        Assert.False(reading.Windows[1].NotStarted);
    }

    [Fact]
    public void Running_five_hour_window_uses_its_own_times()
    {
        var five = "{\"startsAt\":\"2026-11-24T05:00:00.000Z\",\"resetsAt\":\"2026-11-24T10:00:00.000Z\",\"limitMicroCents\":\"1200000000\",\"usedMicroCents\":\"300000000\"}";

        var reading = OpenCodeUsageParser.Parse(Meters(five, Week, Month), ReadAt).Reading!;

        Assert.Equal(25, reading.Windows[0].UsedPercent, 9);
        Assert.Equal(DateTimeOffset.Parse("2026-11-24T10:00:00Z"), reading.Windows[0].ResetsAt);
        Assert.Equal(TimeSpan.FromHours(5), reading.Windows[0].Length);
    }

    [Fact]
    public void Signed_out_payload_gives_SignedOut()
    {
        var result = OpenCodeUsageParser.Parse(Fixture.Text("opencode-go-usage-signed-out.json"), ReadAt);

        Assert.Equal(OpenCodeParseStatus.SignedOut, result.Status);
        Assert.Null(result.Reading);
    }

    [Theory]
    [InlineData("<html>hello</html>")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"access\":null}")]
    [InlineData("{\"product\":\"go\",\"access\":{\"meters\":{}}}")]
    [InlineData("{\"_tag\":\"SomethingElse\"}")]
    public void Garbage_gives_Unrecognised(string payload)
    {
        var result = OpenCodeUsageParser.Parse(payload, ReadAt);

        Assert.Equal(OpenCodeParseStatus.Unrecognised, result.Status);
        Assert.Null(result.Reading);
    }

    [Fact]
    public void Over_limit_caps_at_100()
    {
        var overWeek = Week.Replace("\"usedMicroCents\":\"0\"", "\"usedMicroCents\":\"4500000000\"");

        var reading = OpenCodeUsageParser.Parse(Meters("null", overWeek, Month), ReadAt).Reading!;

        Assert.Equal(100, reading.Windows.Single(w => w.Kind == WindowKind.Weekly).UsedPercent);
    }

    [Fact]
    public void Meter_with_bad_numbers_is_dropped_not_zero()
    {
        var badWeek = Week.Replace("\"limitMicroCents\":\"3000000000\"", "\"limitMicroCents\":\"0\"");

        var reading = OpenCodeUsageParser.Parse(Meters("null", badWeek, Month), ReadAt).Reading!;

        Assert.Equal(new[] { WindowKind.Monthly }, reading.Windows.Select(w => w.Kind));
    }

    [Theory]
    [InlineData("https://opencode.ai/console/api/go/status", true)]
    [InlineData("https://opencode.ai/console/api/go/status?x=1", true)]
    [InlineData("https://OPENCODE.AI/console/api/go/status", true)]
    [InlineData("https://opencode.ai/console/api/go/statusx", false)]
    [InlineData("https://opencode.ai/console/api/billing/drawer/status", false)]
    [InlineData("https://evil.example/console/api/go/status", false)]
    [InlineData("not a url", false)]
    public void Recognises_the_usage_request(string url, bool expected) =>
        Assert.Equal(expected, OpenCodeUsageParser.IsUsageRequest(url));

    [Theory]
    [InlineData("https://opencode.ai/console/login", true)]
    [InlineData("https://opencode.ai/auth/authorize?client=x", true)]
    [InlineData("https://accounts.example.com/oauth", true)]   // sent off to an identity provider
    [InlineData("https://opencode.ai/console/org_ABC", false)]
    [InlineData("https://opencode.ai/console/workspace", false)]
    [InlineData("about:blank", false)]
    public void Recognises_being_sent_to_sign_in(string url, bool expected) =>
        Assert.Equal(expected, OpenCodeUsageParser.IsSignInPage(url));

    [Fact]
    public void Numbers_may_also_come_as_json_numbers()
    {
        var numericWeek = Week.Replace("\"3000000000\"", "3000000000").Replace("\"usedMicroCents\":\"0\"", "\"usedMicroCents\":1500000000");

        var reading = OpenCodeUsageParser.Parse(Meters("null", numericWeek, Month), ReadAt).Reading!;

        Assert.Equal(50, reading.Windows.Single(w => w.Kind == WindowKind.Weekly).UsedPercent, 9);
    }
}
