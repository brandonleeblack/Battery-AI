using System.Windows;

namespace Battery.App;

/// <summary>
/// A normal, visible browser window on Battery's own WebView2 profile, opened at a service's site.
/// The user signs in here; Battery reads nothing from this window and stores nothing typed into it.
/// </summary>
public partial class SignInWindow : Window
{
    /// <summary>The address of each page the window shows (address only; nothing on the page is read).</summary>
    public event Action<string>? PageShown;

    public SignInWindow(string serviceName, string startUrl, string? profile)
    {
        InitializeComponent();
        Icon = TrayIcon.WindowIcon();
        Title = $"Sign in to {serviceName} – Battery";
        Blurb.Text = $"Sign in to {serviceName} here. Battery keeps only the sign-in session, in its own private " +
                     "profile; it never sees your password. Close this window once you are signed in.";
        Loaded += async (_, _) =>
        {
            try
            {
                var env = await WebUsageReader.Environment;
                await Web.EnsureCoreWebView2Async(env, WebProfiles.Options(env, profile));
                Web.CoreWebView2.NavigationCompleted += (_, e) => { if (e.IsSuccess) PageShown?.Invoke(Web.CoreWebView2.Source); };
                Web.CoreWebView2.SourceChanged += (_, _) => PageShown?.Invoke(Web.CoreWebView2.Source);   // single-page sites
                Web.CoreWebView2.Navigate(startUrl);
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "The sign-in window could not open a browser:\n\n" + e.Message +
                    "\n\nBattery needs the Microsoft Edge WebView2 Runtime.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        };
        Closed += (_, _) => Web.Dispose();
    }
}
