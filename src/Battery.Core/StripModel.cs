namespace Battery.Core;

/// <summary>What one bar should draw. Missing = no data for this window at all (outline only).</summary>
public record BarView(WindowKind Kind, double FillPercent, double ClockPercent, BarState State,
    bool Stale, bool Missing, DateTimeOffset? ResetsAt)
{
    /// <summary>No window running: nothing used and no real reset time (ResetsAt is null).</summary>
    public bool NotStarted { get; init; }

    /// <summary>The window rolled over after the last reading: drawn empty, stale, with no clock line.</summary>
    public bool ResetPassed { get; init; }
}

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

    /// <summary>
    /// The shown apps in the fixed order Claude, Codex, OpenCode Go: all three unless some are hidden
    /// (see <see cref="ShownApps"/>; at least one always shows).
    /// </summary>
    public static IReadOnlyList<AppView> Build(
        IReadOnlyDictionary<string, ProviderReading> readings,
        IReadOnlyDictionary<string, string> hints,
        DateTimeOffset now,
        IEnumerable<string>? hidden = null)
    {
        var apps = ShownApps.Visible(hidden);
        var result = new List<AppView>(apps.Count);
        foreach (var (id, name) in apps)
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
            return new BarView(w.Kind, 0, 0, BarState.Normal, true, false, w.ResetsAt) { ResetPassed = true };

        var fill = Math.Clamp(w.UsedPercent, 0, 100);
        if (w.NotStarted)
            return new BarView(w.Kind, fill, 0, PaceCalculator.StateFor(fill, 0), readingIsOld, false, null) { NotStarted = true };

        var clock = PaceCalculator.ElapsedPercent(w, now);
        return new BarView(w.Kind, fill, clock, PaceCalculator.StateFor(fill, clock), readingIsOld, false, w.ResetsAt);
    }
}
