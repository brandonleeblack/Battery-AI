using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Battery.App;

/// <summary>
/// Follows the Windows mode that colours the taskbar (SystemUsesLightTheme), not the separate app mode.
/// Must be created on the UI thread; Changed is raised on it.
/// </summary>
public sealed class SystemThemeWatcher : IDisposable
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string ValueName = "SystemUsesLightTheme";
    const int WM_SETTINGCHANGE = 0x001A;

    readonly HwndSource _source;
    readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public SystemThemeWatcher()
    {
        IsLight = ReadIsLight();
        // WM_SETTINGCHANGE is broadcast to top-level windows only, so this is a hidden top-level
        // window rather than a message-only one.
        _source = new HwndSource(new HwndSourceParameters("Battery.ThemeWatcher")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000),   // WS_POPUP, never shown
            ExtendedWindowStyle = 0x00000080,            // WS_EX_TOOLWINDOW: no taskbar button
        });
        _source.AddHook(WndProc);
    }

    public bool IsLight { get; private set; }

    public event Action<bool>? Changed;

    /// <summary>Missing key or value means dark.</summary>
    public static bool ReadIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(ValueName) is int v && v != 0;
        }
        catch
        {
            return false;
        }
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE && lParam != IntPtr.Zero
            && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
        {
            // Let the registry write settle before reading it back.
            _dispatcher.BeginInvoke(DispatcherPriority.Background, Recheck);
        }
        return IntPtr.Zero;
    }

    void Recheck()
    {
        var now = ReadIsLight();
        if (now == IsLight) return;
        IsLight = now;
        Changed?.Invoke(now);
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
