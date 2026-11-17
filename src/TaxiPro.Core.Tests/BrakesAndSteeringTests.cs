using TaxiPro.Core.Models;
using TaxiPro.Core.Physics;

namespace TaxiPro.Core.Tests;

public class BrakesTests
{
    [Fact]
    public void A_resting_foot_never_drags_the_brakes()
    {
        Assert.Equal(0, Brakes.Target(0.03));
        Assert.Equal(0, Brakes.Target(-1));
        Assert.Equal(0, Brakes.Target(double.NaN));
    }

    [Fact]
    public void Pedal_travel_is_progressive_with_full_pressure_at_the_end()
    {
        Assert.Equal(1, Brakes.Target(1), 6);
        Assert.True(Brakes.Target(0.5) < 0.4, "the first half of the pedal should be gentle");
        Assert.True(Brakes.Target(0.75) > Brakes.Target(0.5));
    }

    [Fact]
    public void Pressure_builds_over_the_rise_time_and_lets_go_faster()
    {
        var b = new Brakes();
        var out1 = b.Step(1, 1, 1.0, 0.1);
        Assert.Equal(0.1, out1.Left, 6);
        BrakeOutput o = out1;
        for (var i = 0; i < 9; i++) o = b.Step(1, 1, 1.0, 0.1);
        Assert.Equal(1, o.Left, 6);
        // Release at 0.4 × rise: gone in 0.4 s.
        for (var i = 0; i < 4; i++) o = b.Step(0, 0, 1.0, 0.1);
        Assert.Equal(0, o.Left, 6);
    }

    [Fact]
    public void After_release_it_writes_one_zero_then_goes_quiet()
    {
        var b = new Brakes();
        b.Step(1, 0, 0.5, 0.05);
        BrakeOutput o = default;
        for (var i = 0; i < 20; i++) o = b.Step(0, 0, 0.5, 0.05);
        Assert.False(o.Active);
        b = new Brakes();
        b.Step(1, 0, 0.5, 0.05);
        o = b.Step(0, 0, 0.5, 1); // pressure fully bled this step
        Assert.True(o.Active);
        Assert.Equal(0, o.Left);
        Assert.False(b.Step(0, 0, 0.5, 0.05).Active);
    }

    [Fact]
    public void Heavier_and_fuller_aircraft_build_pressure_slower()
    {
        var a320 = GroundProfiles.For(new AircraftIdentity("Fenix A320"), 0);
        var b777 = GroundProfiles.For(new AircraftIdentity("PMDG 777-300ER"), 0);
        var light = new GroundSnapshot { TotalWeightPounds = 400_000, MaxGrossWeightPounds = 775_000 };
        var full = light with { TotalWeightPounds = 770_000 };
        Assert.True(Brakes.RiseSeconds(b777, light) > Brakes.RiseSeconds(a320, light));
        Assert.True(Brakes.RiseSeconds(b777, full) > Brakes.RiseSeconds(b777, light));
    }

    [Fact]
    public void The_brake_key_builds_firm_pressure_while_held_and_releases()
    {
        var b = new Brakes();
        BrakeOutput o = default;
        for (var t = 0.0; t < 2; t += 0.05)
        {
            if (t % 0.1 < 0.05) b.PressKey(BrakeKey.Both); // MSFS repeats the key while held
            o = b.Step(0, 0, 0.5, 0.05);
        }
        Assert.Equal(Brakes.KeyPressure, o.Left, 6);
        Assert.Equal(Brakes.KeyPressure, o.Right, 6);
        for (var t = 0.0; t < Brakes.KeyHoldSeconds + 0.5; t += 0.05) o = b.Step(0, 0, 0.5, 0.05);
        Assert.Equal(0, o.Left);
    }

    [Fact]
    public void Left_brake_key_brakes_only_the_left_side()
    {
        var b = new Brakes();
        b.PressKey(BrakeKey.Left);
        var o = b.Step(0, 0, 0.5, 0.05);
        Assert.True(o.Left > 0);
        Assert.Equal(0, o.Right);
    }

