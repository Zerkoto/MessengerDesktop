using Xunit;

namespace MessengerDesktop.Tests;

public class NotificationOriginsTests
{
    [Theory]
    [InlineData("https://www.facebook.com")]
    [InlineData("https://facebook.com")]
    [InlineData("https://web.facebook.com")]
    [InlineData("https://m.facebook.com")]
    [InlineData("https://business.facebook.com/messages")]
    [InlineData("https://www.messenger.com")]
    [InlineData("https://messenger.com/t/1")]
    [InlineData("https://WWW.FACEBOOK.COM/messages/t")]
    public void Facebook_and_messenger_https_origins_are_allowed(string value)
    {
        Assert.True(NotificationOrigins.IsAllowed(new Uri(value)));
    }

    [Theory]
    [InlineData("http://www.facebook.com")]
    [InlineData("https://facebook.com.evil.com")]
    [InlineData("https://notfacebook.com")]
    [InlineData("https://evil.com/facebook.com")]
    [InlineData("https://www.facebook.com.attacker.test")]
    [InlineData("https://messenger.com.evil.com")]
    [InlineData("https://example.com")]
    public void Other_origins_are_denied(string value)
    {
        Assert.False(NotificationOrigins.IsAllowed(new Uri(value)));
    }

    [Fact]
    public void Seeded_profile_origins_are_https_facebook_or_messenger()
    {
        Assert.NotEmpty(NotificationOrigins.GrantedOrigins);
        foreach (string origin in NotificationOrigins.GrantedOrigins)
        {
            var uri = new Uri(origin);
            Assert.Equal("https", uri.Scheme);
            Assert.True(uri.AbsolutePath is "" or "/");
            Assert.True(NotificationOrigins.IsAllowed(uri));
        }
    }
}
