using System.Windows;
using Battery.App.Providers;

namespace Battery.App;

/// <summary>
/// A normal, visible browser window on Battery's own WebView2 profile, opened at the OpenCode console.
/// The user signs in here; Battery reads nothing from this window and stores nothing typed into it.
/// </summary>
public partial class OpenCodeSignInWindow : Window
{
    const string StartUrl = "https://opencode.ai/console";

    public OpenCodeSignInWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                await Web.EnsureCoreWebView2Async(await OpenCodeProvider.Environment);
                Web.CoreWebView2.Navigate(StartUrl);
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
