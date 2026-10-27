using GrtPluginKit.Analysis;

namespace GrtReloadingToolkit.Log;

/// <summary>One outing with a single load: what was shot, the conditions, and what was different from the time before.
/// Velocities are in m/s, one per shot; everything else is optional because nobody writes down all of it every time.</summary>
public sealed record FollowSession(
    DateTime Date,
    double? TempC,
    IReadOnlyList<double> VelocitiesMps,
    double? GroupMoa = null,
    double? DistanceM = null,
    string? PowderLot = null,
    string? PrimerLot = null,
    double? SeatingMm = null);

public sealed record SessionStat(FollowSession Session, StringStats Stats);

/// <summary>A fitted effect with its uncertainty: the estimate, its standard error and a 95 % interval. It is called significant
/// only when that interval leaves out zero, which with a handful of sessions it often will not: that is a statement about the
/// data, not about the load.</summary>
public sealed record LinearEffect(double Estimate, double StdError, double CiLow, double CiHigh, int Dof)
{
    public bool Significant => CiLow > 0 || CiHigh < 0;
}

public sealed record TemperatureEffect(LinearEffect MpsPerC, double SpanC, int Sessions, bool TimeAccountedFor);

public sealed record DriftEffect(LinearEffect MpsPer30Days, double SpanDays, int Sessions, bool TemperatureAccountedFor);

public enum FollowSignal { Normal, Warning, Out }

/// <summary>Is the latest outing normal for this load? Judged against the sessions before it, never against itself.</summary>
public sealed record LatestCheck(
    FollowSignal Mean, double MeanScore, bool TemperatureAdjusted, double? ExpectedMps, double ObservedMps,
    FollowSignal Spread, double? SdRatio, double? SpreadP, int BaselineSessions);

/// <summary>What differs from the previous outing: a lot, the seating depth. Not the brass's firing count, which changes every time by design.</summary>
public sealed record SessionChange(int Index, IReadOnlyList<string> Changes);

/// <summary>A difference of temperature in degrees Celsius inside a message's arguments: the report writes it in the unit GRT shows
/// (a difference, not a temperature, so Fahrenheit has no offset to add).</summary>
public readonly record struct TempDelta(double Celsius);

public sealed record FollowUpNotEvaluated(string Template, object[] Args);

public sealed record FollowUpResult(
    IReadOnlyList<SessionStat> Sessions,
    TemperatureEffect? Temperature,
    DriftEffect? Drift,
    LatestCheck? Latest,
    IReadOnlyList<SessionChange> Changes,
    IReadOnlyList<FollowUpNotEvaluated> NotEvaluated);

/// <summary>
/// The analysis behind "follow a load": the sessions of ONE load, oldest first, and what they say about it over time. Pure logic,
/// no storage and no window. Every verdict needs a minimum of data and says so when it does not have it, rather than
/// answering with a number that looks like a result.
/// </summary>
public static class LoadFollowUp
{
    /// <summary>A session counts as a data point from this many shots: below it the mean says little and the SD nothing.</summary>
    public const int MinShots = 3;
    /// <summary>Sessions needed before a temperature or time effect is fitted.</summary>
    public const int MinSessionsForEffect = 4;
    /// <summary>Sessions needed before the latest one is compared with the rest (the control limits come from their spread).</summary>
    public const int MinBaseline = 4;
    public const double MinTempSpanC = 5;
    public const double MinDriftSpanDays = 7;
    /// <summary>Seating depth difference (mm) that counts as a change.</summary>
    public const double SeatingChangeMm = 0.05;
    /// <summary>The constant that turns the mean moving range of an individuals chart into a standard deviation (d2 for pairs).</summary>
    private const double D2 = 1.128;

    public static FollowUpResult Analyze(IEnumerable<FollowSession> sessions)
    {
        var ordered = sessions.OrderBy(s => s.Date).ToList();
        var all = ordered.Select(s => new SessionStat(s, StringStats.From(s.VelocitiesMps.Where(v => v > 0).ToList()))).ToList();
        var used = all.Where(s => s.Stats.N >= MinShots).ToList();
        var gaps = new List<FollowUpNotEvaluated>();

        var (temp, drift) = Effects(used, gaps);
        var latest = CheckLatest(used, gaps);
        return new FollowUpResult(all, temp, drift, latest, Changes(ordered), gaps);
    }

