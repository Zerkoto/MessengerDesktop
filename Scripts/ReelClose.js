(() => {
    if (window !== window.top || location.protocol !== 'https:' || location.hostname !== 'www.facebook.com') return;
    let canReturn = false;
    chrome.webview.addEventListener('message', event => {
        if (event.data === 'messenger-reel-enable') canReturn = true;
        if (event.data === 'messenger-reel-disable') canReturn = false;
    });
    chrome.webview.postMessage('messenger-reel-ready');
    window.addEventListener('click', event => {
        if (!canReturn || !event.isTrusted || !location.pathname.startsWith('/reel/')) return;
        const target = event.target instanceof Element ? event.target : event.target?.parentElement;
        const link = target?.closest('a');
        if (!link || link.href !== 'https://www.facebook.com/') return;
        const label = 'Close viewer and return to feed';
        const labelled = link.getAttribute('aria-label') === label || link.getAttribute('title') === label ||
            link.textContent.trim() === label || !!link.querySelector('[aria-label="Close viewer and return to feed"], [title="Close viewer and return to feed"]');
        if (!labelled) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        canReturn = false;
        chrome.webview.postMessage('messenger-reel-close');
    }, true);
})();
