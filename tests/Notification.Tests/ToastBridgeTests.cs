using Xunit;

namespace MessengerDesktop.Tests;

public class ToastBridgeTests
{
    [Fact]
    public void A_click_before_the_window_exists_is_delivered_on_attach()
    {
        var bridge = new ToastBridge();
        var seen = new List<string?>();

        bridge.Publish("url=https%3A%2F%2Fwww.facebook.com%2Fmessages%2Ft%2F1");
        bridge.Attach(seen.Add);

        Assert.Equal(new[] { "url=https%3A%2F%2Fwww.facebook.com%2Fmessages%2Ft%2F1" }, seen);
    }

    [Fact]
    public void A_click_after_attach_is_delivered_immediately()
    {
        var bridge = new ToastBridge();
        var seen = new List<string?>();
        bridge.Attach(seen.Add);

        bridge.Publish("tag=m1");

        Assert.Equal(new[] { "tag=m1" }, seen);
    }

    [Fact]
    public void Only_the_latest_click_is_kept_until_the_window_exists()
    {
        var bridge = new ToastBridge();
        var seen = new List<string?>();

        bridge.Publish("first");
        bridge.Publish(null);
        bridge.Attach(seen.Add);

        Assert.Equal(new string?[] { null }, seen);
    }
}
