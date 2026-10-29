using Xunit;

namespace MessengerDesktop.Tests;

public class LaunchArgsTests
{
    [Fact]
    public void No_arguments_is_not_minimized()
    {
        Assert.False(LaunchArgs.IsMinimized(null));
        Assert.False(LaunchArgs.IsMinimized(Array.Empty<string>()));
        Assert.False(LaunchArgs.IsMinimized(new[] { "--other" }));
    }

    [Fact]
    public void Minimized_flag_is_recognized_in_any_position_and_case()
    {
        Assert.True(LaunchArgs.IsMinimized(new[] { "--minimized" }));
        Assert.True(LaunchArgs.IsMinimized(new[] { "--Minimized" }));
        Assert.True(LaunchArgs.IsMinimized(new[] { "Messenger.exe", "--minimized" }));
    }

    [Fact]
    public void Similar_flags_are_not_the_minimized_flag()
    {
        Assert.False(LaunchArgs.IsMinimized(new[] { "--minimize" }));
        Assert.False(LaunchArgs.IsMinimized(new[] { "--minimized=true" }));
        Assert.False(LaunchArgs.IsMinimized(new[] { "/minimized" }));
    }
}
