using System.Windows.Threading;
using Battery.Core;
using Battery.Core.Providers;

namespace Battery.App.Providers;

/// <summary>
/// A usage source read from a website the user signed in to inside Battery's own browser (claude.ai,
/// the OpenCode console). Each site has its own profile. Every 5 minutes, while connected, it loads the
/// site's usage page in the hidden browser and parses the usage JSON the page itself fetches. Battery
/// never sees a password and never reads cookies. Use on the UI thread; never throws out of its timer.
/// </summary>
public sealed class WebSignInProvider : IUsageProvider
{
    public const string UnreachableHintFormat = "Couldn't read {0}, trying again in 5 min";
    public const string NoWebViewHint = "Needs the Microsoft Edge WebView2 Runtime";

    public enum ParseStatus { Ok, SignedOut, Unrecognised }

    static readonly TimeSpan PollEvery = TimeSpan.FromMinutes(5);
    static readonly TimeSpan JustRead = TimeSpan.FromSeconds(30);

    readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    readonly WebUsageReader _reader;
    readonly string _source;
    readonly string _siteName;
    readonly Func<string, DateTimeOffset, (ParseStatus, ProviderReading?)> _parse;
    readonly Func<string, bool> _isUsageRequest;
    readonly Func<string?, bool> _isSignInPage;
    readonly Func<bool> _isConnected;
    DispatcherTimer? _timer;
    DateTimeOffset _lastRead = DateTimeOffset.MinValue;

    WebSignInProvider(string id, string serviceName, string siteName, string source, string? profile, string pageUrl,
        WebUsageReader reader, Func<bool> isConnected,
        Func<string, DateTimeOffset, (ParseStatus, ProviderReading?)> parse,
        Func<string, bool> isUsageRequest, Func<string?, bool> isSignInPage)
    {
        Id = id;
        ServiceName = serviceName;
        _siteName = siteName;
        _source = source;
        Profile = profile;
        PageUrl = pageUrl;
        _reader = reader;
        _isConnected = isConnected;
        _parse = parse;
        _isUsageRequest = isUsageRequest;
        _isSignInPage = isSignInPage;
    }

    /// <summary>claude.ai, account-wide: Desktop, web and Claude Code share one allowance.</summary>
    public static WebSignInProvider Claude(WebUsageReader reader, Func<bool> isConnected) => new(
        ProviderIds.Claude, "Claude", "claude.ai", "claude", WebProfiles.Claude, ClaudeWebUsageParser.UsagePageUrl, reader, isConnected,
        (body, now) =>
        {
            var r = ClaudeWebUsageParser.Parse(body, now);
            return (r.Status switch
            {
                ClaudeWebParseStatus.Ok => ParseStatus.Ok,
                ClaudeWebParseStatus.SignedOut => ParseStatus.SignedOut,
                _ => ParseStatus.Unrecognised,
            }, r.Reading);
        },
        ClaudeWebUsageParser.IsUsageRequest, ClaudeWebUsageParser.IsSignInPage);

    /// <summary>The OpenCode console's Go usage.</summary>
    public static WebSignInProvider OpenCode(WebUsageReader reader, Func<bool> isConnected) => new(
        ProviderIds.OpenCode, "OpenCode", "OpenCode", "opencode", WebProfiles.OpenCode, OpenCodeUsageParser.ConsoleUrl, reader, isConnected,
        (body, now) =>
        {
            var r = OpenCodeUsageParser.Parse(body, now);
            return (r.Status switch
            {
                OpenCodeParseStatus.Ok => ParseStatus.Ok,
                OpenCodeParseStatus.SignedOut => ParseStatus.SignedOut,
                _ => ParseStatus.Unrecognised,
            }, r.Reading);
        },
        OpenCodeUsageParser.IsUsageRequest, OpenCodeUsageParser.IsSignInPage);

    public string Id { get; }
    public string ServiceName { get; }
    public string? Profile { get; }
    public string PageUrl { get; }

    public event Action<ProviderReading>? ReadingAvailable;
    public event Action<string?>? HintChanged;

    /// <summary>True after a successful read (signed in), false when the site sent Battery to sign in.</summary>
    public event Action<bool>? SignedInChanged;

    /// <summary>Polls every 5 minutes; reads now unless a read just happened (as after a sign-in).</summary>
    public void Start()
    {
        _timer ??= new DispatcherTimer(PollEvery, DispatcherPriority.Background, async (_, _) => await RefreshAsync(), _dispatcher);
        if (DateTimeOffset.Now - _lastRead > JustRead) _dispatcher.BeginInvoke(async () => await RefreshAsync());
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public Task RefreshAsync() => _dispatcher.InvokeAsync(ReadOnceAsync).Task.Unwrap();

    /// <summary>
    /// The sign-in window reached one of the site's own pages (not its sign-in pages): the user has
    /// probably just signed in, so read now to confirm, unless a read is already under way.
    /// </summary>
    public void PageShownInSignInWindow(string url)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase) || _isSignInPage(url)) return;
        if (_reading) return;
        _ = RefreshAsync();
    }

    bool _reading;

    /// <summary>Signs Battery out of this site only (clears this site's profile).</summary>
    public Task<bool> SignOutAsync() => _reader.SignOutAsync(_source, Profile);

    async Task ReadOnceAsync()
    {
        _lastRead = DateTimeOffset.Now;
        _reading = true;
        var connection = AppConnections.For(Id);
        try
        {
            var read = await _reader.ReadAsync(_source, Profile, PageUrl, _isUsageRequest, _isSignInPage);
            if (read.Outcome == WebUsageReader.Outcome.NoWebView)
            {
                HintChanged?.Invoke(NoWebViewHint);
                return;
            }

            var (status, reading) = read.Outcome == WebUsageReader.Outcome.SignedOut
                ? (ParseStatus.SignedOut, null)
                : _parse(read.Body ?? "", DateTimeOffset.Now);

            switch (status)
            {
                case ParseStatus.Ok:
                    ReadingAvailable?.Invoke(reading!);
                    HintChanged?.Invoke(null);
                    SignedInChanged?.Invoke(true);
                    break;
                case ParseStatus.SignedOut:
                    // A lapsed session reads "signed out"; a sign-in window closed without signing in does not.
                    HintChanged?.Invoke(_isConnected() ? connection.SignedOutHint : connection.NotConnectedHint);
                    SignedInChanged?.Invoke(false);
                    break;
                default:
                    HintChanged?.Invoke(string.Format(UnreachableHintFormat, _siteName));
                    break;
            }
        }
        catch
        {
            HintChanged?.Invoke(string.Format(UnreachableHintFormat, _siteName));
        }
        finally
        {
            _reading = false;
        }
    }
}
