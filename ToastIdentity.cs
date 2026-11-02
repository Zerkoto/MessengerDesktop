using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MessengerDesktop;

// One AppUserModelID for the process, the toast notifier, and Action Center.
// The toolkit otherwise asks the shell for a path-based id and rewrites
// backslashes to slashes, which files toasts under a second group from the
// process and from any shortcut that launched the exe.
internal static class ToastIdentity
{
    public const string AppUserModelId = "MessengerDesktop.Messenger";
    public const string DisplayName = "Messenger";

    public static string IconUri(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return "";
        string path = exePath.Trim();
        try
        {
            if (!File.Exists(path)) return "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "";
        }

        return path + ",0";
    }

    public static void AssignProcess()
    {
        try { SetCurrentProcessExplicitAppUserModelID(AppUserModelId); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The id is still published for the notifier below.
        }
    }

    public static void Publish(string? exePath)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppUserModelId);
            if (key is null) return;
            key.SetValue("DisplayName", DisplayName);
            string icon = IconUri(exePath);
            if (icon.Length > 0) key.SetValue("IconUri", icon);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A toast can still show. The group icon is best-effort.
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
