namespace MessengerDesktop;

// External http(s) links leave the in-app browser. Host checks only; no navigation.
internal static class ExternalLinks
{
    // Parsed host equals the domain or a subdomain of it. l.facebook.com and
    // lm.facebook.com are facebook.com, so a shim is unwrapped before this list.
    private static readonly string[] InAppHosts =
    [
        "facebook.com",
        "messenger.com",
        "fb.com",
        "fb.me",
        "fbcdn.net",
        "fbsbx.com",
        "facebook.net",
        "instagram.com",
        "cdninstagram.com",
        "x.com",
        "twitter.com",
        "t.co",
        "twimg.com"
    ];

    // The repo had no auth allowlist. X's Google and Apple buttons navigate the
    // overlay itself to these hosts, so they stay there and do not leave the app.
    private static readonly string[] SignInHosts =
    [
        "accounts.google.com",
        "appleid.apple.com"
    ];

    public static bool ShouldOpenExternally(Uri? uri) => TryGetExternal(uri, out _);

    public static bool TryGetExternal(Uri? clicked, out Uri? external) =>
        TryGetExternal(clicked, out external, signInStaysInApp: false);

    public static bool TryGetExternal(Uri? clicked, out Uri? external, bool signInStaysInApp)
    {
        external = null;
        if (!TryResolve(clicked, out Uri? target) || target is null) return false;
        if (!IsHttp(target)) return false;
        if (IsListedHost(target.Host, InAppHosts)) return false;
        if (signInStaysInApp && IsListedHost(target.Host, SignInHosts)) return false;
        external = target;
        return true;
    }

    private static bool TryResolve(Uri? clicked, out Uri? target)
    {
        target = null;
        if (clicked is null || !clicked.IsAbsoluteUri) return false;
        if (!IsFacebookRedirect(clicked))
        {
            target = clicked;
            return true;
        }
        return XLinkCapture.TryUnwrapQuery(clicked, out target, httpsOnly: false) && target is not null;
    }

    private static bool IsFacebookRedirect(Uri uri) =>
        uri.IsDefaultPort &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("l.facebook.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("lm.facebook.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsHttp(Uri uri) =>
        uri.IsAbsoluteUri &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsListedHost(string host, string[] domains)
    {
        foreach (string domain in domains)
        {
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
