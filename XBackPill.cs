using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MessengerDesktop;

// Dark-glass pill over the X logo. WebView2 paints over sibling controls, so
// this is its own layered window, the same way the loading cover is.
internal sealed class XBackPill : Form
{
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const int HtClient = 1;
    private const int AcSrcOver = 0;
    private const byte AcSrcAlpha = 1;
    private const int UlwAlpha = 2;

    private Rectangle body;

    public event EventHandler? BackClicked;

    public XBackPill()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ControlBox = false;
        ShowIcon = false;
        Text = OverlayBackPill.Caption;
        Cursor = Cursors.Hand;
        BackColor = Color.Black;
    }

    public int BodyWidth(int deviceDpi)
    {
        using Bitmap measure = new(1, 1, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(measure);
        using Font font = CreateFont(deviceDpi);
        using var format = Typographic();
        int arrow = (int)Math.Ceiling(graphics.MeasureString(OverlayBackPill.Arrow, font, PointF.Empty, format).Width);
        int caption = (int)Math.Ceiling(graphics.MeasureString(OverlayBackPill.Caption, font, PointF.Empty, format).Width);
        int pad = OverlayBackPill.PadPixels(deviceDpi);
        int gap = OverlayBackPill.GapPixels(deviceDpi);
        return pad + OverlayBackPill.IconPixels(deviceDpi) + gap + arrow + gap + caption + pad;
    }

    public void ShowAt(Form owner, Rectangle pillOnScreen, int deviceDpi)
    {
        if (IsDisposed || owner.IsDisposed) return;
        int dpi = deviceDpi <= 0 ? 96 : deviceDpi;
        if (!IsHandleCreated) CreateHandle();
        using Bitmap bitmap = Render(dpi, pillOnScreen.Width);
        var origin = new Point(pillOnScreen.X - body.X, pillOnScreen.Y - body.Y);
        ApplyBitmap(bitmap, origin);
        if (!Visible)
        {
            Show(owner);
            ApplyBitmap(bitmap, origin);
        }
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_LAYERED = 0x00080000;
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return parameters;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (body.Contains(e.Location) && (e.Button == MouseButtons.Left || e.Button == MouseButtons.XButton1))
            BackClicked?.Invoke(this, EventArgs.Empty);
        base.OnMouseDown(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest)
        {
            int x = SignedLow(m.LParam);
            int y = SignedHigh(m.LParam);
            Point client = PointToClient(new Point(x, y));
            m.Result = body.Contains(client) ? new IntPtr(HtClient) : new IntPtr(HtTransparent);
            return;
        }
        base.WndProc(ref m);
    }

    private Bitmap Render(int dpi, int pillWidth)
    {
        int height = OverlayBackPill.HeightPixels(dpi);
        int blur = OverlayBackPill.ShadowBlurPixels(dpi);
        int drop = OverlayBackPill.ShadowOffsetYPixels(dpi);
        int pad = blur + drop + 2;
        int width = Math.Max(height, pillWidth);
        var bitmap = new Bitmap(width + pad * 2, height + pad * 2, PixelFormat.Format32bppArgb);
        body = new Rectangle(pad, pad, width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        DrawShadow(graphics, body, blur, drop);
        Color glass = Color.FromArgb(OverlayBackPill.BackgroundAlpha, OverlayBackPill.FillRgb.R, OverlayBackPill.FillRgb.G, OverlayBackPill.FillRgb.B);
        using (var fill = new SolidBrush(glass))
            FillCapsule(graphics, body, fill);
        DrawContents(graphics, body, dpi);
        return bitmap;
    }

    private static void DrawShadow(Graphics graphics, Rectangle pill, int blur, int drop)
    {
        if (blur <= 0) return;
        for (int spread = blur; spread >= 1; spread--)
        {
            int alpha = OverlayBackPill.ShadowAlpha * (blur - spread + 1) / (blur * blur);
            if (alpha <= 0) continue;
            var halo = new Rectangle(
                pill.X - spread,
                pill.Y - spread + drop,
                pill.Width + spread * 2,
                pill.Height + spread * 2);
            using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
            FillCapsule(graphics, halo, brush);
        }
    }

    private static void DrawContents(Graphics graphics, Rectangle pill, int dpi)
    {
        int pad = OverlayBackPill.PadPixels(dpi);
        int gap = OverlayBackPill.GapPixels(dpi);
        int icon = OverlayBackPill.IconPixels(dpi);
        int x = pill.X + pad;
        int iconY = pill.Y + Math.Max(0, (pill.Height - icon) / 2);
        DrawMessengerMark(graphics, new Rectangle(x, iconY, icon, icon), Color.White, Color.Black);
        x += icon + gap;
        using Font font = CreateFont(dpi);
        using var format = Typographic();
        using var text = new SolidBrush(Color.FromArgb(OverlayBackPill.TextRgb.R, OverlayBackPill.TextRgb.G, OverlayBackPill.TextRgb.B));
        float arrowWidth = graphics.MeasureString(OverlayBackPill.Arrow, font, PointF.Empty, format).Width;
        var row = new RectangleF(x, pill.Y, pill.Right - x, pill.Height);
        graphics.DrawString(OverlayBackPill.Arrow, font, text, row, format);
        row.X += (float)Math.Ceiling(arrowWidth) + gap;
        graphics.DrawString(OverlayBackPill.Caption, font, text, row, format);
    }

    private static void DrawMessengerMark(Graphics graphics, Rectangle bounds, Color bubbleColor, Color boltColor)
    {
        float s = bounds.Width;
        float left = bounds.X;
        float top = bounds.Y;
        using var bubble = new SolidBrush(bubbleColor);
        graphics.FillEllipse(bubble, left + s * 0.02f, top, s * 0.86f, s * 0.86f);
        graphics.FillPolygon(bubble, new[]
        {
            new PointF(left + s * 0.16f, top + s * 0.70f),
            new PointF(left + s * 0.02f, top + s * 0.98f),
            new PointF(left + s * 0.36f, top + s * 0.76f)
        });
        float ox = left + s * 0.24f;
        float oy = top + s * 0.16f;
        float u = s * 0.50f;
        using var bolt = new SolidBrush(boltColor);
        graphics.FillPolygon(bolt, new[]
        {
            new PointF(ox + u * 0.62f, oy),
            new PointF(ox + u * 0.16f, oy + u * 0.50f),
            new PointF(ox + u * 0.48f, oy + u * 0.50f),
            new PointF(ox + u * 0.32f, oy + u * 1.02f),
            new PointF(ox + u * 0.88f, oy + u * 0.40f),
            new PointF(ox + u * 0.54f, oy + u * 0.40f)
        });
    }

    private static void FillCapsule(Graphics graphics, Rectangle bounds, Brush brush)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1) return;
        using GraphicsPath path = Capsule(bounds);
        graphics.FillPath(brush, path);
    }

    private static GraphicsPath Capsule(Rectangle bounds)
    {
        int diameter = Math.Min(bounds.Width, bounds.Height);
        var path = new GraphicsPath();
        var left = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
        var right = new Rectangle(bounds.Right - diameter, bounds.Top, diameter, diameter);
        path.AddArc(left, 90, 180);
        path.AddArc(right, 270, 180);
        path.CloseFigure();
        return path;
    }

    private void ApplyBitmap(Bitmap bitmap, Point screen)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = CreateCompatibleDC(screenDc);
        IntPtr dib = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            dib = CreatePremultiplied(bitmap, screenDc);
            if (dib == IntPtr.Zero) return;
            previous = SelectObject(memoryDc, dib);
            var size = new Size(bitmap.Width, bitmap.Height);
            var source = new Point();
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };
            const uint SWP_NOZORDER = 0x0004;
            const uint SWP_NOACTIVATE = 0x0010;
            SetWindowPos(Handle, IntPtr.Zero, screen.X, screen.Y, size.Width, size.Height, SWP_NOZORDER | SWP_NOACTIVATE);
            UpdateLayeredWindow(Handle, screenDc, ref screen, ref size, memoryDc, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memoryDc, previous);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static IntPtr CreatePremultiplied(Bitmap bitmap, IntPtr screenDc)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        var header = new BitmapInfoHeader
        {
            biSize = Marshal.SizeOf<BitmapInfoHeader>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32
        };
        IntPtr section = CreateDIBSection(screenDc, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (section == IntPtr.Zero || bits == IntPtr.Zero) return section;
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = width * 4;
            var row = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, stride);
                for (int i = 0; i < stride; i += 4)
                {
                    byte alpha = row[i + 3];
                    row[i] = (byte)(row[i] * alpha / 255);
                    row[i + 1] = (byte)(row[i + 1] * alpha / 255);
                    row[i + 2] = (byte)(row[i + 2] * alpha / 255);
                }
                Marshal.Copy(row, 0, IntPtr.Add(bits, y * stride), stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return section;
    }

    private static Font CreateFont(int deviceDpi)
    {
        FontFamily family = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
        return new Font(family, OverlayBackPill.FontPixels(deviceDpi), FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private static StringFormat Typographic()
    {
        var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.None
        };
        format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
        return format;
    }

    private static int SignedLow(nint value) => (short)(value.ToInt64() & 0xFFFF);

    private static int SignedHigh(nint value) => (short)((value.ToInt64() >> 16) & 0xFFFF);

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize, IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BlendFunction pblend, int dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader bitmapInfo, uint usage, out IntPtr bits, IntPtr section, uint offset);
}
