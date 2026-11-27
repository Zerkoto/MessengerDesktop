using Xunit;

namespace MessengerDesktop.Tests;

public class BareAltMenuTests
{
    [Theory]
    [InlineData(BareAltMenu.WmSysCommand, BareAltMenu.ScKeyMenu, 0, true)]
    [InlineData(BareAltMenu.WmSysCommand, BareAltMenu.ScKeyMenu | 0x3, 0, true)]
    [InlineData(BareAltMenu.WmSysCommand, BareAltMenu.ScKeyMenu, 0x20, false)]
    [InlineData(BareAltMenu.WmSysCommand, BareAltMenu.ScKeyMenu, 0x25, false)]
    [InlineData(BareAltMenu.WmSysCommand, 0xF060, 0, false)]
    [InlineData(BareAltMenu.WmSysCommand, 0xF090, 0, false)]
    [InlineData(0x0104, BareAltMenu.ScKeyMenu, 0, false)]
    [InlineData(BareAltMenu.WmSysCommand, BareAltMenu.ScKeyMenu, 0x00100000, false)]
    public void Bare_alt_does_not_open_a_menu_and_real_alt_keys_still_do(int message, int wParam, int lParam, bool ignore)
    {
        Assert.Equal(ignore, BareAltMenu.ShouldIgnore(message, wParam, lParam));
    }
}
