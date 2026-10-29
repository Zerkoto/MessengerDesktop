namespace MessengerDesktop;

internal enum ServiceSurface
{
    Tab,
    Overlay
}

internal enum LinkMatch
{
    Prefix,
    Status
}

// A host plus either a path prefix (Facebook) or a status path (X).
internal sealed class LinkRule
{
    private LinkRule(string host, string pathPrefix, LinkMatch match)
    {
        Host = host;
        PathPrefix = pathPrefix;
        Match = match;
    }

    public LinkRule(string host, string pathPrefix)
        : this(CheckedHost(host), CheckedPrefix(pathPrefix), LinkMatch.Prefix)
    {
    }

    public static LinkRule Status(string host) => new(CheckedHost(host), "/", LinkMatch.Status);

    public string Host { get; }
    public string PathPrefix { get; }
    public LinkMatch Match { get; }

    public bool Matches(Uri? uri)
    {
        if (!IsHttpsOnHost(uri)) return false;
        if (Match == LinkMatch.Status) return XLinkCapture.IsStatusPath(uri!.AbsolutePath);
        return uri!.AbsolutePath.StartsWith(PathPrefix, StringComparison.Ordinal);
    }

    private bool IsHttpsOnHost(Uri? uri) =>
        uri is not null &&
        uri.IsAbsoluteUri &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort &&
        uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase);

    private static string CheckedHost(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        return host;
    }

    private static string CheckedPrefix(string pathPrefix)
    {
        ArgumentNullException.ThrowIfNull(pathPrefix);
        if (!pathPrefix.StartsWith('/'))
            throw new ArgumentException("A link rule path starts with '/'.", nameof(pathPrefix));
        return pathPrefix;
    }
}

internal sealed class ServiceDefinition
{
    public ServiceDefinition(
        string name,
        string profileName,
        Uri home,
        ServiceSurface surface,
        IReadOnlyList<LinkRule> linkRules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(linkRules);
        if (!home.IsAbsoluteUri) throw new ArgumentException("The home URL is absolute.", nameof(home));
        Name = name;
        ProfileName = profileName;
        Home = home;
        Surface = surface;
        LinkRules = linkRules;
    }

    public string Name { get; }
    public string ProfileName { get; }
    public Uri Home { get; }
    public ServiceSurface Surface { get; }
    public IReadOnlyList<LinkRule> LinkRules { get; }

    public bool Matches(Uri? uri)
    {
        if (uri is null) return false;
        foreach (LinkRule rule in LinkRules)
        {
            if (rule.Match == LinkMatch.Status) return XLinkCapture.TryCapture(uri, out _);
        }
        foreach (LinkRule rule in LinkRules)
        {
            if (rule.Matches(uri)) return true;
        }
        return false;
    }

    // Same parent directory the app already uses, plus this service's profile folder.
    public string ProfileFolder(string localApplicationData) =>
        Path.Combine(localApplicationData, "MessengerDesktop", ProfileName);
}

internal static class ServiceCatalog
{
    public static ServiceDefinition Facebook { get; } = new(
        "facebook",
        "WebView2",
        new Uri("https://www.facebook.com/messages/t"),
        ServiceSurface.Tab,
        [
            new LinkRule("www.facebook.com", "/reel/"),
            new LinkRule("www.facebook.com", "/share/r/"),
            new LinkRule("www.facebook.com", "/messages/t/"),
            new LinkRule("www.facebook.com", "/messages/e2ee/t/"),
        ]);

    public static ServiceDefinition X { get; } = new(
        "x",
        "WebView2-X",
        new Uri("https://x.com/"),
        ServiceSurface.Overlay,
        [
            LinkRule.Status("x.com"),
            LinkRule.Status("www.x.com"),
            LinkRule.Status("mobile.x.com"),
            LinkRule.Status("twitter.com"),
            LinkRule.Status("www.twitter.com"),
            LinkRule.Status("mobile.twitter.com"),
            LinkRule.Status("m.twitter.com"),
        ]);
}
