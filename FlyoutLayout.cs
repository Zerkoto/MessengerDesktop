namespace MessengerDesktop;

internal readonly record struct FlyoutSlot(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Overlaps(FlyoutSlot other) =>
        Width > 0 && Height > 0 && other.Width > 0 && other.Height > 0
        && X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
}

internal readonly record struct MeasuredLine(int Width, int Height, bool Ellipsized)
{
    public bool Fits(FlyoutSlot slot) =>
        slot.Width > 0
        && slot.Height > 0
        && Height <= slot.Height
        && (Ellipsized || Width <= slot.Width);
}

// Design pixels at 96 DPI. The flyout multiplies them by dpi/96 and draws with
// pixel fonts, so PerMonitorV2 does not scale the same values a second time.
internal static class FlyoutLayout
{
    public const int LightBackground = 0xFFFFFF;
    public const int DarkBackground = 0x202020;
    public const int LightAccent = 0x0F6CBD;
    public const int DarkAccent = 0x4CC2FF;

    public static int Px(int px, int dpi)
    {
        if (dpi < 1) dpi = 96;
        return (int)Math.Round(px * (dpi / 96.0));
    }

    public static int FontPixels(double points, int dpi)
    {
        if (dpi < 1) dpi = 96;
        return Math.Max(1, (int)Math.Round(points * dpi / 72.0));
    }

    // Wider than Segoe UI. A string that fits this estimate fits the real font.
    public static int EstimateWidth(string? text, int fontPixels)
    {
        if (string.IsNullOrEmpty(text) || fontPixels < 1) return 0;
        return (int)Math.Ceiling(text.Length * fontPixels * 0.62);
    }

    public static int EstimateHeight(int fontPixels) =>
        Math.Max(1, (int)Math.Ceiling(fontPixels * 1.4));

    public readonly record struct Metrics(
        int Dpi,
        int Width,
        int MaxHeight,
        int Corner,
        int Gap,
        int HeaderHeight,
        int HeaderPad,
        int RowHeight,
        int Avatar,
        int HoverInset,
        int HoverRadius,
        int ButtonHeight,
        int ButtonMargin,
        int ButtonRadius,
        int EmptyPad,
        int Check,
        int VisibleRows)
    {
        public int ListWidth => Width;
    }

    public static Metrics At(int dpi) => new(
        dpi,
        Px(TrayMetrics.PanelWidth, dpi),
        Px(TrayMetrics.PanelMaxHeight, dpi),
        Px(TrayMetrics.CornerRadius, dpi),
        Px(TrayMetrics.FlyoutGap, dpi),
        Px(TrayMetrics.HeaderHeight, dpi),
        Px(TrayMetrics.HeaderPadding, dpi),
        Px(TrayMetrics.RowHeight, dpi),
        Px(TrayMetrics.Avatar, dpi),
        Px(TrayMetrics.HoverInset, dpi),
        Px(TrayMetrics.HoverRadius, dpi),
        Px(TrayMetrics.ButtonHeight, dpi),
        Px(TrayMetrics.ButtonMargin, dpi),
        Px(TrayMetrics.ButtonRadius, dpi),
        Px(TrayMetrics.EmptyPadding, dpi),
        Px(TrayMetrics.CheckSize, dpi),
        TrayMetrics.VisibleConversations);

    public readonly record struct HeaderArrangement(FlyoutSlot Title, FlyoutSlot Pill, FlyoutSlot Link, bool PillVisible);

    public static HeaderArrangement Header(
        Metrics metrics,
        int titleWidth,
        int titleHeight,
        int pillWidth,
        int pillHeight,
        int linkWidth,
        int linkHeight)
    {
        int titleY = Math.Max(0, (metrics.HeaderHeight - titleHeight) / 2);
        var title = new FlyoutSlot(metrics.HeaderPad, titleY, titleWidth, titleHeight);
        int cursor = title.Right;
        FlyoutSlot pill = default;
        bool pillVisible = pillWidth > 0 && pillHeight > 0;
        if (pillVisible)
        {
            int gap = Px(8, metrics.Dpi);
            int pillY = Math.Max(0, (metrics.HeaderHeight - pillHeight) / 2);
            pill = new FlyoutSlot(cursor + gap, pillY, pillWidth, pillHeight);
            cursor = pill.Right;
        }

        int linkX = metrics.Width - metrics.HeaderPad - linkWidth;
        int minLink = cursor + Px(8, metrics.Dpi);
        if (linkX < minLink) linkX = minLink;
        int linkY = Math.Max(0, (metrics.HeaderHeight - linkHeight) / 2);
        var link = new FlyoutSlot(linkX, linkY, linkWidth, linkHeight);
        return new HeaderArrangement(title, pill, link, pillVisible);
    }

