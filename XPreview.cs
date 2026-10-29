using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MessengerDesktop;

// A plain-text card for one public status. The oEmbed HTML never leaves this process.
internal sealed class XPreviewCard
{
    public XPreviewCard(string author, string handle, string text)
    {
        Author = author;
        Handle = handle;
        Text = text;
    }

    public string Author { get; }
    public string Handle { get; }
    public string Text { get; }
}

// Builds https://publish.twitter.com/oembed?url=...&omit_script=1 for a status link only.
internal static class XPreviewEndpoint
{
    public const string Host = "publish.twitter.com";

    public static bool TryCreate(Uri? clicked, out Uri? request, out Uri? status)
    {
        request = null;
        status = null;
        if (!XLinkCapture.TryCapture(clicked, out status) || status is null) return false;
        string built = "https://" + Host + "/oembed?url=" + Uri.EscapeDataString(status.AbsoluteUri) + "&omit_script=1";
        if (!Uri.TryCreate(built, UriKind.Absolute, out Uri? endpoint) || !IsPublishHost(endpoint)) return false;
        request = endpoint;
        return true;
    }

    internal static bool IsPublishHost(Uri? uri) =>
        uri is not null &&
        uri.IsAbsoluteUri &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 &&
        (uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("publish.x.com", StringComparison.OrdinalIgnoreCase));
}

internal static class XPreviewParser
{
    public const int MaxAuthorLength = 80;
    public const int MaxTextLength = 500;

