// Watches the chat grid and reports a new row. It does not activate, move, or change the page.
(() => {
    if (window !== window.top || location.protocol !== 'https:' || location.hostname !== 'www.facebook.com') return;
    if (!location.pathname.startsWith('/messages')) return;
    if (!window.chrome || !chrome.webview) return;

    const storageKey = 'messenger-chat-list-seen';
    const prior = window.__messengerChatListWatch;
    if (prior) {
        prior.start();
        return;
    }

    const state = {
        observer: null,
        timer: 0,
        target: null,
        baselined: false,
        start: null,
        stop: null
    };

    function loadSeen() {
        try {
            const raw = sessionStorage.getItem(storageKey);
            const parsed = raw ? JSON.parse(raw) : [];
            return new Set(Array.isArray(parsed) ? parsed.filter(item => typeof item === 'string') : []);
        } catch {
            return new Set();
        }
    }

    function saveSeen(seen) {
        try {
            const items = Array.from(seen).slice(-400);
            sessionStorage.setItem(storageKey, JSON.stringify(items));
        } catch {
            // The page can refuse storage. The host still drops a repeated payload.
        }
    }

    function bold(el) {
        const weight = getComputedStyle(el).fontWeight;
        if (weight === 'bold' || weight === 'bolder') return true;
        const value = Number.parseInt(weight, 10);
        return value >= 600;
    }

    function rowLooksNew(row) {
        if (bold(row)) return true;
        const nodes = row.querySelectorAll('*');
        for (let i = 0; i < nodes.length; i++) {
            if (bold(nodes[i])) return true;
        }
        return false;
    }

    function threadPath(link) {
        let url;
        try {
            url = new URL(link.href, location.origin);
        } catch {
            return '';
        }
        if (url.protocol !== 'https:' || url.hostname !== 'www.facebook.com' || url.port) return '';
        if (!url.pathname.includes('/t/')) return '';
        return url.pathname;
    }

    function flatText(el) {
        return (el.textContent || '').replace(/\s+/g, ' ').trim();
    }

    function chatName(link) {
        const nodes = link.querySelectorAll('*');
        for (let i = 0; i < nodes.length; i++) {
            const node = nodes[i];
            if (node.children.length !== 0) continue;
            const text = flatText(node);
            if (text && bold(node)) return text.slice(0, 120);
        }
        return flatText(link).slice(0, 120);
    }

    function previewOf(link, name) {
        let text = flatText(link);
        if (name && text.startsWith(name)) text = text.slice(name.length).trim();
        return text.slice(0, 240);
    }

    function grid() {
        return document.querySelector('[role="navigation"] [role="grid"]');
    }

    function scan() {
        const list = grid();
        if (!list) return;
        const post = state.baselined;
        const seen = loadSeen();
        let changed = false;
        const links = list.querySelectorAll('a[href*="/t/"]');
        const handled = new Set();
        for (let i = 0; i < links.length; i++) {
            const link = links[i];
            const row = link.closest('[role="row"]') || link;
            if (handled.has(row)) continue;
            handled.add(row);
            if (!rowLooksNew(row)) continue;
            const href = threadPath(link);
            if (!href) continue;
            const name = chatName(link);
            if (!name) continue;
            const preview = previewOf(link, name);
            const key = href + '\n' + preview;
            if (seen.has(key)) continue;
            seen.add(key);
            changed = true;
            if (post) {
                chrome.webview.postMessage({
                    type: 'messenger-chat-list-activity',
                    name: name,
                    href: href,
                    preview: preview
                });
            }
        }
        if (links.length > 0) state.baselined = true;
        if (changed) saveSeen(seen);
    }

    function schedule() {
        if (state.timer) return;
        state.timer = setTimeout(() => {
            state.timer = 0;
            bind();
            scan();
        }, 200);
    }

    function bind() {
        const list = grid();
        const target = list || document.documentElement;
        if (!target || state.target === target) return;
        if (state.observer) state.observer.disconnect();
        state.observer = new MutationObserver(schedule);
        state.observer.observe(target, {
            subtree: true,
            childList: true,
            characterData: true,
            attributes: true
        });
        state.target = target;
    }

    function stop() {
        if (state.observer) {
            state.observer.disconnect();
            state.observer = null;
        }
        state.target = null;
        if (state.timer) {
            clearTimeout(state.timer);
            state.timer = 0;
        }
    }

    function start() {
        stop();
        let hadStored = false;
        try {
            hadStored = !!sessionStorage.getItem(storageKey);
        } catch {
            hadStored = false;
        }
        state.baselined = hadStored;
        bind();
        scan();
    }

    state.start = start;
    state.stop = stop;
    window.__messengerChatListWatch = state;
    start();
})();
