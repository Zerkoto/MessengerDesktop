using Xunit;

namespace MessengerDesktop.Tests;

public class NotificationArgumentsTests
{
    [Fact]
    public void Url_and_tag_round_trip_through_toast_arguments()
    {
        string encoded = NotificationArguments.Format(
            "https://www.facebook.com/messages/t/1?x=1",
            "tag;with=pieces");

        (string url, string tag) = NotificationArguments.Parse(encoded);

        Assert.Equal("https://www.facebook.com/messages/t/1?x=1", url);
        Assert.Equal("tag;with=pieces", tag);
        Assert.DoesNotContain("&", encoded);
    }

    [Fact]
    public void A_missing_argument_opens_nothing()
    {
        (string url, string tag) = NotificationArguments.Parse(null);

        Assert.Equal("", url);
        Assert.Equal("", tag);
    }

    [Fact]
    public void Pairs_can_arrive_in_either_order()
    {
        (string url, string tag) = NotificationArguments.Parse("tag=m1;url=https://www.messenger.com/t/9");

        Assert.Equal("https://www.messenger.com/t/9", url);
        Assert.Equal("m1", tag);
    }
}
