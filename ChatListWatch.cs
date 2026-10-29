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

          function extract(row) {
            const link = row.querySelector('a[href]');
            const href = link && isThread(link.href) ? link.href : '';
            const img = row.querySelector('img[src]');
            const src = img ? (img.currentSrc || img.src || '') : '';
            const avatar = /^https:\/\//i.test(src) ? src : '';
            const nodes = row.querySelectorAll('span, div, abbr, h1, h2, h3, strong, a');
            let name = '';
            let preview = '';
            let time = '';
            for (let i = 0; i < nodes.length; i++) {
              const text = ownText(nodes[i]);
              if (!text || text.length > 180) continue;
              if (!time && isTime(text)) { time = text; continue; }
              if (!name) { name = text; continue; }
              if (!preview && text !== name) preview = text;
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
