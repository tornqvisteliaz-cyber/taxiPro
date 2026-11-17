using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TaxiPro.Core;
using TaxiPro.Core.Models;
using TaxiPro.Core.Physics;
using TaxiPro.Core.Services;

namespace TaxiPro.App;

/// <summary>The TaxiPro window, in Rotaidem's colours: status, the four effects with live readouts, and the log.</summary>
public sealed class MainForm : Form
{
    private readonly SettingsStore _store = new(SettingsStore.DefaultPath);
    private readonly TaxiSettings _settings;
    private readonly SimLink _link;

    private readonly Label _badge = new();
    private readonly Label _connection = new();
    private readonly Label _aircraft = new();
    private readonly Label _details = new();
    private readonly CheckBox _master = new() { Text = "TaxiPro on" };

    private readonly CheckBox _breakaway = new() { Text = "Breakaway thrust" };
    private readonly Meter _thrust = new();
    private readonly Label _breakawayLive = new();

    private readonly CheckBox _weight = new() { Text = "Weight you can feel" };
    private readonly TrackBar _weightFeel = new() { Minimum = 0, Maximum = 100, TickFrequency = 25, SmallChange = 5, LargeChange = 25 };
    private readonly Label _weightValue = new();
    private readonly Label _weightLive = new();

    private readonly CheckBox _brakes = new() { Text = "Progressive brakes" };
    private readonly Meter _brakeL = new();
    private readonly Meter _brakeR = new();
    private readonly Label _brakesLive = new();

    private readonly CheckBox _tiller = new() { Text = "Real tiller limits" };
    private readonly Meter _tillerIn = new() { Centred = true };
    private readonly Meter _nosewheel = new() { Centred = true, Fill = Theme.Good };
    private readonly Label _tillerLive = new();

    private readonly CheckBox _yield = new() { Text = "Leave the toe brakes to HeavyPro while it shapes them" };
    private readonly Label _heavyPro = new();
    private readonly CheckBox _runInBackground = new() { Text = "Keep running in the tray when closed" };
    private readonly CheckBox _startWithWindows = new() { Text = "Start with Windows" };
    private readonly Label _licence = new();
    private readonly LinkLabel _deactivate = new() { Text = "Deactivate this PC" };
    private readonly TextBox _log = new();
    private readonly TrayIcon _tray;
    private readonly System.Windows.Forms.Timer _recheck = new() { Interval = (int)Licence.RecheckInterval.TotalMilliseconds };
    private bool _loading;
    private bool _allowShow;
    private bool _exiting;

