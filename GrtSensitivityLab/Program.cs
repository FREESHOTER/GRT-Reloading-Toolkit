using System.Windows.Forms;
using GrtPluginKit.Ipc;
using GrtSensitivityLab.Ui;

namespace GrtSensitivityLab;

internal static class Program
{
    internal const string PluginId = "com.grt.plugin.sensitivitylab";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--reports")
        {
            string? root = args.Length >= 2 ? args[1] : Reports.ReportTemplates.InferGrtRoot();
            if (root == null) { Console.WriteLine("could not find GRT root (pass it as an argument)"); return; }
            foreach (var l in Reports.ReportTemplates.Install(root)) Console.WriteLine(l);
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
            // Give GRT a moment to deliver the toolbar/menu click that launched us (onDemand), so
            // we can open that tool straight away instead of the launcher.
            for (int i = 0; i < 25 && grt.PendingActivation is null; i++) Thread.Sleep(20);
        }

        Application.Run(new ToolkitContext(grt));
        grt?.Dispose();
    }
}

internal enum Tool { Launcher, WhatIf, Sweep, Reports }

/// <summary>Same one-process-many-windows shape as GrtModelLab's and the Reloading Toolkit's own
/// ToolkitContext -- kept even though this release has only one real tool, so a second tool can be
/// added later without restructuring this class.</summary>
internal sealed class ToolkitContext : ApplicationContext
{
    private readonly GrtClient? _grt;
    private readonly Dictionary<Tool, Form> _open = new();
    private readonly WindowsFormsSynchronizationContext _ui = new();

    public ToolkitContext(GrtClient? grt)
    {
        _grt = grt;
        SynchronizationContext.SetSynchronizationContext(_ui);

        // If GRT started us from a click on a *specific* tool, that tool opens itself (the id is
        // replayed synchronously by the subscribe below). A click on the plain toolbar/menu entry
        // resolves to Launcher, so we still want the launcher.
        bool launchedIntoTool = grt?.PendingActivation is { Length: > 0 } pa && FromId(pa) != Tool.Launcher;
        if (grt != null)
            grt.MenuOrToolbarActivated += id => _ui.Post(_ => Open(FromId(id)), null);

        if (!launchedIntoTool) Open(Tool.Launcher);
    }

    public static Tool FromId(string id) =>
        id.EndsWith(".whatif", StringComparison.Ordinal) ? Tool.WhatIf :
        id.EndsWith(".sweep", StringComparison.Ordinal) ? Tool.Sweep :
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
            Tool.WhatIf => new WhatIfForm(_grt),
            Tool.Sweep => new SensitivitySweepForm(_grt),
            _ => new LauncherForm(Open),
        });
        f.FormClosed += (_, _) =>
        {
            _open.Remove(t);
            if (_open.Count == 0) ExitThread();
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
