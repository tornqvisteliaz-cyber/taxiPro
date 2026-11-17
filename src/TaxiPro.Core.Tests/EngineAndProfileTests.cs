using TaxiPro.Core.Models;
using TaxiPro.Core.Physics;
using TaxiPro.Core.Services;

namespace TaxiPro.Core.Tests;

public class ProfileTests
{
    [Theory]
    [InlineData("PMDG 777-300ER Emirates", "", "", "PMDG 777", AircraftClass.WideBody)]
    [InlineData("iniBuilds A350-1000 Airbus House", "", "", "iniBuilds A350-1000", AircraftClass.WideBody)]
    [InlineData("iniBuilds A350-900", "", "", "iniBuilds A350-900", AircraftClass.WideBody)]
    [InlineData("Fenix A320 IAE", "", "", "Fenix A320", AircraftClass.NarrowBody)]
    [InlineData("PMDG 737-800 BW", "", "", "PMDG 737", AircraftClass.NarrowBody)]
    [InlineData("Something", "ATCCOM.AC_MODEL B748.0.text", "", "Boeing 747", AircraftClass.HeavyWide)]
    [InlineData("Airbus A380-800", "", "", "Airbus A380", AircraftClass.HeavyWide)]
    public void Known_types_are_recognised(string title, string model, string type, string name, AircraftClass cls)
    {
        var p = GroundProfiles.For(new AircraftIdentity(title, model, type), 0);
        Assert.Equal(name, p.Name);
        Assert.Equal(cls, p.Class);
        Assert.True(p.HasMomentumModel);
    }

    [Fact]
    public void Airbus_tillers_fade_to_zero_and_Boeing_tillers_to_the_pedals()
    {
        var a350 = GroundProfiles.For(new AircraftIdentity("iniBuilds A350-900"), 0);
        var b777 = GroundProfiles.For(new AircraftIdentity("PMDG 777-300ER"), 0);
        Assert.Equal(0, a350.TillerMinimumAuthority);
        Assert.Equal(0.1, b777.TillerMinimumAuthority, 6);
        Assert.Equal(70, b777.TillerDegrees);
        Assert.Equal(7, b777.PedalSteerDegrees);
    }

    [Fact]
    public void Unknown_aircraft_fall_back_to_their_weight_class()
    {
        Assert.Equal(AircraftClass.HeavyWide, GroundProfiles.For(new AircraftIdentity("Mystery jet"), 900_000).Class);
        Assert.Equal(AircraftClass.NarrowBody, GroundProfiles.For(new AircraftIdentity("Mystery jet"), 150_000).Class);
        var light = GroundProfiles.For(new AircraftIdentity("Cessna C172"), 0);
        Assert.False(light.HasMomentumModel);
        Assert.Equal(0, light.TillerDegrees);
    }

    [Fact]
    public void Numbers_inside_longer_numbers_do_not_match()
    {
        Assert.Equal(AircraftClass.LightGa, AircraftCatalog.Classify(new AircraftIdentity("Cessna C172"), 0));
        Assert.Equal(AircraftClass.Unknown, AircraftCatalog.Classify(new AircraftIdentity("Prototype C1727"), 0));
        Assert.NotEqual("Boeing 777", GroundProfiles.For(new AircraftIdentity("Model 17777"), 0).Name);
    }
}

public class TaxiEngineTests
{
    private static readonly TaxiSettings On = new();

    [Fact]
    public void Master_off_owns_nothing_and_writes_nothing()
    {
        var engine = Sim.Engine();
        var settings = new TaxiSettings { MasterEnable = false };
        TaxiOutput o = null!;
        for (var i = 0; i < 30; i++) o = engine.Step(Sim.Parked777(), new TaxiInputs(1, 1, 1), settings, false, Sim.Dt);
        Assert.False(o.Speed.Write);
        Assert.False(o.OwnsBrakes);
        Assert.False(o.OwnsTiller);
        Assert.Equal("OFF", o.Badge);
    }

