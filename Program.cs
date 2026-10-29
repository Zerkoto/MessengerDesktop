using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

namespace MessengerDesktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using SingleInstance lifetime = SingleInstance.Acquire();
        if (!lifetime.IsPrimary)
        {
            lifetime.SignalExisting();
            return;
        }

        ApplicationConfiguration.Initialize();
        string exePath = Application.ExecutablePath;
        var startup = new StartupRegistration(new WindowsRunKey());
        AppStartup.Configure(new SettingsStore(SettingsStore.DefaultPath), startup, exePath);
        Application.Run(new MessengerWindow(LaunchArgs.IsMinimized(args), lifetime, startup, exePath));
    }
}

internal sealed class MessengerWindow : Form
{
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private readonly ReelReturnTracker reelReturn = new();
    private readonly SingleInstance lifetime;
    private readonly StartupRegistration startup;
    private readonly string exePath;
    private readonly ToastBridge toasts = new();
    private readonly NotificationPipeline notifications = new(ReactionWatch.DefaultWindow);
    private readonly System.Windows.Forms.Timer reactionTimer = new();
    private readonly Dictionary<string, CoreWebView2Notification> taggedWebNotifications = new(StringComparer.Ordinal);
    private CoreWebView2Environment? webEnvironment;
    private ReelOverlay? overlay;
    private NotifyIcon? tray;
    private readonly TraySummary summary = new();
    private readonly System.Windows.Forms.Timer trayClickTimer = new();
    private readonly System.Windows.Forms.Timer muteTimer = new();
    private MessengerTrayMenu? trayMenu;
    private UnreadFlyout? flyout;
    private Icon? trayBaseIcon;
    private Icon? trayComposedIcon;
    private DateTime trayDoubleClickedUtc = DateTime.MinValue;
    private bool suppressStartupClick;
    private Task? reelInitialization;
    private bool reelIsOpen;
    private bool shuttingDown;
    private bool allowVisible;
    private bool exitRequested;
    private bool browserStarted;
    private bool initialNavigationStarted;
    private bool watchTitles;
    private bool osToastsReady;
    private int unreadCount;
    private string balloonUrl = "";
    private string balloonTag = "";
    private Uri? pendingChat;
    private CoreWebView2Notification? untaggedWebNotification;
    private long reelGeneration;
    private Uri? fallbackReel;
    private long sourceRevision;
    private FormWindowState restoreState = FormWindowState.Maximized;

    public MessengerWindow(bool startMinimized, SingleInstance lifetime, StartupRegistration startup, string exePath)
    {
        this.lifetime = lifetime;
        this.startup = startup;
        this.exePath = exePath;
        allowVisible = !startMinimized;
        Text = "Messenger";
        Width = 1200;
        Height = 800;
        MinimumSize = new Size(520, 400);
        WindowState = startMinimized ? FormWindowState.Minimized : FormWindowState.Maximized;
        if (startMinimized) ShowInTaskbar = false;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        Controls.Add(browser);
        CreateTray();
        reactionTimer.Tick += OnReactionTimer;
        toasts.Attach(OnToastActivated);
        osToastsReady = OsToast.TryEnable(toasts);
    }

    // A minimized launch must not flash the window. Application.Run still calls
    // Show; this keeps the form hidden until Open or a second launch.
    protected override void SetVisibleCore(bool value)
    {
        if (!allowVisible)
        {
            value = false;
            if (!IsHandleCreated) CreateHandle();
        }

        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        lifetime.Listen(RequestActivate);
        if (browserStarted) return;
        browserStarted = true;
        InitializeBrowser();
    }

    private void CreateTray()
    {
        Icon? icon = Icon is null ? SystemIcons.Application : Icon;
        trayBaseIcon = (Icon)icon.Clone();
        trayMenu = new MessengerTrayMenu(
            trayBaseIcon,
            () =>
            {
                CloseFlyout();
                BringToForeground();
            },
            ExitApplication,
            ToggleStartupFromMenu,
            ChooseMute);
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

    private void ToggleStartupFromMenu()
    {
        // CheckOnClick only flips the glyph. The Run value is the source of truth.
        if (suppressStartupClick) return;
        if (startup.IsEnabled()) startup.Disable();
        else startup.Enable(exePath);
        BindTrayMenu();
    }

    private void ChooseMute(TrayMute choice)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        if (summary.MuteChoice(now) == choice) summary.Unmute();
        else summary.Mute(choice, now);
        ApplyTrayAppearance();
        BindTrayMenu();
    }

