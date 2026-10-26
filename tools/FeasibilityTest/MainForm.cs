#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TaxiPro.Feasibility;

/// <summary>The TaxiPro feasibility test window, in Rotaidem's colours.</summary>
public sealed class MainForm : Form
{
    private static readonly Color Bg = Color.FromArgb(0x11, 0x13, 0x15);
    private static readonly Color Surface = Color.FromArgb(0x16, 0x19, 0x1C);
    private static readonly Color Field = Color.FromArgb(0x1B, 0x1F, 0x22);
    private static readonly Color Ink = Color.FromArgb(0xEE, 0xF0, 0xF2);
    private static readonly Color Muted = Color.FromArgb(0x87, 0x8D, 0x94);
    private static readonly Color Accent = Color.FromArgb(0xAA, 0xB6, 0xC2);
    private static readonly Color Good = Color.FromArgb(0x4C, 0xC3, 0x8A);
    private static readonly Color Bad = Color.FromArgb(0xE5, 0x48, 0x4D);

    private readonly SimWorker _worker = new();
    private readonly Label _connection = new();
    private readonly Label _aircraft = new();
    private readonly Label _details = new();
    private readonly Button _run = new() { Text = "Run test" };
    private readonly Button _stop = new() { Text = "Stop", Enabled = false };
    private readonly Button _openLogs = new() { Text = "Open logs" };
    private readonly TextBox _log = new();
    private readonly TextBox _results = new();
    private SimStatus _status = new(false, "", 0, false, false, 0, 0, false);

