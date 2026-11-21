using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using Rotaidem.Licensing;
using GroundPro.Licensing;

namespace GroundPro.Setup;

/// <summary>
/// GroundProSetup.exe: one file that activates the licence key, installs GroundPro into
/// %LOCALAPPDATA%\GroundPro (no administrator rights), adds Start menu and desktop shortcuts and an
/// entry in Windows' installed apps, and starts it. The same flow as HeavyProSetup.
///
///   GroundProSetup.exe               install or update
///   GroundProSetup.exe /uninstall    remove GroundPro (what Windows' "Uninstall" runs)
/// </summary>
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var uninstall = Array.FindIndex(args, a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        if (uninstall >= 0)
            return Uninstaller.Run(args.Skip(uninstall + 1).FirstOrDefault());

        using var setup = new SetupForm();
        Application.Run(setup);
        return setup.DialogResult == DialogResult.OK ? 0 : 1;
    }
}

static class Install
{
    public const string AppName = "GroundPro";
    public const string Exe = "GroundPro.exe";
    public const string SetupCopy = "GroundProSetup.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RotaidemGroundPro";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>%LOCALAPPDATA%\GroundPro, the same folder RotaHub installs GroundPro into.</summary>
    public static string AppDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string Version => typeof(Install).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static bool HasPayload => typeof(Install).Assembly.GetManifestResourceNames().Contains("GroundProPayload.zip");

