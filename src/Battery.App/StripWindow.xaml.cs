using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Battery.App.Native;
using Battery.Core;

namespace Battery.App;

/// <summary>The strip. It only draws what it is given and reports drags; placement is decided by App.</summary>
public partial class StripWindow : Window
{
    const double DotSize = 6, DotToBars = 4, BarGap = 4, GroupGap = 10;
    const int DragThresholdPx = 3;

    IntPtr _hwnd;
    bool _pressed;
    int _dragStartCursorX;
    double _dragStartLeft;

    public StripWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WindowStyles.MakeToolWindowNoActivate(_hwnd);
        };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        LostMouseCapture += (_, _) => FinishDrag();
    }

    public IntPtr Handle => _hwnd;

    /// <summary>True from the first real movement of a drag until the button is released.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>Horizontal limits for dragging, in physical pixels (the taskbar's left and right).</summary>
    public (double Left, double Right)? DragBounds { get; set; }

    /// <summary>A drag ended; the argument is the strip's new left edge in physical pixels.</summary>
    public event Action<double>? DragFinished;

    public double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Current window rectangle in physical pixels.</summary>
    public RectD PixelRect => WindowStyles.TryGetRect(_hwnd, out var r) ? r : default;

    public void MoveToPixels(double x, double y) =>
        WindowStyles.MoveTopmost(_hwnd, (int)Math.Round(x), (int)Math.Round(y));

    public void Render(IReadOnlyList<AppView> apps, Theme theme)
    {
        Box.Background = Paint.FromHex(theme.Box);
        // The Daylight border is drawn inside the box so both themes have the same outer size.
        Box.BorderThickness = new Thickness(theme.BoxBorder is null ? 0 : 1);
        Box.BorderBrush = theme.BoxBorder is null ? null : Paint.FromHex(theme.BoxBorder);
        Box.Padding = theme.BoxBorder is null ? new Thickness(10, 8, 10, 8) : new Thickness(9, 7, 9, 7);

        Groups.Children.Clear();
        foreach (var app in apps)
        {
            var bars = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            bars.Children.Add(BarControl.ForStrip(app.FiveHour, theme));
            var weekly = BarControl.ForStrip(app.Weekly, theme);
            weekly.Margin = new Thickness(0, BarGap, 0, 0);
            bars.Children.Add(weekly);

            var group = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(Groups.Children.Count == 0 ? 0 : GroupGap, 0, 0, 0),
            };
            group.Children.Add(new Ellipse
            {
                Width = DotSize,
                Height = DotSize,
                Fill = Paint.FromHex(Theme.DotFor(app.ProviderId)),
                Margin = new Thickness(0, 0, DotToBars, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            group.Children.Add(bars);
            Groups.Children.Add(group);
        }
    }

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _dragStartCursorX = WindowStyles.CursorPosition().X;
        _dragStartLeft = PixelRect.Left;
        CaptureMouse();
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        var dx = WindowStyles.CursorPosition().X - _dragStartCursorX;
        if (!IsDragging && Math.Abs(dx) < DragThresholdPx) return;
        IsDragging = true;

        var rect = PixelRect;
        var left = _dragStartLeft + dx;
        if (DragBounds is { } b) left = Math.Max(b.Left, Math.Min(left, b.Right - rect.Width));
        MoveToPixels(left, rect.Top);   // horizontal only: the strip stays on the taskbar
    }

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture(); else FinishDrag();
    }

    void FinishDrag()
    {
        var wasDragging = IsDragging;
        _pressed = false;
        IsDragging = false;
        if (wasDragging) DragFinished?.Invoke(PixelRect.Left);
    }
}
