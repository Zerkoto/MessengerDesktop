using System.Globalization;

namespace MessengerDesktop;

internal enum TrayRowKind
{
    Message,
    Reaction
}

internal enum TrayGlyph
{
    None,
    Count,
    Muted
}

internal enum TrayMute
{
    Off,
    OneHour,
    EightHours,
    UntilOn
}

internal enum TrayMenuEntryKind
{
    Header,
    Separator,
    Open,
    Mute,
    Startup,
    Quit
}

// Local tray state. Nothing in this type talks to Facebook, the WebView, or a read receipt.
internal sealed record TrayRow(
    string Key,
    string Name,
    string? Preview,
    int MessageCount,
    string? AvatarUrl,
    string Url,
    string Tag,
    TrayRowKind Kind,
    DateTimeOffset At)
{
    public string Initials => TrayText.Initials(Name);

    public bool CountOnly => Kind == TrayRowKind.Message && string.IsNullOrWhiteSpace(Preview);

    public string Secondary => Kind == TrayRowKind.Reaction
        ? TrayText.ReactionLine
        : CountOnly
            ? MessageCount.ToString(CultureInfo.InvariantCulture)
            : Preview ?? "";
}

internal readonly record struct TrayPoint(int X, int Y);

internal readonly record struct TrayDisk(int X, int Y, int Diameter, int Argb);

// Pixel layout for the 16px tray icon. The painter fills these disks and nothing else.
internal static class TrayBadgeLayout
{
    public static IReadOnlyList<TrayDisk> Layers(TrayGlyph glyph)
    {
        if (glyph == TrayGlyph.None)
            return [];

        if (glyph == TrayGlyph.Muted)
        {
            int diameter = TrayBadgeMetrics.MutedDotDiameter;
            int inset = 1;
            int origin = TrayBadgeMetrics.IconSize - diameter - inset;
            return [new TrayDisk(origin, origin, diameter, TrayBadgeMetrics.MutedDotArgb)];
        }

        int outer = TrayBadgeMetrics.CountDiameter + (TrayBadgeMetrics.RingThickness * 2);
        int x = TrayBadgeMetrics.IconSize - outer;
        int y = TrayBadgeMetrics.IconSize - outer;
        return
        [
            new TrayDisk(x, y, outer, TrayBadgeMetrics.RingArgb),
            new TrayDisk(x + TrayBadgeMetrics.RingThickness, y + TrayBadgeMetrics.RingThickness, TrayBadgeMetrics.CountDiameter, TrayBadgeMetrics.CountArgb)
        ];
    }
}

internal static class TrayBadgeMetrics
{
    public const int IconSize = 16;
    public const int CountDiameter = 9;
    public const int RingThickness = 1;
    public const int CountArgb = unchecked((int)0xFFE81123);
    public const int RingArgb = unchecked((int)0xFFFFFFFF);
    public const int NumberArgb = unchecked((int)0xFFFFFFFF);
    public const int MutedDotDiameter = 5;
    public const int MutedDotArgb = unchecked((int)0xFF808080);

    public static int Rgb(int argb) => argb & 0xFFFFFF;
}

internal static class TrayMetrics
{
    public const int MenuMinWidth = 220;
    public const int RowHeight = 32;
    public const int HeaderHeight = 52;
    public const int HorizontalPadding = 12;
    public const int ImageColumn = 28;
    public const int PanelWidth = 340;
    public const int PanelMaxHeight = 420;
    public const int PanelRowHeight = 56;
    public const int VisibleConversations = 5;
    public const int FlyoutGap = 8;
}

internal static class TrayMenuModel
{
    public static IReadOnlyList<TrayMenuEntryKind> Order { get; } =
    [
        TrayMenuEntryKind.Header,
        TrayMenuEntryKind.Separator,
        TrayMenuEntryKind.Open,
        TrayMenuEntryKind.Mute,
        TrayMenuEntryKind.Separator,
        TrayMenuEntryKind.Startup,
        TrayMenuEntryKind.Separator,
        TrayMenuEntryKind.Quit
    ];

    public static IReadOnlyList<TrayMute> MuteChoices { get; } =
    [
        TrayMute.OneHour,
        TrayMute.EightHours,
        TrayMute.UntilOn
    ];

    public static string Text(TrayMenuEntryKind kind) => kind switch
    {
        TrayMenuEntryKind.Header => "Messenger+",
        TrayMenuEntryKind.Open => "Open Messenger",
        TrayMenuEntryKind.Mute => "Mute notifications",
        TrayMenuEntryKind.Startup => "Start with Windows",
        TrayMenuEntryKind.Quit => "Quit Messenger+",
        _ => ""
    };