    public MainForm()
    {
        Text = "TaxiPro feasibility test";
        ClientSize = new Size(760, 640);
        MinimumSize = new Size(640, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

        var logo = new PictureBox { Left = 24, Top = 20, Width = 48, Height = 48, SizeMode = PictureBoxSizeMode.Zoom, Image = LoadLogo() };
        var title = new Label { Text = "TaxiPro", Left = 84, Top = 18, AutoSize = true, Font = new Font("Segoe UI Semibold", 20f), ForeColor = Ink };
        var by = new Label { Text = "Feasibility test · by Rotaidem", Left = 87, Top = 54, AutoSize = true, ForeColor = Muted };

        _connection.SetBounds(24, 88, 712, 22);
        _connection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _aircraft.SetBounds(24, 112, 712, 24);
        _aircraft.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _aircraft.Font = new Font("Segoe UI Semibold", 11f);
        _details.SetBounds(24, 138, 712, 22);
        _details.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _details.ForeColor = Muted;

        var checklist = new Label
        {
            Text = "Before you run: aircraft lined up on a RUNWAY, engines running, parking brake released, hands off the throttles, HeavyPro closed. "
                 + "The test throttles up and taxis to about 14 kt several times (about two minutes).",
            Left = 24, Top = 168, Width = 712, Height = 40, ForeColor = Muted, Font = new Font("Segoe UI", 9f),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        Style(_run, primary: true);
        _run.SetBounds(24, 214, 160, 40);
        _run.Click += (_, _) => Run();
        Style(_stop, primary: false);
        _stop.SetBounds(192, 214, 110, 40);
        _stop.Click += (_, _) => { _worker.RequestStop(); _stop.Enabled = false; };
        Style(_openLogs, primary: false);
        _openLogs.SetBounds(310, 214, 120, 40);
        _openLogs.Click += (_, _) => OpenLogs();

        var resultsLabel = new Label { Text = "RESULTS", Left = 24, Top = 268, AutoSize = true, ForeColor = Accent, Font = new Font("Segoe UI Semibold", 9f) };
        SetupBox(_results);
        _results.SetBounds(24, 290, 712, 150);
        _results.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _results.Text = "Run the test to see what works in this aircraft.";

        var logLabel = new Label { Text = "LOG", Left = 24, Top = 452, AutoSize = true, ForeColor = Accent, Font = new Font("Segoe UI Semibold", 9f) };
        SetupBox(_log);
        _log.SetBounds(24, 474, 712, 146);
        _log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        Controls.AddRange(new Control[] { logo, title, by, _connection, _aircraft, _details, checklist, _run, _stop, _openLogs, resultsLabel, _results, logLabel, _log });

        _worker.StatusChanged += s => UI(() => ShowStatus(s));
        _worker.Log += line => UI(() => _log.AppendText(line + Environment.NewLine));
        _worker.Finished += lines => UI(() => ShowResults(lines));
        ShowStatus(_status);
        FormClosing += (_, e) =>
        {
            if (_status.Running && MessageBox.Show(this, "The test is running. Stop it and close?", "TaxiPro", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _worker.Dispose();
        };
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Dark title bar to match the window (Windows 10 2004+ and 11; ignored elsewhere).
        var dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }

    private void Run()
    {
        if (!_status.Connected)
        {
            MessageBox.Show(this, "MSFS 2024 is not connected yet. Start the sim and load the aircraft; TaxiPro connects by itself.", "TaxiPro");
            return;
        }
        var problem = !_status.OnGround ? "The aircraft is not on the ground."
            : _status.ParkingBrake ? "The parking brake is set. Release it first."
            : null;
        if (problem != null)
        {
            MessageBox.Show(this, problem, "TaxiPro");
            return;
        }
        var answer = MessageBox.Show(this,
            "Is the aircraft lined up on a RUNWAY with the engines running?\n\n"
            + "The test throttles up and taxis to about 14 kt several times. Keep your hands off the throttles; Stop brings it to a halt.",
            "Run the TaxiPro test?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;
        _log.Clear();
        _results.Text = "Running…";
        _worker.RequestRun();
    }

    private void ShowStatus(SimStatus s)
    {
        _status = s;
        _connection.Text = s.Connected ? "● Connected to MSFS 2024" : "● Waiting for MSFS 2024 — start the sim; TaxiPro connects by itself";
        _connection.ForeColor = s.Connected ? Good : Muted;
        _aircraft.Text = s.Connected ? (s.Title.Length > 0 ? s.Title : "Loading aircraft…") : "";
        _details.Text = s.Connected
            ? $"{s.WeightLb:N0} lb   ·   {(s.OnGround ? "on the ground" : "in the air")}   ·   parking brake {(s.ParkingBrake ? "SET" : "released")}   ·   {s.GroundSpeedKt:0.0} kt   ·   N1 {s.N1:0} %"
            : "";
        _run.Enabled = s.Connected && !s.Running;
        _stop.Enabled = s.Running;
        _run.Text = s.Running ? "Running…" : "Run test";
    }

    private void ShowResults(IReadOnlyList<string> lines)
    {
        _results.Text = string.Join(Environment.NewLine, lines);
    }

    private void OpenLogs()
    {
        Directory.CreateDirectory(_worker.LogFolder);
        Process.Start(new ProcessStartInfo(_worker.LogFolder) { UseShellExecute = true });
    }

    private void UI(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }

    private static void Style(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = primary ? 0 : 1;
        b.FlatAppearance.BorderColor = Color.FromArgb(0x26, 0x2A, 0x2E);
        b.BackColor = primary ? Accent : Surface;
        b.ForeColor = primary ? Color.FromArgb(0x0B, 0x0C, 0x0E) : Ink;
        b.Font = new Font("Segoe UI Semibold", 10f);
        b.Cursor = Cursors.Hand;
    }

    private static void SetupBox(TextBox box)
    {
        box.Multiline = true;
        box.ReadOnly = true;
        box.ScrollBars = ScrollBars.Vertical;
        box.BackColor = Field;
        box.ForeColor = Ink;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Consolas", 9.5f);
        box.WordWrap = false;
    }

    private static Image? LoadLogo()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("TaxiPro.Logo.png");
        if (stream == null) return null;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Image.FromStream(new MemoryStream(copy.ToArray()));
    }
}