    public readonly record struct RowArrangement(
        FlyoutSlot Avatar,
        FlyoutSlot Dot,
        FlyoutSlot Name,
        FlyoutSlot Preview,
        FlyoutSlot Time,
        FlyoutSlot Hover,
        bool NameEllipsized,
        bool PreviewEllipsized,
        int NameTextWidth,
        int PreviewTextWidth);

    public static RowArrangement Row(
        Metrics metrics,
        int nameWidth,
        int nameHeight,
        int previewWidth,
        int previewHeight,
        int timeWidth,
        int timeHeight)
    {
        int inset = metrics.HoverInset;
        var hover = new FlyoutSlot(inset, Px(2, metrics.Dpi), Math.Max(1, metrics.Width - (inset * 2)), Math.Max(1, metrics.RowHeight - Px(4, metrics.Dpi)));
        int avatarY = Math.Max(0, (metrics.RowHeight - metrics.Avatar) / 2);
        var avatar = new FlyoutSlot(metrics.HeaderPad, avatarY, metrics.Avatar, metrics.Avatar);
        int dot = Math.Max(Px(8, metrics.Dpi), 6);
        var unreadDot = new FlyoutSlot(avatar.Right - (dot / 2), avatar.Bottom - (dot / 2), dot, dot);
        int timeX = metrics.Width - metrics.HeaderPad - timeWidth;
        int textX = avatar.Right + Px(12, metrics.Dpi);
        int nameMax = Math.Max(1, timeX - Px(8, metrics.Dpi) - textX);
        int previewMax = Math.Max(1, metrics.Width - metrics.HeaderPad - textX);
        int block = nameHeight + Px(2, metrics.Dpi) + previewHeight;
        int textY = Math.Max(0, (metrics.RowHeight - block) / 2);
        var name = new FlyoutSlot(textX, textY, nameMax, nameHeight);
        var preview = new FlyoutSlot(textX, name.Bottom + Px(2, metrics.Dpi), previewMax, previewHeight);
        var time = new FlyoutSlot(timeX, textY, timeWidth, timeHeight);
        return new RowArrangement(
            avatar,
            unreadDot,
            name,
            preview,
            time,
            hover,
            nameWidth > nameMax,
            previewWidth > previewMax,
            nameWidth,
            previewWidth);
    }

    public readonly record struct EmptyArrangement(FlyoutSlot Check, FlyoutSlot Title, FlyoutSlot Hint, int Height);

    public static EmptyArrangement Empty(Metrics metrics, int titleWidth, int titleHeight, int hintWidth, int hintHeight)
    {
        int y = metrics.EmptyPad;
        var check = new FlyoutSlot(Math.Max(0, (metrics.Width - metrics.Check) / 2), y, metrics.Check, metrics.Check);
        y = check.Bottom + Px(12, metrics.Dpi);
        var title = new FlyoutSlot(Centered(metrics.Width, titleWidth), y, titleWidth, titleHeight);
        y = title.Bottom + Px(4, metrics.Dpi);
        var hint = new FlyoutSlot(Centered(metrics.Width, hintWidth), y, hintWidth, hintHeight);
        return new EmptyArrangement(check, title, hint, hint.Bottom + metrics.EmptyPad);
    }

    public static FlyoutSlot FooterButton(Metrics metrics) =>
        new(
            metrics.ButtonMargin,
            metrics.ButtonMargin,
            Math.Max(1, metrics.Width - (metrics.ButtonMargin * 2)),
            metrics.ButtonHeight);

    public static int FooterHeight(Metrics metrics) =>
        Px(1, metrics.Dpi) + metrics.ButtonMargin + metrics.ButtonHeight + metrics.ButtonMargin;

    public static int PreferredHeight(Metrics metrics, int rowCount, int emptyHeight)
    {
        int body = rowCount <= 0 ? emptyHeight : rowCount * metrics.RowHeight;
        return Math.Min(metrics.MaxHeight, metrics.HeaderHeight + body + FooterHeight(metrics));
    }

    private static int Centered(int outer, int inner) => Math.Max(0, (outer - inner) / 2);
}

internal static class FlyoutText
{
    public const string Reaction = "Reacted ❤️ to your message";
    public const string Activity = "New activity";
    public const string CaughtUp = "You're all caught up";
    public const string CaughtUpHint = "New messages will show here";
    public const string Open = "Open Messenger";

    public static string Secondary(TrayRow row)
    {
        if (row.Kind == TrayRowKind.Reaction)
            return row.Name.Equals(Activity, StringComparison.OrdinalIgnoreCase) ? Activity : Reaction;
        if (row.CountOnly)
            return row.MessageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return row.Preview ?? "";
    }
}