    // ---- temperature and time

    private static (TemperatureEffect?, DriftEffect?) Effects(List<SessionStat> used, List<FollowUpNotEvaluated> gaps)
    {
        double t0 = used.Count > 0 ? used[0].Session.Date.ToOADate() : 0;
        var withTemp = used.Where(s => s.Session.TempC is not null).ToList();
        double tempSpan = withTemp.Count == 0 ? 0 : withTemp.Max(s => s.Session.TempC!.Value) - withTemp.Min(s => s.Session.TempC!.Value);
        double daySpan = used.Count == 0 ? 0 : used[^1].Session.Date.ToOADate() - t0;

        bool tempOk = withTemp.Count >= MinSessionsForEffect && tempSpan >= MinTempSpanC;
        bool driftOk = used.Count >= MinSessionsForEffect && daySpan >= MinDriftSpanDays;
        if (!tempOk)
            gaps.Add(new FollowUpNotEvaluated("Temperature effect: not evaluated, it needs at least {0} sessions with a temperature written down, at least {1} apart (there are {2}, {3} apart).",
                new object[] { MinSessionsForEffect, new TempDelta(MinTempSpanC), withTemp.Count, new TempDelta(Math.Round(tempSpan, 1)) }));
        if (!driftOk)
            gaps.Add(new FollowUpNotEvaluated("Drift over time: not evaluated, it needs at least {0} sessions at least {1} days apart (there are {2}, {3} days apart).",
                new object[] { MinSessionsForEffect, MinDriftSpanDays, used.Count, Math.Round(daySpan, 0) }));

        TemperatureEffect? temp = null;
        DriftEffect? drift = null;

        // With both the temperature and the dates available the two are fitted together: a warm summer and a slow drift would
        // otherwise be taken for one another. Needs one more session than a single effect (three coefficients, two degrees of freedom).
        bool allTemp = used.Count == withTemp.Count;
        if (tempOk && driftOk && allTemp && used.Count >= MinSessionsForEffect + 1)
        {
            var fit = Ols(used.Select(s => new[] { 1.0, s.Session.TempC!.Value, s.Session.Date.ToOADate() - t0 }).ToList(), used.Select(s => s.Stats.Mean).ToList());
            if (fit is not null)
            {
                temp = new TemperatureEffect(Effect(fit.Value, 1), tempSpan, used.Count, true);
                var d = Effect(fit.Value, 2);
                drift = new DriftEffect(Scale(d, 30), daySpan, used.Count, true);
                return (temp, drift);
            }
        }
        if (tempOk)
        {
            var fit = Ols(withTemp.Select(s => new[] { 1.0, s.Session.TempC!.Value }).ToList(), withTemp.Select(s => s.Stats.Mean).ToList());
            if (fit is not null) temp = new TemperatureEffect(Effect(fit.Value, 1), tempSpan, withTemp.Count, false);
        }
        if (driftOk)
        {
            var fit = Ols(used.Select(s => new[] { 1.0, s.Session.Date.ToOADate() - t0 }).ToList(), used.Select(s => s.Stats.Mean).ToList());
            if (fit is not null) drift = new DriftEffect(Scale(Effect(fit.Value, 1), 30), daySpan, used.Count, false);
        }
        return (temp, drift);
    }

    private static LinearEffect Scale(LinearEffect e, double k) => new(e.Estimate * k, e.StdError * k, e.CiLow * k, e.CiHigh * k, e.Dof);

    private static LinearEffect Effect((double[] Beta, double[] Se, int Dof) fit, int i)
    {
        double t = StudentT.TwoTailedCritical(fit.Dof, 0.05);
        return new LinearEffect(fit.Beta[i], fit.Se[i], fit.Beta[i] - t * fit.Se[i], fit.Beta[i] + t * fit.Se[i], fit.Dof);
    }

    // ---- the latest session against the ones before it

