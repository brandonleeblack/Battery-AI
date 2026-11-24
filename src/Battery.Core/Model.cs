namespace Battery.Core;

public enum WindowKind { FiveHour, Weekly, Monthly }

/// <summary>One allowance window as last read from a provider.</summary>
public record UsageWindow(WindowKind Kind, double UsedPercent, DateTimeOffset ResetsAt, TimeSpan Length);

public record ProviderReading(string ProviderId, IReadOnlyList<UsageWindow> Windows, DateTimeOffset ReadAt);

public static class ProviderIds
{
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string OpenCode = "opencode";
}

public enum BarState { Normal, Ahead, Low }
