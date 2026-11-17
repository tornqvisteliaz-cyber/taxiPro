using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TaxiPro.Licensing;

namespace TaxiPro.App;

/// <summary>
/// "Activate TaxiPro": asks for a licence key when this computer has none (for example when the
/// installer ran as another Windows user) or the saved one stopped working. DialogResult is OK once
/// the key is activated and saved.
/// </summary>
public sealed class LicenseForm : Form
{
    private readonly TextBox _key = new() { CharacterCasing = CharacterCasing.Upper };
    private readonly Button _activate = new() { Text = "Activate" };
    private readonly Button _quit = new() { Text = "Quit" };
    private readonly Label _message = new();

    /// <summary>The lease the server granted (set when the dialog closes with OK).</summary>
    public DateTimeOffset? LeaseUntil { get; private set; }

    public LicenseForm(string? reason = null, string? key = null)
    {
        Text = "Activate TaxiPro";
        ClientSize = new Size(520, 330);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Ink;
        Font = new Font("Segoe UI", 10f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

        var logo = new PictureBox { Left = 32, Top = 28, Width = 48, Height = 48, SizeMode = PictureBoxSizeMode.Zoom, Image = MainForm.LoadLogo() };
        var title = new Label { Text = "Activate TaxiPro", Left = 92, Top = 30, AutoSize = true, Font = new Font("Segoe UI Semibold", 17f) };
        var intro = new Label
        {
            Text = "Enter the licence key from your purchase email. TaxiPro activates it on this computer once; after that it starts offline too.",
            Left = 32, Top = 92, Width = 456, Height = 42, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9.5f)
        };
        _key.SetBounds(32, 144, 456, 32);
        _key.BackColor = Theme.Field;
        _key.ForeColor = Theme.Ink;
        _key.BorderStyle = BorderStyle.FixedSingle;
        _key.Font = new Font("Consolas", 13f);
        _key.Text = key ?? "";
        _key.HandleCreated += (_, _) => SendMessage(_key.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, TaxiProLicense.KeyExample);

        _message.SetBounds(32, 186, 456, 56);
        _message.ForeColor = Theme.Bad;
        _message.Font = new Font("Segoe UI", 9f);
        _message.Text = reason ?? "";

        _activate.SetBounds(32, 258, 300, 42);
        _activate.FlatStyle = FlatStyle.Flat;
        _activate.FlatAppearance.BorderSize = 0;
        _activate.BackColor = Theme.Accent;
        _activate.ForeColor = Theme.OnAccent;
        _activate.Font = new Font("Segoe UI Semibold", 11f);
        _activate.Click += async (_, _) => await ActivateAsync();
        _quit.SetBounds(344, 258, 144, 42);
        _quit.FlatStyle = FlatStyle.Flat;
        _quit.FlatAppearance.BorderColor = Theme.Border;
        _quit.BackColor = Theme.Surface;
        _quit.ForeColor = Theme.Ink;
        _quit.DialogResult = DialogResult.Cancel;
        AcceptButton = _activate;
        CancelButton = _quit;

        Controls.AddRange(new Control[] { logo, title, intro, _key, _message, _activate, _quit });
        Shown += (_, _) => { _key.Focus(); _key.SelectionStart = _key.Text.Length; };
        TaxiProLicense.WakeServer();
    }

    private async Task ActivateAsync()
    {
        if (!TaxiProLicense.IsWellFormed(_key.Text))
        {
            Show($"That key has a typo. It should look like {TaxiProLicense.KeyExample}.", problem: true);
            return;
        }
        _activate.Enabled = false;
        _activate.Text = "Checking…";
        Show("Contacting the licence server. This can take up to a minute.", problem: false);
        try
        {
            var result = await TaxiProLicense.ActivateAsync(_key.Text);
            if (!result.IsValid)
            {
                Show(result.Message, problem: true);
                Logger.Shared.Warn($"Activation refused: {result.Status} — {result.Message}");
                return;
            }
            TaxiProLicense.Save(_key.Text, result);
            LeaseUntil = result.LeaseUntil;
            Logger.Shared.Info($"Licence activated on this computer ({result.ActivationsUsed} of {result.MaxActivations}).");
            DialogResult = DialogResult.OK;
            Close();
        }
        finally
        {
            if (!IsDisposed)
            {
                _activate.Enabled = true;
                _activate.Text = "Activate";
            }
        }
    }

    private void Show(string text, bool problem)
    {
        _message.ForeColor = problem ? Theme.Bad : Theme.Muted;
        _message.Text = text;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
}
