# MessengerDesktop architecture

A Windows shell around Facebook Messenger. The app opens `https://www.facebook.com/messages/t` in WebView2, keeps the Facebook sign-in profile under `%LOCALAPPDATA%\MessengerDesktop\WebView2`, and shows reels in a second WebView so closing one returns to the same chat without reloading it. An X or Twitter status link opens in a third WebView with its own profile, `%LOCALAPPDATA%\MessengerDesktop\WebView2-X`. Esc or Back to chat hides that overlay and leaves the same chat loaded.

Closing the window hides it to the notification area and leaves the WebViews loaded. A second launch brings that window forward. Exit on the tray menu ends the process.

PR2 is split. PR2a is the .NET 10 retarget and the stack decision below. PR2b adds the settings store, single instance, tray, and Start with Windows. The approved task list is [ROADMAP.md](ROADMAP.md).

## Current modules

| Module | File | Responsibility |
| --- | --- | --- |
| Entry point and chat host | `Program.cs` | WinForms window, maximized start, chat WebView2, reel and X overlay open/close, navigation guards, DevTools history return |
| Page scripts | `Scripts/ChatReel.js`, `Scripts/ChatStatus.js`, `Scripts/XLinkPreview.js`, `Scripts/ReelClose.js`, `Scripts/ReelLayout.js`, loaded by `EmbeddedScripts.cs` | Same document-created scripts as before, plus status clicks and the preview card. Chat WebView gets close + chat + status + preview scripts. Reel WebView gets close + layout scripts. The X WebView gets none of these |
| Reel sound | `ReelSoundScript.cs` | Document-created script that sets the playing reel to half volume on the user's open gesture. Still injected only into the reel WebView |
| Service profile | `ServiceDefinition.cs` | `ServiceDefinition`: name, WebView2 profile folder, home URL, link rules, and tab or overlay. Facebook is a tab on the existing `WebView2` folder and `https://www.facebook.com/messages/t`, so an upgrade keeps the current login. X is an overlay on `WebView2-X` |
| X links | `XLinkCapture.cs` | x.com and twitter.com status URLs, including `/i/web/status/`. `l.facebook.com` `u=` shims are unwrapped. Other X pages and lookalike hosts stay in the chat |
| X previews | `XPreview.cs`, `Scripts/XLinkPreview.js` | The chat page asks the host, over `postMessage`, about a status link card and sends its visible text and image. The host replaces the card only when it is empty: no title or description, a telescope placeholder image, or an English or Bulgarian failure label such as "No preview available" or "Няма наличен преглед". A card that already has a title or description is left alone and is not fetched. The fetch is `https://publish.twitter.com/oembed?url=...&omit_script=1` with `HttpClient`, no cookie jar and no API key, follows the redirect only to `publish.x.com`, and gives up after 4 seconds. Results are cached in memory. The page shows the author, @handle, plain text taken out of the oEmbed HTML, and an x.com label. The HTML is not inserted. A failed lookup leaves Facebook's card. Clicking the new card uses the existing status overlay. The tray item "X link previews" defaults on and is stored in settings |
| Overlay host | `OverlayHost.cs` | Loading cover, spinner, fade after first paint, 8 second timeout, Esc, and Back to chat, for a service and a URL. Reel close and history stay in the reel types. The cover is a separate owned window because WebView2 paints over ordinary WinForms siblings |
| Load coordinator | `ReelLoadCoordinator.cs` | Whether the cover is visible, fading, or hidden. Generation and epoch drop late WebView2 callbacks. Unit-tested |
| Return tracker | `ReelReturnTracker.cs` | Remembers the chat for the chat → optional share link → reel flow, and whether close may return there |
| History selector | `HistoryEntrySelector.cs` | Picks a nearby back-history entry for that chat so return does not reload the page |
| Reel message | `ReelMessage.cs` | Parses a `messenger-open-reel` web message and checks the target is a reel or share URL |
| Settings store | `SettingsStore.cs` | Typed JSON at `%LOCALAPPDATA%\MessengerDesktop\settings.json`. Save writes a temp file beside it and replaces the real file. A corrupt file is moved to `settings.json.bak` and defaults load |
| Launch arguments | `LaunchArgs.cs` | Recognizes `--minimized` |
| Single instance | `SingleInstance.cs` | Named mutex `Local\MessengerDesktop.SingleInstance` and auto-reset event `Local\MessengerDesktop.Activate`. A second process allows this one to take the foreground, signals the event, and exits |
| Start with Windows | `StartupRegistration.cs`, `WindowsRunKey.cs` | HKCU `Software\Microsoft\Windows\CurrentVersion\Run` value `MessengerDesktop`: quoted exe path plus `--minimized`. On when the settings file is missing. The tray check reads the registry. A value that still points at another path is rewritten |
| Tray | `Program.cs` | NotifyIcon menu: Open, Start with Windows (checkable), X link previews (checkable, on by default), Exit. The window X hides the form. WebView2 is disposed only on Exit |
| Background throttling | `BackgroundThrottling.cs` | Browser arguments passed to `CoreWebView2Environment.CreateAsync` so a hidden window keeps running |
| Notification permission | `NotificationOrigins.cs`, wired in `Program.cs` | Auto-grant WebView2 notifications for `https` `facebook.com` and `messenger.com` origins, including subdomains. The same decision is saved on the WebView2 profile for the common origins, and `PermissionRequested` on the page and on iframes answers Allow for those origins. Every other origin is denied. WebView2 leaves Notifications at Default, which denies them and shows no prompt |
| Notification capture | `NotificationRecord.cs`, `NotificationShim.cs`, `NotificationPageMessage.cs`, `Program.cs` | `CoreWebView2.NotificationReceived` (WebView2 SDK 1.0.4314.35) for a non-persistent `Notification()` call, and a document-created shim for that constructor and for `ServiceWorkerRegistration.showNotification`. Persistent `showNotification` calls do not raise `NotificationReceived`. One record: title, body, icon (`IconUri`, otherwise `BadgeUri`), tag, url, service (`facebook` or `messenger`). The WebView2 notification has no URL, so `url` is empty unless the page shim supplies one |
| Toast presenter | `OsToast.cs`, `NotificationToast.cs`, `NotificationImageCache.cs`, `AppIconCache.cs`, `NotificationDeduper.cs`, `NotificationArguments.cs`, `NotificationNavigation.cs`, `ToastBridge.cs` | Windows Community Toolkit `Microsoft.Toolkit.Uwp.Notifications` 7.1.3 shows an OS toast, so Focus Assist applies. The title is the sender name, the body is the first 120 characters, and the image is the sender picture saved under the temp directory, or the app icon when that picture cannot be loaded. The same tag is toasted once. A click restores the window and opens a chat URL from the arguments, otherwise the inbox. If the OS toast cannot be created, the tray balloon is used |
| Unread badge | `UnreadTitle.cs`, `TaskbarBadge.cs` | Page title `(N)` or `(N+)` becomes the tray tooltip and an `ITaskbarList3` overlay icon |
| Reaction watch | `ReactionWatch.cs`, `NotificationPipeline.cs` | After the first page load, an unread increase with no message toast inside 2 seconds becomes one "New activity" toast. A message toast inside that window suppresses it. Further increases in that window wait, so a burst is one toast |

