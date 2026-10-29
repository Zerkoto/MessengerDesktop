namespace MessengerDesktop;

// Notifications are granted only for Facebook Messenger origins. Other sites
// that load inside the WebView stay denied.
internal static class NotificationOrigins
{
    public static readonly string[] GrantedOrigins =
    [
        "https://www.facebook.com",
        "https://facebook.com",
        "https://web.facebook.com",
        "https://m.facebook.com",
        "https://www.messenger.com",
        "https://messenger.com",
    ];

    public static bool IsAllowed(Uri uri)
    {
        if (!uri.IsAbsoluteUri) return false;
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        string host = uri.IdnHost;
        return IsHost(host, "facebook.com") || IsHost(host, "messenger.com");
    }

    private static bool IsHost(string host, string root) =>
        host.Equals(root, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + root, StringComparison.OrdinalIgnoreCase);
}
