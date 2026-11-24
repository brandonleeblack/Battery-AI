using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace Battery.App;

/// <summary>
/// One hidden WebView2 on Battery's private profile (%AppData%\Battery\WebView2), shared by the web
/// sources. A read loads a page the user can open when signed in there, and captures the response to
/// the usage request that page makes for itself. Battery never sees a password, never reads cookies,
/// and never touches the main browser. Use on the UI thread; reads are serialised.
/// </summary>
public sealed class WebUsageReader : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>One environment per process for the shared profile folder (WebView2 requires matching options).</summary>
    public static Task<CoreWebView2Environment> Environment => LazyEnvironment.Value;

    static readonly Lazy<Task<CoreWebView2Environment>> LazyEnvironment = new(() =>
    {
        Directory.CreateDirectory(AppPaths.WebView2Dir);
        return CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: AppPaths.WebView2Dir);
    });

    readonly SemaphoreSlim _gate = new(1, 1);
    HwndSource? _host;
    CoreWebView2Controller? _controller;

    public enum Outcome { Ok, SignedOut, Failed, NoWebView }

    public sealed record Result(Outcome Outcome, int Status, string? Body);

    /// <param name="source">Short name for the debug log ("claude", "opencode").</param>
    /// <param name="pageUrl">The page to load.</param>
    /// <param name="isUsageRequest">Which of the page's requests carries the usage.</param>
    /// <param name="isSignInPage">True when loading ended on a sign-in page instead.</param>
    public async Task<Result> ReadAsync(string source, string pageUrl, Func<string, bool> isUsageRequest, Func<string?, bool> isSignInPage)
    {
        await _gate.WaitAsync();
        try
        {
            var core = await EnsureCoreAsync();
            if (core is null) return new Result(Outcome.NoWebView, 0, null);

            var (status, body) = await FetchAsync(source, core, pageUrl, isUsageRequest, isSignInPage);
            Debug(source, $"status={status} bodyLength={body?.Length ?? -1} " +
                          $"first={(body is { Length: > 0 } ? body.TrimStart()[0] : '-')} source={SafePath(core.Source)}");
            var outcome = status switch
            {
                401 or 403 => Outcome.SignedOut,
                >= 200 and < 300 when body is not null => Outcome.Ok,
                _ => Outcome.Failed,
            };
            return new Result(outcome, status, body);
        }
        catch (Exception e)
        {
            Debug(source, "exception " + e.GetType().Name);
            return new Result(Outcome.Failed, 0, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _controller?.Close();
        _controller = null;
        _host?.Dispose();
        _host = null;
    }

    async Task<CoreWebView2?> EnsureCoreAsync()
    {
        if (_controller is not null) return _controller.CoreWebView2;
        try
        {
            // A never-shown window to parent the browser; nothing is ever displayed.
            _host ??= new HwndSource(new HwndSourceParameters("Battery.WebReader")
            {
                Width = 1024,
                Height = 768,
                WindowStyle = unchecked((int)0x80000000),        // WS_POPUP
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
    /// (HTTP status, body) of the page's usage request; (401, null) when the page sent us to sign in;
    /// (0, null) on a network failure or timeout. Only events of this read's own navigation count, so the
    /// previous read's about:blank being cancelled is never taken for a failure.
    /// </summary>
    static async Task<(int Status, string? Body)> FetchAsync(string source, CoreWebView2 core, string pageUrl,
        Func<string, bool> isUsageRequest, Func<string?, bool> isSignInPage)
    {
        var done = new TaskCompletionSource<(int, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong? ourNavigation = null;

        void OnStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            // Redirects keep the navigation id, so the first start after our Navigate is ours throughout.
            if (ourNavigation is null && e.Uri.StartsWith(pageUrl, StringComparison.OrdinalIgnoreCase))
                ourNavigation = e.NavigationId;
        }

        async void OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            try
            {
                if (!isUsageRequest(e.Request.Uri)) return;
                var status = e.Response.StatusCode;
                Debug(source, $"response-event status={status}");
                try
                {
                    using var stream = await e.Response.GetContentAsync();
                    using var reader = new StreamReader(stream);
                    done.TrySetResult((status, await reader.ReadToEndAsync()));
                }
                catch (Exception ex)
                {
                    Debug(source, "get-content failed " + ex.GetType().Name);
                    done.TrySetResult((status, null));
                }
            }
            catch
            {
                // An event for a request that went away; nothing to read.
            }
        }

        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (ourNavigation is null || e.NavigationId != ourNavigation) return;
            Debug(source, $"nav-completed ok={e.IsSuccess} web={e.WebErrorStatus} http={e.HttpStatusCode} source={SafePath(core.Source)}");
            if (!e.IsSuccess) done.TrySetResult((0, null));   // offline, DNS failure, server error page
            else if (isSignInPage(core.Source)) done.TrySetResult((401, null));
            // Otherwise the page is loading; its usage request arrives through OnResponse.
        }

        core.NavigationStarting += OnStarting;
        core.WebResourceResponseReceived += OnResponse;
        core.NavigationCompleted += OnCompleted;
        try
        {
            core.Navigate(pageUrl);
            var finished = await Task.WhenAny(done.Task, Task.Delay(Timeout));
            return finished == done.Task ? done.Task.Result : (0, null);
        }
        finally
        {
            core.NavigationStarting -= OnStarting;
            core.WebResourceResponseReceived -= OnResponse;
            core.NavigationCompleted -= OnCompleted;
            // Leave the page so its scripts are not left running in the background between reads.
            try { core.Navigate("about:blank"); } catch { }
        }
    }

    /// <summary>
    /// Set BATTERY_DEBUG=1 to append one line per read to %AppData%\Battery\web-debug.log: status, body
    /// length, first character, and host and path with id-like segments blanked. Never a body or query string.
    /// </summary>
    static void Debug(string source, string line)
    {
        if (System.Environment.GetEnvironmentVariable("BATTERY_DEBUG") != "1") return;
        try
        {
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "web-debug.log"), $"{DateTimeOffset.Now:O} {source} {line}\n");
        }
        catch { }
    }

    static string SafePath(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
            ? u.Host + Regex.Replace(u.AbsolutePath,
                @"/([a-z]+_[A-Za-z0-9]{10,}|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", "/<id>")
            : "-";
}
