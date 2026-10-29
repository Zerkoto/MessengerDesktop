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
        using SingleInstance lifetime = SingleInstance.Acquire();
        if (!lifetime.IsPrimary)
        {
            lifetime.SignalExisting();
            return;
        }

        ApplicationConfiguration.Initialize();
        string exePath = Application.ExecutablePath;
        var startup = new StartupRegistration(new WindowsRunKey());
        var settingsStore = new SettingsStore(SettingsStore.DefaultPath);
        SettingsLoadResult loaded = AppStartup.Configure(settingsStore, startup, exePath);
        Application.Run(new MessengerWindow(LaunchArgs.IsMinimized(args), lifetime, startup, exePath, settingsStore, loaded.Settings));
    }
}

internal sealed class MessengerWindow : Form
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
    private readonly System.Windows.Forms.Timer reactionTimer = new();
    private readonly Dictionary<string, CoreWebView2Notification> taggedWebNotifications = new(StringComparer.Ordinal);
    private CoreWebView2Environment? webEnvironment;
    private CoreWebView2Environment? xEnvironment;
    private OverlayHost? overlay;
    private OverlayHost? statusOverlay;
    private NotifyIcon? tray;
    private bool suppressStartupClick;
    private Task? reelInitialization;
    private Task? statusInitialization;
    private bool reelIsOpen;
    private bool statusIsOpen;
    private bool shuttingDown;
    private bool allowVisible;
    private bool exitRequested;
    private bool browserStarted;
    private bool initialNavigationStarted;
    private bool watchTitles;
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

    public MessengerWindow(bool startMinimized, SingleInstance lifetime, StartupRegistration startup, string exePath, SettingsStore settingsStore, AppSettings settings)
    {
        this.lifetime = lifetime;
        this.startup = startup;
        this.exePath = exePath;
        this.settingsStore = settingsStore;
        this.settings = settings;
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
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open");
        open.Click += (_, _) => BringToForeground();
        var startWithWindows = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        startWithWindows.Click += (_, _) =>
        {
            // CheckOnClick only flips the glyph. The Run value is the source of truth.
            if (suppressStartupClick) return;
            if (startup.IsEnabled()) startup.Disable();
            else startup.Enable(exePath);
            SetStartupCheck(startWithWindows, startup.IsEnabled());
        };
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitApplication();
        var xPreviews = new ToolStripMenuItem(SettingsStore.XLinkPreviewsMenuText)
        {
            CheckOnClick = true,
            Checked = settings.XLinkPreviews
        };
        xPreviews.Click += (_, _) =>
        {
            settings.XLinkPreviews = xPreviews.Checked;
            settingsStore.Save(settings);
            PostPreviewState();
        };
        menu.Items.Add(open);
        menu.Items.Add(startWithWindows);
        menu.Items.Add(xPreviews);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        menu.Opening += (_, _) =>
        {
            startup.TryHeal(exePath);
            SetStartupCheck(startWithWindows, startup.IsEnabled());
        };

        Icon? icon = Icon is null ? SystemIcons.Application : Icon;
        tray = new NotifyIcon
        {
            Icon = (Icon)icon.Clone(),
            Text = "Messenger",
            Visible = true,
            ContextMenuStrip = menu
        };
        tray.DoubleClick += (_, _) => BringToForeground();
        tray.BalloonTipClicked += (_, _) => OpenChat(balloonUrl, balloonTag);
    }

    private void SetStartupCheck(ToolStripMenuItem item, bool enabled)
    {
        suppressStartupClick = true;
        try
        {
            item.Checked = enabled;
        }
        finally
        {
            suppressStartupClick = false;
        }
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
            string dataFolder = ServiceCatalog.Facebook.ProfileFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
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
            await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(EmbeddedScripts.ChatStatus);
            await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(EmbeddedScripts.XLinkPreview);
            Uri start = pendingChat ?? ServiceCatalog.Facebook.Home;
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
            XPreviewMessage.TryReadRequest(e.WebMessageAsJson, out string? previewUrl) &&
            previewUrl is not null)
        {
            _ = ReplyPreviewAsync(previewUrl);
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
        await current.View.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(OverlayEscapeScript.Source);
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
            browser.CoreWebView2.PostWebMessageAsString(armed ? "messenger-overlay-armed" : "messenger-overlay-idle");
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

    private async Task ReplyPreviewAsync(string requestUrl)
    {
        XPreviewCard? card = null;
        try
        {
            if (Uri.TryCreate(requestUrl, UriKind.Absolute, out Uri? clicked))
                card = await previews.TryGetAsync(clicked, settings.XLinkPreviews);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            card = null;
        }
        if (shuttingDown || IsDisposed) return;
        string payload = XPreviewMessage.Result(requestUrl, card);
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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && OverlayBusy)
        {
            CloseOpenOverlay();
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
        reactionTimer.Stop();
        reactionTimer.Dispose();
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
            bool show = notifications.ShouldToastMessage(message, DateTimeOffset.Now);
            notification.ReportShown();
            if (!show)
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
        if (!NotificationPageMessage.TryParse(raw, out NotificationPageFields fields)) return;
        string icon = NotificationToast.PreferredImageUri(fields.IconUri, fields.BadgeUri) ?? "";
        NotificationRecord message = NotificationNormalizer.Normalize(
            fields.Title,
            fields.Body,
            icon,
            fields.Tag,
            fields.Url,
            source.AbsoluteUri);
        if (!notifications.ShouldToastMessage(message, DateTimeOffset.Now)) return;
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
        string icon = message.Icon;
        string appIcon = AppIcon();
        _ = PresentMessageAsync(message, icon, appIcon);
    }

    private async Task PresentMessageAsync(NotificationRecord message, string icon, string appIcon)
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
            Trace.WriteLine(NotificationToast.Describe(content));
            var shown = new NotificationRecord(content.Title, content.Body, message.Icon, content.Tag, content.Url, message.Service);
            if (!osToastsReady || !OsToast.TryShow(content.Title, content.Body, content.Url, content.Tag, content.ImagePath))
                ShowBalloon(shown);
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
            ShowActivity();
        ArmReactionTimer(now);
    }

    private void ApplyUnread(int count)
    {
        unreadCount = count;
        if (tray is not null)
        {
            try { tray.Text = UnreadTitle.Tooltip(count); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // The icon limit is enforced by the tooltip helper. Ignore a stale icon.
            }
        }

        TaskbarBadge.Apply(IsHandleCreated ? Handle : IntPtr.Zero, count);
    }

    private void OnReactionTimer(object? sender, EventArgs e)
    {
        reactionTimer.Stop();
        if (shuttingDown || !watchTitles) return;
        DateTimeOffset now = DateTimeOffset.Now;
        if (notifications.ShouldToastActivity(unreadCount, now))
            ShowActivity();
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
        Trace.WriteLine(NotificationToast.Describe(NotificationToastContent.Activity));
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
        if (statusOverlay is not null && statusIsOpen) CloseStatus(statusOverlay);
        BringToForeground();
        Uri target = NotificationNavigation.OpenTarget(url, tag);
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
