using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Text.Json;

namespace MessengerDesktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Before any toast API. The toolkit otherwise adopts a path-based id.
        ToastIdentity.AssignProcess();
        try
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (root.Length > 0)
            {
                string logs = Path.Combine(root, "MessengerDesktop", "logs");
                Directory.CreateDirectory(logs);
                string path = Path.Combine(logs, AlertLog.FileName);
                AlertLog.RotateIfNeeded(path);
                Trace.AutoFlush = true;
                Trace.Listeners.Add(new TextWriterTraceListener(path));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A missing log must not stop Messenger from opening.
        }

        using SingleInstance lifetime = SingleInstance.Acquire();
        if (!lifetime.IsPrimary)
        {
            lifetime.SignalExisting();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        NativeMenuTheme.TryAllowDark();
        string exePath = Application.ExecutablePath;
        var startup = new StartupRegistration(new WindowsRunKey());
        var settingsStore = new SettingsStore(SettingsStore.DefaultPath);
        SettingsLoadResult loaded = AppStartup.Configure(settingsStore, startup, exePath);
        Application.Run(new MessengerWindow(LaunchArgs.IsMinimized(args), lifetime, startup, exePath, settingsStore, loaded.Settings));
    }
}

internal sealed class MessengerWindow : Form, IMessageFilter
{
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private readonly ReelReturnTracker reelReturn = new();
    private readonly SingleInstance lifetime;
    private readonly StartupRegistration startup;
    private readonly string exePath;
    private readonly SettingsStore settingsStore;
    private readonly AppSettings settings;
    private readonly XPreviewClient previews = new();
    private readonly ToastBridge toasts = new();
    private readonly NotificationPipeline notifications = new(ReactionWatch.DefaultWindow);
    private readonly ChatListReactionWatch chatListReactions = new(ReactionWatch.DefaultWindow);
    private readonly System.Windows.Forms.Timer reactionTimer = new();
    private readonly System.Windows.Forms.Timer backupTimer = new();
    private readonly HiddenTrayAlerts hiddenAlerts = new();
    private bool pollingChatList;
    private readonly Dictionary<string, CoreWebView2Notification> taggedWebNotifications = new(StringComparer.Ordinal);
    private CoreWebView2Environment? webEnvironment;
    private CoreWebView2Environment? xEnvironment;
    private OverlayHost? overlay;
    private OverlayHost? statusOverlay;
    private NotifyIcon? tray;
    private readonly TraySummary summary = new();
    private readonly System.Windows.Forms.Timer trayClickTimer = new();
    private readonly System.Windows.Forms.Timer muteTimer = new();
    private UnreadFlyout? flyout;
    private Icon? trayBaseIcon;
    private Icon? trayComposedIcon;
    private DateTime trayDoubleClickedUtc = DateTime.MinValue;
    private Task? reelInitialization;
    private Task? statusInitialization;
    private bool reelIsOpen;
    private bool statusIsOpen;
    private bool shuttingDown;
    private bool parked;
    private bool exitRequested;
    private bool browserStarted;
    private bool initialNavigationStarted;
    private bool watchTitles;
    private string? chatListScriptId;
    private bool osToastsReady;
    private string? appIconPath;
    private int unreadCount;
    private string balloonUrl = "";
    private string balloonTag = "";
    private Uri? pendingChat;
    private CoreWebView2Notification? untaggedWebNotification;
    private long reelGeneration;
    private long statusGeneration;
    private Uri? fallbackReel;
    private long sourceRevision;
    private FormWindowState restoreState = FormWindowState.Maximized;
    private Rectangle restoreBounds;

    public MessengerWindow(bool startMinimized, SingleInstance lifetime, StartupRegistration startup, string exePath, SettingsStore settingsStore, AppSettings settings)
    {
        this.lifetime = lifetime;
        this.startup = startup;
        this.exePath = exePath;
        this.settingsStore = settingsStore;
        this.settings = settings;
        Text = "Messenger";
        Width = 1200;
        Height = 800;
        MinimumSize = new Size(520, 400);
        WindowState = FormWindowState.Maximized;
        if (startMinimized) ParkOffScreen();
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        Controls.Add(browser);
        CreateTray();
        reactionTimer.Tick += OnReactionTimer;
        backupTimer.Interval = (int)HiddenTrayAlerts.BackupInterval.TotalMilliseconds;
        backupTimer.Tick += (_, _) => _ = PollChatListAsync();
        backupTimer.Start();
        toasts.Attach(OnToastActivated);
        osToastsReady = OsToast.TryEnable(toasts);
        ToastIdentity.Publish(exePath);
        Application.AddMessageFilter(this);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyTrayAppearance();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (parked || shuttingDown || IsDisposed) return;
        // The open chat is being read. Recount from the live rows now; the
        // backup timer is what covers messages that arrive while parked.
        if (browser.CoreWebView2 is not null &&
            Uri.TryCreate(browser.CoreWebView2.Source, UriKind.Absolute, out Uri? current) &&
            IsMessengerChat(current))
        {
            summary.MarkChatRead(current.AbsoluteUri);
            ApplyTrayAppearance();
            RefreshFlyout();
        }

        _ = PollChatListAsync();
    }

    private static bool IsMessengerChat(Uri uri)
    {
        if (!NotificationOrigins.IsAllowed(uri)) return false;
        string path = uri.AbsolutePath;
        return path.StartsWith("/messages/t/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/messages/e2ee/t/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/t/", StringComparison.OrdinalIgnoreCase);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTrayAppearance();
        lifetime.Listen(RequestActivate);
        if (browserStarted) return;
        browserStarted = true;
        InitializeBrowser();
    }

    private void CreateTray()
    {
        Icon? icon = Icon is null ? SystemIcons.Application : Icon;
        trayBaseIcon = (Icon)icon.Clone();
        tray = new NotifyIcon
        {
            Visible = true
        };
        tray.MouseUp += OnTrayMouseUp;
        tray.DoubleClick += OnTrayDoubleClick;
        tray.BalloonTipClicked += (_, _) => OpenChat(balloonUrl, balloonTag);
        trayClickTimer.Interval = Math.Max(SystemInformation.DoubleClickTime, 200);
        trayClickTimer.Tick += (_, _) =>
        {
            trayClickTimer.Stop();
            ShowUnreadFlyout();
        };
        muteTimer.Tick += (_, _) =>
        {
            muteTimer.Stop();
            ApplyTrayAppearance();
        };
        ApplyTrayAppearance();
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            trayClickTimer.Stop();
            ShowTrayMenu();
            return;
        }

