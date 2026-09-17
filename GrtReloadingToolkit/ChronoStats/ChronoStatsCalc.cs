using System.Globalization;
using System.Text;
using GrtPluginKit.Analysis;

namespace GrtReloadingToolkit.ChronoStats;

/// <summary>One shot's distance from the mean, in Chauvenet terms.</summary>
public sealed record ChronoOutlier(int Index, double ValueMps, double ZScore, double ExpectedCount, bool Flagged);

/// <summary>
/// Everything GRT's own "AVG/SD/ES" line on an imported Measurement doesn't tell you: how much the
/// true mean could plausibly differ from what you measured, how many shots you'd need for a
/// tighter answer, and which shots (if any) look like they don't belong.
/// </summary>
public sealed class ChronoSummary
{
    public required IReadOnlyList<double> Values { get; init; }
    public int N => Values.Count;
    /// <summary>Population SD (&#247;n) &#8212; same convention as the rest of the toolkit and the
    /// chrono device itself, shown for comparison with what's already on screen elsewhere.</summary>
    public double Sd { get; init; }
    public double Mean { get; init; }
    public double Es { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }

    /// <summary>Sample SD (&#247;n&#8722;1) &#8212; the one the CI/sample-size/comparison math
    /// actually uses; the Bessel correction matters for n this small.</summary>
    public double SampleSd { get; init; }

    public double CiLevel { get; init; }
    public double CiMarginMps { get; init; }
    public double CiLoMps => Mean - CiMarginMps;
    public double CiHiMps => Mean + CiMarginMps;

    /// <summary>Shots the mean/SD were computed from (Chauvenet-flagged points excluded).</summary>
    public IReadOnlyList<ChronoOutlier> Outliers { get; init; } = Array.Empty<ChronoOutlier>();
    public bool HasOutliers => Outliers.Any(o => o.Flagged);
}

public sealed record TwoSampleResult(
    int NA, double MeanA, double SdA,
    int NB, double MeanB, double SdB,
    double MeanDiffMps, double TStat, double TDof, double TPValue, bool MeansDiffer,
    double FStat, double FDof1, double FDof2, double FPValue, bool SpreadsDiffer);

public static class ChronoStatsCalc
{
    /// <summary>Chauvenet's criterion: a shot is rejected if the expected number of measurements
    /// this far (or farther) from the mean, over the whole sample, is under &#189; &#8212; i.e. you
    /// wouldn't expect to see it even once. Classic normal-distribution formulation; n&#8805;3.</summary>
    public static IReadOnlyList<ChronoOutlier> ChauvenetOutliers(IReadOnlyList<double> values)
    {
        if (values.Count < 3) return Array.Empty<ChronoOutlier>();
        double mean = values.Average();
        double sd = PopulationSd(values, mean);
        if (sd <= 0) return values.Select((v, i) => new ChronoOutlier(i, v, 0, values.Count, false)).ToList();

        var list = new List<ChronoOutlier>();
        for (int i = 0; i < values.Count; i++)
        {
            double z = Math.Abs(values[i] - mean) / sd;
            double tailProb = 1 - StatsMath.Erf(z / Math.Sqrt(2));   // P(|Z| >= z), two-tailed
            double expected = values.Count * tailProb;
            list.Add(new ChronoOutlier(i, values[i], z, expected, expected < 0.5));
        }
        return list;
    }

    /// <summary>Mean, spread, a confidence interval on the true mean, and any Chauvenet outliers
    /// (excluded from the mean/SD/CI, same as a careful hand analysis would do).</summary>
    public static ChronoSummary Analyze(IReadOnlyList<double> values, double confidence = 0.95)
    {
        var outliers = ChauvenetOutliers(values);
        var kept = outliers.Count > 0
            ? values.Where((_, i) => !outliers[i].Flagged).ToList()
            : values.ToList();
        if (kept.Count == 0) kept = values.ToList();   // never end up with nothing to report

        double mean = kept.Count > 0 ? kept.Average() : 0;
        double sdPop = PopulationSd(kept, mean);
        double sdSample = SampleSdOf(kept, mean);
        double min = kept.Count > 0 ? kept.Min() : 0, max = kept.Count > 0 ? kept.Max() : 0;

        double margin = 0;
        if (kept.Count >= 2 && sdSample > 0)
        {
            double tCrit = StudentT.TwoTailedCritical(kept.Count - 1, 1 - confidence);
            margin = tCrit * sdSample / Math.Sqrt(kept.Count);
        }

        return new ChronoSummary
        {
            Values = kept,
            Mean = mean,
            Sd = sdPop,
            SampleSd = sdSample,
            Es = max - min,
            Min = min,
            Max = max,
            CiLevel = confidence,
            CiMarginMps = margin,
            Outliers = outliers,
        };
    }

    /// <summary>How many shots (of this same spread) would it take to pin the mean down to
    /// &#177;<paramref name="marginMps"/> at the given confidence? Iterative because the t critical
    /// value itself depends on n. Returns 0 if the string has no spread to reason about, -1 if the
    /// target margin is unreachable within a sane number of shots.</summary>
    public static int RequiredSampleSize(double sampleSd, double marginMps, double confidence = 0.95)
    {
        if (sampleSd <= 0 || marginMps <= 0) return 0;
        for (int n = 2; n <= 1000; n++)
        {
            double tCrit = StudentT.TwoTailedCritical(n - 1, 1 - confidence);
            if (tCrit * sampleSd / Math.Sqrt(n) <= marginMps) return n;
        }
        return -1;
    }

