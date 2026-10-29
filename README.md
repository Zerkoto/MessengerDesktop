# Messenger Desktop

A lightweight, unofficial Windows desktop app for [Facebook Messenger](https://www.facebook.com/messages), built on .NET and Microsoft Edge WebView2.

Meta retired its official Messenger desktop app. This project gives you a focused, app-style window that opens straight into your chats instead of the Facebook home page.

> **Disclaimer:** This project is not affiliated with, endorsed by, or sponsored by Meta Platforms, Inc. "Messenger" and the Messenger logo are trademarks of Meta. This app only displays the official website inside WebView2.

## Features

- Opens directly into Messenger chats, not the Facebook feed
- Opening a reel shows a loading overlay until the page is ready, then fades it out
- Closing a reel returns you to the chat you were in, without reloading the page
- Reel viewer is kept ready for faster opening
- Starts maximized, with its own taskbar icon
- Separate sign-in profile, isolated from your Edge browser
- Closing the window hides Messenger to the notification area. The chat stays loaded, so messages keep arriving
- Exit quits Messenger and leaves no process running
- Starting Messenger again while it is already running brings the existing window forward
- Start with Windows is on the first time you run the app. Windows then starts it minimized, with `--minimized`. The menu check matches the startup entry. If the program moves, that entry is updated to the new path
- A new message shows a Windows toast with the sender's name, the sender's picture, and the start of the message (about 120 characters). If the picture cannot be loaded, the toast uses the app icon. The same message is not toasted twice. Clicking the toast brings the window back and opens that chat when the notification has a chat link, otherwise the inbox
- Windows Focus Assist applies to those toasts. If Windows cannot show a toast, Messenger uses a tray balloon instead
- When the unread count goes up and no message toast arrived within about two seconds, Messenger shows one "New activity" toast. That is the best-effort path for reactions. A message toast inside that wait takes its place
- The taskbar icon still shows the unread count from the page title, such as `(3) Messenger`

## Tray

Right-click the notification icon for the Windows menu. It is the native popup, so it follows the system light or dark theme and the monitor DPI. The app asks Windows to allow dark menus at startup.

The menu order is:

- **Open Messenger** (the bold default)
- **Mute notifications**, opening beside the menu: 1 hour, 8 hours, until you turn it back on, and **Unmute**. While mute is on, the tray icon shows a small grey dot instead of the unread badge, and new toasts are not shown. Choosing the active duration again, or **Unmute**, turns notifications back on
- **Start with Windows**
- **Quit Messenger+**

Left-click opens an unread summary just above the taskbar, lined up with the tray icon (340px wide at 100% DPI, at most 420px tall, and those sizes scale with the monitor). It uses the Windows light or dark theme, rounded corners, and it closes when it loses focus or you press Esc. The header is **Unread** with a count, and **Mark all read**. The list is the five latest conversations already captured by message notifications: an avatar or initials, the name, one preview line, and a relative time. A notification with no preview shows the name and the count only. A reaction reads `Reacted ❤️ to your message`. The chat-list watcher row reads `New activity`. An empty list says you're all caught up. The footer **Open Messenger** button brings the window forward. Double-clicking the tray icon still only opens the window.

**Mark all read** clears that summary on this PC only. It does not mark chats read on Facebook, click anything in the page, or send a read receipt. Unread messages leave the red badge when that chat is opened in the app. Reactions leave the badge as soon as the summary opens.

The badge is drawn at the tray's small-icon size for the current DPI (16px at 100%, 24px at 150%), not stretched up from 16px. The red `#E81123` circle covers about 60% of that icon, with a bold white digit. There is no badge at zero. Counts from 1 to 9 are digits. Above 9 the badge reads `9+`. The count is unread messages plus reactions you have not opened the summary for. The tooltip matches it, for example `Messenger+ · 3 unread`. The grey dot is only shown while notifications are muted.

## Requirements

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`winget install Microsoft.DotNet.SDK.10`)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (preinstalled on most Windows 11 machines)

## Build and run

```powershell
git clone https://github.com/Zerkoto/MessengerDesktop.git
cd MessengerDesktop
dotnet build -c Release
.\bin\Release\net10.0-windows10.0.17763.0\Messenger.exe
```

## Privacy

- You sign in to Facebook yourself, inside the app.
- Your session is stored locally in `%LOCALAPPDATA%\MessengerDesktop\WebView2`, separately from Edge.
- Settings are stored in `%LOCALAPPDATA%\MessengerDesktop\settings.json`. That file has no cookies or messages.
- Start with Windows is a per-user registry value (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `MessengerDesktop`). It is not a scheduled task.
- The app does not read, copy, or send your browser cookies, credentials, or messages anywhere.
- Notification text is shown only on this PC, through a Windows toast or a tray balloon. It is not uploaded.

## Known limitations

- Message toasts depend on Messenger creating a web notification. WebView2 reports only non-persistent notifications on `NotificationReceived`, so the app also forwards `showNotification`. Reactions are inferred from the unread count and can be wrong. Calls are not detected.
- A toast click opens the chat link carried on the notification. WebView2 does not provide that link, so the click opens the inbox unless the notification tag itself is a chat link.
- The tray balloon used when Windows toasts are unavailable does not honor Focus Assist.
- Calls, file downloads, and some sign-in flows depend on Facebook and WebView2, and are not fully tested.
- Reel load time is mostly limited by Facebook itself.

## Contributing

Issues and pull requests are welcome. Please describe the problem, your Windows version, and steps to reproduce.

## License

[MIT](LICENSE)
