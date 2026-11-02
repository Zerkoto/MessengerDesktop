using System.Net;
using System.Net.Http.Headers;
using Xunit;
using Xunit.Abstractions;

namespace MessengerDesktop.Tests;

public class NotificationToastTests
{
    private readonly ITestOutputHelper output;

    public NotificationToastTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public async Task Notification_args_map_to_sender_title_avatar_and_trimmed_body()
    {
        string iconUri = "https://cdn.example/alex.png";
        string badgeUri = "https://cdn.example/badge.png";
        string body = new string('a', 80) + " " + new string('b', 80);
        using var directory = new TempDirectory();
        string? downloaded = await NotificationImageCache.DownloadAsync(
            NotificationToast.PreferredImageUri(iconUri, badgeUri),
            directory.Path,
            new PngHandler());
        string appIcon = directory.Write("app-icon.png", Png);

        NotificationToastContent toast = NotificationToast.Map(
            "  Alex  ",
            body,
            downloaded,
            appIcon,
            "https://www.facebook.com/messages/t/1",
            "m1");

        output.WriteLine("after: " + NotificationToast.Describe(toast));

        Assert.Equal(iconUri, NotificationToast.PreferredImageUri(iconUri, badgeUri));
        Assert.Equal("Alex", toast.Title);
        Assert.Equal(120, toast.Body.Length);
        Assert.StartsWith(new string('a', 80), toast.Body);
        Assert.Equal(downloaded, toast.ImagePath);
        Assert.True(File.Exists(toast.ImagePath));
        Assert.Equal("https://www.facebook.com/messages/t/1", toast.Url);
        Assert.Equal("m1", toast.Tag);
    }

    [Fact]
    public void BadgeUri_is_used_when_IconUri_is_missing()
    {
        Assert.Equal(
            "https://cdn.example/badge.png",
            NotificationToast.PreferredImageUri("  ", " https://cdn.example/badge.png "));
        Assert.Equal(
            "https://cdn.example/badge.png",
            NotificationToast.PreferredImageUri("javascript:alert(1)", "https://cdn.example/badge.png"));
        Assert.Null(NotificationToast.PreferredImageUri("data:image/png;base64,aaaa", "file:///C:/a.png"));
    }

    [Theory]
    [InlineData("New activity", "")]
    [InlineData("New notification", "")]
    [InlineData("  New Activity  ", "   ")]
    [InlineData("new notification", null)]
    [InlineData("", "")]
    [InlineData("   ", null)]
    public void No_toast_payload_uses_a_placeholder_title_or_an_empty_body(string title, string? body)
    {
        NotificationToastContent message = NotificationToast.Map(title, body, null, null, "", "m");
        AssertPayload(message);
        Assert.Equal(ChatListSnippet.NewMessage, message.Body);

        NotificationToastContent reaction = NotificationToast.Map(title, body, null, null, "", "reaction", reaction: true);
        AssertPayload(reaction);
        Assert.Equal(ReactionNotice.FallbackBody, reaction.Body);
    }

    [Fact]
    public void A_message_uses_the_sender_and_the_tray_preview()
    {
        NotificationToastContent toast = NotificationToast.Map(
            "  Alex  ",
            "Unread message: on my way",
            null,
            null,
            "https://www.facebook.com/messages/t/1",
            "m1");

        AssertPayload(toast);
        Assert.Equal("Alex", toast.Title);
        Assert.Equal("on my way", toast.Body);
    }

    [Fact]
    public void A_message_without_a_preview_says_New_message()
    {
        NotificationToastContent toast = NotificationToast.Map("Sam", "   ", null, null, "", "m");

        AssertPayload(toast);
        Assert.Equal("Sam", toast.Title);
        Assert.Equal(ChatListSnippet.NewMessage, toast.Body);
    }

    [Fact]
    public void A_photo_preview_uses_the_tray_label()
    {
        NotificationToastContent toast = NotificationToast.Map("Sam", "sent a photo", null, null, "", "m");

        AssertPayload(toast);
        Assert.Equal("Sam", toast.Title);
        Assert.Equal(ChatListSnippet.Photo, toast.Body);
    }

    [Fact]
    public void A_reaction_uses_ReactionNotice_text()
    {
        NotificationToastContent toast = NotificationToast.Map(
            "Todor Mitrev",
            "Todor Mitrev reacted ❤️ to your message: See you there",
            null,
            null,
            "https://www.facebook.com/messages/t/todor",
            "r1",
            reaction: true);

        AssertPayload(toast);
        Assert.Equal("Todor Mitrev", toast.Title);
        Assert.Equal("Reacted ❤️ to: See you there", toast.Body);
        Assert.Equal("https://www.facebook.com/messages/t/todor", toast.Url);
    }

