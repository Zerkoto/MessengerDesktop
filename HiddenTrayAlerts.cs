using System.Globalization;
using System.Text.Json;

namespace MessengerDesktop;

internal enum HiddenTraySource
{
    PageEvent,
    BackupTimer
}

internal readonly record struct HiddenTrayNotice(NotificationToastContent Toast, bool Sound, string Log);

// While the window is not the foreground, a new unread chat-list row is a
// toast and a sound. The first snapshot is only a baseline. Mute stores the
// rows and raises nothing. A page notification for the same thread and kind
// inside the dedup window is the same alert.
internal sealed class HiddenTrayAlerts
{
    public static readonly TimeSpan BackupInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(8);

    private readonly TimeSpan window;
    private readonly Dictionary<string, string> known = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Raised> recent = new();
    private bool baselined;

    public HiddenTrayAlerts()
        : this(DedupWindow)
    {
    }

    public HiddenTrayAlerts(TimeSpan dedupWindow)
    {
        if (dedupWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(dedupWindow));
        window = dedupWindow;
    }

    public IReadOnlyList<HiddenTrayNotice> Observe(
        IReadOnlyList<ChatThread> rows,
        bool foreground,
        bool muted,
        DateTimeOffset now,
        HiddenTraySource source)
    {
        var notices = new List<HiddenTrayNotice>();
        var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ChatThread row in rows)
        {
            string key = ThreadKey(row.Href, row.Name);
            if (key.Length == 0) continue;
            string preview = row.Preview ?? "";
            next[key] = preview;
            if (!baselined) continue;
            bool isNew = !known.ContainsKey(key);
            bool changed = known.TryGetValue(key, out string? previous)
                && !string.Equals(previous, preview, StringComparison.Ordinal);
            if (!isNew && !changed) continue;
            if (foreground || muted) continue;
            bool reaction = ReactionNotice.TryParse(row.Name, preview, row.Href, out ReactionToast parsed);
            string identity = reaction ? parsed.Body : "msg";
            if (Recently(key, row.Name, reaction, identity, now)) continue;
            string url = Absolute(row.Href);
            NotificationToastContent toast = NotificationToast.Map(row.Name, preview, null, null, url, key, reaction);
            notices.Add(new HiddenTrayNotice(toast, Sound: true, LogLine(now, source, key, reaction)));
            Remember(key, row.Name, reaction, identity, now);
        }

        known.Clear();
        foreach ((string key, string preview) in next) known[key] = preview;
        baselined = true;
        return notices;
    }

    public bool SuppressPage(NotificationRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        (bool reaction, string identity) = PageIdentity(record);
        return Recently(ThreadKey(record.Url, record.Title), record.Title, reaction, identity, now)
            || Recently(ThreadKey(record.Tag, record.Title), record.Title, reaction, identity, now);
    }

    public void NotePage(NotificationRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        (bool reaction, string identity) = PageIdentity(record);
        string key = ThreadKey(record.Url, record.Title);
        if (key.Length == 0) key = ThreadKey(record.Tag, record.Title);
        Remember(key, record.Title, reaction, identity, now);
    }

    public static string LogLine(DateTimeOffset at, HiddenTraySource source, string thread, bool reaction)
    {
        string name = source == HiddenTraySource.BackupTimer ? "backup-timer" : "page-event";
        string kind = reaction ? "reaction" : "msg";
        return at.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK", CultureInfo.InvariantCulture)
            + " source=" + name + " kind=" + kind + " thread=" + OneToken(thread);
    }

    private static string OneToken(string thread)
    {
        if (string.IsNullOrWhiteSpace(thread)) return "chat";
        string text = thread.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal).Trim();
        return text.Length == 0 ? "chat" : text;
    }

    public static bool TryReadScriptResult(string? executed, out bool readable, out IReadOnlyList<ChatThread> threads)
    {
        readable = false;
        threads = [];
        if (string.IsNullOrWhiteSpace(executed)) return false;
        string payload = executed.Trim();
        if (payload == "null") return false;
        try
        {
            if (payload.StartsWith('"'))
            {
                string? inner = JsonSerializer.Deserialize<string>(payload);
                if (string.IsNullOrWhiteSpace(inner)) return false;
                payload = inner;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return ChatListMessage.TryParse("messenger-chat-list:" + payload, out readable, out threads);
    }

    private bool Recently(string key, string? name, bool reaction, string identity, DateTimeOffset now)
    {
        Prune(now);
        string who = (name ?? "").Trim();
        foreach (Raised item in recent)
        {
            if (item.Reaction != reaction || now - item.At > window) continue;
            if (!item.Identity.Equals(identity, StringComparison.Ordinal)) continue;
            if (key.Length > 0 && item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return true;
            if (who.Length > 0 && item.Name.Equals(who, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private void Remember(string key, string? name, bool reaction, string identity, DateTimeOffset now)
    {
        recent.Add(new Raised(key, (name ?? "").Trim(), reaction, identity, now));
    }

    private static (bool Reaction, string Identity) PageIdentity(NotificationRecord record)
    {
        if (ReactionNotice.TryParse(record.Title, record.Body, record.Url, out ReactionToast toast))
            return (true, toast.Body);
        if (IsReaction(record))
            return (true, (record.Body ?? "").Trim());
        return (false, "msg");
    }

    private void Prune(DateTimeOffset now)
    {
        recent.RemoveAll(item => now - item.At > window);
    }

    private static bool IsReaction(NotificationRecord record) =>
        TraySummary.IsReaction(record) || ReactionNotice.TryParse(record.Title, record.Body, record.Url, out _);

    internal static string ThreadKey(string? href, string? name)
    {
        if (!string.IsNullOrWhiteSpace(href))
        {
            string text = href.Trim();
            if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
                text = uri.AbsolutePath;
            int cut = text.IndexOfAny(['?', '#']);
            if (cut >= 0) text = text[..cut];
            text = text.Trim('/');
            if (text.Length > 0) return text.ToLowerInvariant();
        }

        string who = (name ?? "").Trim();
        return who.Length == 0 ? "" : "name:" + who.ToLowerInvariant();
    }

    private static string Absolute(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return "";
        string text = href.Trim();
        if (text.StartsWith('/')) return "https://www.facebook.com" + text;
        return text;
    }

    private readonly record struct Raised(string Key, string Name, bool Reaction, string Identity, DateTimeOffset At);
}
