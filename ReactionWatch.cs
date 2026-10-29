namespace MessengerDesktop;

// A title bump with no message notification inside the window is treated as
// other activity (a reaction). The first sample is only a baseline.
internal sealed class ReactionWatch
{
    private readonly TimeSpan window;
    private int? count;
    private DateTimeOffset? pendingSince;
    private DateTimeOffset lastMessage = DateTimeOffset.MinValue;

    public ReactionWatch(TimeSpan window)
    {
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        this.window = window;
    }

    public static TimeSpan DefaultWindow => TimeSpan.FromSeconds(2);

    public void NoteMessage(DateTimeOffset now)
    {
        lastMessage = now;
        pendingSince = null;
    }

    public TimeSpan? DueIn(DateTimeOffset now)
    {
        if (pendingSince is null) return null;
        TimeSpan remaining = window - (now - pendingSince.Value);
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public bool ObserveUnread(int unread, DateTimeOffset now)
    {
        if (unread < 0) unread = 0;
        if (count is null)
        {
            count = unread;
            return false;
        }

        if (unread > count.Value)
            pendingSince = now;
        else if (unread < count.Value)
            pendingSince = null;

        count = unread;
        if (pendingSince is null) return false;

        if ((pendingSince.Value - lastMessage).Duration() <= window)
        {
            pendingSince = null;
            return false;
        }

        if (now - pendingSince.Value < window) return false;

        pendingSince = null;
        return true;
    }
}
