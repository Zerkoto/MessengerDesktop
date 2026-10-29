# Messenger Desktop

A lightweight, unofficial Windows desktop app for [Facebook Messenger](https://www.facebook.com/messages), built on .NET and Microsoft Edge WebView2.

Meta retired its official Messenger desktop app. This project gives you a focused, app-style window that opens straight into your chats instead of the Facebook home page.

> **Disclaimer:** This project is not affiliated with, endorsed by, or sponsored by Meta Platforms, Inc. "Messenger" and the Messenger logo are trademarks of Meta. This app only displays the official website inside WebView2.

## Features

- Opens directly into Messenger chats, not the Facebook feed
- Opening a reel shows a loading overlay until the page is ready, then fades it out
- Closing a reel returns you to the chat you were in, without reloading the page
- Clicking an X or Twitter status link in a chat opens it in an overlay. A link wrapped by `l.facebook.com` is unwrapped first. Esc, or Back to chat, closes the overlay and leaves that same chat loaded
- A status card that Facebook shows as "No preview available" is replaced with the author name, the @handle, the post text, and an x.com label. The text comes from the public oEmbed endpoint, with no API key. Clicking that card opens the same X overlay. If oEmbed does not answer, Facebook's card stays. The tray item "X link previews" is on by default and is stored in settings
- You sign in to X once inside the overlay. That sign-in is kept in its own profile
- Reel viewer is kept ready for faster opening
- Starts maximized, with its own taskbar icon
- Separate sign-in profile, isolated from your Edge browser
- Closing the window hides Messenger to the notification area. The chat stays loaded, so messages keep arriving
- The tray menu can open the window, turn Start with Windows on or off, or exit. A double-click on the icon also opens the window
- Exit quits Messenger and leaves no process running
- Starting Messenger again while it is already running brings the existing window forward
- Start with Windows is on the first time you run the app. Windows then starts it minimized, with `--minimized`. The menu check matches the startup entry. If the program moves, that entry is updated to the new path
- A new message shows a Windows toast with the sender's name, the sender's picture, and the start of the message (about 120 characters). If the picture cannot be loaded, the toast uses the app icon. The same message is not toasted twice. Clicking the toast brings the window back and opens that chat when the notification has a chat link, otherwise the inbox
- Windows Focus Assist applies to those toasts. If Windows cannot show a toast, Messenger uses a tray balloon instead
- The tray tooltip and the taskbar icon show the unread count from the page title, such as `(3) Messenger`
- When the unread count goes up and no message toast arrived within about two seconds, Messenger shows one "New activity" toast. That is the best-effort path for reactions. A message toast inside that wait takes its place

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
- Your Facebook session is stored locally in `%LOCALAPPDATA%\MessengerDesktop\WebView2`, separately from Edge.
- Your X session is stored locally in `%LOCALAPPDATA%\MessengerDesktop\WebView2-X`, separately from Messenger and from Edge.
- X link previews are requested by the app from `https://publish.twitter.com/oembed` (that host redirects to `https://publish.x.com/oembed`). The request does not use an API key, an X login, or the cookies stored in either WebView2 profile. Cookies set by that response are discarded. The Messenger page does not call oEmbed. The card is plain text; the oEmbed HTML is not inserted into the page.
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
- Only an X or Twitter status link opens in the X overlay. A profile, the home feed, or a lookalike host stays in the chat.
- The X preview is the author, the handle, and the post text. Photos and video in the post are not shown. A deleted, protected, or unavailable post stays on Facebook's card. The lookup times out after 4 seconds. Answers are cached in memory until Exit.
- The replacement looks for the English phrases "No preview available", "Preview unavailable", and "Preview not available". A one-line status link in the message text is left as a link; clicking it still opens the overlay.

## Contributing

Issues and pull requests are welcome. Please describe the problem, your Windows version, and steps to reproduce.

## License

[MIT](LICENSE)
