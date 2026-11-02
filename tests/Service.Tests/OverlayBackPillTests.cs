using Xunit;

namespace MessengerDesktop.Tests;

public class OverlayBackPillTests
{
    [Fact]
    public void Pill_is_36_dip_black_glass_with_white_text()
    {
        Assert.Equal(36, OverlayBackPill.HeightDip);
        Assert.Equal((0, 0, 0), OverlayBackPill.FillRgb);
        Assert.Equal(OverlayBackPill.BackgroundAlpha, (byte)Math.Round(255 * 0.70));
        Assert.Equal((255, 255, 255), OverlayBackPill.TextRgb);
        Assert.Equal("←", OverlayBackPill.Arrow);
        Assert.Equal("Back to chat", OverlayBackPill.Caption);
        Assert.InRange(OverlayBackPill.ShadowBlurDip, 1, OverlayBackPill.HeightDip);
        Assert.True(OverlayBackPill.ShadowOffsetYDip >= 0);
        Assert.InRange(OverlayBackPill.ShadowAlpha, (byte)1, (byte)120);
    }

    [Theory]
    [InlineData(96, 36)]
    [InlineData(120, 45)]
    [InlineData(144, 54)]
    [InlineData(192, 72)]
    [InlineData(0, 36)]
    public void Pill_height_and_corner_scale_with_dpi(int dpi, int pixels)
    {
        Assert.Equal(pixels, OverlayBackPill.HeightPixels(dpi));
        Assert.Equal(pixels / 2, OverlayBackPill.CornerRadiusPixels(dpi));
        int scaleDpi = dpi <= 0 ? 96 : dpi;
        Assert.Equal((int)Math.Round(12d * scaleDpi / 96d), OverlayBackPill.MarginPixels(dpi));
        Assert.Equal((int)Math.Round(20d * scaleDpi / 96d), OverlayBackPill.IconPixels(dpi));
        Assert.Equal((int)Math.Round(15d * scaleDpi / 96d), OverlayBackPill.FontPixels(dpi));
    }

    [Theory]
    [InlineData(0x1B, false, false, false, true)]
    [InlineData(0x25, true, false, false, true)]
    [InlineData(0x25, false, false, false, false)]
    [InlineData(0x27, true, false, false, false)]
    [InlineData(0x25, true, true, false, false)]
    [InlineData(0x1B, false, false, true, false)]
    [InlineData(0x1B, true, false, false, false)]
    public void Esc_and_alt_left_are_the_back_keys(int key, bool alt, bool control, bool shift, bool back)
    {
        Assert.Equal(back, OverlayBackPill.IsBackKey(key, alt, control, shift));
    }

    [Theory]
    [InlineData(0x020B, 1, true)]
    [InlineData(0x020B, 2, false)]
    [InlineData(0x0201, 1, false)]
    public void Mouse_side_back_is_xbutton1_down(int message, int xButton, bool back)
    {
        nint wParam = (nint)(xButton << 16);
        Assert.Equal(back, OverlayBackPill.IsXButton1Down(message, wParam));
    }

    [Fact]
    public void Back_hides_the_overlay_and_does_not_navigate_the_chat()
    {
        OverlayBackDecision decision = OverlayBackPill.Back();

        Assert.True(decision.CloseOverlay);
        Assert.False(decision.NavigateChat);
    }

    [Fact]
    public void Pill_covers_the_x_logo()
    {
        var logo = new LogoRect(122, 8, 32, 32);

        (int x, int y) = OverlayBackPill.Place(1280, 800, 175, 36, 12, logo);

        Assert.Equal(122, x);
        Assert.Equal(6, y);
    }

    [Fact]
    public void Pill_sits_in_the_corner_until_the_logo_is_known()
    {
        (int x, int y) = OverlayBackPill.Place(1280, 800, 175, 36, 12, null);

        Assert.Equal(12, x);
        Assert.Equal(12, y);
    }

    [Fact]
    public void Pill_stays_inside_the_view()
    {
        var logo = new LogoRect(2000, -40, 32, 32);

        (int x, int y) = OverlayBackPill.Place(400, 200, 175, 36, 12, logo);

        Assert.Equal(225, x);
        Assert.Equal(0, y);
    }

    [Theory]
    [InlineData("{\"type\":\"messenger-x-logo\",\"x\":10.5,\"y\":4,\"width\":32,\"height\":30}", 10.5, 4, 32, 30)]
    [InlineData("{\"type\":\"messenger-x-logo\",\"x\":0,\"y\":0,\"width\":1,\"height\":1}", 0, 0, 1, 1)]
    public void Logo_message_is_the_top_home_link(string message, double x, double y, double width, double height)
    {
        Assert.True(OverlayBackPill.TryReadLogo(message, out LogoRect logo));
        Assert.Equal(x, logo.X);
        Assert.Equal(y, logo.Y);
        Assert.Equal(width, logo.Width);
        Assert.Equal(height, logo.Height);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("messenger-overlay-escape")]
    [InlineData("{\"type\":\"messenger-open-status\",\"url\":\"https://x.com/jack/status/20\"}")]
    [InlineData("{\"type\":\"messenger-x-logo\",\"x\":1,\"y\":2,\"width\":0,\"height\":10}")]
    [InlineData("{\"type\":\"messenger-x-logo\"}")]
    [InlineData("{")]
    public void Other_messages_are_not_a_logo(string? message)
    {
        Assert.False(OverlayBackPill.TryReadLogo(message, out _));
    }

    [Fact]
    public void X_page_script_closes_on_escape_and_alt_left_and_reports_the_logo()
    {
        Assert.Contains("messenger-overlay-escape", XOverlayScript.Source, StringComparison.Ordinal);
        Assert.Contains("ArrowLeft", XOverlayScript.Source, StringComparison.Ordinal);
        Assert.Contains("messenger-x-logo", XOverlayScript.Source, StringComparison.Ordinal);
        Assert.Contains("a[href=\"/home\"]", XOverlayScript.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Open in browser", XOverlayScript.Source, StringComparison.Ordinal);
    }
}
