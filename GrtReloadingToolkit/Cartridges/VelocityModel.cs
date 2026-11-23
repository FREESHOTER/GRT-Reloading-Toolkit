using GrtPluginKit.Analysis;

namespace GrtReloadingToolkit.Cartridges;

/// <summary>One chronographed shot of a series, with the measures of the cartridge it was fired from (any of them may be missing).</summary>
public sealed record ShotSample(int SeriesId, double VelocityMps, IReadOnlyDictionary<string, double> Measures);

/// <summary>One measure tested against velocity, inside the series.</summary>
public sealed class ModelTerm
{
    public required string Key { get; init; }
    /// <summary>Shots that carry this measure.</summary>
    public int N { get; init; }
    /// <summary>Series those shots come from.</summary>
    public int Series { get; init; }
    public double R { get; init; }
    public double P { get; init; }
    /// <summary>m/s per unit of the measure. Once several measures stand together it is the one of the joint fit.</summary>
    public double Slope { get; set; }
    public double Se { get; set; }
    /// <summary>The spread of the measure inside the series (square root of the sum of squares over the degrees of freedom).</summary>
    public double SdX { get; init; }
    /// <summary>The SD this measure puts into the velocity: |slope| · SdX.</summary>
    public double SdFrom { get; set; }
    public double Min { get; init; }
    public double Max { get; init; }
    /// <summary>The p of the Holm step-down correction over every measure tested.</summary>
    public double PAdj { get; set; }
    public bool Significant { get; set; }
    /// <summary>Share of the explained variance (joint fit only).</summary>
    public double SsFraction { get; set; }
}

/// <summary>Each shot estimated with the sensitivities computed WITHOUT it: how far the model can be trusted on cartridges it has not seen.</summary>
public sealed record LeaveOneOutCheck(double R, int N, double SdWithout, double SdWith);

public sealed class VelocityModelResult
{
    public required IReadOnlyList<ModelTerm> Tested { get; init; }
    public required IReadOnlyList<ModelTerm> Significant { get; init; }
    public int NShots { get; init; }
    public int NSeries { get; init; }
    /// <summary>The SD inside the series, with the series means taken out.</summary>
    public double SdWithin { get; init; }
    public double R2 { get; init; }
    /// <summary>What is left of that SD after the proven measures: the part the model does not explain (primer, barrel, what was not measured).</summary>
    public double SdResidual { get; init; }
    public LeaveOneOutCheck? LeaveOneOut { get; init; }
}

/// <summary>
/// Which measures of a cartridge really move its velocity. The sensitivities (m/s per unit of each measure) are learned from the shots
/// of the archive, always INSIDE each series (the series means are taken out first, so a warmer day or another barrel cannot pass for an
/// effect of the case). Each measure is tested alone against the velocity, the p values are corrected for the number of measures tried
/// (Holm), and only the measures that survive enter the model, fitted together so that two measures that carry the same effect are not
/// counted twice. A port of the model of "Analisi DS", checked against it on the same data.
/// </summary>
public static class VelocityModel
{
    /// <summary>Fewer shots than this carrying a measure and it is not tested at all.</summary>
    public const int MinShotsPerMeasure = 8;
    public const double Alpha = 0.05;
    /// <summary>The leave-one-out check needs at least this many shots.</summary>
    public const int MinShotsForLeaveOneOut = 10;

    public static VelocityModelResult? Learn(IReadOnlyList<ShotSample> shots)
    {
        var data = shots.Where(s => double.IsFinite(s.VelocityMps)).ToList();
        if (data.Count == 0) return null;

        var keys = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in data)
            foreach (string k in s.Measures.Keys)
                if (seenKeys.Add(k)) keys.Add(k);

        // ---- each measure alone, inside the series -------------------------------------------------------------
        var tested = new List<ModelTerm>();
        foreach (string key in keys)
        {
            var sub = data.Where(s => s.Measures.TryGetValue(key, out double v) && double.IsFinite(v)).ToList();
            if (sub.Count < MinShotsPerMeasure) continue;
            double[] x = CenterBySeries(sub, s => s.Measures[key]);
            double[] y = CenterBySeries(sub, s => s.VelocityMps);
            if (Sxx(x) <= 1e-24) continue;                                  // a measure that never varies says nothing
            var reg = SimpleRegression(x, y);
            int series = sub.Select(s => s.SeriesId).Distinct().Count();
            int dfree = Math.Max(1, sub.Count - series);
            double sdX = Math.Sqrt(x.Sum(v => v * v) / dfree);
            tested.Add(new ModelTerm
            {
                Key = key, N = sub.Count, Series = series, R = reg.R, P = reg.P, Slope = reg.Slope, Se = reg.Se,
                SdX = sdX, SdFrom = Math.Abs(reg.Slope) * sdX,
                Min = sub.Min(s => s.Measures[key]), Max = sub.Max(s => s.Measures[key]),
            });
        }
        if (tested.Count == 0) return null;

