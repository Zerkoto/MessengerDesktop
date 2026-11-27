using Xunit;

namespace MessengerDesktop.Tests;

public class ExternalLinksTests
{
    [Theory]
    [InlineData("https://youtube.com/watch?v=dQw4w9wgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9wgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9wgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9wgXcQ")]
    [InlineData("https://gaming.youtube.com/watch?v=1")]
    [InlineData("http://youtube.com/watch?v=1")]
    [InlineData("http://www.youtube.com/shorts/abc")]
    [InlineData("https://YouTube.com/watch?v=1")]
    [InlineData("https://WWW.YOUTUBE.COM/watch?v=1&t=10s")]
    [InlineData("https://youtube-nocookie.com/embed/dQw4w9wgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9wgXcQ")]
    [InlineData("https://youtu.be/dQw4w9wgXcQ")]
    [InlineData("http://youtu.be/dQw4w9wgXcQ")]
    [InlineData("https://youtu.be/dQw4w9wgXcQ?t=12")]
    [InlineData("https://www.youtube.com:8443/watch?v=1")]
    public void YouTube_hosts_match(string value)
    {
        Assert.True(ExternalLinks.IsYouTube(new Uri(value)));
    }

    [Theory]
    [InlineData("https://notyoutube.com/watch?v=1")]
    [InlineData("https://notyoutube.com")]
    [InlineData("https://youtube.com.evil.com/watch?v=1")]
    [InlineData("https://www.youtube.com.evil.com/watch?v=1")]
    [InlineData("https://evil.com/youtube.com/watch?v=1")]
    [InlineData("https://notyoutu.be/dQw4w9wgXcQ")]
    [InlineData("https://youtu.be.evil.com/dQw4w9wgXcQ")]
    [InlineData("https://www.youtu.be/dQw4w9wgXcQ")]
    [InlineData("https://youtube-nocookie.com.evil.com/embed/1")]
    [InlineData("https://notyoutube-nocookie.com/embed/1")]
    [InlineData("https://www.youtube-nocookie.com.evil.com/embed/1")]
    [InlineData("ftp://youtube.com/watch?v=1")]
    [InlineData("ftp://youtu.be/dQw4w9wgXcQ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://www.facebook.com/watch?v=1")]
    [InlineData("https://x.com/jack/status/20")]
    [InlineData("https://l.facebook.com/l.php?u=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3D1")]
    [InlineData("https://lm.facebook.com/l.php?u=https%3A%2F%2Fyoutu.be%2Fabc")]
    public void Lookalike_and_non_http_urls_do_not_match(string value)
    {
        Assert.False(ExternalLinks.IsYouTube(new Uri(value)));
    }

    [Fact]
    public void Null_and_relative_urls_do_not_match()
    {
        Assert.False(ExternalLinks.IsYouTube(null));
        Assert.False(ExternalLinks.IsYouTube(new Uri("/watch?v=1", UriKind.Relative)));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9wgXcQ")]
    [InlineData("https://youtu.be/dQw4w9wgXcQ")]
    [InlineData("http://m.youtube.com/watch?v=1")]
    public void Direct_youtube_link_is_the_browser_target(string value)
    {
        var clicked = new Uri(value);

        Assert.True(ExternalLinks.TryGetYouTube(clicked, out Uri? youtube));
        Assert.Equal(clicked.AbsoluteUri, youtube!.AbsoluteUri);
    }

    [Theory]
    [InlineData("l.facebook.com", "https://www.youtube.com/watch?v=dQw4w9wgXcQ")]
    [InlineData("l.facebook.com", "https://youtu.be/dQw4w9wgXcQ")]
    [InlineData("l.facebook.com", "http://youtube.com/watch?v=1")]
    [InlineData("l.facebook.com", "https://music.youtube.com/watch?v=1")]
    [InlineData("l.facebook.com", "https://www.youtube-nocookie.com/embed/abc")]
    [InlineData("lm.facebook.com", "https://www.youtube.com/watch?v=dQw4w9wgXcQ")]
    [InlineData("lm.facebook.com", "https://m.youtube.com/watch?v=1")]
    [InlineData("lm.facebook.com", "https://youtu.be/abc")]
    public void Facebook_shims_unwrap_to_the_youtube_url(string host, string youtubeValue)
    {
        string shim = "https://" + host + "/l.php?u=" + Uri.EscapeDataString(youtubeValue) + "&h=AT0";

        Assert.True(ExternalLinks.TryGetYouTube(new Uri(shim), out Uri? youtube));
        Assert.Equal(new Uri(youtubeValue).AbsoluteUri, youtube!.AbsoluteUri);
        Assert.False(ExternalLinks.IsYouTube(new Uri(shim)));
    }

    [Fact]
    public void Shim_query_accepts_the_destination_in_any_position()
    {
        const string youtubeValue = "https://www.youtube.com/watch?v=1";
        string shim = "https://l.facebook.com/l.php?h=AT0&u=" + Uri.EscapeDataString(youtubeValue);

        Assert.True(ExternalLinks.TryGetYouTube(new Uri(shim), out Uri? youtube));
        Assert.Equal(youtubeValue, youtube!.AbsoluteUri);
    }

    [Fact]
    public void Double_encoded_shim_unwraps_to_the_youtube_url()
    {
        const string youtubeValue = "https://youtu.be/dQw4w9wgXcQ";
        string shim = "https://lm.facebook.com/l.php?u=" + Uri.EscapeDataString(Uri.EscapeDataString(youtubeValue));

        Assert.True(ExternalLinks.TryGetYouTube(new Uri(shim), out Uri? youtube));
        Assert.Equal(youtubeValue, youtube!.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://l.facebook.com/l.php?u=https%3A%2F%2Fnotyoutube.com%2Fwatch%3Fv%3D1")]
    [InlineData("https://lm.facebook.com/l.php?u=https%3A%2F%2Fnotyoutube.com%2Fwatch%3Fv%3D1")]
    [InlineData("https://l.facebook.com/l.php?u=https%3A%2F%2Fx.com%2Fjack%2Fstatus%2F20")]
    [InlineData("https://lm.facebook.com/l.php?u=https%3A%2F%2Fx.com%2Fjack%2Fstatus%2F20")]
    [InlineData("https://l.facebook.com/l.php?u=https%3A%2F%2Fwww.facebook.com%2Freel%2F1")]
    [InlineData("https://l.facebook.com.evil.com/l.php?u=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3D1")]
    [InlineData("https://www.facebook.com/l.php?u=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3D1")]
    [InlineData("https://l.facebook.com/l.php")]
    [InlineData("https://l.facebook.com/l.php?u=not-a-url")]
    [InlineData("https://l.facebook.com/l.php?u=javascript%3Aalert(1)")]
    [InlineData("https://notyoutube.com/watch?v=1")]
    [InlineData("http://x.com/jack/status/20")]
    public void Non_youtube_shims_and_lookalikes_stay_in_the_app(string value)
    {
        Assert.False(ExternalLinks.TryGetYouTube(new Uri(value), out Uri? youtube));
        Assert.Null(youtube);
    }
}
