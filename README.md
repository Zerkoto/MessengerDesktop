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
- The tray menu can open the window, turn Start with Windows on or off, or exit. A double-click on the icon also opens the window
- Exit quits Messenger and leaves no process running
- Starting Messenger again while it is already running brings the existing window forward
- Start with Windows is on the first time you run the app. Windows then starts it minimized, with `--minimized`. The menu check matches the startup entry. If the program moves, that entry is updated to the new path

## Requirements

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`winget install Microsoft.DotNet.SDK.10`)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (preinstalled on most Windows 11 machines)

## Build and run

```powershell
git clone https://github.com/Zerkoto/MessengerDesktop.git
cd MessengerDesktop
dotnet build -c Release
.\bin\Release\net10.0-windows\Messenger.exe
```

## Privacy

- You sign in to Facebook yourself, inside the app.
- Your session is stored locally in `%LOCALAPPDATA%\MessengerDesktop\WebView2`, separately from Edge.
- Settings are stored in `%LOCALAPPDATA%\MessengerDesktop\settings.json`. That file has no cookies or messages.
- Start with Windows is a per-user registry value (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `MessengerDesktop`). It is not a scheduled task.
- The app does not read, copy, or send your browser cookies, credentials, or messages anywhere.

## Known limitations

- Messenger can stay loaded in the tray. System toasts, badges, and unread counts are not implemented yet.
- Calls, file downloads, and some sign-in flows depend on Facebook and WebView2, and are not fully tested.
- Reel load time is mostly limited by Facebook itself.

## Contributing

Issues and pull requests are welcome. Please describe the problem, your Windows version, and steps to reproduce.

## License

[MIT](LICENSE)
