using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MessengerDesktop;

internal static class TaskbarBadge
{
    public static void Apply(IntPtr window, int count)
    {
        if (window == IntPtr.Zero) return;
        try
        {
            var list = (ITaskbarList3)new CTaskbarList();
            list.HrInit();
            if (count <= 0)
            {
                list.SetOverlayIcon(window, IntPtr.Zero, "");
                return;
            }

            string text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
            IntPtr icon = Draw(text);
            try
            {
                list.SetOverlayIcon(window, icon, UnreadTitle.Tooltip(count));
            }
            finally
            {
                DestroyIcon(icon);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The tray tooltip still carries the count when the taskbar button is gone.
        }
    }

    private static IntPtr Draw(string text)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(214, 45, 32));
            graphics.FillEllipse(brush, 1, 1, size - 3, size - 3);
            float em = text.Length >= 3 ? 9f : text.Length == 2 ? 12f : 16f;
            using var font = new Font(FontFamily.GenericSansSerif, em, FontStyle.Bold, GraphicsUnit.Pixel);
            using var white = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString(text, font, white, new RectangleF(0, 1, size, size), format);
        }

        return bitmap.GetHicon();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class CTaskbarList
    {
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int flags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint reserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint buttons, IntPtr button);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint buttons, IntPtr button);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        void SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string tip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr clip);
    }
}
