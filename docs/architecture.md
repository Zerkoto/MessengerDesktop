# MessengerDesktop architecture

A Windows shell around Facebook Messenger. The app opens `https://www.facebook.com/messages/t` in WebView2, keeps the sign-in profile under `%LOCALAPPDATA%\MessengerDesktop\WebView2`, and shows reels in a second WebView so closing one returns to the same chat without reloading it.

Closing the window hides it to the notification area and leaves both WebViews loaded. A second launch brings that window forward. Exit on the tray menu ends the process.

PR2 is split. PR2a is the .NET 10 retarget and the stack decision below. PR2b adds the settings store, single instance, tray, and Start with Windows. The approved task list is [ROADMAP.md](ROADMAP.md).

## Current modules

| Module | File | Responsibility |
| --- | --- | --- |
| Entry point and chat host | `Program.cs` | WinForms window, maximized start, chat WebView2, reel open/close, navigation guards, DevTools history return |
| Page scripts | `Scripts/ChatReel.js`, `Scripts/ReelClose.js`, `Scripts/ReelLayout.js`, `Scripts/ChatListWatch.js`, loaded by `EmbeddedScripts.cs` | Same document-created scripts as before: chat clicks that should open a reel, the reel viewer's feed close link, and the reel header layout. Chat WebView gets close + chat scripts. Reel WebView gets close + layout scripts. `ChatListWatch.js` is added only to the Facebook chat WebView, and only while the tray setting is on |
| Reel sound | `ReelSoundScript.cs` | Document-created script that sets the playing reel to half volume on the user's open gesture. Still injected only into the reel WebView |
| Reel overlay | `ReelOverlay.cs` | Loading cover, spinner, fade after first paint, 8 second timeout, failure "Back to chat". The cover is a separate owned window because WebView2 paints over ordinary WinForms siblings |
| Load coordinator | `ReelLoadCoordinator.cs` | Whether the cover is visible, fading, or hidden. Generation and epoch drop late WebView2 callbacks. Unit-tested |
| Return tracker | `ReelReturnTracker.cs` | Remembers the chat for the chat → optional share link → reel flow, and whether close may return there |
| History selector | `HistoryEntrySelector.cs` | Picks a nearby back-history entry for that chat so return does not reload the page |
| Reel message | `ReelMessage.cs` | Parses a `messenger-open-reel` web message and checks the target is a reel or share URL |
| Settings store | `SettingsStore.cs` | Typed JSON at `%LOCALAPPDATA%\MessengerDesktop\settings.json`. Save writes a temp file beside it and replaces the real file. A corrupt file is moved to `settings.json.bak` and defaults load. `watchChatListForReactions` defaults to false |
| Launch arguments | `LaunchArgs.cs` | Recognizes `--minimized` |
| Single instance | `SingleInstance.cs` | Named mutex `Local\MessengerDesktop.SingleInstance` and auto-reset event `Local\MessengerDesktop.Activate`. A second process allows this one to take the foreground, signals the event, and exits |
| Start with Windows | `StartupRegistration.cs`, `WindowsRunKey.cs` | HKCU `Software\Microsoft\Windows\CurrentVersion\Run` value `MessengerDesktop`: quoted exe path plus `--minimized`. On when the settings file is missing. The tray check reads the registry. A value that still points at another path is rewritten |
| Tray | `Program.cs`, `TrayMenu.cs`, `UnreadFlyout.cs`, `TraySummary.cs`, `TrayBadgePainter.cs` | Right-click menu (Open Messenger, mute, Watch chat list for reactions (beta), Start with Windows, Quit Messenger+) and a left-click unread summary. The 16px icon badge is messages plus unseen reactions. Mark all read clears only that local list. The window X hides the form. WebView2 is disposed only on Quit |
| Background throttling | `BackgroundThrottling.cs` | Browser arguments passed to `CoreWebView2Environment.CreateAsync` so a hidden window keeps running |
| Notification permission | `NotificationOrigins.cs`, wired in `Program.cs` | Auto-grant WebView2 notifications for `https` `facebook.com` and `messenger.com` origins, including subdomains. Every other origin is denied. The same decision is saved on the WebView2 profile for the common origins |
| Notification capture | `NotificationRecord.cs`, `Program.cs` | `CoreWebView2.NotificationReceived` (WebView2 SDK 1.0.4314.35). One record: title, body, icon, tag, url, service (`facebook` or `messenger`). The WebView2 notification has no URL, so `url` is empty unless a later caller supplies one |
| Toast presenter | `OsToast.cs`, `NotificationDeduper.cs`, `NotificationArguments.cs`, `NotificationNavigation.cs`, `ToastBridge.cs` | Windows Community Toolkit `Microsoft.Toolkit.Uwp.Notifications` 7.1.3 shows an OS toast, so Focus Assist applies. The same tag is toasted once. A click restores the window and opens a chat URL from the arguments, otherwise the inbox. If the OS toast cannot be created, the tray balloon is used |
| Unread badge | `UnreadTitle.cs`, `TaskbarBadge.cs`, `TraySummary.cs`, `TrayBadgePainter.cs` | Page title `(N)` or `(N+)` is still the taskbar overlay. The tray icon and tooltip use the local summary: unread messages plus reactions not yet seen, drawn into the 16px icon (`#E81123`, 9px, 1px white ring, `9+`). A mute replaces that with a grey dot |
| Reaction watch | `ReactionWatch.cs`, `NotificationPipeline.cs` | After the first page load, an unread increase with no message notification inside 2 seconds becomes one "New activity" toast. Further increases in that window wait, so a burst is one toast |
| Chat-list watch | `Scripts/ChatListWatch.js`, `ChatListWatch.cs`, `TraySummary.cs` | Optional, off by default. A `MutationObserver` on `[role=navigation] [role=grid]` reports a bold row with a thread link `a[href*='/t/']` once per href plus preview. The host adds a summary row labelled "New activity" with the chat name and counts it like a reaction. It does not toast unless reaction watch already would. The same rise inside the 2 second window is one count. The script only observes the DOM. The reel WebView and any future X profile do not get it |

