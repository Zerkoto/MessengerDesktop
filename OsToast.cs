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

    public static bool TryShow(string title, string? body, string? url, string? tag, string? imagePath = null)
    {
        try
        {
            string shownTitle = string.IsNullOrWhiteSpace(title) ? "Messenger" : title;
            var builder = new ToastContentBuilder();
            builder.AddText(shownTitle);
            if (!string.IsNullOrWhiteSpace(body)) builder.AddText(body);
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                builder.AddAppLogoOverride(new Uri(Path.GetFullPath(imagePath)), ToastGenericAppLogoCrop.Circle, shownTitle);
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
