namespace Battery.Core;

public readonly record struct RectD(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public bool Intersects(RectD other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
}

/// <summary>Pure geometry for where the strip goes. All values are in one unit (physical pixels in the app).</summary>
public static class StripPlacement
{
    public const double DefaultGap = 8;

    /// <summary>
    /// Default: the strip's right edge <see cref="DefaultGap"/> left of the tray's left edge, vertically
    /// centred in the taskbar; <paramref name="savedOffsetFromTray"/> replaces the gap. The result is clamped
    /// inside the taskbar horizontally. With no usable horizontal taskbar on the primary screen the strip
    /// goes to the bottom-right of the primary screen and never off it.
    /// </summary>
    public static RectD Compute(RectD? taskbar, RectD? tray, RectD primaryScreen,
        double stripWidth, double stripHeight, double? savedOffsetFromTray)
    {
        if (taskbar is not { } bar || !IsUsable(bar, primaryScreen))
            return BottomRight(primaryScreen, stripWidth, stripHeight);

        // A tray rectangle that is empty or outside the taskbar is not trustworthy: anchor to the taskbar's end.
        var anchorRight = tray is { } t && t.Width > 0 && t.Intersects(bar) ? t.Left : bar.Right;
        var offset = savedOffsetFromTray ?? DefaultGap;

        var left = ClampInto(anchorRight - offset - stripWidth, bar.Left, bar.Right - stripWidth);
        var top = bar.Top + (bar.Height - stripHeight) / 2;
        return new RectD(left, top, stripWidth, stripHeight);
    }

    static bool IsUsable(RectD bar, RectD screen) =>
        bar.Width > 0 && bar.Height > 0
        && bar.Width >= bar.Height            // a vertical taskbar cannot hold a horizontal strip
        && bar.Intersects(screen);

    static RectD BottomRight(RectD screen, double w, double h) => new(
        ClampInto(screen.Right - DefaultGap - w, screen.Left, screen.Right - w),
        ClampInto(screen.Bottom - DefaultGap - h, screen.Top, screen.Bottom - h),
        w, h);

    /// <summary>Clamp where the minimum wins when the range is inverted (content larger than container).</summary>
    static double ClampInto(double value, double min, double max) => Math.Max(min, Math.Min(value, max));
}
