using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

namespace MessengerDesktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MessengerWindow());
    }
}

internal sealed class MessengerWindow : Form
{
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private readonly ReelReturnTracker reelReturn = new();
    private CoreWebView2Environment? webEnvironment;
    private ReelOverlay? overlay;
    private Task? reelInitialization;
    private bool reelIsOpen;
    private bool shuttingDown;
    private long reelGeneration;
    private Uri? fallbackReel;
    private long sourceRevision;

    // Keep the conversation alive when a reel link is followed from a chat.
    private const string ChatReelScript = """
        (() => {
            if (window !== window.top || location.protocol !== 'https:' || location.hostname !== 'www.facebook.com') return;
            window.addEventListener('click', event => {
                if (!event.isTrusted || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
                const path = location.pathname;
                if (!path.startsWith('/messages/t/') && !path.startsWith('/messages/e2ee/t/')) return;
                const target = event.target instanceof Element ? event.target : event.target?.parentElement;
                const link = target?.closest('a[href]');
                if (!link || link.hasAttribute('download')) return;
                const url = new URL(link.href);
                if (url.protocol !== 'https:' || url.hostname !== 'www.facebook.com' || url.port ||
                    (!url.pathname.startsWith('/share/r/') && !url.pathname.startsWith('/reel/'))) return;
                event.preventDefault();
                event.stopImmediatePropagation();
                chrome.webview.postMessage({ type: 'messenger-open-reel', url: url.href });
            }, true);
        })();
        """;

    // Facebook's reel viewer links back to the feed rather than the chat.
    // Only intercept that specific close link when the host has a chat to restore.
    private const string ReelCloseScript = """
        (() => {
            if (window !== window.top || location.protocol !== 'https:' || location.hostname !== 'www.facebook.com') return;
            let canReturn = false;
            chrome.webview.addEventListener('message', event => {
                if (event.data === 'messenger-reel-enable') canReturn = true;
                if (event.data === 'messenger-reel-disable') canReturn = false;
            });
            chrome.webview.postMessage('messenger-reel-ready');
            window.addEventListener('click', event => {
                if (!canReturn || !event.isTrusted || !location.pathname.startsWith('/reel/')) return;
                const target = event.target instanceof Element ? event.target : event.target?.parentElement;
                const link = target?.closest('a');
                if (!link || link.href !== 'https://www.facebook.com/') return;
                const label = 'Close viewer and return to feed';
                const labelled = link.getAttribute('aria-label') === label || link.getAttribute('title') === label ||
                    link.textContent.trim() === label || !!link.querySelector('[aria-label="Close viewer and return to feed"], [title="Close viewer and return to feed"]');
                if (!labelled) return;
                event.preventDefault();
                event.stopImmediatePropagation();
                canReturn = false;
                chrome.webview.postMessage('messenger-reel-close');
            }, true);
        })();
        """;

    public MessengerWindow()
    {
        Text = "Messenger";
        Width = 1200;
        Height = 800;
        MinimumSize = new Size(520, 400);
        WindowState = FormWindowState.Maximized;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Controls.Add(browser);
        Shown += InitializeBrowser;
    }

    private async void InitializeBrowser(object? sender, EventArgs e)
    {
        try
        {
            string dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MessengerDesktop", "WebView2");
            webEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
            await browser.EnsureCoreWebView2Async(webEnvironment);
            browser.CoreWebView2.SourceChanged += OnSourceChanged;
            browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            browser.CoreWebView2.NavigationStarting += OnChatNavigationStarting;
            browser.CoreWebView2.NewWindowRequested += OnChatNewWindowRequested;
            browser.CoreWebView2.NavigationCompleted += OnMainNavigationCompleted;
            await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ReelCloseScript);
            await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ChatReelScript);
            browser.Source = new Uri("https://www.facebook.com/messages/t");
        }
        catch (Exception error)
        {
            MessageBox.Show(this, $"Messenger could not start: {error.Message}", "Messenger", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        sourceRevision++;
        Uri? current = Uri.TryCreate(browser.CoreWebView2.Source, UriKind.Absolute, out Uri? parsed) ? parsed : null;
        reelReturn.Observe(current);
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
        current.Back.Click += (_, _) => CloseReel(current);
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
            // Show the page on both success and failure so browser errors remain visible.
            if (overlay == current && reelIsOpen) current.NotifyContentReady(e.NavigationId);
        };
        await current.View.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ReelCloseScript);
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

    private void OnReelMessage(ReelOverlay current, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!reelIsOpen || overlay != current || !Uri.TryCreate(e.Source, UriKind.Absolute, out Uri? source) || !ReelReturnTracker.IsFacebook(source)) return;
        string message;
        try { message = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { return; }
        if (message == "messenger-reel-ready")
        {
            current.View.CoreWebView2.PostWebMessageAsString("messenger-reel-enable");
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
        shuttingDown = true;
        reelGeneration++;
        if (overlay is not null && reelIsOpen) CloseReel(overlay);
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
}
