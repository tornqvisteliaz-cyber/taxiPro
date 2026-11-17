using TaxiPro.Core.Models;

namespace TaxiPro.Core.Physics;

/// <summary>How TaxiPro's brake pressure reaches the aircraft.</summary>
public enum BrakeRoute
{
    /// <summary>Writing BRAKE LEFT/RIGHT POSITION (what HeavyPro uses; the PMDG 777 takes it).</summary>
    Position,
    /// <summary>Sending AXIS_LEFT/RIGHT_BRAKE_SET, like a brake pedal would.</summary>
    AxisEvents,
    /// <summary>Neither is accepted by this aircraft: brakes pass through untouched.</summary>
    None
}

/// <summary>The user's controls this frame, as TaxiPro captured them.</summary>
/// <param name="LeftPedal">Left toe brake, 0..1.</param>
/// <param name="RightPedal">Right toe brake, 0..1.</param>
/// <param name="Tiller">Tiller, −1..1.</param>
public readonly record struct TaxiInputs(double LeftPedal, double RightPedal, double Tiller);

/// <summary>Everything TaxiPro decided for one frame.</summary>
public sealed record TaxiOutput(
    GroundProfile Profile,
    SpeedCommand Speed,
    BrakeOutput Brakes,
    bool OwnsBrakes,
    BrakeRoute BrakeRoute,
    SteerCommand Steering,
    bool OwnsTiller,
    string Badge,
    string BrakeNote,
    string TillerNote);

/// <summary>
/// Runs TaxiPro's three ground effects for one sim frame: breakaway and weight (GroundMomentum),
/// progressive brakes (Brakes) and the speed-scheduled tiller (Steering). It also watches whether
/// the aircraft accepts each kind of write and gives up on one the aircraft overrides, instead of
/// fighting it. No SimConnect here: the app reads the sim, calls Step, and sends what comes back.
/// </summary>
public sealed class TaxiEngine
{
    private readonly Func<bool> _licensed;
    private readonly GroundMomentum _momentum = new();
    private readonly Brakes _brakes = new();
    private readonly Steering _steering = new();
    private readonly AcceptanceMonitor _speedCheck = new();
    private readonly AcceptanceMonitor _brakeCheck = new(windowSeconds: 1.0);
    private int _slips;
    private string _aircraftKey = "";
    private GroundProfile _profile = GroundProfiles.Generic(AircraftClass.Unknown);

    /// <param name="licensed">Whether TaxiPro may write; normally <see cref="LicenseGate.Unlocked"/>.</param>
    public TaxiEngine(Func<bool>? licensed = null) => _licensed = licensed ?? (() => LicenseGate.Unlocked);

    public Acceptance SpeedAcceptance => _speedCheck.State;
    public Acceptance BrakeAcceptance => _brakeCheck.State;
    public BrakeRoute BrakeRoute { get; private set; } = BrakeRoute.Position;
    public GroundProfile Profile => _profile;

    /// <summary>A keyboard brake key event arrived (MSFS repeats it while the key is held).</summary>
    public void PressBrakeKey(BrakeKey key) => _brakes.PressKey(key);

