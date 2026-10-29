using Microsoft.Toolkit.Uwp.Notifications;

namespace MessengerDesktop;

// OS toasts go through the Windows notification platform, so Focus Assist
// hides the popup. A tray balloon is used only when that platform fails.
internal static class OsToast
{
    public static bool TryEnable(ToastBridge bridge)
    {
        try
        {
            ToastNotificationManagerCompat.OnActivated += args => bridge.Publish(args.Argument);
            _ = ToastNotificationManagerCompat.CreateToastNotifier();
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return false;
        }
    }

    public static bool TryShow(string title, string? body, string? url, string? tag)
    {
        try
        {
            var builder = new ToastContentBuilder();
            builder.AddText(string.IsNullOrWhiteSpace(title) ? "Messenger" : title);
            if (!string.IsNullOrWhiteSpace(body)) builder.AddText(body);
            if (!string.IsNullOrEmpty(url)) builder.AddArgument("url", url);
            if (!string.IsNullOrEmpty(tag)) builder.AddArgument("tag", tag);
            builder.Show();
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return false;
        }
    }
}