    private static LatestCheck? CheckLatest(List<SessionStat> used, List<FollowUpNotEvaluated> gaps)
    {
        if (used.Count < MinBaseline + 1)
        {
            gaps.Add(new FollowUpNotEvaluated("Latest outing against the earlier ones: not evaluated, it needs at least {0} earlier sessions of {1} shots or more (there are {2}).",
                new object[] { MinBaseline, MinShots, Math.Max(0, used.Count - 1) }));
            return null;
        }
        var last = used[^1];
        var baseline = used.Take(used.Count - 1).ToList();

        // mean: against the temperature the baseline itself says matters, when it says so; otherwise against its plain spread
        FollowSignal meanSignal;
        double score;
        bool adjusted = false;
        double? expected = null;
        var tempFit = FitBaselineTemperature(baseline);
        if (tempFit is { } tf && last.Session.TempC is { } lt && tf.Effect.Significant)
        {
            adjusted = true;
            double pred = tf.Intercept + tf.Effect.Estimate * lt;
            expected = pred;
            double se = tf.ResidualSd * Math.Sqrt(1 + 1.0 / tf.K + Math.Pow(lt - tf.MeanT, 2) / tf.Sxx);
            score = se > 0 ? (last.Stats.Mean - pred) / se : 0;
            double warn = StudentT.TwoTailedCritical(tf.Effect.Dof, 0.05), outc = StudentT.TwoTailedCritical(tf.Effect.Dof, 0.0027);
            meanSignal = Math.Abs(score) > outc ? FollowSignal.Out : Math.Abs(score) > warn ? FollowSignal.Warning : FollowSignal.Normal;
        }
        else
        {
            var (centre, sigma) = MeanControl(baseline);
            expected = centre;
            score = sigma > 0 ? (last.Stats.Mean - centre) / sigma : 0;
            meanSignal = Math.Abs(score) > 3 ? FollowSignal.Out : Math.Abs(score) > 2 ? FollowSignal.Warning : FollowSignal.Normal;
        }

        // spread: the latest SD against the SD the earlier sessions had together; only a larger one is a problem
        double df = baseline.Sum(s => s.Stats.N - 1);
        double pooled = baseline.Sum(s => (s.Stats.N - 1) * s.Stats.Sd * s.Stats.Sd) / df;
        FollowSignal spread = FollowSignal.Normal;
        double? ratio = null, p = null;
        if (pooled > 0)
        {
            double f = last.Stats.Sd * last.Stats.Sd / pooled;
            ratio = f;
            p = FDist.TwoTailedPValue(f, last.Stats.N - 1, df);
            if (f > 1) spread = p < 0.01 ? FollowSignal.Out : p < 0.05 ? FollowSignal.Warning : FollowSignal.Normal;
        }
        return new LatestCheck(meanSignal, score, adjusted, expected, last.Stats.Mean, spread, ratio, p, baseline.Count);
    }

    /// <summary>
    /// The centre and the sigma of the individuals chart on the session means: the mean of the means, and the mean moving range
    /// over d2. The sigma never goes below the spread of one session's mean from shot noise alone (the typical SD over the square
    /// root of the typical shot count): a baseline whose means happen to sit very close together would otherwise have limits
    /// tighter than the noise of a single string, and flag a perfectly ordinary outing.
    /// </summary>
    public static (double Centre, double Sigma) MeanControl(IReadOnlyList<SessionStat> sessions)
    {
        double centre = sessions.Average(s => s.Stats.Mean);
        double mrBar = 0;
        for (int i = 1; i < sessions.Count; i++) mrBar += Math.Abs(sessions[i].Stats.Mean - sessions[i - 1].Stats.Mean);
        mrBar = sessions.Count > 1 ? mrBar / (sessions.Count - 1) : 0;
        var sds = sessions.Select(s => s.Stats.Sd).OrderBy(x => x).ToList();
        var ns = sessions.Select(s => s.Stats.N).OrderBy(x => x).ToList();
        double floor = sds[sds.Count / 2] / Math.Sqrt(Math.Max(1, ns[ns.Count / 2]));
        return (centre, Math.Max(mrBar / D2, floor));
    }

    private readonly record struct BaselineFit(double Intercept, LinearEffect Effect, double ResidualSd, int K, double MeanT, double Sxx);

