using System.Media;

namespace MessengerDesktop;

// The page's own sound does not play while the WebView is hidden. The host
// plays this when it raises the toast itself.
internal static class NotificationSound
{
    public static void Play()
    {
        try { SystemSounds.Asterisk.Play(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A machine with no sound device still gets the toast.
        }
    }
}
