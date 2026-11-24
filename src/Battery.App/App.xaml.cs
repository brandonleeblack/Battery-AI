using System.Windows;
using System.Windows.Threading;
using Battery.App.Native;
using Battery.App.Providers;
using Battery.Core;
using Battery.Core.Providers;
using Microsoft.Win32;

namespace Battery.App;

public partial class App : Application
{
    static readonly TimeSpan PlaceEvery = TimeSpan.FromSeconds(2);
    static readonly TimeSpan RenderEvery = TimeSpan.FromSeconds(30);

    Mutex? _singleInstance;
    Settings _settings = new();
    StripWindow _strip = null!;
    SystemThemeWatcher _themeWatcher = null!;
    DispatcherTimer _placeTimer = null!;
    DispatcherTimer _renderTimer = null!;
    ReadingStore _store = null!;
    readonly List<IUsageProvider> _providers = new();
    readonly Dictionary<string, string> _hints = new();   // UI thread only

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

        _store = new ReadingStore(AppPaths.ReadingsFile);
        _store.Changed += () => Dispatcher.BeginInvoke(RenderNow);

        _strip = new StripWindow();
        _strip.DragFinished += OnDragFinished;
        _strip.DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        RenderNow();   // last saved readings show immediately
        _strip.Show();
        Place();

        _providers.Add(new CodexProvider());
        foreach (var provider in _providers)
        {
            var id = provider.Id;
            provider.ReadingAvailable += reading => _store.Put(reading);
            provider.HintChanged += hint => Dispatcher.BeginInvoke(() => SetHint(id, hint));
            provider.Start();
        }

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _placeTimer = new DispatcherTimer(PlaceEvery, DispatcherPriority.Background, (_, _) => Place(), Dispatcher);
        // The clock lines move and readings go stale even when nothing new arrives.
        _renderTimer = new DispatcherTimer(RenderEvery, DispatcherPriority.Background, (_, _) => RenderNow(), Dispatcher);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _placeTimer?.Stop();
        _renderTimer?.Stop();
        foreach (var provider in _providers) provider.Stop();
        _themeWatcher?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Place);

    void SetHint(string providerId, string? hint)
    {
        _hints.TryGetValue(providerId, out var current);
        if (current == hint) return;
        if (hint is null) _hints.Remove(providerId); else _hints[providerId] = hint;
        RenderNow();
    }

    void RenderNow()
    {
        var apps = StripModel.Build(_store.Current, _hints, DateTimeOffset.Now);
        _strip.Render(apps, Theme.For(_themeWatcher.IsLight));
    }

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
}
