using Xunit;

namespace MessengerDesktop.Tests;

public class NotificationDeduperTests
{
    [Fact]
    public void The_same_tag_is_shown_once()
    {
        var dedupe = new NotificationDeduper();

        Assert.True(dedupe.ShouldShow("thread-1"));
        Assert.False(dedupe.ShouldShow("thread-1"));
    }

    [Fact]
    public void Different_tags_both_show()
    {
        var dedupe = new NotificationDeduper();

        Assert.True(dedupe.ShouldShow("a"));
        Assert.True(dedupe.ShouldShow("b"));
    }

    [Fact]
    public void Tag_matching_is_case_sensitive()
    {
        var dedupe = new NotificationDeduper();

        Assert.True(dedupe.ShouldShow("Tag"));
        Assert.True(dedupe.ShouldShow("tag"));
    }

    [Fact]
    public void A_missing_tag_does_not_collapse_later_notifications()
    {
        var dedupe = new NotificationDeduper();

        Assert.True(dedupe.ShouldShow(null));
        Assert.True(dedupe.ShouldShow(""));
        Assert.True(dedupe.ShouldShow(null));
        Assert.True(dedupe.ShouldShow("  "));
        Assert.True(dedupe.ShouldShow("  "));
    }
}
