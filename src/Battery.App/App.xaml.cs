using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    static readonly TimeSpan HoverShowAfter = TimeSpan.FromMilliseconds(300);
    static readonly TimeSpan HoverHideAfter = TimeSpan.FromMilliseconds(200);
    const double HoverGap = 10;

    Mutex? _singleInstance;
    Settings _settings = new();
    StripWindow _strip = null!;
    HoverPanel _panel = null!;
    SystemThemeWatcher _themeWatcher = null!;
    DispatcherTimer _placeTimer = null!;
    DispatcherTimer _renderTimer = null!;
    DispatcherTimer _hoverShow = null!;
    DispatcherTimer _hoverHide = null!;
    ReadingStore _store = null!;
    readonly List<IUsageProvider> _providers = new();
    readonly Dictionary<IUsageProvider, (bool Reported, string? Hint)> _sourceHints = new();   // UI thread only
    readonly WebUsageReader _webReader = new();

    /// <summary>"Sign in to …" menu items, one per web source, in menu order.</summary>
    readonly List<(string Label, Action Open)> _signIns = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Last resort: a bug in one handler must not take the strip down. Logged with no usage data.
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true;
        };

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Battery.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _settings = Settings.Load();
        if (!_settings.FirstRunDone)
        {
            // Start with Windows is on by default: applied once, after that the menu owns it.
            StartWithWindows.Set(_settings.StartWithWindows);
            _settings.FirstRunDone = true;
            _settings.Save();
        }

        _themeWatcher = new SystemThemeWatcher();
        _themeWatcher.Changed += _ => RenderNow();

        _store = new ReadingStore(AppPaths.ReadingsFile);
        _store.Changed += () => Dispatcher.BeginInvoke(RenderNow);

        _strip = new StripWindow();
        _strip.DragFinished += OnDragFinished;
        _strip.DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        _strip.MouseRightButtonUp += (_, args) => { args.Handled = true; ShowMenu(); };
        _panel = new HoverPanel();
        SetUpHover();

        RenderNow();   // the last saved readings show immediately
        _strip.Show();
        Place();

        // Order matters twice: for an app with several sources, the first one's hint is the advice shown.
        AddWebSources();
        _providers.Add(new ClaudeProvider());
        _providers.Add(new CodexProvider());
        var openCode = new OpenCodeProvider(_webReader, () => _store.Current.ContainsKey(ProviderIds.OpenCode));
        _providers.Add(openCode);
        _signIns.Add(("Sign in to OpenCode…", () => OpenSignIn("OpenCode", OpenCodeUsageParser.ConsoleUrl, openCode)));
        foreach (var provider in _providers)
        {
            var source = provider;
            provider.ReadingAvailable += reading => _store.Put(reading);
            provider.HintChanged += hint => Dispatcher.BeginInvoke(() => SetHint(source, hint));
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
        _webReader.Dispose();
        _themeWatcher?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Place);

    /// <summary>Sources added before the file and folder sources (filled in by the Claude web source).</summary>
    void AddWebSources()
    {
        // claude.ai first: account-wide, so it covers Desktop and web use. Its sign-in advice is the
        // hint shown while neither Claude source has a reading.
        var claudeWeb = new ClaudeWebProvider(_webReader, () => _settings.ClaudeWebSeen);
        claudeWeb.ReadingAvailable += _ => Dispatcher.BeginInvoke(() =>
        {
            if (_settings.ClaudeWebSeen) return;
            _settings.ClaudeWebSeen = true;
            _settings.Save();
        });
        _providers.Add(claudeWeb);
        _signIns.Add(("Sign in to Claude…", () => OpenSignIn("Claude", ClaudeWebUsageParser.UsagePageUrl, claudeWeb)));
    }

    void SetHint(IUsageProvider source, string? hint)
    {
        if (_sourceHints.TryGetValue(source, out var current) && current == (true, hint)) return;
        _sourceHints[source] = (true, hint);
        RenderNow();
    }

    /// <summary>
    /// One hint per app, merged from its sources. Worked out at each render because whether the app's
    /// reading is fresh changes with time, and a stale reading lets the other source's advice through.
    /// </summary>
    Dictionary<string, string> MergedHints(DateTimeOffset now)
    {
        var merged = new Dictionary<string, string>();
        var readings = _store.Current;
        foreach (var id in _providers.Select(p => p.Id).Distinct())
        {
            var fresh = !readings.TryGetValue(id, out var r) || now - r.ReadAt <= StripModel.StaleAfter;
            var hint = HintMerge.For(_providers.Where(p => p.Id == id)
                .Select(p => _sourceHints.TryGetValue(p, out var h) ? h : (false, null)), fresh);
            if (hint is not null) merged[id] = hint;
        }
        return merged;
    }

    void RenderNow()
    {
        var now = DateTimeOffset.Now;
        var apps = StripModel.Build(_store.Current, MergedHints(now), now);
        var theme = Theme.For(_themeWatcher.IsLight);
        _strip.Render(apps, theme);
        if (_panel.IsVisible)
        {
            _panel.Render(apps, theme, now);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, PlacePanel);   // its height may have changed
        }
    }

    // ---- Placement ------------------------------------------------------------------------------

    /// <summary>Puts the strip on the taskbar, re-asserts topmost, and hides it when it should not show.</summary>
    void Place()
    {
        if (_strip.IsDragging) return;

        if (Taskbar.IsFullScreenAppActive(_strip.Handle) || Taskbar.IsAutoHiddenNow())
        {
            if (_strip.IsVisible) _strip.Hide();
            _panel.Hide();
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
        PlacePanel();
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

    // ---- Hover panel ----------------------------------------------------------------------------

    void SetUpHover()
    {
        _hoverShow = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = HoverShowAfter };
        _hoverShow.Tick += (_, _) => { _hoverShow.Stop(); ShowPanel(); };
        _hoverHide = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = HoverHideAfter };
        _hoverHide.Tick += (_, _) => { _hoverHide.Stop(); _panel.Hide(); };

        _strip.MouseEnter += (_, _) => { _hoverHide.Stop(); if (!_panel.IsVisible) _hoverShow.Start(); };
        _strip.MouseLeave += (_, _) => { _hoverShow.Stop(); if (_panel.IsVisible) _hoverHide.Start(); };
        _panel.MouseEnter += (_, _) => _hoverHide.Stop();
        _panel.MouseLeave += (_, _) => _hoverHide.Start();
        // A press starts a drag or opens the menu; neither is a hover.
        _strip.PreviewMouseDown += (_, _) => { _hoverShow.Stop(); _panel.Hide(); };
    }

    void ShowPanel()
    {
        if (_strip.IsDragging || !_strip.IsVisible) return;
        var now = DateTimeOffset.Now;
        _panel.Render(StripModel.Build(_store.Current, MergedHints(now), now), Theme.For(_themeWatcher.IsLight), now);
        _panel.Opacity = 0;   // shown first so it has a size, then moved, then revealed
        _panel.Show();
        _panel.UpdateLayout();
        PlacePanel();
        _panel.Opacity = 1;
    }

    /// <summary>Above the strip, right edges aligned, 10 px gap, kept on the primary screen.</summary>
    void PlacePanel()
    {
        if (!_panel.IsVisible) return;
        var strip = _strip.PixelRect;
        var panel = _panel.PixelRect;
        var screen = Taskbar.PrimaryScreen();
        var x = Math.Max(screen.Left, Math.Min(strip.Right - panel.Width, screen.Right - panel.Width));
        var y = Math.Max(screen.Top, strip.Top - HoverGap * _strip.DpiScale - panel.Height);
        _panel.MoveToPixels(x, y);
    }

    // ---- Right-click menu -----------------------------------------------------------------------

    void ShowMenu()
    {
        _hoverShow.Stop();
        _panel.Hide();

        var menu = new ContextMenu();
        menu.Items.Add(Item("Refresh now", RefreshAll));
        foreach (var (label, open) in _signIns) menu.Items.Add(Item(label, open));

        if (ClaudeHookInstaller.IsInstalled()) menu.Items.Add(Item("Disconnect Claude Code", DisconnectClaude));
        else menu.Items.Add(Item("Connect Claude Code…", ConnectClaude));

        var autostart = Item("Start with Windows", () =>
        {
            var enable = !StartWithWindows.IsEnabled();
            StartWithWindows.Set(enable);
            _settings.StartWithWindows = enable;
            _settings.Save();
        });
        autostart.IsCheckable = true;
        autostart.IsChecked = StartWithWindows.IsEnabled();
        menu.Items.Add(autostart);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quit", Shutdown));

        // A menu needs its window in the foreground to close when the user clicks elsewhere. This is
        // the one moment the strip takes focus, and only because the user asked for the menu.
        SetForegroundWindow(_strip.Handle);
        menu.PlacementTarget = _strip;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    SignInWindow? _signInWindow;

    /// <summary>One sign-in window at a time; closing it reads that source again straight away.</summary>
    void OpenSignIn(string serviceName, string startUrl, IUsageProvider provider)
    {
        if (_signInWindow is not null)
        {
            _signInWindow.Activate();
            return;
        }
        _signInWindow = new SignInWindow(serviceName, startUrl);
        _signInWindow.Closed += (_, _) =>
        {
            _signInWindow = null;
            _ = provider.RefreshAsync();
        };
        _signInWindow.Show();
        _signInWindow.Activate();
    }

    static MenuItem Item(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    void RefreshAll()
    {
        foreach (var provider in _providers)
        {
            // Providers swallow their own failures; this also guards against one that does not.
            _ = provider.RefreshAsync().ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    void ConnectClaude()
    {
        const string title = "Connect Claude Code";
        if (!ClaudeHookInstaller.ShimExists)
        {
            MessageBox.Show("Battery.StatusLine.exe was not found beside Battery:\n" + ClaudeHookInstaller.ShimPath,
                title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var existing = ClaudeHookInstaller.ExistingCommand();
        var keeps = existing is null
            ? "You have no status line now, so Claude Code will still show none."
            : "Your current status line keeps working: Battery runs it after saving the reading.\nCurrent command:\n" + existing;
        var message =
            "Battery reads Claude's usage from the data Claude Code passes to its status line.\n\n" +
            "This will edit:\n" + ClaudeHookInstaller.SettingsPath + "\n\n" +
            "A backup is made first at:\n" + ClaudeHookInstaller.BackupPath + "\n\n" +
            "Only the status-line command changes, to:\n" + ClaudeHookInstaller.ShimPath + "\n\n" +
            keeps + "\n\n" +
            "Readings start the next time Claude Code updates its status line. " +
            "\"Disconnect Claude Code\" puts it back.\n\nGo ahead?";

        if (MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;

        try
        {
            ClaudeHookInstaller.Install();
            RefreshAll();   // swaps the "Connect" hint for "Run Claude Code once"
        }
        catch (Exception ex)
        {
            MessageBox.Show("settings.json was left unchanged.\n\n" + ex.Message, title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void DisconnectClaude()
    {
        const string title = "Disconnect Claude Code";
        var message =
            "This puts your previous status line back in:\n" + ClaudeHookInstaller.SettingsPath + "\n\n" +
            "A backup is made first at:\n" + ClaudeHookInstaller.UninstallBackupPath + "\n\nGo ahead?";
        if (MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;

        try
        {
            ClaudeHookInstaller.Uninstall();
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show("settings.json was left unchanged.\n\n" + ex.Message, title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    static void LogError(Exception e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "error.log"),
                $"{DateTimeOffset.Now:O} {e.GetType().FullName}: {e.Message}\n{e.StackTrace}\n");
        }
        catch { }
    }
}
