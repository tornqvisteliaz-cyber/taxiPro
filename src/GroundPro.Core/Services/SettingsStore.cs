using System.Text.Json;
using GroundPro.Core.Models;

namespace GroundPro.Core.Services;

/// <summary>Loads and saves <see cref="TaxiSettings"/> as JSON. A missing or broken file gives the defaults.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public SettingsStore(string filePath) => FilePath = filePath;

    public string FilePath { get; }

    /// <summary>%AppData%\GroundPro\settings.json.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GroundPro", "settings.json");

    public TaxiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return (JsonSerializer.Deserialize<TaxiSettings>(File.ReadAllText(FilePath), Options) ?? new TaxiSettings()).Normalised();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
        }
        return new TaxiSettings();
    }

    public void Save(TaxiSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalised(), Options));
        if (File.Exists(FilePath))
            File.Delete(FilePath);
        File.Move(temp, FilePath);
    }
}
