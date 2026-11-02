using System.Security.Cryptography;
using System.Text;

namespace MessengerDesktop;

// Action Center replaces a toast when the next one uses the same tag and group.
// A message is "msg" plus the thread. A reaction adds the captured body, so two
// reactions and a later message are three different toasts.
internal static class ToastTags
{
    public const string MessageGroup = "msg";
    public const string ReactionGroup = "reaction";
    public const int MaxLength = 64;

    public static string Message(string? thread) => Fit("msg:" + Token(thread));

    // The raw text is part of the identity so two reactions that share a fallback
    // body still get two tags. The same text maps to the same tag.
    public static string ReactionIdentity(string? raw, string? shown) =>
        (raw ?? "").Trim() + "\n" + (shown ?? "").Trim();

    public static string Reaction(string? thread, string? identity) =>
        Fit("rx:" + Token(thread) + ":" + Hash(identity ?? ""));

    private static string Token(string? value)
    {
        string text = string.IsNullOrWhiteSpace(value) ? "chat" : value.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
            text = uri.AbsolutePath;
        int cut = text.IndexOfAny(['?', '#']);
        if (cut >= 0) text = text[..cut];
        text = text.Trim('/');
        int slash = text.LastIndexOf('/');
        if (slash >= 0 && slash < text.Length - 1) text = text[(slash + 1)..];
        if (text.Length == 0) text = "chat";
        return text.Length <= 24 ? text : Hash(text);
    }

    private static string Fit(string tag) => tag.Length <= MaxLength ? tag : Hash(tag);

    private static string Hash(string value)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16];
    }
}
