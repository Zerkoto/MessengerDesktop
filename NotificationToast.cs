namespace MessengerDesktop;

// What a Windows toast shows for one message. Title is the sender name.
// Body is the start of the message. ImagePath is a local picture file.
internal sealed record NotificationToastContent(string Title, string Body, string ImagePath, string Url, string Tag)
{
    public static NotificationToastContent Activity { get; } = new("New activity", "", "", "", "");
}

internal static class NotificationToast
{
    public const int MaxBodyLength = 120;

    public static string? PreferredImageUri(string? iconUri, string? badgeUri)
    {
        if (IsFetchable(iconUri)) return iconUri!.Trim();
        if (IsFetchable(badgeUri)) return badgeUri!.Trim();
        return null;
    }

    public static bool IsFetchable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    }

    public static NotificationToastContent Map(
        string? title,
        string? body,
        string? downloadedImagePath,
        string? appIconPath,
        string? url,
        string? tag)
    {
        string image = ExistingFile(downloadedImagePath);
        if (image.Length == 0) image = ExistingFile(appIconPath);
        return new NotificationToastContent(
            string.IsNullOrWhiteSpace(title) ? "Messenger" : title.Trim(),
            TrimBody(body),
            image,
            string.IsNullOrWhiteSpace(url) ? "" : url.Trim(),
            string.IsNullOrWhiteSpace(tag) ? "" : tag.Trim());
    }

    public static string TrimBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        string trimmed = body.Trim().Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
        if (trimmed.Length <= MaxBodyLength) return trimmed;
        return trimmed[..MaxBodyLength].TrimEnd();
    }

    public static string Describe(NotificationToastContent toast)
    {
        return "toast title='" + OneLine(toast.Title) + "' body='" + OneLine(toast.Body) + "' image='" + toast.ImagePath + "'";
    }

    private static string ExistingFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            return File.Exists(path) ? path : "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static string OneLine(string value) =>
        value.Replace("'", "\\'", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
