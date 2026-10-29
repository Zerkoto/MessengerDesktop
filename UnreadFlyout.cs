using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace MessengerDesktop;

internal readonly record struct FlyoutPalette(
    Color Background,
    Color Text,
    Color Muted,
    Color Accent,
    Color AccentText,
    Color Hover,
    Color Pill,
    Color PillText,
    Color Divider,
    Color AvatarFill,
    Color AvatarText,
    Color Check,
    bool IsDark)
{
    public static FlyoutPalette Light { get; } = new(
        Color.White,
        Color.FromArgb(0x1A, 0x1A, 0x1A),
        Color.FromArgb(0x6B, 0x6B, 0x6B),
        Color.FromArgb(0x0F, 0x6C, 0xBD),
        Color.White,
        Color.FromArgb(0xF3, 0xF3, 0xF3),
        Color.FromArgb(0xF0, 0xF0, 0xF0),
        Color.FromArgb(0x5A, 0x5A, 0x5A),
        Color.FromArgb(0xE5, 0xE5, 0xE5),
        Color.FromArgb(0xE8, 0xEA, 0xED),
        Color.FromArgb(0x3C, 0x40, 0x43),
        Color.FromArgb(0x10, 0x7C, 0x10),
        false);

    public static FlyoutPalette Dark { get; } = new(
        Color.FromArgb(0x20, 0x20, 0x20),
        Color.FromArgb(0xF3, 0xF3, 0xF3),
        Color.FromArgb(0xAD, 0xAD, 0xAD),
        Color.FromArgb(0x4C, 0xC2, 0xFF),
        Color.FromArgb(0x1A, 0x1A, 0x1A),
        Color.FromArgb(0x2C, 0x2C, 0x2C),
        Color.FromArgb(0x3A, 0x3A, 0x3A),
        Color.FromArgb(0xD0, 0xD0, 0xD0),
        Color.FromArgb(0x3A, 0x3A, 0x3A),
        Color.FromArgb(0x3C, 0x40, 0x43),
        Color.White,
        Color.FromArgb(0x10, 0x7C, 0x10),
        true);

    public static FlyoutPalette System()
    {
        try
        {
            object? value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            if (value is int light && light == 0) return Dark;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A locked-down registry still gets the light palette.
        }

        return Light;
    }
}

// Left-click summary. Mark all read is a local list clear: the callback must not
// navigate or touch the WebView. Opening a row is the only path that opens a chat.
internal sealed class UnreadFlyout : Form
{
    private static readonly HttpClient Avatars = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly TraySummary summary;
    private readonly Action markAllRead;
    private readonly Action openMessenger;
    private readonly Action<TrayRow> openRow;
    private readonly int? lockedDpi;
    private readonly Panel listHost;
    private readonly Panel header;
    private readonly Panel footer;
    private readonly Button openButton;
    private FlyoutPalette palette;
    private FlyoutLayout.Metrics metrics;
    private FlyoutSlot markAllSlot;
    private bool closeOnDeactivate;
    private bool closing;
    private Font? titleFont;
    private Font? linkFont;
    private Font? pillFont;

