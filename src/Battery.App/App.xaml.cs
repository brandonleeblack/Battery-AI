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
    HoverPanel? _panel;   // a fresh one each time it shows; null while hidden
    SystemThemeWatcher _themeWatcher = null!;
    DispatcherTimer _placeTimer = null!;
    DispatcherTimer _renderTimer = null!;
    DispatcherTimer _hoverShow = null!;
    DispatcherTimer _hoverHide = null!;
    ReadingStore _store = null!;
    readonly List<IUsageProvider> _providers = new();
    readonly Dictionary<IUsageProvider, (bool Reported, string? Hint)> _sourceHints = new();   // UI thread only
    readonly WebUsageReader _webReader = new();
    WebSignInProvider _claudeWeb = null!;
    WebSignInProvider _openCode = null!;
    CodexProvider _codex = null!;
    ClaudeProvider _claudeCode = null!;

    BarStyle CurrentBarStyle => BarStyles.Parse(_settings.BarStyle);

    bool IsShown(string providerId) => ShownApps.Visible(_settings.HiddenProviders).Any(a => a.Id == providerId);

    bool IsConnected(string providerId) => _settings.ConnectedApps?.Contains(providerId) == true;

    /// <summary>
    /// A source is read only while its app is shown and connected. The Claude Code status line has its
    /// own on/off (the status-line setting itself), so it runs whenever Claude is shown.
    /// </summary>
    bool ShouldRun(IUsageProvider p) => IsShown(p.Id) && (p == _claudeCode || IsConnected(p.Id));

    /// <summary>Starts or stops a source to match the settings; a stopped one gets its "connect" hint.</summary>
    void ApplyRunState(IUsageProvider p)
    {
        if (ShouldRun(p))
        {
            p.Start();
            return;
        }
        p.Stop();
        if (p != _claudeCode)
            SetHint(p, AppConnections.For(p.Id).HintWhileDisconnected(_settings.LapsedApps.Contains(p.Id)));
    }

    void SetConnected(string providerId, bool connected, bool lapsed = false)
    {
        _settings.ConnectedApps = AppConnections.WithConnected(_settings.ConnectedApps ?? new(), providerId, connected).ToList();
        _settings.LapsedApps = AppConnections.WithConnected(_settings.LapsedApps, providerId, lapsed).ToList();
        _settings.Save();
    }

    /// <summary>
    /// First start of the version that tracks connections: carry over what was working. claude.ai was
    /// signed in if it ever gave a reading; Codex was being read if it has a reading. OpenCode moved to
    /// its own browser profile, so its old sign-in does not carry over: it shows as signed out.
    /// </summary>
    void MigrateConnections()
    {
        if (_settings.ConnectedApps is not null) return;
        var connected = new List<string>();
        if (_settings.ClaudeWebSeen) connected.Add(ProviderIds.Claude);
        if (_store.Current.ContainsKey(ProviderIds.Codex)) connected.Add(ProviderIds.Codex);
        _settings.ConnectedApps = connected;
        if (_store.Current.ContainsKey(ProviderIds.OpenCode)) _settings.LapsedApps.Add(ProviderIds.OpenCode);
        _settings.Save();
    }

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
        _tray = new TrayIcon();
        _tray.MenuRequested += ShowMenu;
        _tray.Clicked += TogglePanelFromTray;
        SetUpHover();

        RenderNow();   // the last saved readings show immediately
        _strip.Show();
        Place();

        MigrateConnections();

        // Order matters: for Claude, the first source's hint is the advice shown. claude.ai comes first
        // because it is account-wide, covering Desktop and web use too.
        _claudeWeb = WebSignInProvider.Claude(_webReader, () => IsConnected(ProviderIds.Claude));
        _claudeCode = new ClaudeProvider();
        _codex = new CodexProvider();
        _openCode = WebSignInProvider.OpenCode(_webReader, () => IsConnected(ProviderIds.OpenCode));
        _providers.AddRange(new IUsageProvider[] { _claudeWeb, _claudeCode, _codex, _openCode });

        foreach (var web in new[] { _claudeWeb, _openCode })
        {
            var source = web;
            source.SignedInChanged += signedIn => Dispatcher.BeginInvoke(() => OnSignedInChanged(source, signedIn));
        }
        foreach (var provider in _providers)
        {
            var source = provider;
            provider.ReadingAvailable += reading => _store.Put(reading);
            provider.HintChanged += hint => Dispatcher.BeginInvoke(() => SetHint(source, hint));
            ApplyRunState(provider);   // hidden or not-connected apps are not read at all
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
        _tray?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Place);

    /// <summary>
    /// A read confirmed the sign-in (connect, start polling) or found Battery signed out. Signed out while
    /// connected means the site ended the session: stop reading and say "Signed out" until signed in again.
    /// </summary>
    void OnSignedInChanged(WebSignInProvider source, bool signedIn)
    {
        // A read already under way when the user chose Disconnect must not connect it again.
        if (_disconnecting.Contains(source.Id)) return;
        if (signedIn == IsConnected(source.Id)) return;
        SetConnected(source.Id, signedIn, lapsed: !signedIn);
        ApplyRunState(source);
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
        var apps = StripModel.Build(_store.Current, MergedHints(now), now, _settings.HiddenProviders);
        var theme = Theme.For(_themeWatcher.IsLight);
        _strip.Render(apps, theme, CurrentBarStyle);
        _tray?.SetTooltip(TrayText.For(apps));
        if (_panel is { IsVisible: true })
        {
            _panel.Render(apps, theme, CurrentBarStyle, now);
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
            HidePanel();
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
        _hoverHide.Tick += (_, _) => { _hoverHide.Stop(); HidePanel(); };

        _strip.MouseEnter += (_, _) => { _hoverHide.Stop(); if (_panel is null) _hoverShow.Start(); };
        _strip.MouseLeave += (_, _) => { _hoverShow.Stop(); if (_panel is not null) _hoverHide.Start(); };
        // A press starts a drag or opens the menu; neither is a hover.
        _strip.PreviewMouseDown += (_, _) => { _hoverShow.Stop(); HidePanel(); };
    }

    void ShowPanel()
    {
        if (_strip.IsDragging || !_strip.IsVisible) return;
        var now = DateTimeOffset.Now;
        // A new window every time: a transparent window that was hidden can come back blank after another
        // window (the sign-in browser) has used the graphics device, and a fresh one always draws.
        HidePanel();
        _panel = new HoverPanel();
        _panel.MouseEnter += (_, _) => _hoverHide.Stop();
        _panel.MouseLeave += (_, _) => _hoverHide.Start();
        _panel.Render(StripModel.Build(_store.Current, MergedHints(now), now, _settings.HiddenProviders), Theme.For(_themeWatcher.IsLight), CurrentBarStyle, now);
        _panel.Opacity = 0;   // shown first so it has a size, then moved, then revealed
        _panel.Show();
        _panel.UpdateLayout();
        PlacePanel();
        _panel.Opacity = 1;
    }

    void HidePanel()
    {
        _panel?.Close();
        _panel = null;
    }

    /// <summary>Above the strip, right edges aligned, 10 px gap, kept on the primary screen.</summary>
    void PlacePanel()
    {
        if (_panel is null || !_panel.IsVisible) return;
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
        HidePanel();

        var menu = new ContextMenu();
        menu.Items.Add(Item("Refresh now", RefreshAll));
        // One item per app, worded the same way: "Sign in to …" / "Connect …" until confirmed, then only "Disconnect …".
        menu.Items.Add(new Separator());
        foreach (var connection in AppConnections.All.Where(c => IsShown(c.ProviderId)))
        {
            var id = connection.ProviderId;
            menu.Items.Add(IsConnected(id)
                ? Item(connection.DisconnectLabel, () => Disconnect(id))
                : Item(connection.ConnectLabel, () => Connect(id)));
        }
        if (IsShown(ProviderIds.Claude))
        {
            var statusLine = Item("Use Claude Code status line", () =>
            {
                if (ClaudeHookInstaller.IsInstalled()) DisconnectClaudeCode();
                else ConnectClaudeCode();
            });
            statusLine.IsCheckable = true;
            statusLine.IsChecked = ClaudeHookInstaller.IsInstalled();
            menu.Items.Add(statusLine);
        }
        menu.Items.Add(new Separator());

        menu.Items.Add(AppsMenu());
        menu.Items.Add(BarStyleMenu());

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
        if (_strip.IsVisible)
        {
            SetForegroundWindow(_strip.Handle);
            menu.PlacementTarget = _strip;
        }
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    /// <summary>"Show" submenu: one tick per app. The last ticked app cannot be unticked.</summary>
    MenuItem AppsMenu()
    {
        var show = new MenuItem { Header = "Show" };
        var visible = ShownApps.Visible(_settings.HiddenProviders);
        foreach (var (id, name) in ShownApps.All)
        {
            var shown = visible.Any(a => a.Id == id);
            var item = Item(name, () => ToggleApp(id));
            item.IsCheckable = true;
            item.IsChecked = shown;
            item.IsEnabled = !(shown && visible.Count == 1);
            show.Items.Add(item);
        }
        return show;
    }

/// <summary>"Bar style" submenu: Clean, Glass gel or Cells, one ticked.</summary>
    MenuItem BarStyleMenu()
    {
        var menu = new MenuItem { Header = "Bar style" };
        foreach (var (style, name) in BarStyles.All)
        {
            var item = Item(name, () =>
            {
                _settings.BarStyle = style.ToString();
                _settings.Save();
                RenderNow();
            });
            item.IsCheckable = true;
            item.IsChecked = style == CurrentBarStyle;
            menu.Items.Add(item);
        }
        return menu;
    }

    void ToggleApp(string id)
    {
        _settings.HiddenProviders = ShownApps.Toggle(_settings.HiddenProviders, id).ToList();
        _settings.Save();

        foreach (var provider in _providers.Where(p => p.Id == id)) ApplyRunState(provider);
        RenderNow();
        // The strip changed width; keep its right edge by the tray once the new size is laid out.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Place);
    }

    TrayIcon? _tray;
    DispatcherTimer? _trayPanelTimer;

    /// <summary>
    /// Left-click on the tray icon shows the details panel (or hides it). The pointer is not over the
    /// strip, so it closes after a few seconds unless the pointer moves onto it.
    /// </summary>
    void TogglePanelFromTray()
    {
        if (_panel is not null)
        {
            HidePanel();
            return;
        }
        ShowPanel();
        if (_panel is null) return;   // the strip is hidden (full screen) or being dragged
        _trayPanelTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(6), DispatcherPriority.Normal, (_, _) =>
        {
            _trayPanelTimer!.Stop();
            if (_panel is { IsMouseOver: false }) HidePanel();
        }, Dispatcher);
        _trayPanelTimer.Stop();
        _trayPanelTimer.Start();
    }

    SignInWindow? _signInWindow;

    /// <summary>
    /// One sign-in window at a time. Closing it reads the site straight away: a good read confirms the
    /// sign-in (the menu then offers only Disconnect), otherwise the menu still offers Sign in.
    /// </summary>
    void OpenSignIn(WebSignInProvider provider)
    {
        if (_signInWindow is not null)
        {
            _signInWindow.Activate();
            return;
        }
        _signInWindow = new SignInWindow(provider.ServiceName, provider.PageUrl, provider.Profile);
        // Confirm as soon as the window reaches the site after signing in, not only when it is closed.
        _signInWindow.PageShown += url => { if (!IsConnected(provider.Id)) provider.PageShownInSignInWindow(url); };
        _signInWindow.Closed += (_, _) =>
        {
            _signInWindow = null;
            _ = provider.RefreshAsync();
        };
        _signInWindow.Show();
        _signInWindow.Activate();
    }

    void Connect(string id)
    {
        switch (id)
        {
            case ProviderIds.Claude: OpenSignIn(_claudeWeb); break;
            case ProviderIds.OpenCode: OpenSignIn(_openCode); break;
            case ProviderIds.Codex: ConnectCodex(); break;
        }
    }

    void ConnectCodex()
    {
        var message =
            "Battery reads the usage figures Codex writes to its session files on this PC:\n" +
            _codex.SessionsDir + "\n\n" +
            "It reads only the rate-limit lines, sends nothing anywhere, and never reads Codex's sign-in.\n\n" +
            "Connect?";
        if (!Confirm("Connect Codex", message)) return;
        SetConnected(ProviderIds.Codex, true);
        ApplyRunState(_codex);
    }

    async void Disconnect(string id)
    {
        var name = ShownApps.All.First(a => a.Id == id).Name;
        var message = id switch
        {
            ProviderIds.Codex =>
                "Battery stops reading Codex's session files and clears Codex's bars. Codex itself is not affected.",
            _ =>
                $"Battery signs out of {name} in its own browser and clears {name}'s bars. The {name} apps and " +
                "your normal browser stay signed in.",
        };
        if (id == ProviderIds.Claude && ClaudeHookInstaller.IsInstalled())
            message += "\n\nThe Claude Code status line is still on and will keep sending readings. " +
                       "Untick \"Use Claude Code status line\" to stop those too.";
        if (!Confirm(AppConnections.For(id).DisconnectLabel.TrimEnd('…'), message + "\n\nGo ahead?")) return;

        _disconnecting.Add(id);
        try
        {
            SetConnected(id, false);
            var provider = _providers.First(p => p.Id == id && p != _claudeCode);
            ApplyRunState(provider);
            // Waits for any read under way, so that read's bars are cleared below too.
            if (provider is WebSignInProvider web && !await web.SignOutAsync())
            {
                MessageBox.Show($"Battery stopped reading {name}, but could not clear its own sign-in for it. " +
                                "It will not be read again unless you sign in.", name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            _store.Remove(id);
        }
        finally
        {
            // Events from that last read are queued behind this; let them pass before listening again.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            _disconnecting.Remove(id);
        }
    }

    readonly HashSet<string> _disconnecting = new();

    static bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel)
        == MessageBoxResult.OK;

    static MenuItem Item(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    void RefreshAll()
    {
        foreach (var provider in _providers.Where(ShouldRun))
        {
            // Providers swallow their own failures; this also guards against one that does not.
            _ = provider.RefreshAsync().ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    void ConnectClaudeCode()
    {
        const string title = "Use Claude Code status line";
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
            "Unticking \"Use Claude Code status line\" puts it back.\n\nGo ahead?";

        if (MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;

        try
        {
            ClaudeHookInstaller.Install();
            _ = _claudeCode.RefreshAsync();   // swaps the "Use" hint for "Run Claude Code once"
        }
        catch (Exception ex)
        {
            MessageBox.Show("settings.json was left unchanged.\n\n" + ex.Message, title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void DisconnectClaudeCode()
    {
        const string title = "Stop using Claude Code status line";
        var message =
            "This restores your previous status line, or removes it if you had none, in:\n" +
            ClaudeHookInstaller.SettingsPath + "\n\n" +
            "A backup is made first at:\n" + ClaudeHookInstaller.UninstallBackupPath + "\n\nGo ahead?";
        if (MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;

        try
        {
            ClaudeHookInstaller.Uninstall();
            _ = _claudeCode.RefreshAsync();
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
