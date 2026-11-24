using System.IO;
using Battery.Core;
using Battery.Core.Providers;

namespace Battery.App.Providers;

/// <summary>
/// Watches the file Battery.StatusLine writes each time Claude Code updates its status line.
/// Passive: fresh only while Claude Code is running. A file that cannot be parsed keeps the last reading.
/// </summary>
public sealed class ClaudeProvider : IUsageProvider
{
    public const string NoReadingHint = "Run Claude Code once to start readings";
    public const string NotConnectedHint = "Right-click › Connect Claude Code";
    static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    readonly string _statusFile;
    readonly Func<bool> _isConnected;
    FileSystemWatcher? _watcher;
    Timer? _debounce;
    DateTime _lastWriteSeen;
    bool _haveReading;

    public ClaudeProvider(string? statusFile = null, Func<bool>? isConnected = null)
    {
        _statusFile = statusFile ?? AppPaths.ClaudeStatusFile;
        _isConnected = isConnected ?? ClaudeHookInstaller.IsInstalled;
    }

    public string Id => ProviderIds.Claude;
    public event Action<ProviderReading>? ReadingAvailable;
    public event Action<string?>? HintChanged;

    public void Start()
    {
        try
        {
            var dir = Path.GetDirectoryName(_statusFile)!;
            Directory.CreateDirectory(dir);
            _debounce = new Timer(_ => Read(force: false));
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_statusFile))
            {
                // The shim swaps a temp file in with a rename, which shows up as Renamed or Created.
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _watcher.Changed += (_, _) => Kick();
            _watcher.Created += (_, _) => Kick();
            _watcher.Renamed += (_, _) => Kick();
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            // No watcher: RefreshAsync still works.
        }
        Read(force: true);
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        _debounce?.Dispose();
        _debounce = null;
    }

    public Task RefreshAsync() => Task.Run(() => Read(force: true));

    void Kick() => _debounce?.Change(Debounce, Timeout.InfiniteTimeSpan);

    void Read(bool force)
    {
        try
        {
            if (!File.Exists(_statusFile))
            {
                if (!_haveReading) HintChanged?.Invoke(_isConnected() ? NoReadingHint : NotConnectedHint);
                return;
            }

            var info = new FileInfo(_statusFile);
            if (!force && info.LastWriteTimeUtc == _lastWriteSeen) return;

            string text;
            using (var stream = new FileStream(_statusFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                text = reader.ReadToEnd();
            _lastWriteSeen = info.LastWriteTimeUtc;

            // The reading is as old as the file: a restart must not make an old reading look fresh.
            var reading = ClaudeStatusParser.Parse(text, new DateTimeOffset(info.LastWriteTimeUtc));
            if (reading is null)
            {
                if (!_haveReading) HintChanged?.Invoke(NoReadingHint);
                return;   // keep the last reading
            }
            _haveReading = true;
            ReadingAvailable?.Invoke(reading);
            HintChanged?.Invoke(null);
        }
        catch
        {
            // Locked or mid-swap: the next change event or refresh tries again.
        }
    }
}
