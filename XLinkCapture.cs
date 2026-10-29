using System.Text.Json;

namespace MessengerDesktop;

// Status links clicked in Messenger, including l.facebook.com shims.
internal static class XLinkCapture
{
    private static readonly HashSet<string> ReservedHandles = new(StringComparer.OrdinalIgnoreCase)
    {
        "home", "explore", "search", "settings", "messages", "notifications",
        "intent", "compose", "login", "signup", "share", "hashtag", "download",
        "account", "i", "tos", "privacy", "about", "jobs"
    };

    public static bool TryCapture(Uri? clicked, out Uri? status)
    {
        status = null;
        if (!TryResolve(clicked, out Uri? target) || target is null) return false;
        foreach (LinkRule rule in ServiceCatalog.X.LinkRules)
        {
            if (!rule.Matches(target)) continue;
            status = target;
            return true;
        }
        return false;
    }

    public static bool TryReadMessage(string? json, out Uri? clicked)
    {
        clicked = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String &&
                type.GetString() == "messenger-open-status" &&
                root.TryGetProperty("url", out JsonElement url) && url.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(url.GetString(), UriKind.Absolute, out clicked);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool IsStatusPath(string path)
    {
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 &&
            Segment(parts[0], "i") &&
            Segment(parts[1], "status") &&
            IsId(parts[2]))
            return true;
        if (parts.Length >= 4 &&
            Segment(parts[0], "i") &&
            Segment(parts[1], "web") &&
            Segment(parts[2], "status") &&
            IsId(parts[3]))
            return true;
        return parts.Length >= 3 &&
            IsHandle(parts[0]) &&
            Segment(parts[1], "status") &&
            IsId(parts[2]);
    }

    private static bool TryResolve(Uri? clicked, out Uri? target)
    {
        target = null;
        if (!IsHttpsDefaultPort(clicked)) return false;
        if (clicked!.Host.Equals("l.facebook.com", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetQuery(clicked, "u", out string raw)) return false;
            return TryAbsoluteHttps(raw, out target);
        }
        target = clicked;
        return true;
    }

    private static bool TryGetQuery(Uri uri, string name, out string value)
    {
        value = "";
        string query = uri.Query;
        if (query.Length <= 1 || query[0] != '?') return false;
        foreach (string pair in query[1..].Split('&'))
        {
            if (pair.Length == 0) continue;
            int separator = pair.IndexOf('=');
            string key = separator >= 0 ? pair[..separator] : pair;
            string decodedKey;
            try { decodedKey = Uri.UnescapeDataString(key); }
            catch (UriFormatException) { return false; }
            if (!decodedKey.Equals(name, StringComparison.Ordinal)) continue;
            value = separator >= 0 ? pair[(separator + 1)..] : "";
            return true;
        }
        return false;
    }

    private static bool TryAbsoluteHttps(string raw, out Uri? uri)
    {
        string current = raw.Trim();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (Uri.TryCreate(current, UriKind.Absolute, out Uri? parsed) && IsHttpsDefaultPort(parsed))
            {
                uri = parsed;
                return true;
            }
            string decoded;
            try { decoded = Uri.UnescapeDataString(current); }
            catch (UriFormatException)
            {
                uri = null;
                return false;
            }
            if (decoded == current) break;
            current = decoded;
        }
        uri = null;
        return false;
    }

    private static bool IsHttpsDefaultPort(Uri? uri) =>
        uri is not null &&
        uri.IsAbsoluteUri &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort;

    private static bool Segment(string value, string expected) =>
        value.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsId(string value)
    {
        if (value.Length == 0) return false;
        foreach (char c in value)
        {
            if (c is < '0' or > '9') return false;
        }
        return true;
    }

    private static bool IsHandle(string value)
    {
        if (value.Length is < 1 or > 15) return false;
        if (ReservedHandles.Contains(value)) return false;
        foreach (char c in value)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
                return false;
        }
        return true;
    }
}
