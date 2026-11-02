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
        Assert.Contains("span[dir=\"auto\"]", source, StringComparison.Ordinal);
        Assert.Contains("непрочетено съобщение", source, StringComparison.Ordinal);
        Assert.Contains("sent a photo", source, StringComparison.Ordinal);
        Assert.Contains("sent a sticker", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".click(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".focus(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("scrollIntoView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("dispatchEvent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_uses_the_snippet_under_the_name_without_the_unread_label()
    {
        const string html =
            """
            <div role="row">
              <a href="https://www.facebook.com/messages/t/9">
                <img alt="" src="https://cdn.example/a.jpg" />
                <span dir="auto">Todor</span>
                <span dir="auto"><span>Unread message:</span><span>on my way</span></span>
                <abbr>21m</abbr>
              </a>
            </div>
            """;

        ChatListParsedRow row = ChatListRowHtml.Parse(html);

        Assert.Equal("Todor", row.Name);
        Assert.Equal("on my way", row.Preview);
        Assert.Equal("https://cdn.example/a.jpg", row.AvatarUrl);
        Assert.Equal("21m", row.RelativeTime);
        Assert.Equal("https://www.facebook.com/messages/t/9", row.Href);
        Assert.DoesNotContain("Unread message", row.Preview, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_span_is_not_the_preview_when_the_snippet_is_the_next_span()
    {
        const string html =
            """
            <div role="row">
              <span dir="auto">Todor</span>
              <span dir="auto">Unread message:</span>
              <span>the real preview</span>
            </div>
            """;

        Assert.Equal("the real preview", ChatListRowHtml.Parse(html).Preview);
    }

    [Fact]
    public void A_bulgarian_unread_prefix_is_removed_and_you_is_kept()
    {
        const string html =
            """
            <div role="row">
              <span dir="auto">Todor</span>
              <span dir="auto"><span>Непрочетено съобщение:</span><span>You: донеси мляко</span></span>
            </div>
            """;

        Assert.Equal("You: донеси мляко", ChatListRowHtml.Parse(html).Preview);
    }

    [Fact]
    public void A_snippet_is_one_line()
    {
        const string html =
            """
            <div role="row">
              <span dir="auto">Alex</span>
              <span dir="auto">Unread message: hello
            there</span>
            </div>
            """;

        string preview = ChatListRowHtml.Parse(html).Preview;
        Assert.Equal("hello there", preview);
        Assert.DoesNotContain("\n", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Photo_and_sticker_snippets_become_a_single_label()
    {
        const string photo =
            """
            <div role="row">
              <span dir="auto">Alex</span>
              <span dir="auto">You sent a photo.</span>
            </div>
            """;
        const string photoImage =
            """
            <div role="row">
              <img src="https://cdn.example/a.jpg" alt="" />
              <span dir="auto">Alex</span>
              <span dir="auto"><img alt="" src="https://cdn.example/p.jpg" /></span>
            </div>
            """;
        const string sticker =
            """
            <div role="row">
              <span dir="auto">Alex</span>
              <span dir="auto">sent a sticker.</span>
            </div>
            """;
        const string stickerImage =
            """
            <div role="row">
              <span dir="auto">Alex</span>
              <span dir="auto"><img alt="Sticker" src="https://cdn.example/s.png" /></span>
            </div>
            """;

        Assert.Equal(ChatListSnippet.Photo, ChatListRowHtml.Parse(photo).Preview);
        Assert.Equal(ChatListSnippet.Photo, ChatListRowHtml.Parse(photoImage).Preview);
        Assert.Equal(ChatListSnippet.Sticker, ChatListRowHtml.Parse(sticker).Preview);
        Assert.Equal(ChatListSnippet.Sticker, ChatListRowHtml.Parse(stickerImage).Preview);
    }

    [Fact]
    public void A_row_with_only_the_unread_label_has_no_snippet()
    {
        const string html =
            """
            <div role="row">
              <span dir="auto">Alex</span>
              <span dir="auto">Unread message:</span>
            </div>
            """;

        Assert.Equal("", ChatListRowHtml.Parse(html).Preview);
    }
}
