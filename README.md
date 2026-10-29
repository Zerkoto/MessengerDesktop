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
- A new message shows a Windows toast when Messenger is hidden or in the background. The same message is not toasted twice. Clicking the toast brings the window back and opens that chat when the notification has a chat link, otherwise the inbox
- Windows Focus Assist applies to those toasts. If Windows cannot show a toast, Messenger uses a tray balloon instead
- When the unread count goes up and no message notification arrived, Messenger shows one "New activity" toast after a short wait. That is the best-effort path for reactions
- The taskbar icon still shows the unread count from the page title, such as `(3) Messenger`

## Tray

Right-click the notification icon for the menu. It follows the Windows light or dark app theme: flat background, a 28px check column, 32px rows, and 12px of horizontal padding. The check is a plain glyph. On Windows 11 the menu has rounded corners and a soft shadow. On Windows 10 the corners stay square.

The menu order is:

- Messenger+, with a line such as `1 unread · 2 reactions` (not clickable)
- **Open Messenger**
- **Mute notifications** for 1 hour, 8 hours, or until you turn it back on. While that is on, the tray icon shows a small grey dot instead of the unread badge, and new toasts are not shown. Click the same choice again to turn notifications back on
- **Start with Windows**
- **Quit Messenger+**

Left-click opens an unread summary above the tray (340px wide, at most 420px tall). It uses the same light or dark theme, and it closes when it loses focus or you press Esc. The list is the five latest conversations already captured by message notifications: an avatar or initials, the name, one preview line, and a relative time. A notification with no preview shows the name and the count only. A reaction is kept as `❤️ reacted to your message`. The footer **Open Messenger** button brings the window forward. Double-clicking the tray icon still only opens the window.

**Mark all read** clears that summary on this PC only. It does not mark chats read on Facebook, click anything in the page, or send a read receipt. Unread messages leave the red badge when that chat is opened in the app. Reactions leave the badge as soon as the summary opens.

The badge is drawn on the 16px tray icon: a red `#E81123` circle about 9px across with a 1px white ring, bottom-right, and a white number. There is no badge at zero. Counts from 1 to 9 are digits. Above 9 the badge reads `9+`. The count is unread messages plus reactions you have not opened the summary for. The tooltip matches it, for example `Messenger+ · 3 unread`. While notifications are muted, that circle is replaced by a small grey dot.

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

- Message toasts depend on Messenger raising a web notification. Reactions are inferred from the unread count and can be wrong. Calls are not detected.
- A toast click opens the chat link carried on the notification. WebView2 does not provide that link, so the click opens the inbox unless the notification tag itself is a chat link.
- The tray balloon used when Windows toasts are unavailable does not honor Focus Assist.
- Calls, file downloads, and some sign-in flows depend on Facebook and WebView2, and are not fully tested.
- Reel load time is mostly limited by Facebook itself.

## Contributing

Issues and pull requests are welcome. Please describe the problem, your Windows version, and steps to reproduce.

## License

[MIT](LICENSE)
