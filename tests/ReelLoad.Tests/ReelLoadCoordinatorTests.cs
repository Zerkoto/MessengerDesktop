using Xunit;

namespace MessengerDesktop.Tests;

public class ReelLoadCoordinatorTests
{
    private static readonly Uri Reel = new("https://www.facebook.com/reel/1");

    [Fact]
    public void Begin_shows_the_loader()
    {
        var load = new ReelLoadCoordinator();

        load.Begin(Reel);

        Assert.Equal(ReelLoadVisual.Visible, load.Visual);
        Assert.True(load.IsCurrent(load.Generation));
    }

    [Fact]
    public void About_blank_does_not_count_as_the_reel_navigation()
    {
        var load = new ReelLoadCoordinator();
        load.Begin(Reel);

        bool accepted = load.NoteNavigationStarting("about:blank", 4);

        Assert.False(accepted);
        Assert.Null(load.ArmContentReady(4));
    }

    [Fact]
    public void Content_ready_is_ignored_until_a_real_navigation_starts()
    {
        var load = new ReelLoadCoordinator();
        load.Begin(Reel);

        Assert.Null(load.ArmContentReady(1));
    }

    [Fact]
    public void Matching_navigation_can_arm_fade_once()
    {
        var load = new ReelLoadCoordinator();
        load.Begin(Reel);
        Assert.True(load.NoteNavigationStarting(Reel.AbsoluteUri, 7));

        int? epoch = load.ArmContentReady(7);
        Assert.NotNull(epoch);
        Assert.Null(load.ArmContentReady(7));
        Assert.True(load.TryBeginFade(epoch.Value));
        Assert.True(load.IsFading(epoch.Value));
        Assert.False(load.TryBeginFade(epoch.Value));
    }

    [Fact]
    public void Wrong_navigation_id_does_not_dismiss_the_loader()
    {
        var load = new ReelLoadCoordinator();
        load.Begin(Reel);
        load.NoteNavigationStarting(Reel.AbsoluteUri, 7);

        Assert.Null(load.ArmContentReady(8));
        Assert.Equal(ReelLoadVisual.Visible, load.Visual);
    }

    [Fact]
    public void Later_navigation_replaces_the_one_that_may_dismiss_the_loader()
    {
        var load = new ReelLoadCoordinator();
        load.Begin(Reel);
        load.NoteNavigationStarting("https://www.facebook.com/share/r/abc", 1);
        int epochAfterFirst = load.Epoch;
        Assert.True(load.NoteNavigationStarting("https://www.facebook.com/reel/2", 2));

        Assert.Null(load.ArmContentReady(1));
        Assert.NotEqual(epochAfterFirst, load.Epoch);
        Assert.NotNull(load.ArmContentReady(2));
    }

    [Fact]
    public void Same_navigation_starting_again_does_not_cancel_an_in_flight_ready()
    {
        var load = new ReelLoadCoordinator();
        load.Begin(Reel);
        load.NoteNavigationStarting(Reel.AbsoluteUri, 5);
        int? epoch = load.ArmContentReady(5);

        bool restarted = load.NoteNavigationStarting(Reel.AbsoluteUri, 5);

        Assert.False(restarted);
        Assert.NotNull(epoch);
        Assert.True(load.TryBeginFade(epoch.Value));
    }

    [Fact]
    public void Close_while_loading_hides_immediately_and_ignores_a_late_completion()
    {
        var load = new ReelLoadCoordinator();
        load.Begin(Reel);
        load.NoteNavigationStarting(Reel.AbsoluteUri, 3);
        int generation = load.Generation;

        load.End();

        Assert.Equal(ReelLoadVisual.Hidden, load.Visual);
        Assert.False(load.IsCurrent(generation));
        Assert.Null(load.ArmContentReady(3));
        Assert.False(load.TryBeginFade(generation));
    }

    [Fact]
    public void Close_during_fade_drops_the_fade()
    {
        var load = new ReelLoadCoordinator();
        load.Begin(Reel);
        load.NoteNavigationStarting(Reel.AbsoluteUri, 3);
        int epoch = load.ArmContentReady(3)!.Value;
        Assert.True(load.TryBeginFade(epoch));

        load.End();

        Assert.False(load.IsFading(epoch));
        load.FinishFade(epoch);
        Assert.Equal(ReelLoadVisual.Hidden, load.Visual);
    }

    [Fact]
    public void Reopen_shows_the_loader_again_and_ignores_the_previous_navigation()
    {
        var load = new ReelLoadCoordinator();
        load.Begin(Reel);
        load.NoteNavigationStarting(Reel.AbsoluteUri, 3);
        int firstEpoch = load.ArmContentReady(3)!.Value;
        load.End();

        load.Begin(new Uri("https://www.facebook.com/reel/9"));

        Assert.Equal(ReelLoadVisual.Visible, load.Visual);
        Assert.Null(load.ArmContentReady(3));
        Assert.False(load.TryBeginFade(firstEpoch));
        Assert.True(load.NoteNavigationStarting("https://www.facebook.com/reel/9", 9));
        Assert.NotNull(load.ArmContentReady(9));
    }

    [Fact]
    public void Navigation_that_starts_during_fade_brings_the_loader_back()
    {
        var load = new ReelLoadCoordinator();
        load.Begin(Reel);
        load.NoteNavigationStarting(Reel.AbsoluteUri, 3);
        int epoch = load.ArmContentReady(3)!.Value;
        Assert.True(load.TryBeginFade(epoch));

        Assert.True(load.NoteNavigationStarting("https://www.facebook.com/reel/4", 4));

        Assert.Equal(ReelLoadVisual.Visible, load.Visual);
        Assert.False(load.IsFading(epoch));
        load.FinishFade(epoch);
        Assert.Equal(ReelLoadVisual.Visible, load.Visual);
        Assert.False(load.TryBeginFade(epoch));
        Assert.NotNull(load.ArmContentReady(4));
    }

    [Fact]
    public void Finish_fade_hides_only_the_fade_that_is_still_current()
    {
        var load = new ReelLoadCoordinator();
        load.Begin(Reel);
        load.NoteNavigationStarting(Reel.AbsoluteUri, 3);
        int epoch = load.ArmContentReady(3)!.Value;
        Assert.True(load.TryBeginFade(epoch));

        load.FinishFade(epoch);

        Assert.Equal(ReelLoadVisual.Hidden, load.Visual);
        load.FinishFade(epoch);
        Assert.Equal(ReelLoadVisual.Hidden, load.Visual);
    }

    [Fact]
    public void Rapid_open_and_close_leaves_only_the_newest_load_active()
    {
        var load = new ReelLoadCoordinator();
        load.Begin(Reel);
        int first = load.Generation;
        load.End();
        load.Begin(Reel);
        int second = load.Generation;
        load.End();
        load.Begin(Reel);
        int third = load.Generation;

        Assert.True(third > second && second > first);
        Assert.False(load.IsCurrent(first));
        Assert.False(load.IsCurrent(second));
        Assert.True(load.IsCurrent(third));
        Assert.Equal(ReelLoadVisual.Visible, load.Visual);
    }

    [Fact]
    public void Hidden_loader_ignores_navigations()
    {
        var load = new ReelLoadCoordinator();

        Assert.False(load.NoteNavigationStarting(Reel.AbsoluteUri, 1));
        Assert.Null(load.ArmContentReady(1));
        Assert.Equal(ReelLoadVisual.Hidden, load.Visual);
    }
}
