using System.Drawing.Imaging;

namespace MessengerDesktop;

internal static class AppIconCache
{
    public static string TryCreate(string exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return "";
            string directory = NotificationImageCache.DefaultDirectory;
            Directory.CreateDirectory(directory);
            string stamp = File.GetLastWriteTimeUtc(exePath).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, "app-icon-" + stamp + ".png");
            if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
            using Icon? icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) return "";
            using Bitmap bitmap = icon.ToBitmap();
            bitmap.Save(path, ImageFormat.Png);
            return path;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return "";
        }
    }
}
