using Xunit;

namespace MessengerDesktop.Tests;

public class ChatListWatchTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_thread_payload_keeps_the_name_path_and_preview()
    {
        bool parsed = ChatListMessage.TryParse(
            """{"type":"messenger-chat-list-activity","name":" Alex ","href":"/messages/t/alex","preview":"You: hi"}""",
            out ChatListActivity? activity);

        Assert.True(parsed);
        Assert.NotNull(activity);
        Assert.Equal("Alex", activity.Name);
        Assert.Equal("/messages/t/alex", activity.Href);
        Assert.Equal("You: hi", activity.Preview);
    }

    [Fact]
    public void An_absolute_facebook_thread_url_is_normalized_to_its_path()
    {
        bool parsed = ChatListMessage.TryParse(
            """{"type":"messenger-chat-list-activity","name":"Alex","href":"https://www.facebook.com/messages/e2ee/t/alex?mid=9","preview":"hi"}""",
            out ChatListActivity? activity);

        Assert.True(parsed);
        Assert.Equal("/messages/e2ee/t/alex", activity!.Href);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"type":"messenger-open-reel","url":"https://www.facebook.com/reel/1"}""")]
    [InlineData("""{"type":"messenger-chat-list-activity","name":"","href":"/messages/t/1","preview":"hi"}""")]
    [InlineData("""{"type":"messenger-chat-list-activity","name":"Alex","href":"https://evil.test/t/1","preview":"hi"}""")]
    [InlineData("""{"type":"messenger-chat-list-activity","name":"Alex","href":"https://facebook.com.evil.com/messages/t/1","preview":"hi"}""")]
    [InlineData("""{"type":"messenger-chat-list-activity","name":"Alex","href":"javascript:alert(1)","preview":"hi"}""")]
    [InlineData("""{"type":"messenger-chat-list-activity","name":"Alex","href":"http://www.facebook.com/messages/t/1","preview":"hi"}""")]
    [InlineData("""{"type":"messenger-chat-list-activity","name":"Alex","href":"/messages","preview":"hi"}""")]
    [InlineData("""{"type":"messenger-chat-list-activity","href":"/messages/t/1","preview":"hi"}""")]
    [InlineData("""{"type":"messenger-chat-list-activity","name":"Alex","href":"/messages/t/1"}""")]
    public void Invalid_payloads_are_rejected(string json)
    {
        Assert.False(ChatListMessage.TryParse(json, out ChatListActivity? activity));
        Assert.Null(activity);
    }

    [Fact]
    public void The_same_preview_is_accepted_once_and_stays_seen_across_a_reload()
    {
        var watch = new ChatListWatch(Window);

        Assert.Equal(ChatListEffect.Count, watch.Observe("/messages/t/1", "You: hi", T0));
        Assert.Equal(ChatListEffect.Ignored, watch.Observe("/messages/t/1", "You: hi", T0.AddMinutes(5)));
        Assert.Equal(ChatListEffect.Count, watch.Observe("/messages/t/1", "reacted", T0.AddMinutes(6)));
        Assert.Equal(ChatListEffect.Count, watch.Observe("/messages/t/2", "You: hi", T0.AddMinutes(7)));

        var reloaded = new ChatListWatch(Window, watch.SeenKeys);

        Assert.Equal(ChatListEffect.Ignored, reloaded.Observe("/messages/t/1", "You: hi", T0));
        Assert.Equal(ChatListEffect.Ignored, reloaded.Observe("/messages/t/1", "reacted", T0));
        Assert.Equal(ChatListEffect.Count, reloaded.Observe("/messages/t/3", "You: hi", T0));
    }

    [Fact]
    public void Parsed_absolute_and_relative_hrefs_dedupe_as_the_same_thread()
    {
        Assert.True(ChatListMessage.TryParse(Payload("/messages/t/1", "hi"), out ChatListActivity? path));
        Assert.True(ChatListMessage.TryParse(Payload("https://m.facebook.com/messages/t/1?mid=2", "hi"), out ChatListActivity? absolute));
        var watch = new ChatListWatch(Window);

        Assert.Equal(ChatListEffect.Count, watch.Observe(path!.Href, path.Preview, T0));
        Assert.Equal(ChatListEffect.Ignored, watch.Observe(absolute!.Href, absolute.Preview, T0.AddSeconds(1)));
    }

    [Fact]
    public void Chat_list_activity_is_labelled_new_activity_and_clears_when_the_summary_opens()
    {
        var summary = new TraySummary();

        summary.NoteChatListActivity("Alex", "/messages/t/alex", T0, count: true);

        TrayRow row = Assert.Single(summary.Latest());
        Assert.Equal("New activity", row.Name);
        Assert.Equal("Alex", row.Secondary);
        Assert.Equal(TrayRowKind.Reaction, row.Kind);
        Assert.Equal("https://www.facebook.com/messages/t/alex", row.Url);
        Assert.Equal(0, summary.UnreadMessages);
        Assert.Equal(1, summary.UnseenReactions);
        Assert.Equal(1, summary.BadgeCount);
        Assert.False(ChatListWatch.ShouldToast);

        summary.OnPanelOpened();

        Assert.Equal(0, summary.UnseenReactions);
        Assert.Equal(0, summary.BadgeCount);
        Assert.DoesNotContain(summary.Latest(), item => item.Kind == TrayRowKind.Reaction);
    }

    [Fact]
    public void A_chat_list_event_merges_when_reaction_watch_already_covered_the_rise()
    {
        var summary = new TraySummary();
        var watch = new ChatListWatch(Window);
        Assert.True(watch.CountReactionRise(T0));
        summary.NoteActivity(T0);

        ChatListEffect effect = watch.Observe("/messages/t/alex", "You: hi", T0.AddSeconds(2));
        Assert.Equal(ChatListEffect.Merge, effect);
        summary.NoteChatListActivity("Alex", "/messages/t/alex", T0.AddSeconds(2), count: false);

        Assert.Equal(1, summary.UnseenReactions);
        Assert.Equal(1, summary.BadgeCount);
        TrayRow row = Assert.Single(summary.Latest());
        Assert.Equal("New activity", row.Name);
        Assert.Equal("Alex", row.Secondary);
        Assert.False(ChatListWatch.ShouldToast);
    }

    [Fact]
    public void A_reaction_rise_inside_the_window_does_not_count_a_second_time()
    {
        var summary = new TraySummary();
        var watch = new ChatListWatch(Window);
        Assert.Equal(ChatListEffect.Count, watch.Observe("/messages/t/alex", "You: hi", T0));
        summary.NoteChatListActivity("Alex", "/messages/t/alex", T0, count: true);
        Assert.Equal(ChatListEffect.Ignored, watch.Observe("/messages/t/alex", "You: hi", T0.AddSeconds(1)));

        Assert.False(watch.CountReactionRise(T0.AddSeconds(2)));

        Assert.Equal(1, summary.UnseenReactions);
        Assert.Equal("Alex", Assert.Single(summary.Latest()).Secondary);
    }

    [Fact]
    public void A_rise_outside_the_window_counts_on_its_own()
    {
        var watch = new ChatListWatch(Window);
        Assert.True(watch.CountReactionRise(T0));

        Assert.Equal(ChatListEffect.Count, watch.Observe("/messages/t/alex", "later", T0.AddSeconds(3)));
        Assert.True(watch.CountReactionRise(T0.AddSeconds(6)));
    }

    [Fact]
    public void Only_the_first_chat_in_a_covered_rise_merges()
    {
        var summary = new TraySummary();
        var watch = new ChatListWatch(Window);
        Assert.True(watch.CountReactionRise(T0));
        summary.NoteActivity(T0);

        Assert.Equal(ChatListEffect.Merge, watch.Observe("/messages/t/alex", "one", T0.AddSeconds(1)));
        summary.NoteChatListActivity("Alex", "/messages/t/alex", T0.AddSeconds(1), count: false);
        Assert.Equal(ChatListEffect.Count, watch.Observe("/messages/t/blair", "two", T0.AddSeconds(1)));
        summary.NoteChatListActivity("Blair", "/messages/t/blair", T0.AddSeconds(1), count: true);

        Assert.Equal(2, summary.UnseenReactions);
        Assert.Contains(summary.Latest(), row => row.Secondary == "Blair");
    }

    [Fact]
    public void The_chat_list_script_only_observes_the_grid()
    {
        string source = File.ReadAllText(ScriptPath());

        Assert.Contains("MutationObserver", source, StringComparison.Ordinal);
        Assert.Contains("chrome.webview.postMessage", source, StringComparison.Ordinal);
        Assert.Contains("messenger-chat-list-activity", source, StringComparison.Ordinal);
        Assert.Contains("[role=\"navigation\"]", source, StringComparison.Ordinal);
        Assert.Contains("[role=\"grid\"]", source, StringComparison.Ordinal);
        Assert.Contains("a[href*=\"/t/\"]", source, StringComparison.Ordinal);
        Assert.Contains("sessionStorage", source, StringComparison.Ordinal);
        Assert.Contains("fontWeight", source, StringComparison.Ordinal);
        Assert.Contains("__messengerChatListWatch", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("focus(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("scroll", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("markRead", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mark-read", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("className", source, StringComparison.Ordinal);
        Assert.DoesNotContain("classList", source, StringComparison.Ordinal);
        Assert.DoesNotContain("unread", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reacted", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string Payload(string href, string preview) =>
        $$"""{"type":"messenger-chat-list-activity","name":"Alex","href":"{{href}}","preview":"{{preview}}"}""";

    private static string ScriptPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "Scripts", "ChatListWatch.js");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Scripts/ChatListWatch.js");
    }
}
