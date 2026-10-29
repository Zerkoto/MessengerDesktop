(() => {
    if (window !== window.top || location.protocol !== 'https:' || location.hostname !== 'www.facebook.com') return;
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
    let overlayArmed = false;
    let statusArmed = false;
    chrome.webview.addEventListener('message', event => {
        if (event.data === 'messenger-overlay-armed') { overlayArmed = true; statusArmed = false; }
        if (event.data === 'messenger-status-armed') { overlayArmed = true; statusArmed = true; }
        if (event.data === 'messenger-overlay-idle') { overlayArmed = false; statusArmed = false; }
    });
    window.addEventListener('keydown', event => {
        if (!overlayArmed || !event.isTrusted || event.repeat || event.ctrlKey || event.metaKey || event.shiftKey) return;
        const escape = event.key === 'Escape' && !event.altKey;
        const altLeft = statusArmed && event.altKey && event.key === 'ArrowLeft';
        if (!escape && !altLeft) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        chrome.webview.postMessage('messenger-overlay-escape');
    }, true);
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
    window.addEventListener('click', event => {
        if (!event.isTrusted || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const path = location.pathname;
        if (!path.startsWith('/messages/t/') && !path.startsWith('/messages/e2ee/t/')) return;
        const target = event.target instanceof Element ? event.target : event.target?.parentElement;
        const link = target?.closest('a[href]');
        if (!link || link.hasAttribute('download')) return;
        let url;
        try { url = new URL(link.href); }
        catch { return; }
        if (!isStatus(url, 0)) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        chrome.webview.postMessage({ type: 'messenger-open-status', url: url.href });
    }, true);
})();