    private static IEnumerable<string> Shortcuts() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", AppName + ".lnk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"),
    };

    /// <summary>Unpacks the app into <paramref name="destination"/>, refusing any path that would land outside it.</summary>
    public static void Extract(string destination)
    {
        using var stream = typeof(Install).Assembly.GetManifestResourceStream("GroundProPayload.zip")
            ?? throw new InvalidOperationException("This setup file has no app inside. Download the GroundPro installer again.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                continue;
            var file = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid app payload path.");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            entry.ExtractToFile(file, true);
        }
    }

    public static void CreateShortcuts(string exe)
    {
        foreach (var link in Shortcuts())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            var script = $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{Quote(link)}');$s.TargetPath='{Quote(exe)}';$s.WorkingDirectory='{Quote(Path.GetDirectoryName(exe)!)}';$s.Description='GroundPro ground handling by Rotaidem';$s.Save()";
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -NonInteractive -Command \"" + script + "\"",
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit(10000);
        }
    }

    private static string Quote(string path) => path.Replace("'", "''");

    /// <summary>The entry in Settings → Apps → Installed apps, whose Uninstall runs our copy of the setup.</summary>
    public static void Register(string appDir)
    {
        var setup = Path.Combine(appDir, SetupCopy);
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", "Rotaidem");
        key.SetValue("URLInfoAbout", "https://rotaidem.com");
        key.SetValue("DisplayIcon", Path.Combine(appDir, Exe));
        key.SetValue("InstallLocation", appDir);
        key.SetValue("UninstallString", $"\"{setup}\" /uninstall");
        key.SetValue("QuietUninstallString", $"\"{setup}\" /uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(DirectorySize(appDir) / 1024), RegistryValueKind.DWord);
    }

    /// <summary>Removes shortcuts, the Start with Windows entry, the installed-apps entry and the app folder. Settings and the licence stay.</summary>
    public static void Remove(string appDir)
    {
        foreach (var link in Shortcuts())
            TryDelete(link);
        try
        {
            using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
                if (run?.GetValue(AppName) != null)
                    run.DeleteValue(AppName);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        }
        catch (Exception) { }
        if (Directory.Exists(appDir))
            Directory.Delete(appDir, recursive: true);
    }

    /// <summary>Asks a running GroundPro to close (it may be hidden in the tray). False if the user said no or it would not close.</summary>
    public static bool CloseRunningGroundPro(IWin32Window? owner)
    {
        var processes = Process.GetProcessesByName(AppName);
        try
        {
            if (processes.Length == 0)
                return true;
            if (MessageBox.Show(owner, "GroundPro is running and must close first. Close it now?", "Close GroundPro?",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                return false;
            foreach (var p in processes)
            {
                // A window hidden in the tray ignores CloseMainWindow; it holds no unsaved work, so end it.
                if (!p.HasExited && !(p.CloseMainWindow() && p.WaitForExit(5000)))
                {
                    p.Kill();
                    if (!p.WaitForExit(5000))
                    {
                        MessageBox.Show(owner, "GroundPro could not be closed. Restart Windows, then run setup again.", "GroundPro setup",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
            }
            return true;
        }
        finally
        {
            foreach (var p in processes)
                p.Dispose();
        }
    }

    private static long DirectorySize(string dir)
    {
        try { return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch (Exception) { return 0; }
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); } catch (Exception) { }
    }
}

static class Uninstaller
{
    /// <param name="appDirArg">Set when running from the temporary copy (see below).</param>
    public static int Run(string? appDirArg)
    {
        var appDir = Install.AppDir;
        var self = Application.ExecutablePath;
        // Running from inside the folder we are about to delete: continue from a temporary copy.
        if (appDirArg == null && self.StartsWith(Path.GetFullPath(appDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            var temp = Path.Combine(Path.GetTempPath(), "GroundProUninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(self, temp);
            Process.Start(new ProcessStartInfo(temp, $"/uninstall \"{appDir}\"") { UseShellExecute = false });
            return 0;
        }

        if (MessageBox.Show("Remove GroundPro from this computer?\n\nYour settings are kept, so a reinstall picks up where you left off.",
                "Uninstall GroundPro", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return 1;
        if (!Install.CloseRunningGroundPro(null))
            return 1;

        if (GroundProLicense.Store().Load() is { } saved &&
            MessageBox.Show("Also free this computer's licence activation, so you can use GroundPro on another PC?",
                "Uninstall GroundPro", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            var result = Task.Run(() => GroundProLicense.DeactivateAsync(saved.Key)).GetAwaiter().GetResult();
            if (result.IsValid)
                GroundProLicense.Store().Clear();
            else
                MessageBox.Show("The activation could not be freed: " + result.Message + "\n\nGroundPro is removed anyway; you can free the activation on rotaidem.com.",
                    "Uninstall GroundPro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        try
        {
            Install.Remove(appDirArg ?? appDir);
            MessageBox.Show("GroundPro has been removed.", "Uninstall GroundPro", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("GroundPro could not be removed completely: " + ex.Message, "Uninstall GroundPro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }

        // Remove this temporary copy once it has exited.
        if (appDirArg != null)
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & del \"{self}\"") { CreateNoWindow = true, UseShellExecute = false });
        return 0;
    }
}

sealed class SetupForm : Form
{
    // Rotaidem brand colours, matching rotaidem.com, HeavyProSetup and the app.
    private static readonly Color Bg = Color.FromArgb(0x11, 0x13, 0x15);
    private static readonly Color Field = Color.FromArgb(0x1B, 0x1F, 0x22);
    private static readonly Color Ink = Color.FromArgb(0xEE, 0xF0, 0xF2);
    private static readonly Color Muted = Color.FromArgb(0x87, 0x8D, 0x94);
    private static readonly Color Accent = Color.FromArgb(0xAA, 0xB6, 0xC2);
    private static readonly Color Danger = Color.FromArgb(0xE5, 0x48, 0x4D);

    private readonly TextBox _key = new() { CharacterCasing = CharacterCasing.Upper };
    private readonly Button _install = new() { Text = "Install GroundPro" };
    private readonly CheckBox _startWithWindows = new() { Text = "Start GroundPro with Windows (in the tray)", Checked = true };
    private readonly Label _status = new();
    private readonly bool _activated;

    public SetupForm()
    {
        Text = "Install GroundPro";
        ClientSize = new Size(560, 460);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

        var logo = new PictureBox { Left = 36, Top = 36, Width = 64, Height = 64, SizeMode = PictureBoxSizeMode.Zoom, Image = LoadLogo() };
        var title = new Label { Text = "GroundPro", Left = 116, Top = 36, AutoSize = true, Font = new Font("Segoe UI Semibold", 22f), ForeColor = Ink };
        var by = new Label { Text = $"{Install.Version} · by Rotaidem", Left = 120, Top = 78, AutoSize = true, ForeColor = Muted };
        var intro = new Label
        {
            Text = "Ground handling for Microsoft Flight Simulator 2024: breakaway thrust, weight you can feel, "
                 + "progressive brakes and real tiller limits. Runs next to HeavyPro.",
            Left = 36, Top = 128, Width = 488, Height = 46, ForeColor = Muted
        };

        var keyLabel = new Label { Text = "Licence key", Left = 36, Top = 192, AutoSize = true, ForeColor = Ink, Font = new Font("Segoe UI Semibold", 10f) };
        _key.SetBounds(36, 218, 488, 32);
        _key.BackColor = Field;
        _key.ForeColor = Ink;
        _key.BorderStyle = BorderStyle.FixedSingle;
        _key.Font = new Font("Consolas", 13f);
        var keyHint = new Label { Text = $"From your purchase email. It looks like {GroundProLicense.KeyExample}.", Left = 36, Top = 258, AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9f) };

        _startWithWindows.SetBounds(36, 290, 488, 26);
        _startWithWindows.ForeColor = Muted;
        _startWithWindows.Font = new Font("Segoe UI", 9.5f);

        _install.SetBounds(36, 330, 488, 46);
        _install.FlatStyle = FlatStyle.Flat;
        _install.FlatAppearance.BorderSize = 0;
        _install.BackColor = Accent;
        _install.ForeColor = Color.FromArgb(0x0B, 0x0C, 0x0E);
        _install.Font = new Font("Segoe UI Semibold", 11f);
        _install.Cursor = Cursors.Hand;
        _install.Click += async (_, _) => await ActivateThenInstall();
        AcceptButton = _install;

        _status.SetBounds(36, 388, 488, 56);
        _status.ForeColor = Muted;
        _status.Font = new Font("Segoe UI", 9f);
        _status.Text = "Installs into your user folder (no administrator rights) and adds Start menu and desktop shortcuts.";

        // Reinstalling or updating on a computer that is already activated should not ask again.
        var saved = GroundProLicense.Store().Load();
        if (saved != null)
        {
            _key.Text = saved.Key;
            _activated = GroundProLicense.CheckOffline().IsValid;
        }
        if (Directory.Exists(Install.AppDir) && File.Exists(Path.Combine(Install.AppDir, Install.Exe)))
        {
            _install.Text = "Update GroundPro";
            // An update keeps the choice made in the app.
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            _startWithWindows.Checked = run?.GetValue(Install.AppName) != null;
        }
        GroundProLicense.WakeServer();
        _key.HandleCreated += (_, _) => SendMessage(_key.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, GroundProLicense.KeyExample);
        Shown += (_, _) => { _key.SelectionStart = _key.Text.Length; _key.SelectionLength = 0; };
        Controls.AddRange(new Control[] { logo, title, by, intro, keyLabel, _key, keyHint, _startWithWindows, _install, _status });
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var dark = 1;
        try { DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch (Exception) { }
    }

    private static Image? LoadLogo()
    {
        using var stream = typeof(SetupForm).Assembly.GetManifestResourceStream("GroundPro.Setup.Logo.png");
        if (stream == null)
            return null;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Image.FromStream(new MemoryStream(copy.ToArray()));
    }

    private void ShowStatus(string text, bool problem = false)
    {
        _status.ForeColor = problem ? Danger : Muted;
        _status.Text = text;
    }

    private async Task ActivateThenInstall()
    {
        if (!GroundProLicense.IsWellFormed(_key.Text))
        {
            ShowStatus($"That licence key has a typo. It should look like {GroundProLicense.KeyExample}.", problem: true);
            return;
        }
        var saved = GroundProLicense.Store().Load();
        // Already activated here with this key (an update): no need to use the server.
        if (!(_activated && saved != null && LicenseKey.Normalize(_key.Text) == saved.Key))
        {
            var label = _install.Text;
            _install.Enabled = false;
            _install.Text = "Checking your key…";
            ShowStatus("Contacting the licence server. This can take up to a minute.");
            try
            {
                var result = await GroundProLicense.ActivateAsync(_key.Text);
                if (!result.IsValid)
                {
                    ShowStatus(result.Message, problem: true);
                    return;
                }
                GroundProLicense.Save(_key.Text, result);
            }
            finally
            {
                _install.Enabled = true;
                _install.Text = label;
            }
        }
        DoInstall();
    }

    private void DoInstall()
    {
        if (!Install.HasPayload)
        {
            ShowStatus("This setup file was built without the app. Download the GroundPro installer again.", problem: true);
            return;
        }
        if (!Install.CloseRunningGroundPro(this))
            return;

        var appDir = Install.AppDir;
        try
        {
            _install.Enabled = false;
            ShowStatus("Installing…");
            Directory.CreateDirectory(appDir);
            Install.Extract(appDir);
            // Our own copy, for the Uninstall entry in Windows' installed apps.
            var copy = Path.Combine(appDir, Install.SetupCopy);
            if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase))
                File.Copy(Application.ExecutablePath, copy, overwrite: true);
            var exe = Path.Combine(appDir, Install.Exe);
            Install.CreateShortcuts(exe);
            Install.Register(appDir);
            SetStartWithWindows(exe, _startWithWindows.Checked);

            MessageBox.Show(this,
                "GroundPro is installed and will open now.\n\nIn MSFS 2024, keep your toe brakes and brake key bound as usual; bind a tiller to Steering axis.",
                "GroundPro");
            DialogResult = DialogResult.OK;
            Close();
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = appDir });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            _install.Enabled = true;
            ShowStatus("GroundPro could not be installed. Close GroundPro and try again. " + ex.Message, problem: true);
        }
    }

    /// <summary>The same Run entry the app's "Start with Windows" checkbox writes.</summary>
    private static void SetStartWithWindows(string exe, bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (on)
            key.SetValue(Install.AppName, $"\"{exe}\" --background");
        else if (key.GetValue(Install.AppName) != null)
            key.DeleteValue(Install.AppName);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
}
