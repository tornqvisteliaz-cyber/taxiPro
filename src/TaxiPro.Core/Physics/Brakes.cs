using TaxiPro.Core.Models;

namespace TaxiPro.Core.Physics;

/// <summary>Brake pressure per side (0 = off, 1 = full) and whether TaxiPro should be writing it.</summary>
public readonly record struct BrakeOutput(double Left, double Right, bool Active)
{
    public static BrakeOutput Idle => new(0, 0, false);
}

/// <summary>The keyboard brake keys MSFS sends: BRAKES (both), BRAKES_LEFT, BRAKES_RIGHT.</summary>
public enum BrakeKey
{
    Both,
    Left,
    Right
}

/// <summary>
/// Progressive, weight-aware brakes. An airliner's brakes are hydraulic: pressure builds over a
/// moment after the pedal moves and the first part of the pedal does little, so a heavy jet slows
/// smoothly instead of the instant lock-up a toe-brake axis gives in the sim. Each side gets:
/// - a deadzone, so a resting foot or a worn pedal never drags the brakes (no "sticking" brakes);
/// - a progressive curve: gentle first half of the travel, full pressure at the end;
/// - pressure that builds over time, slower on heavier and more heavily loaded aircraft, and lets go faster than it builds.
/// The keyboard brake keys give a firm, progressive application while held instead of an on/off slam.
/// When both sides are released and the pressure has bled off it goes idle (one last zero is
/// written), so the parking brake and autobrake are never fought.
///
/// The pedal model is HeavyPro's ToeBrakes, so both apps brake the same way.
/// </summary>
public sealed class Brakes
{
    public const double Deadzone = 0.04;
    public const double Curve = 1.6;
    /// <summary>Pressure the keyboard brake keys build to while held.</summary>
    public const double KeyPressure = 0.6;
    /// <summary>MSFS repeats a held key after about half a second; a key counts as held this long after its last event.</summary>
    public const double KeyHoldSeconds = 0.6;

    private double _left, _right;
    private double _keyBoth, _keyLeft, _keyRight;
    private bool _wasActive;

    public void PressKey(BrakeKey key)
    {
        switch (key)
        {
            case BrakeKey.Left: _keyLeft = KeyHoldSeconds; break;
            case BrakeKey.Right: _keyRight = KeyHoldSeconds; break;
            default: _keyBoth = KeyHoldSeconds; break;
        }
    }

    /// <param name="leftPedal">Left toe brake travel, 0..1.</param>
    /// <param name="rightPedal">Right toe brake travel, 0..1.</param>
    /// <param name="riseSeconds">From <see cref="RiseSeconds"/>.</param>
    /// <param name="dt">Seconds since the last step.</param>
    public BrakeOutput Step(double leftPedal, double rightPedal, double riseSeconds, double dt)
    {
        dt = Numeric.Clamp(Numeric.Finite(dt), 0, 0.1);
        _keyBoth = Math.Max(0, _keyBoth - dt);
        _keyLeft = Math.Max(0, _keyLeft - dt);
        _keyRight = Math.Max(0, _keyRight - dt);

        var keyL = _keyBoth > 0 || _keyLeft > 0 ? KeyPressure : 0;
        var keyR = _keyBoth > 0 || _keyRight > 0 ? KeyPressure : 0;
        var rise = Math.Max(0.05, Numeric.Finite(riseSeconds, 0.6));
        var release = rise * 0.4;
        _left = Toward(_left, Math.Max(Target(leftPedal), keyL), dt, rise, release);
        _right = Toward(_right, Math.Max(Target(rightPedal), keyR), dt, rise, release);

        if (_left <= 0.0005 && _right <= 0.0005)
        {
            _left = _right = 0;
            var last = _wasActive;
            _wasActive = false;
            return new BrakeOutput(0, 0, last);
        }
        _wasActive = true;
        return new BrakeOutput(_left, _right, true);
    }

    public void Reset()
    {
        _left = _right = 0;
        _keyBoth = _keyLeft = _keyRight = 0;
        _wasActive = false;
    }

    /// <summary>Pedal travel (0..1) to the pressure it asks for: deadzone, then a progressive curve.</summary>
    public static double Target(double pedal)
    {
        pedal = Numeric.Clamp(Numeric.Finite(pedal), 0, 1);
        if (pedal <= Deadzone)
            return 0;
        return Math.Pow((pedal - Deadzone) / (1 - Deadzone), Curve);
    }

    /// <summary>Seconds for pressure to go from none to full: the type's figure, a little longer when loaded close to its maximum.</summary>
    public static double RiseSeconds(GroundProfile p, GroundSnapshot s)
    {
        var load = s.MaxGrossWeightPounds > 1 && s.TotalWeightPounds > 1
            ? Numeric.Clamp(s.TotalWeightPounds / s.MaxGrossWeightPounds, 0.3, 1.1)
            : 0.7;
        return p.BrakeRiseSeconds * (0.85 + 0.3 * load);
    }

    /// <summary>A brake axis event value (−16383 released … +16383 full) as pedal travel 0..1.</summary>
    public static double PedalFromAxis(int value) => Numeric.Clamp((value + 16383) / 32766.0, 0, 1);

    /// <summary>Pedal travel 0..1 as a brake axis event value.</summary>
    public static int AxisFromPedal(double pedal) => (int)Math.Round(-16383 + Numeric.Clamp(pedal, 0, 1) * 32766);

    private static double Toward(double current, double target, double dt, double rise, double release) =>
        Numeric.Toward(current, target, (target > current ? 1.0 / rise : 1.0 / release) * dt);
}