        // ---- Holm correction over every measure tried -----------------------------------------------------------
        tested = tested.OrderBy(t => t.P).ToList();                          // stable: ties keep the order the measures came in
        int m = tested.Count;
        double run = 0;
        for (int i = 0; i < m; i++)
        {
            run = Math.Max(run, Math.Min(1.0, (m - i) * tested[i].P));
            tested[i].PAdj = run;
            tested[i].Significant = run < Alpha;
        }
        var sig = tested.Where(t => t.Significant).ToList();

        int nSeries = data.Select(s => s.SeriesId).Distinct().Count();
        double[] dv = CenterBySeries(data, s => s.VelocityMps);
        double sdWithin = Math.Sqrt(dv.Sum(v => v * v) / Math.Max(1, data.Count - nSeries));

        double r2 = 0, sdResid = sdWithin;
        LeaveOneOutCheck? loo = null;
        if (sig.Count > 0)
        {
            var sub = data.Where(s => sig.All(t => s.Measures.TryGetValue(t.Key, out double v) && double.IsFinite(v))).ToList();
            double[][] cols = sig.Select(t => CenterBySeries(sub, s => s.Measures[t.Key])).ToArray();
            double[] y = CenterBySeries(sub, s => s.VelocityMps);
            int n = sub.Count, k = sig.Count;
            var X = new double[n][];                                         // a constant, then the centred measures
            for (int i = 0; i < n; i++) { X[i] = new double[k + 1]; X[i][0] = 1; for (int j = 0; j < k; j++) X[i][j + 1] = cols[j][i]; }

            var fit = Ols(X, y);
            if (fit is not null)
            {
                double ybar = y.Average();
                double sst = y.Sum(v => (v - ybar) * (v - ybar));
                double sse = fit.Residuals.Sum(v => v * v);
                r2 = sst > 0 ? 1 - sse / sst : 0;
                if (k > 1)
                {
                    double sigma2 = sse / Math.Max(1, n - k - 1);            // statsmodels' own degrees of freedom
                    for (int j = 0; j < k; j++)
                    {
                        sig[j].Slope = fit.Beta[j + 1];
                        sig[j].Se = Math.Sqrt(Math.Max(0, sigma2 * fit.XtXInverse[j + 1][j + 1]));
                        sig[j].SdFrom = Math.Abs(sig[j].Slope) * sig[j].SdX;
                    }
                }
                double tot = sig.Sum(t => t.SdFrom * t.SdFrom);
                foreach (var t in sig) t.SsFraction = (tot > 0 ? t.SdFrom * t.SdFrom / tot : 0) * r2;
                int seriesSub = sub.Select(s => s.SeriesId).Distinct().Count();
                int dfree = Math.Max(1, n - seriesSub - k);
                sdResid = Math.Sqrt(sse / dfree);

                if (n >= MinShotsForLeaveOneOut)
                {
                    var pred = new double[n];
                    bool ok = true;
                    for (int i = 0; i < n && ok; i++)
                    {
                        var Xi = new List<double[]>(n - 1); var yi = new List<double>(n - 1);
                        for (int r = 0; r < n; r++) if (r != i) { Xi.Add(X[r]); yi.Add(y[r]); }
                        var f = Ols(Xi.ToArray(), yi.ToArray());
                        if (f is null) { ok = false; break; }
                        double p = 0; for (int c = 0; c <= k; c++) p += X[i][c] * f.Beta[c];
                        pred[i] = p;
                    }
                    if (ok)
                    {
                        double rLoo = Pearson(pred, y) ?? 0;
                        int dfLoo = Math.Max(1, n - seriesSub);
                        loo = new LeaveOneOutCheck(rLoo, n,
                            Math.Sqrt(y.Sum(v => v * v) / dfLoo),
                            Math.Sqrt(Enumerable.Range(0, n).Sum(i => (y[i] - pred[i]) * (y[i] - pred[i])) / dfLoo));
                    }
                }
            }
        }

