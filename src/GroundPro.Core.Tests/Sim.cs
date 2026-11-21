using GroundPro.Core;
using GroundPro.Core.Models;
using GroundPro.Core.Physics;

namespace GroundPro.Core.Tests;

/// <summary>Test aircraft and a tiny longitudinal sim to run GroundPro against.</summary>
internal static class Sim
{
    public const double Dt = 1.0 / 30;

    public static readonly AircraftIdentity Pmdg777 = new("PMDG 777-300ER Emirates", "ATCCOM.AC_MODEL B77W.0.text", "BOEING");

    /// <summary>A PMDG 777-300ER at 550,000 lb, parked on concrete, engines at idle, parking brake off.</summary>
    public static GroundSnapshot Parked777(double thrust = 6_000, double n1 = 21) => new()
    {
        Aircraft = Pmdg777,
        OnGround = true,
        TotalWeightPounds = 550_000,
        MaxGrossWeightPounds = 775_000,
        EngineCount = 2,
        EngineType = GroundSnapshot.EngineTypeJet,
        ThrustPounds = thrust,
        MaxN1Percent = n1,
        SurfaceType = 0,
    };

    public static GroundSnapshot Rolling(this GroundSnapshot s, double fps) =>
        s with { VelocityBodyZ = fps, GroundSpeedKnots = Math.Abs(fps) / Numeric.FeetPerSecondPerKnot };

    public static GroundProfile Profile(GroundSnapshot s) => GroundProfiles.For(s.Aircraft, s.MaxGrossWeightPounds);

    /// <summary>
    /// Runs the aircraft for some seconds in a "sim" whose own ground physics are too slippery
    /// (it only knows <paramref name="simResistance"/> of resistance, as share of weight), with
    /// GroundPro writing speeds back. Returns the final forward speed in ft/s.
    /// </summary>
    public static double Run(GroundMomentum model, GroundSnapshot s, double seconds, MomentumOptions o, double simResistance = 0.002,
        Action<SpeedCommand>? each = null)
    {
        var p = Profile(s);
        var v = s.VelocityBodyZ;
        var mass = s.TotalWeightPounds / Numeric.Gravity;
        for (var t = 0.0; t < seconds; t += Dt)
        {
            var simAccel = (s.ThrustPounds - simResistance * s.TotalWeightPounds) / mass;
            v = Math.Max(0, v + simAccel * Dt);
            var cmd = model.Step(s.Rolling(v), p, o, Dt);
            each?.Invoke(cmd);
            if (cmd.Write)
                v = cmd.VelocityBodyZ;
        }
        return v;
    }

    public static readonly MomentumOptions Full = new(true, true, 1.0);

    /// <summary>An engine that counts as licensed, so tests do not depend on the static gate.</summary>
    public static TaxiEngine Engine() => new(() => true);
}
