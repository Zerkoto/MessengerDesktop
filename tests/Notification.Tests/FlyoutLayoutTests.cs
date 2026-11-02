using Xunit;

namespace MessengerDesktop.Tests;

public class FlyoutLayoutTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(168)]
    public void Header_link_and_title_fit_without_overlapping(int dpi)
    {
        FlyoutLayout.Metrics metrics = FlyoutLayout.At(dpi);
        int titleFont = FlyoutLayout.FontPixels(14, dpi);
        int linkFont = FlyoutLayout.FontPixels(12, dpi);
        int pillFont = FlyoutLayout.FontPixels(11, dpi);
        int titleWidth = FlyoutLayout.EstimateWidth("Unread", titleFont);
        int titleHeight = FlyoutLayout.EstimateHeight(titleFont);
        int linkWidth = FlyoutLayout.EstimateWidth("Mark all read", linkFont);
        int linkHeight = FlyoutLayout.EstimateHeight(linkFont);
        int pillWidth = FlyoutLayout.EstimateWidth("5", pillFont) + (FlyoutLayout.Px(8, dpi) * 2);
        int pillHeight = FlyoutLayout.EstimateHeight(pillFont) + (FlyoutLayout.Px(2, dpi) * 2);

        FlyoutLayout.HeaderArrangement header = FlyoutLayout.Header(
            metrics, titleWidth, titleHeight, pillWidth, pillHeight, linkWidth, linkHeight);

        Assert.True(new MeasuredLine(titleWidth, titleHeight, false).Fits(header.Title));
        Assert.True(new MeasuredLine(linkWidth, linkHeight, false).Fits(header.Link));
        Assert.True(header.PillVisible);
        Assert.True(new MeasuredLine(pillWidth, pillHeight, false).Fits(header.Pill));
        Assert.False(header.Title.Overlaps(header.Link));
        Assert.False(header.Title.Overlaps(header.Pill));
        Assert.False(header.Pill.Overlaps(header.Link));
        Assert.True(header.Link.Right <= metrics.Width);
        Assert.True(header.Title.X >= metrics.HeaderPad);
        Assert.Equal(FlyoutLayout.Px(TrayMetrics.PanelWidth, dpi), metrics.Width);
        Assert.Equal(FlyoutLayout.Px(48, dpi), metrics.HeaderHeight);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(168)]
    public void A_row_fits_the_name_and_time_and_ellipsizes_a_long_preview(int dpi)
    {
        FlyoutLayout.Metrics metrics = FlyoutLayout.At(dpi);
        int nameFont = FlyoutLayout.FontPixels(13, dpi);
        int bodyFont = FlyoutLayout.FontPixels(12, dpi);
        int nameWidth = FlyoutLayout.EstimateWidth("Alex", nameFont);
        int nameHeight = FlyoutLayout.EstimateHeight(nameFont);
        string preview = new string('W', 80) + " this preview is longer than the row";
        int previewWidth = FlyoutLayout.EstimateWidth(preview, bodyFont);
        int previewHeight = FlyoutLayout.EstimateHeight(bodyFont);
        int timeWidth = FlyoutLayout.EstimateWidth("3m", bodyFont);
        int timeHeight = FlyoutLayout.EstimateHeight(bodyFont);

        FlyoutLayout.RowArrangement row = FlyoutLayout.Row(
            metrics, nameWidth, nameHeight, previewWidth, previewHeight, timeWidth, timeHeight);

        Assert.False(row.NameEllipsized);
        Assert.True(new MeasuredLine(nameWidth, nameHeight, false).Fits(row.Name));
        Assert.True(new MeasuredLine(timeWidth, timeHeight, false).Fits(row.Time));
        Assert.False(row.Name.Overlaps(row.Time));
        Assert.False(row.Name.Overlaps(row.Preview));
        Assert.True(row.PreviewEllipsized);
        Assert.True(previewWidth > row.Preview.Width);
        Assert.True(row.Preview.Height >= previewHeight);
        Assert.True(new MeasuredLine(previewWidth, previewHeight, true).Fits(row.Preview));
        Assert.Equal(FlyoutLayout.Px(64, dpi), metrics.RowHeight);
        Assert.Equal(FlyoutLayout.Px(40, dpi), row.Avatar.Width);
        Assert.True(row.Hover.X >= metrics.HoverInset);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(168)]
    public void Empty_state_and_footer_text_fit(int dpi)
    {
        FlyoutLayout.Metrics metrics = FlyoutLayout.At(dpi);
        int titleFont = FlyoutLayout.FontPixels(14, dpi);
        int hintFont = FlyoutLayout.FontPixels(12, dpi);
        int titleWidth = FlyoutLayout.EstimateWidth(FlyoutText.CaughtUp, titleFont);
        int titleHeight = FlyoutLayout.EstimateHeight(titleFont);
        int hintWidth = FlyoutLayout.EstimateWidth(FlyoutText.CaughtUpHint, hintFont);
        int hintHeight = FlyoutLayout.EstimateHeight(hintFont);
        FlyoutLayout.EmptyArrangement empty = FlyoutLayout.Empty(metrics, titleWidth, titleHeight, hintWidth, hintHeight);

        Assert.True(new MeasuredLine(titleWidth, titleHeight, false).Fits(empty.Title));
        Assert.True(new MeasuredLine(hintWidth, hintHeight, false).Fits(empty.Hint));
        Assert.True(empty.Title.Right <= metrics.Width);
        Assert.True(empty.Hint.Right <= metrics.Width);
        Assert.True(empty.Check.Width == FlyoutLayout.Px(48, dpi));
        Assert.False(empty.Title.Overlaps(empty.Hint));

        int openFont = FlyoutLayout.FontPixels(14, dpi);
        int openWidth = FlyoutLayout.EstimateWidth(FlyoutText.Open, openFont);
        int openHeight = FlyoutLayout.EstimateHeight(openFont);
        FlyoutSlot button = FlyoutLayout.FooterButton(metrics);
        Assert.True(new MeasuredLine(openWidth, openHeight, false).Fits(button));
        Assert.Equal(FlyoutLayout.Px(36, dpi), button.Height);
        Assert.Equal(0xFFFFFF, FlyoutLayout.LightBackground);
        Assert.Equal(0x202020, FlyoutLayout.DarkBackground);
        Assert.Equal(0x0F6CBD, FlyoutLayout.LightAccent);
        Assert.Equal(0x4CC2FF, FlyoutLayout.DarkAccent);
        Assert.Equal("Reacted ❤️ to your message", FlyoutText.Reaction);
        Assert.Equal("You're all caught up ✓", TrayText.EmptyState);
        int checkingWidth = FlyoutLayout.EstimateWidth(FlyoutText.Checking, hintFont);
        Assert.True(checkingWidth < metrics.Width - (metrics.HeaderPad * 2));
        Assert.Equal("Checking for messages…", FlyoutText.Checking);
    }

    [Fact]
    public void Five_rows_stay_inside_the_max_height()
    {
        FlyoutLayout.Metrics metrics = FlyoutLayout.At(144);
        int height = FlyoutLayout.PreferredHeight(metrics, 5, 0);
        Assert.True(height <= metrics.MaxHeight);
        Assert.Equal(FlyoutLayout.Px(420, 144), metrics.MaxHeight);
    }
}
