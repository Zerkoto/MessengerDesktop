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

    public static bool TryShow(NotificationToastContent content)
    {
        if (!NotificationToast.CanShow(content)) return false;
        try
        {
            var builder = new ToastContentBuilder();
            builder.AddText(content.Title);
            builder.AddText(content.Body);
            if (!string.IsNullOrEmpty(content.ImagePath) && File.Exists(content.ImagePath))
                builder.AddAppLogoOverride(new Uri(Path.GetFullPath(content.ImagePath)), ToastGenericAppLogoCrop.Circle, content.Title);
            if (!string.IsNullOrEmpty(content.Url)) builder.AddArgument("url", content.Url);
            if (!string.IsNullOrEmpty(content.Tag)) builder.AddArgument("tag", content.Tag);
            builder.Show(toast =>
            {
                // Tag plus group is the Action Center identity. A message uses a
                // different pair from a reaction, so the message does not replace it.
                if (content.ToastTag.Length > 0) toast.Tag = content.ToastTag;
                if (content.ToastGroup.Length > 0) toast.Group = content.ToastGroup;
            });
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return false;
        }
    }
}
