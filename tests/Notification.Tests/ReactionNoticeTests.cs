using Xunit;

namespace MessengerDesktop.Tests;

public class ReactionNoticeTests
{
    private const string Chat = "https://www.facebook.com/messages/t/todor";

    [Theory]
    [InlineData("Todor Mitrev reacted ❤️ to your message: See you there", "Todor Mitrev", "Reacted ❤️ to: See you there")]
    [InlineData("Todor Mitrev reacted ❤️ to: See you there", "Todor Mitrev", "Reacted ❤️ to: See you there")]
    [InlineData("reacted ❤️ to your message: See you there", "Todor Mitrev", "Reacted ❤️ to: See you there")]
    public void A_reaction_with_an_emoji_and_a_snippet_names_both(string text, string title, string body)
    {
        Assert.True(ReactionNotice.TryParse("Todor Mitrev", text, Chat, out ReactionToast toast));
        Assert.Equal(title, toast.Title);
        Assert.Equal(body, toast.Body);
        Assert.Equal(Chat, toast.Url);
        Assert.DoesNotContain("\n", toast.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Todor Mitrev reacted ❤️ to your message")]
    [InlineData("Todor Mitrev reacted to your message: See you there")]
    [InlineData("Todor Mitrev reacted to your message")]
    [InlineData("reacted to your message")]
    public void A_missing_emoji_or_snippet_uses_the_plain_fallback(string text)
    {
        Assert.True(ReactionNotice.TryParse("Todor Mitrev", text, Chat, out ReactionToast toast));
        Assert.Equal("Todor Mitrev", toast.Title);
        Assert.Equal(ReactionNotice.FallbackBody, toast.Body);
    }

    [Fact]
    public void Bulgarian_reaction_text_keeps_the_emoji_and_the_snippet()
    {
        Assert.True(ReactionNotice.TryParse(
            "Family",
            "Todor Mitrev реагира с ❤️ на съобщението ви: здрасти",
            Chat,
            out ReactionToast toast));

        Assert.Equal("Todor Mitrev", toast.Title);
        Assert.Equal("Reacted ❤️ to: здрасти", toast.Body);
        Assert.Equal(Chat, toast.Url);
    }

    [Fact]
    public void A_bulgarian_reaction_without_an_emoji_uses_the_fallback()
    {
        Assert.True(ReactionNotice.TryParse(
            "Todor Mitrev",
            "реагира на съобщението ви",
            Chat,
            out ReactionToast toast));

        Assert.Equal("Todor Mitrev", toast.Title);
        Assert.Equal(ReactionNotice.FallbackBody, toast.Body);
    }

    [Fact]
    public void A_notification_payload_is_the_same_reaction()
    {
        var summary = new TraySummary();
        summary.Observe(new NotificationRecord(
            "Todor Mitrev",
            "reacted ❤️ to your message: \"See you there\"",
            "https://cdn.example/a.png",
            "r1",
            Chat,
            "facebook"), TraySummaryTestsTime.T0);

        Assert.True(summary.TryReactionToast(TraySummaryTestsTime.T0, out ReactionToast toast));
        Assert.Equal("Todor Mitrev", toast.Title);
        Assert.Equal("Reacted ❤️ to: See you there", toast.Body);
        Assert.Equal(Chat, toast.Url);
        Assert.Equal(0, summary.UnreadMessages);
    }

    [Fact]
    public void An_ordinary_message_is_not_a_reaction()
    {
        Assert.False(ReactionNotice.TryParse("Todor Mitrev", "on my way", Chat, out _));
        Assert.False(ReactionNotice.TryParse("Todor Mitrev", "I reacted badly", Chat, out _));
    }
}

internal static class TraySummaryTestsTime
{
    public static readonly DateTimeOffset T0 = new(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);
}
