using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Ocw;

/// <summary>Console harness:  --ladder &lt;charge|seating&gt; &lt;folder&gt; [base.grtload]</summary>
internal static class LadderCli
{
    public static void Run(string modeArg, string folder, string? basePath)
    {
        try { AttachConsole(-1); } catch { }

        if (folder.Equals("--synthetic", StringComparison.OrdinalIgnoreCase))
        {
            Synthetic(modeArg, basePath);
            return;
        }

        var mode = modeArg.StartsWith("seat", StringComparison.OrdinalIgnoreCase) ? LadderMode.Seating : LadderMode.Charge;
        string unit = mode == LadderMode.Seating ? "mm" : "gr";

        var loaded = LadderLoader.FromFolder(folder, mode);

        // fill any step that has no chrono velocity from the base load's own Measurement, like the GUI
        if (basePath != null && File.Exists(basePath))
        {
            var bdoc = GrtLoadDoc.Load(GrtLoadDoc.EffectiveReadPath(basePath));
            var lv = new List<LadderVelocity>();
            foreach (var meas in bdoc.Measurements())
                foreach (var ch in meas.Charges)
                {
                    var vs = ch.Shots.Select(sh => sh.VelocityMps).Where(v => v > 50).ToList();
                    if (vs.Count > 0 && LadderLoader.MeasurementStep(mode, ch.ChargeGrains, ch.Name, ch.Note) is { } step)
                        lv.Add(new LadderVelocity(step, vs));
                }
            if (lv.Count > 0) LadderLoader.FillMissingVelocities(loaded, lv);
        }

        foreach (var l in loaded.Log) Console.WriteLine("  " + l);
        Console.WriteLine();

        var res = LadderAnalyzer.Analyze(loaded.Rows, mode, unit);
        string title = mode == LadderMode.Seating ? "Seating Depth Analysis" : "OCW Analysis";
        string report = LadderAnalyzer.BuildReport(res, $"{title} {DateTime.Now:yyyy-MM-dd}");
        Console.WriteLine(report);

        var chart = new LadderChart();
        chart.Show(res);
        byte[]? png = chart.RenderPng();
        Console.WriteLine(png != null ? $"chart PNG: {png.Length} bytes" : "chart PNG: (none)");

        if (basePath != null && File.Exists(basePath))
        {
            var doc = GrtLoadDoc.Load(basePath);
            int removed = doc.RemoveByTitlePrefix(title);
            doc.AddNote(title, report);
            string picName = mode == LadderMode.Seating ? "seating_chart" : "ocw_chart";
            if (png != null) doc.AddGalleryPicture($"{title} chart", picName, png, 1600, 760);
            string outp = doc.SaveSibling(mode == LadderMode.Seating ? "seating" : "ocw");
            Console.WriteLine($"\n(note '{title}' written -> {outp}, replaced {removed})");
        }
    }

    /// <summary>--ladder &lt;charge|seating&gt; --synthetic [out.png] — no fixtures needed, exercises chart + gallery.</summary>
    private static void Synthetic(string modeArg, string? outPng)
    {
        var mode = modeArg.StartsWith("seat", StringComparison.OrdinalIgnoreCase) ? LadderMode.Seating : LadderMode.Charge;
        string unit = mode == LadderMode.Seating ? "mm" : "gr";
        var rows = new List<LadderStep>();
        // a flat spot / plateau in the middle
        double[] xs = mode == LadderMode.Seating
            ? new[] { 2.80, 2.83, 2.86, 2.89, 2.92, 2.95 }
            : new[] { 39.2, 39.6, 40.0, 40.4, 40.8, 41.2 };
        double[] mv = { 805, 812, 820, 821, 822, 831 };
        double[] grp = { 1.4, 0.9, 0.55, 0.5, 0.62, 1.1 };
        double[] poi = { -0.8, -0.3, 0.05, 0.1, 0.2, 1.0 };
        for (int i = 0; i < xs.Length; i++)
            rows.Add(new LadderStep
            {
                X = xs[i],
                VelN = 5, MeanMps = mv[i], SdMps = 4 + i * 0.2, EsMps = 10,
                Impacts = 5, DistanceM = 100, PoiYMoa = poi[i], MeanRadiusMoa = grp[i], VertSpreadMoa = grp[i] * 1.8,
            });

        var res = LadderAnalyzer.Analyze(rows, mode, unit);
        Console.WriteLine(LadderAnalyzer.BuildReport(res, $"{mode} synthetic"));

        var chart = new LadderChart();
        chart.Show(res);
        byte[]? png = chart.RenderPng();
        Console.WriteLine(png != null ? $"chart PNG: {png.Length} bytes" : "chart PNG: (none)");
        if (png == null || string.IsNullOrWhiteSpace(outPng)) return;

        if (outPng.EndsWith(".grtload", StringComparison.OrdinalIgnoreCase) && File.Exists(outPng))
        {
            // write note + gallery chart into a real load, exactly like the GUI "Write note" does
            string title = mode == LadderMode.Seating ? "Seating Depth Analysis" : "OCW Analysis";
            string picName = mode == LadderMode.Seating ? "seating_chart" : "ocw_chart";
            var doc = GrtLoadDoc.OpenForToolkitEdit(outPng);
            doc.RemoveByTitlePrefix(title);
            doc.AddNote(title, LadderAnalyzer.BuildReport(res, $"{title} synthetic"));
            doc.AddGalleryPicture($"{title} chart", picName, png, 1600, 760);
            string outp = doc.SaveSibling(mode == LadderMode.Seating ? "seating" : "ocw");
            Console.WriteLine("wrote " + outp);
        }
        else
        {
            File.WriteAllBytes(outPng, png);
            Console.WriteLine("wrote " + Path.GetFullPath(outPng));
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
