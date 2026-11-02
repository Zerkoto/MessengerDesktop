using System.Text.Json;

namespace MessengerDesktop;

// The page shim posts these fields. icon and badge are the same URLs as
// CoreWebView2Notification.IconUri and BadgeUri.
internal readonly record struct NotificationPageFields(
    string Title,
    string Body,
    string IconUri,
    string BadgeUri,
    string Tag,
    string Url);

internal static class NotificationPageMessage
{
    public const string Prefix = "messenger-notification:";

    public static bool TryParse(string? message, out NotificationPageFields fields)
    {
        fields = default;
        if (string.IsNullOrEmpty(message) || !message.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(message[Prefix.Length..]);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            fields = new NotificationPageFields(
                Text(root, "title"),
                Text(root, "body"),
                Text(root, "icon"),
                Text(root, "badge"),
                Text(root, "tag"),
                Text(root, "url"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return "";
        return value.GetString() ?? "";
    }
}
