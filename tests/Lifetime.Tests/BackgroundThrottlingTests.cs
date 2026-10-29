using Xunit;

namespace MessengerDesktop.Tests;

public class BackgroundThrottlingTests
{
    [Fact]
    public void Browser_arguments_keep_a_hidden_window_from_throttling()
    {
        string args = BackgroundThrottling.BrowserArguments;

        Assert.Contains("--disable-background-timer-throttling", args, StringComparison.Ordinal);
        Assert.Contains("--disable-renderer-backgrounding", args, StringComparison.Ordinal);
        Assert.Contains("--disable-backgrounding-occluded-windows", args, StringComparison.Ordinal);
        Assert.Contains("--disable-features=CalculateNativeWinOcclusion", args, StringComparison.Ordinal);
    }
}
