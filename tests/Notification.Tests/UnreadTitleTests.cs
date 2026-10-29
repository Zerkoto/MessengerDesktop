using Xunit;

namespace MessengerDesktop.Tests;

public class UnreadTitleTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("Messenger", 0)]
    [InlineData("(3) Messenger", 3)]
    [InlineData("(12) Jane Doe", 12)]
    [InlineData("  (4) Messenger", 4)]
    [InlineData("(99+) Messenger", 99)]
    [InlineData("Chat", 0)]
    [InlineData("Meeting (2)", 2)]
    [InlineData("(0) Messenger", 0)]
    [InlineData("(3) (8) Messenger", 3)]
    [InlineData("(-1) Messenger", 0)]
    [InlineData("(099) Messenger", 99)]
    public void Parses_a_parenthesized_count(string? title, int expected)
    {
        Assert.Equal(expected, UnreadTitle.Parse(title));
    }

    [Fact]
    public void Tooltip_names_the_unread_count()
    {
        Assert.Equal("Messenger", UnreadTitle.Tooltip(0));
        Assert.Equal("Messenger", UnreadTitle.Tooltip(-2));
        Assert.Equal("Messenger (1 unread)", UnreadTitle.Tooltip(1));
        Assert.Equal("Messenger (4 unread)", UnreadTitle.Tooltip(4));
    }

    [Fact]
    public void Tooltip_stays_within_the_notify_icon_limit()
    {
        string tooltip = UnreadTitle.Tooltip(int.MaxValue);

        Assert.True(tooltip.Length <= 127);
        Assert.StartsWith("Messenger (", tooltip);
    }
}
