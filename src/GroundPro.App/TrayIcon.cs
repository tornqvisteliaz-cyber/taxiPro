using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GroundPro.App;

/// <summary>
/// GroundPro in the background, as HeavyPro does it: the tray icon (Open / Exit, its tooltip shows
/// what GroundPro is doing), the signal a second launch sends to bring the window back, and the
/// "Start with Windows" entry.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    public const string BackgroundArg = "--background";
    private const string ShowSignalName = @"Local\Rotaidem.GroundPro.ShowWindow";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "GroundPro";

    private readonly NotifyIcon _icon;
    private readonly EventWaitHandle _showSignal;
    private readonly Thread _listener;
    private volatile bool _disposed;
    private bool _toldOnce;
    private string _tip = "";

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    /// <param name="ui">The window, so the listener can hand the show request to the UI thread.</param>
    public TrayIcon(Control ui)
    {
        _icon = new NotifyIcon { Text = "GroundPro", Visible = true };
        try { _icon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { _icon.Icon = SystemIcons.Application; }
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open GroundPro", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Exit GroundPro", null, (_, _) => ExitRequested?.Invoke());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();

        // A second launch sets this instead of starting another copy.
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _listener = new Thread(() =>
        {
            try
            {
                while (!_disposed)
                {
                    if (_showSignal.WaitOne(500) && !_disposed && ui.IsHandleCreated)
                        ui.BeginInvoke(new Action(() => OpenRequested?.Invoke()));
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }) { IsBackground = true, Name = "GroundPro show signal" };
        _listener.Start();
    }

    /// <summary>Tells the copy already running to show its window. False when none is listening.</summary>
    public static bool SignalRunningCopy()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ShowSignalName);
            return signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    /// <summary>The tooltip, e.g. "GroundPro · HOLDING". Windows allows 63 characters.</summary>
    public void SetStatus(string status)
    {
        var tip = "GroundPro · " + status;
        if (tip.Length > 63)
            tip = tip.Substring(0, 63);
        if (tip == _tip)
            return;
        _tip = tip;
        _icon.Text = tip;
    }

    /// <summary>Once per session, so closing the window never looks like quitting.</summary>
    public void TellStillRunning()
    {
        if (_toldOnce)
            return;
        _toldOnce = true;
        _icon.ShowBalloonTip(4000, "GroundPro is still running",
            "GroundPro keeps handling the ground in the background. Find it in the system tray; right-click it to exit.", ToolTipIcon.Info);
    }

    public static bool StartsWithWindows
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunValue) is string;
            }
            catch (Exception) { return false; }
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(RunValue, "\"" + Path.GetFullPath(Application.ExecutablePath) + "\" " + BackgroundArg);
            else if (key.GetValue(RunValue) != null)
                key.DeleteValue(RunValue);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _showSignal.Dispose();
    }
}
