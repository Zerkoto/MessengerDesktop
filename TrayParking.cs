namespace MessengerDesktop;

// How the window leaves the screen without telling WebView2 it is invisible.
// Form.Hide() and a minimized window both set the controller's IsVisible to
// false, and Chromium then drops notifications and audio.
internal readonly record struct TrayParkingPlan(
    bool FormVisible,
    bool ShowInTaskbar,
    bool Minimized,
    bool ControllerVisible,
    int X,
    int Y);

internal static class TrayParking
{
    public const int OffScreenX = -32000;
    public const int OffScreenY = -32000;

    public static TrayParkingPlan Park() => new(
        FormVisible: true,
        ShowInTaskbar: false,
        Minimized: false,
        ControllerVisible: true,
        X: OffScreenX,
        Y: OffScreenY);
}