        if (e.Button != MouseButtons.Left) return;
        if ((DateTime.UtcNow - trayDoubleClickedUtc).TotalMilliseconds < SystemInformation.DoubleClickTime)
            return;
        trayClickTimer.Stop();
        trayClickTimer.Start();
    }

    private void OnTrayDoubleClick(object? sender, EventArgs e)
    {
        trayDoubleClickedUtc = DateTime.UtcNow;
        trayClickTimer.Stop();
        CloseFlyout();
        BringToForeground();
    }

    private void ToggleChatListWatch()
    {
        settings.WatchChatListForReactions = !settings.WatchChatListForReactions;
        bool saved = true;
        try
        {
            settingsStore.Save(settings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            settings.WatchChatListForReactions = !settings.WatchChatListForReactions;
            saved = false;
        }

        if (saved)
            _ = ApplyChatListWatchAsync();
    }

    private void ToggleXLinkPreviews()
    {
        settings.XLinkPreviews = !settings.XLinkPreviews;
        try
        {
            settingsStore.Save(settings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            settings.XLinkPreviews = !settings.XLinkPreviews;
            return;
        }

        PostPreviewState();
    }

    private void ChooseMute(TrayMute choice)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        if (summary.MuteChoice(now) == choice) summary.Unmute();
        else summary.Mute(choice, now);
        ApplyTrayAppearance();
    }

    private void ShowTrayMenu()
    {
        if (tray is null) return;
        CloseFlyout();
        startup.TryHeal(exePath);
        uint command = NativeTrayMenu.Show(
            tray,
            summary,
            DateTimeOffset.Now,
            startup.IsEnabled(),
            settings.WatchChatListForReactions,
            settings.XLinkPreviews);
        switch (command)
        {
            case NativeTrayMenu.CmdOpen:
                BringToForeground();
                break;
            case NativeTrayMenu.CmdMuteOneHour:
                ChooseMute(TrayMute.OneHour);
                break;
            case NativeTrayMenu.CmdMuteEightHours:
                ChooseMute(TrayMute.EightHours);
                break;
            case NativeTrayMenu.CmdMuteUntilOn:
                ChooseMute(TrayMute.UntilOn);
                break;
            case NativeTrayMenu.CmdUnmute:
                summary.Unmute();
                ApplyTrayAppearance();
                break;
            case NativeTrayMenu.CmdWatchChatList:
                ToggleChatListWatch();
                break;
            case NativeTrayMenu.CmdXLinkPreviews:
                ToggleXLinkPreviews();
                break;
            case NativeTrayMenu.CmdStartup:
                if (startup.IsEnabled()) startup.Disable();
                else startup.Enable(exePath);
                break;
            case NativeTrayMenu.CmdQuit:
                ExitApplication();
                break;
        }
    }

    private void ShowUnreadFlyout()
    {
        if (tray is null || shuttingDown) return;
        summary.OnPanelOpened();
        ApplyTrayAppearance();
        CloseFlyout();
        var panel = new UnreadFlyout(
            summary,
            () =>
            {
                // Clears the in-memory summary list only. No navigation and no read receipt.
                summary.MarkAllRead();
                ApplyTrayAppearance();
            },
            () =>
            {
                CloseFlyout();
                BringToForeground();
            },
            row =>
            {
                CloseFlyout();
                summary.MarkRowRead(row.Key);
                OpenChat(row.Url, row.Tag);
            });
        panel.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(flyout, panel))
                flyout = null;
            panel.Dispose();
        };
        flyout = panel;
        panel.ShowAbove(TrayIconBounds.Get(tray));
    }

    private void CloseFlyout()
    {
        UnreadFlyout? panel = flyout;
        if (panel is null || panel.IsDisposed) return;
        flyout = null;
        // A click on the panel can be what closes it. Let that click finish first.
        if (!shuttingDown && panel.IsHandleCreated)
        {
            panel.BeginInvoke(() =>
            {
                if (!panel.IsDisposed) panel.Close();
            });
            return;
        }

        panel.Close();
    }

    private void RefreshFlyout()
    {
        if (flyout is not { IsDisposed: false, Visible: true }) return;
        // A reaction that arrives while the summary is open is already seen.
        summary.OnPanelOpened();
        flyout.RefreshRows();
    }

    private void ApplyTrayAppearance()
    {
        if (tray is null || trayBaseIcon is null) return;
        DateTimeOffset now = DateTimeOffset.Now;
        int iconSize = TrayIconSize.ForDpi(DeviceDpi > 0 ? DeviceDpi : 96);
        Icon composed = TrayBadgePainter.Compose(trayBaseIcon, summary.GlyphAt(now), summary.BadgeLabel, iconSize);
        Icon? previous = trayComposedIcon;
        tray.Icon = composed;
        trayComposedIcon = composed;
        previous?.Dispose();
        string tip = summary.Tooltip;
        try { tray.Text = tip; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The icon limit is enforced by the tooltip helper. Ignore a stale icon.
        }

        ArmMuteTimer(now);
    }

    private void ArmMuteTimer(DateTimeOffset now)
    {
        TimeSpan? remaining = summary.MuteRemaining(now);
        if (remaining is null || remaining.Value == Timeout.InfiniteTimeSpan)
        {
            muteTimer.Stop();
            return;
        }

        int milliseconds = (int)Math.Clamp(remaining.Value.TotalMilliseconds, 50, 60_000);
        muteTimer.Interval = Math.Max(milliseconds, 1);
        muteTimer.Start();
    }

    private void DisposeTray()
    {
        trayClickTimer.Stop();
        trayClickTimer.Dispose();
        muteTimer.Stop();
        muteTimer.Dispose();
        CloseFlyout();
        if (tray is not null)
            tray.Icon = null;
        trayComposedIcon?.Dispose();
        trayComposedIcon = null;
        trayBaseIcon?.Dispose();
        trayBaseIcon = null;
    }

    private void RequestActivate()
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired) BeginInvoke(BringToForeground);
            else BringToForeground();
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
        {
            // The form is already closing.
        }
    }

    private void BringToForeground()
    {
        parked = false;
        ShowInTaskbar = true;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        if (restoreState == FormWindowState.Normal && restoreBounds.Width > 0 && restoreBounds.Height > 0)
            Bounds = restoreBounds;
        WindowState = restoreState;
        Activate();
        NativeForeground.BringToFront(Handle);
        TaskbarBadge.Apply(Handle, unreadCount);
    }

    private void HideToTray()
    {
        if (!parked)
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = restoreState == FormWindowState.Minimized ? FormWindowState.Normal : restoreState;
            restoreState = WindowState;
            if (WindowState == FormWindowState.Normal)
                restoreBounds = Bounds;
        }

        ParkOffScreen();
        if (!Visible) Show();
    }

    // Visible, not minimized, and off-screen. WebView2 then keeps the controller
    // visible, so Chromium does not suspend notifications or audio.
    private void ParkOffScreen()
    {
        TrayParkingPlan plan = TrayParking.Park();
        parked = true;
        ShowInTaskbar = plan.ShowInTaskbar;
        if (WindowState != FormWindowState.Normal)
            WindowState = FormWindowState.Normal;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(plan.X, plan.Y);
    }

    private bool WindowIsForeground => !parked && Visible && ContainsFocus;

    private void ExitApplication()
    {
        exitRequested = true;
        Close();
    }

    private async void InitializeBrowser()
    {
        try
        {
            // The control's window has to exist before WebView2 init when the form is hidden.
            _ = browser.Handle;
            string dataFolder = ServiceCatalog.Facebook.ProfileFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var options = new CoreWebView2EnvironmentOptions(BackgroundThrottling.BrowserArguments);
            webEnvironment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: dataFolder,
                options: options);
            await browser.EnsureCoreWebView2Async(webEnvironment);
            try
            {
                browser.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // An older runtime has no memory target. The browser switches still apply.
            }

            browser.CoreWebView2.SourceChanged += OnSourceChanged;
            browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            browser.CoreWebView2.NavigationStarting += OnChatNavigationStarting;
            browser.CoreWebView2.NewWindowRequested += OnChatNewWindowRequested;
            browser.CoreWebView2.NavigationCompleted += OnMainNavigationCompleted;
            browser.CoreWebView2.DocumentTitleChanged += OnDocumentTitleChanged;
            WireNotifications(browser.CoreWebView2);
            await GrantNotificationPermissionsAsync();
            await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(EmbeddedScripts.ReelClose);
            await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(EmbeddedScripts.ChatReel);
            await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(EmbeddedScripts.ChatStatus);
            await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(EmbeddedScripts.XLinkPreview);
            await ApplyChatListWatchAsync();
            Uri start = pendingChat ?? ServiceCatalog.Facebook.Home;
            pendingChat = null;
            initialNavigationStarted = true;
            browser.Source = start;
        }
        catch (Exception error)
        {
            if (exitRequested || shuttingDown) return;
            BringToForeground();
            MessageBox.Show(this, $"Messenger could not start: {error.Message}", "Messenger", MessageBoxButtons.OK, MessageBoxIcon.Error);
            exitRequested = true;
            Close();
        }
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        sourceRevision++;
        Uri? current = Uri.TryCreate(browser.CoreWebView2.Source, UriKind.Absolute, out Uri? parsed) ? parsed : null;
        reelReturn.Observe(current);
        if (current is not null && IsMessengerChat(current))
        {
            summary.MarkChatRead(current.AbsoluteUri);
            ApplyTrayAppearance();
            RefreshFlyout();
        }

        UpdateReelCloseState();
        if (OverlayBusy) SetOverlayArmed(true);
        PostPreviewState();
    }

    private static bool IsReelTarget(Uri? uri) => ReelReturnTracker.IsReel(uri) || ReelReturnTracker.IsShare(uri);

    private bool CurrentPageIsChat() => Uri.TryCreate(browser.CoreWebView2.Source, UriKind.Absolute, out Uri? current) &&
        ReelReturnTracker.IsChat(current);

    private void OnChatNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Redirects are part of a navigation already allowed or canceled above.
        if (e.IsRedirected) return;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? target)) return;
        if (fallbackReel is not null)
        {
            bool isFallback = target.AbsoluteUri == fallbackReel.AbsoluteUri;
            fallbackReel = null;
            if (isFallback) return;
        }
        if (!OverlayBusy && CurrentPageIsChat() && XLinkCapture.TryCapture(target, out Uri? status))
        {
            e.Cancel = true;
            _ = ShowStatusAsync(status!);
            return;
        }
        if (!OverlayBusy && CurrentPageIsChat() && IsReelTarget(target))
        {
            e.Cancel = true;
            _ = ShowReelAsync(target);
        }
    }

    private void OnChatNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (OverlayBusy || !e.IsUserInitiated || !CurrentPageIsChat() ||
            !Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? target)) return;
        if (XLinkCapture.TryCapture(target, out Uri? status))
        {
            e.Handled = true;
            _ = ShowStatusAsync(status!);
            return;
        }
        if (IsReelTarget(target))
        {
            e.Handled = true;
            _ = ShowReelAsync(target);
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out Uri? source) || !ReelReturnTracker.IsFacebook(source)) return;
        if (ReelReturnTracker.IsChat(source) && CurrentPageIsChat() &&
            Uri.TryCreate(browser.CoreWebView2.Source, UriKind.Absolute, out Uri? previewPage) &&
            previewPage.AbsoluteUri == source.AbsoluteUri &&
            XPreviewMessage.TryReadRequest(e.WebMessageAsJson, out XPreviewRequest? preview) &&
            preview is not null)
        {
            _ = ReplyPreviewAsync(preview);
            return;
        }
        if (ReelReturnTracker.IsChat(source) && CurrentPageIsChat() &&
            Uri.TryCreate(browser.CoreWebView2.Source, UriKind.Absolute, out Uri? liveChat) && liveChat.AbsoluteUri == source!.AbsoluteUri &&
            !OverlayBusy)
        {
            if (XLinkCapture.TryReadMessage(e.WebMessageAsJson, out Uri? clicked) &&
                XLinkCapture.TryCapture(clicked, out Uri? status))
            {
                await ShowStatusAsync(status!);
                return;
            }
            if (ReelMessage.TryReadTarget(e.WebMessageAsJson, out Uri? target))
            {
                await ShowReelAsync(target!);
                return;
            }
        }
        string message;
        try { message = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { return; }
        if (message == "messenger-overlay-escape")
        {
            CloseOpenOverlay();
        }
        else if (message == "messenger-x-preview-ready")
        {
            PostPreviewState();
        }
        else if (message == "messenger-reel-ready")
        {
            UpdateReelCloseState();
        }
        else if (message == "messenger-reel-close" && ReelReturnTracker.IsReel(source) &&
                 Uri.TryCreate(browser.CoreWebView2.Source, UriKind.Absolute, out Uri? current) && ReelReturnTracker.IsReel(current) &&
                 reelReturn.TryTakeReturn(out Uri? destination))
        {
            await ReturnToChatAsync(destination!, current!);
        }
    }

    private async void OnMainNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || shuttingDown) return;
        browser.CoreWebView2.NavigationCompleted -= OnMainNavigationCompleted;
        // The title at the end of the first load is the baseline, so existing
        // unreads do not become a reaction toast. A later increase can.
        int loadedUnread = UnreadTitle.Parse(browser.CoreWebView2.DocumentTitle);
        ApplyUnread(loadedUnread);
        notifications.ShouldToastActivity(loadedUnread, DateTimeOffset.Now);
        watchTitles = true;
        try { await EnsureReelReadyAsync(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The next reel click can retry; prewarming is optional.
            if (!reelIsOpen) DiscardReel();
        }
    }

    private Task EnsureReelReadyAsync()
    {
        if (reelInitialization is not null) return reelInitialization;
        if (webEnvironment is null) throw new InvalidOperationException("The browser environment is not ready.");
        var current = new OverlayHost(ServiceCatalog.Facebook, "Opening reel");
        current.Container.Visible = false;
        overlay = current;
        current.BackRequested += (_, _) => CloseReel(current);
        Controls.Add(current.Container);
        // Explicitly create the hidden control's window before WebView2 initialization.
        _ = current.View.Handle;
        reelInitialization = InitializeReelAsync(current, webEnvironment);
        return reelInitialization;
    }

    private async Task InitializeReelAsync(OverlayHost current, CoreWebView2Environment environment)
    {
        await current.View.EnsureCoreWebView2Async(environment);
        if (shuttingDown) return;
        WireNotifications(current.View.CoreWebView2);
        current.View.CoreWebView2.WebMessageReceived += (_, e) => OnReelMessage(current, e);
        current.View.CoreWebView2.NavigationStarting += (_, e) =>
        {
            if (overlay == current && reelIsOpen) current.NavigationStarting(e.Uri, e.NavigationId);
        };
        current.View.CoreWebView2.DOMContentLoaded += (_, e) =>
        {
            if (overlay == current && reelIsOpen) current.NotifyContentReady(e.NavigationId);
        };
        current.View.CoreWebView2.NavigationCompleted += (_, e) =>
        {
            if (overlay != current || !reelIsOpen) return;
            if (!e.IsSuccess) current.NotifyNavigationFailed(e.NavigationId);
            else current.NotifyContentReady(e.NavigationId);
        };
        current.View.CoreWebView2.ProcessFailed += (_, _) =>
        {
            if (overlay == current && reelIsOpen) current.NotifyProcessFailed();
        };
        await current.View.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(EmbeddedScripts.ReelClose);
        await current.View.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(EmbeddedScripts.ReelLayout);
        await current.View.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ReelSoundScript.Source);
        await current.View.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(OverlayEscapeScript.Source);
        if (!shuttingDown) current.Ready = true;
    }

    private async Task ShowReelAsync(Uri target)
    {
        if (OverlayBusy || shuttingDown || webEnvironment is null || !CurrentPageIsChat() || !IsReelTarget(target)) return;
        string originalChat = browser.CoreWebView2.Source;
        reelIsOpen = true;
        SetOverlayArmed(true);
        long generation = ++reelGeneration;
        try
        {
            Task ready = EnsureReelReadyAsync();
            OverlayHost current = overlay!;
            current.BeginLoading(target);
            current.Container.Visible = true;
            current.Container.BringToFront();
            current.Container.PerformLayout();
            current.Present(this);
            await ready;
            if (shuttingDown || !reelIsOpen || reelGeneration != generation || overlay != current || !current.Ready)
            {
                // A cancelled open must not leave the cover up.
                if (!shuttingDown && reelIsOpen && reelGeneration == generation && overlay == current)
                    CloseReel(current);
                else if (overlay == current && !reelIsOpen)
                    current.EndLoading();
                if (!OverlayBusy) SetOverlayArmed(false);
                return;
            }
            current.View.CoreWebView2.IsMuted = false;
            current.View.CoreWebView2.Navigate(target.AbsoluteUri);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!shuttingDown && reelIsOpen && reelGeneration == generation)
            {
                reelIsOpen = false;
                DiscardReel();
                SetOverlayArmed(false);
                if (browser.CoreWebView2.Source == originalChat)
                {
                    fallbackReel = target;
                    browser.CoreWebView2.Navigate(target.AbsoluteUri);
                }
            }
        }
    }

    private async void OnReelMessage(OverlayHost current, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!reelIsOpen || overlay != current) return;
        string message;
        try { message = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { return; }
        if (message == "messenger-overlay-escape")
        {
            CloseReel(current);
            return;
        }
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out Uri? source) || !ReelReturnTracker.IsFacebook(source)) return;
        if (message == "messenger-reel-ready")
        {
            current.View.CoreWebView2.PostWebMessageAsString("messenger-reel-enable");
        }
        else if (message == "messenger-reel-sound-activate" && ReelReturnTracker.IsReel(source) &&
                 Uri.TryCreate(current.View.CoreWebView2.Source, UriKind.Absolute, out Uri? soundSource) &&
                 soundSource.AbsoluteUri == source!.AbsoluteUri)
        {
            try
            {
                // The user opened this reel in the chat WebView. Apply that
                // gesture only to this viewer's active video, not every page.
                string parameters = JsonSerializer.Serialize(new
                {
                    expression = "window.__messengerActivateReelSound?.()",
                    userGesture = true
                });
                await current.View.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parameters);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // Leave Facebook's normal muted playback available if the
                // viewer closed or the browser could not apply the default.
            }
        }
        else if (message == "messenger-reel-close" && ReelReturnTracker.IsReel(source) &&
                 Uri.TryCreate(current.View.CoreWebView2.Source, UriKind.Absolute, out Uri? actual) &&
                 actual.AbsoluteUri == source!.AbsoluteUri)
        {
            CloseReel(current);
        }
    }

    private void CloseReel(OverlayHost current)
    {
        if (!reelIsOpen || overlay != current) return;
        reelIsOpen = false;
        reelGeneration++;
        SetOverlayArmed(false);
        current.EndLoading();
        current.Container.Visible = false;
        if (current.Ready)
        {
            try
            {
                // Stop audio immediately and replace the old page while retaining the control.
                current.View.CoreWebView2.IsMuted = true;
                current.View.CoreWebView2.Stop();
                current.View.CoreWebView2.Navigate("about:blank");
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // If the browser is no longer usable, recreate it on the next open.
                DiscardReel();
            }
        }
    }

    private void DiscardReel()
    {
        OverlayHost? current = overlay;
        overlay = null;
        reelInitialization = null;
        if (current is null) return;
        Controls.Remove(current.Container);
        current.Dispose();
    }

    private bool OverlayBusy => reelIsOpen || statusIsOpen;

    private async Task<CoreWebView2Environment> EnsureXEnvironmentAsync()
    {
        if (xEnvironment is not null) return xEnvironment;
        string dataFolder = ServiceCatalog.X.ProfileFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var options = new CoreWebView2EnvironmentOptions(BackgroundThrottling.BrowserArguments);
        xEnvironment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: dataFolder,
            options: options);
        return xEnvironment;
    }

    private Task EnsureStatusReadyAsync()
    {
        if (statusInitialization is not null) return statusInitialization;
        var current = new OverlayHost(ServiceCatalog.X, "Opening X");
        current.Container.Visible = false;
        statusOverlay = current;
        current.BackRequested += (_, _) => CloseStatus(current);
        Controls.Add(current.Container);
        _ = current.View.Handle;
        statusInitialization = InitializeStatusAsync(current);
        return statusInitialization;
    }

    private async Task InitializeStatusAsync(OverlayHost current)
    {
        CoreWebView2Environment environment = await EnsureXEnvironmentAsync();
        await current.View.EnsureCoreWebView2Async(environment);
        if (shuttingDown) return;
        current.View.CoreWebView2.WebMessageReceived += (_, e) => OnStatusMessage(current, e);
        await current.View.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(XOverlayScript.Source);
        current.View.CoreWebView2.NavigationStarting += (_, e) =>
        {
            if (statusOverlay == current && statusIsOpen) current.NavigationStarting(e.Uri, e.NavigationId);
        };
        current.View.CoreWebView2.DOMContentLoaded += (_, e) =>
        {
            if (statusOverlay == current && statusIsOpen) current.NotifyContentReady(e.NavigationId);
        };
        current.View.CoreWebView2.NavigationCompleted += (_, e) =>
        {
            if (statusOverlay != current || !statusIsOpen) return;
            if (!e.IsSuccess) current.NotifyNavigationFailed(e.NavigationId);
            else current.NotifyContentReady(e.NavigationId);
        };
        current.View.CoreWebView2.ProcessFailed += (_, _) =>
        {
            if (statusOverlay == current && statusIsOpen) current.NotifyProcessFailed();
        };
        if (!shuttingDown) current.Ready = true;
    }

    private async Task ShowStatusAsync(Uri target)
    {
        if (OverlayBusy || shuttingDown || !CurrentPageIsChat() || !XLinkCapture.TryCapture(target, out Uri? status) || status is null) return;
        statusIsOpen = true;
        SetOverlayArmed(true);
        long generation = ++statusGeneration;
        try
        {
            Task ready = EnsureStatusReadyAsync();
            OverlayHost current = statusOverlay!;
            current.BeginLoading(status);
            current.Container.Visible = true;
            current.Container.BringToFront();
            current.Container.PerformLayout();
            current.Present(this);
            await ready;
            if (shuttingDown || !statusIsOpen || statusGeneration != generation || statusOverlay != current || !current.Ready)
            {
                if (!shuttingDown && statusIsOpen && statusGeneration == generation && statusOverlay == current)
                    CloseStatus(current);
                else if (statusOverlay == current && !statusIsOpen)
                    current.EndLoading();
                if (!OverlayBusy) SetOverlayArmed(false);
                return;
            }
            current.View.CoreWebView2.IsMuted = false;
            current.View.CoreWebView2.Navigate(status.AbsoluteUri);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Leave the chat where it is. A failed overlay must not reload Messenger.
            if (!shuttingDown && statusIsOpen && statusGeneration == generation)
            {
                statusIsOpen = false;
                DiscardStatus();
                SetOverlayArmed(false);
            }
        }
    }

    private void CloseStatus(OverlayHost current)
    {
        if (!statusIsOpen || statusOverlay != current) return;
        statusIsOpen = false;
        statusGeneration++;
        SetOverlayArmed(false);
        current.EndLoading();
        current.Container.Visible = false;
        if (!current.Ready) return;
        try
        {
            current.View.CoreWebView2.IsMuted = true;
            current.View.CoreWebView2.Stop();
            current.View.CoreWebView2.Navigate("about:blank");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            DiscardStatus();
        }
    }

    private void DiscardStatus()
    {
        OverlayHost? current = statusOverlay;
        statusOverlay = null;
        statusInitialization = null;
        if (current is null) return;
        Controls.Remove(current.Container);
        current.Dispose();
    }

    private void OnStatusMessage(OverlayHost current, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!statusIsOpen || statusOverlay != current) return;
        string message;
        try { message = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { return; }
        if (OverlayBackPill.TryReadLogo(message, out LogoRect logo))
        {
            current.MoveToLogo(logo);
            return;
        }
        if (message == "messenger-overlay-escape") CloseStatus(current);
    }

    private void CloseOpenOverlay()
    {
        if (reelIsOpen && overlay is not null) CloseReel(overlay);
        else if (statusIsOpen && statusOverlay is not null) CloseStatus(statusOverlay);
    }

    private void SetOverlayArmed(bool armed)
    {
        if (browser.CoreWebView2 is null) return;
        try
        {
            string message = !armed
                ? "messenger-overlay-idle"
                : statusIsOpen ? "messenger-status-armed" : "messenger-overlay-armed";
            browser.CoreWebView2.PostWebMessageAsString(message);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The chat page can already be gone during shutdown.
        }
    }

    private void PostPreviewState()
    {
        if (shuttingDown || browser.CoreWebView2 is null) return;
        try
        {
            browser.CoreWebView2.PostWebMessageAsString(settings.XLinkPreviews
                ? "messenger-x-previews-on"
                : "messenger-x-previews-off");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The chat page can already be gone during shutdown.
        }
    }

    private async Task ReplyPreviewAsync(XPreviewRequest request)
    {
        // A card that already has a title or description is a real preview. Leave it, and do not fetch.
        if (!XPreviewCards.ShouldReplace(request.Observation))
        {
            if (!shuttingDown && !IsDisposed)
                PostPreviewResult(XPreviewMessage.Result(request.Url, null));
            return;
        }
        XPreviewCard? card = null;
        try
        {
            if (Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? clicked))
                card = await previews.TryGetAsync(clicked, settings.XLinkPreviews);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            card = null;
        }
        if (shuttingDown || IsDisposed) return;
        string payload = XPreviewMessage.Result(request.Url, card);
        if (InvokeRequired)
        {
            try { BeginInvoke(() => PostPreviewResult(payload)); }
            catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
            {
                // The form is already closing.
            }
            return;
        }
        PostPreviewResult(payload);
    }

    private void PostPreviewResult(string payload)
    {
        if (shuttingDown || browser.CoreWebView2 is null) return;
        try { browser.CoreWebView2.PostWebMessageAsJson(payload); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The chat page can already be gone during shutdown.
        }
    }

    public bool PreFilterMessage(ref Message m)
    {
        if (!statusIsOpen || statusOverlay is null) return false;
        if (!OverlayBackPill.IsXButton1Down(m.Msg, m.WParam)) return false;
        CloseStatus(statusOverlay);
        return true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys code = keyData & Keys.KeyCode;
        bool alt = (keyData & Keys.Alt) == Keys.Alt;
        bool control = (keyData & Keys.Control) == Keys.Control;
        bool shift = (keyData & Keys.Shift) == Keys.Shift;
        if (statusIsOpen && statusOverlay is not null && OverlayBackPill.IsBackKey((int)code, alt, control, shift))
        {
            CloseStatus(statusOverlay);
            return true;
        }
        if (keyData == Keys.Escape && reelIsOpen && overlay is not null)
        {
            CloseReel(overlay);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The title-bar X hides the window. WebView2 stays loaded so messages
        // keep arriving. Exit on the tray menu is what closes the process.
        if (!exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        shuttingDown = true;
        Application.RemoveMessageFilter(this);
        reactionTimer.Stop();
        reactionTimer.Dispose();
        backupTimer.Stop();
        backupTimer.Dispose();
        DisposeTray();
        reelGeneration++;
        statusGeneration++;
        if (overlay is not null && reelIsOpen) CloseReel(overlay);
        DiscardReel();
        if (statusOverlay is not null && statusIsOpen) CloseStatus(statusOverlay);
        DiscardStatus();
        if (tray is not null)
        {
            tray.Visible = false;
            tray.Dispose();
            tray = null;
        }

        previews.Dispose();
        browser.Dispose();
        base.OnFormClosing(e);
    }

    private async Task ReturnToChatAsync(Uri destination, Uri reel)
    {
        CoreWebView2 view = browser.CoreWebView2;
        long revision = sourceRevision;
        bool IsCurrent() => !IsDisposed && !Disposing && sourceRevision == revision &&
            Uri.TryCreate(view.Source, UriKind.Absolute, out Uri? source) && source.AbsoluteUri == reel.AbsoluteUri;

        try
        {
            string history = await view.CallDevToolsProtocolMethodAsync("Page.getNavigationHistory", "{}");
            if (!IsCurrent()) return;
            int? entryId = HistoryEntrySelector.Select(history, reel, destination);
            if (entryId is not null)
            {
                await view.CallDevToolsProtocolMethodAsync("Page.navigateToHistoryEntry", JsonSerializer.Serialize(new { entryId }));
                return;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The protocol may not be available or the history entry may have disappeared.
            // Fall back to the known chat only if the user has not navigated elsewhere.
        }

        if (IsCurrent()) view.Navigate(destination.AbsoluteUri);
    }

    private void UpdateReelCloseState() => browser.CoreWebView2.PostWebMessageAsString(
        reelReturn.CanReturn ? "messenger-reel-enable" : "messenger-reel-disable");

    private void WireNotifications(CoreWebView2 view)
    {
        view.PermissionRequested += OnPermissionRequested;
        view.NotificationReceived += OnNotificationReceived;
        view.WebMessageReceived += OnNotificationWebMessage;
        view.FrameCreated += OnNotificationFrameCreated;
    }

    private void OnNotificationFrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs e)
    {
        // Messenger asks for notification permission from an iframe. WebView2
        // denies that permission when the frame event is left at Default, and
        // the page then never constructs a notification.
        e.Frame.PermissionRequested += OnPermissionRequested;
    }

    private async Task GrantNotificationPermissionsAsync()
    {
        CoreWebView2Profile profile = browser.CoreWebView2.Profile;
        try
        {
            profile.AreWebViewScriptApisEnabledForServiceWorkers = true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // An older runtime has no service-worker script switch. The page shim still runs.
        }

        foreach (string origin in NotificationOrigins.GrantedOrigins)
        {
            try
            {
                await profile.SetPermissionStateAsync(
                    CoreWebView2PermissionKind.Notifications,
                    origin,
                    CoreWebView2PermissionState.Allow);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // PermissionRequested still answers the prompt for this origin.
            }
        }

        await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(NotificationShim.Source);
        await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ChatListWatch.Source);
    }

    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (e.PermissionKind != CoreWebView2PermissionKind.Notifications) return;
        bool allow = Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? origin) && NotificationOrigins.IsAllowed(origin);
        e.State = allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
        e.SavesInProfile = true;
    }

    private void OnNotificationReceived(object? sender, CoreWebView2NotificationReceivedEventArgs e)
    {
        // Handled must be set before ReportShown, and the page waits on this handler.
        // This event is only raised for a non-persistent Notification() call.
        e.Handled = true;
        CoreWebView2Notification notification = e.Notification;
        try
        {
            string origin = NotificationOrigin(e.SenderOrigin, sender as CoreWebView2);
            if (origin.Length == 0)
            {
                notification.ReportShown();
                notification.ReportClosed();
                return;
            }

            string icon = NotificationToast.PreferredImageUri(
                ReadNotification(() => notification.IconUri),
                ReadNotification(() => notification.BadgeUri)) ?? "";
            NotificationRecord message = NotificationNormalizer.Normalize(
                ReadNotification(() => notification.Title),
                ReadNotification(() => notification.Body),
                icon,
                ReadNotification(() => notification.Tag),
                url: null,
                origin);
            DateTimeOffset now = DateTimeOffset.Now;
            bool show = notifications.ShouldToastMessage(message, now);
            if (show)
                summary.Observe(message, now);
            notification.ReportShown();
            if (!show)
            {
                notification.ReportClosed();
                return;
            }

            ApplyTrayAppearance();
            RefreshFlyout();
            if (summary.IsMuted(now))
            {
                notification.ReportClosed();
                return;
            }

            if (message.Tag.Length == 0) untaggedWebNotification = notification;
            else taggedWebNotifications[message.Tag] = notification;
            PresentMessage(message);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A page notification must not take down the chat host.
        }
    }

    private void OnNotificationWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (sender is not CoreWebView2) return;
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out Uri? source) || !NotificationOrigins.IsAllowed(source)) return;
        string raw;
        try { raw = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { return; }
        if (ChatListMessage.TryParse(raw, out bool chatListReadable, out IReadOnlyList<ChatThread> threads))
        {
            if (chatListReadable)
                DeliverChatList(threads, HiddenTraySource.PageEvent);
            else
            {
                summary.NoteChatList(false, threads, DateTimeOffset.Now);
                ApplyTrayAppearance();
                RefreshFlyout();
            }

            return;
        }

        if (settings.WatchChatListForReactions && ChatListMessage.TryParse(raw, out ChatListActivity? activity))
        {
            NoteChatListReaction(activity);
            return;
        }

        if (!NotificationPageMessage.TryParse(raw, out NotificationPageFields fields)) return;
        string icon = NotificationToast.PreferredImageUri(fields.IconUri, fields.BadgeUri) ?? "";
        NotificationRecord message = NotificationNormalizer.Normalize(
            fields.Title,
            fields.Body,
            icon,
            fields.Tag,
            fields.Url,
            source.AbsoluteUri);
        DateTimeOffset now = DateTimeOffset.Now;
        if (!notifications.ShouldToastMessage(message, now)) return;
        summary.Observe(message, now);
        ApplyTrayAppearance();
        RefreshFlyout();
        if (summary.IsMuted(now)) return;
        PresentMessage(message);
    }

    private static string NotificationOrigin(string? senderOrigin, CoreWebView2? view)
    {
        if (Uri.TryCreate(senderOrigin, UriKind.Absolute, out Uri? origin) && NotificationOrigins.IsAllowed(origin))
            return origin.AbsoluteUri;
        if (view is not null && Uri.TryCreate(view.Source, UriKind.Absolute, out Uri? page) && NotificationOrigins.IsAllowed(page))
            return page.GetLeftPart(UriPartial.Authority);
        return "";
    }

    private static string ReadNotification(Func<string?> read)
    {
        try { return read() ?? ""; }
        catch (Exception error) when (error is not OutOfMemoryException) { return ""; }
    }

    private void PresentMessage(NotificationRecord message)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        if (hiddenAlerts.SuppressPage(message, now))
            return;
        hiddenAlerts.NotePage(message, now);
        string icon = message.Icon;
        string appIcon = AppIcon();
        _ = PresentMessageAsync(message, icon, appIcon, playSound: !WindowIsForeground);
    }

    private async Task PresentMessageAsync(NotificationRecord message, string icon, string appIcon, bool playSound)
    {
        string? downloaded = null;
        try
        {
            if (NotificationToast.IsFetchable(icon))
                downloaded = await NotificationImageCache.DownloadAsync(icon, NotificationImageCache.DefaultDirectory);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            downloaded = null;
        }

        NotificationToastContent content = NotificationToast.Map(
            message.Title,
            message.Body,
            downloaded,
            appIcon,
            message.Url,
            message.Tag);
        void Show()
        {
            if (shuttingDown || IsDisposed) return;
            Trace.WriteLine(HiddenTrayAlerts.LogLine(
                DateTimeOffset.Now,
                HiddenTraySource.PageEvent,
                HiddenTrayAlerts.ThreadKey(content.Url, content.Title),
                content.ToastGroup == ToastTags.ReactionGroup));
            if (!osToastsReady || !OsToast.TryShow(content))
                ShowBalloon(content);
            if (playSound)
                NotificationSound.Play();
        }

        try
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(Show);
            else Show();
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
        {
            // The form is already closing.
        }
    }

    private string AppIcon()
    {
        if (appIconPath is not null) return appIconPath;
        appIconPath = AppIconCache.TryCreate(exePath);
        return appIconPath;
    }

    private void OnDocumentTitleChanged(object? sender, object e)
    {
        if (shuttingDown || browser.CoreWebView2 is null) return;
        ApplyUnread(UnreadTitle.Parse(browser.CoreWebView2.DocumentTitle));
        if (!watchTitles) return;
        DateTimeOffset now = DateTimeOffset.Now;
        if (notifications.ShouldToastActivity(unreadCount, now))
            NoteTitleActivity(now);

        ArmReactionTimer(now);
    }

    private void ApplyUnread(int count)
    {
        unreadCount = count;
        summary.NotePageUnread(count);
        ApplyTrayAppearance();
        RefreshFlyout();
        TaskbarBadge.Apply(IsHandleCreated ? Handle : IntPtr.Zero, count);
    }

    private void OnReactionTimer(object? sender, EventArgs e)
    {
        reactionTimer.Stop();
        if (shuttingDown || !watchTitles) return;
        DateTimeOffset now = DateTimeOffset.Now;
        if (notifications.ShouldToastActivity(unreadCount, now))
            NoteTitleActivity(now);

        ArmReactionTimer(now);
    }

    private void NoteTitleActivity(DateTimeOffset now)
    {
        if (chatListReactions.CountReactionRise(now))
            summary.NoteActivity(now);
        ApplyTrayAppearance();
        RefreshFlyout();
        if (!summary.IsMuted(now))
            ShowActivity();
    }

    private void NoteChatListReaction(ChatListActivity activity)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        ChatListEffect effect = chatListReactions.Observe(activity.Href, activity.Preview, now);
        if (effect == ChatListEffect.Ignored) return;
        summary.NoteChatListActivity(activity.Name, activity.Preview, activity.Href, now, effect == ChatListEffect.Count);
        ApplyTrayAppearance();
        RefreshFlyout();
    }

    private async Task ApplyChatListWatchAsync()
    {
        if (shuttingDown || browser.CoreWebView2 is null) return;
        try
        {
            if (settings.WatchChatListForReactions)
            {
                if (chatListScriptId is null)
                    chatListScriptId = await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ChatListWatch.EnableReactions);
                if (!settings.WatchChatListForReactions || shuttingDown || browser.CoreWebView2 is null)
                {
                    await DetachChatListScriptAsync();
                    return;
                }
                if (initialNavigationStarted && browser.CoreWebView2 is not null)
                    await browser.CoreWebView2.ExecuteScriptAsync(ChatListWatch.EnableReactions);
                if (!settings.WatchChatListForReactions)
                    await DetachChatListScriptAsync();
                return;
            }

            await DetachChatListScriptAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The page can navigate away while the script is being added or removed.
        }
    }

    private async Task DetachChatListScriptAsync()
    {
        if (browser.CoreWebView2 is null) return;
        if (chatListScriptId is not null)
        {
            browser.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(chatListScriptId);
            chatListScriptId = null;
        }

        if (initialNavigationStarted)
            await browser.CoreWebView2.ExecuteScriptAsync(ChatListWatch.DisableReactions);
    }

    private void ArmReactionTimer(DateTimeOffset now)
    {
        TimeSpan? due = notifications.ActivityDueIn(now);
        if (due is null)
        {
            reactionTimer.Stop();
            return;
        }

        int milliseconds = (int)Math.Clamp(due.Value.TotalMilliseconds, 50, 60_000);
        reactionTimer.Interval = Math.Max(milliseconds, 1);
        reactionTimer.Start();
    }

    private void ShowActivity()
    {
        // While the window is not the foreground, the chat-list poll owns the
        // toast. The title heuristic still fills the tray summary above.
        if (!WindowIsForeground) return;
        DateTimeOffset now = DateTimeOffset.Now;
        ReactionToast toast = summary.TryReactionToast(now, out ReactionToast parsed)
            ? parsed
            : new ReactionToast("Messenger", ReactionNotice.FallbackBody, "");
        NotificationToastContent content = NotificationToast.Map(
            toast.Title,
            toast.Body,
            null,
            AppIcon(),
            toast.Url,
            "reaction",
            reaction: true);
        if (!osToastsReady || !OsToast.TryShow(content))
            ShowBalloon(content);
    }

    private void ShowBalloon(NotificationToastContent content)
    {
        if (tray is null || !NotificationToast.CanShow(content)) return;
        balloonUrl = content.Url;
        balloonTag = content.Tag;
        string title = Clip(content.Title, 63);
        string body = Clip(content.Body, 255);
        if (title.Length == 0 || body.Length == 0) return;
        try { tray.ShowBalloonTip(5000, title, body, ToolTipIcon.Info); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The tray icon can already be gone during shutdown.
        }
    }

    private async Task PollChatListAsync()
    {
        if (pollingChatList || shuttingDown || IsDisposed || browser.CoreWebView2 is null) return;
        pollingChatList = true;
        try
        {
            string executed = await browser.CoreWebView2.ExecuteScriptAsync(ChatListWatch.ReadInvocation);
            if (shuttingDown || IsDisposed) return;
            if (!HiddenTrayAlerts.TryReadScriptResult(executed, out bool readable, out IReadOnlyList<ChatThread> threads) || !readable)
                return;
            DeliverChatList(threads, HiddenTraySource.BackupTimer);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The page can navigate away while the backup read is in flight.
        }
        finally
        {
            pollingChatList = false;
        }
    }

    private void DeliverChatList(IReadOnlyList<ChatThread> threads, HiddenTraySource source)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        summary.NoteChatList(true, threads, now);
        IReadOnlyList<HiddenTrayNotice> notices = hiddenAlerts.Observe(
            threads,
            WindowIsForeground,
            summary.IsMuted(now),
            now,
            source);
        ApplyTrayAppearance();
        RefreshFlyout();
        foreach (HiddenTrayNotice notice in notices)
            RaiseHidden(notice);
    }

    private void RaiseHidden(HiddenTrayNotice notice)
    {
        if (shuttingDown || IsDisposed) return;
        Trace.WriteLine(notice.Log);
        string icon = notice.Toast.ImagePath.Length > 0 ? notice.Toast.ImagePath : AppIcon();
        NotificationToastContent content = notice.Toast with { ImagePath = icon };
        if (!osToastsReady || !OsToast.TryShow(content))
            ShowBalloon(content);
        if (notice.Sound)
            NotificationSound.Play();
    }

    private static string Clip(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || max <= 0) return "";
        return value.Length <= max ? value : value[..max];
    }

    private void OnToastActivated(string? argument)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => OnToastActivated(argument)); }
            catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
            {
                // The form is already closing.
            }
            return;
        }

        (string url, string tag) = NotificationArguments.Parse(argument);
        OpenChat(url, tag);
        ReportWebClick(tag);
    }

    private void ReportWebClick(string tag)
    {
        CoreWebView2Notification? notification = null;
        if (tag.Length > 0) taggedWebNotifications.Remove(tag, out notification);
        else
        {
            notification = untaggedWebNotification;
            untaggedWebNotification = null;
        }

        if (notification is null) return;
        try { notification.ReportClicked(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The page already got the click navigation from us.
        }
    }

    private void OpenChat(string? url, string? tag)
    {
        if (overlay is not null && reelIsOpen) CloseReel(overlay);
        if (statusOverlay is not null && statusIsOpen) CloseStatus(statusOverlay);
        BringToForeground();
        Uri target = NotificationNavigation.OpenTarget(url, tag);
        summary.MarkChatRead(string.IsNullOrWhiteSpace(url) ? tag : url);
        summary.MarkChatRead(target.AbsoluteUri);
        ApplyTrayAppearance();
        // The first Source assignment happens after WebView2 init. A click
        // before that must not be overwritten by the inbox navigation.
        if (!initialNavigationStarted || browser.CoreWebView2 is null)
        {
            pendingChat = target;
            return;
        }

        if (!string.Equals(browser.CoreWebView2.Source, target.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
            browser.CoreWebView2.Navigate(target.AbsoluteUri);
    }
}
