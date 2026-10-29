# MessengerDesktop architecture

A Windows shell around Facebook Messenger. The app opens `https://www.facebook.com/messages/t` in WebView2, keeps the sign-in profile under `%LOCALAPPDATA%\MessengerDesktop\WebView2`, and shows reels in a second WebView so closing one returns to the same chat without reloading it.

Closing the window hides it to the notification area and leaves both WebViews loaded. A second launch brings that window forward. Exit on the tray menu ends the process.

PR2 is split. PR2a is the .NET 10 retarget and the stack decision below. PR2b adds the settings store, single instance, tray, and Start with Windows. The approved task list is [ROADMAP.md](ROADMAP.md).

## Current modules

| Module | File | Responsibility |
| --- | --- | --- |
| Entry point and chat host | `Program.cs` | WinForms window, maximized start, chat WebView2, reel open/close, navigation guards, DevTools history return |
| Page scripts | `Scripts/ChatReel.js`, `Scripts/ReelClose.js`, `Scripts/ReelLayout.js`, loaded by `EmbeddedScripts.cs` | Same document-created scripts as before: chat clicks that should open a reel, the reel viewer's feed close link, and the reel header layout. Chat WebView gets close + chat scripts. Reel WebView gets close + layout scripts |
| Reel sound | `ReelSoundScript.cs` | Document-created script that sets the playing reel to half volume on the user's open gesture. Still injected only into the reel WebView |
| Reel overlay | `ReelOverlay.cs` | Loading cover, spinner, fade after first paint, 8 second timeout, failure "Back to chat". The cover is a separate owned window because WebView2 paints over ordinary WinForms siblings |
| Load coordinator | `ReelLoadCoordinator.cs` | Whether the cover is visible, fading, or hidden. Generation and epoch drop late WebView2 callbacks. Unit-tested |
| Return tracker | `ReelReturnTracker.cs` | Remembers the chat for the chat → optional share link → reel flow, and whether close may return there |
| History selector | `HistoryEntrySelector.cs` | Picks a nearby back-history entry for that chat so return does not reload the page |
| Reel message | `ReelMessage.cs` | Parses a `messenger-open-reel` web message and checks the target is a reel or share URL |
| Settings store | `SettingsStore.cs` | Typed JSON at `%LOCALAPPDATA%\MessengerDesktop\settings.json`. Save writes a temp file beside it and replaces the real file. A corrupt file is moved to `settings.json.bak` and defaults load |
| Launch arguments | `LaunchArgs.cs` | Recognizes `--minimized` |
| Single instance | `SingleInstance.cs` | Named mutex `Local\MessengerDesktop.SingleInstance` and auto-reset event `Local\MessengerDesktop.Activate`. A second process allows this one to take the foreground, signals the event, and exits |
| Start with Windows | `StartupRegistration.cs`, `WindowsRunKey.cs` | HKCU `Software\Microsoft\Windows\CurrentVersion\Run` value `MessengerDesktop`: quoted exe path plus `--minimized`. On when the settings file is missing. The tray check reads the registry. A value that still points at another path is rewritten |
| Tray | `Program.cs` | NotifyIcon menu: Open, Start with Windows (checkable), Exit. The window X hides the form. WebView2 is disposed only on Exit |
| Background throttling | `BackgroundThrottling.cs` | Browser arguments passed to `CoreWebView2Environment.CreateAsync` so a hidden window keeps running |

`tests/ReelLoad.Tests` compiles `ReelLoadCoordinator.cs` and runs the xUnit reel-load cases. It does not host WebView2. `tests/Lifetime.Tests` compiles the settings, launch, startup, and throttling types. It does not host WinForms, WebView2, or the registry.

## Background throttling

The close button hides the form. It does not dispose either WebView2 control. Chromium still treats a hidden or occluded window as background: it freezes the renderer and throttles timers, which stalls Messenger.

The WebView2 environment is created with `BackgroundThrottling.BrowserArguments`:

```text
--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --disable-features=CalculateNativeWinOcclusion
```

`disable-background-timer-throttling` keeps `setTimeout` / `setInterval` from slowing down. `disable-renderer-backgrounding` and `disable-backgrounding-occluded-windows` keep the renderer from being treated as a background tab. `CalculateNativeWinOcclusion` is the Chromium check that freezes a window Windows reports as covered or hidden; turning that feature off leaves the page running after the form is hidden. Messages can still arrive. Exit disposes the tray icon and the WebView2 controls, which ends the WebView processes.

This does not add toasts or a badge. A hidden live WebView uses more power than a throttled one. That is the trade for staying signed in.

## Planned modules

Not implemented yet. Names match [ROADMAP.md](ROADMAP.md).

| Module | Roadmap | Role |
| --- | --- | --- |
| Notification pipeline | PR3, T6–T11 | Permission, capture, presenter, unread badge, reaction heuristic. Design notes only for Skype-style pop-ups |
| Service-profile layer | PR-X1, T12 | `ServiceDefinition` (name, WebView2 profile, home URL, link rules, tab or overlay). Facebook moves onto it without a behaviour change |
| Overlay host | PR-X1, T13 | Generalize the reel cover, spinner, 8 second timeout, and Esc/close. Reel close and history stay in the reel service |

## ADR-001: Stay on .NET 10, WinForms, and WebView2

- Status: accepted, 2026-10-29.
- Decision: keep WinForms and WebView2, and target `net10.0-windows` for the app and the tests.

.NET 10 is the current LTS release, supported until November 2028 (published end date 14 November 2028). .NET 8 LTS ends 10 November 2026, so it is not the base for this app. .NET 7, the previous target, is already out of support. `global.json` pins SDK `10.0.100` with `rollForward` `latestFeature`, so a machine that still has the .NET 7 SDK does not build this repo with that SDK. CI installs the SDK with `actions/setup-dotnet` and `dotnet-version: 10.0.x`.

Standard Windows 10 22H2 is outside Microsoft's official .NET 10 support list. That list covers Windows 11 and Windows 10 LTSC/Enterprise releases (21H2, 1809, and 1607). WinForms and WebView2 work on standard Windows 10 22H2 in practice. The README still lists Windows 10 or 11.

### Alternatives considered

- WinUI 3 / Windows App SDK. A current native Windows UI stack, and a better fit later for OS toasts (AUMID and App Notifications). It would replace a working WinForms host and the reel overlay for a window whose content is a web page. Not chosen.
- Tauri / Rust. A small webview shell in another language. It would drop the C# reel state machine, the xUnit tests, and the WinForms cover. Not chosen.
- Avalonia. A cross-platform UI toolkit. This app is a Windows WebView2 shell with a WinForms cover window, and Avalonia would mean reimplementing that host. Not chosen.

Revisit the stack when a planned module cannot be done on WinForms + WebView2. The notification presenter (roadmap T8) may call Windows App SDK toast APIs from this process; that is a library addition, not a UI rewrite.