    private void ShowTrayMenu()
    {
        if (tray is null || trayMenu is null) return;
        CloseFlyout();
        startup.TryHeal(exePath);
        BindTrayMenu();
        TrayWindowChrome.Show(tray, trayMenu);
    }

    private void BindTrayMenu()
    {
        if (trayMenu is null) return;
        suppressStartupClick = true;
        try
        {
            trayMenu.Bind(summary, DateTimeOffset.Now, startup.IsEnabled());
        }
        finally
        {
            suppressStartupClick = false;
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
        panel.ShowAbove(Cursor.Position);
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
        Icon composed = TrayBadgePainter.Compose(trayBaseIcon, summary.GlyphAt(now), summary.BadgeLabel);
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
        trayMenu?.Dispose();
        trayMenu = null;
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
        allowVisible = true;
        ShowInTaskbar = true;
        if (WindowState == FormWindowState.Minimized)
            WindowState = restoreState;
        Show();
        WindowState = restoreState;
        Activate();
        NativeForeground.BringToFront(Handle);
        TaskbarBadge.Apply(Handle, unreadCount);
    }

    private void HideToTray()
    {
        if (WindowState != FormWindowState.Minimized)
            restoreState = WindowState;
        allowVisible = false;
        ShowInTaskbar = false;
        Hide();
    }

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
            string dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MessengerDesktop", "WebView2");
            var options = new CoreWebView2EnvironmentOptions(BackgroundThrottling.BrowserArguments);
            webEnvironment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: dataFolder,
                options: options);
            await browser.EnsureCoreWebView2Async(webEnvironment);
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
            Uri start = pendingChat ?? new Uri("https://www.facebook.com/messages/t");
            pendingChat = null;
            initialNavigationStarted = true;
            browser.Source = start;
        }
        catch (Exception error)
        {
            if (exitRequested || shuttingDown) return;
            allowVisible = true;
            ShowInTaskbar = true;
            Show();
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
        if (current is not null && ReelReturnTracker.IsChat(current))
        {
            summary.MarkChatRead(current.AbsoluteUri);
            ApplyTrayAppearance();
            RefreshFlyout();
        }

        UpdateReelCloseState();
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
        if (!reelIsOpen && CurrentPageIsChat() && IsReelTarget(target))
        {
            e.Cancel = true;
            _ = ShowReelAsync(target);
        }
    }

    private void OnChatNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!reelIsOpen && e.IsUserInitiated && CurrentPageIsChat() &&
            Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? target) && IsReelTarget(target))
        {
            e.Handled = true;
            _ = ShowReelAsync(target);
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out Uri? source) || !ReelReturnTracker.IsFacebook(source)) return;
        if (ReelReturnTracker.IsChat(source) && CurrentPageIsChat() &&
            Uri.TryCreate(browser.CoreWebView2.Source, UriKind.Absolute, out Uri? liveChat) && liveChat.AbsoluteUri == source!.AbsoluteUri &&
            ReelMessage.TryReadTarget(e.WebMessageAsJson, out Uri? target) && !reelIsOpen)
        {
            await ShowReelAsync(target!);
            return;
        }
        string message;
        try { message = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { return; }
        if (message == "messenger-reel-ready")
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
        // unreads do not become a "New activity" toast. A later increase can.
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
        var current = new ReelOverlay();
        current.Container.Visible = false;
        overlay = current;
        current.BackRequested += (_, _) => CloseReel(current);
        Controls.Add(current.Container);
        // Explicitly create the hidden control's window before WebView2 initialization.
        _ = current.View.Handle;
        reelInitialization = InitializeReelAsync(current, webEnvironment);
        return reelInitialization;
    }

    private async Task InitializeReelAsync(ReelOverlay current, CoreWebView2Environment environment)
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
        if (!shuttingDown) current.Ready = true;
    }

    private async Task ShowReelAsync(Uri target)
    {
        if (reelIsOpen || shuttingDown || webEnvironment is null || !CurrentPageIsChat() || !IsReelTarget(target)) return;
        string originalChat = browser.CoreWebView2.Source;
        reelIsOpen = true;
        long generation = ++reelGeneration;
        try
        {
            Task ready = EnsureReelReadyAsync();
            ReelOverlay current = overlay!;
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
                if (browser.CoreWebView2.Source == originalChat)
                {
                    fallbackReel = target;
                    browser.CoreWebView2.Navigate(target.AbsoluteUri);
                }
            }
        }
    }

    private async void OnReelMessage(ReelOverlay current, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!reelIsOpen || overlay != current || !Uri.TryCreate(e.Source, UriKind.Absolute, out Uri? source) || !ReelReturnTracker.IsFacebook(source)) return;
        string message;
        try { message = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { return; }
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

    private void CloseReel(ReelOverlay current)
    {
        if (!reelIsOpen || overlay != current) return;
        reelIsOpen = false;
        reelGeneration++;
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
        ReelOverlay? current = overlay;
        overlay = null;
        reelInitialization = null;
        if (current is null) return;
        Controls.Remove(current.Container);
        current.Dispose();
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
        reactionTimer.Stop();
        reactionTimer.Dispose();
        DisposeTray();
        reelGeneration++;
        if (overlay is not null && reelIsOpen) CloseReel(overlay);
        DiscardReel();
        if (tray is not null)
        {
            tray.Visible = false;
            tray.Dispose();
            tray = null;
        }

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
    }

    private async Task GrantNotificationPermissionsAsync()
    {
        CoreWebView2Profile profile = browser.CoreWebView2.Profile;
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
        e.Handled = true;
        CoreWebView2Notification notification = e.Notification;
        try
        {
            if (!Uri.TryCreate(e.SenderOrigin, UriKind.Absolute, out Uri? origin) || !NotificationOrigins.IsAllowed(origin))
            {
                notification.ReportShown();
                notification.ReportClosed();
                return;
            }

            NotificationRecord message = NotificationNormalizer.Normalize(
                notification.Title,
                notification.Body,
                notification.IconUri,
                notification.Tag,
                url: null,
                e.SenderOrigin);
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
            if (!osToastsReady || !OsToast.TryShow(message.Title, message.Body, message.Url, message.Tag))
                ShowBalloon(message);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A page notification must not take down the chat host.
        }
    }

    private void OnDocumentTitleChanged(object? sender, object e)
    {
        if (shuttingDown || browser.CoreWebView2 is null) return;
        ApplyUnread(UnreadTitle.Parse(browser.CoreWebView2.DocumentTitle));
        if (!watchTitles) return;
        DateTimeOffset now = DateTimeOffset.Now;
        if (notifications.ShouldToastActivity(unreadCount, now))
        {
            summary.NoteActivity(now);
            ApplyTrayAppearance();
            RefreshFlyout();
            if (!summary.IsMuted(now))
                ShowActivity();
        }

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
        {
            summary.NoteActivity(now);
            ApplyTrayAppearance();
            RefreshFlyout();
            if (!summary.IsMuted(now))
                ShowActivity();
        }

        ArmReactionTimer(now);
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
        var message = new NotificationRecord("New activity", "", "", "", "", "facebook");
        if (!osToastsReady || !OsToast.TryShow(message.Title, message.Body, message.Url, message.Tag))
            ShowBalloon(message);
    }

    private void ShowBalloon(NotificationRecord message)
    {
        if (tray is null) return;
        balloonUrl = message.Url;
        balloonTag = message.Tag;
        string title = Clip(message.Title, 63);
        if (title.Length == 0) title = "Messenger";
        string body = Clip(message.Body, 255);
        if (body.Length == 0) body = "Messenger";
        try { tray.ShowBalloonTip(5000, title, body, ToolTipIcon.Info); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The tray icon can already be gone during shutdown.
        }
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
