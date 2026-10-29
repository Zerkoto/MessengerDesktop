using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MessengerDesktop;

internal readonly record struct TrayTheme(
    Color Background,
    Color Text,
    Color Muted,
    Color Hover,
    Color Separator,
    Color Check,
    bool Dark)
{
    public static TrayTheme Current => For(IsDarkMode());

    public static TrayTheme For(bool dark) => dark
        ? new TrayTheme(
            Color.FromArgb(0x2C, 0x2C, 0x2C),
            Color.FromArgb(0xFF, 0xFF, 0xFF),
            Color.FromArgb(0xAD, 0xAD, 0xAD),
            Color.FromArgb(0x3D, 0x3D, 0x3D),
            Color.FromArgb(0x3F, 0x3F, 0x3F),
            Color.White,
            true)
        : new TrayTheme(
            Color.White,
            Color.FromArgb(0x1A, 0x1A, 0x1A),
            Color.FromArgb(0x5C, 0x5C, 0x5C),
            Color.FromArgb(0xF3, 0xF3, 0xF3),
            Color.FromArgb(0xE5, 0xE5, 0xE5),
            Color.FromArgb(0x1A, 0x1A, 0x1A),
            false);

    private static bool IsDarkMode()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return false;
        }
    }
}

internal static class TrayWindowChrome
{
    public static bool IsWindows11() =>
        OperatingSystem.IsWindows() && Environment.OSVersion.Version.Build >= 22000;

    public static void ApplyCorners(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindows()) return;
        int preference = IsWindows11() ? 2 : 1;
        DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void Show(NotifyIcon icon, ToolStripDropDown menu)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(menu);
        SetForegroundWindow(NotifyWindow(icon));
        menu.Show(Cursor.Position);
    }

    private static IntPtr NotifyWindow(NotifyIcon icon)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? window = typeof(NotifyIcon).GetField("_window", flags)?.GetValue(icon)
            ?? typeof(NotifyIcon).GetField("window", flags)?.GetValue(icon);
        return window is NativeWindow native ? native.Handle : IntPtr.Zero;
    }
}

internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly TrayTheme theme;

    public TrayMenuRenderer(TrayTheme theme) : base(new TrayColorTable(theme))
    {
        this.theme = theme;
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(theme.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(theme.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || e.Item is TrayHeaderItem) return;
        Rectangle bounds = new(4, 1, Math.Max(0, e.Item.Width - 8), Math.Max(0, e.Item.Height - 2));
        using var brush = new SolidBrush(theme.Hover);
        e.Graphics.FillRectangle(brush, bounds);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Rectangle slot = e.ImageRectangle;
        int x = slot.Left + Math.Max(0, (slot.Width - 12) / 2);
        int y = slot.Top + (slot.Height / 2);
        using var pen = new Pen(theme.Check, 1.6f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        e.Graphics.DrawLines(pen,
        [
            new Point(x, y),
            new Point(x + 3, y + 4),
            new Point(x + 9, y - 4)
        ]);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.ContentRectangle.Top + (e.Item.ContentRectangle.Height / 2);
        using var pen = new Pen(theme.Separator);
        e.Graphics.DrawLine(pen, TrayMetrics.HorizontalPadding, y, e.Item.Width - TrayMetrics.HorizontalPadding, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = theme.Muted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? theme.Text : theme.Muted;
        base.OnRenderItemText(e);
    }

    private sealed class TrayColorTable : ProfessionalColorTable
    {
        private readonly TrayTheme theme;

        public TrayColorTable(TrayTheme theme) => this.theme = theme;

        public override Color ToolStripDropDownBackground => theme.Background;
        public override Color ImageMarginGradientBegin => theme.Background;
        public override Color ImageMarginGradientMiddle => theme.Background;
        public override Color ImageMarginGradientEnd => theme.Background;
        public override Color MenuItemSelected => theme.Hover;
        public override Color MenuItemBorder => theme.Hover;
        public override Color MenuItemSelectedGradientBegin => theme.Hover;
        public override Color MenuItemSelectedGradientEnd => theme.Hover;
        public override Color MenuItemPressedGradientBegin => theme.Hover;
        public override Color MenuItemPressedGradientEnd => theme.Hover;
        public override Color CheckBackground => theme.Background;
        public override Color CheckSelectedBackground => theme.Background;
        public override Color CheckPressedBackground => theme.Background;
        public override Color SeparatorDark => theme.Separator;
        public override Color SeparatorLight => theme.Separator;
        public override Color MenuBorder => theme.Separator;
    }
}

internal class ThemedDropDown : ToolStripDropDownMenu
{
    public ThemedDropDown()
    {
        ShowImageMargin = true;
        ShowCheckMargin = false;
        AutoSize = true;
        MinimumSize = new Size(TrayMetrics.MenuMinWidth, 0);
        Renderer = new TrayMenuRenderer(TrayTheme.Current);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            if (!TrayWindowChrome.IsWindows11())
                parameters.ClassStyle |= 0x00020000;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TrayWindowChrome.ApplyCorners(Handle);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        ApplyMetrics();
        base.OnLayout(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        Renderer = new TrayMenuRenderer(TrayTheme.Current);
        base.OnOpened(e);
    }

    private void ApplyMetrics()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = typeof(ToolStripDropDownMenu);
        type.GetField("_defaultImageMarginWidth", flags)?.SetValue(this, TrayMetrics.ImageColumn);
        type.GetField("_textPadding", flags)?.SetValue(this, new Padding(TrayMetrics.HorizontalPadding, 0, TrayMetrics.HorizontalPadding, 0));
    }
}

internal sealed class TrayHeaderItem : ToolStripLabel
{
    private readonly Icon? appIcon;

    public TrayHeaderItem(Icon? appIcon)
    {
        this.appIcon = appIcon;
        AutoSize = false;
        Text = TrayMenuModel.Text(TrayMenuEntryKind.Header);
        Subtitle = TrayText.Header(0, 0);
        AccessibleName = Text;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; }

    public override Size GetPreferredSize(Size constrainingSize) =>
        new(Math.Max(constrainingSize.Width, TrayMetrics.MenuMinWidth), TrayMetrics.HeaderHeight);

    protected override void OnPaint(PaintEventArgs e)
    {
        TrayTheme theme = TrayTheme.Current;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        const int iconSize = 16;
        int iconX = TrayMetrics.HorizontalPadding;
        int iconY = (Height - iconSize) / 2;
        if (appIcon is not null)
            e.Graphics.DrawIcon(appIcon, new Rectangle(iconX, iconY, iconSize, iconSize));

        int textX = iconX + iconSize + TrayMetrics.HorizontalPadding;
        int textWidth = Math.Max(0, Width - textX - TrayMetrics.HorizontalPadding);
        using var titleFont = new Font(Font, FontStyle.Bold);
        using var subtitleFont = new Font(Font.FontFamily, Math.Max(7f, Font.Size - 1f), FontStyle.Regular, Font.Unit);
        using var titleBrush = new SolidBrush(theme.Text);
        using var subtitleBrush = new SolidBrush(theme.Muted);
        e.Graphics.DrawString(Text, titleFont, titleBrush, new RectangleF(textX, 8, textWidth, 20));
        e.Graphics.DrawString(Subtitle, subtitleFont, subtitleBrush, new RectangleF(textX, 28, textWidth, 18));
    }
}

internal sealed class TrayActionItem : ToolStripMenuItem
{
    private bool ownsFont;

    public TrayActionItem(string text, bool bold = false)
    {
        Text = text;
        AutoSize = true;
        if (bold)
        {
            Font = new Font(Font, FontStyle.Bold);
            ownsFont = true;
        }
    }

    public override Size GetPreferredSize(Size constrainingSize)
    {
        Size size = base.GetPreferredSize(constrainingSize);
        return new Size(Math.Max(size.Width, TrayMetrics.MenuMinWidth), TrayMetrics.RowHeight);
    }

    protected override void Dispose(bool disposing)
    {
        Font? owned = disposing && ownsFont ? Font : null;
        ownsFont = false;
        base.Dispose(disposing);
        owned?.Dispose();
    }
}

internal sealed class MessengerTrayMenu : ThemedDropDown
{
    private readonly TrayHeaderItem header;
    private ToolStripMenuItem startupItem = null!;
    private readonly Dictionary<TrayMute, ToolStripMenuItem> muteItems = new();
    private bool binding;

    public MessengerTrayMenu(Icon? appIcon, Action open, Action quit, Action toggleStartup, Action<TrayMute> chooseMute)
    {
        header = new TrayHeaderItem(appIcon);
        var muteMenu = new ThemedDropDown();
        foreach (TrayMute choice in TrayMenuModel.MuteChoices)
        {
            TrayMute captured = choice;
            var item = new TrayActionItem(TrayMenuModel.MuteText(choice));
            item.Click += (_, _) =>
            {
                if (!binding) chooseMute(captured);
            };
            muteItems[choice] = item;
            muteMenu.Items.Add(item);
        }

        foreach (TrayMenuEntryKind kind in TrayMenuModel.Order)
        {
            switch (kind)
            {
                case TrayMenuEntryKind.Header:
                    Items.Add(header);
                    break;
                case TrayMenuEntryKind.Separator:
                    Items.Add(new ToolStripSeparator());
                    break;
                case TrayMenuEntryKind.Open:
                    var openItem = new TrayActionItem(TrayMenuModel.Text(kind), bold: true);
                    openItem.Click += (_, _) => open();
                    Items.Add(openItem);
                    break;
                case TrayMenuEntryKind.Mute:
                    var muteItem = new TrayActionItem(TrayMenuModel.Text(kind));
                    muteItem.DropDown = muteMenu;
                    Items.Add(muteItem);
                    break;
                case TrayMenuEntryKind.Startup:
                    startupItem = new TrayActionItem(TrayMenuModel.Text(kind)) { CheckOnClick = true };
                    startupItem.Click += (_, _) =>
                    {
                        if (!binding) toggleStartup();
                    };
                    Items.Add(startupItem);
                    break;
                case TrayMenuEntryKind.Quit:
                    var quitItem = new TrayActionItem(TrayMenuModel.Text(kind));
                    quitItem.Click += (_, _) => quit();
                    Items.Add(quitItem);
                    break;
            }
        }

        if (startupItem is null)
            throw new InvalidOperationException("The tray menu is missing Start with Windows.");
    }

    public void Bind(TraySummary summary, DateTimeOffset now, bool startWithWindows)
    {
        ArgumentNullException.ThrowIfNull(summary);
        binding = true;
        try
        {
            header.Subtitle = summary.HeaderLine;
            TrayMute active = summary.MuteChoice(now);
            foreach ((TrayMute choice, ToolStripMenuItem item) in muteItems)
                item.Checked = choice == active;
            startupItem.Checked = startWithWindows;
        }
        finally
        {
            binding = false;
        }
    }

}
