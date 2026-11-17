using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TaxiPro.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // One TaxiPro at a time: two would both write the brakes and the speed.
        using var single = new Mutex(true, @"Local\Rotaidem.TaxiPro", out var first);
        if (!first)
        {
            // The copy already running may be hidden in the tray: ask it to show itself.
            if (!TrayIcon.SignalRunningCopy())
                FocusRunningCopy();
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => Crash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Crash(ex); };

        var background = args.Any(a => string.Equals(a, TrayIcon.BackgroundArg, StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(background));
    }

    private static void Crash(Exception ex)
    {
        Logger.Shared.Error("Unhandled: " + ex);
        try
        {
            Directory.CreateDirectory(Logger.Shared.Folder);
            File.AppendAllText(Path.Combine(Logger.Shared.Folder, "crash.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { }
        MessageBox.Show(ex.Message, "TaxiPro", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static void FocusRunningCopy()
    {
        var self = Process.GetCurrentProcess();
        foreach (var other in Process.GetProcessesByName(self.ProcessName))
        {
            if (other.Id == self.Id || other.MainWindowHandle == IntPtr.Zero)
                continue;
            ShowWindow(other.MainWindowHandle, 9 /* SW_RESTORE */);
            SetForegroundWindow(other.MainWindowHandle);
            return;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
