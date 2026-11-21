using GroundPro.Core.Models;

namespace GroundPro.Core.Physics;

/// <summary>Nosewheel steering command, −1 (full left) … +1 (full right), and whether to send it.</summary>
public readonly record struct SteerCommand(double Position, bool Active, double Authority)
{
    public static SteerCommand Idle => new(0, false, 1);
}

/// <summary>
/// The tiller as on the real aircraft:
/// - full nosewheel authority only when slow; above <see cref="GroundProfile.TillerFullBelowKnots"/>
///   it fades, to the pedals' few degrees on a Boeing and to nothing on an Airbus, so a twitchy
///   tiller axis can't throw a heavy jet off the runway at speed;
/// - the nosewheel follows at the steering actuator's rate instead of snapping;
/// - a small deadzone and a softened centre, for fine corrections on the centreline.
/// It sends only while the tiller is out of centre or the nosewheel is still returning; centred
/// and settled it goes quiet, so the rudder pedals' own nosewheel steering is never fought.
/// </summary>
public sealed class Steering
{
    public const double Deadzone = 0.02;
    public const double Expo = 0.35;

    private double _position;
    private bool _wasActive;

    public double Position => _position;

    /// <param name="tiller">Tiller input, −1..1.</param>
    /// <param name="groundSpeedKnots">Ground speed.</param>
    /// <param name="p">The aircraft's ground profile.</param>
    /// <param name="dt">Seconds since the last step.</param>
    public SteerCommand Step(double tiller, double groundSpeedKnots, GroundProfile p, double dt)
    {
        dt = Numeric.Clamp(Numeric.Finite(dt), 0, 0.1);
        var authority = Authority(p, groundSpeedKnots);
        var target = Shape(tiller) * authority;
        var rate = p.TillerDegrees > 0 ? p.SteerRateDegPerSec / p.TillerDegrees : 2.0;
        _position = Numeric.Toward(_position, target, rate * dt);

        if (target == 0 && Math.Abs(_position) < 1e-4)
        {
            _position = 0;
            var last = _wasActive;
            _wasActive = false;
            return new SteerCommand(0, last, authority);
        }
        _wasActive = true;
        return new SteerCommand(_position, true, authority);
    }

    public void Reset()
    {
        _position = 0;
        _wasActive = false;
    }

    /// <summary>Share of full tiller authority at a ground speed.</summary>
    public static double Authority(GroundProfile p, double groundSpeedKnots) =>
        Numeric.Lerp(1, p.TillerMinimumAuthority, Numeric.Ramp(Math.Abs(Numeric.Finite(groundSpeedKnots)), p.TillerFullBelowKnots, p.TillerFadeEndKnots));

    /// <summary>Tiller travel through the deadzone and a softened centre (expo), keeping full travel at the ends.</summary>
    public static double Shape(double tiller)
    {
        var x = Numeric.Clamp(Numeric.Finite(tiller), -1, 1);
        var a = Math.Abs(x);
        if (a <= Deadzone)
            return 0;
        a = (a - Deadzone) / (1 - Deadzone);
        return Math.Sign(x) * ((1 - Expo) * a + Expo * a * a * a);
    }

    /// <summary>A steering axis event value (±16384) as tiller travel −1..1.</summary>
    public static double TillerFromAxis(int value) => Numeric.Clamp(value / 16384.0, -1, 1);

    /// <summary>A steering position −1..1 as a STEERING_SET value (±16383).</summary>
    public static int AxisFromPosition(double position) => (int)Math.Round(Numeric.Clamp(position, -1, 1) * 16383);
}
