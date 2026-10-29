namespace MessengerDesktop;

internal static class NotificationNavigation
{
    public static readonly Uri Inbox = new("https://www.facebook.com/messages/t");

    public static Uri OpenTarget(string? url, string? tag)
    {
        if (TryChat(url, out Uri target)) return target;
        if (TryChat(tag, out target)) return target;
        return Inbox;
    }

    private static bool TryChat(string? value, out Uri target)
    {
        target = Inbox;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)) return false;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!NotificationOrigins.IsAllowed(uri)) return false;
        string path = uri.AbsolutePath;
        bool chat = path.Contains("/messages", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/t", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/t/", StringComparison.OrdinalIgnoreCase);
        if (!chat) return false;
        target = uri;
        return true;
    }
}
