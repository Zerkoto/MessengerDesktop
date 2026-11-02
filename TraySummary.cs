using System.Globalization;
using System.Text.RegularExpressions;

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
    Separator,
    Open,
    Mute,
    WatcherToggle,
    XLinkPreviews,
    Startup,
    Quit,
    Unmute
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
    DateTimeOffset At,
    string? TimeLabel = null)
{
    public string Initials => TrayText.Initials(Name);

    public bool CountOnly => Kind == TrayRowKind.Message && string.IsNullOrWhiteSpace(Preview);

    public string Secondary => Kind == TrayRowKind.Reaction
        ? ReactionSecondary
        : CountOnly
            ? MessageCount.ToString(CultureInfo.InvariantCulture)
            : Preview ?? "";

    // A parsed reaction keeps its body. A row with no preview uses the plain fallback.
    private string ReactionSecondary =>
        string.IsNullOrWhiteSpace(Preview) ? ReactionNotice.FallbackBody : Preview;
}

internal readonly record struct TrayPoint(int X, int Y);

internal readonly record struct TrayDisk(int X, int Y, int Diameter, int Argb);

// Disks for a tray icon drawn at the real small-icon size. The count circle is
// about 60% of that icon so the digit stays readable after the shell shows it.
internal static class TrayBadgeLayout
{
    public static IReadOnlyList<TrayDisk> Layers(TrayGlyph glyph) =>
        Layers(glyph, TrayBadgeMetrics.IconSizeForDpi(96));

    public static IReadOnlyList<TrayDisk> Layers(TrayGlyph glyph, int iconSize)
    {
        if (iconSize < 1) iconSize = 1;
        if (glyph == TrayGlyph.None)
            return [];

        if (glyph == TrayGlyph.Muted)
        {
            int diameter = Math.Max(4, (int)Math.Round(iconSize * 0.30));
            int inset = Math.Max(1, iconSize / 16);
            if (diameter + inset > iconSize)
                diameter = Math.Max(1, iconSize - inset);
            int origin = iconSize - diameter - inset;
            return [new TrayDisk(origin, origin, diameter, TrayBadgeMetrics.MutedDotArgb)];
        }

        int outer = Math.Max(1, (int)Math.Round(iconSize * 0.60));
        if (outer > iconSize) outer = iconSize;
        int ring = Math.Max(1, iconSize / 16);
        if ((ring * 2) >= outer) ring = Math.Max(1, outer / 5);
        int inner = outer - (ring * 2);
        if (inner < 1)
        {
            ring = 0;
            inner = outer;
        }

        int x = iconSize - outer;
        int y = iconSize - outer;
        if (ring == 0)
            return [new TrayDisk(x, y, outer, TrayBadgeMetrics.CountArgb)];

        return
        [
            new TrayDisk(x, y, outer, TrayBadgeMetrics.RingArgb),
            new TrayDisk(x + ring, y + ring, inner, TrayBadgeMetrics.CountArgb)
        ];
    }

    // Pixel size of the bold white digit. A single character stays large enough
    // to read inside the inner circle; "9+" is slightly smaller so both glyphs fit.
    public static int NumberSize(int innerDiameter, int charCount)
    {
        if (innerDiameter < 1) return 1;
        int characters = Math.Max(1, charCount);
        int target = (int)Math.Round(innerDiameter * (characters > 1 ? 0.55 : 0.72));
        int floor = characters > 1
            ? Math.Max(5, innerDiameter / 3)
            : Math.Max(7, innerDiameter / 2);
        int size = Math.Max(floor, target);
        return Math.Min(size, innerDiameter);
    }
}

internal static class TrayBadgeMetrics
{
    public const int BaseIconSize = 16;
    public const int CountArgb = unchecked((int)0xFFE81123);
    public const int RingArgb = unchecked((int)0xFFFFFFFF);
    public const int NumberArgb = unchecked((int)0xFFFFFFFF);
    public const int MutedDotArgb = unchecked((int)0xFF808080);

    public static int IconSizeForDpi(int dpi)
    {
        if (dpi < 1) dpi = 96;
        return Math.Max(BaseIconSize, (int)Math.Round(BaseIconSize * (dpi / 96.0)));
    }

    public static int Rgb(int argb) => argb & 0xFFFFFF;
}