    /// <param name="startHidden">Started with Windows (--background): stay in the tray until opened.</param>
    public MainForm(bool startHidden = false)
    {
        _allowShow = !startHidden;
        _settings = _store.Load();
        Text = "TaxiPro";
        ClientSize = new Size(780, 790);
        MinimumSize = new Size(800, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Ink;
        Font = new Font("Segoe UI", 10f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

        var logo = new PictureBox { Left = 24, Top = 18, Width = 48, Height = 48, SizeMode = PictureBoxSizeMode.Zoom, Image = LoadLogo() };
        var title = new Label { Text = "TaxiPro", Left = 84, Top = 16, AutoSize = true, Font = new Font("Segoe UI Semibold", 20f) };
        var by = new Label { Text = "Ground handling · by Rotaidem", Left = 87, Top = 52, AutoSize = true, ForeColor = Theme.Muted };
        _badge.SetBounds(556, 26, 200, 30);
        _badge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _badge.TextAlign = ContentAlignment.MiddleCenter;
        _badge.Font = new Font("Segoe UI Semibold", 10f);

        _connection.SetBounds(24, 80, 732, 22);
        _aircraft.SetBounds(24, 104, 732, 24);
        _aircraft.Font = new Font("Segoe UI Semibold", 11f);
        _details.SetBounds(24, 130, 732, 22);
        _details.ForeColor = Theme.Muted;
        foreach (var line in new[] { _connection, _aircraft, _details })
            line.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _master.SetBounds(24, 162, 200, 30);
        _master.Font = new Font("Segoe UI Semibold", 12f);
        var retry = Button("Retry checks", 470, 162, 140);
        retry.Click += (_, _) => _link?.RetryChecks();
        new ToolTip().SetToolTip(retry, "Forget what TaxiPro learnt about this aircraft and try every effect again.");
        var logs = Button("Open logs", 618, 162, 138);
        logs.Click += (_, _) => OpenFolder(Logger.Shared.Folder);
        retry.Anchor = logs.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        var cardBreak = Card(24, 204, _breakaway, "A parked jet stays put at idle. It needs a push of thrust to start rolling, then settles back.");
        _thrust.SetBounds(16, 84, 328, 8);
        _breakawayLive.SetBounds(16, 100, 328, 40);
        cardBreak.Controls.AddRange(new Control[] { _thrust, _breakawayLive });

        var cardWeight = Card(400, 204, _weight, "Speed builds and fades with thrust, weight and rolling resistance. Only ever takes speed away.");
        _weightFeel.SetBounds(10, 78, 270, 30);
        _weightFeel.BackColor = Theme.Surface;
        _weightFeel.TickStyle = TickStyle.None;
        _weightValue.SetBounds(284, 82, 60, 22);
        _weightValue.TextAlign = ContentAlignment.MiddleRight;
        _weightLive.SetBounds(16, 112, 328, 30);
        cardWeight.Controls.AddRange(new Control[] { _weightFeel, _weightValue, _weightLive });

        var cardBrakes = Card(24, 366, _brakes, "Toe brakes and brake keys build pressure smoothly, slower on heavier aircraft, and never stick.");
        var l = SmallLabel("L", 16, 80); var r = SmallLabel("R", 16, 96);
        _brakeL.SetBounds(32, 84, 312, 8);
        _brakeR.SetBounds(32, 100, 312, 8);
        _brakesLive.SetBounds(16, 114, 328, 30);
        cardBrakes.Controls.AddRange(new Control[] { l, r, _brakeL, _brakeR, _brakesLive });

        var cardTiller = Card(400, 366, _tiller, "Full nosewheel from the tiller only when slow; a few degrees at speed. Turns at the actuator's rate.");
        var tin = SmallLabel("Tiller", 16, 80); var nw = SmallLabel("Wheel", 16, 96);
        _tillerIn.SetBounds(64, 84, 280, 8);
        _nosewheel.SetBounds(64, 100, 280, 8);
        _tillerLive.SetBounds(16, 114, 328, 30);
        cardTiller.Controls.AddRange(new Control[] { tin, nw, _tillerIn, _nosewheel, _tillerLive });

        _yield.SetBounds(24, 530, 460, 26);
        _yield.ForeColor = Theme.Muted;
        _yield.Font = new Font("Segoe UI", 9f);
        _heavyPro.SetBounds(490, 532, 266, 22);
        _heavyPro.TextAlign = ContentAlignment.MiddleRight;
        _heavyPro.Font = new Font("Segoe UI", 9f);
        _heavyPro.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        foreach (var c in new[] { _runInBackground, _startWithWindows })
        {
            c.ForeColor = Theme.Muted;
            c.Font = new Font("Segoe UI", 9f);
        }
        _runInBackground.SetBounds(24, 556, 270, 26);
        _startWithWindows.SetBounds(300, 556, 170, 26);
        _licence.SetBounds(470, 558, 160, 22);
        _licence.TextAlign = ContentAlignment.MiddleRight;
        _licence.Font = new Font("Segoe UI", 9f);
        _licence.ForeColor = Theme.Muted;
        _deactivate.SetBounds(634, 558, 122, 22);
        _deactivate.TextAlign = ContentAlignment.MiddleRight;
        _deactivate.Font = new Font("Segoe UI", 9f);
        _deactivate.LinkColor = _deactivate.ActiveLinkColor = Theme.Accent;
        _deactivate.LinkClicked += async (_, _) => await DeactivateAsync();
        new ToolTip().SetToolTip(_deactivate, "Free this computer's activation, to move TaxiPro to another PC.");
        foreach (var c in new Control[] { _licence, _deactivate })
            c.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        var logLabel = new Label { Text = "LOG", Left = 24, Top = 596, AutoSize = true, ForeColor = Theme.Accent, Font = new Font("Segoe UI Semibold", 9f) };
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Theme.Field;
        _log.ForeColor = Theme.Ink;
        _log.BorderStyle = BorderStyle.FixedSingle;
        _log.Font = new Font("Consolas", 9f);
        _log.WordWrap = false;
        _log.SetBounds(24, 618, 732, 152);
        _log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        Controls.AddRange(new Control[] { logo, title, by, _badge, _connection, _aircraft, _details, _master, retry, logs,
            cardBreak, cardWeight, cardBrakes, cardTiller, _yield, _heavyPro, _runInBackground, _startWithWindows, _licence, _deactivate, logLabel, _log });

        _loading = true;
        _master.Checked = _settings.MasterEnable;
        _breakaway.Checked = _settings.Breakaway;
        _weight.Checked = _settings.Weight;
        _weightFeel.Value = (int)Math.Round(_settings.WeightFeel);
        _brakes.Checked = _settings.Brakes;
        _tiller.Checked = _settings.Tiller;
        _yield.Checked = _settings.YieldBrakesToHeavyPro;
        _runInBackground.Checked = _settings.RunInBackground;
        _startWithWindows.Checked = TrayIcon.StartsWithWindows;
        _loading = false;
        _startWithWindows.CheckedChanged += (_, _) => SetStartWithWindows();
        foreach (var c in new[] { _master, _breakaway, _weight, _brakes, _tiller, _yield, _runInBackground })
            c.CheckedChanged += (_, _) => SettingsChanged();
        _weightFeel.ValueChanged += (_, _) => SettingsChanged();
        UpdateEnabled();

        Logger.Shared.Line += line => UI(() => AppendLog(line));
        Logger.Shared.Info($"TaxiPro {typeof(MainForm).Assembly.GetName().Version?.ToString(3)} started. Settings: {_store.FilePath}");
        _link = new SimLink(_settings, Logger.Shared);
        _link.Status += s => UI(() => ShowStatus(s));

        // Closing the window keeps TaxiPro running in the tray; Exit there quits.
        _tray = new TrayIcon(this);
        _tray.OpenRequested += ShowFromTray;
        _tray.ExitRequested += ExitApp;
        FormClosing += OnClosing;

        _recheck.Tick += async (_, _) => await RecheckLicenceAsync();
        _recheck.Start();
        ShowLicence();
        ShowStatus(LiveStatus.Offline);
        if (startHidden && !IsHandleCreated)
            CreateHandle(); // hidden in the tray, but status updates still need the window handle
    }

    // ---------------- Tray ----------------
    protected override void SetVisibleCore(bool value)
    {
        // Started with Windows: stay hidden until opened from the tray.
        base.SetVisibleCore(_allowShow && value);
    }

    private void ShowFromTray()
    {
        _allowShow = true;
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing && _settings.RunInBackground)
        {
            e.Cancel = true;
            Hide();
            _tray.TellStillRunning();
            return;
        }
        _recheck.Stop();
        _link.Dispose();
        _tray.Dispose();
    }

    private void SetStartWithWindows()
    {
        if (_loading)
            return;
        try
        {
            TrayIcon.StartsWithWindows = _startWithWindows.Checked;
            Logger.Shared.Info(_startWithWindows.Checked ? "TaxiPro starts with Windows (in the tray)." : "TaxiPro no longer starts with Windows.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Logger.Shared.Warn("Could not change Start with Windows: " + ex.Message);
        }
    }

    // ---------------- Licence ----------------
    private void ShowLicence()
    {
        _licence.Text = LicenseGate.Unlocked ? "Licence " + Licence.MaskedKey() : "Not licensed";
        _licence.ForeColor = LicenseGate.Unlocked ? Theme.Muted : Theme.Bad;
    }

    private async Task RecheckLicenceAsync()
    {
        var (ok, warning, message) = await Licence.RecheckAsync();
        ShowLicence();
        if (warning != null)
            MessageBox.Show(this, warning, "TaxiPro", MessageBoxButtons.OK, MessageBoxIcon.Information);
        if (ok)
            return;
        // Revoked, removed from this PC, not signed by the real server, or the lease ran out:
        // TaxiPro is already locked (everything passes through); close it.
        ShowFromTray();
        MessageBox.Show(this, message + "\n\nTaxiPro will close. Start it again to re-check or enter a licence key.", "TaxiPro",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        ExitApp();
    }

    private async Task DeactivateAsync()
    {
        if (MessageBox.Show(this, "Free this computer's TaxiPro activation, so you can activate TaxiPro on another PC?\n\nTaxiPro stops working here and closes.",
                "Deactivate TaxiPro", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _deactivate.Enabled = false;
        var (ok, message) = await Licence.DeactivateAsync();
        _deactivate.Enabled = true;
        MessageBox.Show(this, ok ? "TaxiPro is deactivated on this computer and will close." : message, "TaxiPro",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (ok)
            ExitApp();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var dark = 1; // dark title bar (Windows 10 2004+ and 11)
        try { DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch (Exception) { }
    }

    // ---------------- Settings ----------------
    private void SettingsChanged()
    {
        if (_loading)
            return;
        _settings.MasterEnable = _master.Checked;
        _settings.Breakaway = _breakaway.Checked;
        _settings.Weight = _weight.Checked;
        _settings.WeightFeel = _weightFeel.Value;
        _settings.Brakes = _brakes.Checked;
        _settings.Tiller = _tiller.Checked;
        _settings.YieldBrakesToHeavyPro = _yield.Checked;
        _settings.RunInBackground = _runInBackground.Checked;
        UpdateEnabled();
        _link.UpdateSettings(_settings);
        try { _store.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Logger.Shared.Warn("Could not save settings: " + ex.Message); }
    }

    private void UpdateEnabled()
    {
        foreach (var c in new Control[] { _breakaway, _weight, _brakes, _tiller, _yield })
            c.Enabled = _master.Checked;
        _weightFeel.Enabled = _master.Checked && _weight.Checked;
        _weightValue.Text = _weightFeel.Value + " %";
    }

    // ---------------- Live status ----------------
    private void ShowStatus(LiveStatus s)
    {
        _badge.Text = s.Badge;
        _tray.SetStatus(s.Connected ? s.Badge : "waiting for MSFS 2024");
        (_badge.BackColor, _badge.ForeColor) = s.Badge switch
        {
            "OFFLINE" or "WAITING" or "OFF" => (Theme.Field, Theme.Muted),
            "NOT LICENSED" => (Theme.Bad, Theme.Ink),
            "HOLDING" or "BRAKING" => (Theme.Warn, Theme.OnAccent),
            "TAXIING" => (Theme.Good, Theme.OnAccent),
            _ => (Theme.Accent, Theme.OnAccent)
        };
        _connection.Text = s.Connected ? "● Connected to MSFS 2024" : "● Waiting for MSFS 2024 — start the sim; TaxiPro connects by itself";
        _connection.ForeColor = s.Connected ? Theme.Good : Theme.Muted;
        _aircraft.Text = s.Connected ? (s.Aircraft.Length > 0 ? s.Aircraft : "Loading aircraft…") : "";
        _details.Text = s.Connected && s.Profile.Length > 0
            ? $"{s.Profile} profile   ·   {s.WeightPounds:N0} lb   ·   {(s.OnGround ? s.Surface : "in the air")}   ·   {s.GroundSpeedKnots:0.0} kt   ·   N1 {s.N1:0} %"
            : "";

        // Breakaway: thrust against what it takes to start rolling (marker in the middle of the bar).
        var share = s.BreakawayPounds > 0 ? s.ThrustPounds / s.BreakawayPounds : 0;
        _thrust.Value = share / 2;
        _thrust.Marker = s.BreakawayPounds > 0 ? 0.5 : -1;
        _thrust.Fill = share >= 1 ? Theme.Good : Theme.Accent;
        _breakawayLive.Text = s.BreakawayPounds > 0
            ? $"Thrust {s.ThrustPounds:N0} lbf · breakaway {s.BreakawayPounds:N0} lbf\n{s.SpeedDetail}"
            : s.SpeedDetail;
        _breakawayLive.ForeColor = Theme.Muted;

        _weightLive.Text = s.SpeedAcceptance switch
        {
            Acceptance.Ignored => "This aircraft overrides speed writes: left to the sim.",
            _ => $"Extra drag {s.ExtraDecel * 0.5925:0.00} kt/s · speed writes: {(s.SpeedAcceptance == Acceptance.Works ? "taken" : "not checked yet")}"
        };
        _weightLive.ForeColor = s.SpeedAcceptance == Acceptance.Ignored ? Theme.Warn : Theme.Muted;

        _brakeL.Value = s.BrakeLeft;
        _brakeR.Value = s.BrakeRight;
        _brakesLive.Text = s.Connected && !s.BrakeInputSeen && s.BrakeNote == "Progressive brakes"
            ? "Progressive brakes · press the toe brakes or the brake key once"
            : s.BrakeNote;
        _brakesLive.ForeColor = s.BrakeRoute == BrakeRoute.None ? Theme.Warn : Theme.Muted;

        _tillerIn.Value = s.TillerInput;
        _nosewheel.Value = s.TillerDegrees > 0 ? s.NosewheelDegrees / s.TillerDegrees : 0;
        _tillerLive.Text = s.Connected && !s.TillerInputSeen && s.TillerNote.StartsWith("Full tiller", StringComparison.Ordinal)
            ? s.TillerNote + " · no tiller axis seen yet (bind one to Steering axis in MSFS)"
            : s.Connected ? $"{s.TillerNote} · nosewheel {s.NosewheelDegrees:0}°" : s.TillerNote;
        _tillerLive.ForeColor = Theme.Muted;

        _heavyPro.Text = s.HeavyProRunning ? "● HeavyPro running" : "HeavyPro not running";
        _heavyPro.ForeColor = s.HeavyProRunning ? Theme.Good : Theme.Muted;
    }

    // ---------------- Helpers ----------------
    private void AppendLog(string line)
    {
        if (_log.TextLength > 200_000)
            _log.Text = _log.Text.Substring(_log.TextLength - 100_000);
        _log.AppendText(line + Environment.NewLine);
    }

    private void UI(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }

    private static Panel Card(int x, int y, CheckBox check, string description)
    {
        var card = new Panel { Left = x, Top = y, Width = 356, Height = 150, BackColor = Theme.Surface };
        check.SetBounds(14, 10, 330, 26);
        check.Font = new Font("Segoe UI Semibold", 10.5f);
        check.ForeColor = Theme.Ink;
        var desc = new Label { Text = description, Left = 16, Top = 38, Width = 328, Height = 38, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.75f) };
        card.Controls.AddRange(new Control[] { check, desc });
        return card;
    }

    private static Label SmallLabel(string text, int x, int y) =>
        new() { Text = text, Left = x, Top = y, AutoSize = true, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8f) };

    private static Button Button(string text, int x, int y, int width)
    {
        var b = new Button { Text = text, Left = x, Top = y, Width = width, Height = 32 };
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Theme.Border;
        b.BackColor = Theme.Surface;
        b.ForeColor = Theme.Ink;
        b.Font = new Font("Segoe UI Semibold", 9.5f);
        b.Cursor = Cursors.Hand;
        return b;
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { }
    }

    internal static Image? LoadLogo()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("TaxiPro.Logo.png");
        if (stream == null) return null;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Image.FromStream(new MemoryStream(copy.ToArray()));
    }
}
