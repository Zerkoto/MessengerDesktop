using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace MessengerDesktop.Tests;

public class XPreviewTests
{
    private const string JackStatus = "https://x.com/jack/status/20";

    private const string JackJson = """
        {"url":"https://x.com/jack/status/20","author_name":"jack","author_url":"https://x.com/jack","html":"<blockquote class=\"twitter-tweet\"><p lang=\"en\" dir=\"ltr\">just setting up my twttr</p>&mdash; jack (@jack) <a href=\"https://x.com/jack/status/20?ref_src=twsrc%5Etfw\">March 21, 2006</a></blockquote>\n","type":"rich"}
        """;

    private const string InteriorHtml = """
        <blockquote class="twitter-tweet"><p lang="en" dir="ltr">Happy 50th anniversary to the Wilderness Act! Here&#39;s a great wilderness photo from <a href="https://x.com/YosemiteNPS">@YosemiteNPS</a>. <a href="https://x.com/hashtag/Wilderness50">#Wilderness50</a> <a href="http://t.co/HMhbyTg18X">pic.twitter.com/HMhbyTg18X</a></p>&mdash; U.S. Department of the Interior (@Interior) <a href="https://x.com/Interior/status/507185938620219395">September 3, 2014</a></blockquote>
        """;

    [Theory]
    [InlineData("https://x.com/jack/status/20")]
    [InlineData("https://www.x.com/jack/status/20")]
    [InlineData("https://mobile.x.com/jack/status/20")]
    [InlineData("https://twitter.com/jack/status/20")]
    [InlineData("https://www.twitter.com/jack/status/20")]
    [InlineData("https://mobile.twitter.com/jack/status/20")]
    [InlineData("https://m.twitter.com/jack/status/20")]
    [InlineData("https://x.com/i/status/20")]
    [InlineData("https://twitter.com/i/web/status/20")]
    [InlineData("https://x.com/jack/status/20/photo/1")]
    [InlineData("https://X.COM/Jack/status/20")]
    public void Status_urls_become_a_keyless_oembed_request(string value)
    {
        var clicked = new Uri(value);

        Assert.True(XPreviewEndpoint.TryCreate(clicked, out Uri? request, out Uri? status));
        Assert.Equal(clicked.AbsoluteUri, status!.AbsoluteUri);
        Assert.Equal("https", request!.Scheme);
        Assert.Equal("publish.twitter.com", request.Host);
        Assert.True(request.IsDefaultPort);
        Assert.Empty(request.UserInfo);
        Assert.Contains("omit_script=1", request.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("key=", request.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", request.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("oauth", request.Query, StringComparison.OrdinalIgnoreCase);
        string encoded = QueryValue(request, "url");
        Assert.Equal(status.AbsoluteUri, Uri.UnescapeDataString(encoded));
    }

    [Fact]
    public void Facebook_shims_request_the_unwrapped_status()
    {
        string shim = "https://l.facebook.com/l.php?u=" + Uri.EscapeDataString(JackStatus) + "&h=AT0";

        Assert.True(XPreviewEndpoint.TryCreate(new Uri(shim), out Uri? request, out Uri? status));
        Assert.NotNull(request);
        Assert.NotNull(status);
        Assert.Equal(JackStatus, status.AbsoluteUri);
        Assert.Equal(JackStatus, Uri.UnescapeDataString(QueryValue(request, "url")));
        Assert.Equal("publish.twitter.com", request.Host);
    }

    [Theory]
    [InlineData("https://x.com/jack")]
    [InlineData("https://x.com/home")]
    [InlineData("https://x.com/jack/status/abc")]
    [InlineData("http://x.com/jack/status/20")]
    [InlineData("https://x.com.evil.com/jack/status/20")]
    [InlineData("https://www.facebook.com/reel/1")]
    [InlineData("https://l.facebook.com/l.php?u=https%3A%2F%2Fx.com%2Fhome")]
    public void Non_status_urls_are_not_requested(string value)
    {
        Assert.False(XPreviewEndpoint.TryCreate(new Uri(value), out _, out _));
    }

    [Fact]
    public void Oembed_html_becomes_author_handle_and_plain_text()
    {
        Assert.True(XPreviewParser.TryParse(JackJson, out XPreviewCard? jack));
        Assert.Equal("jack", jack!.Author);
        Assert.Equal("jack", jack.Handle);
        Assert.Equal("just setting up my twttr", jack.Text);
        Assert.DoesNotContain("March 21", jack.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<", jack.Text, StringComparison.Ordinal);

        string interior = "{\"url\":\"https://x.com/Interior/status/507185938620219395\",\"author_name\":\"U.S. Department of the Interior\",\"author_url\":\"https://x.com/Interior\",\"html\":" +
            JsonSerializer.Serialize(InteriorHtml) + ",\"type\":\"rich\"}";
        Assert.True(XPreviewParser.TryParse(interior, out XPreviewCard? card));
        Assert.Equal("U.S. Department of the Interior", card!.Author);
        Assert.Equal("Interior", card.Handle);
        Assert.Equal(
            "Happy 50th anniversary to the Wilderness Act! Here's a great wilderness photo from @YosemiteNPS. #Wilderness50 pic.twitter.com/HMhbyTg18X",
            card.Text);
    }

    [Fact]
    public void Markup_in_the_post_and_the_author_is_stripped()
    {
        const string html = """
            <blockquote class="twitter-tweet"><p>Hello <script>alert(1)</script><img src=x onerror="alert(1)">world &amp; &#39;ok&#39; &lt;b&gt;</p>&mdash; Ada (@ada) <a href="https://x.com/ada/status/1">Date</a></blockquote>
            """;
        string json = "{" +
            "\"url\":\"https://x.com/ada/status/1\"," +
            "\"author_name\":\"<img src=x onerror=alert(1)>Ada <script>nope</script>\"," +
            "\"author_url\":\"https://twitter.com/ada\"," +
            "\"html\":" + JsonSerializer.Serialize(html) + "," +
            "\"type\":\"rich\"}";

        Assert.True(XPreviewParser.TryParse(json, out XPreviewCard? card));
        Assert.Equal("Ada", card!.Author);
        Assert.Equal("ada", card.Handle);
        Assert.Equal("Hello world & 'ok' <b>", card.Text);
        Assert.DoesNotContain("onerror", card.Author, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("script", card.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", card.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", card.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_script_only_body_keeps_the_handle_and_drops_the_script()
    {
        const string json = """
            {"url":"https://x.com/jack/status/20","author_name":"<script>x</script>","author_url":"https://x.com/jack","html":"<script>alert(1)</script>","type":"rich"}
            """;

        Assert.True(XPreviewParser.TryParse(json, out XPreviewCard? card));
        Assert.Equal("jack", card!.Author);
        Assert.Equal("jack", card.Handle);
        Assert.Equal("", card.Text);
        Assert.DoesNotContain("alert", card.Author, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", card.Author, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_author_name_uses_the_handle()
    {
        const string json = """
            {"url":"https://x.com/ada/status/1","author_name":"  ","author_url":"https://x.com/ada","html":"<p>hi</p>","type":"rich"}
            """;

        Assert.True(XPreviewParser.TryParse(json, out XPreviewCard? card));
        Assert.Equal("ada", card!.Author);
        Assert.Equal("ada", card.Handle);
        Assert.Equal("hi", card.Text);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{\"errors\":[{\"message\":\"no\"}]}")]
    [InlineData("{\"url\":\"https://x.com/jack\",\"author_name\":\"jack\",\"author_url\":\"https://x.com/jack\",\"html\":\"<p>Posts by jack</p>\",\"type\":\"rich\"}")]
    [InlineData("{\"url\":\"https://x.com/jack/status/20\",\"author_name\":\"jack\",\"author_url\":\"javascript:alert(1)\",\"html\":\"<p>hi</p>\",\"type\":\"rich\"}")]
    [InlineData("{\"url\":\"https://x.com/jack/status/20\",\"author_name\":\"jack\",\"author_url\":\"https://evil.com/jack\",\"html\":\"<p>hi</p>\",\"type\":\"rich\"}")]
    [InlineData("{\"url\":\"https://x.com/jack/status/20\",\"author_name\":\"jack\",\"author_url\":\"https://x.com/jack/status/20\",\"html\":\"<p>hi</p>\",\"type\":\"rich\"}")]
    [InlineData("{\"url\":\"https://x.com/home\",\"author_name\":\"jack\",\"author_url\":\"https://x.com/jack\",\"html\":\"<p>hi</p>\",\"type\":\"rich\"}")]
    public void Invalid_oembed_payloads_do_not_parse(string json)
    {
        Assert.False(XPreviewParser.TryParse(json, out XPreviewCard? card));
        Assert.Null(card);
    }

    [Fact]
    public async Task A_successful_fetch_is_cached_and_sends_no_cookies()
    {
        var handler = new ScriptedHandler(_ => JsonResponse(HttpStatusCode.OK, JackJson));
        using var client = new XPreviewClient(handler);

        XPreviewCard? first = await client.TryGetAsync(new Uri(JackStatus), enabled: true);
        XPreviewCard? second = await client.TryGetAsync(new Uri("https://X.COM/jack/status/20"), enabled: true);

        Assert.NotNull(first);
        Assert.Equal("just setting up my twttr", first!.Text);
        Assert.Equal(first.Text, second!.Text);
        Assert.Single(handler.Requests);
        HttpRequestMessage request = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("publish.twitter.com", request.RequestUri!.Host);
        Assert.Contains("omit_script=1", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("MessengerDesktop", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.False(request.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task The_twitter_redirect_is_followed_only_to_publish_x()
    {
        var handler = new ScriptedHandler(request =>
        {
            if (request.RequestUri!.Host.Equals("publish.twitter.com", StringComparison.OrdinalIgnoreCase))
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                redirect.Headers.Location = new Uri("https://publish.x.com/oembed?url=" + Uri.EscapeDataString(JackStatus) + "&omit_script=1");
                redirect.Headers.TryAddWithoutValidation("Set-Cookie", "guest_id=secret");
                return redirect;
            }
            return JsonResponse(HttpStatusCode.OK, JackJson);
        });
        using var client = new XPreviewClient(handler);

        XPreviewCard? card = await client.TryGetAsync(new Uri(JackStatus), enabled: true);

        Assert.Equal("jack", card!.Handle);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("publish.x.com", handler.Requests[1].RequestUri!.Host);
        Assert.False(handler.Requests[1].Headers.Contains("Cookie"));
        Assert.False(handler.Requests[0].Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task An_offsite_redirect_is_not_followed()
    {
        var handler = new ScriptedHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://evil.example/oembed");
            return redirect;
        });
        using var client = new XPreviewClient(handler);

        Assert.Null(await client.TryGetAsync(new Uri(JackStatus), enabled: true));
        Assert.Null(await client.TryGetAsync(new Uri(JackStatus), enabled: true));
        Assert.Single(handler.Requests);
        Assert.Equal("publish.twitter.com", handler.Requests[0].RequestUri!.Host);
    }

    [Fact]
    public async Task Http_failure_returns_no_card_and_is_cached()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new XPreviewClient(handler);

        Assert.Null(await client.TryGetAsync(new Uri(JackStatus), enabled: true));
        Assert.Null(await client.TryGetAsync(new Uri(JackStatus), enabled: true));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_timeout_returns_no_card_and_can_be_retried()
    {
        var handler = new HangHandler();
        using var client = new XPreviewClient(handler, TimeSpan.FromMilliseconds(50));

        Assert.Null(await client.TryGetAsync(new Uri(JackStatus), enabled: true));
        Assert.Null(await client.TryGetAsync(new Uri(JackStatus), enabled: true));
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task A_disabled_setting_does_not_fetch()
    {
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException("should not be called"));
        using var client = new XPreviewClient(handler);

        Assert.Null(await client.TryGetAsync(new Uri(JackStatus), enabled: false));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void The_page_result_carries_plain_text_and_no_html()
    {
        Assert.True(XPreviewParser.TryParse(JackJson, out XPreviewCard? card));
        string json = XPreviewMessage.Result("https://l.facebook.com/l.php?u=1", card);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal("messenger-x-preview-result", root.GetProperty("type").GetString());
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("https://l.facebook.com/l.php?u=1", root.GetProperty("requestUrl").GetString());
        Assert.Equal("jack", root.GetProperty("author").GetString());
        Assert.Equal("jack", root.GetProperty("handle").GetString());
        Assert.Equal("just setting up my twttr", root.GetProperty("text").GetString());
        Assert.False(root.TryGetProperty("html", out _));
        Assert.DoesNotContain("blockquote", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<p", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_failed_result_tells_the_page_to_leave_the_card()
    {
        string json = XPreviewMessage.Result(JackStatus, null);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(JackStatus, root.GetProperty("requestUrl").GetString());
        Assert.False(root.TryGetProperty("author", out _));
        Assert.False(root.TryGetProperty("handle", out _));
        Assert.False(root.TryGetProperty("text", out _));
        Assert.False(root.TryGetProperty("html", out _));
    }

    [Fact]
    public void Preview_requests_read_the_page_url_and_reject_other_messages()
    {
        Assert.True(XPreviewMessage.TryReadRequest(
            """{"type":"messenger-x-preview","url":"https://x.com/jack/status/20"}""",
            out string? url));
        Assert.Equal(JackStatus, url);
        Assert.False(XPreviewMessage.TryReadRequest(
            """{"type":"messenger-open-status","url":"https://x.com/jack/status/20"}""",
            out _));
        Assert.False(XPreviewMessage.TryReadRequest("not json", out _));
    }

    private static string QueryValue(Uri uri, string name)
    {
        foreach (string pair in uri.Query.TrimStart('?').Split('&'))
        {
            int separator = pair.IndexOf('=');
            if (separator < 0) continue;
            if (pair[..separator] == name) return pair[(separator + 1)..];
        }
        throw new InvalidOperationException("Missing query value " + name);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return response;
    }

    private sealed class HangHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The timeout should cancel this request.");
        }
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> next;
        public List<HttpRequestMessage> Requests { get; } = new();

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> next) => this.next = next;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(next(request));
        }
    }
}