internal static class TrayMetrics
{
    public const int PanelWidth = 340;
    public const int PanelMaxHeight = 420;
    public const int VisibleConversations = 5;
    public const int FlyoutGap = 8;
    public const int CornerRadius = 8;
    public const int HeaderHeight = 48;
    public const int HeaderPadding = 16;
    public const int RowHeight = 64;
    public const int Avatar = 40;
    public const int HoverInset = 8;
    public const int HoverRadius = 6;
    public const int ButtonHeight = 36;
    public const int ButtonMargin = 12;
    public const int ButtonRadius = 6;
    public const int EmptyPadding = 24;
    public const int CheckSize = 48;
}

internal static class TrayMenuModel
{
    // WatcherToggle is the chat-list item drawn directly under Mute.
    public static IReadOnlyList<TrayMenuEntryKind> Order { get; } =
    [
        TrayMenuEntryKind.Open,
        TrayMenuEntryKind.Separator,
        TrayMenuEntryKind.Mute,
        TrayMenuEntryKind.WatcherToggle,
        TrayMenuEntryKind.XLinkPreviews,
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
        TrayMenuEntryKind.Open => "Open Messenger",
        TrayMenuEntryKind.Mute => "Mute notifications",
        TrayMenuEntryKind.WatcherToggle => "Watch chat list for reactions (beta)",
        TrayMenuEntryKind.XLinkPreviews => "X link previews",
        TrayMenuEntryKind.Startup => "Start with Windows",
        TrayMenuEntryKind.Quit => "Quit Messenger+",
        TrayMenuEntryKind.Unmute => "Unmute",
        _ => ""
    };

    public static string Description(TrayMenuEntryKind kind) => kind switch
    {
        TrayMenuEntryKind.WatcherToggle => "Can miss reactions in the chat you have open and in muted chats.",
        _ => ""
    };

    // X link previews starts checked. The reactions watch stays off until the menu turns it on.
    public static bool CheckedByDefault(TrayMenuEntryKind kind) => kind == TrayMenuEntryKind.XLinkPreviews;

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

    public static string UnreadFallback(int count) =>
        $"{Math.Max(0, count).ToString(CultureInfo.InvariantCulture)} unread";

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
    // Right edge lines up with the tray icon. Bottom taskbar: 8px above the working
    // area (the taskbar). Top taskbar: 8px below the working area. Then clamp.
    public static TrayPoint Place(
        int iconLeft,
        int iconTop,
        int iconRight,
        int iconBottom,
        int width,
        int height,
        int areaLeft,
        int areaTop,
        int areaRight,
        int areaBottom)
    {
        int gap = TrayMetrics.FlyoutGap;
        int x = iconRight - width;
        int y = areaBottom - height - gap;
        if (iconBottom <= areaTop)
            y = areaTop + gap;
        if (iconRight <= areaLeft)
            x = areaLeft + gap;
        else if (iconLeft >= areaRight)
            x = areaRight - width - gap;

        if (x < areaLeft)
            x = areaLeft;
        if (x + width > areaRight)
            x = Math.Max(areaLeft, areaRight - width);
        if (y < areaTop)
            y = areaTop;
        if (y + height > areaBottom)
            y = Math.Max(areaTop, areaBottom - height);
        return new TrayPoint(x, y);
    }
}

