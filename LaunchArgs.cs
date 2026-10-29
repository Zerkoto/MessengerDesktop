namespace MessengerDesktop;

internal static class LaunchArgs
{
    public static bool IsMinimized(IReadOnlyList<string>? args)
    {
        if (args is null) return false;
        foreach (string arg in args)
        {
            if (string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
