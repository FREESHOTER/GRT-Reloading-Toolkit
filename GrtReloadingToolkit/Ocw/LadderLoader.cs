using System.Text.RegularExpressions;
using System.Globalization;
using GrtPluginKit.Grt;
using GrtPluginKit.Util;
using GrtReloadingToolkit.Athlon;
using GrtPluginKit.Analysis;

namespace GrtReloadingToolkit.Ocw;

public sealed class LoadReport
{
    public List<LadderStep> Rows { get; } = new();
    public List<string> Log { get; } = new();
}

/// <summary>Velocities for one ladder step, sourced outside a chrono folder (e.g. a GRT load Measurement).</summary>
public sealed record LadderVelocity(double Step, IReadOnlyList<double> Velocities);

/// <summary>
/// Builds ladder steps from a folder of Athlon Rangecraft <c>*.xlsx</c> (velocities) and
/// OnTarget <c>*.csv</c> (target coords), pairing them by the step value.
/// Charge mode keys on charge weight (gr); Seating mode keys on a number in the file name.
/// </summary>
public static class LadderLoader
{
    public static LoadReport FromFolder(string folder, LadderMode mode)
    {
        var (tgts, vels, log) = ScanFolder(folder, mode);
        var rep = new LoadReport();
        rep.Log.AddRange(log);
        Merge(rep, vels, tgts);
        return rep;
    }

    /// <summary>
    /// The raw scan behind <see cref="FromFolder"/>, kept separate because <see cref="FromFolder"/>
    /// immediately reduces every target to a <see cref="LadderStep"/>'s summary numbers (impact count,
    /// centre, ES, mean radius) and discards the actual <see cref="TargetGroup.Impacts"/> list — fine
    /// for a ladder table, but Group Analysis needs the raw impacts (for Mahalanobis outliers, CEP50,
    /// and pooling several charges into one combined group), so it calls this directly instead.
    /// </summary>
    public static (Dictionary<double, TargetGroup> Targets, Dictionary<double, IReadOnlyList<double>> Velocities, List<string> Log) ScanFolder(string folder, LadderMode mode)
    {
        var log = new List<string>();
        var vels = new Dictionary<double, IReadOnlyList<double>>();
        var chronoExt = new[] { ".xlsx", ".csv" };
        var chronoFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string f in Directory.EnumerateFiles(folder).Where(f => chronoExt.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(x => x))
        {
            try
            {
                var a = AthlonParser.Parse(f);
                double? x = StepValue(mode, a.ChargeGrains, a.SessionNote, f);
                if (x is { } v) { AddVelocities(vels, v, a.Velocities.ToList(), log, Path.GetFileName(f)); chronoFiles.Add(f); log.Add($"velocity {Path.GetFileName(f)} -> {StepText(mode, v)}, {a.Shots.Count} shots"); }
                else log.Add($"velocity {Path.GetFileName(f)}: no step value, skipped");
            }
            catch (Exception) when (Path.GetExtension(f).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                // a .csv that isn't a chrono export is probably an OnTarget target — handled below
            }
            catch (Exception ex) { log.Add($"velocity {Path.GetFileName(f)}: {ex.Message}"); }
        }

        var tgts = new Dictionary<double, TargetGroup>();
        foreach (string f in Directory.EnumerateFiles(folder, "*.csv").OrderBy(x => x))
        {
            try
            {
                var t = OnTargetCsv.Parse(f);
                double? x = StepValue(mode, t.ChargeGrains, null, f);
                if (x is { } v) { AddTarget(tgts, v, t, log, Path.GetFileName(f)); log.Add($"target {Path.GetFileName(f)} -> {StepText(mode, v)}, {t.Impacts.Count} impacts @ {GrtUnits.Current.Distance(t.DistanceM)}"); }
                else log.Add($"target {Path.GetFileName(f)}: no step value, skipped");
            }
            catch (Exception) when (chronoFiles.Contains(f))
            {
                // already consumed as a chrono file
            }
            catch (Exception ex) { log.Add($"target {Path.GetFileName(f)}: {ex.Message}"); }
        }

        return (tgts, vels, log);
    }

