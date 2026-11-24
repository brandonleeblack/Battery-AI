using System.Windows;
using System.Windows.Media;
using Battery.Core;

namespace Battery.App;

/// <summary>One bar and its clock line, drawn from a BarView and a Theme. Holds no logic of its own.</summary>
public sealed class BarControl : FrameworkElement
{
    public const double StripWidth = 28, StripHeight = 6, StripRadius = 2, StripOvershoot = 2;
    public const double PanelWidth = 130, PanelHeight = 8, PanelRadius = 4, PanelOvershoot = 3;
    const double ClockWidth = 2;

    readonly BarView _bar;
    readonly Theme _theme;
    readonly double _radius;
    readonly double _overshoot;

    public BarControl(BarView bar, Theme theme, double width, double height, double radius, double overshoot)
    {
        _bar = bar;
        _theme = theme;
        _radius = radius;
        _overshoot = overshoot;
        Width = width;
        Height = height;
        Opacity = bar.Stale ? 0.5 : 1.0;
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
    }

    public static BarControl ForStrip(BarView bar, Theme theme) =>
        new(bar, theme, StripWidth, StripHeight, StripRadius, StripOvershoot);

    public static BarControl ForPanel(BarView bar, Theme theme) =>
        new(bar, theme, PanelWidth, PanelHeight, PanelRadius, PanelOvershoot);

    protected override void OnRender(DrawingContext dc)
    {
        var w = Width;
        var h = Height;

        if (_bar.Missing)
        {
            // Never read: an empty outline, no fill, no clock line.
            var pen = new Pen(Paint.FromHex(_theme.Track), 1);
            dc.DrawRoundedRectangle(null, pen, new Rect(0.5, 0.5, w - 1, h - 1), _radius, _radius);
            return;
        }

        dc.DrawRoundedRectangle(Paint.FromHex(_theme.Track), null, new Rect(0, 0, w, h), _radius, _radius);

        var fillWidth = w * Math.Clamp(_bar.FillPercent, 0, 100) / 100;
        if (fillWidth > 0)
        {
            var r = Math.Min(_radius, fillWidth / 2);
            dc.DrawRoundedRectangle(Paint.FromHex(_theme.FillFor(_bar.State)), null, new Rect(0, 0, fillWidth, h), r, r);
        }

        // After a reset with no new reading, when the next window started is unknown: no clock line.
        if (!_bar.ResetPassed)
        {
            var x = Math.Min(w * Math.Clamp(_bar.ClockPercent, 0, 100) / 100, w - ClockWidth);
            // Land on whole device pixels so the 2 px line stays crisp instead of blurring to 3.
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            x = Math.Round(x * scale) / scale;
            dc.DrawRectangle(Paint.FromHex(_theme.ClockLine), null, new Rect(x, -_overshoot, ClockWidth, h + 2 * _overshoot));
        }
    }
}

static class Paint
{
    static readonly Dictionary<string, SolidColorBrush> Cache = new();

    public static SolidColorBrush FromHex(string hex)
    {
        if (!Cache.TryGetValue(hex, out var brush))
        {
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            Cache[hex] = brush;
        }
        return brush;
    }
}
