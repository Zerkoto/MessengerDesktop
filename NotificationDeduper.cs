namespace MessengerDesktop;

// An empty tag is not an identity. Only a real tag can suppress a second toast.
internal sealed class NotificationDeduper
{
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);

    public bool ShouldShow(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return true;
        return seen.Add(tag);
    }
}
