namespace MessengerDesktop;

// Apply the requested default only to a reel that is actually playing in the
// viewport. Facebook may also keep other videos ready outside the viewport.
internal static class ReelSoundScript
{
    public const string Source = """
        (() => {
            if (window !== window.top || location.protocol !== 'https:' || location.hostname !== 'www.facebook.com') return;
            const applied = new Set();
            const adjustedByUser = new Set();
            const requested = new Set();
            let scheduled = false;
            let observer;
            let pending;

            const reelPath = () => location.pathname.startsWith('/reel/') ? location.pathname : null;
            const isVisible = video => {
                const rect = video.getBoundingClientRect();
                return rect.width > 0 && rect.height > 0 && rect.bottom > 0 && rect.right > 0 &&
                    rect.top < window.innerHeight && rect.left < window.innerWidth;
            };

            function activateDefault() {
                const candidate = pending;
                pending = undefined;
                if (!candidate) return;
                const { path, video } = candidate;
                if (reelPath() !== path || applied.has(path) || adjustedByUser.has(path) ||
                    video.paused || !isVisible(video)) return;
                // Use Facebook's control when present so its button state follows
                // the change, then set the media element's volume to 50%.
                const button = video.parentElement?.querySelector('[role="button"][aria-label="Unmute"]');
                if (video.muted && button) button.click();
                video.volume = 0.5;
                if (video.muted) video.muted = false;
                applied.add(path);
                observer?.disconnect();
                // WebView2 may pause muted autoplay when sound is enabled. The
                // host invokes this function with the user's reel-opening gesture.
                video.play().catch(() => {});

                // Facebook can update the media element after its click handler.
                // Never override a real user adjustment made in the meantime.
                requestAnimationFrame(() => {
                    if (reelPath() === path && !adjustedByUser.has(path) && !video.paused && isVisible(video)) {
                        video.volume = 0.5;
                    }
                });
            }

            Object.defineProperty(window, '__messengerActivateReelSound', { value: activateDefault });

            function requestDefault() {
                scheduled = false;
                const path = reelPath();
                if (!path || applied.has(path) || requested.has(path) || adjustedByUser.has(path)) return;
                const video = [...document.querySelectorAll('video')].find(video => !video.paused && isVisible(video));
                if (!video) return;
                pending = { path, video };
                requested.add(path);
                chrome.webview.postMessage('messenger-reel-sound-activate');
            }

            function schedule() {
                if (scheduled) return;
                scheduled = true;
                requestAnimationFrame(requestDefault);
            }

            function rememberUserAdjustment(event) {
                if (!event.isTrusted) return;
                const target = event.target instanceof Element ? event.target : event.target?.parentElement;
                if (target?.closest('[aria-label="Unmute"], [aria-label="Mute"], [aria-label="Change volume"]')) {
                    const path = reelPath();
                    if (path) adjustedByUser.add(path);
                }
            }

            document.addEventListener('pointerdown', rememberUserAdjustment, true);
            document.addEventListener('keydown', rememberUserAdjustment, true);
            document.addEventListener('playing', schedule, true);
            document.addEventListener('loadedmetadata', schedule, true);
            document.addEventListener('DOMContentLoaded', () => {
                observer = new MutationObserver(schedule);
                observer.observe(document.documentElement, { childList: true, subtree: true });
                schedule();
            }, { once: true });
        })();
        """;
}