    public UnreadFlyout(
        TraySummary summary,
        Action markAllRead,
        Action openMessenger,
        Action<TrayRow> openRow,
        FlyoutPalette? palette = null,
        int? lockedDpi = null)
    {
        this.summary = summary;
        this.markAllRead = markAllRead;
        this.openMessenger = openMessenger;
        this.openRow = openRow;
        this.palette = palette ?? FlyoutPalette.System();
        this.lockedDpi = lockedDpi is > 0 ? lockedDpi : null;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        AccessibleName = TrayText.PanelTitle;
        Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

        header = new Panel { Dock = DockStyle.Top };
        header.Paint += PaintHeader;
        header.MouseMove += HeaderMouseMove;
        header.MouseLeave += (_, _) => header.Cursor = Cursors.Default;
        header.MouseClick += HeaderClick;

        footer = new Panel { Dock = DockStyle.Bottom };
        footer.Paint += PaintFooter;
        openButton = new Button
        {
            Text = FlyoutText.Open,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = true
        };
        openButton.FlatAppearance.BorderSize = 0;
        openButton.Click += (_, _) => this.openMessenger();
        footer.Controls.Add(openButton);
        AcceptButton = openButton;

        listHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        Controls.Add(listHost);
        Controls.Add(footer);
        Controls.Add(header);
        ApplyMetrics();
        RefreshRows();
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool CloseWhenDeactivated { get; set; } = true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ClassStyle |= 0x00020000;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyCorners(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        closeOnDeactivate = CloseWhenDeactivated;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (lockedDpi is not null) return;
        ApplyMetrics();
        RefreshRows();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        closing = true;
        base.OnFormClosing(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (closeOnDeactivate && CloseWhenDeactivated && !closing && !IsDisposed)
            Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }

    public void ShowAbove(Rectangle icon)
    {
        RefreshRows();
        if (icon.Width <= 0 || icon.Height <= 0)
        {
            Point cursor = Cursor.Position;
            icon = new Rectangle(cursor.X, cursor.Y, 1, 1);
        }

        Rectangle area = Screen.FromRectangle(icon).WorkingArea;
        TrayPoint origin = TrayFlyoutPlacement.Place(
            icon.Left,
            icon.Top,
            icon.Right,
            icon.Bottom,
            Width,
            Height,
            area.Left,
            area.Top,
            area.Right,
            area.Bottom);
        Location = new Point(origin.X, origin.Y);
        Show();
        Activate();
    }

    public void RefreshRows()
    {
        ApplyMetrics();
        IReadOnlyList<TrayRow> rows = summary.Latest();
        listHost.SuspendLayout();
        while (listHost.Controls.Count > 0)
        {
            Control child = listHost.Controls[0];
            listHost.Controls.RemoveAt(0);
            child.Dispose();
        }

        FlyoutLayout.EmptyArrangement? empty = null;
        if (rows.Count == 0)
        {
            empty = BuildEmpty();
            listHost.Controls.Add(new EmptyState(palette, metrics, empty.Value));
        }
        else
        {
            int y = 0;
            foreach (TrayRow row in rows)
            {
                var card = new ConversationRow(row, palette, metrics, () => openRow(row));
                card.Location = new Point(0, y);
                card.Width = metrics.Width;
                card.Height = metrics.RowHeight;
                listHost.Controls.Add(card);
                y += metrics.RowHeight;
            }
        }

        listHost.ResumeLayout();
        int body = rows.Count == 0 ? empty!.Value.Height : rows.Count * metrics.RowHeight;
        int height = FlyoutLayout.PreferredHeight(metrics, rows.Count, body);
        ClientSize = new Size(metrics.Width, height);
        header.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            titleFont?.Dispose();
            linkFont?.Dispose();
            pillFont?.Dispose();
            titleFont = null;
            linkFont = null;
            pillFont = null;
        }
    }

    private int CurrentDpi => lockedDpi ?? (DeviceDpi > 0 ? DeviceDpi : 96);

    private void ApplyMetrics()
    {
        metrics = FlyoutLayout.At(CurrentDpi);
        BackColor = palette.Background;
        ForeColor = palette.Text;
        header.Height = metrics.HeaderHeight;
        header.BackColor = palette.Background;
        footer.Height = FlyoutLayout.FooterHeight(metrics);
        footer.BackColor = palette.Background;
        listHost.BackColor = palette.Background;
        Width = metrics.Width;

        Font nextTitle = PixelFont("Segoe UI Semibold", FlyoutLayout.FontPixels(14, metrics.Dpi), FontStyle.Bold);
        Font nextLink = PixelFont("Segoe UI", FlyoutLayout.FontPixels(12, metrics.Dpi), FontStyle.Regular);
        Font nextPill = PixelFont("Segoe UI", FlyoutLayout.FontPixels(11, metrics.Dpi), FontStyle.Regular);
        Font? previousTitle = titleFont;
        Font? previousLink = linkFont;
        Font? previousPill = pillFont;
        titleFont = nextTitle;
        linkFont = nextLink;
        pillFont = nextPill;

        int count = summary.Latest().Count;
        string pill = count > 0 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        Size titleSize = TextRenderer.MeasureText(TrayText.PanelTitle, titleFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        Size linkSize = TextRenderer.MeasureText(TrayText.MarkAllRead, linkFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        int pillWidth = 0;
        int pillHeight = 0;
        if (pill.Length > 0)
        {
            Size pillText = TextRenderer.MeasureText(pill, pillFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int padX = FlyoutLayout.Px(8, metrics.Dpi);
            int padY = FlyoutLayout.Px(2, metrics.Dpi);
            pillWidth = pillText.Width + (padX * 2);
            pillHeight = pillText.Height + (padY * 2);
        }

        FlyoutLayout.HeaderArrangement arrangement = FlyoutLayout.Header(
            metrics,
            titleSize.Width,
            titleSize.Height,
            pillWidth,
            pillHeight,
            linkSize.Width,
            linkSize.Height);
        markAllSlot = arrangement.Link;

        FlyoutSlot button = FlyoutLayout.FooterButton(metrics);
        openButton.Bounds = new Rectangle(button.X, button.Y + FlyoutLayout.Px(1, metrics.Dpi), button.Width, button.Height);
        openButton.Font = titleFont;
        openButton.BackColor = palette.Accent;
        openButton.ForeColor = palette.AccentText;
        int radius = Math.Max(2, metrics.ButtonRadius);
        IntPtr region = CreateRoundRectRgn(0, 0, openButton.Width + 1, openButton.Height + 1, radius * 2, radius * 2);
        openButton.Region = Region.FromHrgn(region);
        DeleteObject(region);
        previousTitle?.Dispose();
        previousLink?.Dispose();
        previousPill?.Dispose();
    }

    private FlyoutLayout.EmptyArrangement BuildEmpty()
    {
        using Font heading = PixelFont("Segoe UI Semibold", FlyoutLayout.FontPixels(14, metrics.Dpi), FontStyle.Bold);
        using Font hint = PixelFont("Segoe UI", FlyoutLayout.FontPixels(12, metrics.Dpi), FontStyle.Regular);
        Size title = TextRenderer.MeasureText(FlyoutText.CaughtUp, heading, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        Size sub = TextRenderer.MeasureText(FlyoutText.CaughtUpHint, hint, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return FlyoutLayout.Empty(metrics, title.Width, title.Height, sub.Width, sub.Height);
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(palette.Background);
        int count = summary.Latest().Count;
        string pill = count > 0 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        Size titleSize = TextRenderer.MeasureText(TrayText.PanelTitle, titleFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        Size linkSize = TextRenderer.MeasureText(TrayText.MarkAllRead, linkFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        int pillWidth = 0;
        int pillHeight = 0;
        Size pillText = Size.Empty;
        if (pill.Length > 0 && pillFont is not null)
        {
            pillText = TextRenderer.MeasureText(pill, pillFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int padX = FlyoutLayout.Px(8, metrics.Dpi);
            int padY = FlyoutLayout.Px(2, metrics.Dpi);
            pillWidth = pillText.Width + (padX * 2);
            pillHeight = pillText.Height + (padY * 2);
        }

        FlyoutLayout.HeaderArrangement arrangement = FlyoutLayout.Header(
            metrics,
            titleSize.Width,
            titleSize.Height,
            pillWidth,
            pillHeight,
            linkSize.Width,
            linkSize.Height);
        markAllSlot = arrangement.Link;
        TextRenderer.DrawText(e.Graphics, TrayText.PanelTitle, titleFont, Slot(arrangement.Title), palette.Text, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (arrangement.PillVisible && pillFont is not null)
        {
            using var path = Rounded(Slot(arrangement.Pill), FlyoutLayout.Px(8, metrics.Dpi));
            using var fill = new SolidBrush(palette.Pill);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(fill, path);
            TextRenderer.DrawText(e.Graphics, pill, pillFont, Slot(arrangement.Pill), palette.PillText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        TextRenderer.DrawText(e.Graphics, TrayText.MarkAllRead, linkFont, Slot(arrangement.Link), palette.Accent, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    private void PaintFooter(object? sender, PaintEventArgs e)
    {
        using var pen = new Pen(palette.Divider);
        e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
    }

    private void HeaderMouseMove(object? sender, MouseEventArgs e)
    {
        header.Cursor = markAllSlot.Width > 0 && Slot(markAllSlot).Contains(e.Location) ? Cursors.Hand : Cursors.Default;
    }

    private void HeaderClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !Slot(markAllSlot).Contains(e.Location)) return;
        // Local tray rows only. Do not navigate, inject a click, or mark Facebook read.
        markAllRead();
        RefreshRows();
    }

    private static Rectangle Slot(FlyoutSlot slot) => new(slot.X, slot.Y, slot.Width, slot.Height);

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Font PixelFont(string family, int pixels, FontStyle fallbackStyle)
    {
        try
        {
            var font = new Font(family, pixels, FontStyle.Regular, GraphicsUnit.Pixel);
            if (font.Name.Contains("Segoe", StringComparison.OrdinalIgnoreCase) || font.FontFamily.Name.Equals(family, StringComparison.OrdinalIgnoreCase))
                return font;
            font.Dispose();
        }
        catch (ArgumentException)
        {
            // The named family is missing. The fallback below is a real face.
        }

        return new Font(FontFamily.GenericSansSerif, pixels, fallbackStyle, GraphicsUnit.Pixel);
    }

    private static void ApplyCorners(IntPtr handle)
    {
        if (!OperatingSystem.IsWindows()) return;
        int preference = 2;
        try
        {
            DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            // Windows 10 keeps the square frame. The drop shadow still comes from the class style.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    private sealed class EmptyState : Control
    {
        private readonly FlyoutPalette palette;
        private readonly FlyoutLayout.Metrics metrics;
        private readonly FlyoutLayout.EmptyArrangement arrangement;

        public EmptyState(FlyoutPalette palette, FlyoutLayout.Metrics metrics, FlyoutLayout.EmptyArrangement arrangement)
        {
            this.palette = palette;
            this.metrics = metrics;
            this.arrangement = arrangement;
            Dock = DockStyle.Fill;
            BackColor = palette.Background;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(palette.Background);
            Rectangle check = new(arrangement.Check.X, arrangement.Check.Y, arrangement.Check.Width, arrangement.Check.Height);
            using var fill = new SolidBrush(palette.Check);
            e.Graphics.FillEllipse(fill, check);
            DrawCheck(e.Graphics, check);
            using Font heading = PixelFont("Segoe UI Semibold", FlyoutLayout.FontPixels(14, metrics.Dpi), FontStyle.Bold);
            using Font hint = PixelFont("Segoe UI", FlyoutLayout.FontPixels(12, metrics.Dpi), FontStyle.Regular);
            TextRenderer.DrawText(e.Graphics, FlyoutText.CaughtUp, heading, new Rectangle(0, arrangement.Title.Y, Width, arrangement.Title.Height), palette.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, FlyoutText.CaughtUpHint, hint, new Rectangle(0, arrangement.Hint.Y, Width, arrangement.Hint.Height), palette.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private static void DrawCheck(Graphics graphics, Rectangle bounds)
        {
            using var pen = new Pen(Color.White, Math.Max(2f, bounds.Width / 12f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            float x = bounds.X;
            float y = bounds.Y;
            float w = bounds.Width;
            float h = bounds.Height;
            graphics.DrawLines(pen, new[]
            {
                new PointF(x + (w * 0.28f), y + (h * 0.52f)),
                new PointF(x + (w * 0.44f), y + (h * 0.68f)),
                new PointF(x + (w * 0.74f), y + (h * 0.36f))
            });
        }
    }

    private sealed class ConversationRow : Control
    {
        private readonly TrayRow row;
        private readonly FlyoutPalette palette;
        private readonly FlyoutLayout.Metrics metrics;
        private readonly Action open;
        private readonly string secondary;
        private readonly string time;
        private bool hover;
        private Image? photo;

        public ConversationRow(TrayRow row, FlyoutPalette palette, FlyoutLayout.Metrics metrics, Action open)
        {
            this.row = row;
            this.palette = palette;
            this.metrics = metrics;
            this.open = open;
            secondary = FlyoutText.Secondary(row);
            time = TrayText.Relative(row.At, DateTimeOffset.Now);
            Height = metrics.RowHeight;
            BackColor = palette.Background;
            Cursor = Cursors.Hand;
            AccessibleName = row.Name;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            if (!string.IsNullOrWhiteSpace(row.AvatarUrl))
                _ = LoadAsync(row.AvatarUrl);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnClick(EventArgs e)
        {
            open();
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(palette.Background);
            using Font nameFont = PixelFont("Segoe UI Semibold", FlyoutLayout.FontPixels(13, metrics.Dpi), FontStyle.Bold);
            using Font bodyFont = PixelFont("Segoe UI", FlyoutLayout.FontPixels(12, metrics.Dpi), FontStyle.Regular);
            Size nameSize = TextRenderer.MeasureText(row.Name, nameFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Size previewSize = TextRenderer.MeasureText(secondary, bodyFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Size timeSize = TextRenderer.MeasureText(time, bodyFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            FlyoutLayout.RowArrangement arrangement = FlyoutLayout.Row(
                metrics,
                nameSize.Width,
                nameSize.Height,
                previewSize.Width,
                previewSize.Height,
                timeSize.Width,
                timeSize.Height);
            if (hover)
            {
                using var path = Rounded(Slot(arrangement.Hover), metrics.HoverRadius);
                using var fill = new SolidBrush(palette.Hover);
                graphics.FillPath(fill, path);
            }

            Rectangle avatar = Slot(arrangement.Avatar);
            using (GraphicsPath clip = Circle(avatar))
                graphics.SetClip(clip);
            if (photo is not null)
                graphics.DrawImage(photo, avatar);
            else
            {
                using var fill = new SolidBrush(palette.AvatarFill);
                using var text = new SolidBrush(palette.AvatarText);
                using Font initials = PixelFont("Segoe UI Semibold", Math.Max(8, metrics.Avatar / 3), FontStyle.Bold);
                graphics.FillEllipse(fill, avatar);
                graphics.DrawString(row.Initials, initials, text, avatar, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }

            graphics.ResetClip();
            Rectangle dot = Slot(arrangement.Dot);
            using (var dotBrush = new SolidBrush(palette.Accent))
            using (var ring = new Pen(palette.Background, Math.Max(1f, metrics.Dpi / 96f)))
            {
                graphics.FillEllipse(dotBrush, dot);
                graphics.DrawEllipse(ring, dot);
            }

            var flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(graphics, row.Name, nameFont, Slot(arrangement.Name), palette.Text, flags);
            TextRenderer.DrawText(graphics, secondary, bodyFont, Slot(arrangement.Preview), palette.Muted, flags);
            TextRenderer.DrawText(graphics, time, bodyFont, Slot(arrangement.Time), palette.Muted, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.Right);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) photo?.Dispose();
            base.Dispose(disposing);
        }

        private async Task LoadAsync(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
                return;
            try
            {
                using HttpResponseMessage response = await Avatars.GetAsync(uri).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return;
                if (response.Content.Headers.ContentLength is > 1_000_000) return;
                byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length == 0 || bytes.Length > 1_000_000) return;
                using var stream = new MemoryStream(bytes);
                using Image downloaded = Image.FromStream(stream);
                var copy = new Bitmap(downloaded);
                if (IsDisposed)
                {
                    copy.Dispose();
                    return;
                }

                void Apply()
                {
                    if (IsDisposed)
                    {
                        copy.Dispose();
                        return;
                    }

                    photo?.Dispose();
                    photo = copy;
                    Invalidate();
                }

                if (InvokeRequired) BeginInvoke(Apply);
                else Apply();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // Initials stay up when the notification icon cannot be loaded.
            }
        }

        private static GraphicsPath Circle(Rectangle bounds)
        {
            var path = new GraphicsPath();
            path.AddEllipse(bounds);
            return path;
        }
    }
}
