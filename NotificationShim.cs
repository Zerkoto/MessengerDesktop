namespace MessengerDesktop;

// WebView2 raises NotificationReceived only for a non-persistent Notification
// constructor call. Messenger shows a new message with
// ServiceWorkerRegistration.showNotification, which does not raise that event.
// This script runs first on a facebook or messenger page and forwards both
// calls. The host still handles NotificationReceived when the runtime raises it.
internal static class NotificationShim
{
    public const string Source =
        """
        (() => {
          if (window.__messengerNotificationShim) return;
          window.__messengerNotificationShim = true;
          const host = location.hostname || '';
          const allowed = location.protocol === 'https:' && (
            host === 'facebook.com' || host.endsWith('.facebook.com') ||
            host === 'messenger.com' || host.endsWith('.messenger.com'));
          if (!allowed || !window.chrome || !chrome.webview || typeof chrome.webview.postMessage !== 'function') return;

          function post(title, options) {
            const opts = options && typeof options === 'object' ? options : {};
            const data = opts.data && typeof opts.data === 'object' ? opts.data : null;
            let url = '';
            if (data) {
              if (typeof data.url === 'string') url = data.url;
              else if (typeof data.href === 'string') url = data.href;
            }
            const payload = {
              title: title == null ? '' : String(title),
              body: typeof opts.body === 'string' ? opts.body : '',
              icon: typeof opts.icon === 'string' ? opts.icon : '',
              badge: typeof opts.badge === 'string' ? opts.badge : '',
              tag: typeof opts.tag === 'string' ? opts.tag : '',
              url: url
            };
            try { chrome.webview.postMessage('messenger-notification:' + JSON.stringify(payload)); }
            catch (error) { }
          }

          const Native = window.Notification;
          if (typeof Native === 'function') {
            function Wrapped(title, options) {
              post(title, options);
              try { return new Native(title, options); }
              catch (error) {
                return { close() {}, addEventListener() {}, removeEventListener() {} };
              }
            }
            Wrapped.requestPermission = function (callback) {
              const granted = Promise.resolve('granted');
              if (typeof callback === 'function') granted.then(callback);
              return granted;
            };
            try { Object.defineProperty(Wrapped, 'permission', { configurable: true, get: () => 'granted' }); }
            catch (error) { }
            try { Wrapped.prototype = Native.prototype; } catch (error) { }
            try { window.Notification = Wrapped; } catch (error) { }
          }

          const registration = window.ServiceWorkerRegistration && ServiceWorkerRegistration.prototype;
          if (registration && typeof registration.showNotification === 'function') {
            registration.showNotification = function (title, options) {
              post(title, options);
              return Promise.resolve();
            };
          }
        })();
        """;
}
