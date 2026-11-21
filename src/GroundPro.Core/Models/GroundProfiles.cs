namespace GroundPro.Core.Models;

/// <summary>
/// Ground handling data per aircraft type. Steering angles and tiller schedules are the published
/// figures for each type (Boeing: tiller and pedals keep their authority, pilots use the tiller only
/// slow; Airbus: tiller authority fades from 20 kt to zero). Rolling and breakaway resistance are
/// typical airliner tyre values; the "weight feel" slider scales how strictly they are applied.
/// </summary>
public static class GroundProfiles
{
    // Resistance by size: heavier aircraft sit on more, harder-worked tyres and need a bigger push.
    private const double NarrowRoll = 0.015, NarrowBreak = 0.025;
    private const double WideRoll = 0.016, WideBreak = 0.028;
    private const double HeavyRoll = 0.017, HeavyBreak = 0.030;

    private static GroundProfile Wide(string name, double thrust, double tiller = 70, double pedals = 7) =>
        new(name, AircraftClass.WideBody, WideRoll, WideBreak, thrust, 50, 0.9, tiller, pedals, 10, 30, false, 20);

    private static GroundProfile Heavy(string name, double thrust, double tiller = 70, double pedals = 7) =>
        new(name, AircraftClass.HeavyWide, HeavyRoll, HeavyBreak, thrust, 50, 1.0, tiller, pedals, 10, 30, false, 18);

    private static GroundProfile Narrow(string name, double thrust, double tiller, double pedals = 7) =>
        new(name, AircraftClass.NarrowBody, NarrowRoll, NarrowBreak, thrust, 50, 0.7, tiller, pedals, 10, 30, false, 25);

    private static GroundProfile Airbus(GroundProfile p, double fadeEnd) =>
        p with { TillerFullBelowKnots = 20, TillerFadeEndKnots = fadeEnd, TillerFadesToZero = true };

    // First match wins: specific types before the families they share codes with.
    private static readonly (string[] Tokens, Func<string, GroundProfile> Make)[] Types =
    {
        (new[] { "A380", "A388" }, _ => Airbus(Heavy("Airbus A380", 72_000, 70, 6), 70)),
        (new[] { "747", "B744", "B748" }, b => Heavy(Has(b, "PMDG") ? "PMDG 747" : "Boeing 747", 62_000)),
        (new[] { "A350", "A359", "A35K" }, b => Airbus(Wide(Has(b, "A35K", "A350-1000") ? "iniBuilds A350-1000" : "iniBuilds A350-900",
            Has(b, "A35K", "A350-1000") ? 97_000 : 84_000, 75, 6), 70)),
        (new[] { "777", "B77W", "B77L", "B772", "B773", "B77F" }, b => Wide(Has(b, "PMDG") ? "PMDG 777" : "Boeing 777", 110_000)),
        (new[] { "787", "B788", "B789", "B78X" }, _ => Wide("Boeing 787", 70_000)),
        (new[] { "767", "B763", "B764" }, _ => Wide("Boeing 767", 60_000, 65)),
        (new[] { "A330", "A332", "A333", "A339" }, _ => Airbus(Wide("Airbus A330", 70_000, 72, 6), 70)),
        (new[] { "A340", "A346" }, _ => Airbus(Wide("Airbus A340", 56_000, 72, 6), 70)),
        (new[] { "A300", "A306", "A310" }, _ => Wide("Airbus A300 / A310", 58_000, 65, 6)),
        (new[] { "MD-11", "MD11" }, _ => Wide("MD-11", 61_000)),
        (new[] { "L-1011", "L1011", "TRISTAR" }, _ => Wide("L-1011 TriStar", 48_000)),
        (new[] { "757", "B752", "B753" }, _ => Narrow("Boeing 757", 40_000, 65)),
        (new[] { "737", "B738", "B739", "B38M", "B39M" }, b => Narrow(Has(b, "PMDG") ? "PMDG 737" : "Boeing 737", 27_000, 78)),
        (new[] { "A321", "A21N" }, b => Airbus(Narrow(Has(b, "FENIX") ? "Fenix A321" : "Airbus A321", 33_000, 75, 6), 80)),
        (new[] { "A318", "A319", "A320", "A32N", "A20N" }, b => Airbus(Narrow(Has(b, "FENIX") ? "Fenix A320" : "Airbus A320", 27_000, 75, 6), 80)),
        (new[] { "A220", "BCS1", "BCS3" }, _ => Airbus(Narrow("Airbus A220", 23_000, 70, 7), 70)),
        (new[] { "MD-80", "MD-82", "MD82", "MD-90", "DC-9", "717" }, _ => Narrow("MD-80 / DC-9", 20_000, 82)),
        (new[] { "727" }, _ => Narrow("Boeing 727", 16_000, 78)),
        (new[] { "E170", "E175", "E190", "E195" }, _ => Narrow("Embraer E-Jet", 18_000, 76)),
        (new[] { "CRJ" }, _ => Narrow("Bombardier CRJ", 13_800, 80, 5)),
        (new[] { "146", "RJ85", "RJ100" }, _ => Narrow("BAe 146 / Avro RJ", 7_000, 70)),
    };

    /// <summary>The ground profile for an aircraft: a known type by name, otherwise one for its size class.</summary>
    public static GroundProfile For(AircraftIdentity id, double maxGrossPounds)
    {
        var blob = id.Blob;
        if (blob.Trim().Length > 0)
        {
            foreach (var (tokens, make) in Types)
            {
                if (AircraftCatalog.ContainsAny(blob, tokens))
                    return make(blob);
            }
        }
        return Generic(AircraftCatalog.Classify(id, maxGrossPounds));
    }

    public static GroundProfile Generic(AircraftClass c) => c switch
    {
        AircraftClass.HeavyWide => Heavy("Heavy wide-body jet", 60_000),
        AircraftClass.WideBody => Wide("Wide-body jet", 70_000),
        AircraftClass.NarrowBody => Narrow("Narrow-body jet", 25_000, 75),
        AircraftClass.Regional => new("Turboprop / bizjet", c, 0.02, 0.03, 7_000, 45, 0.5, 60, 10, 10, 30, false, 30),
        AircraftClass.LightGa => new("Light aircraft", c, 0.03, 0.04, 0, 100, 0.25, 0, 30, 10, 30, false, 40),
        _ => new("Unknown aircraft", AircraftClass.Unknown, 0.02, 0.03, 0, 100, 0.6, 60, 10, 10, 30, false, 25),
    };

    private static bool Has(string blob, params string[] any) => any.Any(blob.Contains);
}
