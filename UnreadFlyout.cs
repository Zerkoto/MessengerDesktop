using System.Net.Http;

namespace MessengerDesktop;

// Left-click summary. Mark all read is a local list clear: the callback must not
// navigate or touch the WebView. Opening a row is the only path that opens a chat.
internal sealed class UnreadFlyout : Form
{
    private static readonly HttpClient Avatars = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly TraySummary summary;
    private readonly Action markAllRead;
    private readonly Action openMessenger;
    private readonly Action<TrayRow> openRow;
    private readonly Panel listHost;
    private bool closeOnDeactivate;
    private bool closing;

    public UnreadFlyout(TraySummary summary, Action markAllRead, Action openMessenger, Action<TrayRow> openRow)
    {
        this.summary = summary;
        this.markAllRead = markAllRead;
        this.openMessenger = openMessenger;
        this.openRow = openRow;

        TrayTheme theme = TrayTheme.Current;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Width = TrayMetrics.PanelWidth;
        BackColor = theme.Background;
        ForeColor = theme.Text;
        Font = SystemFonts.MessageBoxFont;
        AccessibleName = TrayText.PanelTitle;
        Padding = new Padding(1);

        var header = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = theme.Background };
        var title = new Label
        {
            Text = TrayText.PanelTitle,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 0, 0),
            ForeColor = theme.Text,
            Font = new Font(Font, FontStyle.Bold),
            BackColor = theme.Background
        };
        var markAll = new Button
        {
            Text = TrayText.MarkAllRead,
            AutoSize = true,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            ForeColor = theme.Muted,
            BackColor = theme.Background,
            Cursor = Cursors.Hand,
            Padding = new Padding(8, 0, 8, 0)
        };
        markAll.FlatAppearance.BorderSize = 0;
        markAll.Click += (_, _) =>
        {
            // Local tray rows only. Do not navigate, inject a click, or mark Facebook read.
            this.markAllRead();
            RefreshRows();
        };
        header.Controls.Add(title);
        header.Controls.Add(markAll);

        var footer = new Button
        {
            Text = TrayMenuModel.Text(TrayMenuEntryKind.Open),
            Dock = DockStyle.Bottom,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            BackColor = theme.Hover,
            ForeColor = theme.Text,
            Cursor = Cursors.Hand,
            Font = new Font(Font, FontStyle.Bold)
        };
        footer.FlatAppearance.BorderSize = 0;
        footer.Click += (_, _) => this.openMessenger();

        listHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = theme.Background
        };

        Controls.Add(listHost);
        Controls.Add(footer);
        Controls.Add(header);
        RefreshRows();
    }

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
        TrayWindowChrome.ApplyCorners(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        closeOnDeactivate = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        closing = true;
        base.OnFormClosing(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (closeOnDeactivate && !closing && !IsDisposed)
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(TrayTheme.Current.Separator);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    public void ShowAbove(Point anchor)
    {
        RefreshRows();
        Rectangle area = Screen.FromPoint(anchor).WorkingArea;
        TrayPoint origin = TrayFlyoutPlacement.Place(
            anchor.X,
            anchor.Y,
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
        TrayTheme theme = TrayTheme.Current;
        listHost.SuspendLayout();
        while (listHost.Controls.Count > 0)
        {
            Control child = listHost.Controls[0];
            listHost.Controls.RemoveAt(0);
            child.Dispose();
        }
        IReadOnlyList<TrayRow> rows = summary.Latest();
        if (rows.Count == 0)
        {
            listHost.Controls.Add(new Label
            {
                Text = TrayText.EmptyState,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = theme.Muted,
                BackColor = theme.Background
            });
        }
        else
        {
            int y = 0;
            foreach (TrayRow row in rows)
            {
                Control card = CreateRow(row, theme);
                card.Location = new Point(0, y);
                card.Width = TrayMetrics.PanelWidth - 2;
                listHost.Controls.Add(card);
                y += card.Height;
            }
        }

        listHost.ResumeLayout();
        int content = rows.Count == 0 ? 120 : rows.Count * TrayMetrics.PanelRowHeight;
        int height = Math.Min(TrayMetrics.PanelMaxHeight, 48 + 40 + content + 2);
        Height = height;
    }

    private Control CreateRow(TrayRow row, TrayTheme theme)
    {
        var card = new Panel
        {
            Height = TrayMetrics.PanelRowHeight,
            BackColor = theme.Background,
            Cursor = Cursors.Hand,
            AccessibleName = row.Name
        };
        var avatar = new AvatarBadge(row.Name, row.AvatarUrl, theme)
        {
            Location = new Point(12, 10),
            Size = new Size(36, 36)
        };
        var name = new Label
        {
            Text = row.Name,
            AutoEllipsis = true,
            AutoSize = false,
            Location = new Point(58, 8),
            Size = new Size(190, 18),
            ForeColor = theme.Text,
            BackColor = theme.Background,
            Font = new Font(Font, FontStyle.Bold)
        };
        var secondary = new Label
        {
            Text = row.Secondary,
            AutoEllipsis = true,
            AutoSize = false,
            Location = new Point(58, 28),
            Size = new Size(190, 18),
            ForeColor = theme.Muted,
            BackColor = theme.Background
        };
        var time = new Label
        {
            Text = TrayText.Relative(row.At, DateTimeOffset.Now),
            AutoSize = false,
            TextAlign = ContentAlignment.TopRight,
            Location = new Point(250, 8),
            Size = new Size(76, 18),
            ForeColor = theme.Muted,
            BackColor = theme.Background
        };
        card.Controls.Add(avatar);
        card.Controls.Add(name);
        card.Controls.Add(secondary);
        card.Controls.Add(time);
        void Enter(object? sender, EventArgs e)
        {
            card.BackColor = theme.Hover;
            foreach (Control child in card.Controls)
                child.BackColor = theme.Hover;
        }
        void Leave(object? sender, EventArgs e)
        {
            Point cursor = card.PointToClient(Cursor.Position);
            if (card.ClientRectangle.Contains(cursor)) return;
            card.BackColor = theme.Background;
            foreach (Control child in card.Controls)
                child.BackColor = theme.Background;
        }
        void Clicked(object? sender, EventArgs e) => openRow(row);
        card.MouseEnter += Enter;
        card.MouseLeave += Leave;
        card.Click += Clicked;
        foreach (Control child in card.Controls)
        {
            child.MouseEnter += Enter;
            child.Click += Clicked;
            child.Cursor = Cursors.Hand;
        }

        return card;
    }

    private sealed class AvatarBadge : Panel
    {
        private readonly string initials;
        private readonly TrayTheme theme;
        private Image? photo;

        public AvatarBadge(string name, string? url, TrayTheme theme)
        {
            this.initials = TrayText.Initials(name);
            this.theme = theme;
            AccessibleName = initials;
            if (!string.IsNullOrWhiteSpace(url))
                _ = LoadAsync(url);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddEllipse(bounds);
            e.Graphics.SetClip(path);
            if (photo is not null)
            {
                e.Graphics.DrawImage(photo, bounds);
            }
            else
            {
                using var fill = new SolidBrush(theme.Dark ? Color.FromArgb(0x3C, 0x40, 0x43) : Color.FromArgb(0xE8, 0xEA, 0xED));
                using var text = new SolidBrush(theme.Dark ? Color.White : Color.FromArgb(0x3C, 0x40, 0x43));
                using var font = new Font(Font.FontFamily, 9f, FontStyle.Bold, GraphicsUnit.Point);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.FillEllipse(fill, bounds);
                e.Graphics.DrawString(initials, font, text, bounds, format);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                photo?.Dispose();
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
    }
}