    [Fact]
    public void A_reaction_without_an_emoji_or_snippet_uses_the_fallback_body()
    {
        NotificationToastContent toast = NotificationToast.Map(
            "Todor Mitrev",
            "reacted to your message",
            null,
            null,
            "",
            "reaction",
            reaction: true);

        AssertPayload(toast);
        Assert.Equal("Todor Mitrev", toast.Title);
        Assert.Equal(ReactionNotice.FallbackBody, toast.Body);
    }

    [Fact]
    public void A_message_does_not_reuse_a_reaction_tag_and_two_reactions_stay_distinct()
    {
        const string url = "https://www.facebook.com/messages/t/alex";
        NotificationToastContent heart = NotificationToast.Map(
            "Alex",
            "Alex reacted ❤️ to your message: See you there",
            null,
            null,
            url,
            "r1",
            reaction: true);
        NotificationToastContent thumb = NotificationToast.Map(
            "Alex",
            "Alex reacted 👍 to your message: On my way",
            null,
            null,
            url,
            "r2",
            reaction: true);
        NotificationToastContent message = NotificationToast.Map("Alex", "on my way", null, null, url, "m1");

        AssertPayload(heart);
        AssertPayload(thumb);
        AssertPayload(message);
        Assert.Equal(ToastTags.ReactionGroup, heart.ToastGroup);
        Assert.Equal(ToastTags.ReactionGroup, thumb.ToastGroup);
        Assert.Equal(ToastTags.MessageGroup, message.ToastGroup);
        Assert.NotEqual(heart.ToastTag, thumb.ToastTag);
        Assert.NotEqual(heart.ToastTag, message.ToastTag);
        Assert.NotEqual(thumb.ToastTag, message.ToastTag);
        Assert.NotEqual(heart.ToastGroup, message.ToastGroup);
        Assert.InRange(heart.ToastTag.Length, 1, ToastTags.MaxLength);
        Assert.InRange(thumb.ToastTag.Length, 1, ToastTags.MaxLength);
        Assert.InRange(message.ToastTag.Length, 1, ToastTags.MaxLength);
        Assert.InRange(heart.ToastGroup.Length, 1, ToastTags.MaxLength);
    }

    private static void AssertPayload(NotificationToastContent toast)
    {
        Assert.False(string.IsNullOrWhiteSpace(toast.Title));
        Assert.False(string.IsNullOrWhiteSpace(toast.Body));
        Assert.False(NotificationToast.IsPlaceholder(toast.Title));
        Assert.False(NotificationToast.IsPlaceholder(toast.Body));
    }

    [Fact]
    public async Task A_failed_picture_download_falls_back_to_the_app_icon()
    {
        using var directory = new TempDirectory();
        string appIcon = directory.Write("app-icon.png", Png);
        string? downloaded = await NotificationImageCache.DownloadAsync(
            "https://cdn.example/not-an-image",
            directory.Path,
            new HtmlHandler());

        NotificationToastContent toast = NotificationToast.Map("Sam", "hello there", downloaded, appIcon, "", "t");

        output.WriteLine("after: " + NotificationToast.Describe(toast));
        Assert.Null(downloaded);
        Assert.Equal("Sam", toast.Title);
        Assert.Equal("hello there", toast.Body);
        Assert.Equal(appIcon, toast.ImagePath);
    }

    [Fact]
    public void Page_notification_fields_keep_icon_and_badge()
    {
        bool parsed = NotificationPageMessage.TryParse(
            "messenger-notification:{\"title\":\"Alex\",\"body\":\"hi\",\"icon\":\"https://cdn.example/a.png\",\"badge\":\"https://cdn.example/b.png\",\"tag\":\"m1\",\"url\":\"https://www.facebook.com/messages/t/1\"}",
            out NotificationPageFields fields);

        Assert.True(parsed);
        Assert.Equal("Alex", fields.Title);
        Assert.Equal("hi", fields.Body);
        Assert.Equal("https://cdn.example/a.png", fields.IconUri);
        Assert.Equal("https://cdn.example/b.png", fields.BadgeUri);
        Assert.Equal("m1", fields.Tag);
        Assert.Contains("showNotification", NotificationShim.Source, StringComparison.Ordinal);
        Assert.Contains(NotificationPageMessage.Prefix, NotificationShim.Source, StringComparison.Ordinal);
        Assert.Contains("permission", NotificationShim.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrelated_web_message_is_not_a_notification()
    {
        Assert.False(NotificationPageMessage.TryParse("messenger-reel-ready", out _));
        Assert.False(NotificationPageMessage.TryParse("messenger-notification:{", out _));
    }

    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
        0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54,
        0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01,
        0x0D, 0x0A, 0x2D, 0xB4,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];

    private sealed class PngHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Png)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        }
    }

    private sealed class HtmlHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>nope</html>")
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(response);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "messenger-toast-tests", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Write(string name, byte[] bytes)
        {
            string path = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
