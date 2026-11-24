namespace Battery.Core;

/// <summary>What one bar should draw. Missing = no data for this window at all (outline only).</summary>
public record BarView(WindowKind Kind, double FillPercent, double ClockPercent, BarState State,
    bool Stale, bool Missing, DateTimeOffset? ResetsAt);

/// <summary>
/// One app on the strip. FiveHour and Weekly are the two strip bars. AllWindows feeds the hover panel:
/// empty when there has never been a reading, otherwise FiveHour and Weekly (Missing when absent)
/// followed by Monthly when the provider has one.
/// </summary>
public record AppView(string ProviderId, string DisplayName, BarView FiveHour, BarView Weekly,
    IReadOnlyList<BarView> AllWindows, DateTimeOffset? ReadAt, string? Hint);

public static class StripModel
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    static readonly (string Id, string Name)[] Apps =
    {
        (ProviderIds.Claude, "Claude"),
        (ProviderIds.Codex, "Codex"),
        (ProviderIds.OpenCode, "OpenCode Go"),
    };

    /// <summary>Always three apps, in the fixed order Claude, Codex, OpenCode Go.</summary>
    public static IReadOnlyList<AppView> Build(
        IReadOnlyDictionary<string, ProviderReading> readings,
        IReadOnlyDictionary<string, string> hints,
        DateTimeOffset now)
    {
        var result = new List<AppView>(Apps.Length);
        foreach (var (id, name) in Apps)
        {
            hints.TryGetValue(id, out var hint);
            if (!readings.TryGetValue(id, out var reading))
            {
                result.Add(new AppView(id, name, MissingBar(WindowKind.FiveHour), MissingBar(WindowKind.Weekly),
                    Array.Empty<BarView>(), null, hint));
                continue;
            }

            var readingIsOld = now - reading.ReadAt > StaleAfter;
            BarView BarFor(WindowKind kind)
            {
                var w = reading.Windows.FirstOrDefault(x => x.Kind == kind);
                return w is null ? MissingBar(kind) : ToBar(w, readingIsOld, now);
            }

            var five = BarFor(WindowKind.FiveHour);
            var week = BarFor(WindowKind.Weekly);
            var all = new List<BarView> { five, week };
            var month = BarFor(WindowKind.Monthly);
            if (!month.Missing) all.Add(month);

            result.Add(new AppView(id, name, five, week, all, reading.ReadAt, hint));
        }
        return result;
    }

    static BarView MissingBar(WindowKind kind) => new(kind, 0, 0, BarState.Normal, false, true, null);

    static BarView ToBar(UsageWindow w, bool readingIsOld, DateTimeOffset now)
    {
        // The window has rolled over since this reading: the old figure no longer applies.
        if (w.ResetsAt <= now)
            return new BarView(w.Kind, 0, 0, BarState.Normal, true, false, w.ResetsAt);

        var fill = Math.Clamp(w.UsedPercent, 0, 100);
        var clock = PaceCalculator.ElapsedPercent(w, now);
        return new BarView(w.Kind, fill, clock, PaceCalculator.StateFor(fill, clock), readingIsOld, false, w.ResetsAt);
    }
}
