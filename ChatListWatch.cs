using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace MessengerDesktop;

internal sealed record ChatListActivity(string Name, string Href, string Preview);

internal enum ChatListEffect
{
    Ignored,
    Count,
    Merge
}

// Parses a read-only chat-list post. The page may only describe a thread; the
// host never treats this payload as a navigation or a read receipt.
internal static class ChatListMessage
{
    public const string TypeName = "messenger-chat-list-activity";
    private const int MaxName = 120;
    private const int MaxPreview = 240;
    private const int MaxHref = 500;

    public static bool TryParse(string? json, [NotNullWhen(true)] out ChatListActivity? activity)
    {
        activity = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String) return false;
            if (type.GetString() != TypeName) return false;
            if (!TryText(root, "name", MaxName, allowEmpty: false, out string name)) return false;
            if (!TryText(root, "href", MaxHref, allowEmpty: false, out string href)) return false;
            if (!TryText(root, "preview", MaxPreview, allowEmpty: true, out string preview)) return false;
            if (!TryNormalizeThread(href, out string path)) return false;
            activity = new ChatListActivity(name, path, preview);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryText(JsonElement root, string name, int max, bool allowEmpty, out string text)
    {
        text = "";
        if (!root.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.String) return false;
        string? value = element.GetString();
        if (value is null) return false;
        text = value.Trim();
        if (text.Length > max) return false;
        return allowEmpty || text.Length > 0;
    }

    private static bool TryNormalizeThread(string href, out string path)
    {
        path = "";
        if (href.Contains('\\') || href.Contains("..", StringComparison.Ordinal) || href.Contains(' ')) return false;
        if (href.StartsWith("/", StringComparison.Ordinal))
        {
            if (href.StartsWith("//", StringComparison.Ordinal)) return false;
            int cut = href.IndexOfAny(['?', '#']);
            string bare = cut >= 0 ? href[..cut] : href;
            if (!bare.Contains("/t/", StringComparison.Ordinal)) return false;
            path = bare;
            return true;
        }

        if (!Uri.TryCreate(href, UriKind.Absolute, out Uri? uri)) return false;
        if (!uri.IsDefaultPort || !NotificationOrigins.IsAllowed(uri)) return false;
        if (!uri.AbsolutePath.Contains("/t/", StringComparison.Ordinal)) return false;
        path = uri.AbsolutePath;
        return true;
    }
}

// One thread href plus one preview is reported once per app session. A reload
// passes the keys it already stored so the same preview does not count again.
internal sealed class ChatListWatch
{
    public const bool ShouldToast = false;

    private readonly TimeSpan window;
    private readonly HashSet<string> seen;
    private DateTimeOffset? reactionAt;
    private bool reactionMerged;
    private DateTimeOffset? listAt;
    private bool listMerged;

    public ChatListWatch(TimeSpan window, IEnumerable<string>? seenKeys = null)
    {
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        this.window = window;
        seen = new HashSet<string>(seenKeys ?? [], StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> SeenKeys => seen;

    public static string Key(string href, string preview) => href + "\n" + preview;

    public ChatListEffect Observe(string href, string preview, DateTimeOffset now)
    {
        if (!seen.Add(Key(href, preview))) return ChatListEffect.Ignored;
        if (reactionAt is not null && !reactionMerged && now >= reactionAt.Value && now - reactionAt.Value <= window)
        {
            reactionMerged = true;
            return ChatListEffect.Merge;
        }

        listAt = now;
        listMerged = false;
        return ChatListEffect.Count;
    }

    // ReactionWatch already decided this title rise is activity. If a chat-list
    // row counted that same rise inside the window, do not count it again.
    public bool CountReactionRise(DateTimeOffset now)
    {
        if (listAt is not null && !listMerged && now >= listAt.Value && now - listAt.Value <= window)
        {
            listMerged = true;
            return false;
        }

        reactionAt = now;
        reactionMerged = false;
        return true;
    }
}
