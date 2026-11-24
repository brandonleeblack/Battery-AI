using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Battery.Core;
using Battery.Core.Providers;
using Microsoft.Web.WebView2.Core;

namespace Battery.App.Providers;

/// <summary>
/// Reads OpenCode Go usage by loading the console's own usage JSON in a hidden WebView2 that uses
/// Battery's private profile (%AppData%\Battery\WebView2), where the user signed in through
/// OpenCodeSignInWindow. Battery never sees a password and never reads the main browser.
/// Create and use on the UI thread. Never throws out of its timer.
/// </summary>
public sealed class OpenCodeProvider : IUsageProvider
{
    public const string SignInHint = "Right-click › Sign in to OpenCode";
    public const string SignInAgainHint = "Sign in again (right-click › Sign in to OpenCode)";
    public const string UnreachableHint = "OpenCode page changed or unreachable";
    public const string NoWebViewHint = "Needs the Microsoft Edge WebView2 Runtime";

    static readonly TimeSpan PollEvery = TimeSpan.FromMinutes(5);
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    readonly Func<bool> _hadReadingBefore;
    DispatcherTimer? _timer;
    HwndSource? _host;
    CoreWebView2Controller? _controller;
    bool _busy;

    /// <param name="hadReadingBefore">True when a reading was ever stored, so "signed out" means the session expired.</param>
    public OpenCodeProvider(Func<bool> hadReadingBefore) => _hadReadingBefore = hadReadingBefore;

    public string Id => ProviderIds.OpenCode;
    public event Action<ProviderReading>? ReadingAvailable;
    public event Action<string?>? HintChanged;

    /// <summary>One environment per process for the shared profile folder (WebView2 requires matching options).</summary>
    public static Task<CoreWebView2Environment> Environment => LazyEnvironment.Value;

    static readonly Lazy<Task<CoreWebView2Environment>> LazyEnvironment = new(() =>
    {
        Directory.CreateDirectory(AppPaths.WebView2Dir);
        return CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: AppPaths.WebView2Dir);
    });

    public void Start()
    {
        _timer ??= new DispatcherTimer(PollEvery, DispatcherPriority.Background, async (_, _) => await RefreshAsync(), _dispatcher);
        _dispatcher.BeginInvoke(async () => await RefreshAsync());
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _controller?.Close();
        _controller = null;
        _host?.Dispose();
        _host = null;
    }

    public Task RefreshAsync() => _dispatcher.InvokeAsync(ReadOnceAsync).Task.Unwrap();

    async Task ReadOnceAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var core = await EnsureCoreAsync();
            if (core is null)
            {
                HintChanged?.Invoke(NoWebViewHint);
                return;
            }

            var (status, body) = await FetchAsync(core);
            Debug($"status={status} bodyLength={body?.Length ?? -1} first={(body is { Length: > 0 } ? body.TrimStart()[0] : '-')} " +
                  $"source={SafePath(core.Source)}");
            var result = status is 401 or 403
                ? new OpenCodeParseResult(OpenCodeParseStatus.SignedOut, null)
                : OpenCodeUsageParser.Parse(body ?? "", DateTimeOffset.Now);

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
        catch (Exception e)
        {
            Debug("exception " + e.GetType().Name);
            HintChanged?.Invoke(UnreachableHint);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Set BATTERY_DEBUG=1 to append one line per read to %AppData%\Battery\opencode-debug.log:
    /// status, body length, first character, and the host and path loaded. Never the body or a query string.
    /// </summary>
    static void Debug(string line)
    {
        if (System.Environment.GetEnvironmentVariable("BATTERY_DEBUG") != "1") return;
        try
        {
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "opencode-debug.log"), $"{DateTimeOffset.Now:O} {line}\n");
        }
        catch { }
    }

    /// <summary>Host and path with account-like segments (org_..., user_...) blanked out.</summary>
    static string SafePath(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
            ? u.Host + System.Text.RegularExpressions.Regex.Replace(u.AbsolutePath, @"/[a-z]+_[A-Za-z0-9]{10,}", "/<id>")
            : "-";

    async Task<CoreWebView2?> EnsureCoreAsync()
    {
        if (_controller is not null) return _controller.CoreWebView2;
        try
        {
            // A never-shown window to parent the browser; nothing is ever displayed.
            _host ??= new HwndSource(new HwndSourceParameters("Battery.OpenCodeReader")
            {
                Width = 800,
                Height = 600,
                WindowStyle = unchecked((int)0x80000000),   // WS_POPUP
                ExtendedWindowStyle = 0x00000080 | 0x08000000,   // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            });
            var env = await Environment;
            _controller = await env.CreateCoreWebView2ControllerAsync(_host.Handle);
            _controller.IsVisible = false;
            var settings = _controller.CoreWebView2.Settings;
            settings.AreDefaultScriptDialogsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreDevToolsEnabled = false;
            return _controller.CoreWebView2;
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or FileNotFoundException or COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads the console and captures the usage JSON the console page fetches for itself:
    /// (HTTP status, body). (401, null) when the console sends us to its sign-in page; (0, null) on a
    /// network failure or timeout. Requesting the JSON URL directly does not work (HTTP 400): the
    /// endpoint needs what the console's own request carries.
    /// </summary>
    async Task<(int Status, string? Body)> FetchAsync(CoreWebView2 core)
    {
        var done = new TaskCompletionSource<(int, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);

        async void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            if (!OpenCodeUsageParser.IsUsageRequest(e.Request.Uri)) return;
            var status = e.Response.StatusCode;
            Debug($"response-event status={status}");
            try
            {
                using var stream = await e.Response.GetContentAsync();
                using var reader = new StreamReader(stream);
                done.TrySetResult((status, await reader.ReadToEndAsync()));
            }
            catch (Exception ex)
            {
                Debug("get-content failed " + ex.GetType().Name);
                done.TrySetResult((status, null));
            }
        }

        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            Debug($"nav-completed ok={e.IsSuccess} web={e.WebErrorStatus} http={e.HttpStatusCode} source={SafePath(core.Source)}");
            if (!e.IsSuccess) done.TrySetResult((0, null));   // offline, DNS failure, server error page
            else if (OpenCodeUsageParser.IsSignInPage(core.Source)) done.TrySetResult((401, null));
            // Otherwise the console is loading; its usage request arrives through OnResponse.
        }

        core.WebResourceResponseReceived += OnResponse;
        core.NavigationCompleted += OnCompleted;
        try
        {
            core.Navigate(OpenCodeUsageParser.ConsoleUrl);
            var finished = await Task.WhenAny(done.Task, Task.Delay(Timeout));
            return finished == done.Task ? done.Task.Result : (0, null);
        }
        finally
        {
            core.WebResourceResponseReceived -= OnResponse;
            core.NavigationCompleted -= OnCompleted;
            // Leave the console so its scripts are not left running in the background between reads.
            try { core.Navigate("about:blank"); } catch { }
        }
    }

}
