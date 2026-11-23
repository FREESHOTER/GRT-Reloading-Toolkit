using System.Windows.Forms;
using GrtPluginKit.Ipc;
using GrtModelLab.BarrelProfile;
using GrtModelLab.Ui;

namespace GrtModelLab;

internal static class Program
{
    internal const string PluginId = "com.grt.plugin.modelobt";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--profiletest") { ProfileTestCli.Run(args.Length >= 2 ? args[1] : null); return; }
        if (args.Length >= 1 && args[0] == "--sweeptest") { SensitivitySweepTestCli.Run(args.Length >= 2 ? args[1] : null); return; }
        if (args.Length >= 1 && args[0] == "--beamtest") { BeamModelTestCli.Run(args.Length >= 2 ? args[1] : null); return; }
        if (args.Length >= 1 && args[0] == "--femtest") { BeamFemTestCli.Run(); return; }
        if (args.Length >= 1 && args[0] == "--curvetest" && args.Length >= 2 && int.TryParse(args[1], out int cport)) { CurveTestCli.Run(cport); return; }
        if (args.Length >= 1 && args[0] == "--forcetest" && args.Length >= 2 && int.TryParse(args[1], out int fport)) { ForceTestCli.Run(fport); return; }
        if (args.Length >= 1 && args[0] == "--reports")
        {
            string? reportRoot = args.Length >= 2 ? args[1] : Reports.ReportTemplates.InferGrtRoot();
            if (reportRoot == null) { Console.WriteLine("non trovo la cartella GRT (passala come argomento)"); return; }
            foreach (var l in Reports.ReportTemplates.Install(reportRoot)) Console.WriteLine(l);
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
            for (int i = 0; i < 25 && grt.PendingActivation is null; i++) Thread.Sleep(20);
        }

        Application.Run(new ToolkitContext(grt));
        grt?.Dispose();
    }
}

// SensitivitySweep is intentionally not wired here — it studies GRT's own model in general, not the
// barrel specifically, so it belongs in a future separate "advanced diagnostics" plugin instead. The
// form/engine (Ui/SensitivitySweepForm.cs, SensitivitySweep/*.cs) stay in the tree, unreferenced, as
// a starting point for that plugin.
internal enum Tool { Launcher, BarrelProfile, BarrelVibration, Reports }

/// <summary>Same one-process-many-windows shape as the Reloading Toolkit's ToolkitContext.</summary>
internal sealed class ToolkitContext : ApplicationContext
{
    private readonly BarrelProfileDb _db;
    private readonly GrtClient? _grt;
    private readonly Dictionary<Tool, Form> _open = new();
    private readonly WindowsFormsSynchronizationContext _ui = new();

    public ToolkitContext(GrtClient? grt)
    {
        SynchronizationContext.SetSynchronizationContext(_ui);
        _db = new BarrelProfileDb(BarrelProfileDb.DefaultPath);
        _grt = grt;

        bool launchedIntoTool = grt?.PendingActivation is { Length: > 0 } pa && FromId(pa) != Tool.Launcher;
        if (grt != null)
            grt.MenuOrToolbarActivated += id => _ui.Post(_ => Open(FromId(id)), null);

        if (!launchedIntoTool) Open(Tool.Launcher);
    }

    public static Tool FromId(string id) =>
        id.EndsWith(".barrelprofile", StringComparison.Ordinal) ? Tool.BarrelProfile :
        id.EndsWith(".barrelvibration", StringComparison.Ordinal) ? Tool.BarrelVibration :
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

        // Every tool window is born here, so this is the one place that has to dress them.
        Form f = AppIcon.Apply<Form>(t switch
        {
            Tool.BarrelProfile => new BarrelProfileForm(_db),
            Tool.BarrelVibration => new BarrelVibrationForm(_db, _grt),
            _ => new LauncherForm(Open),
        });
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
            using var d = new FolderBrowserDialog { Description = "Seleziona la cartella di GRT (quella che contiene 'doku')" };
            if (d.ShowDialog() != DialogResult.OK) return;
            root = d.SelectedPath;
        }
        var log = Reports.ReportTemplates.Install(root);
        MessageBox.Show(string.Join(Environment.NewLine, log),
            "Installa modelli di rapporto GRT", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
