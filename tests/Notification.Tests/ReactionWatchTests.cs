using Xunit;

namespace MessengerDesktop.Tests;

public class ReactionWatchTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_app_waits_two_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), ReactionWatch.DefaultWindow);
    }

    [Fact]
    public void The_first_title_sets_a_baseline()
    {
        var watch = new ReactionWatch(Window);

        Assert.False(watch.ObserveUnread(5, T0));
        Assert.Null(watch.DueIn(T0));
    }

    [Fact]
    public void An_increase_without_a_message_toasts_after_the_debounce()
    {
        var watch = new ReactionWatch(Window);
        watch.ObserveUnread(1, T0);

        Assert.False(watch.ObserveUnread(2, T0));
        Assert.Equal(Window, watch.DueIn(T0));
        Assert.False(watch.ObserveUnread(2, T0.AddSeconds(1)));
        Assert.True(watch.ObserveUnread(2, T0.AddSeconds(2)));
        Assert.Null(watch.DueIn(T0.AddSeconds(2)));
    }

    [Fact]
    public void Another_increase_restarts_the_debounce()
    {
        var watch = new ReactionWatch(Window);
        watch.ObserveUnread(0, T0);

        Assert.False(watch.ObserveUnread(1, T0));
        Assert.False(watch.ObserveUnread(2, T0.AddSeconds(1)));
        Assert.False(watch.ObserveUnread(2, T0.AddSeconds(2)));
        Assert.True(watch.ObserveUnread(2, T0.AddSeconds(3)));
    }

    [Fact]
    public void A_message_inside_the_window_suppresses_the_activity_toast()
    {
        var watch = new ReactionWatch(Window);
        watch.ObserveUnread(1, T0);
        watch.ObserveUnread(2, T0.AddSeconds(1));
        watch.NoteMessage(T0.AddSeconds(1.5));

        Assert.False(watch.ObserveUnread(2, T0.AddSeconds(5)));
        Assert.Null(watch.DueIn(T0.AddSeconds(5)));
    }

    [Fact]
    public void A_message_just_before_the_title_bump_suppresses_the_activity_toast()
    {
        var watch = new ReactionWatch(Window);
        watch.ObserveUnread(1, T0);
        watch.NoteMessage(T0.AddSeconds(1));

        Assert.False(watch.ObserveUnread(2, T0.AddSeconds(1.2)));
        Assert.False(watch.ObserveUnread(2, T0.AddSeconds(10)));
    }

    [Fact]
    public void An_old_message_does_not_suppress_a_later_bump()
    {
        var watch = new ReactionWatch(Window);
        watch.ObserveUnread(1, T0);
        watch.NoteMessage(T0);

        Assert.False(watch.ObserveUnread(2, T0.AddSeconds(5)));
        Assert.True(watch.ObserveUnread(2, T0.AddSeconds(7)));
    }

    [Fact]
    public void A_lower_or_equal_count_does_not_toast()
    {
        var watch = new ReactionWatch(Window);
        watch.ObserveUnread(4, T0);

        Assert.False(watch.ObserveUnread(4, T0.AddSeconds(5)));
        Assert.False(watch.ObserveUnread(1, T0.AddSeconds(6)));
        Assert.Null(watch.DueIn(T0.AddSeconds(6)));
    }

    [Fact]
    public void A_later_bump_can_toast_again_after_its_own_window()
    {
        var watch = new ReactionWatch(Window);
        watch.ObserveUnread(0, T0);
        watch.ObserveUnread(1, T0);
        Assert.True(watch.ObserveUnread(1, T0.AddSeconds(2)));

        Assert.False(watch.ObserveUnread(2, T0.AddSeconds(3)));
        Assert.True(watch.ObserveUnread(2, T0.AddSeconds(5)));
    }
}
