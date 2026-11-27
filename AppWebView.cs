using Microsoft.Web.WebView2.WinForms;

namespace MessengerDesktop;

// The chat page is focused while typing, so Alt's menu command arrives here.
internal sealed class AppWebView : WebView2
{
    protected override void WndProc(ref Message m)
    {
        if (BareAltMenu.ShouldIgnore(m.Msg, m.WParam, m.LParam))
        {
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }
}
