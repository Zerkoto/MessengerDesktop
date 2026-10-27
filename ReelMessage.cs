using System.Text.Json;

namespace MessengerDesktop;

internal static class ReelMessage
{
    public static bool TryReadTarget(string json, out Uri? target)
    {
        target = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String &&
                type.GetString() == "messenger-open-reel" &&
                root.TryGetProperty("url", out JsonElement url) && url.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(url.GetString(), UriKind.Absolute, out target) &&
                (ReelReturnTracker.IsShare(target) || ReelReturnTracker.IsReel(target));
        }
        catch (JsonException) { return false; }
    }
}
