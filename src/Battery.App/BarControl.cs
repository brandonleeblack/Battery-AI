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
    readonly BarStyle _style;
    readonly double _radius;
    readonly double _overshoot;

    public BarControl(BarView bar, Theme theme, BarStyle style, double width, double height, double radius, double overshoot)
    {
        _bar = bar;
        _theme = theme;
        _style = style;
        _radius = radius;
        _overshoot = overshoot;
        Width = width;
        Height = height;
        Opacity = bar.Stale ? 0.5 : 1.0;
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
    }

    public static BarControl ForStrip(BarView bar, Theme theme, BarStyle style) =>
        new(bar, theme, style, StripWidth, StripHeight, StripRadius, StripOvershoot);

    public static BarControl ForPanel(BarView bar, Theme theme, BarStyle style) =>
        new(bar, theme, style, PanelWidth, PanelHeight, PanelRadius, PanelOvershoot);

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

        var fillWidth = w * Math.Clamp(_bar.FillPercent, 0, 100) / 100;
        var fill = Paint.FromHex(_theme.FillFor(_bar.State));
        switch (_style)
        {
            case BarStyle.Gel: DrawGel(dc, w, h, fillWidth, fill.Color); break;
            case BarStyle.Cells: DrawCells(dc, w, h, fillWidth, fill); break;
            default: DrawClean(dc, w, h, fillWidth, fill); break;
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

    void DrawClean(DrawingContext dc, double w, double h, double fillWidth, Brush fill)
    {
        dc.DrawRoundedRectangle(Paint.FromHex(_theme.Track), null, new Rect(0, 0, w, h), _radius, _radius);
        if (fillWidth <= 0) return;
        var r = Math.Min(_radius, fillWidth / 2);
        dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, fillWidth, h), r, r);
    }

    /// <summary>A groove pressed into the glass, holding a fill lit along its top edge like a filled tube.</summary>
    void DrawGel(DrawingContext dc, double w, double h, double fillWidth, Color fill)
    {
        var groove = new RectangleGeometry(new Rect(0, 0, w, h), _radius, _radius);
        dc.PushClip(groove);
        dc.DrawRectangle(Paint.FromHex(_theme.GrooveTrack), null, new Rect(0, 0, w, h));
        if (fillWidth > 0)
        {
            var r = Math.Min(_radius, fillWidth / 2);
            dc.DrawRoundedRectangle(GelBrush(fill), null, new Rect(0, 0, fillWidth, h), r, r);
        }
        // Shadow along the groove's top inside edge, over the fill too: the fill sits inside the groove.
        dc.DrawRectangle(Paint.FromHex(_theme.GrooveShade), null, new Rect(0, 0, w, 1));
        dc.Pop();
    }

    /// <summary>Battery cells: track and fill drawn only inside equal segments.</summary>
    void DrawCells(DrawingContext dc, double w, double h, double fillWidth, Brush fill)
    {
        var cells = new GeometryGroup();
        foreach (var (x, cw) in BarCells.Layout(w, BarCells.CountFor(w), BarCells.Gap))
            cells.Children.Add(new RectangleGeometry(new Rect(x, 0, cw, h), 1, 1));
        cells.Freeze();
        dc.PushClip(cells);
        dc.DrawRectangle(Paint.FromHex(_theme.Track), null, new Rect(0, 0, w, h));
        if (fillWidth > 0) dc.DrawRectangle(fill, null, new Rect(0, 0, fillWidth, h));
        dc.Pop();
    }

    static readonly Dictionary<Color, Brush> GelBrushes = new();

    static Brush GelBrush(Color c)
    {
        if (GelBrushes.TryGetValue(c, out var brush)) return brush;
        static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        gradient.GradientStops.Add(new GradientStop(Mix(c, Colors.White, 0.45), 0));
        gradient.GradientStops.Add(new GradientStop(c, 0.45));
        gradient.GradientStops.Add(new GradientStop(Mix(c, Colors.Black, 0.15), 1));
        gradient.Freeze();
        GelBrushes[c] = gradient;
        return gradient;
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
