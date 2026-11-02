namespace MessengerDesktop;

// Chromium freezes a hidden or occluded renderer and slows its timers.
// These switches are not enough when the WebView2 controller is invisible:
// Form.Hide() and a minimized window set IsVisible to false, and Chromium
// then drops notifications and audio. The host parks the form off-screen
// instead, and still passes these switches. The page keeps its connection.
internal static class BackgroundThrottling
{
    public const string BrowserArguments =
        "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --disable-features=CalculateNativeWinOcclusion";
}