    /// <param name="s">This frame's sim data.</param>
    /// <param name="input">The user's captured pedals and tiller.</param>
    /// <param name="settings">The user's settings.</param>
    /// <param name="heavyProOwnsBrakes">HeavyPro is running and shaping the toe brakes itself.</param>
    /// <param name="dt">Real seconds since the last frame.</param>
    public TaxiOutput Step(GroundSnapshot s, TaxiInputs input, TaxiSettings settings, bool heavyProOwnsBrakes, double dt)
    {
        dt = Numeric.Clamp(Numeric.Finite(dt), 0, 0.25) * Numeric.Clamp(Numeric.Finite(s.SimulationRate, 1), 0.1, 16);
        UpdateAircraft(s);
        var p = _profile;
        var licensed = _licensed();
        // Unlicensed is the same as switched off: everything passes straight through.
        var master = settings.MasterEnable && licensed;

        // ---- Breakaway and weight
        SpeedCommand speed;
        if (_speedCheck.State == Acceptance.Ignored)
        {
            _momentum.Reset();
            speed = SpeedCommand.Off("This aircraft overrides speed writes");
        }
        else
        {
            var options = new MomentumOptions(master && settings.Breakaway, master && settings.Weight, settings.WeightFeel / 100.0);
            speed = _momentum.Step(s, p, options, dt);
            // Judge the hold only while it is doing something: thrust above rolling resistance would
            // move the aircraft without it. A hold that slips twice is overridden whatever came before.
            if (speed.State == MomentumState.Holding && speed.Write && speed.ThrustPounds > speed.RollingPounds)
                _speedCheck.Observe(0, s.VelocityBodyZ, 1.0, dt);
            if (speed.Detail.StartsWith("Hold slipped", StringComparison.Ordinal) && ++_slips >= 2)
                _speedCheck.Decide(Acceptance.Ignored);
        }

        // ---- Brakes
        string brakeNote;
        var ownsBrakes = false;
        var brakes = BrakeOutput.Idle;
        if (!master || !settings.Brakes) brakeNote = "Off: brakes pass through";
        else if (settings.YieldBrakesToHeavyPro && heavyProOwnsBrakes) brakeNote = "HeavyPro is shaping the toe brakes";
        else if (BrakeRoute == BrakeRoute.None) brakeNote = "This aircraft overrides brake writes: brakes pass through";
        else if (s.ParkingBrakeSet) brakeNote = "Parking brake set";
        else
        {
            ownsBrakes = true;
            brakes = _brakes.Step(input.LeftPedal, input.RightPedal, Brakes.RiseSeconds(p, s), dt);
            brakeNote = BrakeRoute == BrakeRoute.Position ? "Progressive brakes" : "Progressive brakes (axis route)";
            if (brakes.Active && Math.Max(brakes.Left, brakes.Right) > 0.05)
            {
                _brakeCheck.Observe(brakes.Left, s.BrakeLeft, 0.15, dt);
                if (_brakeCheck.State == Acceptance.Ignored)
                {
                    BrakeRoute = BrakeRoute == BrakeRoute.Position ? BrakeRoute.AxisEvents : BrakeRoute.None;
                    if (BrakeRoute != BrakeRoute.None)
                        _brakeCheck.Reset();
                }
            }
        }
        if (!ownsBrakes)
            _brakes.Reset();

        // ---- Tiller
        string tillerNote;
        var ownsTiller = false;
        var steer = SteerCommand.Idle;
        if (!master || !settings.Tiller) tillerNote = "Off: tiller passes through";
        else if (!s.OnGround) tillerNote = "In the air";
        else if (s.PushbackActive) tillerNote = "Pushback: steering is the tug's";
        else if (s.SlewActive) tillerNote = "Slew mode";
        else if (p.TillerDegrees <= 0) tillerNote = "No tiller on this aircraft";
        else
        {
            ownsTiller = true;
            steer = _steering.Step(input.Tiller, s.GroundSpeedKnots, p, dt);
            tillerNote = steer.Authority >= 0.999
                ? $"Full tiller ({p.TillerDegrees:0}°)"
                : $"Tiller {steer.Authority * 100:0} % at {s.GroundSpeedKnots:0} kt";
        }
        if (!ownsTiller)
            _steering.Reset();

        return new TaxiOutput(p, speed, brakes, ownsBrakes, BrakeRoute, steer, ownsTiller,
            licensed ? Badge(s, master, speed, brakes) : "NOT LICENSED", brakeNote, tillerNote);
    }

    /// <summary>Forgets what was learnt about the aircraft (another aircraft was loaded, or the user asked to retry).</summary>
    public void ResetChecks()
    {
        _speedCheck.Reset();
        _brakeCheck.Reset();
        BrakeRoute = BrakeRoute.Position;
        _slips = 0;
        _momentum.Reset();
        _brakes.Reset();
        _steering.Reset();
    }

    private void UpdateAircraft(GroundSnapshot s)
    {
        var key = $"{s.Aircraft.Title}|{s.Aircraft.AtcModel}|{s.Aircraft.AtcType}|{Math.Round(s.MaxGrossWeightPounds / 1000)}";
        if (key == _aircraftKey)
            return;
        _aircraftKey = key;
        _profile = GroundProfiles.For(s.Aircraft, s.MaxGrossWeightPounds);
        ResetChecks();
    }

    private static string Badge(GroundSnapshot s, bool master, SpeedCommand speed, BrakeOutput brakes)
    {
        if (!master) return "OFF";
        if (!s.OnGround) return "IN THE AIR";
        if (s.SlewActive) return "SLEW";
        if (s.PushbackActive) return "PUSHBACK";
        if (s.ParkingBrakeSet) return "PARKED";
        if (speed.State == MomentumState.Holding) return "HOLDING";
        if (brakes.Active && Math.Max(brakes.Left, brakes.Right) > 0.02) return "BRAKING";
        if (s.GroundSpeedKnots >= GroundMomentum.HandOffEndKnots) return "TAKEOFF / LANDING";
        return "TAXIING";
    }
}
