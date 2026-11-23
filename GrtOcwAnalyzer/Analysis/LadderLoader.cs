using GrtOcwAnalyzer.Athlon;
using GrtOcwAnalyzer.Targets;
using GrtPluginKit.Analysis;

namespace GrtOcwAnalyzer.Analysis;

public sealed class LoadReport
{
    public List<ChargeRow> Rows { get; } = new();
    public List<string> Log { get; } = new();
}

/// <summary>
/// Builds ladder rows from a folder that holds Athlon Rangecraft <c>*.xlsx</c> exports
/// and Ballistic-X <c>*.csv</c> target files, pairing them by charge weight (±0.05 gr).
/// </summary>
public static class LadderLoader
{
    public static LoadReport FromFolder(string folder)
    {
        var rep = new LoadReport();

        var vels = new Dictionary<double, AthlonString>();
        foreach (string f in Directory.EnumerateFiles(folder, "*.xls*").OrderBy(x => x))
        {
            if (f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) is false && f.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) is false) continue;
            try
            {
                var a = AthlonParser.Parse(f);
                if (a.ChargeGrains is { } g) { vels[Key(g)] = a; rep.Log.Add($"velocity {Path.GetFileName(f)} -> {g:0.0} gr, {a.Shots.Count} shots"); }
                else rep.Log.Add($"velocity {Path.GetFileName(f)}: no charge weight, skipped");
            }
            catch (Exception ex) { rep.Log.Add($"velocity {Path.GetFileName(f)}: {ex.Message}"); }
        }

        var tgts = new Dictionary<double, TargetGroup>();
        foreach (string f in Directory.EnumerateFiles(folder, "*.csv").OrderBy(x => x))
        {
            try
            {
                var t = BallisticXCsv.Parse(f);
                if (t.ChargeGrains is { } g) { tgts[Key(g)] = t; rep.Log.Add($"target {Path.GetFileName(f)} -> {g:0.0} gr, {t.Impacts.Count} impacts @ {t.DistanceM:0} m"); }
                else rep.Log.Add($"target {Path.GetFileName(f)}: no charge weight, skipped");
            }
            catch (Exception ex) { rep.Log.Add($"target {Path.GetFileName(f)}: {ex.Message}"); }
        }

        foreach (double key in vels.Keys.Union(tgts.Keys).OrderBy(x => x))
        {
            var row = new ChargeRow { ChargeGr = key };
            if (vels.TryGetValue(key, out var a))
            {
                var v = a.Velocities.ToList();
                var st = StringStats.From(v);
                row.VelN = st.N; row.MeanMps = st.Mean; row.SdMps = st.Sd; row.EsMps = st.Es;
            }
            if (tgts.TryGetValue(key, out var t))
            {
                row.Impacts = t.Impacts.Count;
                row.DistanceM = t.DistanceM;
                row.PoiXMoa = t.CenterXMoa;
                row.PoiYMoa = t.CenterYMoa;
                row.GroupEsMoa = t.GroupEsMoa;
                row.MeanRadiusMoa = t.MeanRadiusMoa;
                row.VertSpreadMoa = t.VerticalSpreadMoa;
            }
            rep.Rows.Add(row);
        }
        return rep;
    }

    private static double Key(double grains) => Math.Round(grains * 10) / 10.0;
}
