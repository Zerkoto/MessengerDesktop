using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace MessengerDesktop.Tests;

public class UnreadFlyoutPaintTests
{
    [Fact]
    public void Opening_refreshing_and_closing_the_flyout_does_not_paint_a_disposed_font()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Exercise();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)));
        if (failure is not null) throw failure;
    }

    private static void Exercise()
    {
        var summary = new TraySummary();
        for (int i = 0; i < 20; i++)
        {
            summary.NoteChatList(true, Rows(i), DateTimeOffset.Now);
            using var flyout = new UnreadFlyout(
                summary,
                static () => { },
                static () => { },
                static _ => { },
                FlyoutPalette.Light)
            {
                CloseWhenDeactivated = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-20000, -20000)
            };
            flyout.Show();
            Application.DoEvents();
            summary.NoteChatList(true, Rows(i + 1), DateTimeOffset.Now);
            flyout.RefreshRows();
            Paint(flyout);
            flyout.ApplyMonitorDpi(i % 2 == 0 ? 144 : 120);
            flyout.RefreshRows();
            Paint(flyout);
            flyout.Close();
            Application.DoEvents();
        }
    }

    private static void Paint(Form form)
    {
        int width = Math.Max(1, form.ClientSize.Width);
        int height = Math.Max(1, form.ClientSize.Height);
        using var bitmap = new Bitmap(width, height);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
        form.Refresh();
        Application.DoEvents();
    }

    private static ChatThread[] Rows(int seed) =>
    [
        new(
            "Alex",
            "Unread message: hello " + seed + " " + new string('W', 40),
            "",
            "2m",
            "https://www.facebook.com/messages/t/alex"),
        new(
            "Blair",
            "You: line " + seed,
            "",
            "5m",
            "https://www.facebook.com/messages/t/blair")
    ];
}
