namespace MessengerDesktop;

// Chromium freezes a hidden or occluded renderer and slows its timers.
// The window X hides Messenger, so the WebView2 environment is created with
// these switches. The page then keeps its connection while the form is hidden.
internal static class BackgroundThrottling
{
    public const string BrowserArguments =
        "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --disable-features=CalculateNativeWinOcclusion";
}
