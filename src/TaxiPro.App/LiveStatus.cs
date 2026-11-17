using TaxiPro.Core.Physics;

namespace TaxiPro.App;

/// <summary>What the window shows, published by <see cref="SimLink"/> about ten times a second.</summary>
public sealed record LiveStatus
{
    public bool Connected { get; init; }
    public string Aircraft { get; init; } = "";
    public string Profile { get; init; } = "";
    public double WeightPounds { get; init; }
    public bool OnGround { get; init; }
    public double GroundSpeedKnots { get; init; }
    public double N1 { get; init; }
    public string Surface { get; init; } = "";

    public string Badge { get; init; } = "OFFLINE";
    public string SpeedDetail { get; init; } = "";
    public double ThrustPounds { get; init; }
    public double BreakawayPounds { get; init; }
    public double ExtraDecel { get; init; }
    public Acceptance SpeedAcceptance { get; init; }

    public double BrakeLeft { get; init; }
    public double BrakeRight { get; init; }
    public string BrakeNote { get; init; } = "";
    public BrakeRoute BrakeRoute { get; init; }
    public Acceptance BrakeAcceptance { get; init; }
    public bool BrakeInputSeen { get; init; }

    public double TillerInput { get; init; }
    public double NosewheelDegrees { get; init; }
    public double TillerDegrees { get; init; }
    public string TillerNote { get; init; } = "";
    public bool TillerInputSeen { get; init; }

    public bool HeavyProRunning { get; init; }
    public string HeavyProNote { get; init; } = "";

    public static LiveStatus Offline { get; } = new();
}