    public static bool TryParse(string? json, out XPreviewCard? card)
    {
        card = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 100_000) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("url", out JsonElement urlElement) || urlElement.ValueKind != JsonValueKind.String)
                return false;
            if (!Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out Uri? status) ||
                !XLinkCapture.TryCapture(status, out _))
                return false;
            if (root.TryGetProperty("type", out JsonElement type) &&
                type.ValueKind == JsonValueKind.String &&
                type.GetString() != "rich")
                return false;
            if (!TryHandle(root, out string handle)) return false;

            string author = XPreviewHtml.OneLine(StringOrEmpty(root, "author_name"));
            if (author.Length == 0) author = handle;
            if (author.Length > MaxAuthorLength) author = author[..MaxAuthorLength].Trim();
            if (author.Length == 0) return false;

            string text = "";
            if (root.TryGetProperty("html", out JsonElement html) && html.ValueKind == JsonValueKind.String)
                text = XPreviewHtml.PostText(html.GetString());
            if (text.Length > MaxTextLength)
                text = text[..MaxTextLength].TrimEnd() + "…";

            card = new XPreviewCard(author, handle, text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryHandle(JsonElement root, out string handle)
    {
        handle = "";
        if (!root.TryGetProperty("author_url", out JsonElement authorUrl) || authorUrl.ValueKind != JsonValueKind.String)
            return false;
        if (!Uri.TryCreate(authorUrl.GetString(), UriKind.Absolute, out Uri? profile) ||
            profile.Scheme != Uri.UriSchemeHttps ||
            !profile.IsDefaultPort ||
            profile.UserInfo.Length != 0 ||
            !IsXHost(profile.Host))
            return false;
        string[] parts = profile.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 1 || !IsHandle(parts[0])) return false;
        handle = parts[0];
        return true;
    }

    private static bool IsXHost(string host)
    {
        foreach (LinkRule rule in ServiceCatalog.X.LinkRules)
        {
            if (rule.Host.Equals(host, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsHandle(string value)
    {
        if (value.Length is < 1 or > 15) return false;
        foreach (char c in value)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
                return false;
        }
        return true;
    }

    private static string StringOrEmpty(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}

// Turns the oEmbed html field into visible characters. Tags are dropped, never returned.
internal static class XPreviewHtml
{
    public static string PostText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var paragraphs = new List<string>();
        var current = new StringBuilder();
        bool inParagraph = false;
        Scan(html, (kind, text) =>
        {
            if (kind == HtmlPiece.ParagraphStart)
            {
                inParagraph = true;
                current.Clear();
            }
            else if (kind == HtmlPiece.ParagraphEnd && inParagraph)
            {
                paragraphs.Add(current.ToString());
                inParagraph = false;
                current.Clear();
            }
            else if (kind == HtmlPiece.Break && inParagraph)
            {
                current.Append('\n');
            }
            else if (kind == HtmlPiece.Text && inParagraph)
            {
                current.Append(text);
            }
        });
        if (paragraphs.Count == 0) return "";
        return Normalize(WebUtility.HtmlDecode(string.Join("\n\n", paragraphs)), keepNewlines: true);
    }

    public static string OneLine(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var text = new StringBuilder();
        Scan(value, (kind, piece) =>
        {
            if (kind == HtmlPiece.Text) text.Append(piece);
            else if (kind == HtmlPiece.Break) text.Append(' ');
        });
        return Normalize(WebUtility.HtmlDecode(text.ToString()), keepNewlines: false);
    }

    private static void Scan(string html, Action<HtmlPiece, string> emit)
    {
        int limit = Math.Min(html.Length, 100_000);
        bool skip = false;
        string skipTag = "";
        for (int i = 0; i < limit; i++)
        {
            if (html[i] != '<')
            {
                if (!skip) emit(HtmlPiece.Text, html[i].ToString());
                continue;
            }
            if (i + 3 < limit && html[i + 1] == '!' && html[i + 2] == '-' && html[i + 3] == '-')
            {
                int end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (end < 0) return;
                i = end + 2;
                continue;
            }
            char next = i + 1 < limit ? html[i + 1] : '\0';
            if (next != '/' && next != '!' && !char.IsAsciiLetter(next))
            {
                if (!skip) emit(HtmlPiece.Text, "<");
                continue;
            }
            int close = html.IndexOf('>', i + 1);
            if (close < 0 || close >= limit) return;
            ReadOnlySpan<char> raw = html.AsSpan(i + 1, close - i - 1);
            bool closing = raw.Length > 0 && raw[0] == '/';
            ReadOnlySpan<char> name = TagName(raw, closing);
            if (NameIs(name, "script") || NameIs(name, "style"))
            {
                if (!closing)
                {
                    skip = true;
                    skipTag = name.ToString();
                }
                else if (skip && name.Equals(skipTag, StringComparison.OrdinalIgnoreCase))
                {
                    skip = false;
                }
            }
            else if (!skip && NameIs(name, "br"))
            {
                emit(HtmlPiece.Break, "");
            }
            else if (!skip && NameIs(name, "p"))
            {
                emit(closing ? HtmlPiece.ParagraphEnd : HtmlPiece.ParagraphStart, "");
            }
            i = close;
        }
    }

    private static ReadOnlySpan<char> TagName(ReadOnlySpan<char> tag, bool closing)
    {
        if (closing) tag = tag[1..];
        int i = 0;
        while (i < tag.Length && char.IsWhiteSpace(tag[i])) i++;
        int start = i;
        while (i < tag.Length && char.IsAsciiLetter(tag[i])) i++;
        return tag[start..i];
    }

    private static bool NameIs(ReadOnlySpan<char> name, string expected) =>
        name.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value, bool keepNewlines)
    {
        var sb = new StringBuilder(value.Length);
        bool space = false;
        int newlines = 0;
        foreach (char c in value)
        {
            if (c is '\u00A0' or '\t' or '\r' or '\f' or ' ')
            {
                if (newlines == 0) space = true;
                continue;
            }
            if (c == '\n')
            {
                space = false;
                if (keepNewlines && newlines < 2) newlines++;
                continue;
            }
            if (char.IsControl(c) || c is '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E' or '\u2066' or '\u2067' or '\u2068' or '\u2069')
                continue;
            if (newlines > 0)
            {
                if (sb.Length > 0) sb.Append('\n', newlines);
                newlines = 0;
            }
            else if (space && sb.Length > 0)
            {
                sb.Append(' ');
            }
            space = false;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    private enum HtmlPiece
    {
        Text,
        Break,
        ParagraphStart,
        ParagraphEnd
    }
}

// Keyless GET of the public oEmbed document. No cookie jar, no WebView2 cookie store, no API key.
internal sealed class XPreviewClient : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);
    public const int MaxBytes = 64 * 1024;

    private readonly HttpClient http;
    private readonly TimeSpan timeout;
    private readonly ConcurrentDictionary<string, XPreviewCard?> cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);

    public XPreviewClient(HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        this.timeout = timeout ?? DefaultTimeout;
        HttpMessageHandler sockets = handler ?? new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false
        };
        http = new HttpClient(sockets, disposeHandler: handler is null)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MessengerDesktop");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<XPreviewCard?> TryGetAsync(Uri? clicked, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!enabled) return null;
        if (!XPreviewEndpoint.TryCreate(clicked, out Uri? endpoint, out Uri? status) || endpoint is null || status is null)
            return null;
        string key = status.AbsoluteUri;
        if (cache.TryGetValue(key, out XPreviewCard? cached)) return cached;

        SemaphoreSlim gate = gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await gate.WaitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            if (cache.TryGetValue(key, out cached)) return cached;
            FetchResult fetched = await FetchAsync(endpoint, timeoutSource.Token);
            if (fetched.Remember) cache[key] = fetched.Card;
            return fetched.Card;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => http.Dispose();

    private async Task<FetchResult> FetchAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        Uri current = endpoint;
        for (int hop = 0; hop < 3; hop++)
        {
            if (!XPreviewEndpoint.IsPublishHost(current)) return FetchResult.Keep(null);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return FetchResult.Drop();
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    Uri? next = response.Headers.Location;
                    if (next is null) return FetchResult.Keep(null);
                    if (!next.IsAbsoluteUri) next = new Uri(current, next);
                    current = next;
                    continue;
                }
                if (!response.IsSuccessStatusCode) return FetchResult.Keep(null);
                string? body = await ReadLimitedAsync(response, cancellationToken);
                if (body is null) return FetchResult.Keep(null);
                return FetchResult.Keep(XPreviewParser.TryParse(body, out XPreviewCard? card) ? card : null);
            }
        }
        return FetchResult.Keep(null);
    }

    private static async Task<string?> ReadLimitedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length && length > MaxBytes) return null;
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            int read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length == 0) return null;
        string text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        return text;
    }

    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private readonly struct FetchResult
    {
        private FetchResult(bool remember, XPreviewCard? card)
        {
            Remember = remember;
            Card = card;
        }

        public bool Remember { get; }
        public XPreviewCard? Card { get; }
        public static FetchResult Keep(XPreviewCard? card) => new(true, card);
        public static FetchResult Drop() => new(false, null);
    }
}

internal static class XPreviewMessage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static bool TryReadRequest(string? json, out string? requestUrl)
    {
        requestUrl = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4096) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() != "messenger-x-preview")
                return false;
            if (!root.TryGetProperty("url", out JsonElement url) || url.ValueKind != JsonValueKind.String)
                return false;
            string? value = url.GetString();
            if (string.IsNullOrWhiteSpace(value) || value.Length > 2000) return false;
            if (!Uri.TryCreate(value, UriKind.Absolute, out _)) return false;
            requestUrl = value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // ok false means the page leaves Facebook's card alone. The html field is never copied in.
    public static string Result(string requestUrl, XPreviewCard? card)
    {
        if (card is null)
        {
            return JsonSerializer.Serialize(new
            {
                type = "messenger-x-preview-result",
                requestUrl,
                ok = false
            }, JsonOptions);
        }
        return JsonSerializer.Serialize(new
        {
            type = "messenger-x-preview-result",
            requestUrl,
            ok = true,
            author = card.Author,
            handle = card.Handle,
            text = card.Text
        }, JsonOptions);
    }
}