    /// <summary>
    /// Ladder from pre-built groups (e.g. GRT shot-group tabs), optionally paired with velocities
    /// parsed from a folder of Athlon/Garmin exports. A group whose step is null is SKIPPED, never
    /// numbered 1..N: GRT names a new group "Gruppo 1", "Gruppo 2"... and reading those as 1, 2... grains
    /// invents a ladder (and a "node") that has nothing to do with the real charges. The caller
    /// (<see cref="AssignOrdinalSteps"/>) is the only place an unlabelled group may be given a step,
    /// and only after the user has confirmed the mapping.
    /// </summary>
    public static LoadReport FromGroups(IEnumerable<TargetGroup> groups, LadderMode mode, string? velFolder = null)
    {
        var rep = new LoadReport();
        var vels = new Dictionary<double, IReadOnlyList<double>>();

        if (velFolder != null && Directory.Exists(velFolder))
            foreach (string f in Directory.EnumerateFiles(velFolder, "*.xls*").OrderBy(x => x))
            {
                if (!f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) continue;   // the glob also catches .xlsm/.xlsb/.xls
                try
                {
                    var a = AthlonParser.Parse(f);
                    if (StepValue(mode, a.ChargeGrains, a.SessionNote, f) is { } v)
                    { AddVelocities(vels, v, a.Velocities.ToList(), rep.Log, Path.GetFileName(f)); rep.Log.Add($"velocity {Path.GetFileName(f)} -> {StepText(mode, v)}, {a.Shots.Count} shots"); }
                }
                catch (Exception ex) { rep.Log.Add($"velocity {Path.GetFileName(f)}: {ex.Message}"); }
            }

        var tgts = new Dictionary<double, TargetGroup>();
        foreach (var t in groups)
        {
            if (t.ChargeGrains is not { } step)
            {
                rep.Log.Add($"{t.SourceFile}: no charge / step in the group name -- skipped (rename the group in GRT, e.g. \"40.2 gr\")");
                continue;
            }
            AddTarget(tgts, step, t, rep.Log, t.SourceFile);
        }
        Merge(rep, vels, tgts);
        return rep;
    }

    /// <summary>
    /// Gives each step-less group the next velocity step, in ascending order, when -- and only when --
    /// there are exactly as many step-less groups as velocity steps not already taken by a named group.
    /// Returns the (group, step) pairs it WOULD assign, or null if the counts do not line up. Nothing is
    /// modified: the caller shows the pairs to the user and applies them only on a yes
    /// (<see cref="ApplySteps"/>), because shooting order is an assumption, not data.
    /// </summary>
    public static IReadOnlyList<(TargetGroup Group, double Step)>? AssignOrdinalSteps(
        IReadOnlyList<TargetGroup> groups, IEnumerable<double> velocitySteps)
    {
        var unlabelled = groups.Where(g => g.ChargeGrains is null).ToList();
        if (unlabelled.Count == 0) return null;
        var taken = groups.Where(g => g.ChargeGrains is not null).Select(g => Key(g.ChargeGrains!.Value)).ToHashSet();
        var free = velocitySteps.Select(Key).Distinct().Where(k => !taken.Contains(k)).OrderBy(k => k).ToList();
        if (free.Count != unlabelled.Count) return null;
        return unlabelled.Select((g, i) => (g, free[i])).ToList();
    }

    public static void ApplySteps(IEnumerable<(TargetGroup Group, double Step)> pairs)
    {
        foreach (var (g, step) in pairs) g.ChargeGrains = step;
    }

    // Two strings or two groups at the same step are pooled, not "last one wins": a ladder shot over
    // two sessions, or a chrono file re-exported, would otherwise silently lose all but one of them.
    private static void AddVelocities(Dictionary<double, IReadOnlyList<double>> vels, double step,
        List<double> v, List<string> log, string label)
    {
        double key = Key(step);
        if (vels.TryGetValue(key, out var prev))
        {
            vels[key] = prev.Concat(v).ToList();
            log.Add($"{label}: same step as an earlier string -- pooled ({prev.Count}+{v.Count} shots)");
        }
        else vels[key] = v;
    }

