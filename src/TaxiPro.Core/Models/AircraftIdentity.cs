namespace TaxiPro.Core.Models;

/// <summary>What MSFS reports about the loaded aircraft (TITLE, ATC MODEL, ATC TYPE).</summary>
public sealed record AircraftIdentity(string Title = "", string AtcModel = "", string AtcType = "")
{
    public static AircraftIdentity None { get; } = new();

    /// <summary>Upper-case title + ATC model + ATC type, for name matching.</summary>
    public string Blob => $"{Title} {Readable(AtcModel)} {Readable(AtcType)}".ToUpperInvariant();

    public bool IsEmpty => string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(AtcModel) && string.IsNullOrWhiteSpace(AtcType);

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Title) ? Title.Trim()
        : !string.IsNullOrWhiteSpace(AtcModel) ? Readable(AtcModel)
        : "Unknown aircraft";

    /// <summary>
    /// MSFS sends ATC names as localisation keys ("ATCCOM.AC_MODEL B77W.0.text"); this is the part a
    /// pilot recognises ("B77W"). Plain values come back unchanged. (Same rule as HeavyPro.)
    /// </summary>
    public static string Readable(string? value)
    {
        var s = (value ?? "").Trim();
        if (s.StartsWith("TT:", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(3);
        if (s.StartsWith("ATCCOM.", StringComparison.OrdinalIgnoreCase) && s.IndexOf(' ') is > 0 and var space)
            s = s.Substring(space + 1);
        if (s.EndsWith(".0.text", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(0, s.Length - ".0.text".Length);
        return s.Trim();
    }
}
