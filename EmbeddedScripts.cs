using System.Reflection;
using System.Text;

namespace MessengerDesktop;

// Page scripts moved out of Program.cs. The text WebView2 receives is the
// embedded file, with newlines normalized so a CRLF checkout cannot change it.
internal static class EmbeddedScripts
{
    // Keep the conversation alive when a reel link is followed from a chat.
    public static string ChatReel { get; } = Read("MessengerDesktop.Scripts.ChatReel.js");

    // Optional. The host adds this only while chat-list watching is on, and only in the Facebook chat WebView.
    public static string ChatListWatch { get; } = Read("MessengerDesktop.Scripts.ChatListWatch.js");

    // Facebook's reel viewer links back to the feed rather than the chat.
    // Only intercept that specific close link when the host has a chat to restore.
    public static string ReelClose { get; } = Read("MessengerDesktop.Scripts.ReelClose.js");

    // Facebook reserves space for its navigation even though the reel viewer
    // has its own close control. Scope this to the separate reel WebView.
    public static string ReelLayout { get; } = Read("MessengerDesktop.Scripts.ReelLayout.js");

    private static string Read(string name)
    {
        using Stream stream = typeof(EmbeddedScripts).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded script '{name}'.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    }
}
