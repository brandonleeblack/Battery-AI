using System.IO;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Battery.App;

/// <summary>
/// Battery's icon in the notification area. Right-click opens the same menu as the strip; left-click
/// shows the details panel. The tooltip lists each app's 5-hour and weekly use.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon _icon;

    public TrayIcon()
    {
        using var stream = OpenIcon();
        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "Battery",
            Visible = true,
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right) MenuRequested?.Invoke();
            else if (e.Button == Forms.MouseButtons.Left) Clicked?.Invoke();
        };
    }

    public event Action? MenuRequested;
    public event Action? Clicked;

    public void SetTooltip(string text)
    {
        if (_icon.Text != text) _icon.Text = text;
    }

    public void Dispose()
    {
        _icon.Visible = false;   // otherwise the icon lingers in the tray until the pointer passes over it
        _icon.Dispose();
    }

    /// <summary>The same icon as a WPF image, for window title bars.</summary>
    public static BitmapFrame WindowIcon()
    {
        using var stream = OpenIcon();
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    static Stream OpenIcon() =>
        typeof(TrayIcon).Assembly.GetManifestResourceStream("Battery.ico")
        ?? throw new InvalidOperationException("Battery.ico is not embedded.");
}
