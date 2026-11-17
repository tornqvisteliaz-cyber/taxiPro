using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TaxiPro.Core.Services;

/// <summary>Whether HeavyPro is shaping the toe brakes right now, and why (for the window and the log).</summary>
public sealed record HeavyProBrakes(bool Running, bool OwnsBrakes, string Why)
{
    public static HeavyProBrakes NotRunning { get; } = new(false, false, "HeavyPro not running");
}

/// <summary>
/// TaxiPro next to HeavyPro. The only thing both apps can shape is the toe brakes: HeavyPro reads
/// the pedals from Windows and writes BRAKE LEFT/RIGHT POSITION, TaxiPro shapes the brake inputs MSFS
/// sends. Two apps writing the brakes would fight, so TaxiPro makes exactly the decision HeavyPro
/// makes (HeavyPro's MainViewModel.StepBrakes and ScanMsfsBindings) and steps aside whenever HeavyPro
/// shapes them:
/// - HeavyPro is running, with Master Enable and Heavy toe brakes on, a rudder-pedal device picked and
///   at least one toe brake set up with Detect (all read from %AppData%\HeavyPro\settings.json);
/// - and those pedals' toe brakes are not still bound in an MSFS airplane control profile. If they are,
///   HeavyPro leaves the brakes to MSFS ("a toe brake still bound in MSFS would fight these writes"),
///   so TaxiPro shapes them. HeavyPro only checks this when the pedals are a different device from
///   the stick, and so does TaxiPro.
/// Everything else HeavyPro does (stick, rudder, its in-game panel) uses inputs TaxiPro never touches.
/// </summary>
public static class HeavyProLink
{
    /// <summary>HeavyPro's process name (HeavyPro.exe).</summary>
    public const string ProcessName = "HeavyPro";

    /// <summary>MSFS 2024's Steam app id; its control profiles are in Steam\userdata\*\2537590\remote\inputprofile_*.</summary>
    public const string MsfsSteamAppId = "2537590";

    public static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeavyPro", "settings.json");

    /// <param name="heavyProRunning">A HeavyPro.exe process exists.</param>
    /// <param name="settingsJson">HeavyPro's settings.json, or null.</param>
    /// <param name="msfsProfiles">The MSFS control profile files (XML), as HeavyPro reads them.</param>
    public static HeavyProBrakes Brakes(bool heavyProRunning, string? settingsJson, IEnumerable<string> msfsProfiles)
    {
        if (!heavyProRunning)
            return HeavyProBrakes.NotRunning;
        if (string.IsNullOrWhiteSpace(settingsJson))
            return new(true, false, "HeavyPro running, its toe brakes not set up");
        bool master, toeBrakes, setUp;
        string pedals, stick;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson!);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(true, false, "HeavyPro running, its settings unreadable");
            master = Bool(root, "MasterEnable", true);
            toeBrakes = Bool(root, "ToeBrakes", true);
            setUp = Text(root, "LeftBrakeAxis").Length > 0 || Text(root, "RightBrakeAxis").Length > 0;
            pedals = Text(root, "RudderDevice");
            stick = Text(root, "StickDevice");
        }
        catch (JsonException)
        {
            return new(true, false, "HeavyPro running, its settings unreadable");
        }

        if (!master) return new(true, false, "HeavyPro running with Master Enable off");
        if (!toeBrakes) return new(true, false, "HeavyPro running, Heavy toe brakes off");
        var pedalId = ProductIdFromKey(pedals);
        if (!setUp || pedalId is null) return new(true, false, "HeavyPro running, its toe brakes not set up");
        if (pedalId != ProductIdFromKey(stick) && BrakesBoundInMsfs(msfsProfiles, pedalId.Value))
            return new(true, false, "HeavyPro leaves the toe brakes to MSFS (still bound there)");
        return new(true, true, "HeavyPro is shaping the toe brakes");
    }

    /// <summary>The USB product id from a HeavyPro device key ("VID:PID" in hex), as HeavyPro reads it.</summary>
    public static int? ProductIdFromKey(string? key)
    {
        var parts = (key ?? "").Split(':');
        return parts.Length == 2 && int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var pid) ? pid : null;
    }

    /// <summary>
    /// Some MSFS airplane control profile for this device binds a toe-brake axis (HeavyPro's
    /// MsfsInputProfiles.FindBindings for "brakes"). A slot counts only if it is made of axes alone
    /// (MSFS key codes from 512); a button bound to the brake action is not an axis binding.
    /// </summary>
    public static bool BrakesBoundInMsfs(IEnumerable<string> profiles, int productId)
    {
        foreach (var xml in profiles)
        {
            var device = Device(xml);
            if (device == null || !int.TryParse((string?)device.Attribute("ProductID"), out var id) || id != productId)
                continue;
            var category = (string?)device.Element("AircraftInfo")?.Attribute("CategoryName") ?? "";
            if (!string.Equals(category, "AIRPLANE", StringComparison.OrdinalIgnoreCase))
                continue;
            var bound = device.Descendants("Action")
                .Where(a => (string?)a.Attribute("ActionName") is "KEY_AXIS_LEFT_BRAKE_SET" or "KEY_AXIS_RIGHT_BRAKE_SET")
                .Any(a => a.Elements().Where(s => s.Name == "Primary" || s.Name == "Secondary")
                    .Any(s => IsAxisSlot(s.Elements("KEY").Select(k => int.TryParse(k.Value.Trim(), out var c) ? c : -1))));
            if (bound)
                return true;
        }
        return false;
    }

    private const int FirstAxisCode = 512;

    private static bool IsAxisSlot(IEnumerable<int> codes)
    {
        var list = codes.ToList();
        return list.Count > 0 && list.All(c => c >= FirstAxisCode);
    }

    private static XElement? Device(string xml)
    {
        try
        {
            // The file has several top-level elements; give them one root.
            var body = Regex.Replace(xml, @"<\?xml[^>]*\?>", "");
            return XElement.Parse("<r>" + body + "</r>").Element("Device");
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool Bool(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
