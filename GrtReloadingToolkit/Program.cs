using System.Windows.Forms;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Log;
using GrtReloadingToolkit.Cal;
using GrtReloadingToolkit.TempCoeff;
using GrtReloadingToolkit.Ocw;
using GrtReloadingToolkit.Ui;

namespace GrtReloadingToolkit;

internal static class Program
{
    internal const string PluginId = "com.grt.plugin.reloadingtoolkit";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--dbtest") { DbSelfTest.Run(args); return; }
        if (args.Length >= 3 && args[0] == "--ladder") { LadderCli.Run(args[1], args[2], args.Length >= 4 ? args[3] : null); return; }
        if (args.Length >= 4 && args[0] == "--cal") { Cal.CalCli.RunAsync(args).GetAwaiter().GetResult(); return; }
        if (args.Length >= 4 && args[0] == "--tcoeff") { TempCoeffCli.Run(args); return; }
        if (args.Length >= 2 && args[0] == "--card") { Cards.CardCli.Run(args[1], args.Length >= 3 ? args[2] : null); return; }
        if (args.Length >= 2 && args[0] == "--brass") { Brass.BrassCli.Run(args); return; }
        if (args.Length >= 2 && args[0] == "--groups") { Ocw.GroupsCli.Run(args); return; }
        if (args.Length >= 1 && args[0] == "--reports")
        {
            string? root = args.Length >= 2 ? args[1] : Reports.ReportTemplates.InferGrtRoot();
            if (root == null) { Console.WriteLine("could not find GRT root (pass it as an argument)"); return; }
            foreach (var l in Reports.ReportTemplates.Install(root)) Console.WriteLine(l);
            return;
        }
        if (args.Length >= 3 && args[0] == "--athlon")
        {
            Athlon.AthlonCli.Run(args[1], args[2], args.Length >= 4 && double.TryParse(args[3], System.Globalization.CultureInfo.InvariantCulture, out double t) ? t : null);
            return;
        }

        ApplicationConfiguration.Initialize();

        int port = 0;
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--ipcport" && int.TryParse(args[i + 1], out int p)) port = p;

        GrtClient? grt = null;
        if (port > 0)
        {
            grt = new GrtClient(port);
            try { grt.Connect(); } catch { }
            // Give GRT a moment to deliver the toolbar/menu click that launched us (onDemand),
            // so we can open that tool straight away instead of the launcher.
            for (int i = 0; i < 25 && grt.PendingActivation is null; i++) Thread.Sleep(20);
        }

        Application.Run(new ToolkitContext(grt));
        grt?.Dispose();
    }
}

internal enum Tool { Launcher, Athlon, Ocw, Seating, Cal, Temp, Brass, Label, Log, Reports }

/// <summary>
/// One process, three tools. GRT (launch-type onDemand) starts us on the first
/// toolbar/menu click and sends the item id; each further click on another button
/// reaches the running process and opens that tool.
/// </summary>
internal sealed class ToolkitContext : ApplicationContext
{
    private readonly GrtClient? _grt;
    private readonly Db _db;
    private readonly Dictionary<Tool, Form> _open = new();
    private readonly WindowsFormsSynchronizationContext _ui = new();

    public ToolkitContext(GrtClient? grt)
    {
        _grt = grt;
        SynchronizationContext.SetSynchronizationContext(_ui);
        _db = new Db(Environment.GetEnvironmentVariable("RELOADING_LOG_DB") ?? Db.DefaultPath);

        // If GRT started us from a click on a *specific* tool, that tool opens itself (the id is
        // replayed synchronously by the subscribe below). A click on the plain toolbar/menu entry
        // resolves to Launcher, so we still want the launcher.
        bool launchedIntoTool = grt?.PendingActivation is { Length: > 0 } pa && FromId(pa) != Tool.Launcher;
        if (grt != null)
            grt.MenuOrToolbarActivated += id => _ui.Post(_ => Open(FromId(id)), null);

        if (!launchedIntoTool) Open(Tool.Launcher);
    }

    public static Tool FromId(string id) =>
        id.EndsWith(".athlon", StringComparison.Ordinal) ? Tool.Athlon :
        id.EndsWith(".ocw", StringComparison.Ordinal) ? Tool.Ocw :
        id.EndsWith(".seating", StringComparison.Ordinal) ? Tool.Seating :
        id.EndsWith(".calibration", StringComparison.Ordinal) ? Tool.Cal :
        id.EndsWith(".tempcoeff", StringComparison.Ordinal) ? Tool.Temp :
        id.EndsWith(".brass", StringComparison.Ordinal) ? Tool.Brass :
        id.EndsWith(".label", StringComparison.Ordinal) ? Tool.Label :
        id.EndsWith(".inventory", StringComparison.Ordinal) ? Tool.Log :
        id.EndsWith(".reports", StringComparison.Ordinal) ? Tool.Reports :
        Tool.Launcher;

    private void Open(Tool t)
    {
        if (t == Tool.Reports) { InstallReports(); return; }

        if (_open.TryGetValue(t, out var existing) && !existing.IsDisposed)
        {
            if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
            existing.Activate();
            existing.BringToFront();
            return;
        }

        Form f = t switch
        {
            Tool.Athlon => new AthlonForm(_grt),
            Tool.Ocw => new OcwForm(_grt),
            Tool.Seating => new SeatingForm(_grt),
            Tool.Cal => new CalibrationForm(_grt),
            Tool.Temp => new TempCoeffForm(_grt),
            Tool.Brass => new BrassForm(_grt),
            Tool.Label => new LabelForm(_grt, _db),
            Tool.Log => new LogForm(_grt, _db),
            _ => new LauncherForm(Open),
        };
        f.FormClosed += (_, _) =>
        {
            _open.Remove(t);
            if (_open.Count == 0) { _db.Dispose(); ExitThread(); }
        };
        _open[t] = f;
        f.Show();
        f.Activate();
    }

    private static void InstallReports()
    {
        string? root = Reports.ReportTemplates.InferGrtRoot();
        if (root == null)
        {
            using var d = new FolderBrowserDialog { Description = "Select your GRT folder (the one containing 'doku')" };
            if (d.ShowDialog() != DialogResult.OK) return;
            root = d.SelectedPath;
        }
        var log = Reports.ReportTemplates.Install(root);
        MessageBox.Show(string.Join(Environment.NewLine, log),
            "Install GRT report templates", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
