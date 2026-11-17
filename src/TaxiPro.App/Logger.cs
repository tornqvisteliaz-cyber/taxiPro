using System.IO;

namespace TaxiPro.App;

/// <summary>Writes %AppData%\TaxiPro\logs\taxipro-yyyyMMdd.log and tells the window about each line.</summary>
public sealed class Logger
{
    public static Logger Shared { get; } = new();

    private readonly object _gate = new();

    public event Action<string>? Line;

    public string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaxiPro", "logs");

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {level} {message}";
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, $"taxipro-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Line?.Invoke(line);
    }
}
