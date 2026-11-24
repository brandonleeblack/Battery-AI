using System.Windows.Threading;
using Battery.Core;
using Battery.Core.Providers;

namespace Battery.App.Providers;

/// <summary>
/// Claude's account-wide usage (Desktop, web and Code share one allowance), read from claude.ai's own
/// Settings › Usage page in the shared hidden browser, where the user signed in through SignInWindow.
/// Claude Code's and Claude Desktop's stored sign-ins are never read. Use on the UI thread.
/// </summary>
public sealed class ClaudeWebProvider : IUsageProvider
{
    public const string SignInHint = "Right-click › Claude › Sign in to claude.ai";
    public const string SignInAgainHint = "Sign in again (right-click › Claude › Sign in to claude.ai)";
    public const string UnreachableHint = "Couldn't read claude.ai, trying again in 5 min";
    public const string NoWebViewHint = "Needs the Microsoft Edge WebView2 Runtime";

    static readonly TimeSpan PollEvery = TimeSpan.FromMinutes(5);

    readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    readonly WebUsageReader _reader;
    readonly Func<bool> _wasSignedInBefore;
    DispatcherTimer? _timer;

    /// <param name="wasSignedInBefore">True once this source has produced a reading, so "signed out" means expired.</param>
    public ClaudeWebProvider(WebUsageReader reader, Func<bool> wasSignedInBefore)
    {
        _reader = reader;
        _wasSignedInBefore = wasSignedInBefore;
    }

    public string Id => ProviderIds.Claude;
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
            var read = await _reader.ReadAsync("claude", ClaudeWebUsageParser.UsagePageUrl,
                ClaudeWebUsageParser.IsUsageRequest, ClaudeWebUsageParser.IsSignInPage);
            if (read.Outcome == WebUsageReader.Outcome.NoWebView)
            {
                HintChanged?.Invoke(NoWebViewHint);
                return;
            }

            var result = read.Outcome == WebUsageReader.Outcome.SignedOut
                ? new ClaudeWebParseResult(ClaudeWebParseStatus.SignedOut, null)
                : ClaudeWebUsageParser.Parse(read.Body ?? "", DateTimeOffset.Now);

            switch (result.Status)
            {
                case ClaudeWebParseStatus.Ok:
                    ReadingAvailable?.Invoke(result.Reading!);
                    HintChanged?.Invoke(null);
                    break;
                case ClaudeWebParseStatus.SignedOut:
                    HintChanged?.Invoke(_wasSignedInBefore() ? SignInAgainHint : SignInHint);
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
