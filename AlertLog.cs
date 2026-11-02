using System.Diagnostics;

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
