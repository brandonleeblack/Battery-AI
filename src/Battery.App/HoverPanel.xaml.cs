using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Battery.App.Native;
using Battery.Core;

namespace Battery.App;

/// <summary>The detail panel shown above the strip while the pointer is over it. Draws only.</summary>
public partial class HoverPanel : Window
{
    const double SectionGap = 12, RowGap = 6, CellGap = 10;
    const double LabelWidth = 40, PercentWidth = 32;

    IntPtr _hwnd;

    public HoverPanel()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WindowStyles.MakeToolWindowNoActivate(_hwnd);
        };
    }

    public RectD PixelRect => WindowStyles.TryGetRect(_hwnd, out var r) ? r : default;

    public void MoveToPixels(double x, double y) =>
        WindowStyles.MoveTopmost(_hwnd, (int)Math.Round(x), (int)Math.Round(y));

    public void Render(IReadOnlyList<AppView> apps, Theme theme, DateTimeOffset now)
    {
        var text = Paint.FromHex(theme.PanelText);
        var secondary = Paint.FromHex(theme.SecondaryText);

        Box.Background = Paint.FromHex(theme.PanelBackground);
        Box.BorderBrush = Paint.FromHex(theme.PanelBorder);
        Foreground = text;

        Sections.Children.Clear();
        Sections.Children.Add(Legend(theme, secondary));
        foreach (var app in apps)
        {
            var section = new StackPanel { Margin = new Thickness(0, SectionGap, 0, 0) };

            var name = Row();
            name.Children.Add(new Ellipse
            {
                Width = 8, Height = 8, Fill = Paint.FromHex(Theme.DotFor(app.ProviderId)),
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            });
            name.Children.Add(new TextBlock { Text = app.DisplayName, FontWeight = FontWeights.SemiBold, Foreground = text });
            section.Children.Add(name);

            foreach (var bar in app.AllWindows)
            {
                var row = WindowRow(bar, theme, text, secondary, now);
                row.Margin = new Thickness(0, RowGap, 0, 0);
                section.Children.Add(row);
            }

            section.Children.Add(new TextBlock
            {
                Text = Footnote(app, now),
                Foreground = secondary,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, RowGap, 0, 0),
            });
            Sections.Children.Add(section);
        }
    }

    /// <summary>The hint when there is one, and how old the reading is when there is a reading.</summary>
    static string Footnote(AppView app, DateTimeOffset now)
    {
        var updated = app.ReadAt is { } readAt ? "updated " + HoverFormat.FormatAge(now - readAt) : null;
        if (app.Hint is not null) return updated is null ? app.Hint : $"{app.Hint} · {updated}";
        return updated ?? "No data yet";
    }

    static StackPanel WindowRow(BarView bar, Theme theme, Brush text, Brush secondary, DateTimeOffset now)
    {
        var row = Row();
        row.Children.Add(new TextBlock { Text = HoverFormat.Label(bar.Kind), Width = LabelWidth, Foreground = secondary });

        var barControl = BarControl.ForPanel(bar, theme);
        barControl.Margin = new Thickness(CellGap, 0, CellGap, 0);
        barControl.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(barControl);

        if (bar.Missing)
        {
            row.Children.Add(new TextBlock { Text = "no data", Foreground = secondary });
            return row;
        }

        var resetPassed = bar.ResetsAt is { } r && r <= now;
        if (resetPassed)
        {
            // The window has rolled over since the last reading; the old figure no longer applies.
            row.Children.Add(new TextBlock { Text = "window reset, no new reading yet", Foreground = secondary });
            return row;
        }

        row.Children.Add(new TextBlock
        {
            Text = Math.Round(bar.FillPercent, MidpointRounding.AwayFromZero) + "%",
            Width = PercentWidth,
            FontWeight = FontWeights.SemiBold,
            Foreground = bar.State == BarState.Normal ? text : Paint.FromHex(theme.FillFor(bar.State)),
            Opacity = bar.Stale ? 0.5 : 1,
        });
        row.Children.Add(new TextBlock
        {
            Text = bar.ResetsAt is { } resetsAt ? "resets " + HoverFormat.FormatReset(resetsAt - now) : "",
            Foreground = secondary,
            Margin = new Thickness(CellGap, 0, 0, 0),
        });
        return row;
    }

    static StackPanel Legend(Theme theme, Brush secondary)
    {
        var legend = Row();
        void Item(FrameworkElement swatch, string label, bool first = false)
        {
            swatch.VerticalAlignment = VerticalAlignment.Center;
            swatch.Margin = new Thickness(first ? 0 : 14, 0, 6, 0);
            legend.Children.Add(swatch);
            legend.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = secondary });
        }
        Rectangle Pill(string colour) => new() { Width = 18, Height = 6, RadiusX = 3, RadiusY = 3, Fill = Paint.FromHex(colour) };

        Item(Pill(theme.Normal), "used", first: true);
        Item(new Rectangle { Width = 2, Height = 12, Fill = Paint.FromHex(theme.ClockLine) }, "clock");
        Item(Pill(theme.Ahead), "ahead");
        Item(Pill(theme.Low), "under 20% left");
        return legend;
    }

    static StackPanel Row() => new() { Orientation = Orientation.Horizontal };
}
