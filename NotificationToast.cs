using System.Security.Cryptography;
using System.Text;

namespace MessengerDesktop;

// What a Windows toast shows. Title and body are the tray's text.
// ToastTag and ToastGroup are the Action Center identity. A message and a
// reaction never share one, so a later message does not replace the reaction.
internal sealed record NotificationToastContent(
    string Title,
    string Body,
    string ImagePath,
    string Url,
    string Tag,
    string ToastTag = "",
    string ToastGroup = "");

internal static class NotificationToast
{
    public const int MaxBodyLength = 120;

    public static string? PreferredImageUri(string? iconUri, string? badgeUri)
    {
        if (IsFetchable(iconUri)) return iconUri!.Trim();
        if (IsFetchable(badgeUri)) return badgeUri!.Trim();
        return null;
    }

    public static bool IsFetchable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string text = value.Trim();
        return text.Equals("New activity", StringComparison.OrdinalIgnoreCase)
            || text.Equals("New notification", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanShow(NotificationToastContent toast) =>
        !string.IsNullOrWhiteSpace(toast.Title)
        && !string.IsNullOrWhiteSpace(toast.Body)
        && !IsPlaceholder(toast.Title)
        && !IsPlaceholder(toast.Body);

    public static NotificationToastContent Map(
        string? title,
        string? body,
        string? downloadedImagePath,
        string? appIconPath,
        string? url,
        string? tag,
        bool reaction = false)
    {
        string cleanUrl = string.IsNullOrWhiteSpace(url) ? "" : url.Trim();
        string navTag = string.IsNullOrWhiteSpace(tag) ? "" : tag.Trim();
        bool asReaction = reaction || ReactionNotice.TryParse(title, body, cleanUrl, out _);
        string shownTitle;
        string shownBody;
        if (asReaction)
        {
            if (!ReactionNotice.TryParse(title, body, cleanUrl, out ReactionToast toast))
                toast = new ReactionToast(Sender(title), ReactionNotice.FallbackBody, cleanUrl);
            shownTitle = Sender(toast.Title);
            shownBody = string.IsNullOrWhiteSpace(toast.Body) || IsPlaceholder(toast.Body)
                ? ReactionNotice.FallbackBody
                : toast.Body.Trim();
            if (!string.IsNullOrWhiteSpace(toast.Url)) cleanUrl = toast.Url.Trim();
        }
        else
        {
            shownTitle = Sender(title);
            shownBody = ChatListSnippet.Resolve(body, null);
            if (string.IsNullOrWhiteSpace(shownBody) || IsPlaceholder(shownBody))
                shownBody = ChatListSnippet.NewMessage;
        }

        shownBody = TrimBody(shownBody);
        if (string.IsNullOrWhiteSpace(shownBody))
            shownBody = asReaction ? ReactionNotice.FallbackBody : ChatListSnippet.NewMessage;
        if (IsPlaceholder(shownTitle) || string.IsNullOrWhiteSpace(shownTitle))
            shownTitle = "Messenger";

        string image = ExistingFile(downloadedImagePath);
        if (image.Length == 0) image = ExistingFile(appIconPath);
        string thread = cleanUrl.Length > 0 ? cleanUrl : navTag.Length > 0 ? navTag : shownTitle;
        string toastTag = asReaction
            ? ToastTags.Reaction(thread, ToastTags.ReactionIdentity(body, shownBody))
            : ToastTags.Message(thread);
        string toastGroup = asReaction ? ToastTags.ReactionGroup : ToastTags.MessageGroup;
        return new NotificationToastContent(shownTitle, shownBody, image, cleanUrl, navTag, toastTag, toastGroup);
    }

    private static string Sender(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || IsPlaceholder(title)) return "Messenger";
        return title.Trim();
    }

    public static string TrimBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        string trimmed = body.Trim().Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
        if (trimmed.Length <= MaxBodyLength) return trimmed;
        return trimmed[..MaxBodyLength].TrimEnd();
    }

    public static string Describe(NotificationToastContent toast)
    {
        return "toast title='" + OneLine(toast.Title) + "' body='" + OneLine(toast.Body) + "' image='" + toast.ImagePath + "'";
    }

    private static string ExistingFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            return File.Exists(path) ? path : "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static string OneLine(string value) =>
        value.Replace("'", "\\'", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}

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
