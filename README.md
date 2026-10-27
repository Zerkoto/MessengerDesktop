# Messenger desktop wrapper

A small Windows app that opens Facebook Messages in WebView2. This is an unofficial wrapper; Meta operates the website.

Build: `dotnet build -c Release`

Run: `bin\Release\net7.0-windows\Messenger.exe`

The app requires the .NET 7 Windows Desktop Runtime and Microsoft Edge WebView2 Runtime. The WebView2 profile is stored under `%LOCALAPPDATA%\MessengerDesktop\WebView2`, separately from Edge, so sign in there yourself. The program does not copy browser cookies or credentials.

The app does not currently implement custom background notification handling. Calls, downloads, authentication and messaging depend on Facebook and WebView2; they have not been verified.
