using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MessengerDesktop;

// SM_CXSMICON via GetSystemMetricsForDpi, so 150% is 24px instead of a stretched 16px icon.
internal static class TrayIconSize
{
    public const int SmCxSmIcon = 49;

    public static int ForDpi(int dpi)
    {
        int fromSystem = TrySystem(dpi);
        if (fromSystem > 0) return fromSystem;
        try
        {
            int small = SystemInformation.SmallIconSize.Width;
            if (small > 0 && (dpi <= 96 || dpi == GuessDpi(small)))
                return small;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // SystemInformation is unavailable before the message loop. Fall through.
        }

        return TrayBadgeMetrics.IconSizeForDpi(dpi);
    }

    private static int GuessDpi(int smallIcon) =>
        (int)Math.Round(smallIcon * 96.0 / TrayBadgeMetrics.BaseIconSize);

    private static int TrySystem(int dpi)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        try
        {
            return GetSystemMetricsForDpi(SmCxSmIcon, (uint)Math.Max(dpi, 1));
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}

// Draws the badge at the tray's real small-icon size. A fixed 16px bitmap gets
// scaled by the shell and the digit disappears.
internal static class TrayBadgePainter
{
    public static Icon Compose(Icon source, TrayGlyph glyph, string? text, int iconSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        using Bitmap sourceBitmap = source.ToBitmap();
        using Bitmap bitmap = Render(sourceBitmap, glyph, text, iconSize);
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon raw = Icon.FromHandle(handle);
            return (Icon)raw.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static Bitmap Render(Image source, TrayGlyph glyph, string? text, int iconSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (iconSize < 8) iconSize = 8;
        var bitmap = new Bitmap(iconSize, iconSize, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);
        graphics.DrawImage(source, new Rectangle(0, 0, iconSize, iconSize));
        foreach (TrayDisk disk in TrayBadgeLayout.Layers(glyph, iconSize))
        {
            using var brush = new SolidBrush(Color.FromArgb(disk.Argb));
            graphics.FillEllipse(brush, disk.X, disk.Y, disk.Diameter, disk.Diameter);
        }

        if (glyph == TrayGlyph.Count && !string.IsNullOrEmpty(text))
            DrawNumber(graphics, text, iconSize);
        return bitmap;
    }

    private static void DrawNumber(Graphics graphics, string text, int iconSize)
    {
        TrayDisk fill = TrayBadgeLayout.Layers(TrayGlyph.Count, iconSize).Last(disk => disk.Argb == TrayBadgeMetrics.CountArgb);
        int limit = Math.Max(1, fill.Diameter - 1);
        int em = TrayBadgeLayout.NumberSize(fill.Diameter, text.Length);
        int floor = Math.Max(1, (em * 4) / 5);
        var flags = TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding
            | TextFormatFlags.SingleLine
            | TextFormatFlags.NoPrefix;
        FontFamily family = ResolveFamily(out bool ownedFamily);
        Font? font = null;
        try
        {
            while (true)
            {
                font?.Dispose();
                font = new Font(family, em, FontStyle.Bold, GraphicsUnit.Pixel);
                Size measured = TextRenderer.MeasureText(graphics, text, font, new Size(limit, limit), flags);
                if ((measured.Width <= limit && measured.Height <= limit) || em <= floor)
                    break;
                em--;
            }

            var bounds = new Rectangle(fill.X, fill.Y, fill.Diameter, fill.Diameter);
            TextRenderer.DrawText(graphics, text, font, bounds, Color.FromArgb(TrayBadgeMetrics.NumberArgb), flags);
        }
        finally
        {
            font?.Dispose();
            if (ownedFamily) family.Dispose();
        }
    }

    private static FontFamily ResolveFamily(out bool owned)
    {
        try
        {
            owned = true;
            return new FontFamily("Segoe UI");
        }
        catch (ArgumentException)
        {
            owned = false;
            return FontFamily.GenericSansSerif;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
