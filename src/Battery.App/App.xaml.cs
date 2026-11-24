using System.Windows;
using System.Windows.Threading;
using Battery.App.Native;
using Battery.Core;
using Microsoft.Win32;

namespace Battery.App;

public partial class App : Application
{
    static readonly TimeSpan PlaceEvery = TimeSpan.FromSeconds(2);

    Mutex? _singleInstance;
    Settings _settings = new();
    StripWindow _strip = null!;
    SystemThemeWatcher _themeWatcher = null!;
    DispatcherTimer _placeTimer = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Battery.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _settings = Settings.Load();
        _themeWatcher = new SystemThemeWatcher();
        _themeWatcher.Changed += _ => RenderNow();

        _strip = new StripWindow();
        _strip.DragFinished += OnDragFinished;
        _strip.DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        RenderNow();
        _strip.Show();
        Place();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _placeTimer = new DispatcherTimer(PlaceEvery, DispatcherPriority.Background, (_, _) => Place(), Dispatcher);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _placeTimer?.Stop();
        _themeWatcher?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Place);

    void RenderNow() => _strip.Render(SampleApps(), Theme.For(_themeWatcher.IsLight));

    /// <summary>Puts the strip on the taskbar, re-asserts topmost, and hides it when it should not show.</summary>
    void Place()
    {
        if (_strip.IsDragging) return;

        if (Taskbar.IsFullScreenAppActive(_strip.Handle) || Taskbar.IsAutoHiddenNow())
        {
            if (_strip.IsVisible) _strip.Hide();
            return;
        }
        if (!_strip.IsVisible) _strip.Show();

        var size = _strip.PixelRect;
        var scale = _strip.DpiScale;
        var hasTaskbar = Taskbar.TryGetRects(out var taskbar, out var tray);
        var target = StripPlacement.Compute(hasTaskbar ? taskbar : null, tray, Taskbar.PrimaryScreen(),
            size.Width, size.Height, _settings.OffsetFromTray * scale);

        _strip.DragBounds = hasTaskbar ? (taskbar.Left, taskbar.Right) : null;
        _strip.MoveToPixels(target.Left, target.Top);
    }

    void OnDragFinished(double leftPx)
    {
        if (Taskbar.TryGetRects(out var taskbar, out var tray))
        {
            var size = _strip.PixelRect;
            var anchor = tray?.Left ?? taskbar.Right;
            _settings.OffsetFromTray = (anchor - (leftPx + size.Width)) / _strip.DpiScale;
            _settings.Save();
        }
        Place();
    }

    /// <summary>Fixed bars matching the mockup; replaced by live readings in Task 8.</summary>
    static IReadOnlyList<AppView> SampleApps()
    {
        static BarView Bar(WindowKind kind, double used, double clock) =>
            new(kind, used, clock, PaceCalculator.StateFor(used, clock), false, false, null);

        static AppView App(string id, string name, (double Used, double Clock) five, (double Used, double Clock) week)
        {
            var f = Bar(WindowKind.FiveHour, five.Used, five.Clock);
            var w = Bar(WindowKind.Weekly, week.Used, week.Clock);
            return new AppView(id, name, f, w, new[] { f, w }, null, null);
        }

        return new[]
        {
            App(ProviderIds.Claude, "Claude", (38, 55), (66, 55)),
            App(ProviderIds.Codex, "Codex", (82, 60), (34, 42)),
            App(ProviderIds.OpenCode, "OpenCode Go", (12, 19), (26, 29)),
        };
    }
}
