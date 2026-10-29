using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MessengerDesktop;

// Draws the badge into a 16px copy of the app icon. The disk geometry lives in
// TrayBadgeLayout so the red circle, white ring, and muted dot stay testable.
internal static class TrayBadgePainter
{
    public static Icon Compose(Icon source, TrayGlyph glyph, string? text)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var bitmap = new Bitmap(TrayBadgeMetrics.IconSize, TrayBadgeMetrics.IconSize, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);
            graphics.DrawIcon(source, new Rectangle(0, 0, TrayBadgeMetrics.IconSize, TrayBadgeMetrics.IconSize));
            foreach (TrayDisk disk in TrayBadgeLayout.Layers(glyph))
            {
                using var brush = new SolidBrush(Color.FromArgb(disk.Argb));
                graphics.FillEllipse(brush, disk.X, disk.Y, disk.Diameter, disk.Diameter);
            }

            if (glyph == TrayGlyph.Count && !string.IsNullOrEmpty(text))
                DrawNumber(graphics, text);
        }

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

    private static void DrawNumber(Graphics graphics, string text)
    {
        TrayDisk fill = TrayBadgeLayout.Layers(TrayGlyph.Count)[1];
        float em = text.Length > 1 ? 5f : 6.5f;
        using var font = new Font(FontFamily.GenericSansSerif, em, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(TrayBadgeMetrics.NumberArgb));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        var bounds = new RectangleF(fill.X, fill.Y - 0.5f, fill.Diameter, fill.Diameter);
        graphics.DrawString(text, font, brush, bounds, format);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
