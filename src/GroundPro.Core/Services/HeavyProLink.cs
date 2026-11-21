using System.Text.Json;

namespace GroundPro.Core.Services;

/// <summary>
/// GroundPro runs next to HeavyPro. HeavyPro can shape the toe brakes itself (it reads the pedals
/// from Windows and writes the brake positions); two apps writing the brakes would fight, so
/// GroundPro leaves them to HeavyPro while HeavyPro is running with its toe brakes set up and on.
/// HeavyPro's settings live in %AppData%\HeavyPro\settings.json.
/// </summary>
public static class HeavyProLink
{
    /// <summary>HeavyPro's process name (HeavyPro.exe).</summary>
    public const string ProcessName = "HeavyPro";

    public static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeavyPro", "settings.json");

    /// <summary>
    /// Whether HeavyPro is shaping the toe brakes: it is running, Master Enable and Toe brakes are
    /// on (both default on), and at least one brake pedal was set up with Detect.
    /// </summary>
    public static bool OwnsBrakes(bool heavyProRunning, string? settingsJson)
    {
        if (!heavyProRunning || string.IsNullOrWhiteSpace(settingsJson))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson!);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            return Bool(root, "MasterEnable", true) && Bool(root, "ToeBrakes", true)
                && (Text(root, "LeftBrakeAxis").Length > 0 || Text(root, "RightBrakeAxis").Length > 0);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool Bool(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
