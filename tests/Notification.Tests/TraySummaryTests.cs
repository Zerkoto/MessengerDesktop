using Xunit;

namespace MessengerDesktop.Tests;

public class TraySummaryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Badge_is_blank_at_zero_and_the_tooltip_is_the_app_name()
    {
        var summary = new TraySummary();

        Assert.Equal(0, summary.BadgeCount);
        Assert.Equal("", summary.BadgeLabel);
        Assert.Equal(TrayGlyph.None, summary.GlyphAt(T0));
        Assert.Equal("Messenger+", summary.Tooltip);
        Assert.True(summary.IsEmpty);
        Assert.Equal(TrayText.EmptyState, "You're all caught up ✓");
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(9, "9")]
    public void Badge_shows_a_single_digit_through_nine(int count, string label)
    {
        var summary = Messages(count);

        Assert.Equal(count, summary.BadgeCount);
        Assert.Equal(label, summary.BadgeLabel);
        Assert.Equal(TrayGlyph.Count, summary.GlyphAt(T0));
        Assert.Equal($"Messenger+ · {count} unread", summary.Tooltip);
    }

    [Fact]
    public void Badge_shows_nine_plus_above_nine_and_the_tooltip_keeps_the_full_count()
    {
        var summary = Messages(10);

        Assert.Equal(10, summary.BadgeCount);
        Assert.Equal("9+", summary.BadgeLabel);
        Assert.Equal("Messenger+ · 10 unread", summary.Tooltip);
        Assert.Equal(TrayGlyph.Count, summary.GlyphAt(T0));
    }

    [Fact]
    public void Reactions_count_until_the_panel_opens_and_then_drop_out()
    {
        var summary = new TraySummary();
        summary.Observe(Message("Alex", "hi", "https://www.facebook.com/messages/t/1"), T0);
        summary.Observe(Reaction("Sam", "https://www.facebook.com/messages/t/2"), T0.AddMinutes(1));
        summary.NoteActivity(T0.AddMinutes(2));

        Assert.Equal(3, summary.BadgeCount);
        Assert.Equal(1, summary.UnreadMessages);
        Assert.Equal(2, summary.UnseenReactions);
        Assert.Contains(summary.Latest(), row => row.Kind == TrayRowKind.Reaction && row.Secondary == TrayText.ReactionLine);

        summary.OnPanelOpened();

        Assert.Equal(1, summary.BadgeCount);
        Assert.Equal(1, summary.UnreadMessages);
        Assert.Equal(0, summary.UnseenReactions);
        Assert.DoesNotContain(summary.Latest(), row => row.Kind == TrayRowKind.Reaction);
        Assert.Equal("1", summary.BadgeLabel);
        Assert.Equal("Messenger+ · 1 unread", summary.Tooltip);
    }

    [Fact]
    public void Messages_clear_only_when_that_chat_is_opened()
    {
        var summary = new TraySummary();
        summary.Observe(Message("Alex", "hi", "https://www.facebook.com/messages/t/alex"), T0);
        summary.Observe(Message("Blair", "hey", "https://www.facebook.com/messages/t/blair"), T0.AddMinutes(1));
        summary.Observe(Reaction("Sam", "https://www.facebook.com/messages/t/sam"), T0.AddMinutes(2));

        summary.OnPanelOpened();
        Assert.Equal(2, summary.BadgeCount);

        summary.MarkChatRead("https://m.facebook.com/messages/t/alex");

        Assert.Equal(1, summary.BadgeCount);
        Assert.Equal("Blair", Assert.Single(summary.Latest()).Name);
        Assert.Equal("Messenger+ · 1 unread", summary.Tooltip);
    }

    [Fact]
    public void Mark_all_read_clears_only_the_local_summary()
    {
        var summary = new TraySummary();
        summary.Observe(Message("Alex", "hi", "https://www.facebook.com/messages/t/alex"), T0);
        summary.Observe(Message("Alex", "again", "https://www.facebook.com/messages/t/alex"), T0.AddMinutes(1));
        summary.NoteActivity(T0.AddMinutes(2));
        int badge = summary.BadgeCount;

        summary.MarkAllRead();

        Assert.True(summary.IsEmpty);
        Assert.Equal(badge, summary.BadgeCount);
        Assert.Equal(2, summary.UnreadMessages);
        Assert.Equal(1, summary.UnseenReactions);
        Assert.Equal("3", summary.BadgeLabel);
        Assert.Equal("Messenger+ · 3 unread", summary.Tooltip);

        summary.MarkChatRead("https://www.facebook.com/messages/t/alex");
        Assert.Equal(1, summary.BadgeCount);
    }

    [Fact]
    public void The_list_keeps_the_five_latest_conversations()
    {
        var summary = new TraySummary();
        for (int i = 0; i < 6; i++)
        {
            summary.Observe(
                Message($"Person {i}", $"preview {i}", $"https://www.facebook.com/messages/t/{i}"),
                T0.AddMinutes(i));
        }

        IReadOnlyList<TrayRow> rows = summary.Latest();

        Assert.Equal(5, rows.Count);
        Assert.Equal("Person 5", rows[0].Name);
        Assert.Equal("Person 1", rows[4].Name);
        Assert.DoesNotContain(rows, row => row.Name == "Person 0");
        Assert.Equal(6, summary.BadgeCount);
        Assert.Equal("9+", TrayText.Badge(10));
    }

    [Fact]
    public void A_newer_message_in_an_older_chat_moves_that_chat_to_the_top()
    {
        var summary = new TraySummary();
        summary.Observe(Message("Alex", "one", "https://www.facebook.com/messages/t/alex"), T0);
        summary.Observe(Message("Blair", "two", "https://www.facebook.com/messages/t/blair"), T0.AddMinutes(5));
        summary.Observe(Message("Alex", "three", "https://www.facebook.com/messages/t/alex"), T0.AddMinutes(6));

        TrayRow first = summary.Latest()[0];
        Assert.Equal("Alex", first.Name);
        Assert.Equal("three", first.Preview);
        Assert.Equal(2, first.MessageCount);
        Assert.Equal(3, summary.BadgeCount);
    }

    [Fact]
    public void Notifications_without_a_chat_link_group_by_the_captured_name()
    {
        var summary = new TraySummary();
        summary.Observe(new NotificationRecord("Alex", "one", "", "m1", "", "facebook"), T0);
        summary.Observe(new NotificationRecord("Alex", "two", "", "m2", "", "facebook"), T0.AddMinutes(1));
        summary.Observe(new NotificationRecord("Blair", "", "", "m3", "", "facebook"), T0.AddMinutes(2));

        Assert.Equal(3, summary.BadgeCount);
        Assert.Equal(2, summary.Latest().Count);
        TrayRow alex = Assert.Single(summary.Latest(), row => row.Name == "Alex");
        Assert.Equal("two", alex.Preview);
        Assert.Equal(2, alex.MessageCount);

        summary.MarkRowRead(alex.Key);

        Assert.Equal(1, summary.UnreadMessages);
        Assert.Equal("Blair", Assert.Single(summary.Latest()).Name);
        Assert.True(Assert.Single(summary.Latest()).CountOnly);
    }

    [Fact]
    public void A_missing_preview_is_the_name_and_the_count_only()
    {
        var summary = new TraySummary();
        summary.Observe(Message("Alex", "   ", "https://www.facebook.com/messages/t/alex"), T0);
        summary.Observe(Message("Alex", "", "https://www.facebook.com/messages/t/alex"), T0.AddMinutes(1));

        TrayRow row = Assert.Single(summary.Latest());

        Assert.Null(row.Preview);
        Assert.True(row.CountOnly);
        Assert.Equal("Alex", row.Name);
        Assert.Equal(2, row.MessageCount);
        Assert.Equal("2", row.Secondary);
        Assert.Equal("A", row.Initials);
        Assert.Null(row.AvatarUrl);
    }

    [Fact]
    public void Initials_fall_back_for_a_single_name_and_a_blank_name()
    {
        Assert.Equal("A", TrayText.Initials("ada"));
        Assert.Equal("?", TrayText.Initials("   "));
        Assert.Equal("?", TrayText.Initials(null));
        Assert.Equal("S", TrayText.Initials("Sam"));
    }

    [Fact]
    public void Mute_replaces_the_count_with_a_grey_dot_until_it_ends()
    {
        var summary = Messages(3);

        summary.Mute(TrayMute.OneHour, T0);

        Assert.Equal(TrayGlyph.Muted, summary.GlyphAt(T0.AddMinutes(59)));
        Assert.Equal(TrayMute.OneHour, summary.MuteChoice(T0.AddMinutes(59)));
        Assert.Equal("3", summary.BadgeLabel);
        Assert.Equal("Messenger+ · 3 unread", summary.Tooltip);
        Assert.Equal(TrayGlyph.Count, summary.GlyphAt(T0.AddHours(1)));
        Assert.Equal(TrayMute.Off, summary.MuteChoice(T0.AddHours(1)));

        summary.Mute(TrayMute.EightHours, T0);
        Assert.True(summary.IsMuted(T0.AddHours(7)));
        Assert.False(summary.IsMuted(T0.AddHours(8)));

        summary.Mute(TrayMute.UntilOn, T0);
        Assert.Equal(TrayGlyph.Muted, summary.GlyphAt(T0.AddDays(40)));
        Assert.Equal(TrayMute.UntilOn, summary.MuteChoice(T0.AddDays(40)));
        summary.Unmute();
        Assert.Equal(TrayGlyph.Count, summary.GlyphAt(T0.AddDays(40)));
    }

    [Fact]
    public void A_muted_icon_with_no_unread_is_still_the_grey_dot()
    {
        var summary = new TraySummary();
        summary.Mute(TrayMute.UntilOn, T0);

        Assert.Equal(TrayGlyph.Muted, summary.GlyphAt(T0));
        Assert.Equal("", summary.BadgeLabel);
        Assert.Equal("Messenger+", summary.Tooltip);
    }

    [Fact]
    public void The_count_badge_is_a_red_circle_with_a_white_ring_at_the_bottom_right()
    {
        IReadOnlyList<TrayDisk> layers = TrayBadgeLayout.Layers(TrayGlyph.Count);

        TrayDisk ring = layers[0];
        TrayDisk fill = layers[1];
        Assert.Equal(0xFFFFFF, TrayBadgeMetrics.Rgb(ring.Argb));
        Assert.Equal(0xE81123, TrayBadgeMetrics.Rgb(fill.Argb));
        Assert.Equal(TrayBadgeMetrics.CountDiameter + (TrayBadgeMetrics.RingThickness * 2), ring.Diameter);
        Assert.Equal(9, fill.Diameter);
        Assert.Equal(1, TrayBadgeMetrics.RingThickness);
        Assert.Equal(16, ring.X + ring.Diameter);
        Assert.Equal(16, ring.Y + ring.Diameter);
        Assert.Equal(ring.X + 1, fill.X);
        Assert.Equal(ring.Y + 1, fill.Y);
        Assert.Equal(0xFFFFFF, TrayBadgeMetrics.Rgb(TrayBadgeMetrics.NumberArgb));
        Assert.Empty(TrayBadgeLayout.Layers(TrayGlyph.None));
    }

    [Fact]
    public void The_muted_glyph_is_a_small_grey_dot()
    {
        TrayDisk dot = Assert.Single(TrayBadgeLayout.Layers(TrayGlyph.Muted));

        Assert.Equal(5, dot.Diameter);
        Assert.Equal(0x808080, TrayBadgeMetrics.Rgb(dot.Argb));
        Assert.True(dot.X + dot.Diameter <= TrayBadgeMetrics.IconSize);
        Assert.True(dot.Y + dot.Diameter <= TrayBadgeMetrics.IconSize);
        Assert.True(dot.X > TrayBadgeMetrics.IconSize / 2);
        Assert.True(dot.Y > TrayBadgeMetrics.IconSize / 2);
    }

    [Fact]
    public void The_menu_lists_open_mute_startup_and_quit_without_instagram()
    {
        Assert.Equal(
            [
                TrayMenuEntryKind.Header,
                TrayMenuEntryKind.Separator,
                TrayMenuEntryKind.Open,
                TrayMenuEntryKind.Mute,
                TrayMenuEntryKind.WatchChatList,
                TrayMenuEntryKind.Separator,
                TrayMenuEntryKind.Startup,
                TrayMenuEntryKind.Separator,
                TrayMenuEntryKind.Quit
            ],
            TrayMenuModel.Order);
        Assert.Equal("Messenger+", TrayMenuModel.Text(TrayMenuEntryKind.Header));
        Assert.Equal("Open Messenger", TrayMenuModel.Text(TrayMenuEntryKind.Open));
        Assert.Equal("Mute notifications", TrayMenuModel.Text(TrayMenuEntryKind.Mute));
        Assert.Equal("Watch chat list for reactions (beta)", TrayMenuModel.Text(TrayMenuEntryKind.WatchChatList));
        Assert.Equal(
            "Can miss reactions in the chat you have open and in muted chats.",
            TrayMenuModel.Description(TrayMenuEntryKind.WatchChatList));
        int mute = Array.IndexOf(TrayMenuModel.Order.ToArray(), TrayMenuEntryKind.Mute);
        Assert.Equal(TrayMenuEntryKind.WatchChatList, TrayMenuModel.Order[mute + 1]);
        Assert.Equal("Start with Windows", TrayMenuModel.Text(TrayMenuEntryKind.Startup));
        Assert.Equal("Quit Messenger+", TrayMenuModel.Text(TrayMenuEntryKind.Quit));
        Assert.Equal(["1 hour", "8 hours", "Until I turn it back on"], TrayMenuModel.MuteChoices.Select(TrayMenuModel.MuteText).ToArray());
        Assert.Equal(TimeSpan.FromHours(1), TrayMenuModel.Duration(TrayMute.OneHour));
        Assert.Equal(TimeSpan.FromHours(8), TrayMenuModel.Duration(TrayMute.EightHours));
        Assert.Null(TrayMenuModel.Duration(TrayMute.UntilOn));
        Assert.DoesNotContain(TrayMenuModel.Order, kind => TrayMenuModel.Text(kind).Contains("Instagram", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_header_counts_unread_messages_and_unseen_reactions()
    {
        var summary = new TraySummary();
        summary.Observe(Message("Alex", "hi", "https://www.facebook.com/messages/t/1"), T0);
        summary.NoteActivity(T0.AddMinutes(1));

        Assert.Equal("1 unread · 1 reaction", summary.HeaderLine);
        summary.NoteActivity(T0.AddMinutes(2));
        Assert.Equal("1 unread · 2 reactions", summary.HeaderLine);
    }

    [Theory]
    [InlineData(0, "now")]
    [InlineData(5, "5m")]
    [InlineData(120, "2h")]
    [InlineData(60 * 24 * 3, "3d")]
    public void Relative_time_is_short(int minutes, string expected)
    {
        Assert.Equal(expected, TrayText.Relative(T0, T0.AddMinutes(minutes)));
    }

    [Fact]
    public void A_page_title_falling_to_zero_clears_messages_and_leaves_reactions()
    {
        var summary = new TraySummary();
        summary.NotePageUnread(4);
        summary.Observe(Message("Alex", "hi", "https://www.facebook.com/messages/t/alex"), T0);
        summary.NoteActivity(T0.AddMinutes(1));

        summary.NotePageUnread(4);
        Assert.Equal(2, summary.BadgeCount);

        summary.NotePageUnread(0);

        Assert.Equal(0, summary.UnreadMessages);
        Assert.Equal(1, summary.UnseenReactions);
        Assert.Equal(1, summary.BadgeCount);
        Assert.Equal("1", summary.BadgeLabel);
    }

    [Fact]
    public void The_first_zero_title_does_not_wipe_a_message_captured_before_it()
    {
        var summary = new TraySummary();
        summary.Observe(Message("Alex", "hi", "https://www.facebook.com/messages/t/alex"), T0);

        summary.NotePageUnread(0);

        Assert.Equal(1, summary.BadgeCount);
    }

    [Fact]
    public void The_flyout_prefers_to_sit_above_the_tray()
    {
        TrayPoint above = TrayFlyoutPlacement.Place(1000, 700, TrayMetrics.PanelWidth, 200, 0, 0, 1920, 1080);
        Assert.Equal(1000 - (TrayMetrics.PanelWidth / 2), above.X);
        Assert.Equal(700 - 200 - TrayMetrics.FlyoutGap, above.Y);

        TrayPoint flipped = TrayFlyoutPlacement.Place(100, 20, 340, 200, 0, 0, 800, 600);
        Assert.Equal(20 + TrayMetrics.FlyoutGap, flipped.Y);
        Assert.Equal(340, TrayMetrics.PanelWidth);
        Assert.Equal(420, TrayMetrics.PanelMaxHeight);
    }

    [Fact]
    public void A_reaction_shaped_notification_is_not_counted_as_a_message()
    {
        var summary = new TraySummary();
        var record = new NotificationRecord(
            "Sam",
            "reacted ❤️ to your message",
            "https://cdn.example/a.png",
            "r1",
            "https://www.facebook.com/messages/t/sam",
            "facebook");

        summary.Observe(record, T0);

        TrayRow row = Assert.Single(summary.Latest());
        Assert.Equal(TrayRowKind.Reaction, row.Kind);
        Assert.Equal("❤️ reacted to your message", row.Secondary);
        Assert.Equal("https://cdn.example/a.png", row.AvatarUrl);
        Assert.Equal(0, summary.UnreadMessages);
        Assert.Equal(1, summary.BadgeCount);
        Assert.Equal("S", row.Initials);
    }

    private static TraySummary Messages(int count)
    {
        var summary = new TraySummary();
        for (int i = 0; i < count; i++)
            summary.Observe(Message($"Person {i}", "hi", $"https://www.facebook.com/messages/t/{i}"), T0.AddSeconds(i));
        return summary;
    }

    private static NotificationRecord Message(string name, string body, string url) =>
        new(name, body, "", "tag-" + url, url, "facebook");

    private static NotificationRecord Reaction(string name, string url) =>
        new(name, "reacted to your message", "", "react-" + url, url, "facebook");
}