// Unread chats come from the Messenger chat list. Toasts only fill in a row the
// list already has, or stand in until that read finishes. MarkAllRead forgets the
// visible rows on this PC. It does not change the badge and it has no WebView,
// click, or read-receipt side effect.
internal sealed class TraySummary
{
    private static readonly Regex RelativeAmount = new(@"^(\d+)\s*([smhdw])$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UnreadChat> unreadMessages = new(StringComparer.Ordinal);
    private readonly List<ChatThread> pageThreads = new();
    // Chats opened in the window, keyed to the preview at that moment. A later
    // snapshot that still lists the same preview does not put the badge back.
    private readonly Dictionary<string, string> opened = new(StringComparer.Ordinal);
    private int unseenReactions;
    private int? pageUnread;
    private long sequence;
    private bool chatListReadable;
    private bool chatListFailed;
    private bool hidePageThreads;
    private string pageFingerprint = "";
    private DateTimeOffset pageObservedAt;
    private TrayMute muteChoice = TrayMute.Off;
    private DateTimeOffset? mutedUntil;
    private ReactionToast? pendingReaction;
    private DateTimeOffset pendingReactionAt;

    public int UnreadMessages => unreadMessages.Values.Sum(chat => chat.Count);

    public int UnseenReactions => unseenReactions;

    public int BadgeCount => MessageBadge + UnseenReactions;

    public string BadgeLabel => TrayText.Badge(BadgeCount);

    public string Tooltip => TrayText.Tooltip(BadgeCount);

    public string HeaderLine => TrayText.Header(UnreadMessages, UnseenReactions);

    public bool IsEmpty => Latest().Count == 0;

    public TrayIdle Idle
    {
        get
        {
            if (BuildList(1).Count > 0) return TrayIdle.Rows;
            if (!chatListReadable)
            {
                if (chatListFailed && (pageUnread ?? 0) > 0) return TrayIdle.CountOnly;
                return TrayIdle.Checking;
            }

            if (MessageBadge == 0 && UnseenReactions == 0) return TrayIdle.CaughtUp;
            return TrayIdle.Rows;
        }
    }

    public string IdleLine => Idle switch
    {
        TrayIdle.Checking => FlyoutText.Checking,
        TrayIdle.CaughtUp => FlyoutText.CaughtUp,
        TrayIdle.CountOnly => TrayText.UnreadFallback(pageUnread ?? 0),
        _ => ""
    };

    public bool ShowsCaughtUp => Idle == TrayIdle.CaughtUp;

    // readable is a finished scan of the chat grid. False means the rows could
    // not be read, so the page title is the only backup. A missing result leaves
    // the summary on "Checking for messages…".
    public void NoteChatList(bool readable, IReadOnlyList<ChatThread> threads, DateTimeOffset now)
    {
        if (!readable)
        {
            if (!chatListReadable) chatListFailed = true;
            return;
        }

        List<ChatThread> next = Dedupe(threads);
        string print = Fingerprint(next);
        if (print != pageFingerprint) hidePageThreads = false;
        pageFingerprint = print;
        pageThreads.Clear();
        pageThreads.AddRange(next);
        pageObservedAt = now;
        chatListReadable = true;
        chatListFailed = false;
        SyncOpened();
        bool pendingSet = false;
        foreach (ChatThread thread in next)
        {
            if (!ReactionNotice.TryParse(thread.Name, thread.Preview, thread.Href, out ReactionToast toast)) continue;
            if (!pendingSet)
            {
                pendingReaction = toast;
                pendingReactionAt = now;
                pendingSet = true;
            }

            // count is false: the unread row is already in the badge. The body is
            // stored now so a later message in this thread does not erase it.
            CaptureReaction(thread.Name, thread.Preview, toast.Body, thread.Href, thread.Href, thread.AvatarUrl, "", now, count: false, mergeActivity: false);
        }
    }

    public bool TryReactionToast(DateTimeOffset now, out ReactionToast toast)
    {
        toast = default;
        if (pendingReaction is null) return false;
        if (now - pendingReactionAt > TimeSpan.FromMinutes(2)) return false;
        toast = pendingReaction.Value;
        return true;
    }

    public static bool IsReaction(NotificationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ContainsReacted(record.Title) || ContainsReacted(record.Body)
            || ContainsBulgarianReaction(record.Title) || ContainsBulgarianReaction(record.Body);
    }

    public void Observe(NotificationRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (ReactionNotice.TryParse(record.Title, record.Body, record.Url, out ReactionToast toast))
        {
            pendingReaction = toast;
            pendingReactionAt = now;
        }

        if (IsReaction(record)) NoteReaction(record, now);
        else NoteMessage(record, now);
    }

    public void NoteActivity(DateTimeOffset now)
    {
        unseenReactions++;
        Entry entry = GetOrAdd("reaction:activity", TrayRowKind.Reaction);
        entry.Name = "Messenger";
        entry.Preview = ReactionNotice.FallbackBody;
        entry.At = now;
        entry.Sequence = ++sequence;
    }

    // Counted like a reaction: it leaves the badge when the summary opens.
    // count is false when ReactionWatch already covered this rise.
    public void NoteChatListActivity(string chatName, string? preview, string href, DateTimeOffset now, bool count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentException.ThrowIfNullOrWhiteSpace(href);
        string name = chatName.Trim();
        string path = href.Trim();
        string body = ReactionNotice.TryParse(name, preview, path, out ReactionToast toast)
            ? toast.Body
            : ReactionNotice.FallbackBody;
        string url = path.StartsWith('/') ? "https://www.facebook.com" + path : path;
        CaptureReaction(name, preview, body, path, url, null, "", now, count, mergeActivity: true);
    }

    public void OnPanelOpened() => MarkReactionsSeen();

    public void MarkReactionsSeen()
    {
        unseenReactions = 0;
        foreach (string key in entries.Where(pair => pair.Value.Kind == TrayRowKind.Reaction).Select(pair => pair.Key).ToList())
            entries.Remove(key);
    }

    // The chat was opened in the app. The badge drops that chat until its preview
    // changes. Other chats and unseen reactions stay. A chat-list refresh that
    // still shows the same preview does not bring the badge back.
    public void MarkChatRead(string? urlOrTag)
    {
        if (string.IsNullOrWhiteSpace(urlOrTag)) return;
        string needle = urlOrTag.Trim();
        RememberOpened(needle);
        foreach (string key in unreadMessages.Keys.Where(key => UnreadMatches(key, needle)).ToList())
            unreadMessages.Remove(key);
        foreach (string key in entries.Where(pair => pair.Value.Kind == TrayRowKind.Message && EntryMatches(pair.Key, needle)).Select(pair => pair.Key).ToList())
            entries.Remove(key);
        pageThreads.RemoveAll(thread => SameThread(thread.Href, needle) || SameThread(thread.Name, needle));
    }

    // The summary row itself was opened. Used when the captured notification has no chat URL.
    public void MarkRowRead(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        foreach (ChatThread thread in pageThreads)
        {
            if (ThreadKey(thread.Href, thread.Name) != key) continue;
            opened[key] = thread.Preview ?? "";
        }

        unreadMessages.Remove(key);
        if (entries.TryGetValue(key, out Entry? entry) && entry.Kind == TrayRowKind.Message)
            entries.Remove(key);
        pageThreads.RemoveAll(thread => ThreadKey(thread.Href, thread.Name) == key);
    }

    // Drops the visible summary only. The chat-list badge is left alone,
    // and this method does not navigate, click, or tell Facebook anything.
    public void MarkAllRead()
    {
        entries.Clear();
        hidePageThreads = true;
    }

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
        return BuildList(take);
    }

