using Xunit;

namespace MessengerDesktop.Tests;

public class XLinkCaptureTests
{
    [Theory]
    [InlineData("https://x.com/jack/status/20")]
    [InlineData("https://www.x.com/jack/status/20")]
    [InlineData("https://mobile.x.com/jack/status/20")]
    [InlineData("https://twitter.com/jack/status/20")]
    [InlineData("https://www.twitter.com/jack/status/20")]
    [InlineData("https://mobile.twitter.com/jack/status/20")]
    [InlineData("https://m.twitter.com/jack/status/20")]
    [InlineData("https://x.com/jack/status/20?s=46&t=abc")]
    [InlineData("https://x.com/jack/status/20/photo/1")]
    [InlineData("https://x.com/jack/status/20/")]
    [InlineData("https://x.com/i/status/20")]
    [InlineData("https://twitter.com/i/web/status/20")]
    [InlineData("https://X.COM/Jack/status/20")]
    public void Direct_status_links_open_in_the_x_service(string value)
    {
        var clicked = new Uri(value);

        Assert.True(XLinkCapture.TryCapture(clicked, out Uri? status));
        Assert.Equal(clicked.AbsoluteUri, status!.AbsoluteUri);
        Assert.True(ServiceCatalog.X.Matches(clicked));
    }

    [Theory]
    [InlineData("https://x.com/jack/status/20")]
    [InlineData("https://twitter.com/jack/status/20?s=20")]
    [InlineData("https://x.com/i/web/status/20")]
    [InlineData("https://www.x.com/jack/status/20/photo/1")]
    public void Facebook_redirect_shims_unwrap_to_the_status(string statusValue)
    {
        string shim = "https://l.facebook.com/l.php?u=" + Uri.EscapeDataString(statusValue) + "&h=AT0";

        Assert.True(XLinkCapture.TryCapture(new Uri(shim), out Uri? status));
        Assert.Equal(new Uri(statusValue).AbsoluteUri, status!.AbsoluteUri);
        Assert.True(ServiceCatalog.X.Matches(new Uri(shim)));
    }

    [Fact]
    public void Shim_query_accepts_the_destination_in_any_position()
    {
        const string statusValue = "https://x.com/jack/status/20";
        string shim = "https://l.facebook.com/l.php?h=AT0&u=" + Uri.EscapeDataString(statusValue);

        Assert.True(XLinkCapture.TryCapture(new Uri(shim), out Uri? status));
        Assert.Equal(statusValue, status!.AbsoluteUri);
    }

    [Fact]
    public void Double_encoded_shim_unwraps_to_the_status()
    {
        const string statusValue = "https://x.com/jack/status/20";
        string shim = "https://l.facebook.com/l.php?u=" + Uri.EscapeDataString(Uri.EscapeDataString(statusValue));

        Assert.True(XLinkCapture.TryCapture(new Uri(shim), out Uri? status));
        Assert.Equal(statusValue, status!.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://x.com/jack")]
    [InlineData("https://x.com/home")]
    [InlineData("https://x.com/explore")]
    [InlineData("https://x.com/jack/status/")]
    [InlineData("https://x.com/jack/status/abc")]
    [InlineData("https://x.com/jack/statuses/20")]
    [InlineData("https://x.com/intent/post")]
    [InlineData("https://x.com/i/web/status/")]
    [InlineData("https://x.com/search?q=status")]
    [InlineData("https://x.com/home/status/20")]
    [InlineData("http://x.com/jack/status/20")]
    [InlineData("https://x.com:444/jack/status/20")]
    [InlineData("https://x.com.evil.com/jack/status/20")]
    [InlineData("https://notx.com/jack/status/20")]
    [InlineData("https://evil.com/x.com/jack/status/20")]
    [InlineData("https://twitter.com.evil.com/jack/status/20")]
    [InlineData("https://x.com.twitter.com/jack/status/20")]
    [InlineData("https://status.x.com/jack/status/20")]
    [InlineData("https://www.facebook.com/reel/1")]
    [InlineData("https://www.facebook.com/messages/t/1")]
    [InlineData("https://l.facebook.com/l.php?u=https%3A%2F%2Fx.com%2Fhome")]
    [InlineData("https://l.facebook.com/l.php?u=https%3A%2F%2Fwww.facebook.com%2Freel%2F1")]
    [InlineData("https://l.facebook.com/l.php?u=https%3A%2F%2Fx.com%2Fjack")]
    [InlineData("https://l.facebook.com.evil.com/l.php?u=https%3A%2F%2Fx.com%2Fjack%2Fstatus%2F20")]
    [InlineData("https://www.facebook.com/l.php?u=https%3A%2F%2Fx.com%2Fjack%2Fstatus%2F20")]
    [InlineData("https://lm.facebook.com/l.php?u=https%3A%2F%2Fx.com%2Fjack%2Fstatus%2F20")]
    [InlineData("https://l.facebook.com/l.php")]
    [InlineData("https://l.facebook.com/l.php?u=not-a-url")]
    [InlineData("https://l.facebook.com/l.php?u=javascript%3Aalert(1)")]
    public void Non_status_and_lookalike_links_stay_in_the_chat(string value)
    {
        Assert.False(XLinkCapture.TryCapture(new Uri(value), out _));
        Assert.False(ServiceCatalog.X.Matches(new Uri(value)));
    }

    [Fact]
    public void Web_message_reads_a_status_target()
    {
        const string json = """{"type":"messenger-open-status","url":"https://x.com/jack/status/20"}""";

        Assert.True(XLinkCapture.TryReadMessage(json, out Uri? clicked));
        Assert.True(XLinkCapture.TryCapture(clicked, out Uri? status));
        Assert.Equal("https://x.com/jack/status/20", status!.AbsoluteUri);
    }

    [Fact]
    public void Web_message_reads_a_shim_and_capture_unwraps_it()
    {
        const string statusValue = "https://twitter.com/jack/status/20";
        string shim = "https://l.facebook.com/l.php?u=" + Uri.EscapeDataString(statusValue);
        string json = "{\"type\":\"messenger-open-status\",\"url\":\"" + shim + "\"}";

        Assert.True(XLinkCapture.TryReadMessage(json, out Uri? clicked));
        Assert.True(XLinkCapture.TryCapture(clicked, out Uri? status));
        Assert.Equal(statusValue, status!.AbsoluteUri);
    }

    [Theory]
    [InlineData("{\"type\":\"messenger-open-reel\",\"url\":\"https://x.com/jack/status/20\"}")]
    [InlineData("{\"type\":\"messenger-open-status\",\"url\":\"https://x.com/home\"}")]
    [InlineData("{\"type\":\"messenger-open-status\"}")]
    [InlineData("{\"url\":\"https://x.com/jack/status/20\"}")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{\"type\":\"messenger-open-status\",\"url\":\"javascript:alert(1)\"}")]
    public void Web_message_rejects_other_payloads(string json)
    {
        bool read = XLinkCapture.TryReadMessage(json, out Uri? clicked);
        if (!read)
        {
            Assert.Null(clicked);
            return;
        }

        Assert.False(XLinkCapture.TryCapture(clicked, out _));
    }
}
