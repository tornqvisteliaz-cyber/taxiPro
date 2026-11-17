using TaxiPro.Core;
using TaxiPro.Core.Models;
using TaxiPro.Core.Physics;

namespace TaxiPro.Core.Tests;

public class GroundMomentumTests
{
    [Fact]
    public void The_test_sim_creeps_at_idle_without_TaxiPro()
    {
        var v = Sim.Run(new GroundMomentum(), Sim.Parked777(), 10, new MomentumOptions(false, false, 0));
        Assert.True(v > 2, $"expected the slippery sim to creep, got {v:0.00} ft/s");
    }

    [Fact]
    public void A_parked_777_at_idle_does_not_creep()
    {
        var model = new GroundMomentum();
        var v = Sim.Run(model, Sim.Parked777(), 10, Sim.Full);
        Assert.Equal(0, v, 3);
        Assert.Equal(MomentumState.Holding, model.State);
    }

    [Fact]
    public void Breakaway_thrust_starts_it_rolling()
    {
        var model = new GroundMomentum();
        Sim.Run(model, Sim.Parked777(), 2, Sim.Full);
        var v = Sim.Run(model, Sim.Parked777(thrust: 20_000, n1: 36), 5, Sim.Full);
        Assert.Equal(MomentumState.Rolling, model.State);
        Assert.True(v > 1, $"got {v:0.00} ft/s");
    }

    [Fact]
    public void Back_at_idle_a_heavy_777_settles_to_a_stop_and_holds()
    {
        var model = new GroundMomentum();
        var rolling = Sim.Parked777().Rolling(15 * Numeric.FeetPerSecondPerKnot);
        var v = Sim.Run(model, rolling, 200, Sim.Full);
        Assert.Equal(0, v, 3);
        Assert.Equal(MomentumState.Holding, model.State);
    }

    [Fact]
    public void Speed_builds_at_thrust_minus_rolling_resistance_over_mass()
    {
        var model = new GroundMomentum();
        var s = Sim.Parked777(thrust: 20_000, n1: 36).Rolling(2);
        var v = Sim.Run(model, s, 10, Sim.Full);
        var p = Sim.Profile(s);
        var expected = 2 + 10 * (20_000 - p.RollingCoefficient * 550_000) / (550_000 / Numeric.Gravity);
        Assert.InRange(v, expected - 0.3, expected + 0.3);
        // The slippery sim alone would have been far faster.
        var simOnly = Sim.Run(new GroundMomentum(), s, 10, new MomentumOptions(false, false, 0));
        Assert.True(simOnly > v + 3);
    }

    [Fact]
    public void Half_strength_lands_between_the_sim_and_full_physics()
    {
        var s = Sim.Parked777(thrust: 20_000, n1: 36).Rolling(2);
        var full = Sim.Run(new GroundMomentum(), s, 10, Sim.Full);
        var half = Sim.Run(new GroundMomentum(), s, 10, new MomentumOptions(true, true, 0.5));
        var none = Sim.Run(new GroundMomentum(), s, 10, new MomentumOptions(true, true, 0));
        Assert.True(full < half && half < none, $"{full:0.0} < {half:0.0} < {none:0.0}");
    }

    [Fact]
    public void It_never_adds_speed_and_never_corrects_abruptly()
    {
        var rng = new Random(7);
        var model = new GroundMomentum();
        var p = Sim.Profile(Sim.Parked777());
        for (var i = 0; i < 20_000; i++)
        {
            var s = Sim.Parked777(thrust: rng.NextDouble() * 60_000, n1: 20 + rng.NextDouble() * 60)
                .Rolling(rng.NextDouble() * 60 - 1) with { YawRateDegPerSec = rng.NextDouble() * 20 - 10 };
            var cmd = model.Step(s, p, Sim.Full, Sim.Dt);
            if (!cmd.Write)
                continue;
            Assert.True(cmd.VelocityBodyZ >= 0);
            Assert.True(cmd.VelocityBodyZ <= Math.Max(0, s.VelocityBodyZ) + 1e-9, "wrote a speed above the sim's");
            if (cmd.State == MomentumState.Rolling)
                Assert.True(s.VelocityBodyZ - cmd.VelocityBodyZ <= GroundMomentum.MaxCorrection * Sim.Dt + 1e-9);
        }
    }

    public static IEnumerable<object[]> OffCases() => new[]
    {
        new object[] { Sim.Parked777() with { OnGround = false }, "In the air" },
        new object[] { Sim.Parked777() with { ParkingBrakeSet = true }, "Parking brake set" },
        new object[] { Sim.Parked777() with { PushbackActive = true }, "Pushback" },
        new object[] { Sim.Parked777() with { SlewActive = true }, "Slew mode" },
        new object[] { Sim.Parked777().Rolling(-2), "Rolling backwards" },
        new object[] { Sim.Parked777() with { EngineType = 5 }, "Breakaway and weight are for jet airliners" },
        new object[] { Sim.Parked777() with { TotalWeightPounds = 0 }, "Waiting for the aircraft's weight" },
        new object[] { Sim.Parked777().Rolling(45 * Numeric.FeetPerSecondPerKnot), "Above 40 kt: the sim's own" },
    };