    private int MessageBadge
    {
        get
        {
            if (chatListReadable) return LiveRows() + NewerToastThreads();
            if (chatListFailed) return Math.Max(pageUnread ?? 0, UnreadMessages);
            return UnreadMessages;
        }
    }

    // The badge follows the live unread rows. A chat opened in the window stays
    // out while that row's preview is unchanged.
    private int LiveRows()
    {
        int count = 0;
        foreach (ChatThread thread in pageThreads)
        {
            if (!IsOpened(thread)) count++;
        }

        return count;
    }

    private int NewerToastThreads()
    {
        int extra = 0;
        foreach (Entry entry in entries.Values)
        {
            if (entry.Kind != TrayRowKind.Message || entry.At <= pageObservedAt) continue;
            if (IsOpenedEntry(entry)) continue;
            if (pageThreads.Any(thread => ThreadKey(thread.Href, thread.Name) == entry.Key || SameThread(thread.Href, entry.Url)))
                continue;
            extra++;
        }

        return extra;
    }

    private void RememberOpened(string needle)
    {
        foreach (ChatThread thread in pageThreads)
        {
            if (!SameThread(thread.Href, needle) && !SameThread(thread.Name, needle)) continue;
            opened[ThreadKey(thread.Href, thread.Name)] = thread.Preview ?? "";
        }
    }

    private void SyncOpened()
    {
        foreach (ChatThread thread in pageThreads)
        {
            string key = ThreadKey(thread.Href, thread.Name);
            if (!opened.TryGetValue(key, out string? seen)) continue;
            if (!string.Equals(seen, thread.Preview ?? "", StringComparison.Ordinal))
                opened.Remove(key);
        }
    }

    private bool IsOpened(ChatThread thread)
    {
        string key = ThreadKey(thread.Href, thread.Name);
        return opened.TryGetValue(key, out string? seen)
            && string.Equals(seen, thread.Preview ?? "", StringComparison.Ordinal);
    }

    private bool IsOpenedEntry(Entry entry)
    {
        if (opened.ContainsKey(entry.Key)) return true;
        return TryThreadKey(entry.Url, out string? key) && key is not null && opened.ContainsKey(key);
    }

