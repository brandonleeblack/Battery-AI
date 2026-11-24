namespace Battery.Core;

/// <summary>
/// Made-up readings for <c>Battery.exe --demo</c>, used for screenshots: every colour shows (grey,
/// amber, red) and no real account is read. Times are relative to <paramref name="now"/> so the clock
/// lines sit in believable places.
/// </summary>
public static class DemoReadings
{
    public static IReadOnlyDictionary<string, ProviderReading> At(DateTimeOffset now)
    {
        var fiveHours = TimeSpan.FromHours(5);
        var week = TimeSpan.FromDays(7);
        var month = TimeSpan.FromDays(30);

        UsageWindow W(WindowKind kind, double used, TimeSpan left, TimeSpan length) =>
            new(kind, used, now + left, length);

        ProviderReading R(string id, params UsageWindow[] windows) => new(id, windows, now);

        return new Dictionary<string, ProviderReading>
        {
            [ProviderIds.Claude] = R(ProviderIds.Claude,
                W(WindowKind.FiveHour, 42, TimeSpan.FromHours(3), fiveHours),
                W(WindowKind.Weekly, 31, TimeSpan.FromDays(4), week)),
            [ProviderIds.Codex] = R(ProviderIds.Codex,
                W(WindowKind.FiveHour, 68, TimeSpan.FromHours(2.5), fiveHours),      // ahead of the clock: amber
                W(WindowKind.Weekly, 47, TimeSpan.FromDays(3), week)),
            [ProviderIds.OpenCode] = R(ProviderIds.OpenCode,
                W(WindowKind.FiveHour, 18, TimeSpan.FromHours(4), fiveHours),
                W(WindowKind.Weekly, 86, TimeSpan.FromDays(1), week),                // under 20% left: red
                W(WindowKind.Monthly, 52, TimeSpan.FromDays(12), month)),
        };
    }
}
