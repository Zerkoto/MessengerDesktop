using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MessengerDesktop;

// The on-disk copy of alert lines. One line is the time, the source, and the
// kind. The message text is not written. A file over about 1 MB is renamed
// aside the next time the app starts.
internal static class AlertLog
{
    public const long MaxBytes = 1024 * 1024;
    public const string FileName = "trace.log";
    public const string PreviousName = "trace.old.log";

    public static string Folder(string localAppData) =>
        Path.Combine(localAppData, "MessengerDesktop", "logs");

    public static void RotateIfNeeded(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (new FileInfo(path).Length <= MaxBytes) return;
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) return;
        File.Move(path, Path.Combine(directory, PreviousName), overwrite: true);
    }

    // Writes one alert line and closes the listener. The app keeps its own
    // listener for the life of the process.
    public static void WriteOne(string path, string line)
    {
        var listener = new TextWriterTraceListener(path);
        Trace.Listeners.Add(listener);
        try
        {
            Trace.WriteLine(line);
            Trace.Flush();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
            listener.Dispose();
        }
    }
}

// Badge lines share trace.log with the alert lines. A line is the count, the
// reason, or a thread id. Message text and chat names are not written.
internal static class BadgeLog
{
    public static string Badge(DateTimeOffset at, int from, int to, string reason) =>
        Stamp(at) + " badge " + from.ToString(CultureInfo.InvariantCulture)
        + " -> " + to.ToString(CultureInfo.InvariantCulture)
        + " reason=" + Token(reason);

    public static string MarkRead(DateTimeOffset at, string key, string trigger) =>
        Stamp(at) + " mark-read key=" + Token(key) + " trigger=" + Token(trigger);

    public static string Scrape(DateTimeOffset at, IEnumerable<string> keys)
    {
        var list = keys.ToList();
        return Stamp(at) + " scrape rows=" + list.Count.ToString(CultureInfo.InvariantCulture)
            + " keys=" + string.Join(",", list);
    }

    public static string RowToken(string? href, string? name)
    {
        if (ChatThreadPath.TryRead(href, out string id)) return "thread:" + id;
        string who = (name ?? "").Trim();
        return who.Length == 0 ? "chat" : "name:" + Hash(who);
    }

    public static string Token(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "chat";
        string text = value.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal).Trim();
        if (text.StartsWith("thread:", StringComparison.Ordinal) && text.IndexOf(' ') < 0) return text;
        if (text.StartsWith("name:", StringComparison.Ordinal)) return "name:" + Hash(text);
        if (text.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or ':')) return text;
        return "chat:" + Hash(text);
    }

    private static string Stamp(DateTimeOffset at) =>
        at.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK", CultureInfo.InvariantCulture);

    private static string Hash(string value)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes, 0, 4).ToLowerInvariant();
    }
}