    [Fact]
    public void Parked_at_idle_it_holds()
    {
        var engine = Sim.Engine();
        TaxiOutput o = null!;
        for (var i = 0; i < 30; i++) o = engine.Step(Sim.Parked777(), default, On, false, Sim.Dt);
        Assert.Equal("HOLDING", o.Badge);
        Assert.True(o.Speed.Write);
        Assert.Equal("PMDG 777", o.Profile.Name);
    }

    [Fact]
    public void Brakes_are_left_to_HeavyPro_when_it_shapes_them()
    {
        var engine = Sim.Engine();
        var o = engine.Step(Sim.Parked777(), new TaxiInputs(1, 1, 0), On, heavyProOwnsBrakes: true, Sim.Dt);
        Assert.False(o.OwnsBrakes);
        Assert.Equal("HeavyPro is shaping the toe brakes", o.BrakeNote);
        var mine = engine.Step(Sim.Parked777(), new TaxiInputs(1, 1, 0), new TaxiSettings { YieldBrakesToHeavyPro = false }, true, Sim.Dt);
        Assert.True(mine.OwnsBrakes);
    }

    [Fact]
    public void An_aircraft_that_ignores_the_hold_is_detected_and_left_alone()
    {
        var engine = Sim.Engine();
        var s = Sim.Parked777();
        // The aircraft ignores every write: each time the hold engages it keeps creeping until it slips.
        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < 30; i++) engine.Step(s, default, On, false, Sim.Dt);
            for (var v = 0.0; v < 4; v += 0.1) engine.Step(s.Rolling(v), default, On, false, Sim.Dt);
            for (var v = 4.0; v > 0; v -= 0.5) engine.Step(s.Rolling(v), default, On, false, Sim.Dt);
        }
        Assert.Equal(Acceptance.Ignored, engine.SpeedAcceptance);
        var o = engine.Step(s, default, On, false, Sim.Dt);
        Assert.False(o.Speed.Write);
        Assert.Equal("This aircraft overrides speed writes", o.Speed.Detail);
    }

    [Fact]
    public void A_working_hold_is_confirmed()
    {
        var engine = Sim.Engine();
        // Thrust above rolling resistance but below breakaway: without the hold it would creep.
        for (var i = 0; i < 120; i++) engine.Step(Sim.Parked777(thrust: 12_000, n1: 28), default, On, false, Sim.Dt);
        Assert.Equal(Acceptance.Works, engine.SpeedAcceptance);
    }

    [Fact]
    public void Brake_writes_the_aircraft_ignores_fall_back_to_axis_events_then_pass_through()
    {
        var engine = Sim.Engine();
        var s = Sim.Parked777() with { BrakeLeft = 0, BrakeRight = 0 }; // readback never moves
        for (var i = 0; i < 300 && engine.BrakeRoute == BrakeRoute.Position; i++)
            engine.Step(s, new TaxiInputs(1, 1, 0), On, false, Sim.Dt);
        Assert.Equal(BrakeRoute.AxisEvents, engine.BrakeRoute);
        for (var i = 0; i < 300 && engine.BrakeRoute == BrakeRoute.AxisEvents; i++)
            engine.Step(s, new TaxiInputs(1, 1, 0), On, false, Sim.Dt);
        Assert.Equal(BrakeRoute.None, engine.BrakeRoute);
        Assert.False(engine.Step(s, new TaxiInputs(1, 1, 0), On, false, Sim.Dt).OwnsBrakes);
    }

    [Fact]
    public void Brake_writes_the_aircraft_takes_stay_on_the_position_route()
    {
        var engine = Sim.Engine();
        var last = 0.0;
        for (var i = 0; i < 120; i++)
            last = engine.Step(Sim.Parked777() with { BrakeLeft = last }, new TaxiInputs(1, 1, 0), On, false, Sim.Dt).Brakes.Left;
        Assert.Equal(BrakeRoute.Position, engine.BrakeRoute);
        Assert.Equal(Acceptance.Works, engine.BrakeAcceptance);
    }

    [Fact]
    public void Loading_another_aircraft_forgets_what_was_learnt()
    {
        var engine = Sim.Engine();
        for (var i = 0; i < 120; i++) engine.Step(Sim.Parked777(thrust: 12_000, n1: 28), default, On, false, Sim.Dt);
        Assert.Equal(Acceptance.Works, engine.SpeedAcceptance);
        var a350 = Sim.Parked777() with { Aircraft = new AircraftIdentity("iniBuilds A350-900") };
        var o = engine.Step(a350, default, On, false, Sim.Dt);
        Assert.Equal(Acceptance.Unknown, engine.SpeedAcceptance);
        Assert.Equal("iniBuilds A350-900", o.Profile.Name);
    }

    [Fact]
    public void The_tiller_is_not_owned_in_the_air_or_during_pushback()
    {
        var engine = Sim.Engine();
        Assert.False(engine.Step(Sim.Parked777() with { OnGround = false }, default, On, false, Sim.Dt).OwnsTiller);
        Assert.False(engine.Step(Sim.Parked777() with { PushbackActive = true }, default, On, false, Sim.Dt).OwnsTiller);
        Assert.True(engine.Step(Sim.Parked777(), default, On, false, Sim.Dt).OwnsTiller);
    }

    [Fact]
    public void Sim_rate_speeds_up_the_models()
    {
        var a = Sim.Engine();
        var b = Sim.Engine();
        var s = Sim.Parked777();
        var normal = a.Step(s, new TaxiInputs(1, 1, 0), On, false, 0.04).Brakes.Left;
        var fast = b.Step(s with { SimulationRate = 2 }, new TaxiInputs(1, 1, 0), On, false, 0.04).Brakes.Left;
        Assert.Equal(2 * normal, fast, 6);
    }
}

