using Xunit;

namespace MessengerDesktop.Tests;

public class ServiceDefinitionTests
{
    [Fact]
    public void Facebook_keeps_the_existing_profile_and_inbox()
    {
        ServiceDefinition facebook = ServiceCatalog.Facebook;

        Assert.Equal("facebook", facebook.Name);
        Assert.Equal("WebView2", facebook.ProfileName);
        Assert.Equal(ServiceSurface.Tab, facebook.Surface);
        Assert.Equal("https://www.facebook.com/messages/t", facebook.Home.AbsoluteUri);
        string root = Path.Combine(Path.GetTempPath(), "messenger-profile");
        Assert.Equal(Path.Combine(root, "MessengerDesktop", "WebView2"), facebook.ProfileFolder(root));
    }

    [Fact]
    public void X_has_its_own_overlay_profile()
    {
        ServiceDefinition x = ServiceCatalog.X;

        Assert.Equal("x", x.Name);
        Assert.Equal("WebView2-X", x.ProfileName);
        Assert.Equal(ServiceSurface.Overlay, x.Surface);
        Assert.Equal("https://x.com/", x.Home.AbsoluteUri);
        string root = Path.Combine(Path.GetTempPath(), "messenger-profile");
        Assert.Equal(Path.Combine(root, "MessengerDesktop", "WebView2-X"), x.ProfileFolder(root));
        Assert.NotEqual(ServiceCatalog.Facebook.ProfileFolder(root), x.ProfileFolder(root));
    }

    [Fact]
    public void Facebook_link_rules_are_the_reel_share_and_chat_prefixes()
    {
        Assert.Equal(
            ["www.facebook.com", "www.facebook.com", "www.facebook.com", "www.facebook.com"],
            ServiceCatalog.Facebook.LinkRules.Select(rule => rule.Host).ToArray());
        Assert.Equal(
            ["/reel/", "/share/r/", "/messages/t/", "/messages/e2ee/t/"],
            ServiceCatalog.Facebook.LinkRules.Select(rule => rule.PathPrefix).ToArray());
    }

    [Theory]
    [InlineData("https://www.facebook.com/reel/1")]
    [InlineData("https://www.facebook.com/reel/1/")]
    [InlineData("https://WWW.FACEBOOK.COM/share/r/abc")]
    [InlineData("https://www.facebook.com/messages/t/123?mid=9")]
    [InlineData("https://www.facebook.com/messages/e2ee/t/456")]
    public void Facebook_link_rules_match_reel_share_and_chat(string value)
    {
        Assert.True(ServiceCatalog.Facebook.Matches(new Uri(value)));
    }

    [Theory]
    [InlineData("https://www.facebook.com/messages/t")]
    [InlineData("https://facebook.com/reel/1")]
    [InlineData("http://www.facebook.com/reel/1")]
    [InlineData("https://www.facebook.com:444/reel/1")]
    [InlineData("https://m.facebook.com/reel/1")]
    [InlineData("https://web.facebook.com/reel/1")]
    [InlineData("https://www.facebook.com.evil.com/reel/1")]
    [InlineData("https://notfacebook.com/reel/1")]
    [InlineData("https://www.facebook.com/watch/?v=1")]
    [InlineData("https://x.com/jack/status/20")]
    public void Facebook_link_rules_reject_other_urls(string value)
    {
        Assert.False(ServiceCatalog.Facebook.Matches(new Uri(value)));
    }

    [Fact]
    public void X_link_rules_name_the_status_hosts()
    {
        string[] hosts = ServiceCatalog.X.LinkRules.Select(rule => rule.Host).ToArray();

        Assert.Contains("x.com", hosts);
        Assert.Contains("www.x.com", hosts);
        Assert.Contains("mobile.x.com", hosts);
        Assert.Contains("twitter.com", hosts);
        Assert.Contains("www.twitter.com", hosts);
        Assert.Contains("mobile.twitter.com", hosts);
        Assert.Contains("m.twitter.com", hosts);
        Assert.DoesNotContain(hosts, host => host.Contains("facebook", StringComparison.OrdinalIgnoreCase));
        Assert.All(ServiceCatalog.X.LinkRules, rule => Assert.Equal(LinkMatch.Status, rule.Match));
    }
}
