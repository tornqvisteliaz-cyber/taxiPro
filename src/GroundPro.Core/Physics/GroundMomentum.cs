using GroundPro.Core.Models;

namespace GroundPro.Core.Physics;

public enum MomentumState
{
    /// <summary>GroundPro is not touching the speed (see Detail for why).</summary>
    Off,
    /// <summary>Parked with too little thrust to break away: the speed is held at zero.</summary>
    Holding,
    /// <summary>Rolling: the speed may not grow faster than thrust minus rolling resistance allows.</summary>
    Rolling
}

/// <summary>What the momentum model wants this frame.</summary>
/// <param name="Write">Write <paramref name="VelocityBodyZ"/> to the aircraft this frame.</param>
/// <param name="VelocityBodyZ">Forward speed to write, ft/s.</param>
/// <param name="ThrustPounds">Thrust GroundPro used (the sim's, or estimated from N1).</param>
/// <param name="BreakawayPounds">Thrust needed to start rolling from rest here.</param>
/// <param name="RollingPounds">Resistance while rolling (includes surface and turning).</param>
/// <param name="ExtraDecel">Deceleration GroundPro added this frame, ft/s².</param>
public readonly record struct SpeedCommand(
    bool Write,
    double VelocityBodyZ,
    MomentumState State,
    string Detail,
    double ThrustPounds = 0,
    double BreakawayPounds = 0,
    double RollingPounds = 0,
    double ExtraDecel = 0)
{
    public static SpeedCommand Off(string why) => new(false, 0, MomentumState.Off, why);

    /// <summary>Thrust as a share of breakaway thrust (1 = just enough to start rolling).</summary>
    public double BreakawayShare => BreakawayPounds > 0 ? ThrustPounds / BreakawayPounds : 0;
}

/// <summary>Which parts of the momentum model run, and how strongly.</summary>
/// <param name="Breakaway">Hold a stopped aircraft until thrust beats static friction.</param>
/// <param name="Weight">Limit acceleration to what thrust minus rolling resistance gives.</param>
/// <param name="Strength">0..1: how much of the difference between the sim's acceleration and the model's is removed.</param>
public readonly record struct MomentumOptions(bool Breakaway, bool Weight, double Strength);

/// <summary>
/// A heavy jet's longitudinal ground physics, applied on top of the sim's.
///
/// Every frame GroundPro works out the acceleration the aircraft should have,
/// a = (thrust − rolling resistance) / mass, with rolling resistance scaled by weight, surface and
/// how hard it is turning. If the sim lets the aircraft speed up faster than that, GroundPro writes a
/// lower forward speed. Parked, it needs breakaway thrust (more than rolling resistance) before it
/// moves at all; at idle a heavy jet then slows and settles back to rest.
///
/// Safety rules, all covered by tests:
/// - GroundPro only ever takes speed away. It never writes a speed above the sim's own.
/// - At most <see cref="MaxCorrection"/> ft/s² of extra deceleration, so nothing is ever abrupt.
/// - It stays out in the air, in slew, during pushback, with the parking brake set, rolling
///   backwards, on non-jets, and hands the speed back to the sim between 25 and 40 kt and at
///   takeoff thrust, so a takeoff roll is never touched.
/// </summary>
public sealed class GroundMomentum
{
    public const double StopFps = 0.5;               // ~0.3 kt: "stopped"
    public const double ReverseFps = -0.3;           // rolling backwards: pushback or a tug
    public const double HoldSlipFps = 3.0;           // ~1.8 kt: something is moving us harder than the hold
    public const double SettleSeconds = 0.3;         // stopped this long before the hold engages
    public const double HandOffStartKnots = 25, HandOffEndKnots = 40;
    public const double TakeoffN1Start = 70, TakeoffN1End = 80;
    public const double MaxCorrection = 6.0;         // ft/s², ~0.19 g
    public const double TurnScrubDegPerSec = 10;     // a 10°/s turn doubles rolling resistance

    private MomentumState _state = MomentumState.Off;
    private double _vRef;
    private bool _hasRef;
    private double _stoppedFor;

    public MomentumState State => _state;

    public void Reset()
    {
        _state = MomentumState.Off;
        _hasRef = false;
        _stoppedFor = 0;
    }

