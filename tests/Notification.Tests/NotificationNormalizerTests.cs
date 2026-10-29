using Xunit;

namespace MessengerDesktop.Tests;

public class NotificationNormalizerTests
{
    [Fact]
    public void Trims_fields_and_names_facebook_from_the_origin()
    {
        NotificationRecord message = NotificationNormalizer.Normalize(
            "  Alex  ",
            "  hello  ",
            "  https://example.com/a.png  ",
            "  m1  ",
            "  https://www.facebook.com/messages/t/1  ",
            "https://www.facebook.com/");

        Assert.Equal("Alex", message.Title);
        Assert.Equal("hello", message.Body);
        Assert.Equal("https://example.com/a.png", message.Icon);
        Assert.Equal("m1", message.Tag);
        Assert.Equal("https://www.facebook.com/messages/t/1", message.Url);
        Assert.Equal("facebook", message.Service);
    }

    [Fact]
    public void A_messenger_origin_selects_the_messenger_service()
    {
        NotificationRecord message = NotificationNormalizer.Normalize(
            "Hi", "there", null, "t", null, "https://www.messenger.com");

        Assert.Equal("messenger", message.Service);
    }

    [Fact]
    public void Missing_fields_become_an_empty_record_with_a_title()
    {
        NotificationRecord message = NotificationNormalizer.Normalize(null, null, null, null, null, null);

        Assert.Equal("Messenger", message.Title);
        Assert.Equal("", message.Body);
        Assert.Equal("", message.Icon);
        Assert.Equal("", message.Tag);
        Assert.Equal("", message.Url);
        Assert.Equal("", message.Service);
    }

    [Fact]
    public void An_unknown_origin_does_not_invent_a_service()
    {
        NotificationRecord message = NotificationNormalizer.Normalize(
            "Hi", "there", null, null, null, "https://example.com");

        Assert.Equal("", message.Service);
    }
}
