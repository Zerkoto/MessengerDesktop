(() => {
    if (window !== window.top || location.protocol !== 'https:' || location.hostname !== 'www.facebook.com') return;
    if (!location.pathname.startsWith('/messages')) return;
    // Read-only toward Facebook: no fetch, no XHR, no form, no navigation.
    // The host loads the public oEmbed document. This script only rewrites the local card.
    const hosts = new Set([
        'x.com', 'www.x.com', 'mobile.x.com',
        'twitter.com', 'www.twitter.com', 'mobile.twitter.com', 'm.twitter.com'
    ]);
    const reserved = new Set([
        'home', 'explore', 'search', 'settings', 'messages', 'notifications',
        'intent', 'compose', 'login', 'signup', 'share', 'hashtag', 'download',
        'account', 'i', 'tos', 'privacy', 'about', 'jobs'
    ]);
    const isId = value => /^[0-9]+$/.test(value);
    const isHandle = value => /^[A-Za-z0-9_]{1,15}$/.test(value) && !reserved.has(value.toLowerCase());
    const isStatusPath = pathname => {
        const parts = pathname.split('/').filter(Boolean);
        if (parts.length >= 3 && parts[0].toLowerCase() === 'i' && parts[1].toLowerCase() === 'status' && isId(parts[2])) return true;
        if (parts.length >= 4 && parts[0].toLowerCase() === 'i' && parts[1].toLowerCase() === 'web' && parts[2].toLowerCase() === 'status' && isId(parts[3])) return true;
        return parts.length >= 3 && isHandle(parts[0]) && parts[1].toLowerCase() === 'status' && isId(parts[2]);
    };
    const isDirectStatus = url => url.protocol === 'https:' && !url.port && hosts.has(url.hostname.toLowerCase()) && isStatusPath(url.pathname);
    const isStatus = (url, depth) => {
        if (isDirectStatus(url)) return true;
        if (depth > 0) return false;
        if (url.protocol !== 'https:' || url.port || url.hostname.toLowerCase() !== 'l.facebook.com') return false;
        const inner = url.searchParams.get('u');
        if (!inner) return false;
        try { return isStatus(new URL(inner), depth + 1); }
        catch { return false; }
    };
    const mentionsPreview = node => {
        if (!node) return false;
        const label = node.getAttribute ? (node.getAttribute('aria-label') || '') : '';
        const text = node.innerText || '';
        const value = (label + ' ' + text).toLowerCase();
        return value.includes('no preview available') || value.includes('preview unavailable') || value.includes('preview not available');
    };
    // The failed card is the anchor, or a small parent that has that one link.
    // A message bubble is too large, so a miss leaves the row alone.
    const findCard = anchor => {
        if (mentionsPreview(anchor)) return anchor;
        const parent = anchor.parentElement;
        if (!parent || parent === document.body || parent === document.documentElement) return null;
        if (!mentionsPreview(parent)) return null;
        if (parent.querySelector('[contenteditable="true"]')) return null;
        if ((parent.innerText || '').trim().length > 400) return null;
        if (parent.querySelectorAll('a[href]').length !== 1) return null;
        return parent;
    };
    let enabled = false;
    let generation = 0;
    let domReady = false;
    let timer = 0;
    const asked = new WeakMap();
    const waiting = new Map();

    const build = (href, author, handle, text) => {
        const root = document.createElement('div');
        root.setAttribute('data-messenger-x-preview', '1');
        root.setAttribute('data-href', href);
        root.style.cssText = 'box-sizing:border-box;display:flex;flex-direction:column;gap:2px;max-width:100%;padding:10px 12px;text-align:start;white-space:normal;line-height:1.35;cursor:pointer;';
        const name = document.createElement('div');
        name.textContent = author;
        name.style.cssText = 'font-weight:600;font-size:14px;';
        const at = document.createElement('div');
        at.textContent = '@' + handle;
        at.style.cssText = 'opacity:0.72;font-size:12px;';
        root.appendChild(name);
        root.appendChild(at);
        if (text) {
            const body = document.createElement('div');
            body.textContent = text;
            body.style.cssText = 'font-size:14px;margin-top:4px;white-space:pre-wrap;overflow:hidden;display:-webkit-box;-webkit-line-clamp:8;-webkit-box-orient:vertical;';
            root.appendChild(body);
        }
        const label = document.createElement('div');
        label.textContent = 'x.com';
        label.style.cssText = 'opacity:0.62;font-size:12px;margin-top:6px;';
        root.appendChild(label);
        return root;
    };

    const restoreAll = () => {
        document.querySelectorAll('[data-messenger-x-card]').forEach(surface => {
            const preview = surface.querySelector(':scope > [data-messenger-x-preview]');
            const hidden = surface.querySelector(':scope > [data-messenger-x-hidden]');
            if (preview) preview.remove();
            if (hidden) {
                while (hidden.firstChild) surface.insertBefore(hidden.firstChild, hidden);
                hidden.remove();
            }
            surface.removeAttribute('data-messenger-x-card');
        });
    };

    const render = (anchor, href, author, handle, text) => {
        if (!anchor.isConnected) return;
        const surface = findCard(anchor);
        if (!surface || surface.querySelector('[data-messenger-x-preview]')) return;
        const hidden = document.createElement('div');
        hidden.setAttribute('data-messenger-x-hidden', '1');
        hidden.style.setProperty('display', 'none', 'important');
        while (surface.firstChild) hidden.appendChild(surface.firstChild);
        surface.appendChild(hidden);
        surface.appendChild(build(href, author, handle, text));
        surface.setAttribute('data-messenger-x-card', '1');
    };

    const apply = (href, data) => {
        const job = waiting.get(href);
        waiting.delete(href);
        if (!job || job.generation !== generation || !enabled) return;
        if (!data || data.ok !== true) return;
        if (typeof data.author !== 'string' || typeof data.handle !== 'string' || typeof data.text !== 'string') return;
        if (!/^[A-Za-z0-9_]{1,15}$/.test(data.handle)) return;
        if (data.author.length === 0 || data.author.length > 80 || data.text.length > 600) return;
        job.anchors.forEach(anchor => render(anchor, href, data.author, data.handle, data.text));
    };

    const ask = anchor => {
        let href;
        try { href = new URL(anchor.href).href; }
        catch { return; }
        let url;
        try { url = new URL(href); }
        catch { return; }
        if (!isStatus(url, 0) || !findCard(anchor)) return;
        if (asked.get(anchor) === generation) return;
        asked.set(anchor, generation);
        let job = waiting.get(href);
        if (!job || job.generation !== generation) {
            job = { generation, anchors: new Set() };
            waiting.set(href, job);
            chrome.webview.postMessage({ type: 'messenger-x-preview', url: href });
        }
        job.anchors.add(anchor);
    };

    const scan = root => {
        if (!enabled || !domReady) return;
        const scope = root && root.querySelectorAll ? root : document;
        scope.querySelectorAll('a[href]').forEach(anchor => {
            if (anchor.closest('[data-messenger-x-preview], [data-messenger-x-hidden]')) return;
            ask(anchor);
        });
    };

    const schedule = () => {
        if (!enabled || timer) return;
        timer = window.setTimeout(() => {
            timer = 0;
            scan(document);
        }, 200);
    };

    const setEnabled = next => {
        if (next) {
            enabled = true;
            if (domReady) scan(document);
            return;
        }
        enabled = false;
        generation += 1;
        waiting.clear();
        if (timer) {
            window.clearTimeout(timer);
            timer = 0;
        }
        restoreAll();
    };

    const onMessage = event => {
        let data = event.data;
        if (data === 'messenger-x-previews-on') { setEnabled(true); return; }
        if (data === 'messenger-x-previews-off') { setEnabled(false); return; }
        if (typeof data === 'string') {
            try { data = JSON.parse(data); }
            catch { return; }
        }
        if (!data || data.type !== 'messenger-x-preview-result' || typeof data.requestUrl !== 'string') return;
        apply(data.requestUrl, data);
    };

    window.addEventListener('click', event => {
        const target = event.target instanceof Element ? event.target : null;
        const preview = target?.closest('[data-messenger-x-preview]');
        if (!preview) return;
        if (!event.isTrusted || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const href = preview.getAttribute('data-href');
        if (!href) return;
        let url;
        try { url = new URL(href); }
        catch { return; }
        if (!isStatus(url, 0)) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        chrome.webview.postMessage({ type: 'messenger-open-status', url: href });
    }, true);

    chrome.webview.addEventListener('message', onMessage);
    const start = () => {
        domReady = true;
        if (!document.documentElement) return;
        const observer = new MutationObserver(schedule);
        observer.observe(document.documentElement, { childList: true, subtree: true, characterData: true });
        if (enabled) scan(document);
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
    else start();
    chrome.webview.postMessage('messenger-x-preview-ready');
})();