    private IReadOnlyList<TrayRow> BuildList(int take)
    {
        if (!chatListReadable || hidePageThreads) return ToastList(take);

        var ranked = new List<(TrayRow Row, long Rank)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < pageThreads.Count; index++)
        {
            ChatThread thread = pageThreads[index];
            string key = ThreadKey(thread.Href, thread.Name);
            if (!seen.Add(key)) continue;
            Entry? toast = FindToast(thread);
            DateTimeOffset at = ParseRelative(thread.RelativeTime, pageObservedAt, index);
            string? label = string.IsNullOrWhiteSpace(thread.RelativeTime) ? null : thread.RelativeTime.Trim();
            // A toast captured after this snapshot is a newer message than the row we read.
            if (toast is not null && toast.At > pageObservedAt && toast.At > at)
            {
                at = toast.At;
                label = null;
            }

            string? avatar = string.IsNullOrWhiteSpace(thread.AvatarUrl) ? toast?.AvatarUrl : thread.AvatarUrl.Trim();
            string url = string.IsNullOrWhiteSpace(thread.Href) ? toast?.Url ?? "" : thread.Href.Trim();
            if (ReactionNotice.TryParse(thread.Name, thread.Preview, thread.Href, out ReactionToast reaction))
            {
                string body = string.IsNullOrWhiteSpace(reaction.Body) ? ReactionNotice.FallbackBody : reaction.Body;
                string reactionKey = ReactionKey(thread.Href, thread.Preview, body);
                seen.Add(reactionKey);
                seen.Add(key);
                ranked.Add((new TrayRow(reactionKey, DisplayName(thread.Name), body, 0, BlankToNull(avatar), url, "", TrayRowKind.Reaction, at, label), pageThreads.Count - index));
                continue;
            }

            string preview = ChatListSnippet.Resolve(thread.Preview, toast?.Preview);
            int count = toast?.MessageCount > 0 ? toast.MessageCount : 1;
            ranked.Add((new TrayRow(key, DisplayName(thread.Name), preview, count, BlankToNull(avatar), url, toast?.Tag ?? "", TrayRowKind.Message, at, label), pageThreads.Count - index));
        }

        foreach (Entry entry in entries.Values)
        {
            if (entry.Kind == TrayRowKind.Reaction)
            {
                if (seen.Contains(entry.Key)) continue;
                ranked.Add((entry.ToRow(), entry.Sequence));
                continue;
            }

            if (entry.Kind != TrayRowKind.Message || entry.At <= pageObservedAt || seen.Contains(entry.Key)) continue;
            if (pageThreads.Any(thread => SameThread(thread.Href, entry.Url))) continue;
            ranked.Add((entry.ToRow(), entry.Sequence));
        }