    private static BaselineFit? FitBaselineTemperature(List<SessionStat> baseline)
    {
        var w = baseline.Where(s => s.Session.TempC is not null).ToList();
        if (w.Count < MinSessionsForEffect) return null;
        double span = w.Max(s => s.Session.TempC!.Value) - w.Min(s => s.Session.TempC!.Value);
        if (span < MinTempSpanC) return null;
        var fit = Ols(w.Select(s => new[] { 1.0, s.Session.TempC!.Value }).ToList(), w.Select(s => s.Stats.Mean).ToList());
        if (fit is null) return null;
        double meanT = w.Average(s => s.Session.TempC!.Value);
        double sxx = w.Sum(s => Math.Pow(s.Session.TempC!.Value - meanT, 2));
        double sse = w.Sum(s => Math.Pow(s.Stats.Mean - (fit.Value.Beta[0] + fit.Value.Beta[1] * s.Session.TempC!.Value), 2));
        return new BaselineFit(fit.Value.Beta[0], Effect(fit.Value, 1), Math.Sqrt(sse / fit.Value.Dof), w.Count, meanT, sxx);
    }

    // ---- what changed

    private static List<SessionChange> Changes(List<FollowSession> ordered)
    {
        var list = new List<SessionChange>();
        for (int i = 1; i < ordered.Count; i++)
        {
            var a = ordered[i - 1]; var b = ordered[i];
            var c = new List<string>();
            if (Differs(a.PowderLot, b.PowderLot)) c.Add("powder lot");
            if (Differs(a.PrimerLot, b.PrimerLot)) c.Add("primer lot");
            if (a.SeatingMm is { } sa && b.SeatingMm is { } sb && Math.Abs(sa - sb) >= SeatingChangeMm - 1e-9) c.Add("seating depth");
            if (c.Count > 0) list.Add(new SessionChange(i, c));
        }
        return list;
    }

    private static bool Differs(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && !string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    // ---- least squares for up to three coefficients

    /// <summary>Ordinary least squares by the normal equations: coefficients, their standard errors and the degrees of freedom;
    /// null when there are no degrees of freedom or the predictors are collinear.</summary>
    internal static (double[] Beta, double[] Se, int Dof)? Ols(IReadOnlyList<double[]> x, IReadOnlyList<double> y)
    {
        int n = x.Count, p = x[0].Length, dof = n - p;
        if (dof < 1) return null;
        var xtx = new double[p, p]; var xty = new double[p];
        for (int r = 0; r < n; r++)
            for (int i = 0; i < p; i++)
            {
                xty[i] += x[r][i] * y[r];
                for (int j = 0; j < p; j++) xtx[i, j] += x[r][i] * x[r][j];
            }
        var inv = Invert(xtx, p);
        if (inv is null) return null;
        var beta = new double[p];
        for (int i = 0; i < p; i++) for (int j = 0; j < p; j++) beta[i] += inv[i, j] * xty[j];
        double sse = 0;
        for (int r = 0; r < n; r++)
        {
            double fit = 0;
            for (int i = 0; i < p; i++) fit += beta[i] * x[r][i];
            sse += (y[r] - fit) * (y[r] - fit);
        }
        double s2 = sse / dof;
        var se = new double[p];
        for (int i = 0; i < p; i++) se[i] = Math.Sqrt(Math.Max(0, s2 * inv[i, i]));
        return (beta, se, dof);
    }

    private static double[,]? Invert(double[,] m, int n)
    {
        var a = new double[n, 2 * n];
        for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) a[i, j] = m[i, j]; a[i, n + i] = 1; }
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
            if (Math.Abs(a[piv, c]) < 1e-12) return null;
            if (piv != c) for (int j = 0; j < 2 * n; j++) (a[c, j], a[piv, j]) = (a[piv, j], a[c, j]);
            double d = a[c, c];
            for (int j = 0; j < 2 * n; j++) a[c, j] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == c) continue;
                double f = a[r, c];
                if (f == 0) continue;
                for (int j = 0; j < 2 * n; j++) a[r, j] -= f * a[c, j];
            }
        }
        var inv = new double[n, n];
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) inv[i, j] = a[i, n + j];
        return inv;
    }
}