    /// <summary>
    /// Welch's t-test (means, unequal-variance) and an F-test (spreads) between two strings —
    /// "is charge A actually faster than charge B, or is that just chrono noise?" Neither test
    /// assumes the two strings have the same size or the same SD.
    /// </summary>
    public static TwoSampleResult Compare(IReadOnlyList<double> a, IReadOnlyList<double> b, double alpha = 0.05)
    {
        int na = a.Count, nb = b.Count;
        double ma = a.Average(), mb = b.Average();
        double sa = SampleSdOf(a, ma), sb = SampleSdOf(b, mb);

        double va = sa * sa, vb = sb * sb;
        double se = Math.Sqrt((na > 0 ? va / na : 0) + (nb > 0 ? vb / nb : 0));
        double t = se > 0 ? (ma - mb) / se : 0;
        double dof = 1;
        if (na > 1 && nb > 1 && (va > 0 || vb > 0))
        {
            double num = Math.Pow(va / na + vb / nb, 2);
            double den = Math.Pow(va / na, 2) / (na - 1) + Math.Pow(vb / nb, 2) / (nb - 1);
            dof = den > 0 ? num / den : na + nb - 2;
        }
        double tp = se > 0 ? 2 * (1 - StudentT.Cdf(Math.Abs(t), dof)) : 1;

        double f = vb > 0 ? va / vb : (va > 0 ? double.PositiveInfinity : 1);
        double fp = double.IsFinite(f) && na > 1 && nb > 1 ? FDist.TwoTailedPValue(f, na - 1, nb - 1) : 1;

        return new TwoSampleResult(
            na, ma, sa, nb, mb, sb,
            ma - mb, t, dof, tp, tp < alpha,
            f, na - 1, nb - 1, fp, fp < alpha);
    }

    private static double PopulationSd(IReadOnlyList<double> v, double mean) =>
        v.Count == 0 ? 0 : Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / v.Count);

    private static double SampleSdOf(IReadOnlyList<double> v, double mean) =>
        v.Count < 2 ? 0 : Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1));

    /// <summary>
    /// One compact table over every string, GRT-report style — the point of a statistics note is
    /// to scan many charges at a glance, not read a paragraph per charge. Chauvenet exclusions are
    /// marked inline (*) and spelled out once in a footnote below the table.
    /// </summary>
    public static string BuildTableReport(IReadOnlyList<(string Label, ChronoSummary Summary)> rows, double targetMarginMps, double confidence)
    {
        int ci = (int)Math.Round(confidence * 100);
        var sb = new StringBuilder();
        sb.AppendLine(FormattableString.Invariant(
            $"{ci}% confidence interval on each string's true mean. \"n needed\" = shots (of that"));
        sb.AppendLine(FormattableString.Invariant($"string's own spread) to pin the mean to +/-{targetMarginMps:0.0} m/s."));
        sb.AppendLine();
        sb.AppendLine(" charge            n   mean    SD    ES  |    95% CI on mean     | n needed");
        var footnotes = new List<string>();
        foreach (var (label, s) in rows)
        {
            string shortLabel = ShortLabel(label);
            int need = RequiredSampleSize(s.SampleSd, targetMarginMps, s.CiLevel);
            string needTxt = need switch { 0 => "–", -1 => ">1000", _ => need.ToString(CultureInfo.InvariantCulture) };
            var flagged = s.Outliers.Where(o => o.Flagged).ToList();
            sb.AppendLine(FormattableString.Invariant(
                $"{shortLabel,-14}{(flagged.Count > 0 ? "*" : " ")} {s.N,3} {s.Mean,7:0.0} {s.Sd,5:0.0} {s.Es,5:0.0}  | {s.CiLoMps,7:0.0} to {s.CiHiMps,-7:0.0} |   {needTxt,4}"));
            foreach (var o in flagged)
                footnotes.Add(FormattableString.Invariant($"  * {shortLabel}: shot #{o.Index + 1} = {o.ValueMps:0.0} m/s excluded (Chauvenet) — not counted above"));
        }
        if (footnotes.Count > 0) { sb.AppendLine(); foreach (var f in footnotes) sb.AppendLine(f); }
        return sb.ToString();
    }

    /// <summary>The two-sample comparison block for the note/console output.</summary>
    public static string BuildCompareReport(string labelA, string labelB, TwoSampleResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(FormattableString.Invariant($"Compare: {ShortLabel(labelA)}  vs  {ShortLabel(labelB)}"));
        sb.AppendLine(FormattableString.Invariant(
            $"  mean:   {r.MeanA:0.0} vs {r.MeanB:0.0} m/s, diff {r.MeanDiffMps:+0.0;-0.0} m/s (p={r.TPValue:0.000}) -> ") +
            (r.MeansDiffer ? "different, not chrono noise" : "not different — could be chrono noise"));
        sb.AppendLine(FormattableString.Invariant(
            $"  spread: SD {r.SdA:0.0} vs {r.SdB:0.0} m/s (p={r.FPValue:0.000}) -> ") +
            (r.SpreadsDiffer ? "one load is meaningfully more consistent" : "no evidence either is more consistent"));
        return sb.ToString();
    }

    /// <summary>The bit before " (" — e.g. "39.2 gr" out of "39.2 gr (Athlon Ladder 2026-09-10)".</summary>
    private static string ShortLabel(string label)
    {
        int i = label.IndexOf(" (", StringComparison.Ordinal);
        return i > 0 ? label[..i] : label;
    }
}
