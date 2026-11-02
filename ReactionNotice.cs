using System.Globalization;

namespace MessengerDesktop;

// A reaction toast names the person, the emoji, and the message they reacted to.
// The body falls back when either the emoji or the snippet is missing.
internal readonly record struct ReactionToast(string Title, string Body, string Url);

internal static class ReactionNotice
{
    public const string FallbackBody = "Reacted to your message";

    private static readonly string[] BulgarianTargets =
    [
        "съобщението ви",
        "съобщението Ви",
        "съобщението ти",
        "вашето съобщение"
    ];

    public static bool TryParse(string? nameHint, string? text, string? url, out ReactionToast toast)
    {
        toast = default;
        string hint = ChatListSnippet.Collapse(nameHint ?? "");
        string body = ChatListSnippet.Collapse(text ?? "");
        if (!TryRead(body, out string name, out string? emoji, out string? snippet)
            && !TryRead(hint, out name, out emoji, out snippet)
            && !(hint.Length > 0 && body.Length > 0 && TryRead(hint + " " + body, out name, out emoji, out snippet)))
            return false;

        if (string.IsNullOrWhiteSpace(snippet) && !string.IsNullOrWhiteSpace(body) && !LooksLikeReaction(body) && LooksLikeReaction(hint))
            snippet = body;
        if (string.IsNullOrWhiteSpace(name))
            name = LooksLikeReaction(hint) ? "" : hint;

        toast = new ReactionToast(
            string.IsNullOrWhiteSpace(name) ? "Messenger" : name.Trim(),
            FormatBody(emoji, snippet),
            string.IsNullOrWhiteSpace(url) ? "" : url.Trim());
        return true;
    }

    public static string FormatBody(string? emoji, string? snippet)
    {
        string face = (emoji ?? "").Trim();
        string line = ChatListSnippet.Collapse(snippet ?? "");
        if (face.Length == 0 || line.Length == 0) return FallbackBody;
        string body = "Reacted " + face + " to: " + line;
        return body.Length <= 120 ? body : body[..117].TrimEnd() + "…";
    }

    private static bool LooksLikeReaction(string text) =>
        text.Contains("reacted", StringComparison.OrdinalIgnoreCase)
        || text.Contains("реагира", StringComparison.Ordinal)
        || text.Contains("Реагира", StringComparison.Ordinal);

    private static bool TryRead(string text, out string name, out string? emoji, out string? snippet)
    {
        name = "";
        emoji = null;
        snippet = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        int english = FindWord(text, "reacted");
        int bulgarian = FindWord(text, "реагира");
        if (bulgarian < 0) bulgarian = FindWord(text, "Реагира");
        if (english >= 0 && (bulgarian < 0 || english <= bulgarian))
            return ReadEnglish(text, english, out name, out emoji, out snippet);
        if (bulgarian >= 0)
            return ReadBulgarian(text, bulgarian, out name, out emoji, out snippet);
        return false;
    }

    private static bool ReadEnglish(string text, int at, out string name, out string? emoji, out string? snippet)
    {
        name = text[..at].Trim();
        string rest = text[(at + "reacted".Length)..].Trim();
        emoji = null;
        snippet = null;
        if (rest.StartsWith("with ", StringComparison.OrdinalIgnoreCase))
            rest = rest[5..].Trim();
        emoji = TakeEmoji(ref rest);
        if (rest.StartsWith(':'))
        {
            snippet = CleanSnippet(rest[1..]);
            return true;
        }

        if (!StartsWord(rest, "to")) return false;
        rest = rest[2..].Trim();
        if (rest.StartsWith(':'))
        {
            snippet = CleanSnippet(rest[1..]);
            return true;
        }

        if (!StartsWord(rest, "your message")) return false;
        snippet = CleanSnippet(rest["your message".Length..]);
        return true;
    }

    private static bool ReadBulgarian(string text, int at, out string name, out string? emoji, out string? snippet)
    {
        name = text[..at].Trim();
        string rest = text[(at + "реагира".Length)..].Trim();
        emoji = null;
        snippet = null;
        if (rest.StartsWith("с ", StringComparison.Ordinal) || rest.StartsWith("с\u00A0", StringComparison.Ordinal))
            rest = rest[2..].Trim();
        emoji = TakeEmoji(ref rest);
        if (rest.StartsWith(':'))
        {
            snippet = CleanSnippet(rest[1..]);
            return true;
        }

        if (!StartsWord(rest, "на")) return false;
        rest = rest[2..].Trim();
        if (rest.StartsWith(':'))
        {
            snippet = CleanSnippet(rest[1..]);
            return true;
        }

        foreach (string target in BulgarianTargets)
        {
            if (!rest.StartsWith(target, StringComparison.Ordinal)) continue;
            snippet = CleanSnippet(rest[target.Length..]);
            return true;
        }

        return false;
    }

    private static string? TakeEmoji(ref string rest)
    {
        if (rest.Length == 0) return null;
        int space = rest.IndexOf(' ');
        string token = space < 0 ? rest : rest[..space];
        if (!IsEmoji(token)) return null;
        rest = (space < 0 ? "" : rest[(space + 1)..]).Trim();
        return token;
    }

    private static bool IsEmoji(string token)
    {
        if (token.Length == 0) return false;
        if (token.Equals("to", StringComparison.OrdinalIgnoreCase)
            || token.Equals("with", StringComparison.OrdinalIgnoreCase)
            || token.Equals("your", StringComparison.OrdinalIgnoreCase)
            || token.Equals("на", StringComparison.Ordinal)
            || token.Equals("с", StringComparison.Ordinal))
            return false;
        foreach (char character in token)
        {
            if (char.IsSurrogate(character) || character is '\u2764' or '\uFE0F' or '\u200D' or '\u2665')
                return true;
            UnicodeCategory category = char.GetUnicodeCategory(character);
            if (category is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol)
                return true;
        }

        return false;
    }

    private static string? CleanSnippet(string value)
    {
        string text = ChatListSnippet.Collapse(value).Trim(' ', '.', '!', '…', ':', '"', '\'', '“', '”', '„', '«', '»');
        text = ChatListSnippet.Collapse(text);
        return text.Length == 0 ? null : text;
    }

    private static bool StartsWord(string text, string word)
    {
        if (!text.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return false;
        if (text.Length == word.Length) return true;
        char next = text[word.Length];
        return !char.IsLetter(next);
    }

    private static int FindWord(string text, string word)
    {
        StringComparison comparison = word.Any(character => character > 127)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        int start = 0;
        while (start < text.Length)
        {
            int at = text.IndexOf(word, start, comparison);
            if (at < 0) return -1;
            bool left = at == 0 || !char.IsLetter(text[at - 1]);
            int end = at + word.Length;
            bool right = end >= text.Length || !char.IsLetter(text[end]);
            if (left && right) return at;
            start = at + word.Length;
        }

        return -1;
    }
}
