namespace MessengerDesktop;

internal sealed class NotificationPipeline
{
    private readonly NotificationDeduper deduper = new();
    private readonly ReactionWatch reactions;

    public NotificationPipeline(TimeSpan debounce)
    {
        reactions = new ReactionWatch(debounce);
    }

    public bool ShouldToastMessage(NotificationRecord message, DateTimeOffset now)
    {
        reactions.NoteMessage(now);
        return deduper.ShouldShow(message.Tag);
    }

    public bool ShouldToastActivity(int unread, DateTimeOffset now) =>
        reactions.ObserveUnread(unread, now);

    public TimeSpan? ActivityDueIn(DateTimeOffset now) => reactions.DueIn(now);
}
