namespace MessengerDesktop;

// Retains the chat only for the observed chat -> optional share link -> reel flow.
internal sealed class ReelReturnTracker
{
    private Uri? chat;
    private bool previousWasChat;
    private bool previousWasShare;
    private bool previousWasReel;
    private bool pendingShare;

    public bool CanReturn { get; private set; }

    public static bool IsFacebook(Uri? uri) => uri is not null &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("www.facebook.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort;

    public static bool IsReel(Uri? uri) => IsFacebook(uri) && uri!.AbsolutePath.StartsWith("/reel/", StringComparison.Ordinal);

    public static bool IsChat(Uri? uri) => IsFacebook(uri) &&
        (uri!.AbsolutePath.StartsWith("/messages/t/", StringComparison.Ordinal) ||
         uri.AbsolutePath.StartsWith("/messages/e2ee/t/", StringComparison.Ordinal));

    public static bool IsShare(Uri? uri) => IsFacebook(uri) && uri!.AbsolutePath.StartsWith("/share/r/", StringComparison.Ordinal);

    public void Observe(Uri? current)
    {
        bool isChat = IsChat(current);
        bool isShare = IsShare(current);
        bool isReel = IsReel(current);
        if (isChat)
        {
            chat = current;
            pendingShare = false;
            CanReturn = false;
        }
        else if (isShare)
        {
            pendingShare = chat is not null && (previousWasChat || (previousWasShare && pendingShare));
            CanReturn = false;
        }
        else if (isReel)
        {
            CanReturn = chat is not null && (previousWasChat || (previousWasShare && pendingShare) || (previousWasReel && CanReturn));
            pendingShare = false;
        }
        else
        {
            chat = null;
            pendingShare = false;
            CanReturn = false;
        }
        previousWasChat = isChat;
        previousWasShare = isShare;
        previousWasReel = isReel;
    }

    public bool TryTakeReturn(out Uri? destination)
    {
        destination = CanReturn ? chat : null;
        CanReturn = false;
        return destination is not null;
    }
}
