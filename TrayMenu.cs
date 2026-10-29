using System.Runtime.InteropServices;

namespace MessengerDesktop;

// uxtheme ordinals. AllowDark follows the system theme. ForceDark and ForceLight
// exist so a screenshot can ask for one theme; missing ordinals are ignored.
internal static class NativeMenuTheme
{
    public const int AllowDark = 1;
    public const int ForceDark = 2;
    public const int ForceLight = 3;

    private static readonly object Gate = new();
    private static bool loaded;
    private static SetPreferredAppModeDelegate? setPreferred;
    private static FlushMenuThemesDelegate? flush;

    public static bool Available
    {
        get
        {
            Ensure();
            return setPreferred is not null;
        }
    }

    public static bool TryAllowDark() => TrySet(AllowDark);

    public static bool TryForce(bool dark) => TrySet(dark ? ForceDark : ForceLight);

    public static bool TrySet(int mode)
    {
        Ensure();
        if (setPreferred is null) return false;
        try
        {
            setPreferred(mode);
            flush?.Invoke();
            return true;
        }
        catch (Exception error) when (error is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    private static void Ensure()
    {
        if (loaded) return;
        lock (Gate)
        {
            if (loaded) return;
            loaded = true;
            if (!OperatingSystem.IsWindows()) return;
            IntPtr module = LoadLibrary("uxtheme.dll");
            if (module == IntPtr.Zero) return;
            IntPtr set = GetProcAddress(module, (IntPtr)135);
            IntPtr flushPtr = GetProcAddress(module, (IntPtr)136);
            if (set != IntPtr.Zero)
                setPreferred = Marshal.GetDelegateForFunctionPointer<SetPreferredAppModeDelegate>(set);
            if (flushPtr != IntPtr.Zero)
                flush = Marshal.GetDelegateForFunctionPointer<FlushMenuThemesDelegate>(flushPtr);
        }
    }

    private delegate int SetPreferredAppModeDelegate(int mode);

    private delegate void FlushMenuThemesDelegate();

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string name);

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, IntPtr name);
}

internal static class TrayIconBounds
{
    public static IntPtr Window(NotifyIcon icon)
    {
        object? window = Read(icon, "_window") ?? Read(icon, "window");
        return window is NativeWindow native ? native.Handle : IntPtr.Zero;
    }

    public static Rectangle Get(NotifyIcon icon)
    {
        IntPtr hwnd = Window(icon);
        object? idValue = Read(icon, "_id") ?? Read(icon, "id");
        uint id = idValue switch
        {
            uint value => value,
            int signed => (uint)signed,
            _ => 0
        };
        if (hwnd != IntPtr.Zero && id != 0)
        {
            var identifier = new NotifyIconIdentifier
            {
                cbSize = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
                hWnd = hwnd,
                uID = id
            };
            if (Shell_NotifyIconGetRect(ref identifier, out Rect rect) == 0
                && rect.Right > rect.Left
                && rect.Bottom > rect.Top)
            {
                return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            }
        }

        Point cursor = Cursor.Position;
        return new Rectangle(cursor.X, cursor.Y, 1, 1);
    }

    private static object? Read(NotifyIcon icon, string name)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public;
        return typeof(NotifyIcon).GetField(name, flags)?.GetValue(icon);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect rect);
}

// Native popup. The shell draws the text, so row height and DPI come from Windows.
internal static class NativeTrayMenu
{
    public const uint CmdOpen = 1;
    public const uint CmdMuteOneHour = 2;
    public const uint CmdMuteEightHours = 3;
    public const uint CmdMuteUntilOn = 4;
    public const uint CmdUnmute = 5;
    public const uint CmdStartup = 6;
    public const uint CmdQuit = 7;

    private const uint MfString = 0x0000;
    private const uint MfGrayed = 0x0001;
    private const uint MfChecked = 0x0008;
    private const uint MfPopup = 0x0010;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmRightAlign = 0x0008;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmNonotify = 0x0080;
    private const uint TpmReturnCmd = 0x0100;

    public static uint Show(NotifyIcon icon, TraySummary summary, DateTimeOffset now, bool startupEnabled) =>
        Show(icon, IntPtr.Zero, summary, now, startupEnabled);

    public static uint Show(NotifyIcon icon, IntPtr fallbackOwner, TraySummary summary, DateTimeOffset now, bool startupEnabled)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(summary);
        IntPtr menu = CreatePopupMenu();
        IntPtr submenu = CreatePopupMenu();
        try
        {
            TrayMute active = summary.MuteChoice(now);
            Append(submenu, CmdMuteOneHour, TrayMenuModel.MuteText(TrayMute.OneHour), active == TrayMute.OneHour, enabled: true);
            Append(submenu, CmdMuteEightHours, TrayMenuModel.MuteText(TrayMute.EightHours), active == TrayMute.EightHours, enabled: true);
            Append(submenu, CmdMuteUntilOn, TrayMenuModel.MuteText(TrayMute.UntilOn), active == TrayMute.UntilOn, enabled: true);
            AppendSeparator(submenu);
            Append(submenu, CmdUnmute, TrayMenuModel.Text(TrayMenuEntryKind.Unmute), check: false, enabled: summary.IsMuted(now));

            foreach (TrayMenuEntryKind kind in TrayMenuModel.Order)
            {
                switch (kind)
                {
                    case TrayMenuEntryKind.Separator:
                        AppendSeparator(menu);
                        break;
                    case TrayMenuEntryKind.Open:
                        Append(menu, CmdOpen, TrayMenuModel.Text(kind), check: false, enabled: true);
                        SetMenuDefaultItem(menu, CmdOpen, false);
                        break;
                    case TrayMenuEntryKind.Mute:
                        AppendMenu(menu, MfPopup | MfString, (UIntPtr)submenu, TrayMenuModel.Text(kind));
                        submenu = IntPtr.Zero;
                        break;
                    case TrayMenuEntryKind.WatcherToggle:
                        break;
                    case TrayMenuEntryKind.Startup:
                        Append(menu, CmdStartup, TrayMenuModel.Text(kind), startupEnabled, enabled: true);
                        break;
                    case TrayMenuEntryKind.Quit:
                        Append(menu, CmdQuit, TrayMenuModel.Text(kind), check: false, enabled: true);
                        break;
                }
            }

            IntPtr owner = fallbackOwner != IntPtr.Zero ? fallbackOwner : TrayIconBounds.Window(icon);
            if (owner != IntPtr.Zero) SetForegroundWindow(owner);
            Rectangle bounds = TrayIconBounds.Get(icon);
            uint flags = TpmReturnCmd | TpmRightButton | TpmNonotify | TpmRightAlign | TpmBottomAlign;
            int selected = TrackPopupMenuEx(menu, flags, bounds.Right, bounds.Top, owner, IntPtr.Zero);
            if (owner != IntPtr.Zero)
                PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);
            return (uint)selected;
        }
        finally
        {
            if (submenu != IntPtr.Zero) DestroyMenu(submenu);
            if (menu != IntPtr.Zero) DestroyMenu(menu);
        }
    }

    private static void Append(IntPtr menu, uint id, string text, bool check, bool enabled)
    {
        uint flags = MfString;
        if (check) flags |= MfChecked;
        if (!enabled) flags |= MfGrayed;
        AppendMenu(menu, flags, (UIntPtr)id, text);
    }

    private static void AppendSeparator(IntPtr menu) =>
        AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, bool byPosition);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
}