    public static string MuteText(TrayMute mute) => mute switch
    {
        TrayMute.OneHour => "1 hour",
        TrayMute.EightHours => "8 hours",
        TrayMute.UntilOn => "Until I turn it back on",
        _ => ""
    };

    public static TimeSpan? Duration(TrayMute mute) => mute switch
    {
        TrayMute.OneHour => TimeSpan.FromHours(1),
        TrayMute.EightHours => TimeSpan.FromHours(8),
        TrayMute.UntilOn => null,
        _ => throw new ArgumentOutOfRangeException(nameof(mute))
    };
}

internal static class TrayText
{
    public const string ReactionLine = "❤️ reacted to your message";
    public const string EmptyState = "You're all caught up ✓";
    public const string PanelTitle = "Unread";
    public const string MarkAllRead = "Mark all read";

    public static string Badge(int count)
    {
        if (count <= 0) return "";
        if (count > 9) return "9+";
        return count.ToString(CultureInfo.InvariantCulture);
    }

    public static string Tooltip(int count)
    {
        string text = count <= 0 ? "Messenger+" : $"Messenger+ · {count} unread";
        return text.Length <= UnreadTitle.TooltipLimit ? text : text[..UnreadTitle.TooltipLimit];
    }

    public static string Header(int unread, int reactions)
    {
        string reactionWord = reactions == 1 ? "reaction" : "reactions";
        return $"{unread.ToString(CultureInfo.InvariantCulture)} unread · {reactions.ToString(CultureInfo.InvariantCulture)} {reactionWord}";
    }

    public static string Relative(DateTimeOffset at, DateTimeOffset now)
    {
        TimeSpan delta = now - at;
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        if (delta < TimeSpan.FromMinutes(1)) return "now";
        if (delta < TimeSpan.FromHours(1)) return $"{(int)delta.TotalMinutes}m";
        if (delta < TimeSpan.FromDays(1)) return $"{(int)delta.TotalHours}h";
        return $"{(int)delta.TotalDays}d";
    }

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        char? Letter(string word)
        {
            foreach (char character in word)
            {
                if (char.IsLetter(character)) return char.ToUpperInvariant(character);
            }

            return null;
        }

        if (parts.Length == 0) return "?";
        char? first = Letter(parts[0]);
        if (parts.Length == 1) return first is null ? "?" : first.Value.ToString();
        char? last = Letter(parts[^1]);
        if (first is null && last is null) return "?";
        if (first is null) return last!.Value.ToString();
        if (last is null) return first.Value.ToString();
        return string.Concat(first.Value, last.Value);
    }
}

internal static class TrayFlyoutPlacement
{
    public static TrayPoint Place(
        int anchorX,
        int anchorY,
        int width,
        int height,
        int areaLeft,
        int areaTop,
        int areaRight,
        int areaBottom)
    {
        int x = anchorX - (width / 2);
        int y = anchorY - height - TrayMetrics.FlyoutGap;
        if (y < areaTop)
            y = anchorY + TrayMetrics.FlyoutGap;
        if (x < areaLeft)
            x = areaLeft;
        if (x + width > areaRight)
            x = Math.Max(areaLeft, areaRight - width);
        if (y + height > areaBottom)
            y = Math.Max(areaTop, areaBottom - height);
        return new TrayPoint(x, y);
    }
}

