using System.Windows.Forms;
using GrtAthlonImporter.Ipc;
using GrtAthlonImporter.Ui;

namespace GrtAthlonImporter;

internal static class Program
{
    /// <summary>
    /// GRT launches the plugin as:  GRT_Athlon_Importer.exe --ipcport &lt;port&gt;
    /// Run with no args to use the importer stand-alone (no GRT connection).
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        // Debug helper:  GRT_Athlon_Importer.exe --parse <file.xlsx>
        if (args.Length >= 2 && args[0] == "--parse")
        {
            DebugParse.Run(args[1], args.Length >= 3 ? args[2] : null);
            return;
        }

        // Headless integration test:  --selftest <ipcport> <xlsx> [more xlsx...]
        if (args.Length >= 3 && args[0] == "--selftest")
        {
            SelfTest.Run(int.Parse(args[1]), args.Skip(2).ToArray()).GetAwaiter().GetResult();
            return;
        }

        ApplicationConfiguration.Initialize();

        int port = 0;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--ipcport" && int.TryParse(args[i + 1], out int p))
                port = p;
        }

        GrtClient? grt = null;
        if (port > 0)
        {
            grt = new GrtClient(port);
            try { grt.Connect(); }
            catch { /* keep going; the UI still works stand-alone */ }
        }

        using var form = new MainForm(grt);

        // onDemand plugins are (re)started by GRT on menu/toolbar click; bring the
        // window forward if another click arrives while we are already running.
        if (grt != null)
        {
            grt.MenuOrToolbarActivated += () =>
            {
                if (form.IsHandleCreated)
                    form.BeginInvoke(() => form.BringToFrontForced());
            };
        }

        Application.Run(form);

        grt?.Dispose();
    }
}
