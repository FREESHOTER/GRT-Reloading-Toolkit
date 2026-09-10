using System.Text.RegularExpressions;
using GrtReloadingToolkit.Athlon;
using GrtPluginKit.Analysis;

namespace GrtReloadingToolkit.Ocw;

public sealed class LoadReport
{
    public List<LadderStep> Rows { get; } = new();
    public List<string> Log { get; } = new();
}

/// <summary>
/// Builds ladder steps from a folder of Athlon Rangecraft <c>*.xlsx</c> (velocities) and
/// Ballistic-X <c>*.csv</c> (target coords), pairing them by the step value.
/// Charge mode keys on charge weight (gr); Seating mode keys on a number in the file name.
/// </summary>
public static class LadderLoader
{
    public static LoadReport FromFolder(string folder, LadderMode mode)
    {
        var rep = new LoadReport();

        var vels = new Dictionary<double, AthlonString>();
        var chronoExt = new[] { ".xlsx", ".xls", ".csv" };
        foreach (string f in Directory.EnumerateFiles(folder).Where(f => chronoExt.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(x => x))
        {
            try
            {
                var a = AthlonParser.Parse(f);
                double? x = StepValue(mode, a.ChargeGrains, a.SessionNote, f);
                if (x is { } v) { vels[Key(v)] = a; rep.Log.Add($"velocity {Path.GetFileName(f)} -> {v:0.0##}, {a.Shots.Count} shots"); }
                else rep.Log.Add($"velocity {Path.GetFileName(f)}: no step value, skipped");
            }
            catch (Exception) when (Path.GetExtension(f).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                // a .csv that isn't a chrono export is probably a Ballistic-X target — handled below
            }
            catch (Exception ex) { rep.Log.Add($"velocity {Path.GetFileName(f)}: {ex.Message}"); }
        }

        var tgts = new Dictionary<double, TargetGroup>();
        foreach (string f in Directory.EnumerateFiles(folder, "*.csv").OrderBy(x => x))
        {
            try
            {
                var t = BallisticXCsv.Parse(f);
                double? x = StepValue(mode, t.ChargeGrains, null, f);
                if (x is { } v) { tgts[Key(v)] = t; rep.Log.Add($"target {Path.GetFileName(f)} -> {v:0.0##}, {t.Impacts.Count} impacts @ {t.DistanceM:0} m"); }
                else rep.Log.Add($"target {Path.GetFileName(f)}: no step value, skipped");
            }
            catch (Exception) when (vels.Values.Any(a => string.Equals(a.SourceFile, f, StringComparison.OrdinalIgnoreCase)))
            {
                // already consumed as a chrono file
            }
            catch (Exception ex) { rep.Log.Add($"target {Path.GetFileName(f)}: {ex.Message}"); }
        }

        Merge(rep, vels, tgts);
        return rep;
    }

    /// <summary>
    /// Ladder from pre-built groups (e.g. GRT shot-group tabs), optionally paired with velocities
    /// parsed from a folder of Athlon/Garmin exports. Groups whose step is null are numbered 1..N.
    /// </summary>
    public static LoadReport FromGroups(IEnumerable<TargetGroup> groups, LadderMode mode, string? velFolder = null)
    {
        var rep = new LoadReport();
        var vels = new Dictionary<double, AthlonString>();
        if (velFolder != null && Directory.Exists(velFolder))
            foreach (string f in Directory.EnumerateFiles(velFolder, "*.xls*").OrderBy(x => x))
            {
                if (!f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var a = AthlonParser.Parse(f);
                    if (StepValue(mode, a.ChargeGrains, a.SessionNote, f) is { } v)
                    { vels[Key(v)] = a; rep.Log.Add($"velocity {Path.GetFileName(f)} -> {v:0.0##}, {a.Shots.Count} shots"); }
                }
                catch (Exception ex) { rep.Log.Add($"velocity {Path.GetFileName(f)}: {ex.Message}"); }
            }

        var tgts = new Dictionary<double, TargetGroup>();
        int ord = 0;
        foreach (var t in groups)
        {
            ord++;
            double key = Key(t.ChargeGrains ?? ord);
            tgts[key] = t;
        }
        Merge(rep, vels, tgts);
        return rep;
    }

    private static void Merge(LoadReport rep, Dictionary<double, AthlonString> vels, Dictionary<double, TargetGroup> tgts)
    {
        foreach (double key in vels.Keys.Union(tgts.Keys).OrderBy(x => x))
        {
            var row = new LadderStep { X = key };
            if (vels.TryGetValue(key, out var a))
            {
                var st = StringStats.From(a.Velocities.ToList());
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
    }

    private static double? StepValue(LadderMode mode, double? chargeGrains, string? sessionNote, string path)
    {
        if (mode == LadderMode.Charge)
            return chargeGrains ?? NumberIn(Path.GetFileNameWithoutExtension(path));

        // Seating: number from the session note ("salto 0.020", "jump .015", "cbto 2.850") or the file name.
        if (!string.IsNullOrWhiteSpace(sessionNote))
        {
            var m = Regex.Match(sessionNote, @"(?:salto|jump|seat\w*|cbto|coal|profond\w*)\s*[:=]?\s*(-?\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (m.Success && TryNum(m.Groups[1].Value, out double s)) return s;
        }
        return NumberIn(Path.GetFileNameWithoutExtension(path));
    }

    private static double? NumberIn(string s)
    {
        var m = Regex.Match(s, @"(-?\d+(?:[.,]\d+)?)");
        return m.Success && TryNum(m.Groups[1].Value, out double v) ? v : null;
    }

    private static bool TryNum(string s, out double v)
        => double.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

    private static double Key(double x) => Math.Round(x, 3);
}
