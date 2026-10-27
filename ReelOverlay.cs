using Microsoft.Web.WebView2.WinForms;

namespace MessengerDesktop;

internal sealed class ReelOverlay : IDisposable
{
    public Panel Container { get; } = new() { Dock = DockStyle.Fill };
    public WebView2 View { get; } = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(24, 25, 26) };
    public Button Back { get; } = new() { Text = "Back to chat", Dock = DockStyle.Top, Height = 36 };
    public bool Ready { get; set; }
    private readonly Panel loading = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 25, 26) };
    private Uri? loadingTarget;
    private ulong? loadingNavigationId;

    public ReelOverlay()
    {
        loading.Controls.Add(new Label
        {
            Text = "Opening reel…",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter
        });
        Container.Controls.Add(View);
        Container.Controls.Add(loading);
        Container.Controls.Add(Back);
        Back.BringToFront();
    }

    public void BeginLoading(Uri target)
    {
        loadingTarget = target;
        loadingNavigationId = null;
        loading.Visible = true;
        loading.BringToFront();
        Back.BringToFront();
    }

    public void NavigationStarting(string source, ulong navigationId)
    {
        if (loadingTarget is not null && Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) &&
            uri.AbsoluteUri == loadingTarget.AbsoluteUri)
        {
            loadingNavigationId = navigationId;
        }
    }

    public void NavigationCompleted(ulong navigationId)
    {
        if (loadingNavigationId != navigationId) return;
        EndLoading();
    }

    public void EndLoading()
    {
        loadingTarget = null;
        loadingNavigationId = null;
        loading.Visible = false;
    }

    public void Dispose() => Container.Dispose();
}
