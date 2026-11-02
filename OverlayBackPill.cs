using System.Text.Json;

namespace MessengerDesktop;

internal readonly record struct OverlayBackDecision(bool CloseOverlay, bool NavigateChat);

internal readonly record struct LogoRect(double X, double Y, double Width, double Height);

// Size, caption, and gestures for the X back pill. The chat WebView is left
// alone, so its scroll position stays where it was.
internal static class OverlayBackPill
{
    public const int HeightDip = 36;
    public const int MarginDip = 12;
    public const int PadDip = 12;
    public const int IconDip = 20;
    public const int GapDip = 7;
    public const int FontDip = 15;
    public const int ShadowBlurDip = 8;
    public const int ShadowOffsetYDip = 2;
    public const byte BackgroundAlpha = 178;
    public const byte ShadowAlpha = 64;
    public const int EscapeKey = 0x1B;
    public const int LeftKey = 0x25;
    public const int XButton1Down = 0x020B;
    public const int XButton1 = 1;
    public const string Arrow = "←";
    public const string Caption = "Back to chat";
    public const string LogoMessageType = "messenger-x-logo";

    public static (int R, int G, int B) FillRgb { get; } = (0, 0, 0);
    public static (int R, int G, int B) TextRgb { get; } = (255, 255, 255);

    public static int HeightPixels(int deviceDpi) => Scale(HeightDip, deviceDpi);

    public static int MarginPixels(int deviceDpi) => Scale(MarginDip, deviceDpi);

    public static int PadPixels(int deviceDpi) => Scale(PadDip, deviceDpi);

    public static int IconPixels(int deviceDpi) => Scale(IconDip, deviceDpi);

    public static int GapPixels(int deviceDpi) => Scale(GapDip, deviceDpi);

    public static int FontPixels(int deviceDpi) => Scale(FontDip, deviceDpi);

    public static int ShadowBlurPixels(int deviceDpi) => Scale(ShadowBlurDip, deviceDpi);

    public static int ShadowOffsetYPixels(int deviceDpi) => Scale(ShadowOffsetYDip, deviceDpi);

    // Fully rounded: the end caps are semicircles, so the radius is half the height.
    public static int CornerRadiusPixels(int deviceDpi) => HeightPixels(deviceDpi) / 2;

    public static bool IsBackKey(int virtualKey, bool alt, bool control, bool shift)
    {
        if (control || shift) return false;
        if (virtualKey == EscapeKey && !alt) return true;
        return virtualKey == LeftKey && alt;
    }

    public static bool IsXButton1Down(int message, nint wParam)
    {
        if (message != XButton1Down) return false;
        int button = (int)((wParam.ToInt64() >> 16) & 0xFFFF);
        return button == XButton1;
    }

    public static OverlayBackDecision Back() => new(true, false);

    public static bool TryReadLogo(string? message, out LogoRect logo)
    {
        logo = default;
        if (string.IsNullOrWhiteSpace(message) || message[0] != '{') return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(message);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String) return false;
            if (!string.Equals(type.GetString(), LogoMessageType, StringComparison.Ordinal)) return false;
            if (!TryNumber(root, "x", out double x) || !TryNumber(root, "y", out double y)) return false;
            if (!TryNumber(root, "width", out double width) || !TryNumber(root, "height", out double height)) return false;
            if (width <= 0 || height <= 0) return false;
            logo = new LogoRect(x, y, width, height);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Cover the X logo when its rectangle is known. Until then, sit in the
    // top-left corner of the view. The pill stays inside the view.
    public static (int X, int Y) Place(int viewWidth, int viewHeight, int pillWidth, int pillHeight, int margin, LogoRect? logo)
    {
        int x;
        int y;
        if (logo is { Width: > 0, Height: > 0 } mark)
        {
            x = (int)Math.Round(mark.X);
            y = (int)Math.Round(mark.Y + (mark.Height - pillHeight) / 2d);
        }
        else
        {
            x = margin;
            y = margin;
        }

        int maxX = Math.Max(0, viewWidth - Math.Max(0, pillWidth));
        int maxY = Math.Max(0, viewHeight - Math.Max(0, pillHeight));
        return (Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY));
    }

    private static bool TryNumber(JsonElement root, string name, out double value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.Number) return false;
        if (!property.TryGetDouble(out value) || !double.IsFinite(value)) return false;
        return true;
    }

    private static int Scale(int dip, int deviceDpi)
    {
        int dpi = deviceDpi <= 0 ? 96 : deviceDpi;
        return (int)Math.Round(dip * dpi / 96d);
    }
}
