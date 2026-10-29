(() => {
    if (window !== window.top || location.protocol !== 'https:' || location.hostname !== 'www.facebook.com') return;
    document.addEventListener('DOMContentLoaded', () => {
        const style = document.createElement('style');
        style.textContent = `
            :root:has(a[aria-label="Close viewer and return to feed"]) { --header-height: 0px !important; }
            :root:has(a[aria-label="Close viewer and return to feed"]) [role="banner"] { display: none !important; }
        `;
        document.head.appendChild(style);
    }, { once: true });
})();
