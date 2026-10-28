namespace MessengerDesktop;

internal enum ReelLoadVisual
{
    Hidden = 0,
    Visible = 1,
    Fading = 2
}

// Tracks whether the reel loading cover should show, fade, or hide.
// WebView2 raises navigation events after a reel is closed or replaced;
// the epoch drops those late callbacks so the cover cannot get stuck.
internal sealed class ReelLoadCoordinator
{
    public const int MaxWaitMilliseconds = 8000;

    private Uri? target;
    private ulong? navigationId;
    private bool completionArmed;

    public ReelLoadVisual Visual { get; private set; } = ReelLoadVisual.Hidden;

    public int Generation { get; private set; }

    public int Epoch { get; private set; }

    public void Begin(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Generation++;
        Epoch++;
        this.target = target;
        navigationId = null;
        completionArmed = false;
        Visual = ReelLoadVisual.Visible;
    }

    public bool NoteNavigationStarting(string? source, ulong navigationId)
    {
        if (Visual == ReelLoadVisual.Hidden || target is null) return false;
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (this.navigationId == navigationId) return false;

        Epoch++;
        this.navigationId = navigationId;
        completionArmed = false;
        Visual = ReelLoadVisual.Visible;
        return true;
    }

    public int? ArmContentReady(ulong navigationId)
    {
        if (Visual != ReelLoadVisual.Visible || completionArmed) return null;
        if (this.navigationId != navigationId) return null;
        completionArmed = true;
        return Epoch;
    }

    public bool TryBeginFade(int epoch)
    {
        if (epoch != Epoch || Visual != ReelLoadVisual.Visible) return false;
        Visual = ReelLoadVisual.Fading;
        return true;
    }

    public bool TryExpire(int epoch) => TryBeginFade(epoch);

    public bool NoteNavigationFailed(ulong navigationId)
    {
        if (Visual == ReelLoadVisual.Hidden || this.navigationId != navigationId) return false;
        HideNow();
        return true;
    }

    public bool NoteProcessFailed()
    {
        if (Visual == ReelLoadVisual.Hidden) return false;
        HideNow();
        return true;
    }

    private void HideNow()
    {
        Epoch++;
        Visual = ReelLoadVisual.Hidden;
        completionArmed = false;
        navigationId = null;
    }

    public bool IsFading(int epoch) => epoch == Epoch && Visual == ReelLoadVisual.Fading;

    public void FinishFade(int epoch)
    {
        if (epoch != Epoch || Visual != ReelLoadVisual.Fading) return;
        Visual = ReelLoadVisual.Hidden;
        target = null;
        navigationId = null;
        completionArmed = false;
    }

    public void End()
    {
        Generation++;
        Epoch++;
        Visual = ReelLoadVisual.Hidden;
        target = null;
        navigationId = null;
        completionArmed = false;
    }

    public bool IsCurrent(int generation) => generation == Generation;
}
