using System.Globalization;
using System.Text.RegularExpressions;

namespace MessengerDesktop;

internal static class UnreadTitle
{
    public const int TooltipLimit = 127;
    private static readonly Regex Count = new(@"\((\d+)\+?\)", RegexOptions.CultureInvariant);

    public static int Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return 0;
        Match match = Count.Match(title);
        if (!match.Success) return 0;
        return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            ? count
            : 0;
    }

    public static string Tooltip(int count)
    {
        string text;
        if (count <= 0) text = "Messenger";
        else if (count == 1) text = "Messenger (1 unread)";
        else text = $"Messenger ({count} unread)";
        return text.Length <= TooltipLimit ? text : text[..TooltipLimit];
    }
}
