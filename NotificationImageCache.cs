using System.Security.Cryptography;
using System.Text;

namespace MessengerDesktop;

// Sender pictures are remote http(s) URLs. Windows toasts want a local file.
internal static class NotificationImageCache
{
    public const int MaxBytes = 2 * 1024 * 1024;

    private static readonly HttpClient Shared = CreateClient();

    public static string DefaultDirectory =>
        Path.Combine(Path.GetTempPath(), "MessengerDesktop", "notification-images");

    public static async Task<string?> DownloadAsync(
        string? uri,
        string directory,
        HttpMessageHandler? handler = null,
        CancellationToken cancellationToken = default)
    {
        if (!NotificationToast.IsFetchable(uri)) return null;
        var parsed = new Uri(uri!.Trim());
        HttpClient? owned = null;
        HttpClient client = Shared;
        if (handler is not null)
        {
            owned = CreateClient(handler);
            client = owned;
        }

        try
        {
            using var response = await client.GetAsync(parsed, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            string? media = response.Content.Headers.ContentType?.MediaType;
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            while (true)
            {
                int read = await stream.ReadAsync(chunk, cancellationToken);
                if (read == 0) break;
                if (buffer.Length + read > MaxBytes) return null;
                buffer.Write(chunk, 0, read);
            }

            return TryWrite(buffer.ToArray(), media, parsed, directory);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    public static string? TryWrite(byte[] data, string? mediaType, Uri source, string directory)
    {
        if (data.Length == 0 || data.Length > MaxBytes) return null;
        if (!IsImage(data, mediaType)) return null;
        Directory.CreateDirectory(directory);
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.AbsoluteUri)))
            + ExtensionFor(data, mediaType, source);
        string path = Path.Combine(directory, name);
        if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, data);
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    private static HttpClient CreateClient(HttpMessageHandler? handler = null)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(2);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MessengerDesktop");
        return client;
    }

    private static bool IsImage(byte[] data, string? mediaType)
    {
        if (mediaType is not null && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && !LooksLikeText(data))
            return true;
        return LooksLikeImage(data);
    }

    private static bool LooksLikeText(byte[] data)
    {
        int count = Math.Min(data.Length, 16);
        for (int i = 0; i < count; i++)
        {
            if (data[i] == 0) return false;
        }

        if (data.Length >= 1 && data[0] == (byte)'<') return true;
        return false;
    }

    private static bool LooksLikeImage(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true;
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true;
        if (data.Length >= 6 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F') return true;
        if (data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            return true;
        return false;
    }

    private static string ExtensionFor(byte[] data, string? mediaType, Uri source)
    {
        if (LooksLikeImage(data))
        {
            if (data[0] == 0x89) return ".png";
            if (data[0] == 0xFF) return ".jpg";
            if (data[0] == (byte)'G') return ".gif";
            if (data[0] == (byte)'R') return ".webp";
        }

        if (mediaType is not null)
        {
            if (mediaType.Contains("png", StringComparison.OrdinalIgnoreCase)) return ".png";
            if (mediaType.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("jpg", StringComparison.OrdinalIgnoreCase))
                return ".jpg";
            if (mediaType.Contains("gif", StringComparison.OrdinalIgnoreCase)) return ".gif";
            if (mediaType.Contains("webp", StringComparison.OrdinalIgnoreCase)) return ".webp";
        }

        string path = source.AbsolutePath;
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return ".png";
        if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            return ".jpg";
        if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return ".gif";
        if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return ".webp";
        return ".png";
    }
}
