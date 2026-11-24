using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Battery.App.Native;

/// <summary>
/// Blur behind a transparent WPF window (the hover panel), the way the taskbar and Start blur. Uses the
/// accent policy Windows has exposed since Windows 10: it works on a window that is never activated,
/// where the newer backdrop API falls back to a solid colour. The tint itself is drawn by WPF on top.
/// </summary>
static class Acrylic
{
    /// <summary>True when Windows "Transparency effects" is on (Settings › Personalisation › Colours).</summary>
    public static bool TransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Turns the blur on. False when Windows refused it or transparency effects are off.</summary>
    public static bool TryEnable(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !TransparencyEnabled()) return false;
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentEnableAcrylicBlurBehind,
                AccentFlags = 0,
                GradientColor = 0x01000000,   // a zero alpha makes the blur opaque black; the tint is WPF's
            };
            var size = Marshal.SizeOf<AccentPolicy>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new CompositionAttributeData { Attribute = WcaAccentPolicy, Data = ptr, SizeOfData = size };
                return SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// The blur covers the window's whole rectangle; clip the window to its rounded shape so no blurred
    /// corners show outside it. Call again whenever the window changes size.
    /// </summary>
    public static void RoundCorners(IntPtr hwnd, double radiusPx)
    {
        if (!WindowStyles.TryGetRect(hwnd, out var r) || r.Width < 1 || r.Height < 1) return;
        var d = (int)Math.Round(radiusPx * 2);
        var region = CreateRoundRectRgn(0, 0, (int)Math.Round(r.Width) + 1, (int)Math.Round(r.Height) + 1, d, d);
        if (region != IntPtr.Zero && SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region);   // on success Windows owns it
    }

    const int WcaAccentPolicy = 19;
    const int AccentEnableAcrylicBlurBehind = 4;

    [StructLayout(LayoutKind.Sequential)]
    struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionAttributeData data);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
}
