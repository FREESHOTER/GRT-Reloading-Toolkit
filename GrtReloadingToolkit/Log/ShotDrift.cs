using System.Globalization;
using System.Text.RegularExpressions;
using GrtPluginKit.Analysis;

namespace GrtReloadingToolkit.Log;

/// <summary>
/// Does the velocity move along the string, and does it depend on how quickly the shots were fired.
/// </summary>
/// <param name="N">Shots in the string.</param>
/// <param name="SlopePerShot">Least-squares change of velocity per shot, m/s, and its standard error.</param>
/// <param name="PValue">Two-sided p of the slope against zero (Student t, n-2 degrees of freedom).</param>
/// <param name="TotalChange">The slope over the whole string, m/s: the drift from the first shot to the last.</param>
/// <param name="SdMps">Sample SD of the string.</param>
/// <param name="ResidualSdMps">The SD that is left once that straight drift is taken out.</param>
/// <param name="IntervalCount">Shots that have a pause before them (needs clock times).</param>
/// <param name="MedianPauseS">The middle pause between consecutive shots, seconds.</param>
/// <param name="IntervalR">Pearson r of velocity against the pause before the shot, and its p.</param>
public sealed record ShotDriftReport(
    int N, double SlopePerShot, double SlopeSe, double PValue, double TotalChange, double SdMps, double ResidualSdMps,
    int IntervalCount, double? MedianPauseS, double? MinPauseS, double? MaxPauseS, double? IntervalR, double? IntervalP)
{
    public bool DriftSignificant => PValue < ShotDrift.Alpha;
    public bool IntervalSignificant => IntervalP is < ShotDrift.Alpha;
}

/// <summary>
/// The straight-line drift of velocity along a string, with a real test, and the dependence on the pause before each shot.
///
/// A barrel that heats as the string goes on, powder that warms in the sun, and fouling all move velocity along the string, and
/// the order of the shots cannot tell them apart: the tool reports the drift and how much of the SD it explains, and leaves the
/// cause to the shooter. The pause before each shot is a second look: if shots after a short pause differ from the rest, the
/// rate of fire matters, which is what a hot barrel does. The old Ballistic Lab looked only at the pause, and a string can drift
/// clearly with no link to the pause at all (a measured 40-shot string here: drift +5.8 m/s, p 0.01, pause r 0.006).
///
/// Below <see cref="MinShots"/> shots nothing is claimed: a trend through a handful of points is mostly luck.
/// </summary>
public static class ShotDrift
{
    public const int MinShots = 8;
    public const double Alpha = 0.05;

    private static readonly Regex TimeToken = new(@"TIME=\s*(\d{1,2}):(\d{2}):(\d{2})(?:\s*([AaPp])[Mm])?", RegexOptions.Compiled);

    /// <summary>
    /// The clock time of a shot, in seconds from midnight, from the note the Athlon import writes ("TIME=10:18:34 AM"); null when the
    /// note has none. A 24-hour time (no AM/PM) is read as it stands.
    /// </summary>
    public static double? ClockSeconds(string? note)
    {
        if (string.IsNullOrEmpty(note)) return null;
        var m = TimeToken.Match(note);
        if (!m.Success) return null;
        int h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int mi = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        int s = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        if (mi > 59 || s > 59) return null;
        if (m.Groups[4].Success)
        {
            if (h < 1 || h > 12) return null;
            bool pm = char.ToUpperInvariant(m.Groups[4].Value[0]) == 'P';
            h = h % 12 + (pm ? 12 : 0);
        }
        else if (h > 23) return null;
        return h * 3600 + mi * 60 + s;
    }

    /// <summary>
    /// Null below <see cref="MinShots"/> shots or when the velocities do not vary. <paramref name="clockSeconds"/> is one entry per shot
    /// (null where a shot has no time); the pauses use consecutive shots that both have one, a clock that runs past midnight is carried over.
    /// </summary>
    public static ShotDriftReport? Analyze(IReadOnlyList<double> velocities, IReadOnlyList<double?>? clockSeconds = null)
    {
        int n = velocities.Count;
        if (n < MinShots) return null;

        var x = Enumerable.Range(1, n).Select(i => (double)i).ToArray();
        var y = velocities.ToArray();
        if (!Fit(x, y, out double slope, out double se, out double resSd, out double p)) return null;

        double mean = y.Average();
        double sd = Math.Sqrt(y.Sum(v => (v - mean) * (v - mean)) / (n - 1));

        int count = 0;
        double? median = null, min = null, max = null, r = null, rp = null;
        if (clockSeconds is { Count: > 0 } && clockSeconds.Count == n)
        {
            var pauses = new List<double>(); var pv = new List<double>();
            double offset = 0;
            double? prev = null;
            for (int i = 0; i < n; i++)
            {
                if (clockSeconds[i] is not { } t) { prev = null; continue; }
                double now = t + offset;
                if (prev is { } pr)
                {
                    if (now < pr) { offset += 86400; now += 86400; }       // past midnight
                    pauses.Add(now - pr); pv.Add(y[i]);
                }
                prev = now;
            }
            count = pauses.Count;
            if (count > 0)
            {
                var sorted = pauses.OrderBy(a => a).ToList();
                median = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
                min = sorted[0]; max = sorted[^1];
            }
            if (count >= MinShots && Pearson(pauses.ToArray(), pv.ToArray(), out double rr))
            {
                r = rr;
                double dof = count - 2, t = rr * Math.Sqrt(dof / Math.Max(1 - rr * rr, 1e-12));
                rp = 2 * (1 - StudentT.Cdf(Math.Abs(t), dof));
            }
        }
        return new ShotDriftReport(n, slope, se, p, slope * (n - 1), sd, resSd, count, median, min, max, r, rp);
    }

    /// <summary>Ordinary least squares of y on x; the p is two-sided against a zero slope. False when x does not vary or the fit is exact.</summary>
    internal static bool Fit(double[] x, double[] y, out double slope, out double se, out double residualSd, out double p)
    {
        slope = se = residualSd = 0; p = 1;
        int n = x.Length;
        double mx = x.Average(), my = y.Average();
        double sxx = x.Sum(a => (a - mx) * (a - mx));
        if (sxx <= 0) return false;
        double sxy = 0; for (int i = 0; i < n; i++) sxy += (x[i] - mx) * (y[i] - my);
        slope = sxy / sxx;
        double intercept = my - slope * mx, sse = 0;
        for (int i = 0; i < n; i++) { double e = y[i] - (intercept + slope * x[i]); sse += e * e; }
        residualSd = Math.Sqrt(sse / (n - 2));
        se = residualSd / Math.Sqrt(sxx);
        if (se <= 1e-12) { p = slope == 0 ? 1 : 0; return true; }
        double t = slope / se;
        p = 2 * (1 - StudentT.Cdf(Math.Abs(t), n - 2));
        return true;
    }

    private static bool Pearson(double[] a, double[] b, out double r)
    {
        r = 0;
        double ma = a.Average(), mb = b.Average(), saa = 0, sbb = 0, sab = 0;
        for (int i = 0; i < a.Length; i++) { saa += (a[i] - ma) * (a[i] - ma); sbb += (b[i] - mb) * (b[i] - mb); sab += (a[i] - ma) * (b[i] - mb); }
        if (saa <= 0 || sbb <= 0) return false;
        r = sab / Math.Sqrt(saa * sbb);
        return true;
    }
}
