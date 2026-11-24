namespace Battery.Core;

public static class PaceCalculator
{
    /// <summary>Used percent above which less than 20% of the allowance is left.</summary>
    public const double LowAboveUsedPercent = 80;

    /// <summary>How many points used must lead the clock before the bar counts as ahead.</summary>
    public const double AheadMarginPoints = 5;

    /// <summary>Percent of the window's time that has gone, clamped to 0–100.</summary>
    public static double ElapsedPercent(UsageWindow w, DateTimeOffset now)
    {
        if (w.Length <= TimeSpan.Zero) return 0;
        var untilReset = w.ResetsAt - now;
        var elapsed = (w.Length - untilReset) / w.Length * 100;
        return Math.Clamp(elapsed, 0, 100);
    }

    /// <summary>Low (under 20% left) wins over Ahead (more than 5 points past the clock).</summary>
    public static BarState StateFor(double usedPercent, double elapsedPercent)
    {
        if (usedPercent > LowAboveUsedPercent) return BarState.Low;
        if (usedPercent - elapsedPercent > AheadMarginPoints) return BarState.Ahead;
        return BarState.Normal;
    }
}
