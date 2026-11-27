namespace MessengerDesktop;

// YouTube links leave the in-app browser. Host checks only; no navigation.
internal static class ExternalLinks
{
    public static bool IsYouTube(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        return IsYouTubeHost(uri.Host);
    }

    public static bool TryGetYouTube(Uri? clicked, out Uri? youtube)
    {
        youtube = null;
        if (IsYouTube(clicked))
        {
            youtube = clicked;
            return true;
        }
        if (!IsFacebookRedirect(clicked)) return false;
        if (!XLinkCapture.TryUnwrapQuery(clicked!, out Uri? inner, httpsOnly: false) || !IsYouTube(inner))
            return false;
        youtube = inner;
        return true;
    }

    private static bool IsYouTubeHost(string host)
    {
        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)) return true;
        if (IsHostOrSubdomain(host, "youtube.com")) return true;
        return IsHostOrSubdomain(host, "youtube-nocookie.com");
    }

    private static bool IsHostOrSubdomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    private static bool IsFacebookRedirect(Uri? uri) =>
        uri is not null &&
        uri.IsAbsoluteUri &&
        uri.IsDefaultPort &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("l.facebook.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("lm.facebook.com", StringComparison.OrdinalIgnoreCase));
}
