using System.Text.Json;

namespace MessengerDesktop;

// The page posts one snapshot of unread chat-list rows. ok is false when the
// grid is there but the rows cannot be read. The host then falls back to the title.
internal static class ChatListMessage
{
    public const string Prefix = "messenger-chat-list:";

    public static bool TryParse(string? message, out bool readable, out IReadOnlyList<ChatThread> threads)
    {
        readable = false;
        threads = [];
        if (string.IsNullOrEmpty(message) || !message.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(message[Prefix.Length..]);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            readable = root.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True;
            if (!readable || !root.TryGetProperty("rows", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
            {
                threads = [];
                return true;
            }

            var parsed = new List<ChatThread>();
            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                parsed.Add(new ChatThread(
                    Text(row, "name"),
                    Text(row, "preview"),
                    Text(row, "avatar"),
                    Text(row, "time"),
                    Text(row, "href")));
            }

            threads = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return "";
        return value.GetString() ?? "";
    }
}

// Reads unread inbox rows and posts them. It never clicks, focuses, or scrolls,
// so a read receipt is not sent. Bold text plus the blue dot is the unread row.
// An aria-label that already says unread is the same dot.
internal static class ChatListWatch
{
    public const string Source =
        """
        (() => {
          if (window.__messengerChatListWatch) return;
          window.__messengerChatListWatch = true;
          const host = location.hostname || '';
          const allowed = location.protocol === 'https:' && (
            host === 'facebook.com' || host.endsWith('.facebook.com') ||
            host === 'messenger.com' || host.endsWith('.messenger.com'));
          if (!allowed || !window.chrome || !chrome.webview || typeof chrome.webview.postMessage !== 'function') return;

          const started = Date.now();
          let last = '';
          let timer = 0;

          function publish(payload) {
            let text = '';
            try { text = JSON.stringify(payload); }
            catch (error) { return; }
            if (text === last) return;
            last = text;
            try { chrome.webview.postMessage('messenger-chat-list:' + text); }
            catch (error) { }
          }

          function ownText(el) {
            let value = '';
            const nodes = el.childNodes;
            for (let i = 0; i < nodes.length; i++) {
              if (nodes[i].nodeType === 3) value += nodes[i].nodeValue;
            }
            return value.replace(/\s+/g, ' ').trim();
          }

          function isTime(text) {
            return /^(now|just now|\d+\s*[smhdw])$/i.test(text);
          }

          function isBlue(color) {
            const match = String(color || '').match(/rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*([\d.]+))?\s*\)/);
            if (!match) return false;
            const alpha = match[4] === undefined ? 1 : parseFloat(match[4]);
            if (alpha < 0.4) return false;
            const red = +match[1], green = +match[2], blue = +match[3];
            return blue >= 180 && blue > red + 30 && blue >= green;
          }

          function isBold(el) {
            const value = getComputedStyle(el).fontWeight;
            const parsed = parseInt(value, 10);
            return parsed >= 600 || value === 'bold' || value === 'bolder';
          }

          function hasBoldText(row) {
            const nodes = row.querySelectorAll('span, div, abbr, h1, h2, h3, strong, a');
            for (let i = 0; i < nodes.length; i++) {
              if (!ownText(nodes[i])) continue;
              if (isBold(nodes[i])) return true;
            }
            return false;
          }

          function hasBlueDot(row) {
            const nodes = row.querySelectorAll('div, span, i, svg, circle');
            for (let i = 0; i < nodes.length; i++) {
              const el = nodes[i];
              const rect = el.getBoundingClientRect();
              const w = rect.width;
              const h = rect.height;
              if (w < 5 || h < 5 || w > 20 || h > 20) continue;
              if (Math.abs(w - h) > 4) continue;
              const style = getComputedStyle(el);
              const radius = parseFloat(style.borderRadius) || 0;
              const round = style.borderRadius === '50%' || radius >= (Math.min(w, h) / 2) - 1;
              const tag = el.tagName;
              if (!round && tag !== 'CIRCLE' && tag !== 'SVG') continue;
              if (isBlue(style.backgroundColor) || isBlue(style.fill) || isBlue(el.getAttribute('fill') || '')) return true;
            }
            return false;
          }

          function hasUnreadLabel(row) {
            const nodes = row.querySelectorAll('[aria-label]');
            for (let i = 0; i < nodes.length; i++) {
              const label = nodes[i].getAttribute('aria-label') || '';
              if (/unread|mark as read/i.test(label)) return true;
            }
            return false;
          }

          function isUnread(row) {
            if (hasUnreadLabel(row)) return true;
            return hasBoldText(row) && hasBlueDot(row);
          }

          function isThread(href) {
            try {
              const path = new URL(href, location.href).pathname.replace(/\/+$/, '');
              return /^\/messages\/t\/[^/]+$/i.test(path) || /^\/t\/[^/]+$/i.test(path);
            } catch (error) {
              return false;
            }
          }

          function lineOf(text) {
            return String(text || '').replace(/\s+/g, ' ').trim();
          }

          function fullText(el) {
            return lineOf(el.textContent || '');
          }

          function isUnreadLabel(text) {
            return /^(?:unread message|непрочетено съобщение)\s*:?\s*$/i.test(lineOf(text));
          }

          function plainName(el) {
            const direct = lineOf(ownText(el));
            if (direct && !isUnreadLabel(direct) && !isTime(direct)) return direct;
            const spans = el.querySelectorAll('span');
            for (let i = 0; i < spans.length; i++) {
              const text = lineOf(ownText(spans[i]));
              if (text && !isUnreadLabel(text) && !isTime(text)) return text;
            }
            const all = fullText(el);
            return isUnreadLabel(all) ? '' : all;
          }

          function extract(row) {
            const link = row.querySelector('a[href]');
            const href = link && isThread(link.href) ? link.href : '';
            const imgs = row.querySelectorAll('img[src]');
            let avatar = '';
            for (let i = 0; i < imgs.length; i++) {
              const src = imgs[i].currentSrc || imgs[i].src || '';
              if (/^https:\/\//i.test(src)) { avatar = src; break; }
            }

            let name = '';
            let preview = '';
            let time = '';
            let snippet = null;
            const directed = row.querySelectorAll('span[dir="auto"]');
            if (directed.length) {
              name = plainName(directed[0]);
              for (let i = 1; i < directed.length; i++) {
                if (snippet && snippet.contains(directed[i])) continue;
                const text = fullText(directed[i]);
                if (!text || isUnreadLabel(text)) continue;
                preview = text;
                snippet = directed[i];
                break;
              }
              if (!snippet && directed.length > 1) snippet = directed[1];
            }

            const nodes = row.querySelectorAll('span, div, abbr, h1, h2, h3, strong, a');
            for (let i = 0; i < nodes.length; i++) {
              const text = lineOf(ownText(nodes[i]));
              if (!text || text.length > 180) continue;
              if (!time && isTime(text)) { time = text; continue; }
              if (!name) {
                if (isUnreadLabel(text)) continue;
                name = text;
                continue;
              }
              if (!preview && !isUnreadLabel(text) && text !== name) preview = text;
            }

            if (snippet && !lineOf(preview)) {
              const images = snippet.querySelectorAll('img');
              let sticker = false;
              let photo = false;
              for (let i = 0; i < images.length; i++) {
                const alt = lineOf((images[i].getAttribute('alt') || '') + ' ' + (images[i].getAttribute('aria-label') || ''));
                if (/sticker|стикер/i.test(alt)) sticker = true;
                else photo = true;
              }
              if (sticker) preview = 'sent a sticker';
              else if (photo) preview = 'sent a photo';
            }

            if (!name) name = 'Messenger';
            return { name: name, preview: preview, avatar: avatar, time: time, href: href };
          }

          function collect(grid) {
            const rowEls = grid.querySelectorAll('[role="row"]');
            if (!rowEls.length) {
              const text = (grid.innerText || '').replace(/\s+/g, ' ').trim();
              if (text.length < 2) return undefined;
              if (text.length > 80) return null;
              return [];
            }
            const rows = [];
            for (let i = 0; i < rowEls.length && rows.length < 50; i++) {
              if (!isUnread(rowEls[i])) continue;
              rows.push(extract(rowEls[i]));
            }
            return rows;
          }

          function scan() {
            let grid = null;
            try { grid = document.querySelector('[role="navigation"] [role="grid"]'); }
            catch (error) {
              publish({ ok: false, reason: 'unreadable', rows: [] });
              return;
            }
            if (!grid) {
              if (Date.now() - started > 8000) publish({ ok: false, reason: 'missing', rows: [] });
              return;
            }
            try {
              const rows = collect(grid);
              if (rows === undefined) return;
              if (rows === null) publish({ ok: false, reason: 'unreadable', rows: [] });
              else publish({ ok: true, rows: rows });
            } catch (error) {
              publish({ ok: false, reason: 'unreadable', rows: [] });
            }
          }

          function schedule() {
            if (timer) return;
            timer = setTimeout(() => { timer = 0; scan(); }, 250);
          }

          function boot() {
            const root = document.documentElement;
            if (!root) {
              setTimeout(boot, 50);
              return;
            }
            scan();
            const observer = new MutationObserver(schedule);
            observer.observe(root, {
              subtree: true,
              childList: true,
              characterData: true,
              attributes: true,
              attributeFilter: ['class', 'style', 'aria-label']
            });
          }

          boot();
        })();
        """;
}

internal readonly record struct ChatListParsedRow(
    string Name,
    string Preview,
    string AvatarUrl,
    string RelativeTime,
    string Href);

// The snippet is the span under the name. A span that is only the unread label is skipped.
internal static class ChatListRowHtml
{
    public static ChatListParsedRow Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return new ChatListParsedRow("", "", "", "", "");
        Node root = HtmlTree.Parse(html);
        return Read(root);
    }

    private static ChatListParsedRow Read(Node root)
    {
        string href = "";
        string avatar = "";
        string time = "";
        foreach (Node node in Walk(root))
        {
            if (href.Length == 0 && node.Tag == "a" && IsThread(node.Attr("href")))
                href = node.Attr("href");
            if (avatar.Length == 0 && node.Tag == "img")
            {
                string src = node.Attr("src");
                if (src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    avatar = src;
            }

            if (time.Length == 0 && ChatListSnippet.IsRelativeTime(DirectText(node)))
                time = ChatListSnippet.Collapse(DirectText(node));
        }

        List<Node> directed = Walk(root)
            .Where(node => node.Tag == "span" && node.Attr("dir").Equals("auto", StringComparison.OrdinalIgnoreCase))
            .ToList();
        string name = "";
        string preview = "";
        Node? snippet = null;
        if (directed.Count > 0)
        {
            name = FirstPlain(directed[0]);
            for (int i = 1; i < directed.Count; i++)
            {
                if (snippet is not null && Contains(snippet, directed[i])) continue;
                string? line = ChatListSnippet.Present(FullText(directed[i]));
                if (line is null) continue;
                preview = line;
                snippet = directed[i];
                break;
            }
        }

        if (name.Length == 0 || preview.Length == 0)
        {
            bool seenName = name.Length > 0;
            foreach (Node node in Walk(root))
            {
                string direct = DirectText(node);
                if (direct.Length == 0 || direct.Length > 180) continue;
                if (ChatListSnippet.IsRelativeTime(direct))
                {
                    if (time.Length == 0) time = direct;
                    continue;
                }

                if (!seenName)
                {
                    if (ChatListSnippet.IsUnreadLabel(direct)) continue;
                    name = direct;
                    seenName = true;
                    continue;
                }

                if (preview.Length > 0 || ChatListSnippet.IsUnreadLabel(direct) || direct == name) continue;
                string? line = ChatListSnippet.Present(direct);
                if (line is null) continue;
                preview = line;
            }
        }

        if (preview.Length == 0 && directed.Count > 1)
        {
            foreach (Node host in directed.Skip(1))
            {
                string? media = MediaPreview(host);
                if (media is null) continue;
                preview = media;
                break;
            }
        }

        return new ChatListParsedRow(name, preview, avatar, time, href);
    }

    private static string? MediaPreview(Node host)
    {
        bool photo = false;
        bool sticker = false;
        foreach (Node node in Walk(host))
        {
            if (node.Tag != "img") continue;
            string alt = node.Attr("alt") + " " + node.Attr("aria-label");
            if (ChatListSnippet.IsStickerAlt(alt)) sticker = true;
            else photo = true;
        }

        if (sticker) return ChatListSnippet.Sticker;
        if (photo) return ChatListSnippet.Photo;
        return null;
    }

    private static string FirstPlain(Node element)
    {
        string direct = DirectText(element);
        if (direct.Length > 0 && !ChatListSnippet.IsUnreadLabel(direct) && !ChatListSnippet.IsRelativeTime(direct))
            return direct;
        foreach (Node child in Walk(element))
        {
            if (ReferenceEquals(child, element)) continue;
            string text = DirectText(child);
            if (text.Length == 0 || ChatListSnippet.IsUnreadLabel(text) || ChatListSnippet.IsRelativeTime(text)) continue;
            return text;
        }

        return ChatListSnippet.Present(FullText(element)) ?? "";
    }

    private static string DirectText(Node node)
    {
        if (node.IsText) return ChatListSnippet.Collapse(node.Text);
        var parts = new List<string>();
        foreach (Node child in node.Children)
        {
            if (!child.IsText) continue;
            string text = ChatListSnippet.Collapse(child.Text);
            if (text.Length > 0) parts.Add(text);
        }

        return string.Join(' ', parts);
    }

    private static string FullText(Node node)
    {
        var parts = new List<string>();
        void Visit(Node current)
        {
            if (current.IsText)
            {
                string text = current.Text.Trim();
                if (text.Length > 0) parts.Add(text);
                return;
            }

            foreach (Node child in current.Children)
                Visit(child);
        }

        Visit(node);
        return ChatListSnippet.Collapse(string.Join(' ', parts));
    }

    private static bool Contains(Node ancestor, Node node)
    {
        foreach (Node child in ancestor.Children)
        {
            if (ReferenceEquals(child, node) || Contains(child, node)) return true;
        }

        return false;
    }

    private static bool IsThread(string href) =>
        href.Contains("/messages/t/", StringComparison.OrdinalIgnoreCase)
        || href.Contains("/t/", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (Node child in node.Children)
        {
            foreach (Node nested in Walk(child))
                yield return nested;
        }
    }

    private sealed class Node
    {
        public string Tag { get; init; } = "";
        public string Text { get; init; } = "";
        public bool IsText { get; init; }
        public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Node> Children { get; } = new();

        public string Attr(string name) => Attributes.TryGetValue(name, out string? value) ? value : "";
    }

    private static class HtmlTree
    {
        public static Node Parse(string html)
        {
            var root = new Node { Tag = "#root" };
            var stack = new Stack<Node>();
            stack.Push(root);
            int index = 0;
            while (index < html.Length)
            {
                if (html[index] != '<')
                {
                    int next = html.IndexOf('<', index);
                    if (next < 0) next = html.Length;
                    string text = System.Net.WebUtility.HtmlDecode(html[index..next]);
                    index = next;
                    if (text.Length > 0)
                        stack.Peek().Children.Add(new Node { IsText = true, Text = text });
                    continue;
                }

                if (index + 3 < html.Length && html[index + 1] == '!' && html[index + 2] == '-' && html[index + 3] == '-')
                {
                    int end = html.IndexOf("-->", index + 4, StringComparison.Ordinal);
                    index = end < 0 ? html.Length : end + 3;
                    continue;
                }

                int close = html.IndexOf('>', index + 1);
                if (close < 0) break;
                string token = html[(index + 1)..close].Trim();
                index = close + 1;
                if (token.Length == 0) continue;
                if (token[0] == '/')
                {
                    string closing = token[1..].Trim();
                    while (stack.Count > 1 && !stack.Peek().Tag.Equals(closing, StringComparison.OrdinalIgnoreCase))
                        stack.Pop();
                    if (stack.Count > 1) stack.Pop();
                    continue;
                }

                bool selfClosing = token.EndsWith('/');
                if (selfClosing) token = token[..^1].Trim();
                Node element = ParseStart(token);
                stack.Peek().Children.Add(element);
                bool voidTag = selfClosing || element.Tag is "img" or "br" or "hr" or "meta" or "input";
                if (!voidTag) stack.Push(element);
            }

            return root;
        }

        private static Node ParseStart(string token)
        {
            int space = token.IndexOfAny([' ', '\t', '\r', '\n']);
            string tag = (space < 0 ? token : token[..space]).ToLowerInvariant();
            var node = new Node { Tag = tag };
            if (space < 0) return node;
            string rest = token[(space + 1)..];
            int cursor = 0;
            while (cursor < rest.Length)
            {
                while (cursor < rest.Length && char.IsWhiteSpace(rest[cursor])) cursor++;
                if (cursor >= rest.Length) break;
                int nameEnd = cursor;
                while (nameEnd < rest.Length && rest[nameEnd] != '=' && !char.IsWhiteSpace(rest[nameEnd])) nameEnd++;
                string attrName = rest[cursor..nameEnd];
                cursor = nameEnd;
                while (cursor < rest.Length && char.IsWhiteSpace(rest[cursor])) cursor++;
                if (cursor >= rest.Length || rest[cursor] != '=')
                {
                    if (attrName.Length > 0) node.Attributes[attrName] = "";
                    continue;
                }

                cursor++;
                while (cursor < rest.Length && char.IsWhiteSpace(rest[cursor])) cursor++;
                if (cursor >= rest.Length) break;
                char quote = rest[cursor];
                string value;
                if (quote is '"' or '\'')
                {
                    int valueEnd = rest.IndexOf(quote, cursor + 1);
                    if (valueEnd < 0) valueEnd = rest.Length;
                    value = rest[(cursor + 1)..valueEnd];
                    cursor = valueEnd < rest.Length ? valueEnd + 1 : rest.Length;
                }
                else
                {
                    int valueEnd = cursor;
                    while (valueEnd < rest.Length && !char.IsWhiteSpace(rest[valueEnd])) valueEnd++;
                    value = rest[cursor..valueEnd];
                    cursor = valueEnd;
                }

                if (attrName.Length > 0)
                    node.Attributes[attrName] = System.Net.WebUtility.HtmlDecode(value);
            }

            return node;
        }
    }
}
