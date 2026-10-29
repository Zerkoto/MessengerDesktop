namespace MessengerDesktop;

internal sealed record NotificationRecord(
    string Title,
    string Body,
    string Icon,
    string Tag,
    string Url,
    string Service);

internal static class NotificationNormalizer
{
    public static NotificationRecord Normalize(
        string? title,
        string? body,
        string? icon,
        string? tag,
        string? url,
        string? sourceOrigin)
    {
        return new NotificationRecord(
            Clean(title, "Messenger"),
            Clean(body, ""),
            Clean(icon, ""),
            Clean(tag, ""),
            Clean(url, ""),
            ServiceFrom(sourceOrigin));
    }

    private static string Clean(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return value.Trim();
    }

    private static string ServiceFrom(string? sourceOrigin)
    {
        if (!Uri.TryCreate(sourceOrigin, UriKind.Absolute, out Uri? origin) || !NotificationOrigins.IsAllowed(origin))
            return "";
        string host = origin.IdnHost;
        if (host.Equals("messenger.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".messenger.com", StringComparison.OrdinalIgnoreCase))
            return "messenger";
        return "facebook";
    }
}
