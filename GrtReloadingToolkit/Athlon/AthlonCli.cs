using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Athlon;

/// <summary>Console harness:  --athlon &lt;folder&gt; &lt;base.grtload&gt; [tempC]</summary>
internal static class AthlonCli
{
    public static void Run(string folder, string basePath, double? tempC)
    {
        try { AttachConsole(-1); } catch { }

        var rows = new List<ImportBuilder.Row>();
        var exts = new[] { ".xlsx", ".xls", ".csv", ".tsv" };
        foreach (string f in Directory.EnumerateFiles(folder).Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(x => x))
        {
            try
            {
                var a = AthlonParser.Parse(f);
                rows.Add(new ImportBuilder.Row(a, tempC ?? a.SessionTempC, true));
                Console.WriteLine($"  {a.FileName}: {a.Shots.Count} shots, {a.ChargeGrains} gr, unit={a.SourceUnit}");
                foreach (var w in a.Warnings) Console.WriteLine($"      ! {w}");
            }
            catch (Exception ex) { Console.WriteLine($"  {Path.GetFileName(f)}: {ex.Message}"); }
        }
        if (rows.Count == 0) { Console.WriteLine("no chrono files parsed"); return; }

        var charges = ImportBuilder.Build(rows, null, perShotTemp: false);
        var doc = GrtLoadDoc.Load(basePath);
        doc.RemoveByTitlePrefix(ImportBuilder.MeasurementTitlePrefix);
        string title = $"{ImportBuilder.MeasurementTitlePrefix}{(charges.Count > 1 ? "Ladder" : "Measurement")} {DateTime.Now:yyyy-MM-dd}";
        doc.AddMeasurement(title, charges);
        string outp = doc.SaveSibling("athlon");
        Console.WriteLine($"\nwrote {outp}  ({charges.Count} charge(s))");
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
