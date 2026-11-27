using Xunit;

namespace MessengerDesktop.Tests;

public class ExternalLinksTests
{
    [Theory]
    [InlineData("https://facebook.com/")]
    [InlineData("https://www.facebook.com/messages/t/1")]
    [InlineData("https://m.facebook.com/login")]
    [InlineData("https://web.facebook.com/checkpoint/")]
    [InlineData("https://www.facebook.com/login.php")]
    [InlineData("https://www.facebook.com/checkpoint/1501092823525282/")]
    [InlineData("https://m.facebook.com/login/checkpoint/")]
    [InlineData("https://business.facebook.com/")]
    [InlineData("https://l.facebook.com/l.php")]
    [InlineData("https://lm.facebook.com/l.php")]
    [InlineData("http://www.facebook.com/")]
    [InlineData("https://WWW.FACEBOOK.COM/login")]
    [InlineData("https://messenger.com/")]
    [InlineData("https://www.messenger.com/t/1")]
    [InlineData("https://fb.com/")]
    [InlineData("https://www.fb.com/")]
    [InlineData("https://fb.me/abc")]
    [InlineData("https://www.fb.me/abc")]
    [InlineData("https://fb.watch/abc")]
    [InlineData("https://www.fb.watch/abc")]
    [InlineData("http://fb.watch/xyz")]
    [InlineData("https://FB.WATCH/abc")]
    [InlineData("https://scontent.xx.fbcdn.net/v/photo.jpg")]
    [InlineData("https://video.xx.fbcdn.net/v/clip.mp4")]
    [InlineData("https://www.fbsbx.com/")]
    [InlineData("https://video.fbsbx.com/file")]
    [InlineData("https://connect.facebook.net/en_US/sdk.js")]
    [InlineData("https://www.facebook.net/rsrc.php")]
    [InlineData("https://instagram.com/")]
    [InlineData("https://www.instagram.com/direct/inbox/")]
    [InlineData("https://scontent.cdninstagram.com/v/image.jpg")]
    [InlineData("https://x.com/")]
    [InlineData("https://x.com/i/flow/login")]
    [InlineData("https://www.x.com/jack/status/20")]
    [InlineData("https://mobile.x.com/jack/status/20")]
    [InlineData("https://twitter.com/i/flow/login")]
    [InlineData("https://mobile.twitter.com/")]
    [InlineData("https://t.co/abc")]
    [InlineData("https://pbs.twimg.com/media/x.jpg")]
    [InlineData("https://abs.twimg.com/responsive-web/client.css")]
    public void In_app_hosts_and_subdomains_stay(string value)
    {
        Assert.False(ExternalLinks.ShouldOpenExternally(new Uri(value)));
        Assert.False(ExternalLinks.TryGetExternal(new Uri(value), out Uri? external));
        Assert.Null(external);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9wgXcQ")]
    [InlineData("https://youtube.com/watch?v=1")]
    [InlineData("https://m.youtube.com/watch?v=1")]
    [InlineData("https://music.youtube.com/watch?v=1")]
    [InlineData("https://youtube-nocookie.com/embed/abc")]
    [InlineData("https://www.youtube-nocookie.com/embed/abc")]
    [InlineData("https://youtu.be/dQw4w9wgXcQ")]
    [InlineData("http://youtu.be/abc")]
    [InlineData("https://YouTube.com/watch?v=1")]
    [InlineData("https://example.com/article")]
    [InlineData("http://example.com/")]
    [InlineData("https://notx.com/")]
    [InlineData("https://notfacebook.com/")]
    [InlineData("https://facebook.com.evil.com/")]
    [InlineData("https://www.facebook.com.evil.com/login")]
    [InlineData("https://x.com.evil.com/jack/status/20")]
    [InlineData("https://twitter.com.evil.com/")]
    [InlineData("https://nottwitter.com/")]
    [InlineData("https://t.co.evil.com/abc")]
    [InlineData("https://fb.com.evil.com/")]
    [InlineData("https://fb.watch.evil.com/")]
    [InlineData("https://notfb.watch/")]
    [InlineData("https://evil.com/fb.watch/abc")]
    [InlineData("https://evil.com/facebook.com/login")]
    [InlineData("https://evil.com/x.com/jack")]
    [InlineData("https://www.google.com/")]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth?client_id=1")]
    [InlineData("https://appleid.apple.com/auth/authorize")]
    public void External_hosts_and_lookalikes_open_outside(string value)
    {
        var clicked = new Uri(value);

        Assert.True(ExternalLinks.ShouldOpenExternally(clicked));
        Assert.True(ExternalLinks.TryGetExternal(clicked, out Uri? external));
        Assert.Equal(clicked.AbsoluteUri, external!.AbsoluteUri);
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("data:text/html,hi")]
    [InlineData("blob:https://www.facebook.com/550e8400-e29b-41d4-a716-446655440000")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("mailto:a@b.com")]
    [InlineData("ftp://youtube.com/watch?v=1")]
    public void Non_http_schemes_are_left_alone(string value)
    {
        Assert.False(ExternalLinks.ShouldOpenExternally(new Uri(value)));
        Assert.False(ExternalLinks.TryGetExternal(new Uri(value), out Uri? external));
        Assert.Null(external);
    }

    [Fact]
    public void Null_and_relative_urls_are_left_alone()
    {
        Assert.False(ExternalLinks.ShouldOpenExternally(null));
        Assert.False(ExternalLinks.TryGetExternal(null, out Uri? external));
        Assert.Null(external);
        Assert.False(ExternalLinks.ShouldOpenExternally(new Uri("/watch?v=1", UriKind.Relative)));
        Assert.False(ExternalLinks.TryGetExternal(new Uri("/messages/t/1", UriKind.Relative), out external));
        Assert.Null(external);
    }

    [Theory]
    [InlineData("l.facebook.com", "https://www.youtube.com/watch?v=dQw4w9wgXcQ")]
    [InlineData("l.facebook.com", "https://youtu.be/dQw4w9wgXcQ")]
    [InlineData("l.facebook.com", "http://example.com/story")]
    [InlineData("lm.facebook.com", "https://music.youtube.com/watch?v=1")]
    [InlineData("lm.facebook.com", "https://example.com/a")]
    public void Shims_to_external_targets_open_the_real_url(string host, string targetValue)
    {
        string shim = "https://" + host + "/l.php?u=" + Uri.EscapeDataString(targetValue) + "&h=AT0";

        Assert.True(ExternalLinks.TryGetExternal(new Uri(shim), out Uri? external));
        Assert.Equal(new Uri(targetValue).AbsoluteUri, external!.AbsoluteUri);
        Assert.False(ExternalLinks.ShouldOpenExternally(new Uri("https://" + host + "/l.php")));
    }

    [Fact]
    public void Shim_query_accepts_the_destination_in_any_position()
    {
        const string targetValue = "https://www.youtube.com/watch?v=1";
        string shim = "https://l.facebook.com/l.php?h=AT0&u=" + Uri.EscapeDataString(targetValue);

        Assert.True(ExternalLinks.TryGetExternal(new Uri(shim), out Uri? external));
        Assert.Equal(new Uri(targetValue).AbsoluteUri, external!.AbsoluteUri);
    }

    [Fact]
    public void Double_encoded_shim_unwraps_to_the_external_url()
    {
        const string targetValue = "https://youtu.be/dQw4w9wgXcQ";
        string shim = "https://lm.facebook.com/l.php?u=" + Uri.EscapeDataString(Uri.EscapeDataString(targetValue));

        Assert.True(ExternalLinks.TryGetExternal(new Uri(shim), out Uri? external));
        Assert.Equal(new Uri(targetValue).AbsoluteUri, external!.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://l.facebook.com/l.php?u=https%3A%2F%2Fwww.facebook.com%2Freel%2F1")]
    [InlineData("https://lm.facebook.com/l.php?u=https%3A%2F%2Fwww.facebook.com%2Fmessages%2Ft%2F1")]
    [InlineData("https://l.facebook.com/l.php?u=https%3A%2F%2Fx.com%2Fjack%2Fstatus%2F20")]
    [InlineData("https://lm.facebook.com/l.php?u=https%3A%2F%2Ftwitter.com%2Fjack%2Fstatus%2F20")]
    [InlineData("https://l.facebook.com/l.php?u=https%3A%2F%2Fwww.instagram.com%2F")]
    [InlineData("https://l.facebook.com/l.php?u=https%3A%2F%2Ffb.watch%2Fabc")]
    [InlineData("https://lm.facebook.com/l.php?u=https%3A%2F%2Fwww.fb.watch%2Fabc")]
    [InlineData("https://l.facebook.com/l.php")]
    [InlineData("https://l.facebook.com/l.php?u=not-a-url")]
    [InlineData("https://l.facebook.com/l.php?u=javascript%3Aalert(1)")]
    [InlineData("https://l.facebook.com/l.php?u=mailto%3Aa%40b.com")]
    [InlineData("https://www.facebook.com/l.php?u=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3D1")]
    public void Shims_to_in_app_targets_and_bad_shims_stay(string value)
    {
        Assert.False(ExternalLinks.ShouldOpenExternally(new Uri(value)));
        Assert.False(ExternalLinks.TryGetExternal(new Uri(value), out Uri? external));
        Assert.Null(external);
    }

    [Fact]
    public void Lookalike_shim_host_is_external_and_is_not_unwrapped()
    {
        const string inner = "https://www.youtube.com/watch?v=1";
        string shim = "https://l.facebook.com.evil.com/l.php?u=" + Uri.EscapeDataString(inner);
        var clicked = new Uri(shim);

        Assert.True(ExternalLinks.TryGetExternal(clicked, out Uri? external));
        Assert.Equal(clicked.AbsoluteUri, external!.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth?client_id=1")]
    [InlineData("https://accounts.google.com/signin/oauth")]
    [InlineData("https://appleid.apple.com/auth/authorize")]
    [InlineData("https://x.com/i/flow/login")]
    [InlineData("https://twitter.com/i/flow/login")]
    [InlineData("https://api.x.com/oauth/authenticate")]
    [InlineData("https://api.twitter.com/oauth/authenticate")]
    public void X_sign_in_stays_in_the_overlay(string value)
    {
        Assert.False(ExternalLinks.TryGetExternal(new Uri(value), out Uri? external, signInStaysInApp: true));
        Assert.Null(external);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=1")]
    [InlineData("https://example.com/story")]
    [InlineData("https://www.google.com/")]
    public void Other_links_in_the_overlay_still_open_outside(string value)
    {
        var clicked = new Uri(value);

        Assert.True(ExternalLinks.TryGetExternal(clicked, out Uri? external, signInStaysInApp: true));
        Assert.Equal(clicked.AbsoluteUri, external!.AbsoluteUri);
    }

    [Fact]
    public void Overlay_still_unwraps_a_shim_to_an_external_url()
    {
        const string targetValue = "https://youtu.be/dQw4w9wgXcQ";
        string shim = "https://l.facebook.com/l.php?u=" + Uri.EscapeDataString(targetValue);

        Assert.True(ExternalLinks.TryGetExternal(new Uri(shim), out Uri? external, signInStaysInApp: true));
        Assert.Equal(new Uri(targetValue).AbsoluteUri, external!.AbsoluteUri);
    }
}
