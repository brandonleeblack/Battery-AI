using System.Windows.Threading;
using Battery.Core;
using Battery.Core.Providers;

namespace Battery.App.Providers;

/// <summary>
/// Reads OpenCode Go usage by loading the console in the shared hidden browser (Battery's own profile,
/// where the user signed in through SignInWindow) and parsing the usage JSON the console fetches.
/// Create and use on the UI thread. Never throws out of its timer.
/// </summary>
public sealed class OpenCodeProvider : IUsageProvider
{
    public const string SignInHint = "Right-click › Sign in to OpenCode";
    public const string SignInAgainHint = "Sign in again (right-click › Sign in to OpenCode)";
    public const string UnreachableHint = "OpenCode page changed or unreachable";
    public const string NoWebViewHint = "Needs the Microsoft Edge WebView2 Runtime";

    static readonly TimeSpan PollEvery = TimeSpan.FromMinutes(5);

    readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    readonly WebUsageReader _reader;
    readonly Func<bool> _hadReadingBefore;
    DispatcherTimer? _timer;

    /// <param name="hadReadingBefore">True when a reading was ever stored, so "signed out" means the session expired.</param>
    public OpenCodeProvider(WebUsageReader reader, Func<bool> hadReadingBefore)
    {
        _reader = reader;
        _hadReadingBefore = hadReadingBefore;
    }

    public string Id => ProviderIds.OpenCode;
    public event Action<ProviderReading>? ReadingAvailable;
    public event Action<string?>? HintChanged;

    public void Start()
    {
        _timer ??= new DispatcherTimer(PollEvery, DispatcherPriority.Background, async (_, _) => await RefreshAsync(), _dispatcher);
        _dispatcher.BeginInvoke(async () => await RefreshAsync());
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public Task RefreshAsync() => _dispatcher.InvokeAsync(ReadOnceAsync).Task.Unwrap();

    async Task ReadOnceAsync()
    {
        try
        {
            var read = await _reader.ReadAsync("opencode", OpenCodeUsageParser.ConsoleUrl,
                OpenCodeUsageParser.IsUsageRequest, OpenCodeUsageParser.IsSignInPage);

            if (read.Outcome == WebUsageReader.Outcome.NoWebView)
            {
                HintChanged?.Invoke(NoWebViewHint);
                return;
            }

            var result = read.Outcome == WebUsageReader.Outcome.SignedOut
                ? new OpenCodeParseResult(OpenCodeParseStatus.SignedOut, null)
                : OpenCodeUsageParser.Parse(read.Body ?? "", DateTimeOffset.Now);

            switch (result.Status)
            {
                case OpenCodeParseStatus.Ok:
                    ReadingAvailable?.Invoke(result.Reading!);
                    HintChanged?.Invoke(null);
                    break;
                case OpenCodeParseStatus.SignedOut:
                    HintChanged?.Invoke(_hadReadingBefore() ? SignInAgainHint : SignInHint);
                    break;
                default:
                    HintChanged?.Invoke(UnreachableHint);
                    break;
            }
        }
        catch
        {
            HintChanged?.Invoke(UnreachableHint);
        }
    }
}
