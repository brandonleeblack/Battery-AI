namespace Battery.Core.Providers;

/// <summary>
/// One data source. Providers are isolated: each raises its own readings and hints and must never
/// throw out of Start, Stop, RefreshAsync or its own timers.
/// </summary>
public interface IUsageProvider
{
    string Id { get; }

    /// <summary>A new reading was obtained.</summary>
    event Action<ProviderReading>? ReadingAvailable;

    /// <summary>The per-provider message for the hover panel changed; null clears it.</summary>
    event Action<string?>? HintChanged;

    void Start();
    void Stop();
    Task RefreshAsync();
}