`tests/ReelLoad.Tests` compiles `ReelLoadCoordinator.cs` and runs the xUnit reel-load cases. It does not host WebView2. `tests/Lifetime.Tests` compiles the settings, launch, startup, and throttling types. It does not host WinForms, WebView2, or the registry. `tests/Notification.Tests` compiles the notification types above except `OsToast.cs` and `TaskbarBadge.cs`, plus `TraySummary.cs` and `ChatListWatch.cs`. It does not host WinForms, WebView2, or the toast runtime. `TrayBadgePainter.cs`, `TrayMenu.cs`, and `UnreadFlyout.cs` are Windows UI and are not in the test project.

## Background throttling

The close button hides the form. It does not dispose either WebView2 control. Chromium still treats a hidden or occluded window as background: it freezes the renderer and throttles timers, which stalls Messenger.

The WebView2 environment is created with `BackgroundThrottling.BrowserArguments`:

```text
--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --disable-features=CalculateNativeWinOcclusion
```

`disable-background-timer-throttling` keeps `setTimeout` / `setInterval` from slowing down. `disable-renderer-backgrounding` and `disable-backgrounding-occluded-windows` keep the renderer from being treated as a background tab. `CalculateNativeWinOcclusion` is the Chromium check that freezes a window Windows reports as covered or hidden; turning that feature off leaves the page running after the form is hidden. Messages can still arrive. Exit disposes the tray icon and the WebView2 controls, which ends the WebView processes.

A hidden live WebView uses more power than a throttled one. That is the trade for staying signed in. Toasts and the unread badge are separate from these switches: the page has to keep running so a message can raise a web notification and so the title can change.

## Notification coverage

| Kind | What the app does | Confidence |
| --- | --- | --- |
| Messages | `NotificationReceived` becomes one OS toast, deduped by tag. Click restores the window and opens a chat URL from the toast arguments, otherwise the inbox | Yes |
| Reactions | Unread count in the page title goes up and no message notification arrives within 2 seconds: one "New activity" toast. The optional chat-list watch can also add that tray row from the chat list, without its own toast | Best-effort |
| Calls | Not detected | Untested |

The app target framework is `net10.0-windows10.0.17763.0` so the Windows Community Toolkit can call the OS toast APIs. `global.json` still pins the .NET 10 SDK. The toolkit registers the unpackaged app under `HKCU\Software\Classes\AppUserModelId`. Windows 10 1903 and later accept that registration, so this app does not create a Start menu shortcut. A shortcut would be required only for an older toast API. The test projects stay on `net10.0-windows` because they do not call those APIs.

OS toasts are suppressed by Focus Assist. The tray balloon is the fallback when `ToastNotificationManagerCompat` cannot register or show, and that balloon does not consult Focus Assist. Skype-style pop-ups are not implemented. The design is [popup-notifications-plan.md](popup-notifications-plan.md).

## Planned modules

Not implemented yet. Names match [ROADMAP.md](ROADMAP.md).

| Module | Roadmap | Role |
| --- | --- | --- |
| Service-profile layer | PR-X1, T12 | `ServiceDefinition` (name, WebView2 profile, home URL, link rules, tab or overlay). Facebook moves onto it without a behaviour change |
| Overlay host | PR-X1, T13 | Generalize the reel cover, spinner, 8 second timeout, and Esc/close. Reel close and history stay in the reel service |

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
