namespace GroundPro.Core.Models;

/// <summary>Size class of an aircraft. The same classes and order as HeavyPro, so both apps agree.</summary>
public enum AircraftClass
{
    Unknown = 0,
    LightGa = 1,
    Regional = 2,
    NarrowBody = 3,
    WideBody = 4,
    HeavyWide = 5
}

/// <summary>
/// Sorts an aircraft into a class from its name, or from its maximum weight when the name says
/// nothing. The name lists follow HeavyPro's AircraftCatalog.
/// </summary>
public static class AircraftCatalog
{
    public static AircraftClass Classify(AircraftIdentity id, double maxGrossPounds)
    {
        var blob = id.Blob;

        if (ContainsAny(blob, "747", "B744", "B748", "A380", "A388", "AN-225", "AN225", "BELUGA", "A400", "C-5", "C5 GALAXY", "C-17", "C17", "IL-76"))
            return AircraftClass.HeavyWide;

        if (ContainsAny(blob, "777", "B77W", "B77L", "B772", "B773", "B77F", "787", "B788", "B789", "B78X",
                "A330", "A332", "A333", "A339", "A340", "A346", "A350", "A359", "A35K", "A300", "A306", "A310",
                "MD-11", "MD11", "DC-10", "IL-96", "707", "767", "B763", "B764", "L-1011", "L1011", "TRISTAR"))
            return AircraftClass.WideBody;

        if (ContainsAny(blob, "A318", "A319", "A320", "A321", "A32N", "A20N", "A21N",
                "737", "B738", "B739", "B38M", "B39M", "717", "757", "B752", "B753", "727", "MD-80", "MD-82", "MD82", "MD-90",
                "DC-9", "E170", "E175", "E190", "E195", "CRJ", "A220", "BCS1", "BCS3", "146", "RJ85", "RJ100",
                "SUPERJET", "SSJ", "TRANSALL", "C-160", "C160", "CONCORDE"))
            return AircraftClass.NarrowBody;

        if (ContainsAny(blob,
                "KING AIR", "B350", "CARAVAN", "C208", "SKYCOURIER", "C408", "TBM", "PC-12", "PC12",
                "CJ4", "CITATION", "LONGITUDE", "C680", "C700", "HONDAJET", "HA420", "SF50",
                "TWIN OTTER", "DHC-6", "ATR", "DASH", "Q400", "DH8", "KODIAK", "F406", "ISLANDER", "BN2"))
            return AircraftClass.Regional;

        if (ContainsAny(blob, "C152", "C172", "C182", "CESSNA 1", "PIPER", "PA-28", "PA28", "CUB", "BONANZA", "BARON",
                "DA40", "DA42", "DA62", "SR22", "CIRRUS", "PITTS", "EXTRA", "COMANCHE", "DR400", "CAP10", "GLIDER"))
            return AircraftClass.LightGa;

        if (maxGrossPounds > 500_000) return AircraftClass.HeavyWide;
        if (maxGrossPounds > 250_000) return AircraftClass.WideBody;
        if (maxGrossPounds > 80_000) return AircraftClass.NarrowBody;
        if (maxGrossPounds > 12_000) return AircraftClass.Regional;
        if (maxGrossPounds > 500) return AircraftClass.LightGa;
        return AircraftClass.Unknown;
    }

    public static string DisplayName(AircraftClass c) => c switch
    {
        AircraftClass.LightGa => "Light GA",
        AircraftClass.Regional => "Turboprop / bizjet",
        AircraftClass.NarrowBody => "Narrow-body jet",
        AircraftClass.WideBody => "Wide-body jet",
        AircraftClass.HeavyWide => "Heavy wide-body jet",
        _ => "Unknown"
    };

    internal static bool ContainsAny(string blob, params string[] tokens)
    {
        foreach (var t in tokens)
        {
            if (ContainsToken(blob, t))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Substring match where a number in the token cannot be part of a longer number:
    /// "C17" must not match "C172", but "737" still matches "B737-800".
    /// </summary>
    internal static bool ContainsToken(string blob, string token)
    {
        var digitStart = char.IsDigit(token[0]);
        var digitEnd = char.IsDigit(token[token.Length - 1]);
        for (var i = blob.IndexOf(token, StringComparison.Ordinal); i >= 0; i = blob.IndexOf(token, i + 1, StringComparison.Ordinal))
        {
            var end = i + token.Length;
            if (digitStart && i > 0 && char.IsDigit(blob[i - 1]))
                continue;
            if (digitEnd && end < blob.Length && char.IsDigit(blob[end]))
                continue;
            return true;
        }
        return false;
    }
}
