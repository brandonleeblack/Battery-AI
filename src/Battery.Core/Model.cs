namespace Battery.Core;

public enum WindowKind { FiveHour, Weekly, Monthly }

/// <summary>One allowance window as last read from a provider.</summary>
public record UsageWindow(WindowKind Kind, double UsedPercent, DateTimeOffset ResetsAt, TimeSpan Length)
{
    /// <summary>
    /// No window is running (OpenCode's 5-hour window opens on first use). ResetsAt is then only
    /// "a window opened now would end here": used for expiry, never shown as a reset time.
    /// </summary>
    public bool NotStarted { get; init; }
}

public record ProviderReading(string ProviderId, IReadOnlyList<UsageWindow> Windows, DateTimeOffset ReadAt);

public static class ProviderIds
{
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string OpenCode = "opencode";
}

public enum BarState { Normal, Ahead, Low }