    /// <param name="dt">Sim seconds since the last frame (already scaled by sim rate).</param>
    public SpeedCommand Step(GroundSnapshot s, GroundProfile p, MomentumOptions o, double dt)
    {
        dt = Numeric.Clamp(Numeric.Finite(dt), 0, 0.25);
        var why = WhyOff(s, p, o);
        if (why != null)
        {
            Reset();
            return SpeedCommand.Off(why);
        }

        var weight = s.TotalWeightPounds;
        var mass = weight / Numeric.Gravity;
        var surface = Surface.ResistanceFactor(s.SurfaceType);
        var turn = Numeric.Clamp(Math.Abs(Numeric.Finite(s.YawRateDegPerSec)) / TurnScrubDegPerSec, 0, 1);
        var thrust = Thrust(s, p);
        var rolling = p.RollingCoefficient * surface * weight * (1 + turn);
        var breakaway = Math.Max(rolling, p.BreakawayCoefficient * surface * weight);
        var v = Numeric.Finite(s.VelocityBodyZ);
        var canBreakAway = thrust >= breakaway || s.MaxN1Percent >= p.BreakawayFailsafeN1;

        SpeedCommand Cmd(bool write, double velocity, string detail, double extra = 0) =>
            new(write, velocity, _state, detail, thrust, breakaway, rolling, extra);

        // ---- Breakaway: parked, the aircraft stays put until thrust beats static friction.
        if (o.Breakaway)
        {
            if (_state == MomentumState.Holding)
            {
                if (canBreakAway)
                {
                    _state = MomentumState.Rolling;
                    _vRef = v;
                    _hasRef = true;
                    _stoppedFor = 0;
                    return Cmd(false, v, "Breakaway: rolling");
                }
                if (v > HoldSlipFps)
                {
                    // Something else is moving the aircraft (a tug the sim doesn't report, or an aircraft
                    // that ignores the hold): let it go instead of fighting it.
                    _state = MomentumState.Rolling;
                    _vRef = v;
                    _hasRef = true;
                    _stoppedFor = 0;
                    return Cmd(false, v, "Hold slipped: let go");
                }
                return Cmd(true, 0, "Holding: needs breakaway thrust");
            }
            if (v < StopFps)
            {
                _stoppedFor += dt;
                if (_stoppedFor >= SettleSeconds && !canBreakAway)
                {
                    _state = MomentumState.Holding;
                    return Cmd(true, 0, "Holding: needs breakaway thrust");
                }
            }
            else
            {
                _stoppedFor = 0;
            }
        }

        // ---- Rolling: acceleration limited to (thrust − resistance) / mass.
        if (_state != MomentumState.Rolling)
        {
            _state = MomentumState.Rolling;
            _hasRef = false;
        }
        var authority = Authority(s, o);
        if (!o.Weight || authority <= 0)
        {
            _vRef = v;
            _hasRef = true;
            return Cmd(false, v, authority <= 0 && o.Weight ? "Handing over to the sim" : "Rolling");
        }
        if (!_hasRef)
        {
            _vRef = v;
            _hasRef = true;
            return Cmd(false, v, "Rolling");
        }

        var modelAccel = (thrust - rolling) / mass;
        var allowed = _vRef + modelAccel * dt;
        if (v > allowed + 1e-4)
        {
            var correction = Math.Min(authority * (v - allowed), MaxCorrection * dt);
            var target = Math.Max(0, v - correction);
            _vRef = target;
            return Cmd(true, target, "Rolling: weight applied", dt > 0 ? correction / dt : 0);
        }
        _vRef = v;
        return Cmd(false, v, "Rolling");
    }

    /// <summary>Why the momentum model stays out this frame, or null when it runs.</summary>
    public static string? WhyOff(GroundSnapshot s, GroundProfile p, MomentumOptions o)
    {
        if (!o.Breakaway && !o.Weight) return "Off";
        if (!s.OnGround) return "In the air";
        if (s.SlewActive) return "Slew mode";
        if (s.PushbackActive) return "Pushback";
        if (s.ParkingBrakeSet) return "Parking brake set";
        if (!p.HasMomentumModel || !s.IsJet) return "Breakaway and weight are for jet airliners";
        if (s.TotalWeightPounds < 1000) return "Waiting for the aircraft's weight";
        if (Numeric.Finite(s.VelocityBodyZ) < ReverseFps) return "Rolling backwards";
        if (s.GroundSpeedKnots >= HandOffEndKnots) return "Above 40 kt: the sim's own";
        return null;
    }

    /// <summary>How much of the correction applies: full while taxiing, fading out toward takeoff speed and thrust.</summary>
    public static double Authority(GroundSnapshot s, MomentumOptions o) =>
        Numeric.Clamp(o.Strength, 0, 1)
        * (1 - Numeric.Ramp(s.GroundSpeedKnots, HandOffStartKnots, HandOffEndKnots))
        * (1 - Numeric.Ramp(s.MaxN1Percent, TakeoffN1Start, TakeoffN1End));

    /// <summary>Total thrust: the sim's jet thrust when it reports one, otherwise an estimate from N1 and the type's rated thrust.</summary>
    public static double Thrust(GroundSnapshot s, GroundProfile p)
    {
        var reported = Numeric.Finite(s.ThrustPounds, -1);
        if (reported > 0)
            return reported;
        if (p.RatedThrustPerEngine <= 0 || s.EngineCount <= 0)
            return 0;
        return s.EngineCount * p.RatedThrustPerEngine * ThrustShare(s.MaxN1Percent);
    }

    /// <summary>
    /// Share of rated thrust at an N1 (high-bypass turbofan, roughly): about 4 % at ground idle
    /// (~20 % N1), about 10 % at 40 % N1, all of it at 100 %.
    /// </summary>
    public static double ThrustShare(double n1Percent)
    {
        var n1 = Numeric.Clamp(Numeric.Finite(n1Percent), 0, 110);
        if (n1 < 20)
            return 0.04 * n1 / 20;
        var x = (n1 - 20) / 80;
        return 0.04 + 0.96 * x * x;
    }
}
