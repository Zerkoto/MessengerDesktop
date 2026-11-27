namespace MessengerDesktop;

// Releasing Alt asks Windows to open the window menu: WM_SYSCOMMAND / SC_KEYMENU
// with no character in lParam. Alt+Shift is that same message, because Shift is
// not a mnemonic. This window has no menu bar. DefWindowProc still enters the
// menu loop, which takes the keyboard off WebView2. A quick Alt+Shift therefore
// changes language and then snaps back when focus moves, and holding Alt+Shift
// long enough for the change to settle keeps the language but the menu loop
// eats the next character. Real mnemonics (Alt+Space, Alt+Left) put the key in
// lParam and are left alone.
internal static class BareAltMenu
{
    public const int WmSysCommand = 0x0112;
    public const int ScKeyMenu = 0xF100;

    public static bool ShouldIgnore(int message, nint wParam, nint lParam)
    {
        if (message != WmSysCommand || lParam != 0) return false;
        return (wParam & 0xFFF0) == ScKeyMenu;
    }
}