`tests/ReelLoad.Tests` compiles `ReelLoadCoordinator.cs` and runs the xUnit reel-load cases. It does not host WebView2. `tests/Lifetime.Tests` compiles the settings, launch, startup, and throttling types. It does not host WinForms, WebView2, or the registry. `tests/Notification.Tests` compiles the notification types above except `OsToast.cs`, `AppIconCache.cs`, and `TaskbarBadge.cs`. It does not host WinForms, WebView2, or the toast runtime. The toast tests download a picture through a stub handler and print the toast payload. `tests/Service.Tests` compiles the service profile, X link, and X preview types. It does not host WinForms or WebView2. The preview tests stub `HttpClient` and do not call the network.

## Background throttling

The close button hides the form. It does not dispose the WebView2 controls. Chromium still treats a hidden or occluded window as background: it freezes the renderer and throttles timers, which stalls Messenger.

The WebView2 environment is created with `BackgroundThrottling.BrowserArguments`:

```text
--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --disable-features=CalculateNativeWinOcclusion
```

`disable-background-timer-throttling` keeps `setTimeout` / `setInterval` from slowing down. `disable-renderer-backgrounding` and `disable-backgrounding-occluded-windows` keep the renderer from being treated as a background tab. `CalculateNativeWinOcclusion` is the Chromium check that freezes a window Windows reports as covered or hidden; turning that feature off leaves the page running after the form is hidden. Messages can still arrive. Exit disposes the tray icon and the WebView2 controls, which ends the WebView processes.

