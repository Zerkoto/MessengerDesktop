using Xunit;

namespace MessengerDesktop.Tests;

public class NotificationNavigationTests
{
    [Fact]
    public void A_facebook_message_url_opens_that_chat()
    {
        Uri target = NotificationNavigation.OpenTarget(
            "https://www.facebook.com/messages/t/123?mid=9",
            "https://www.messenger.com/t/other");

        Assert.Equal("https://www.facebook.com/messages/t/123?mid=9", target.AbsoluteUri);
    }

    [Fact]
    public void A_messenger_thread_url_in_the_tag_is_used_when_the_url_is_empty()
    {
        Uri target = NotificationNavigation.OpenTarget(null, "https://www.messenger.com/t/123");

        Assert.Equal("https://www.messenger.com/t/123", target.AbsoluteUri);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("https://evil.example/messages/t/1", "nope")]
    [InlineData("https://www.facebook.com/reel/1", null)]
    [InlineData("http://www.facebook.com/messages/t/1", "thread")]
    [InlineData("javascript:alert(1)", "not-a-url")]
    public void Anything_else_opens_the_inbox(string? url, string? tag)
    {
        Assert.Equal(NotificationNavigation.Inbox, NotificationNavigation.OpenTarget(url, tag));
    }

    [Fact]
    public void A_rejected_url_still_honors_a_chat_url_in_the_tag()
    {
        Uri target = NotificationNavigation.OpenTarget(
            "javascript:alert(1)",
            "https://www.facebook.com/messages/t/1");

        Assert.Equal("https://www.facebook.com/messages/t/1", target.AbsoluteUri);
    }

    [Fact]
    public void The_inbox_is_the_messenger_thread_list()
    {
        Assert.Equal("https://www.facebook.com/messages/t", NotificationNavigation.Inbox.AbsoluteUri);
    }
}
