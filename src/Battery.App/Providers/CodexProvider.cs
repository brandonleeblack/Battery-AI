using System.IO;
using Battery.Core;
using Battery.Core.Providers;

namespace Battery.App.Providers;

/// <summary>Reads the newest rate-limit sample from Codex CLI session files. Passive: fresh only after Codex runs.</summary>
public sealed class CodexProvider : IUsageProvider
{
    public const string NoReadingHint = "Run Codex once to start readings";
    static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(60);

    readonly string _sessionsDir;
    Timer? _timer;
    int _polling;

    public CodexProvider(string? sessionsDir = null)
    {
        _sessionsDir = sessionsDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
    }

    /// <summary>The folder Codex writes its session files to.</summary>
    public string SessionsDir => _sessionsDir;

    public string Id => ProviderIds.Codex;
    public event Action<ProviderReading>? ReadingAvailable;
    public event Action<string?>? HintChanged;

    public void Start() => _timer ??= new Timer(_ => Poll(), null, TimeSpan.Zero, PollEvery);

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public Task RefreshAsync() => Task.Run(Poll);

    void Poll()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            var reading = CodexSessionParser.LatestReading(_sessionsDir, DateTimeOffset.Now);
            if (reading is null)
            {
                HintChanged?.Invoke(NoReadingHint);
            }
            else
            {
                ReadingAvailable?.Invoke(reading);
                HintChanged?.Invoke(null);
            }
        }
        catch
        {
            // A broken source must never take the app down; keep the last reading.
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }
}
