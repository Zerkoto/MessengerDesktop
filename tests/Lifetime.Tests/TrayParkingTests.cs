using Xunit;

namespace MessengerDesktop.Tests;

public class TrayParkingTests
{
    [Fact]
    public void Parking_keeps_the_form_visible_and_off_screen()
    {
        TrayParkingPlan plan = TrayParking.Park();

        Assert.True(plan.FormVisible);
        Assert.False(plan.ShowInTaskbar);
        Assert.False(plan.Minimized);
        Assert.True(plan.ControllerVisible);
        Assert.Equal(TrayParking.OffScreenX, plan.X);
        Assert.Equal(TrayParking.OffScreenY, plan.Y);
        Assert.True(plan.X < -1000);
        Assert.True(plan.Y < -1000);
    }

    [Fact]
    public void The_toast_app_id_is_one_stable_name()
    {
        Assert.Equal("MessengerDesktop.Messenger", ToastIdentity.AppUserModelId);
        Assert.Equal("Messenger", ToastIdentity.DisplayName);
        Assert.DoesNotContain('\\', ToastIdentity.AppUserModelId);
        Assert.DoesNotContain('/', ToastIdentity.AppUserModelId);
        Assert.Equal("", ToastIdentity.IconUri(""));
        Assert.Equal("", ToastIdentity.IconUri("Z:\\missing\\Messenger.exe"));
    }
}
