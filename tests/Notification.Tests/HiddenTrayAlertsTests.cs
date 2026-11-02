using System.Text.Json;
using Xunit;

namespace MessengerDesktop.Tests;

public class HiddenTrayAlertsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Backup_interval_is_three_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), HiddenTrayAlerts.BackupInterval);
    }

    [Fact]
    public void A_hidden_window_and_a_later_tick_raises_one_toast_and_one_sound()
    {
        var alerts = new HiddenTrayAlerts();
        ChatThread existing = Row("Alex", "Unread message: already here", "/messages/t/alex");

        Assert.Empty(alerts.Observe([existing], foreground: false, muted: false, T0, HiddenTraySource.BackupTimer));

        IReadOnlyList<HiddenTrayNotice> again = alerts.Observe(
            [existing],
            foreground: false,
            muted: false,
            T0.AddSeconds(3),
            HiddenTraySource.BackupTimer);
        Assert.Empty(again);

        IReadOnlyList<HiddenTrayNotice> notices = alerts.Observe(
            [Row("Alex", "Unread message: on my way", "/messages/t/alex")],
            foreground: false,
            muted: false,
            T0.AddSeconds(6),
            HiddenTraySource.BackupTimer);

        HiddenTrayNotice notice = Assert.Single(notices);
        Assert.True(notice.Sound);
        Assert.Equal("Alex", notice.Toast.Title);
        Assert.Equal("on my way", notice.Toast.Body);
        Assert.Contains("source=backup-timer", notice.Log, StringComparison.Ordinal);
        Assert.Contains("thread=", notice.Log, StringComparison.Ordinal);
        Assert.Contains("messages/t/alex", notice.Log, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(notice.Log));
        Assert.NotEqual(default, DateTimeOffset.Parse(notice.Log.Split(' ')[0], null));
    }

    [Fact]
    public void Mute_raises_no_toast_and_does_not_replay_that_row_later()
    {
        var alerts = new HiddenTrayAlerts();
        Assert.Empty(alerts.Observe(
            [Row("Alex", "Unread message: hi", "/messages/t/alex")],
            foreground: false,
            muted: true,
            T0,
            HiddenTraySource.BackupTimer));

        Assert.Empty(alerts.Observe(
            [Row("Alex", "Unread message: later", "/messages/t/alex")],
            foreground: false,
            muted: true,
            T0.AddSeconds(3),
            HiddenTraySource.BackupTimer));

        Assert.Empty(alerts.Observe(
            [Row("Alex", "Unread message: later", "/messages/t/alex")],
            foreground: false,
            muted: false,
            T0.AddSeconds(6),
            HiddenTraySource.BackupTimer));
    }

    [Fact]
    public void A_page_duplicate_inside_the_window_is_suppressed_and_a_different_kind_is_not()
    {
        var alerts = new HiddenTrayAlerts();
        alerts.Observe([], foreground: false, muted: false, T0, HiddenTraySource.BackupTimer);
        HiddenTrayNotice notice = Assert.Single(alerts.Observe(
            [Row("Alex", "Alex reacted ❤️ to your message: See you there", "/messages/t/alex")],
            foreground: false,
            muted: false,
            T0.AddSeconds(3),
            HiddenTraySource.BackupTimer));
        Assert.Equal(ToastTags.ReactionGroup, notice.Toast.ToastGroup);
        Assert.True(notice.Sound);

        var reaction = new NotificationRecord(
            "Alex",
            "Alex reacted ❤️ to your message: See you there",
            "",
            "r1",
            "https://www.facebook.com/messages/t/alex",
            "facebook");
        var message = new NotificationRecord(
            "Alex",
            "on my way",
            "",
            "m1",
            "https://www.facebook.com/messages/t/alex",
            "facebook");

        Assert.True(alerts.SuppressPage(reaction, T0.AddSeconds(4)));
        Assert.False(alerts.SuppressPage(message, T0.AddSeconds(4)));
        Assert.False(alerts.SuppressPage(reaction, T0.AddSeconds(20)));
    }

    [Fact]
    public void A_reaction_then_a_message_and_a_second_reaction_all_stay()
    {
        var alerts = new HiddenTrayAlerts();
        alerts.Observe([], foreground: false, muted: false, T0, HiddenTraySource.PageEvent);

        HiddenTrayNotice heart = Assert.Single(alerts.Observe(
            [Row("Alex", "Alex reacted ❤️ to your message: See you there", "/messages/t/alex")],
            foreground: false,
            muted: false,
            T0.AddSeconds(3),
            HiddenTraySource.PageEvent));
        HiddenTrayNotice message = Assert.Single(alerts.Observe(
            [Row("Alex", "Unread message: on my way", "/messages/t/alex")],
            foreground: false,
            muted: false,
            T0.AddSeconds(4),
            HiddenTraySource.BackupTimer));
        HiddenTrayNotice thumb = Assert.Single(alerts.Observe(
            [Row("Alex", "Alex reacted 👍 to your message: On my way", "/messages/t/alex")],
            foreground: false,
            muted: false,
            T0.AddSeconds(5),
            HiddenTraySource.BackupTimer));

        Assert.Equal(ToastTags.ReactionGroup, heart.Toast.ToastGroup);
        Assert.Equal(ToastTags.MessageGroup, message.Toast.ToastGroup);
        Assert.Equal(ToastTags.ReactionGroup, thumb.Toast.ToastGroup);
        Assert.NotEqual(heart.Toast.ToastTag, message.Toast.ToastTag);
        Assert.NotEqual(heart.Toast.ToastTag, thumb.Toast.ToastTag);
        Assert.NotEqual(message.Toast.ToastTag, thumb.Toast.ToastTag);
        Assert.Contains("source=page-event", heart.Log, StringComparison.Ordinal);
        Assert.Contains("source=backup-timer", message.Log, StringComparison.Ordinal);
        Assert.True(heart.Sound);
        Assert.True(message.Sound);
        Assert.True(thumb.Sound);
    }

    [Fact]
    public void The_foreground_updates_the_baseline_and_does_not_toast()
    {
        var alerts = new HiddenTrayAlerts();
        Assert.Empty(alerts.Observe(
            [Row("Alex", "Unread message: hi", "/messages/t/alex")],
            foreground: true,
            muted: false,
            T0,
            HiddenTraySource.BackupTimer));

        Assert.Empty(alerts.Observe(
            [Row("Alex", "Unread message: hi", "/messages/t/alex")],
            foreground: false,
            muted: false,
            T0.AddSeconds(3),
            HiddenTraySource.BackupTimer));
    }

    [Fact]
    public void A_script_result_unwraps_the_json_string_WebView_returns()
    {
        string inner = "{\"ok\":true,\"rows\":[{\"name\":\"Alex\",\"preview\":\"hi\",\"avatar\":\"\",\"time\":\"now\",\"href\":\"/messages/t/alex\"}]}";
        string executed = JsonSerializer.Serialize(inner);

        Assert.True(HiddenTrayAlerts.TryReadScriptResult(executed, out bool readable, out IReadOnlyList<ChatThread> threads));
        Assert.True(readable);
        Assert.Equal("Alex", Assert.Single(threads).Name);
        Assert.Equal("hi", threads[0].Preview);
        Assert.False(HiddenTrayAlerts.TryReadScriptResult("null", out _, out _));
    }

    private static ChatThread Row(string name, string preview, string href) =>
        new(name, preview, "", "now", href);
}
