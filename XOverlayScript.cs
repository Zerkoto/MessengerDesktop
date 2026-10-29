namespace MessengerDesktop;

// Keys and the X logo position inside the X page. Esc and Alt+Left ask the
// host to close. The topmost Home link is the logo the back pill covers.
internal static class XOverlayScript
{
    public const string Source = """
        (() => {
            if (window !== window.top) return;
            window.addEventListener('keydown', event => {
                if (!event.isTrusted || event.repeat || event.ctrlKey || event.metaKey || event.shiftKey) return;
                const escape = event.key === 'Escape' && !event.altKey;
                const altLeft = event.altKey && event.key === 'ArrowLeft';
                if (!escape && !altLeft) return;
                event.preventDefault();
                event.stopImmediatePropagation();
                chrome.webview.postMessage('messenger-overlay-escape');
            }, true);
            let posted = '';
            let frame = 0;
            const started = performance.now();
            const report = () => {
                frame = 0;
                const links = document.querySelectorAll('a[href="/home"], a[href="https://x.com/home"], a[href="https://twitter.com/home"], a[aria-label="X"]');
                let best = null;
                for (const link of links) {
                    const rect = link.getBoundingClientRect();
                    if (rect.width < 8 || rect.height < 8) continue;
                    if (!best || rect.top < best.top) best = rect;
                }
                if (!best) return;
                const dpr = window.devicePixelRatio || 1;
                const next = JSON.stringify({
                    type: 'messenger-x-logo',
                    x: best.left * dpr,
                    y: best.top * dpr,
                    width: best.width * dpr,
                    height: best.height * dpr
                });
                if (next === posted) return;
                posted = next;
                chrome.webview.postMessage(next);
            };
            const schedule = () => {
                if (frame) return;
                frame = requestAnimationFrame(report);
            };
            const watch = new MutationObserver(() => {
                if (performance.now() - started > 8000) {
                    watch.disconnect();
                    return;
                }
                schedule();
            });
            report();
            window.addEventListener('resize', schedule);
            watch.observe(document.documentElement, { childList: true, subtree: true });
        })();
        """;
}
