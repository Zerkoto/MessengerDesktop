using System.Text.Json;

namespace MessengerDesktop;

// Choose only a known chat from the current reel's nearby back history.
internal static class HistoryEntrySelector
{
    private const int MaxPreviousEntries = 32;

    public static int? Select(string json, Uri reel, Uri chat)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("currentIndex", out JsonElement indexElement) ||
                !indexElement.TryGetInt32(out int currentIndex) ||
                !root.TryGetProperty("entries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array ||
                currentIndex < 0 || currentIndex >= entries.GetArrayLength() ||
                !TryGetUri(entries[currentIndex], out Uri? current) || current!.AbsoluteUri != reel.AbsoluteUri ||
                !ReelReturnTracker.IsReel(current)) return null;

            int first = Math.Max(0, currentIndex - MaxPreviousEntries);
            for (int i = currentIndex - 1; i >= first; i--)
            {
                JsonElement entry = entries[i];
                if (!TryGetUri(entry, out Uri? uri)) return null;
                if (uri!.AbsoluteUri == chat.AbsoluteUri)
                {
                    return entry.TryGetProperty("id", out JsonElement id) && id.TryGetInt32(out int entryId)
                        ? entryId : null;
                }
                if (!ReelReturnTracker.IsReel(uri) && !ReelReturnTracker.IsShare(uri)) return null;
            }
        }
        catch (JsonException)
        {
            // Invalid or incomplete protocol output is not reliable history.
        }
        catch (InvalidOperationException)
        {
            // A protocol field has the wrong type.
        }
        return null;
    }

    private static bool TryGetUri(JsonElement entry, out Uri? uri)
    {
        uri = null;
        return entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("url", out JsonElement url) &&
            url.ValueKind == JsonValueKind.String && Uri.TryCreate(url.GetString(), UriKind.Absolute, out uri);
    }
}
