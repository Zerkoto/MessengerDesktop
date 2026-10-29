namespace MessengerDesktop;

// Esc inside an overlay page asks the host to close. The chat page uses its
// own listener so Esc still reaches Messenger when no overlay is open.
internal static class OverlayEscapeScript
{
    public const string Source = """
        (() => {
            if (window !== window.top) return;
            window.addEventListener('keydown', event => {
                if (!event.isTrusted || event.key !== 'Escape' || event.repeat) return;
                event.preventDefault();
                event.stopImmediatePropagation();
                chrome.webview.postMessage('messenger-overlay-escape');
            }, true);
        })();
        """;
}
