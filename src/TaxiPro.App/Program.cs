using System.Threading;
using System.Windows.Forms;

namespace TaxiPro.App;

static class Program
{
    [STAThread]
    static void Main()
    {
        // One TaxiPro at a time: two would both write the brakes and the speed.
        using var single = new Mutex(true, @"Local\Rotaidem.TaxiPro", out var first);
        if (!first)
        {
            MessageBox.Show("TaxiPro is already running.", "TaxiPro", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => Logger.Shared.Error("Unhandled: " + e.Exception);
        Application.Run(new MainForm());
    }
}
