using System.Runtime.InteropServices;
using System.Text;
using Battery.Core;

namespace Battery.App.Native;

/// <summary>Win32 lookups about the primary taskbar. Everything is in physical pixels and never throws.</summary>
static class Taskbar
{
    /// <summary>
    /// The primary taskbar (Shell_TrayWnd) and its notification area (child TrayNotifyWnd).
    /// Returns false when there is no taskbar window. <paramref name="tray"/> is null when the
    /// notification area cannot be found; placement then anchors to the taskbar's right end.
    /// </summary>
    public static bool TryGetRects(out RectD taskbar, out RectD? tray)
    {
        taskbar = default;
        tray = null;
        var bar = FindWindow("Shell_TrayWnd", null);
        if (!WindowStyles.TryGetRect(bar, out taskbar)) return false;

        var notify = FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (WindowStyles.TryGetRect(notify, out var t) && t.Width > 0 && t.Height > 0) tray = t;
        return true;
    }

    public static RectD PrimaryScreen() =>
        new(0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

    /// <summary>True when the foreground window covers the whole primary monitor (video, game, slideshow).</summary>
    public static bool IsFullScreenAppActive(IntPtr ownWindow)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == ownWindow) return false;

        var name = new StringBuilder(64);
        GetClassName(fg, name, name.Capacity);
        // The desktop and the shell's own surfaces cover the screen without being full-screen apps.
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
            or "XamlExplorerHostIslandWindow" or "Windows.UI.Core.CoreWindow")
            return false;

        if (!WindowStyles.TryGetRect(fg, out var r)) return false;
        var screen = PrimaryScreen();
        return r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
    }

    /// <summary>True when the taskbar is set to auto-hide and is currently slid off the screen.</summary>
    public static bool IsAutoHiddenNow()
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        var state = SHAppBarMessage(ABM_GETSTATE, ref data).ToInt64();
        if ((state & ABS_AUTOHIDE) == 0) return false;

        if (!TryGetRects(out var bar, out _)) return false;
        var screen = PrimaryScreen();
        var visibleHeight = Math.Min(bar.Bottom, screen.Bottom) - Math.Max(bar.Top, screen.Top);
        var visibleWidth = Math.Min(bar.Right, screen.Right) - Math.Max(bar.Left, screen.Left);
        return visibleHeight < bar.Height / 2 || visibleWidth < bar.Width / 2;
    }

    const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;
    const uint ABM_GETSTATE = 4;
    const long ABS_AUTOHIDE = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public WindowStyles.RECT rc;
        public IntPtr lParam;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    [DllImport("shell32.dll")]
    static extern IntPtr SHAppBarMessage(uint message, ref APPBARDATA data);
}
