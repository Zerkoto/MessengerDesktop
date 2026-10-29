using Xunit;

namespace MessengerDesktop.Tests;

public class ChatListWatchTests
{
    [Fact]
    public void A_chat_list_payload_becomes_threads()
    {
        const string message =
            "messenger-chat-list:{\"ok\":true,\"rows\":[{\"name\":\"Todor\",\"preview\":\"sent an attachment.\",\"avatar\":\"https://cdn.example/a.jpg\",\"time\":\"21m\",\"href\":\"https://www.facebook.com/messages/t/9\"}]}";

        Assert.True(ChatListMessage.TryParse(message, out bool readable, out IReadOnlyList<ChatThread> threads));

        Assert.True(readable);
        ChatThread thread = Assert.Single(threads);
        Assert.Equal("Todor", thread.Name);
        Assert.Equal("sent an attachment.", thread.Preview);
        Assert.Equal("https://cdn.example/a.jpg", thread.AvatarUrl);
        Assert.Equal("21m", thread.RelativeTime);
        Assert.Equal("https://www.facebook.com/messages/t/9", thread.Href);
    }

    [Fact]
    public void An_unreadable_chat_list_payload_is_not_a_row_list()
    {
        Assert.True(ChatListMessage.TryParse(
            "messenger-chat-list:{\"ok\":false,\"reason\":\"unreadable\",\"rows\":[{\"name\":\"Todor\",\"preview\":\"x\",\"avatar\":\"\",\"time\":\"\",\"href\":\"\"}]}",
            out bool readable,
            out IReadOnlyList<ChatThread> threads));
        Assert.False(readable);
        Assert.Empty(threads);
        Assert.False(ChatListMessage.TryParse("messenger-chat-list:{", out _, out _));
        Assert.False(ChatListMessage.TryParse("messenger-notification:{}", out _, out _));
    }

    [Fact]
    public void The_chat_list_watch_reads_the_grid_and_does_not_click()
    {
        string source = ChatListWatch.Source;

        Assert.Contains("MutationObserver", source, StringComparison.Ordinal);
        Assert.Contains("[role=\"navigation\"]", source, StringComparison.Ordinal);
        Assert.Contains("[role=\"grid\"]", source, StringComparison.Ordinal);
        Assert.Contains("role=\"row\"", source, StringComparison.Ordinal);
        Assert.Contains("messenger-chat-list:", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".click(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("dispatchEvent", source, StringComparison.Ordinal);
    }
}
