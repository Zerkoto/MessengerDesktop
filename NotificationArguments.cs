namespace MessengerDesktop;

// Toast activation arguments use the Windows Community Toolkit format:
// semicolon-separated pairs, with % ; and = percent-encoded.
internal static class NotificationArguments
{
    public static string Format(string? url, string? tag)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(url)) parts.Add(EncodePair("url", url));
        if (!string.IsNullOrEmpty(tag)) parts.Add(EncodePair("tag", tag));
        return string.Join(";", parts);
    }

    public static (string Url, string Tag) Parse(string? argument)
    {
        string url = "";
        string tag = "";
        if (string.IsNullOrWhiteSpace(argument)) return (url, tag);
        foreach (string pair in argument.Split(';'))
        {
            int split = pair.IndexOf('=');
            string key;
            string value;
            if (split < 0)
            {
                key = Decode(pair);
                value = "";
            }
            else
            {
                key = Decode(pair[..split]);
                value = Decode(pair[(split + 1)..]);
            }

            if (key == "url") url = value;
            else if (key == "tag") tag = value;
        }

        return (url, tag);
    }

    private static string EncodePair(string key, string value) => Encode(key) + "=" + Encode(value);

    private static string Encode(string value) =>
        value.Replace("%", "%25", StringComparison.Ordinal)
            .Replace(";", "%3B", StringComparison.Ordinal)
            .Replace("=", "%3D", StringComparison.Ordinal);

    private static string Decode(string value) =>
        value.Replace("%25", "%", StringComparison.Ordinal)
            .Replace("%3B", ";", StringComparison.Ordinal)
            .Replace("%3D", "=", StringComparison.Ordinal);
}
