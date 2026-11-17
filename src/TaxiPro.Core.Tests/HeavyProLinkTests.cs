using TaxiPro.Core.Services;

namespace TaxiPro.Core.Tests;

/// <summary>TaxiPro steps aside from the toe brakes exactly when HeavyPro shapes them.</summary>
public class HeavyProLinkTests
{
    // HeavyPro's own test fixture shape (MsfsInputProfilesTests.PedalsWithToeBrakes): a T-Rudder,
    // USB product id 46713 (0xB679), whose toe brakes are bound in an MSFS airplane profile.
    private static string Pedals(bool brakesBound, string category = "AIRPLANE", int productId = 46713) => $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Version Num=""-1""/>
<FriendlyName PlatformAvailability=""1"" Locked=""false"">pedals</FriendlyName>
<Device DeviceName=""T-Rudder"" GUID=""{{0}}"" ProductID=""{productId}"" CompositeID=""0"" HWVer=""1.0.0.0"">
	<AircraftInfo CategoryName=""{category}""/>
	<Context ContextName=""SURFACES"">
		<Action ActionName=""KEY_AXIS_RUDDER_SET"" Flag=""2""><Primary><KEY Information=""Joystick L-Axis Z "">1058</KEY></Primary></Action>
		{(brakesBound
            ? @"<Action ActionName=""KEY_AXIS_LEFT_BRAKE_SET"" Flag=""2""><Primary><KEY Information=""Joystick L-Axis X "">1026</KEY></Primary></Action>
		<Action ActionName=""KEY_AXIS_RIGHT_BRAKE_SET"" Flag=""2""><Primary><KEY Information=""Joystick L-Axis Y "">1042</KEY></Primary></Action>"
            : @"<Action ActionName=""KEY_AXIS_LEFT_BRAKE_SET"" ValueEvent=""0.000000"" Delay=""0.000000"" Flag=""2""/>
		<Action ActionName=""KEY_AXIS_RIGHT_BRAKE_SET"" ValueEvent=""0.000000"" Delay=""0.000000"" Flag=""2""/>")}
	</Context>
</Device>";

    private const string SetUp = "{\"MasterEnable\":true,\"ToeBrakes\":true,\"LeftBrakeAxis\":\"Rx\",\"RightBrakeAxis\":\"Ry\",\"RudderDevice\":\"044F:B679\",\"StickDevice\":\"4098:BC27\"}";

    [Fact]
    public void HeavyPro_shapes_the_brakes_when_set_up_and_unbound_in_MSFS()
    {
        var state = HeavyProLink.Brakes(true, SetUp, new[] { Pedals(brakesBound: false) });
        Assert.True(state.OwnsBrakes);
        // No MSFS profiles found (not the Steam version): HeavyPro sees no conflict either.
        Assert.True(HeavyProLink.Brakes(true, SetUp, Array.Empty<string>()).OwnsBrakes);
    }

    [Fact]
    public void Brakes_still_bound_in_MSFS_make_HeavyPro_stand_aside_so_TaxiPro_shapes_them()
    {
        var state = HeavyProLink.Brakes(true, SetUp, new[] { Pedals(brakesBound: true) });
        Assert.False(state.OwnsBrakes);
        Assert.Contains("still bound", state.Why);
    }

    [Fact]
    public void Only_airplane_profiles_of_HeavyPros_own_pedals_count()
    {
        Assert.True(HeavyProLink.Brakes(true, SetUp, new[] { Pedals(true, category: "HELICOPTER") }).OwnsBrakes);
        Assert.True(HeavyProLink.Brakes(true, SetUp, new[] { Pedals(true, productId: 1234) }).OwnsBrakes);
    }

    [Fact]
    public void Pedals_on_the_stick_device_are_not_checked_as_HeavyPro_does_not()
    {
        var sameDevice = SetUp.Replace("\"StickDevice\":\"4098:BC27\"", "\"StickDevice\":\"044F:B679\"");
        Assert.True(HeavyProLink.Brakes(true, sameDevice, new[] { Pedals(brakesBound: true) }).OwnsBrakes);
    }

    [Theory]
    [InlineData("{\"MasterEnable\":false,\"LeftBrakeAxis\":\"Rx\",\"RudderDevice\":\"044F:B679\"}", "Master Enable off")]
    [InlineData("{\"ToeBrakes\":false,\"LeftBrakeAxis\":\"Rx\",\"RudderDevice\":\"044F:B679\"}", "Heavy toe brakes off")]
    [InlineData("{\"LeftBrakeAxis\":\"\",\"RightBrakeAxis\":\"\",\"RudderDevice\":\"044F:B679\"}", "not set up")]
    [InlineData("{\"LeftBrakeAxis\":\"Rx\",\"RudderDevice\":\"\"}", "not set up")]
    [InlineData("not json", "unreadable")]
    public void HeavyPro_does_not_shape_the_brakes_when_switched_off_or_not_set_up(string settings, string why)
    {
        var state = HeavyProLink.Brakes(true, settings, Array.Empty<string>());
        Assert.False(state.OwnsBrakes);
        Assert.True(state.Running);
        Assert.Contains(why, state.Why);
    }

    [Fact]
    public void Not_running_means_TaxiPro_has_the_brakes()
    {
        Assert.False(HeavyProLink.Brakes(false, SetUp, Array.Empty<string>()).OwnsBrakes);
        Assert.Equal(0xB679, HeavyProLink.ProductIdFromKey("044F:B679"));
        Assert.Null(HeavyProLink.ProductIdFromKey(""));
    }

    [Fact]
    public void A_button_on_the_brake_action_is_not_an_axis_binding()
    {
        var button = Pedals(true).Replace(@"<KEY Information=""Joystick L-Axis X "">1026</KEY>", @"<KEY Information=""Joystick Button 3 "">3</KEY>")
            .Replace(@"<Action ActionName=""KEY_AXIS_RIGHT_BRAKE_SET"" Flag=""2""><Primary><KEY Information=""Joystick L-Axis Y "">1042</KEY></Primary></Action>", "");
        Assert.False(HeavyProLink.BrakesBoundInMsfs(new[] { button }, 46713));
    }
}