        return ranked
            .OrderByDescending(item => item.Row.At)
            .ThenByDescending(item => item.Rank)
            .Take(take)
            .Select(item => item.Row)
            .ToList();
    }

    private IReadOnlyList<TrayRow> ToastList(int take) =>
        entries.Values
            .OrderByDescending(entry => entry.At)
            .ThenByDescending(entry => entry.Sequence)
            .Take(take)
            .Select(entry => entry.ToRow())
            .ToList();

    private void NoteMessage(NotificationRecord record, DateTimeOffset now)
    {
        string key = KeyFor(record);
        // A new message counts again, including while the window is in the tray.
        opened.Remove(key);
        if (TryThreadKey(record.Url, out string? thread) && thread is not null)
            opened.Remove(thread);
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
        string body;
        string name;
        if (ReactionNotice.TryParse(record.Title, record.Body, record.Url, out ReactionToast toast))
        {
            body = toast.Body;
            name = string.IsNullOrWhiteSpace(toast.Title) ? record.Title : toast.Title;
        }
        else
        {
            body = ReactionNotice.FallbackBody;
            name = record.Title;
        }

        string thread = string.IsNullOrWhiteSpace(record.Url) ? record.Tag : record.Url;
        CaptureReaction(name, record.Body, body, thread, record.Url, record.Icon, record.Tag, now, count: true, mergeActivity: false);
    }

    // One stored row per captured reaction. A later message uses the thread key,
    // so it cannot overwrite this row. mergeActivity keeps the title-rise row
    // when ReactionWatch already counted that rise.
    private void CaptureReaction(
        string name,
        string? raw,
        string body,
        string? thread,
        string? url,
        string? avatar,
        string? tag,
        DateTimeOffset now,
        bool count,
        bool mergeActivity)
    {
        string shown = string.IsNullOrWhiteSpace(body) ? ReactionNotice.FallbackBody : body.Trim();
        string key = ReactionKey(thread, raw, shown);
        if (mergeActivity && !count && entries.ContainsKey("reaction:activity"))
            key = "reaction:activity";
        bool created = !entries.ContainsKey(key);
        if (count && created) unseenReactions++;
        Entry entry = GetOrAdd(key, TrayRowKind.Reaction);
        entry.Name = DisplayName(name);
        entry.Preview = shown;
        entry.AvatarUrl = BlankToNull(avatar);
        entry.Url = AbsoluteChat(url, thread);
        entry.Tag = tag ?? "";
        entry.MessageCount = 0;
        entry.At = now;
        entry.Sequence = ++sequence;
    }

    private static string ReactionKey(string? thread, string? raw, string shown) =>
        "reaction:" + ToastTags.Reaction(string.IsNullOrWhiteSpace(thread) ? raw : thread, ToastTags.ReactionIdentity(raw, shown));

    private static string AbsoluteChat(string? url, string? thread)
    {
        string text = string.IsNullOrWhiteSpace(url) ? thread ?? "" : url.Trim();
        if (text.StartsWith('/')) return "https://www.facebook.com" + text;
        return text;
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
        if (TryThreadKey(record.Url, out string? fromUrl)) return fromUrl!;
        if (TryThreadKey(record.Tag, out string? fromTag)) return fromTag!;
        return "name:" + DisplayName(record.Title);
    }

    private Entry? FindToast(ChatThread thread)
    {
        string key = ThreadKey(thread.Href, thread.Name);
        if (entries.TryGetValue(key, out Entry? entry) && entry.Kind == TrayRowKind.Message)
            return entry;
        foreach (Entry candidate in entries.Values)
        {
            if (candidate.Kind != TrayRowKind.Message) continue;
            if (SameThread(thread.Href, candidate.Url) || SameThread(thread.Href, candidate.Tag))
                return candidate;
            if (string.IsNullOrWhiteSpace(thread.Href)
                && DisplayName(thread.Name).Equals(candidate.Name, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    private static List<ChatThread> Dedupe(IReadOnlyList<ChatThread> threads)
    {
        var list = new List<ChatThread>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ChatThread thread in threads)
        {
            if (string.IsNullOrWhiteSpace(thread.Name) && string.IsNullOrWhiteSpace(thread.Href)) continue;
            if (!seen.Add(ThreadKey(thread.Href, thread.Name))) continue;
            list.Add(thread);
        }

        return list;
    }

    private static string Fingerprint(IReadOnlyList<ChatThread> threads)
    {
        var parts = new List<string>(threads.Count);
        foreach (ChatThread thread in threads)
            parts.Add(ThreadKey(thread.Href, thread.Name) + "|" + thread.Preview + "|" + thread.RelativeTime);
        parts.Sort(StringComparer.Ordinal);
        return string.Join("\n", parts);
    }

    private static string ThreadKey(string? href, string? name)
    {
        if (TryThreadKey(href, out string? key)) return key!;
        return "name:" + DisplayName(name);
    }

    private static bool TryThreadKey(string? href, out string? key)
    {
        key = null;
        if (!TrySpecificChat(href, out Uri? uri)) return false;
        string path = uri!.AbsolutePath.TrimEnd('/');
        int slash = path.LastIndexOf('/');
        string id = slash >= 0 ? path[(slash + 1)..] : path;
        if (id.Length == 0) return false;
        key = "thread:" + id;
        return true;
    }

    private static bool SameThread(string? left, string? right)
    {
        if (TryThreadKey(left, out string? leftKey) && TryThreadKey(right, out string? rightKey))
            return leftKey == rightKey;
        if (string.IsNullOrWhiteSpace(right)) return false;
        return SameChat(left, right);
    }

    private static DateTimeOffset ParseRelative(string? text, DateTimeOffset now, int index)
    {
        if (string.IsNullOrWhiteSpace(text)) return now.AddSeconds(-index);
        string value = text.Trim();
        if (value.Equals("now", StringComparison.OrdinalIgnoreCase)
            || value.Equals("just now", StringComparison.OrdinalIgnoreCase))
            return now;
        Match match = RelativeAmount.Match(value);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int amount))
            return now.AddSeconds(-index);
        return char.ToLowerInvariant(match.Groups[2].Value[0]) switch
        {
            's' => now.AddSeconds(-amount),
            'm' => now.AddMinutes(-amount),
            'h' => now.AddHours(-amount),
            'd' => now.AddDays(-amount),
            'w' => now.AddDays(-7 * amount),
            _ => now.AddSeconds(-index)
        };
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

    private static bool ContainsBulgarianReaction(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Contains("реагира", StringComparison.Ordinal) || value.Contains("Реагира", StringComparison.Ordinal));

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

internal enum TrayIdle
{
    Rows,
    Checking,
    CaughtUp,
    CountOnly
}

// The line under a chat name. Facebook prefixes the snippet with "Unread message:"
// (Bulgarian "Непрочетено съобщение:") and that label is not the message.
internal static class ChatListSnippet
{
    public const string NewMessage = "New message";
    public const string Photo = "📷 Photo";
    public const string Sticker = "Sticker";

    private static readonly Regex LeadingUnread = new(
        @"^(?:unread message|Непрочетено съобщение|непрочетено съобщение)\s*:\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex OnlyUnread = new(
        @"^(?:unread message|Непрочетено съобщение|непрочетено съобщение)\s*:?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SenderPrefix = new(
        @"^(?:you|вие|ти|Вие|Ти)\s*:\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RelativeTime = new(
        @"^(?:now|just now|\d+\s*[smhdw])$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] PhotoPhrases =
    [
        "sent a photo", "sent photos", "you sent a photo", "photo",
        "снимка", "Снимка", "изпрати снимка", "Изпрати снимка", "изпратихте снимка", "Изпратихте снимка"
    ];

    private static readonly string[] StickerPhrases =
    [
        "sent a sticker", "you sent a sticker", "sticker", "Sticker",
        "стикер", "Стикер", "изпрати стикер", "Изпрати стикер"
    ];

    private static readonly string[] PhotoTails = ["sent a photo", "sent photos", "изпрати снимка", "Изпрати снимка"];
    private static readonly string[] StickerTails = ["sent a sticker", "изпрати стикер", "Изпрати стикер"];

    public static string Resolve(string? snippet, string? alertOrToast)
    {
        string? line = Present(snippet);
        if (line is not null) return line;
        line = Present(alertOrToast);
        if (line is not null) return line;
        return NewMessage;
    }

    public static string? Present(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string text = Collapse(raw);
        if (IsUnreadLabel(text)) return null;
        text = Collapse(LeadingUnread.Replace(text, ""));
        if (text.Length == 0 || IsUnreadLabel(text)) return null;
        if (IsMedia(text, StickerPhrases, StickerTails)) return Sticker;
        if (IsMedia(text, PhotoPhrases, PhotoTails)) return Photo;
        return text;
    }

    public static bool IsUnreadLabel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return OnlyUnread.IsMatch(Collapse(text));
    }

    public static bool IsRelativeTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return RelativeTime.IsMatch(Collapse(text));
    }

    public static bool IsStickerAlt(string? alt)
    {
        if (string.IsNullOrWhiteSpace(alt)) return false;
        return alt.Contains("sticker", StringComparison.OrdinalIgnoreCase)
            || alt.Contains("стикер", StringComparison.OrdinalIgnoreCase);
    }

    public static string Collapse(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool IsMedia(string text, string[] phrases, string[] tails)
    {
        string core = SenderPrefix.Replace(text, "", 1).Trim().TrimEnd('.', '!', '…').Trim();
        if (core.Length == 0) return false;
        foreach (string phrase in phrases)
        {
            if (core.Equals(phrase, StringComparison.Ordinal) || core.Equals(phrase, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (string tail in tails)
        {
            bool ends = core.EndsWith(tail, StringComparison.Ordinal) || core.EndsWith(tail, StringComparison.OrdinalIgnoreCase);
            if (!ends || core.Length <= tail.Length) continue;
            if (!char.IsWhiteSpace(core[core.Length - tail.Length - 1])) continue;
            string head = core[..(core.Length - tail.Length)].Trim();
            if (head.Length > 0 && !head.Contains(' ')) return true;
        }

        return false;
    }
}

internal readonly record struct ChatThread(
    string Name,
    string Preview,
    string AvatarUrl,
    string RelativeTime,
    string Href);
