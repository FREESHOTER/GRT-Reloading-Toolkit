using System.Windows.Forms;
using GrtOcwAnalyzer.Analysis;
using GrtOcwAnalyzer.Ui;
using GrtPluginKit.Ipc;

namespace GrtOcwAnalyzer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Debug:  GRT_OCW_Analyzer.exe --analyze <folder>
        if (args.Length >= 2 && args[0] == "--analyze")
        {
            DebugAnalyze(args[1]);
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
        }

        using var form = new MainForm(grt);
        if (grt != null)
            grt.MenuOrToolbarActivated += () =>
            {
                if (form.IsHandleCreated) form.BeginInvoke(form.BringForward);
            };

        Application.Run(form);
        grt?.Dispose();
    }

    private static void DebugAnalyze(string folder)
    {
        try { AttachConsole(-1); } catch { }
        var loaded = LadderLoader.FromFolder(folder);
        foreach (var l in loaded.Log) Console.WriteLine("  " + l);
        Console.WriteLine();
        var res = LadderAnalyzer.Analyze(loaded.Rows);
        string report = LadderAnalyzer.BuildReport(res, "OCW / Ladder analysis — " + Path.GetFileName(folder.TrimEnd('\\', '/')));
        Console.WriteLine(report);

        string? baseArg = Environment.GetCommandLineArgs().Skip(3).FirstOrDefault();
        if (baseArg != null && File.Exists(baseArg))
        {
            var doc = GrtPluginKit.Grt.GrtLoadDoc.Load(baseArg);
            int removed = doc.RemoveByTitlePrefix("OCW ");
            doc.AddNote($"OCW Analysis {DateTime.Now:yyyy-MM-dd}", report);
            string outp = doc.SaveSibling("ocw");
            Console.WriteLine($"\n(note written -> {outp}, replaced {removed})");
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
