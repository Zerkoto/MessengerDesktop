namespace MessengerDesktop;

internal sealed class NotificationPipeline
{
    private readonly NotificationDeduper deduper = new();
    private readonly NotificationEcho echo = new();
    private readonly ReactionWatch reactions;
    private readonly TimeSpan window;

    public NotificationPipeline(TimeSpan debounce)
    {
        window = debounce;
        reactions = new ReactionWatch(debounce);
    }

    public bool ShouldToastMessage(NotificationRecord message, DateTimeOffset now)
    {
        // Note the message before the dedupe decision. A repeat still means a
        // real message arrived, so "New activity" stays suppressed.
        reactions.NoteMessage(now);
        if (!deduper.ShouldShow(message.Tag)) return false;
        if (string.IsNullOrWhiteSpace(message.Tag) && !echo.ShouldShow(message.Title, message.Body, now, window))
            return false;
        return true;
    }

    public bool ShouldToastActivity(int unread, DateTimeOffset now) =>
        reactions.ObserveUnread(unread, now);

    public TimeSpan? ActivityDueIn(DateTimeOffset now) => reactions.DueIn(now);
}

// The page shim and NotificationReceived can both deliver one untagged message.
// The same title and body inside the reaction window is one toast.
internal sealed class NotificationEcho
{
    private readonly Queue<(string Key, DateTimeOffset At)> recent = new();

    public bool ShouldShow(string title, string body, DateTimeOffset now, TimeSpan window)
    {
        while (recent.Count > 0 && now - recent.Peek().At > window) recent.Dequeue();
        string key = title + "\n" + body;
        foreach ((string seen, _) in recent)
        {
            if (seen == key) return false;
        }

        recent.Enqueue((key, now));
        return true;
    }
}