    private static void AddTarget(Dictionary<double, TargetGroup> tgts, double step, TargetGroup t,
        List<string> log, string label)
    {
        double key = Key(step);
        if (tgts.TryGetValue(key, out var prev))
        {
            var pooled = new TargetGroup
            {
                SourceFile = prev.SourceFile + " + " + t.SourceFile,
                ChargeGrains = prev.ChargeGrains ?? t.ChargeGrains,
                DistanceM = prev.DistanceM,
                AimXMoa = prev.AimXMoa, AimYMoa = prev.AimYMoa,
                AppCenterXMoa = prev.AppCenterXMoa, AppCenterYMoa = prev.AppCenterYMoa,
            };
            pooled.Impacts.AddRange(prev.Impacts);
            pooled.Impacts.AddRange(t.Impacts);
            tgts[key] = pooled;
            log.Add($"{label}: same step as an earlier group -- pooled ({prev.Impacts.Count}+{t.Impacts.Count} hits)");
        }
        else tgts[key] = t;
    }

    /// <summary>
    /// Fills in velocity stats for any ladder step that has none, from velocities carried in the
    /// open GRT load's Measurement. Steps that already have a chrono velocity are left untouched.
    /// </summary>
    public static void FillMissingVelocities(LoadReport rep, IEnumerable<LadderVelocity> extra)
    {
        var map = extra.Where(v => v.Velocities.Count > 0)
                       .GroupBy(v => Key(v.Step))
                       .ToDictionary(g => g.Key, g => (IReadOnlyList<double>)g.SelectMany(v => v.Velocities).ToList());
        int filled = 0;
        foreach (var row in rep.Rows)
            if (row.VelN == 0 && map.TryGetValue(Key(row.X), out var vs))
            {
                var st = StringStats.From(vs.ToList());
                row.VelN = st.N; row.MeanMps = st.Mean; row.SdMps = st.Sd; row.EsMps = st.Es;
                filled++;
            }
        if (filled > 0) rep.Log.Add($"filled {filled} step(s) with velocities from the load's Measurement");
    }

    private static void Merge(LoadReport rep, Dictionary<double, IReadOnlyList<double>> vels, Dictionary<double, TargetGroup> tgts)
    {
        foreach (double key in vels.Keys.Union(tgts.Keys).OrderBy(x => x))
        {
            var row = new LadderStep { X = key };
            if (vels.TryGetValue(key, out var vlist) && vlist.Count > 0)
            {
                var st = StringStats.From(vlist.ToList());
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

    /// <summary>Step value for a GRT Measurement <c>&lt;charge&gt;</c> — charge weight (gr) in Charge mode,
    /// a seating/jump number parsed from the charge name or note in Seating mode.</summary>
    public static double? MeasurementStep(LadderMode mode, double? chargeGrains, string name, string? note)
    {
        if (mode == LadderMode.Charge)
            return chargeGrains ?? NumberIn(name);

        foreach (var t in new[] { note, name })
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            var m = Regex.Match(t, @"(?:salto|jump|seat\w*|cbto|coal|profond\w*)\s*[:=]?\s*(-?\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (m.Success && TryNum(m.Groups[1].Value, out double s)) return s;
        }
        return NumberIn(name);
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

    /// <summary>A step number inside a name. A number with a decimal point wins over a bare integer
    /// ("N550 40.2" is 40.2, not the powder's 550); with no decimal number the first integer is used.</summary>
    private static double? NumberIn(string s)
    {
        var all = Regex.Matches(s, @"(-?\d+(?:[.,]\d+)?)");
        var pick = all.FirstOrDefault(m => m.Value.Contains('.') || m.Value.Contains(',')) ?? all.FirstOrDefault();
        return pick is { Success: true } && TryNum(pick.Groups[1].Value, out double v) ? v : null;
    }

    private static bool TryNum(string s, out double v)
        => double.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

    /// <summary>
    /// Pairing key for a ladder step. Deliberately rounded: the step is recovered from file names
    /// and session notes written by hand and by two different apps, so a velocity file and its
    /// target file have to land on the same key despite the last digit disagreeing. Three decimals
    /// is the tolerance, not the precision - <see cref="StepText"/> shows the value unrounded, and
    /// two genuinely distinct steps closer together than 0.001 would merge here.
    /// </summary>
    private static double Key(double x) => Math.Round(x, 3);

    /// <summary>A step value for the log: a length in a seating ladder, a charge weight otherwise.</summary>
    private static string StepText(LadderMode mode, double v) => mode == LadderMode.Seating
        ? Str.Len(v)
        : v.ToString("0.0##", CultureInfo.InvariantCulture);
}