A hidden live WebView uses more power than a throttled one. That is the trade for staying signed in. Toasts and the unread badge are separate from these switches: the page has to keep running so a message can raise a web notification and so the title can change.

## Notification coverage

| Kind | What the app does | Confidence |
| --- | --- | --- |
| Messages | A page notification (`NotificationReceived` or the shim for `showNotification`) becomes one OS toast: sender name, avatar or the app icon, and the first 120 characters. Deduped by tag. Click restores the window and opens a chat URL from the toast arguments, otherwise the inbox | Yes |
| Reactions | Unread count in the page title goes up and no message toast arrives within 2 seconds: one "New activity" toast | Best-effort |
| Calls | Not detected | Untested |

The app target framework is `net10.0-windows10.0.17763.0` so the Windows Community Toolkit can call the OS toast APIs. `global.json` still pins the .NET 10 SDK. The toolkit registers the unpackaged app under `HKCU\Software\Classes\AppUserModelId`. Windows 10 1903 and later accept that registration, so this app does not create a Start menu shortcut. A shortcut would be required only for an older toast API. The test projects stay on `net10.0-windows` because they do not call those APIs.

OS toasts are suppressed by Focus Assist. The tray balloon is the fallback when `ToastNotificationManagerCompat` cannot register or show, and that balloon does not consult Focus Assist. Skype-style pop-ups are not implemented. The design is [popup-notifications-plan.md](popup-notifications-plan.md).

## Planned modules

Not implemented yet. Names match [ROADMAP.md](ROADMAP.md).

| Module | Roadmap | Role |
| --- | --- | --- |
| Instagram tab | PR-IG, T17–T18 | Title-bar tabs and an Instagram Direct service with its own profile |

## ADR-001: Stay on .NET 10, WinForms, and WebView2

- Status: accepted, 2026-10-29.
- Decision: keep WinForms and WebView2. Tests target `net10.0-windows`. The app targets `net10.0-windows10.0.17763.0` so it can call OS toasts. `global.json` still selects the .NET 10 SDK.

.NET 10 is the current LTS release, supported until November 2028 (published end date 14 November 2028). .NET 8 LTS ends 10 November 2026, so it is not the base for this app. .NET 7, the previous target, is already out of support. `global.json` pins SDK `10.0.100` with `rollForward` `latestFeature`, so a machine that still has the .NET 7 SDK does not build this repo with that SDK. CI installs the SDK with `actions/setup-dotnet` and `dotnet-version: 10.0.x`.

Standard Windows 10 22H2 is outside Microsoft's official .NET 10 support list. That list covers Windows 11 and Windows 10 LTSC/Enterprise releases (21H2, 1809, and 1607). WinForms and WebView2 work on standard Windows 10 22H2 in practice. The README still lists Windows 10 or 11.

### Alternatives considered

- WinUI 3 / Windows App SDK. A current native Windows UI stack, and a better fit later for OS toasts (AUMID and App Notifications). It would replace a working WinForms host and the reel overlay for a window whose content is a web page. Not chosen.
- Tauri / Rust. A small webview shell in another language. It would drop the C# reel state machine, the xUnit tests, and the WinForms cover. Not chosen.
- Avalonia. A cross-platform UI toolkit. This app is a Windows WebView2 shell with a WinForms cover window, and Avalonia would mean reimplementing that host. Not chosen.

Revisit the stack when a planned module cannot be done on WinForms + WebView2. The notification presenter calls the Windows Community Toolkit (`Microsoft.Toolkit.Uwp.Notifications` 7.1.3), which posts OS toasts. That is a library addition, not a UI rewrite. The Windows App SDK was the other option and was not required.