// Names and previews come from notification records the pipeline already captured.
// MarkAllRead only forgets those rows. It does not change the message badge and it
// has no WebView, click, or read-receipt side effect.
internal sealed class TraySummary
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UnreadChat> unreadMessages = new(StringComparer.Ordinal);
    private int unseenReactions;
    private int? pageUnread;
    private long sequence;
    private TrayMute muteChoice = TrayMute.Off;
    private DateTimeOffset? mutedUntil;

    public int UnreadMessages => unreadMessages.Values.Sum(chat => chat.Count);

    public int UnseenReactions => unseenReactions;

    public int BadgeCount => UnreadMessages + UnseenReactions;

    public string BadgeLabel => TrayText.Badge(BadgeCount);

    public string Tooltip => TrayText.Tooltip(BadgeCount);

    public string HeaderLine => TrayText.Header(UnreadMessages, UnseenReactions);

    public bool IsEmpty => Latest().Count == 0;

    public static bool IsReaction(NotificationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ContainsReacted(record.Title) || ContainsReacted(record.Body);
    }

    public void Observe(NotificationRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (IsReaction(record)) NoteReaction(record, now);
        else NoteMessage(record, now);
    }

    public void NoteActivity(DateTimeOffset now)
    {
        unseenReactions++;
        Entry entry = GetOrAdd("reaction:activity", TrayRowKind.Reaction);
        entry.Name = "New activity";
        entry.Preview = TrayText.ReactionLine;
        entry.At = now;
        entry.Sequence = ++sequence;
    }

    public void OnPanelOpened() => MarkReactionsSeen();

    public void MarkReactionsSeen()
    {
        unseenReactions = 0;
        foreach (string key in entries.Where(pair => pair.Value.Kind == TrayRowKind.Reaction).Select(pair => pair.Key).ToList())
            entries.Remove(key);
    }

    // The chat was opened in the app. Other chats and unseen reactions stay.
    public void MarkChatRead(string? urlOrTag)
    {
        if (string.IsNullOrWhiteSpace(urlOrTag)) return;
        string needle = urlOrTag.Trim();
        foreach (string key in unreadMessages.Keys.Where(key => UnreadMatches(key, needle)).ToList())
            unreadMessages.Remove(key);
        foreach (string key in entries.Where(pair => pair.Value.Kind == TrayRowKind.Message && EntryMatches(pair.Key, needle)).Select(pair => pair.Key).ToList())
            entries.Remove(key);
    }

    // The summary row itself was opened. Used when the captured notification has no chat URL.
    public void MarkRowRead(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        unreadMessages.Remove(key);
        if (entries.TryGetValue(key, out Entry? entry) && entry.Kind == TrayRowKind.Message)
            entries.Remove(key);
    }

    // Drops the visible summary only. Message and reaction badge counts are left alone,
    // and this method does not navigate, click, or tell Facebook anything.
    public void MarkAllRead() => entries.Clear();

    // Title unread from the existing page-title tracker. Messages leave the badge when
    // that count falls to zero because the open chat was read in the app. The first
    // sample is only a baseline, matching the reaction watch.
    public void NotePageUnread(int count)
    {
        if (count < 0) count = 0;
        int? previous = pageUnread;
        pageUnread = count;
        if (previous is > 0 && count == 0)
            ClearMessages();
    }

    public void Mute(TrayMute choice, DateTimeOffset now)
    {
        if (choice == TrayMute.Off)
        {
            Unmute();
            return;
        }

        muteChoice = choice;
        TimeSpan? duration = TrayMenuModel.Duration(choice);
        mutedUntil = duration is null ? DateTimeOffset.MaxValue : now + duration.Value;
    }

    public void Unmute()
    {
        muteChoice = TrayMute.Off;
        mutedUntil = null;
    }

    public bool IsMuted(DateTimeOffset now) => mutedUntil is not null && now < mutedUntil.Value;

    public TrayMute MuteChoice(DateTimeOffset now) => IsMuted(now) ? muteChoice : TrayMute.Off;

    public TimeSpan? MuteRemaining(DateTimeOffset now)
    {
        if (!IsMuted(now) || mutedUntil is null) return null;
        if (mutedUntil.Value == DateTimeOffset.MaxValue) return Timeout.InfiniteTimeSpan;
        return mutedUntil.Value - now;
    }

    public TrayGlyph GlyphAt(DateTimeOffset now)
    {
        if (IsMuted(now)) return TrayGlyph.Muted;
        return BadgeCount <= 0 ? TrayGlyph.None : TrayGlyph.Count;
    }

    public IReadOnlyList<TrayRow> Latest(int? limit = null)
    {
        int take = limit ?? TrayMetrics.VisibleConversations;
        if (take < 0) take = 0;
        return entries.Values
            .OrderByDescending(entry => entry.At)
            .ThenByDescending(entry => entry.Sequence)
            .Take(take)
            .Select(entry => entry.ToRow())
            .ToList();
    }

    private void NoteMessage(NotificationRecord record, DateTimeOffset now)
    {
        string key = KeyFor(record);
        Entry entry = GetOrAdd(key, TrayRowKind.Message);
        entry.Name = DisplayName(record.Title);
        entry.Preview = string.IsNullOrWhiteSpace(record.Body) ? null : Collapse(record.Body);
        entry.AvatarUrl = BlankToNull(record.Icon);
        entry.Url = record.Url ?? "";
        entry.Tag = record.Tag ?? "";
        entry.At = now;
        entry.Sequence = ++sequence;
        UnreadChat chat = unreadMessages.TryGetValue(key, out UnreadChat? existing)
            ? existing
            : new UnreadChat();
        chat.Count++;
        chat.Url = entry.Url;
        chat.Tag = entry.Tag;
        entry.MessageCount = chat.Count;
        unreadMessages[key] = chat;
    }

    private void NoteReaction(NotificationRecord record, DateTimeOffset now)
    {
        unseenReactions++;
        string key = "reaction:" + KeyFor(record);
        Entry entry = GetOrAdd(key, TrayRowKind.Reaction);
        entry.Name = DisplayName(record.Title);
        entry.Preview = TrayText.ReactionLine;
        entry.AvatarUrl = BlankToNull(record.Icon);
        entry.Url = record.Url ?? "";
        entry.Tag = record.Tag ?? "";
        entry.At = now;
        entry.Sequence = ++sequence;
        entry.MessageCount = 0;
    }

    private void ClearMessages()
    {
        unreadMessages.Clear();
        foreach (string key in entries.Where(pair => pair.Value.Kind == TrayRowKind.Message).Select(pair => pair.Key).ToList())
            entries.Remove(key);
    }

    private Entry GetOrAdd(string key, TrayRowKind kind)
    {
        if (!entries.TryGetValue(key, out Entry? entry) || entry.Kind != kind)
        {
            entry = new Entry { Key = key, Kind = kind };
            entries[key] = entry;
        }

        return entry;
    }

    private bool UnreadMatches(string key, string needle)
    {
        if (!unreadMessages.TryGetValue(key, out UnreadChat? chat)) return false;
        return SameChat(chat.Url, needle) || SameChat(chat.Tag, needle) || SameChat(key, needle);
    }

    private bool EntryMatches(string key, string needle)
    {
        if (!entries.TryGetValue(key, out Entry? entry)) return SameChat(key, needle);
        return SameChat(entry.Url, needle) || SameChat(entry.Tag, needle) || SameChat(key, needle);
    }

    private static bool SameChat(string? stored, string needle)
    {
        if (string.IsNullOrWhiteSpace(stored)) return false;
        string left = stored.Trim();
        if (left.Equals(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (!Uri.TryCreate(left, UriKind.Absolute, out Uri? storedUri)) return false;
        if (!Uri.TryCreate(needle, UriKind.Absolute, out Uri? needleUri)) return false;
        if (IsInbox(storedUri) || IsInbox(needleUri)) return false;
        return storedUri.AbsolutePath.Equals(needleUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string KeyFor(NotificationRecord record)
    {
        // WebView2 notifications usually have no chat URL. The tag is a dedupe id, not a
        // conversation, so rows group by the captured sender name unless a chat link is present.
        if (TrySpecificChat(record.Url, out Uri? uri))
            return "url:" + uri!.AbsolutePath;
        if (TrySpecificChat(record.Tag, out Uri? tagged))
            return "url:" + tagged!.AbsolutePath;
        return "name:" + DisplayName(record.Title);
    }

    private static bool TrySpecificChat(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? parsed)) return false;
        Uri opened = NotificationNavigation.OpenTarget(parsed.AbsoluteUri, null);
        if (!opened.AbsoluteUri.Equals(parsed.AbsoluteUri, StringComparison.OrdinalIgnoreCase)) return false;
        if (IsInbox(parsed)) return false;
        uri = parsed;
        return true;
    }

    private static bool IsInbox(Uri uri)
    {
        string path = uri.AbsolutePath.TrimEnd('/');
        return path.Equals("/messages/t", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/messages", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/t", StringComparison.OrdinalIgnoreCase)
            || path.Length == 0;
    }

    private static string DisplayName(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "Messenger" : title.Trim();

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Collapse(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool ContainsReacted(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains("reacted", StringComparison.OrdinalIgnoreCase);

    private sealed class UnreadChat
    {
        public int Count { get; set; }
        public string Url { get; set; } = "";
        public string Tag { get; set; } = "";
    }

    private sealed class Entry
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Preview { get; set; }
        public int MessageCount { get; set; }
        public string? AvatarUrl { get; set; }
        public string Url { get; set; } = "";
        public string Tag { get; set; } = "";
        public TrayRowKind Kind { get; set; }
        public DateTimeOffset At { get; set; }
        public long Sequence { get; set; }

        public TrayRow ToRow() => new(Key, Name, Preview, MessageCount, AvatarUrl, Url, Tag, Kind, At);
    }
}
