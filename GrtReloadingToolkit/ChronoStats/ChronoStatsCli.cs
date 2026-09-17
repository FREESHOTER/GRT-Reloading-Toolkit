using GrtPluginKit.Grt;
using GrtReloadingToolkit.Athlon;

namespace GrtReloadingToolkit.ChronoStats;

/// <summary>Console harness: --chronostats &lt;folder&gt; [base.grtload] [targetMarginMps]</summary>
internal static class ChronoStatsCli
{
    public static void Run(string folder, string? basePath, double targetMarginMps = 3.0)
    {
        try { AttachConsole(-1); } catch { }

        var exts = new[] { ".xlsx", ".csv", ".tsv" };
        var rows = new List<(string label, List<double> values)>();
        foreach (string f in Directory.EnumerateFiles(folder).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(x => x))
        {
            try
            {
                var a = AthlonParser.Parse(f);
                var vs = a.Velocities.ToList();
                if (vs.Count == 0) continue;
                string label = a.ChargeGrains is { } g ? FormattableString.Invariant($"{g:0.0##} gr ({Path.GetFileName(f)})") : Path.GetFileName(f);
                rows.Add((label, vs));
                Console.WriteLine($"  loaded {Path.GetFileName(f)}: {vs.Count} shots");
            }
            catch (Exception ex) { Console.WriteLine($"  {Path.GetFileName(f)}: {ex.Message}"); }
        }
        Console.WriteLine();
        if (rows.Count == 0) { Console.WriteLine("no chrono files found"); return; }

        const double confidence = 0.95;
        var summaries = rows.Select(r => (r.label, Summary: ChronoStatsCalc.Analyze(r.values, confidence))).ToList();
        var report = new System.Text.StringBuilder();
        string table = ChronoStatsCalc.BuildTableReport(summaries, targetMarginMps, confidence);
        Console.WriteLine(table);
        report.Append(table);

        if (rows.Count >= 2)
        {
            var cmp = ChronoStatsCalc.Compare(rows[0].values, rows[1].values);
            string block = ChronoStatsCalc.BuildCompareReport(rows[0].label, rows[1].label, cmp);
            Console.WriteLine(block);
            report.AppendLine().Append(block);
        }

        if (basePath != null && File.Exists(basePath))
        {
            var doc = GrtLoadDoc.Load(basePath);
            const string title = "Chrono Statistics";
            int removed = doc.RemoveByTitlePrefix(title);
            doc.AddNote(title, report.ToString());
            string outp = doc.SaveSibling("chronostats");
            Console.WriteLine($"\n(note '{title}' written -> {outp}, replaced {removed})");
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