    [Theory]
    [MemberData(nameof(OffCases))]
    public void It_stays_out_when_it_should(GroundSnapshot s, string why)
    {
        var model = new GroundMomentum();
        SpeedCommand cmd = default;
        for (var i = 0; i < 60; i++)
            cmd = model.Step(s, Sim.Profile(s), Sim.Full, Sim.Dt);
        Assert.False(cmd.Write);
        Assert.Equal(MomentumState.Off, cmd.State);
        Assert.Equal(why, cmd.Detail);
    }

    [Fact]
    public void Light_aircraft_are_left_alone()
    {
        var c172 = new GroundSnapshot { Aircraft = new("Cessna Skyhawk C172"), OnGround = true, TotalWeightPounds = 2400, EngineType = 0, EngineCount = 1 };
        var cmd = new GroundMomentum().Step(c172, Sim.Profile(c172), Sim.Full, Sim.Dt);
        Assert.False(cmd.Write);
    }

    [Fact]
    public void Takeoff_thrust_hands_the_speed_back_to_the_sim()
    {
        var model = new GroundMomentum();
        var s = Sim.Parked777(thrust: 150_000, n1: 85).Rolling(10);
        var wrote = false;
        Sim.Run(model, s, 5, Sim.Full, each: c => wrote |= c.Write);
        Assert.False(wrote);
    }

    [Fact]
    public void Grass_needs_much_more_breakaway_thrust()
    {
        var concrete = new GroundMomentum();
        Sim.Run(concrete, Sim.Parked777(thrust: 20_000, n1: 36), 3, Sim.Full);
        Assert.Equal(MomentumState.Rolling, concrete.State);

        var grass = new GroundMomentum();
        var v = Sim.Run(grass, Sim.Parked777(thrust: 20_000, n1: 36) with { SurfaceType = 1 }, 3, Sim.Full);
        Assert.Equal(MomentumState.Holding, grass.State);
        Assert.Equal(0, v, 3);
    }

    [Fact]
    public void Turning_adds_scrub_to_the_rolling_resistance()
    {
        var s = Sim.Parked777(thrust: 20_000, n1: 36).Rolling(10);
        var straight = new GroundMomentum().Step(s, Sim.Profile(s), Sim.Full, Sim.Dt);
        var turning = new GroundMomentum().Step(s with { YawRateDegPerSec = 10 }, Sim.Profile(s), Sim.Full, Sim.Dt);
        Assert.Equal(2 * straight.RollingPounds, turning.RollingPounds, 3);
    }

    [Fact]
    public void Without_a_thrust_reading_it_estimates_thrust_from_N1()
    {
        Assert.InRange(GroundMomentum.ThrustShare(21), 0.04, 0.05);
        Assert.InRange(GroundMomentum.ThrustShare(40), 0.09, 0.11);
        Assert.Equal(1, GroundMomentum.ThrustShare(100), 3);

        var model = new GroundMomentum();
        var idle = Sim.Parked777(thrust: double.NaN, n1: 21);
        for (var i = 0; i < 30; i++) model.Step(idle, Sim.Profile(idle), Sim.Full, Sim.Dt);
        Assert.Equal(MomentumState.Holding, model.State);
        var push = idle with { MaxN1Percent = 40 };
        model.Step(push, Sim.Profile(push), Sim.Full, Sim.Dt);
        Assert.Equal(MomentumState.Rolling, model.State);
    }

    [Fact]
    public void A_high_N1_always_releases_the_hold_whatever_the_thrust_reading()
    {
        var model = new GroundMomentum();
        var s = Sim.Parked777(thrust: 100, n1: 21);
        for (var i = 0; i < 30; i++) model.Step(s, Sim.Profile(s), Sim.Full, Sim.Dt);
        Assert.Equal(MomentumState.Holding, model.State);
        model.Step(s with { MaxN1Percent = 55 }, Sim.Profile(s), Sim.Full, Sim.Dt);
        Assert.Equal(MomentumState.Rolling, model.State);
    }

    [Fact]
    public void A_hold_that_something_else_overpowers_lets_go()
    {
        var model = new GroundMomentum();
        var s = Sim.Parked777();
        for (var i = 0; i < 30; i++) model.Step(s, Sim.Profile(s), Sim.Full, Sim.Dt);
        var cmd = model.Step(s.Rolling(4), Sim.Profile(s), Sim.Full, Sim.Dt);
        Assert.False(cmd.Write);
        Assert.Equal("Hold slipped: let go", cmd.Detail);
    }

    [Fact]
    public void Breakaway_off_lets_idle_thrust_move_it_but_weight_still_applies()
    {
        var model = new GroundMomentum();
        // Light 777 at idle with more thrust than rolling resistance: it creeps, slower than the slippery sim.
        var s = Sim.Parked777(thrust: 10_000, n1: 22) with { TotalWeightPounds = 400_000 };
        var v = Sim.Run(model, s, 10, new MomentumOptions(false, true, 1));
        var simOnly = Sim.Run(new GroundMomentum(), s, 10, new MomentumOptions(false, false, 0));
        Assert.True(v > 0.5 && v < simOnly, $"{v:0.00} vs sim {simOnly:0.00}");
    }
}
