(() => {
    if (window !== window.top || location.protocol !== 'https:' || location.hostname !== 'www.facebook.com') return;
    window.addEventListener('click', event => {
        if (!event.isTrusted || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const path = location.pathname;
        if (!path.startsWith('/messages/t/') && !path.startsWith('/messages/e2ee/t/')) return;
        const target = event.target instanceof Element ? event.target : event.target?.parentElement;
        const link = target?.closest('a[href]');
        if (!link || link.hasAttribute('download')) return;
        const url = new URL(link.href);
        if (url.protocol !== 'https:' || url.hostname !== 'www.facebook.com' || url.port ||
            (!url.pathname.startsWith('/share/r/') && !url.pathname.startsWith('/reel/'))) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        chrome.webview.postMessage({ type: 'messenger-open-reel', url: url.href });
    }, true);
})();