        return new VelocityModelResult
        {
            Tested = tested, Significant = sig, NShots = data.Count, NSeries = nSeries,
            SdWithin = sdWithin, R2 = r2, SdResidual = sdResid, LeaveOneOut = loo,
        };
    }

    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Values with the mean of their own series taken out.</summary>
    internal static double[] CenterBySeries(IReadOnlyList<ShotSample> rows, Func<ShotSample, double> value)
    {
        var sum = new Dictionary<int, (double Sum, int N)>();
        foreach (var r in rows)
        {
            sum.TryGetValue(r.SeriesId, out var a);
            sum[r.SeriesId] = (a.Sum + value(r), a.N + 1);
        }
        var result = new double[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var a = sum[rows[i].SeriesId];
            result[i] = value(rows[i]) - a.Sum / a.N;
        }
        return result;
    }

    private static double Sxx(double[] x)
    {
        double m = x.Average();
        return x.Sum(v => (v - m) * (v - m));
    }

    internal readonly record struct Reg(double Slope, double Se, double R, double P);

    /// <summary>Ordinary least squares of y on x with an intercept: the slope, its standard error, r and the two-sided p (n−2 degrees of freedom).</summary>
    internal static Reg SimpleRegression(double[] x, double[] y)
    {
        int n = x.Length;
        double mx = x.Average(), my = y.Average();
        double sxx = 0, syy = 0, sxy = 0;
        for (int i = 0; i < n; i++) { double dx = x[i] - mx, dy = y[i] - my; sxx += dx * dx; syy += dy * dy; sxy += dx * dy; }
        if (sxx <= 0 || syy <= 0) return new Reg(0, 0, 0, 1);
        double slope = sxy / sxx;
        double r = Math.Clamp(sxy / Math.Sqrt(sxx * syy), -1, 1);
        int df = n - 2;
        if (df <= 0) return new Reg(slope, 0, r, 1);
        double se = Math.Sqrt(Math.Max(0, (syy - slope * sxy) / df) / sxx);
        double p;
        if (Math.Abs(r) >= 1) p = 0;
        else
        {
            double t = r * Math.Sqrt(df / ((1 - r) * (1 + r)));
            p = 2 * (1 - StudentT.Cdf(Math.Abs(t), df));
        }
        return new Reg(slope, se, r, Math.Clamp(p, 0, 1));
    }

    internal static double? Pearson(double[] a, double[] b)
    {
        int n = a.Length;
        double ma = a.Average(), mb = b.Average();
        double saa = 0, sbb = 0, sab = 0;
        for (int i = 0; i < n; i++) { double da = a[i] - ma, db = b[i] - mb; saa += da * da; sbb += db * db; sab += da * db; }
        if (saa <= 0 || sbb <= 0) return null;
        return sab / Math.Sqrt(saa * sbb);
    }

    internal sealed record OlsFit(double[] Beta, double[] Residuals, double[][] XtXInverse);

    /// <summary>Least squares by the normal equations (Gauss-Jordan with partial pivoting): fine for the handful of centred columns used here. Null when the columns are dependent.</summary>
    internal static OlsFit? Ols(double[][] X, double[] y)
    {
        int n = X.Length, p = X[0].Length;
        var a = new double[p, 2 * p];
        for (int i = 0; i < p; i++)
            for (int j = 0; j < p; j++)
            {
                double s = 0;
                for (int r = 0; r < n; r++) s += X[r][i] * X[r][j];
                a[i, j] = s;
                a[i, p + j] = i == j ? 1 : 0;
            }
        for (int c = 0; c < p; c++)
        {
            int piv = c;
            for (int r = c + 1; r < p; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
            if (Math.Abs(a[piv, c]) < 1e-12) return null;
            if (piv != c) for (int j = 0; j < 2 * p; j++) (a[c, j], a[piv, j]) = (a[piv, j], a[c, j]);
            double d = a[c, c];
            for (int j = 0; j < 2 * p; j++) a[c, j] /= d;
            for (int r = 0; r < p; r++)
            {
                if (r == c) continue;
                double f = a[r, c];
                if (f == 0) continue;
                for (int j = 0; j < 2 * p; j++) a[r, j] -= f * a[c, j];
            }
        }
        var inv = new double[p][];
        for (int i = 0; i < p; i++) { inv[i] = new double[p]; for (int j = 0; j < p; j++) inv[i][j] = a[i, p + j]; }
        var xty = new double[p];
        for (int i = 0; i < p; i++) { double s = 0; for (int r = 0; r < n; r++) s += X[r][i] * y[r]; xty[i] = s; }
        var beta = new double[p];
        for (int i = 0; i < p; i++) { double s = 0; for (int j = 0; j < p; j++) s += inv[i][j] * xty[j]; beta[i] = s; }
        var res = new double[n];
        for (int r = 0; r < n; r++) { double fitted = 0; for (int j = 0; j < p; j++) fitted += X[r][j] * beta[j]; res[r] = y[r] - fitted; }
        return new OlsFit(beta, res, inv);
    }
}
