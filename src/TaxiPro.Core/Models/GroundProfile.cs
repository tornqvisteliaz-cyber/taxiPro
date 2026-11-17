namespace TaxiPro.Core.Models;

/// <summary>
/// How one aircraft behaves on the ground.
/// </summary>
/// <param name="Name">What the app shows, e.g. "PMDG 777".</param>
/// <param name="Class">Size class.</param>
/// <param name="RollingCoefficient">Rolling resistance as a share of weight once moving (dry concrete). Airliner tyres: about 0.012–0.02.</param>
/// <param name="BreakawayCoefficient">Thrust as a share of weight needed to start a parked aircraft rolling (tyre flat spots, static friction).</param>
/// <param name="RatedThrustPerEngine">Takeoff thrust per engine, lbf. Only used when the sim reports no jet thrust.</param>
/// <param name="BreakawayFailsafeN1">Above this N1 the breakaway hold always lets go, whatever the thrust reading says.</param>
/// <param name="BrakeRiseSeconds">Seconds for brake pressure to go from none to full at a typical load.</param>
/// <param name="TillerDegrees">Nosewheel angle at full tiller.</param>
/// <param name="PedalSteerDegrees">Nosewheel angle the rudder pedals give (the authority the tiller fades to at speed on Boeings).</param>
/// <param name="TillerFullBelowKnots">Full tiller authority at and below this ground speed.</param>
/// <param name="TillerFadeEndKnots">At and above this ground speed the tiller has only its minimum authority.</param>
/// <param name="TillerFadesToZero">Airbus: the tiller fades out completely (to 0 at TillerFadeEndKnots). Boeing: it fades to pedal authority.</param>
/// <param name="SteerRateDegPerSec">How fast the hydraulic steering actuator turns the nosewheel.</param>
public sealed record GroundProfile(
    string Name,
    AircraftClass Class,
    double RollingCoefficient,
    double BreakawayCoefficient,
    double RatedThrustPerEngine,
    double BreakawayFailsafeN1,
    double BrakeRiseSeconds,
    double TillerDegrees,
    double PedalSteerDegrees,
    double TillerFullBelowKnots,
    double TillerFadeEndKnots,
    bool TillerFadesToZero,
    double SteerRateDegPerSec)
{
    /// <summary>The share of full tiller authority left at and above TillerFadeEndKnots.</summary>
    public double TillerMinimumAuthority => TillerFadesToZero || TillerDegrees <= 0 ? 0 : Numeric.Clamp(PedalSteerDegrees / TillerDegrees, 0, 1);

    /// <summary>Whether TaxiPro's breakaway and weight model applies (jets from narrow-body up).</summary>
    public bool HasMomentumModel => Class is AircraftClass.NarrowBody or AircraftClass.WideBody or AircraftClass.HeavyWide;
}
