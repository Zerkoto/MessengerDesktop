using System.ComponentModel;
using System.Drawing.Drawing2D;
using Microsoft.Web.WebView2.WinForms;

namespace MessengerDesktop;

// Cover, spinner, 8 second timeout, and Esc/close for one service URL.
// Reel close and history stay with the reel service.
internal sealed class OverlayHost : IDisposable
{
    private const int FadeSteps = 12;
    private const string FirstPaintScript =
        "new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve(true))))";

    private readonly ReelLoadCoordinator coordinator = new();
    private readonly LoadingCover cover;
    private readonly bool failureBack;
    private readonly XBackPill? pill;
    private LogoRect? logo;
    private readonly System.Windows.Forms.Timer fadeTimer = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer spinnerTimer = new() { Interval = 40 };
    private readonly Button back = new() { Text = "Back to chat", Dock = DockStyle.Bottom, Height = 36, Visible = false };
    private CancellationTokenSource? timeoutWait;
    private Form? owner;
    private EventHandler? layoutHandler;
    private int fadeEpoch;
    private int fadeStep;
    private bool disposed;

    public Panel Container { get; } = new() { Dock = DockStyle.Fill };
    public WebView2 View { get; } = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(24, 25, 26) };
    public ServiceDefinition Service { get; }
    public Uri? Target { get; private set; }
    public event EventHandler? BackRequested;
    public bool Ready { get; set; }

    public OverlayHost(ServiceDefinition service, string openingTitle)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(openingTitle);
        Service = service;
        failureBack = service.Surface != ServiceSurface.Overlay;
        cover = new LoadingCover(openingTitle);
        if (service.Surface == ServiceSurface.Overlay)
        {
            pill = new XBackPill();
            pill.BackClicked += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        }
        Container.Controls.Add(View);
        Container.Controls.Add(back);
        // Fill the remaining space after the failure-only button is docked.
        View.BringToFront();
        back.Click += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        fadeTimer.Tick += OnFadeTick;
        spinnerTimer.Tick += (_, _) => cover.AdvanceSpinner();
        Container.Disposed += (_, _) => Dispose();
    }

    public void BeginLoading(Uri target)
    {
        if (disposed) return;
        Target = target;
        logo = null;
        back.Visible = false;
        fadeTimer.Stop();
        coordinator.Begin(target);
        ArmTimeout();
        if (cover.Visible) cover.Opacity = 1;
        AlignOverlay();
    }

    public void Present(Form ownerForm)
    {
        if (disposed) return;
        Hook(ownerForm);
        if (Container.Visible) Container.PerformLayout();
        if (!View.IsDisposed) _ = View.Handle;
        AlignOverlay();
        if (!Container.IsDisposed && Container.IsHandleCreated)
        {
            try { Container.BeginInvoke(AlignOverlay); }
            catch (InvalidOperationException) { }
        }
    }

    public void NavigationStarting(string source, ulong navigationId)
    {
        if (disposed) return;
        if (!coordinator.NoteNavigationStarting(source, navigationId)) return;
        fadeTimer.Stop();
        ArmTimeout();
        if (cover.Visible) cover.Opacity = 1;
        AlignOverlay();
    }

    public void MoveToLogo(LogoRect mark)
    {
        if (disposed || pill is null) return;
        logo = mark;
        AlignPill();
    }

    public void NotifyContentReady(ulong navigationId)
    {
        if (disposed) return;
        int? epoch = coordinator.ArmContentReady(navigationId);
        if (epoch is null) return;
        _ = FadeAfterPaintAsync(epoch.Value);
    }

    public void NotifyNavigationFailed(ulong navigationId)
    {
        if (disposed || !coordinator.NoteNavigationFailed(navigationId)) return;
        if (failureBack) back.Visible = true;
        DismissCover();
    }

    public void NotifyProcessFailed()
    {
        if (disposed) return;
        coordinator.NoteProcessFailed();
        if (failureBack) back.Visible = true;
        DismissCover();
    }

    public void EndLoading()
    {
        if (disposed) return;
        back.Visible = false;
        fadeTimer.Stop();
        CancelTimeout();
        coordinator.End();
        HideCover();
        if (pill is { IsDisposed: false, Visible: true }) pill.Hide();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        fadeTimer.Stop();
        spinnerTimer.Stop();
        CancelTimeout();
        Unhook();
        coordinator.End();
        fadeTimer.Dispose();
        spinnerTimer.Dispose();
        pill?.Dispose();
        if (!cover.IsDisposed)
        {
            cover.AllowClose = true;
            cover.Dispose();
        }
        if (!Container.IsDisposed) Container.Dispose();
    }

    private async Task FadeAfterPaintAsync(int epoch)
    {
        try
        {
            if (View.CoreWebView2 is not null)
            {
                Task<string> paint = View.CoreWebView2.ExecuteScriptAsync(FirstPaintScript);
                Task finished = await Task.WhenAny(paint, Task.Delay(TimeSpan.FromSeconds(2)));
                if (finished == paint) await paint;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The page can close while the paint check is running.
        }

        if (!disposed) StartFade(epoch);
    }

    private void ArmTimeout()
    {
        CancelTimeout();
        var wait = new CancellationTokenSource();
        timeoutWait = wait;
        _ = ExpireAfterAsync(coordinator.Epoch, wait.Token);
    }

    private async Task ExpireAfterAsync(int epoch, CancellationToken token)
    {
        try
        {
            await Task.Delay(ReelLoadCoordinator.MaxWaitMilliseconds, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!disposed && !token.IsCancellationRequested && coordinator.Epoch == epoch && coordinator.Visual == ReelLoadVisual.Visible)
        {
            // If loading stalls, keep a way back without covering a usable page.
            if (failureBack) back.Visible = true;
            StartFade(epoch);
        }
    }

    private void CancelTimeout()
    {
        if (timeoutWait is null) return;
        timeoutWait.Cancel();
        timeoutWait.Dispose();
        timeoutWait = null;
    }

    private void DismissCover()
    {
        fadeTimer.Stop();
        CancelTimeout();
        HideCover();
    }

    private void StartFade(int epoch)
    {
        if (disposed || !coordinator.TryBeginFade(epoch)) return;
        CancelTimeout();
        fadeEpoch = epoch;
        fadeStep = 0;
        if (cover.IsDisposed || !cover.Visible)
        {
            coordinator.FinishFade(epoch);
            HideCover();
            return;
        }
        fadeTimer.Start();
    }

    private void OnFadeTick(object? sender, EventArgs e)
    {
        if (disposed || !coordinator.IsFading(fadeEpoch))
        {
            fadeTimer.Stop();
            return;
        }

        fadeStep++;
        if (fadeStep >= FadeSteps)
        {
            fadeTimer.Stop();
            coordinator.FinishFade(fadeEpoch);
            HideCover();
            return;
        }

        try
        {
            cover.Opacity = 1d - (fadeStep / (double)FadeSteps);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            fadeTimer.Stop();
            coordinator.FinishFade(fadeEpoch);
            HideCover();
        }
    }

    private void Hook(Form form)
    {
        if (owner == form) return;
        Unhook();
        owner = form;
        layoutHandler = (_, _) => AlignOverlay();
        form.Move += layoutHandler;
        form.Resize += layoutHandler;
        form.VisibleChanged += layoutHandler;
        View.SizeChanged += layoutHandler;
        View.LocationChanged += layoutHandler;
        Container.VisibleChanged += layoutHandler;
    }

    private void Unhook()
    {
        if (layoutHandler is null) return;
        if (owner is not null)
        {
            owner.Move -= layoutHandler;
            owner.Resize -= layoutHandler;
            owner.VisibleChanged -= layoutHandler;
        }
        if (!View.IsDisposed)
        {
            View.SizeChanged -= layoutHandler;
            View.LocationChanged -= layoutHandler;
        }
        if (!Container.IsDisposed)
            Container.VisibleChanged -= layoutHandler;
        layoutHandler = null;
        owner = null;
    }

    private void AlignOverlay()
    {
        AlignCover();
        AlignPill();
    }

    private void AlignPill()
    {
        if (pill is null || pill.IsDisposed) return;
        Form? form = owner;
        bool canPlace = form is not null
            && !form.IsDisposed
            && form.Visible
            && form.WindowState != FormWindowState.Minimized
            && !Container.IsDisposed
            && Container.Visible
            && !View.IsDisposed
            && View.IsHandleCreated;
        if (!canPlace)
        {
            if (pill.Visible) pill.Hide();
            return;
        }

        Rectangle view = View.RectangleToScreen(View.ClientRectangle);
        if (view.Width <= 0 || view.Height <= 0)
        {
            if (pill.Visible) pill.Hide();
            return;
        }

        int dpi = View.DeviceDpi;
        int height = OverlayBackPill.HeightPixels(dpi);
        int width = pill.BodyWidth(dpi);
        (int x, int y) = OverlayBackPill.Place(view.Width, view.Height, width, height, OverlayBackPill.MarginPixels(dpi), logo);
        pill.ShowAt(form!, new Rectangle(view.X + x, view.Y + y, width, height), dpi);
    }

    private void AlignCover()
    {
        if (disposed || cover.IsDisposed || Container.IsDisposed) return;

        bool wantsCover = coordinator.Visual is ReelLoadVisual.Visible or ReelLoadVisual.Fading;
        Form? form = owner;
        bool canPlace = wantsCover
            && form is not null
            && !form.IsDisposed
            && form.Visible
            && form.WindowState != FormWindowState.Minimized
            && Container.Visible
            && View.IsHandleCreated;

        if (!canPlace)
        {
            if (!wantsCover) HideCover();
            else if (cover.Visible) cover.Hide();
            return;
        }

        Rectangle bounds = View.RectangleToScreen(View.ClientRectangle);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            if (cover.Visible) cover.Hide();
            return;
        }

        cover.Bounds = bounds;
        if (!cover.Visible)
        {
            if (coordinator.Visual == ReelLoadVisual.Visible) cover.Opacity = 1;
            cover.Show(form);
        }

        if (coordinator.Visual == ReelLoadVisual.Visible && !spinnerTimer.Enabled)
            spinnerTimer.Start();
    }

    private void HideCover()
    {
        spinnerTimer.Stop();
        if (cover.IsDisposed || !cover.Visible) return;
        cover.Hide();
        cover.Opacity = 1;
    }

    // WebView2 owns a child window that paints over ordinary WinForms siblings.
    // A separate owned window is what actually covers that surface.
    private sealed class LoadingCover : Form
    {
        private readonly SpinnerCanvas canvas;

        public LoadingCover(string openingTitle)
        {
            canvas = new SpinnerCanvas(openingTitle + "\u2026") { Dock = DockStyle.Fill };
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ControlBox = false;
            ShowIcon = false;
            Text = openingTitle;
            BackColor = Color.FromArgb(24, 25, 26);
            Controls.Add(canvas);
        }

        [System.ComponentModel.DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool AllowClose { get; set; }

        public void AdvanceSpinner() => canvas.Advance();

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return parameters;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }

    private sealed class SpinnerCanvas : Panel
    {
        private readonly string message;
        private float angle;

        public SpinnerCanvas(string message)
        {
            this.message = message;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(24, 25, 26);
            FontFamily family = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
            Font = new Font(family, 12f, FontStyle.Regular, GraphicsUnit.Point);
        }

        public void Advance()
        {
            angle = (angle + 24f) % 360f;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = DeviceDpi / 96f;
            int diameter = (int)Math.Round(36 * scale);
            int gap = (int)Math.Round(10 * scale);
            Size textSize = TextRenderer.MeasureText(e.Graphics, message, Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int textHeight = textSize.Height + (int)Math.Ceiling(4 * scale);
            int top = Math.Max(0, (Height - diameter - gap - textHeight) / 2);
            var circle = new Rectangle((Width - diameter) / 2, top, diameter, diameter);
            using var track = new Pen(Color.FromArgb(70, 255, 255, 255), 4 * scale);
            using var arc = new Pen(Color.White, 4 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawEllipse(track, circle);
            e.Graphics.DrawArc(arc, circle, angle, 100);
            TextRenderer.DrawText(
                e.Graphics,
                message,
                Font,
                new Rectangle(0, circle.Bottom + gap, Width, textHeight),
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }
}
