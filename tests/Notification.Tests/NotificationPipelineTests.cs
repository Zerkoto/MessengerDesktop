using Xunit;

namespace MessengerDesktop.Tests;

public class NotificationPipelineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_same_message_tag_is_toasted_once_and_suppresses_activity()
    {
        var pipeline = new NotificationPipeline(TimeSpan.FromSeconds(2));
        var message = new NotificationRecord("Alex", "hi", "", "m1", "https://www.facebook.com/messages/t/1", "facebook");

        Assert.True(pipeline.ShouldToastMessage(message, T0));
        Assert.False(pipeline.ShouldToastMessage(message, T0.AddSeconds(1)));

        Assert.False(pipeline.ShouldToastActivity(1, T0));
        Assert.False(pipeline.ShouldToastActivity(2, T0.AddSeconds(1)));
        Assert.False(pipeline.ShouldToastActivity(2, T0.AddSeconds(4)));
    }

    [Fact]
    public void An_unread_bump_without_a_message_becomes_activity_after_the_window()
    {
        var pipeline = new NotificationPipeline(TimeSpan.FromSeconds(2));

        Assert.False(pipeline.ShouldToastActivity(0, T0));
        Assert.False(pipeline.ShouldToastActivity(1, T0.AddSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(2), pipeline.ActivityDueIn(T0.AddSeconds(1)));
        Assert.True(pipeline.ShouldToastActivity(1, T0.AddSeconds(3)));
    }
}
