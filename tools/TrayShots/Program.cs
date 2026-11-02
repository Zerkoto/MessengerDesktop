using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace MessengerDesktop;

internal static class TrayShots
{
    [STAThread]
    private static void Main(string[] args)
    {
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string directory = args.Length > 0 ? args[0] : "screenshots";
        Console.WriteLine("tray shots: starting");
        Directory.CreateDirectory(directory);
        Application.Run(new ShotHost(directory));
    }

    private sealed class ShotHost : Form
    {
        private readonly string directory;

        public ShotHost(string directory)
        {
            this.directory = directory;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            Size = new Size(64, 64);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ArmWatchdog();
            try
            {
                Console.WriteLine("tray shots: badges");
                SaveBadges();
                Console.WriteLine("tray shots: flyouts");
                SaveFlyouts();
                Console.WriteLine("tray shots: menus");
                SaveMenus();
                RequireFiles();
                Console.WriteLine("tray shots: done");
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                Environment.ExitCode = 1;
            }

            Close();
        }

        private void ArmWatchdog()
        {
            string dir = directory;
            var thread = new Thread(() =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(45));
                try
                {
                    File.AppendAllText(
                        Path.Combine(dir, "menu-capture.txt"),
                        "\nHeadless native-menu capture did not return within 45 seconds, so it was stopped. TrackPopupMenuEx is modal and uses the monitor DPI, so menu screenshots at 100%, 125%, and 150% were not produced.\n");
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    // The process is exiting either way.
                }

                Environment.Exit(RequiredFiles(dir) ? 0 : 1);
            })
            {
                IsBackground = true
            };
            thread.Start();
        }

        private void SaveBadges()
        {
            SaveBadge("1", 1, 96, "100");
            SaveBadge("5", 5, 96, "100");
            SaveBadge("9plus", 10, 96, "100");
            SaveBadge("1", 1, 144, "150");
            SaveBadge("5", 5, 144, "150");
            SaveBadge("9plus", 10, 144, "150");
        }

        private void SaveBadge(string label, int count, int dpi, string scaleName)
        {
            int size = TrayBadgeMetrics.IconSizeForDpi(dpi);
            using Bitmap blue = BlueCircle(Math.Max(size, 64));
            using Bitmap raw = TrayBadgePainter.Render(blue, TrayGlyph.Count, TrayText.Badge(count), size);
            if (raw.Width != size || raw.Height != size)
                throw new InvalidOperationException($"Badge {label} at {dpi} DPI was {raw.Width}px, expected {size}px.");
            using Bitmap zoomed = Zoom(raw, 8);
            string path = Path.Combine(directory, $"badge-{label}-{scaleName}.png");
            zoomed.Save(path, ImageFormat.Png);
            File.WriteAllText(
                Path.Combine(directory, $"badge-{label}-{scaleName}.txt"),
                $"source {size}x{size} at {dpi} DPI ({scaleName}%), nearest-neighbor x8, label {TrayText.Badge(count)}");
        }

        private void SaveFlyouts()
        {
            foreach (int dpi in new[] { 96, 120, 144 })
            {
                string scale = dpi switch
                {
                    96 => "100",
                    120 => "125",
                    _ => "150"
                };
                SaveFlyout($"flyout-unread-light-{scale}.png", Sample(withRows: true), dpi, FlyoutPalette.Light);
                SaveFlyout($"flyout-unread-dark-{scale}.png", Sample(withRows: true), dpi, FlyoutPalette.Dark);
                SaveFlyout($"flyout-empty-light-{scale}.png", Sample(withRows: false), dpi, FlyoutPalette.Light);
                SaveFlyout($"flyout-empty-dark-{scale}.png", Sample(withRows: false), dpi, FlyoutPalette.Dark);
            }
        }

        private void SaveFlyout(string name, TraySummary summary, int dpi, FlyoutPalette palette)
        {
            using var flyout = new UnreadFlyout(summary, static () => { }, static () => { }, static _ => { }, palette, dpi)
            {
                CloseWhenDeactivated = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-20000, -20000)
            };
            flyout.Show(this);
            Application.DoEvents();
            int expected = FlyoutLayout.At(dpi).Width;
            if (flyout.ClientSize.Width != expected)
                throw new InvalidOperationException($"{name} width {flyout.ClientSize.Width} did not match {expected} at {dpi} DPI.");
            using var bitmap = new Bitmap(flyout.ClientSize.Width, flyout.ClientSize.Height);
            flyout.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            if (bitmap.Width < 100 || bitmap.Height < 80)
                throw new InvalidOperationException($"{name} rendered at {bitmap.Width}x{bitmap.Height}.");
            bitmap.Save(Path.Combine(directory, name), ImageFormat.Png);
            Console.WriteLine($"tray shots: wrote {name} {bitmap.Width}x{bitmap.Height}");
            flyout.Close();
        }

        private void SaveMenus()
        {
            var note = new StringBuilder();
            note.AppendLine("Native menu capture");
            note.AppendLine("TrackPopupMenuEx uses the monitor DPI. This runner is not switched between 100%, 125%, and 150%, so a native menu cannot be rendered at those scales headless.");
            note.AppendLine($"SetPreferredAppMode ordinal available: {NativeMenuTheme.Available}");
            using var icon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "Messenger+"
            };
            try
            {
                CaptureMenu(icon, dark: false, "menu-light.png", note);
                CaptureMenu(icon, dark: true, "menu-dark.png", note);
            }
            finally
            {
                icon.Visible = false;
                icon.Dispose();
                NativeMenuTheme.TryAllowDark();
            }

            File.WriteAllText(Path.Combine(directory, "menu-capture.txt"), note.ToString());
        }

        private void CaptureMenu(NotifyIcon icon, bool dark, string fileName, StringBuilder note)
        {
            bool forced = NativeMenuTheme.TryForce(dark);
            note.AppendLine(forced
                ? $"Requested {(dark ? "ForceDark" : "ForceLight")}."
                : $"Could not call SetPreferredAppMode for {(dark ? "dark" : "light")}. The ordinal is missing, so the menu follows the runner default.");
            Bitmap? shot = null;
            string? failure = null;
            var timer = new System.Windows.Forms.Timer { Interval = 100 };
            int ticks = 0;
            timer.Tick += (_, _) =>
            {
                ticks++;
                IntPtr hwnd = FindWindow("#32768", null);
                if (hwnd != IntPtr.Zero && shot is null)
                    shot = TryPrint(hwnd);
                if (shot is not null || ticks >= 15)
                {
                    failure ??= hwnd == IntPtr.Zero ? "No #32768 menu window appeared." : null;
                    DismissMenu(icon);
                    timer.Stop();
                }
            };
            var canceller = new Thread(() =>
            {
                Thread.Sleep(2500);
                DismissMenu(icon);
            })
            {
                IsBackground = true
            };
            timer.Start();
            canceller.Start();
            NativeTrayMenu.Show(icon, Handle, new TraySummary(), DateTimeOffset.Now, startupEnabled: true);
            timer.Stop();
            timer.Dispose();
            if (shot is null)
            {
                note.AppendLine($"{fileName}: not captured. {failure ?? "The menu closed before it could be printed."} Native menu screenshots at 100/125/150% were not produced.");
                return;
            }

            double luma = AverageLuma(shot, out double spread);
            note.AppendLine($"{fileName}: {shot.Width}x{shot.Height}, average luminance {luma:0}, spread {spread:0}.");
            if (spread < 4)
            {
                note.AppendLine($"{fileName}: the bitmap is blank, so it is not a usable menu capture.");
                shot.Dispose();
                return;
            }

            if (dark && luma > 170)
                note.AppendLine($"{fileName}: the capture is light. A dark native menu was not shown. Headless dark-mode capture did not succeed.");
            shot.Save(Path.Combine(directory, fileName), ImageFormat.Png);
            shot.Dispose();
        }

        private void RequireFiles()
        {
            if (!RequiredFiles(directory))
                throw new InvalidOperationException("One or more screenshot files are missing.");
        }

        private static bool RequiredFiles(string directory)
        {
            string[] required =
            [
                "badge-1-100.png",
                "badge-5-100.png",
                "badge-9plus-100.png",
                "badge-1-150.png",
                "badge-5-150.png",
                "badge-9plus-150.png",
                "flyout-unread-light-100.png",
                "flyout-unread-dark-100.png",
                "flyout-empty-light-100.png",
                "flyout-empty-dark-100.png",
                "flyout-unread-light-125.png",
                "flyout-unread-dark-125.png",
                "flyout-empty-light-125.png",
                "flyout-empty-dark-125.png",
                "flyout-unread-light-150.png",
                "flyout-unread-dark-150.png",
                "flyout-empty-light-150.png",
                "flyout-empty-dark-150.png",
                "menu-capture.txt"
            ];
            foreach (string name in required)
            {
                string path = Path.Combine(directory, name);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    return false;
            }

            return true;
        }

        private void DismissMenu(NotifyIcon icon)
        {
            const uint cancelMode = 0x001F;
            if (!InvokeRequired)
                EndMenu();
            PostMessage(Handle, cancelMode, IntPtr.Zero, IntPtr.Zero);
            IntPtr iconWindow = TrayIconBounds.Window(icon);
            if (iconWindow != IntPtr.Zero)
                PostMessage(iconWindow, cancelMode, IntPtr.Zero, IntPtr.Zero);
            IntPtr menuWindow = FindWindow("#32768", null);
            if (menuWindow != IntPtr.Zero)
                PostMessage(menuWindow, cancelMode, IntPtr.Zero, IntPtr.Zero);
        }

        private static TraySummary Sample(bool withRows)
        {
            var summary = new TraySummary();
            if (!withRows)
            {
                summary.NoteChatList(true, [], DateTimeOffset.Now);
                return summary;
            }
            DateTimeOffset now = DateTimeOffset.Now;
            summary.Observe(new NotificationRecord(
                "Alex Rivera",
                "On my way, see you at 6 by the station entrance",
                "",
                "a",
                "https://www.facebook.com/messages/t/1",
                "facebook"), now.AddMinutes(-5));
            summary.Observe(new NotificationRecord(
                "Sam Lee",
                "reacted ❤️ to your message",
                "",
                "b",
                "https://www.facebook.com/messages/t/2",
                "facebook"), now.AddMinutes(-2));
            summary.Observe(new NotificationRecord(
                "Jordan",
                "",
                "",
                "c",
                "https://www.facebook.com/messages/t/3",
                "facebook"), now.AddMinutes(-40));
            summary.NoteActivity(now.AddMinutes(-1));
            return summary;
        }

        private static Bitmap BlueCircle(int size)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(0xFF, 0x1B, 0x6C, 0xD6));
            graphics.FillEllipse(brush, 1, 1, size - 3, size - 3);
            return bitmap;
        }

        private static Bitmap Zoom(Bitmap source, int factor)
        {
            var zoomed = new Bitmap(source.Width * factor, source.Height * factor, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(zoomed);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(source, new Rectangle(0, 0, zoomed.Width, zoomed.Height));
            return zoomed;
        }

        private static Bitmap? TryPrint(IntPtr hwnd)
        {
            if (!GetWindowRect(hwnd, out Rect rect)) return null;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 20 || height < 20 || width > 2000 || height > 2000) return null;
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            IntPtr hdc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(hwnd, hdc, 2))
                    PrintWindow(hwnd, hdc, 0);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            return bitmap;
        }

        private static double AverageLuma(Bitmap bitmap, out double spread)
        {
            long total = 0;
            long count = 0;
            int min = 255;
            int max = 0;
            for (int y = 0; y < bitmap.Height; y += 2)
            {
                for (int x = 0; x < bitmap.Width; x += 2)
                {
                    Color color = bitmap.GetPixel(x, y);
                    int luma = (color.R * 3 + color.G * 6 + color.B) / 10;
                    total += luma;
                    count++;
                    if (luma < min) min = luma;
                    if (luma > max) max = luma;
                }
            }

            spread = max - min;
            return count == 0 ? 0 : total / (double)count;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string? windowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EndMenu();

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
