namespace TaxiPro.Core.Models;

/// <summary>One sim frame of what TaxiPro reads. Values the sim did not provide are NaN (numbers) or false.</summary>
public sealed record GroundSnapshot
{
    public AircraftIdentity Aircraft { get; init; } = AircraftIdentity.None;
    public bool OnGround { get; init; }
    public bool ParkingBrakeSet { get; init; }
    public double GroundSpeedKnots { get; init; }
    /// <summary>VELOCITY BODY Z: forward speed along the fuselage, ft/s; negative when rolling backwards.</summary>
    public double VelocityBodyZ { get; init; }
    /// <summary>Yaw rate, degrees per second (ROTATION VELOCITY BODY Y).</summary>
    public double YawRateDegPerSec { get; init; }
    public double TotalWeightPounds { get; init; }
    public double MaxGrossWeightPounds { get; init; }
    public int EngineCount { get; init; }
    /// <summary>MSFS ENGINE TYPE: 0 piston, 1 jet, 2 none, 3 helicopter, 5 turboprop.</summary>
    public int EngineType { get; init; } = -1;
    /// <summary>Total jet thrust of all engines, lbf (sum of TURB ENG JET THRUST); NaN when the sim gives none.</summary>
    public double ThrustPounds { get; init; } = double.NaN;
    public double MaxN1Percent { get; init; }
    public double SimulationRate { get; init; } = 1;
    public int SurfaceType { get; init; } = Surface.Unknown;
    public bool PushbackActive { get; init; }
    public bool SlewActive { get; init; }
    /// <summary>The sim's brake positions, 0..1.</summary>
    public double BrakeLeft { get; init; }
    public double BrakeRight { get; init; }

    public const int EngineTypeJet = 1;

    public bool IsJet => EngineType == EngineTypeJet;
}
