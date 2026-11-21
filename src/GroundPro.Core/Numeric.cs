namespace GroundPro.Core;

public static class Numeric
{
    public const double Gravity = 32.174;            // ft/s²
    public const double FeetPerSecondPerKnot = 1.68781;

    public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

    /// <summary>0 at or below <paramref name="from"/>, 1 at or above <paramref name="to"/>, linear between.</summary>
    public static double Ramp(double value, double from, double to) =>
        to <= from ? (value >= to ? 1 : 0) : Clamp((value - from) / (to - from), 0, 1);

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>A finite number, or <paramref name="fallback"/> for NaN and infinities.</summary>
    public static double Finite(double value, double fallback = 0) => double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;

    /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> by at most <paramref name="step"/>.</summary>
    public static double Toward(double current, double target, double step) =>
        Math.Abs(target - current) <= step ? target : current + Math.Sign(target - current) * step;
}