public class HeavyProLinkTests
{
    [Fact]
    public void HeavyPro_owns_the_brakes_only_when_running_on_and_set_up()
    {
        const string setUp = "{\"MasterEnable\":true,\"ToeBrakes\":true,\"LeftBrakeAxis\":\"Rz\",\"RightBrakeAxis\":\"\"}";
        Assert.True(HeavyProLink.OwnsBrakes(true, setUp));
        Assert.False(HeavyProLink.OwnsBrakes(false, setUp));
        Assert.False(HeavyProLink.OwnsBrakes(true, "{\"ToeBrakes\":true,\"LeftBrakeAxis\":\"\",\"RightBrakeAxis\":\"\"}"));
        Assert.False(HeavyProLink.OwnsBrakes(true, "{\"ToeBrakes\":false,\"LeftBrakeAxis\":\"Rz\"}"));
        Assert.False(HeavyProLink.OwnsBrakes(true, "{\"MasterEnable\":false,\"LeftBrakeAxis\":\"Rz\"}"));
        Assert.True(HeavyProLink.OwnsBrakes(true, "{\"RightBrakeAxis\":\"Slider0\"}"));
        Assert.False(HeavyProLink.OwnsBrakes(true, "not json"));
        Assert.False(HeavyProLink.OwnsBrakes(true, null));
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void Settings_round_trip_and_a_broken_file_gives_defaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "taxipro-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            Assert.True(store.Load().MasterEnable);
            store.Save(new TaxiSettings { Tiller = false, WeightFeel = 140 });
            var loaded = store.Load();
            Assert.False(loaded.Tiller);
            Assert.Equal(100, loaded.WeightFeel);
            File.WriteAllText(store.FilePath, "{ broken");
            Assert.Equal(75, store.Load().WeightFeel);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

public class VersionTests
{
    [Fact]
    public void Version_matches_the_newest_changelog_entry()
    {
        var root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "Directory.Build.props")))
            root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var props = File.ReadAllText(Path.Combine(root!, "Directory.Build.props"));
        var version = System.Text.RegularExpressions.Regex.Match(props, "<Version>([0-9.]+)</Version>").Groups[1].Value;
        var changelog = File.ReadAllLines(Path.Combine(root!, "CHANGELOG.md"));
        var newest = changelog.First(l => l.StartsWith("## ", StringComparison.Ordinal)).Substring(3).Split(' ')[0];
        Assert.Equal(version, newest);
    }
}