    [Theory]
    [InlineData(-16383, 0)]
    [InlineData(16383, 1)]
    [InlineData(0, 0.5)]
    public void Axis_values_convert_to_pedal_travel(int axis, double pedal)
    {
        Assert.Equal(pedal, Brakes.PedalFromAxis(axis), 3);
        Assert.InRange(Brakes.AxisFromPedal(pedal), axis - 1, axis + 1);
    }
}

public class SteeringTests
{
    private static readonly GroundProfile B777 = GroundProfiles.For(new AircraftIdentity("PMDG 777-300ER"), 0);
    private static readonly GroundProfile A350 = GroundProfiles.For(new AircraftIdentity("iniBuilds A350-900"), 0);

    [Fact]
    public void Boeing_tiller_is_full_when_slow_and_fades_to_pedal_authority()
    {
        Assert.Equal(1, Steering.Authority(B777, 5));
        Assert.Equal(7.0 / 70, Steering.Authority(B777, 30), 6);
        Assert.Equal(7.0 / 70, Steering.Authority(B777, 120), 6);
        var mid = Steering.Authority(B777, 20);
        Assert.True(mid < 1 && mid > 0.1);
    }

    [Fact]
    public void Airbus_tiller_fades_to_nothing_by_70_kt()
    {
        Assert.Equal(1, Steering.Authority(A350, 20));
        Assert.Equal(0, Steering.Authority(A350, 70));
        Assert.Equal(0.5, Steering.Authority(A350, 45), 6);
    }

    [Fact]
    public void Shape_has_a_deadzone_a_soft_centre_and_full_ends()
    {
        Assert.Equal(0, Steering.Shape(0.015));
        Assert.Equal(1, Steering.Shape(1), 6);
        Assert.Equal(-1, Steering.Shape(-1), 6);
        Assert.True(Steering.Shape(0.3) < 0.3, "the centre should be softened");
        Assert.True(Steering.Shape(0.3) > 0);
    }

    [Fact]
    public void The_nosewheel_turns_at_the_actuator_rate()
    {
        var s = new Steering();
        var cmd = s.Step(1, 0, B777, 0.1);
        Assert.Equal(B777.SteerRateDegPerSec / B777.TillerDegrees * 0.1, cmd.Position, 6);
        for (var i = 0; i < 100; i++) cmd = s.Step(1, 0, B777, 0.1);
        Assert.Equal(1, cmd.Position, 6);
    }

    [Fact]
    public void Full_tiller_at_speed_gives_only_a_few_degrees()
    {
        var s = new Steering();
        SteerCommand cmd = default;
        for (var i = 0; i < 100; i++) cmd = s.Step(1, 40, B777, 0.1);
        Assert.Equal(0.1, cmd.Position, 6);
    }

    [Fact]
    public void Centred_and_settled_it_goes_quiet_so_the_pedals_steer()
    {
        var s = new Steering();
        for (var i = 0; i < 10; i++) s.Step(0.5, 0, B777, 0.1);
        SteerCommand cmd = default;
        var activeFrames = 0;
        for (var i = 0; i < 100; i++)
        {
            cmd = s.Step(0, 0, B777, 0.1);
            if (cmd.Active) activeFrames++;
        }
        Assert.False(cmd.Active);
        Assert.Equal(0, cmd.Position);
        Assert.True(activeFrames > 1, "the return to centre is sent");
        Assert.False(new Steering().Step(0, 0, B777, 0.1).Active);
    }

    [Theory]
    [InlineData(16384, 1)]
    [InlineData(-16384, -1)]
    [InlineData(0, 0)]
    public void Axis_values_convert(int axis, double tiller)
    {
        Assert.Equal(tiller, Steering.TillerFromAxis(axis), 3);
        Assert.Equal((int)Math.Round(tiller * 16383), Steering.AxisFromPosition(tiller));
    }
}
