using Battery.Core;

namespace Battery.Tests;

public class TrayTextTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    static ProviderReading Reading(string id, double five, double week) => new(id, new[]
    {
        new UsageWindow(WindowKind.FiveHour, five, Now.AddHours(2), TimeSpan.FromHours(5)),
        new UsageWindow(WindowKind.Weekly, week, Now.AddDays(3), TimeSpan.FromDays(7)),
    }, Now);

    static IReadOnlyList<AppView> Apps(params ProviderReading[] readings) =>
        StripModel.Build(readings.ToDictionary(r => r.ProviderId), new Dictionary<string, string>(), Now);

    [Fact]
    public void One_line_per_app_with_five_hour_and_weekly_used()
    {
        var text = TrayText.For(Apps(Reading(ProviderIds.Claude, 56, 34), Reading(ProviderIds.Codex, 12.4, 85),
            Reading(ProviderIds.OpenCode, 0, 22)));

        Assert.Equal("Battery · used 5h / week\nClaude 56% / 34%\nCodex 12% / 85%\nOpenCode Go 0% / 22%", text);
    }

    [Fact]
    public void App_with_no_reading_says_so()
    {
        var text = TrayText.For(Apps(Reading(ProviderIds.Claude, 56, 34)));

        Assert.Contains("\nCodex no data", text);
    }

    [Fact]
    public void Stale_reading_is_marked_old()
    {
        var old = Reading(ProviderIds.Claude, 56, 34) with { ReadAt = Now.AddHours(-2) };

        Assert.Contains("\nClaude 56% / 34% (old)", TrayText.For(Apps(old)));
    }

    [Fact]
    public void Never_longer_than_Windows_allows()
    {
        var text = TrayText.For(Apps(Reading(ProviderIds.Claude, 100, 100), Reading(ProviderIds.Codex, 100, 100),
            Reading(ProviderIds.OpenCode, 100, 100)));

        Assert.True(text.Length <= TrayText.MaxLength);
    }
}
