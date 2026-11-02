# Messenger Desktop

A lightweight, unofficial Windows desktop app for [Facebook Messenger](https://www.facebook.com/messages), built on .NET and Microsoft Edge WebView2.

Meta retired its official Messenger desktop app. This project gives you a focused, app-style window that opens straight into your chats instead of the Facebook home page.

> **Disclaimer:** This project is not affiliated with, endorsed by, or sponsored by Meta Platforms, Inc. "Messenger" and the Messenger logo are trademarks of Meta. This app only displays the official website inside WebView2.

## Features

- Opens directly into Messenger chats, not the Facebook feed
- Opening a reel shows a loading overlay until the page is ready, then fades it out
- Closing a reel returns you to the chat you were in, without reloading the page
- Clicking an X or Twitter status link in a chat opens it in an overlay. A link wrapped by `l.facebook.com` is unwrapped first. A dark pill over the X logo says ← Back to chat. The pill, Esc, Alt+Left, and the mouse back button close the overlay and leave that same chat loaded, at the same scroll position
- A status card that Facebook shows as "No preview available" is replaced with the author name, the @handle, the post text, and an x.com label. The text comes from the public oEmbed endpoint, with no API key. Clicking that card opens the same X overlay. If oEmbed does not answer, Facebook's card stays. The tray item "X link previews" is on by default and is stored in settings
- You sign in to X once inside the overlay. That sign-in is kept in its own profile
- Reel viewer is kept ready for faster opening
- Starts maximized, with its own taskbar icon
- Separate sign-in profile, isolated from your Edge browser
- Closing the window parks Messenger off-screen in the notification area. The window stays visible to Windows, so the chat keeps running. A new message while it is parked still plays a sound and shows a toast within a few seconds
- Exit quits Messenger and leaves no process running
- Starting Messenger again while it is already running brings the existing window forward
- Start with Windows is on the first time you run the app. Windows then starts it minimized, with `--minimized`. The menu check matches the startup entry. If the program moves, that entry is updated to the new path
- A new message shows a Windows toast. The title is the sender. The body is the chat-list preview, with a photo shown as "📷 Photo" and a sticker as "Sticker", or "New message" when there is no preview. The picture is the sender's, or the app icon. Every toast is filed under one Messenger group. Clicking the toast brings the window back and opens that chat when the notification has a chat link, otherwise the inbox
- Windows Focus Assist applies to those toasts. If Windows cannot show a toast, Messenger uses a tray balloon instead. The balloon uses the same title and body
- A reaction is its own toast and its own tray row, in the form `Name: Reacted ❤️ to: …`. With no emoji or snippet the body is "Reacted to your message". The text is saved when the reaction is first seen, so a later message in that chat does not replace the reaction in Action Center or in the tray summary. "New activity" and "New notification" are never shown
- While the window is not in front, Messenger reads the unread chat list about every 3 seconds. A new row plays a sound and shows one toast. Mute shows neither. The same alert from the page, inside a few seconds, is not shown again. Each alert adds one line to `%LOCALAPPDATA%\MessengerDesktop\logs\trace.log`: the time, `source=page-event` or `source=backup-timer`, and `kind=msg` or `kind=reaction`. The line has a thread id, not the message text. When `trace.log` is over about 1 MB at startup, it is renamed to `trace.old.log`
- **Watch chat list for reactions (beta)** is off until you turn it on from the tray menu. It uses the same chat-list read as the unread summary and does not add another observer. A newly bold row becomes a tray summary entry in the form `Name: Reacted ❤️ to: …`, counted like a reaction. It does not toast on its own, click the page, or mark a chat read. It can miss reactions in the chat you have open and in muted chats
- The taskbar icon still shows the unread count from the page title, such as `(3) Messenger`

## Tray

Right-click the notification icon for the Windows menu. It is the native popup, so it follows the system light or dark theme and the monitor DPI. The app asks Windows to allow dark menus at startup.

The menu order is:

- **Open Messenger** (the bold default)
- **Mute notifications**, opening beside the menu: 1 hour, 8 hours, until you turn it back on, and **Unmute**. While mute is on, the tray icon shows a small grey dot instead of the unread badge, and new toasts are not shown. Choosing the active duration again, or **Unmute**, turns notifications back on
- **Watch chat list for reactions (beta)**, directly under Mute. Off by default. The tooltip says: "Can miss reactions in the chat you have open and in muted chats."
- **X link previews**, directly under that item. On by default. Turning it off leaves Facebook's status cards as they are. The choice is stored in settings
- **Start with Windows**
- **Quit Messenger+**

Left-click opens an unread summary just above the taskbar, lined up with the tray icon (340px wide at 100% DPI, at most 420px tall, and those sizes scale with the monitor). It uses the Windows light or dark theme, rounded corners, and it closes when it loses focus or you press Esc. The header is **Unread** with a count, and **Mark all read**. The list is the unread chats in Messenger's own chat list: a row that is bold and has the blue unread dot. The app reads that list when the inbox loads and again whenever the list changes. The read looks at the page only. It does not click a chat or send a read receipt. Each row shows the avatar or initials, the name, one preview line, and the time from that chat. The five newest are shown. A message notification for the same chat fills in a preview when the row does not have one. A notification with no preview shows the name and the count only. A reaction reads `Name: Reacted ❤️ to: …`. Without a message preview it reads `Reacted to your message`. Until that first read finishes, the summary says `Checking for messages…`. It says you're all caught up only after a read that finds no unread chats. If the chat list cannot be read and the window title has a count, such as `(3) Messenger`, the summary says `3 unread`. The footer **Open Messenger** button brings the window forward. Double-clicking the tray icon still only opens the window.

**Mark all read** clears that summary on this PC only. It does not mark chats read on Facebook, click anything in the page, or send a read receipt. The red badge stays until that chat is opened in the app or the page shows it as read. Reactions leave the badge as soon as the summary opens.

The badge is drawn at the tray's small-icon size for the current DPI (16px at 100%, 24px at 150%), not stretched up from 16px. The red `#E81123` circle covers about 60% of that icon, with a bold white digit. There is no badge at zero. Counts from 1 to 9 are digits. Above 9 the badge reads `9+`. The count is the number of unread chats in that list, plus reactions you have not opened the summary for. The page title is only a backup when those rows cannot be read, because the title often has no `(1)`. The taskbar icon still uses the page title. The tooltip matches the tray badge, for example `Messenger+ · 3 unread`. The grey dot is only shown while notifications are muted.

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
- Notification text is shown only on this PC, through a Windows toast or a tray balloon. It is not uploaded. Alert lines in `%LOCALAPPDATA%\MessengerDesktop\logs\trace.log` record the time, the source, and the kind. They do not contain the message text.

## Known limitations

- Message toasts use the unread chat list when the window is not in front, and also a page notification when Messenger creates one. WebView2 reports only non-persistent notifications on `NotificationReceived`, so the app also forwards `showNotification`. Reactions are inferred from that list and from the unread count, and can be wrong. Calls are not detected.
- Chat-list reaction watch is off unless you turn it on. It can miss reactions in the chat you have open and in muted chats. It does not toast by itself, and it does not see activity that never changes the chat list.
- A toast click opens the chat link carried on the notification. WebView2 does not provide that link, so the click opens the inbox unless the notification tag itself is a chat link.
- The tray balloon used when Windows toasts are unavailable does not honor Focus Assist.
- Calls, file downloads, and some sign-in flows depend on Facebook and WebView2, and are not fully tested.
- Reel load time is mostly limited by Facebook itself.
- Only an X or Twitter status link opens in the X overlay. A profile, the home feed, or a lookalike host stays in the chat.
- The X preview is the author, the handle, and the post text. Photos and video in the post are not shown. A deleted, protected, or unavailable post stays on Facebook's card. The lookup times out after 4 seconds. Answers are cached in memory until Exit.
- The replacement looks for an empty status card: no title or description, Facebook's telescope placeholder, or a failure label in English ("No preview available") or Bulgarian ("Няма наличен преглед" and the same idea in a few nearby wordings). A card that already shows a title or description is left as Facebook drew it. A one-line status link in the message text stays a link; clicking it still opens the overlay.

## Contributing

Issues and pull requests are welcome. Please describe the problem, your Windows version, and steps to reproduce.

## License

[MIT](LICENSE)
