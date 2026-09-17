namespace GrtPluginKit.Analysis;

/// <summary>
/// Special functions behind the Student-t and F distributions: no external package, just
/// log-gamma (Lanczos), the regularized incomplete beta function (continued fraction — the
/// classic Numerical Recipes <c>betacf</c>/<c>betai</c>), and an erf approximation
/// (Abramowitz &amp; Stegun 7.1.26, accurate to about 1.5e-7). Used by <see cref="StudentT"/> and
/// <see cref="FDist"/> for confidence intervals, sample-size planning and comparing two
/// chronograph strings — none of which GRT computes on its own.
/// </summary>
public static class StatsMath
{
    public static double LogGamma(double x)
    {
        double[] cof =
        {
            76.18009172947146, -86.50532032941677, 24.01409824083091,
            -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5,
        };
        double y = x, tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        double ser = 1.000000000190015;
        for (int j = 0; j < 6; j++) { y += 1; ser += cof[j] / y; }
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }

    /// <summary>Error function via Abramowitz &amp; Stegun 7.1.26.</summary>
    public static double Erf(double x)
    {
        double sign = x < 0 ? -1 : 1;
        x = Math.Abs(x);
        const double a1 = 0.254829592, a2 = -0.284496736, a3 = 1.421413741, a4 = -1.453152027, a5 = 1.061405429, p = 0.3275911;
        double t = 1.0 / (1.0 + p * x);
        double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
        return sign * y;
    }

    /// <summary>Standard normal CDF, &#934;(z).</summary>
    public static double NormalCdf(double z) => 0.5 * (1 + Erf(z / Math.Sqrt(2)));

    /// <summary>Regularized incomplete beta function I_x(a,b), 0&#8804;x&#8804;1.</summary>
    public static double BetaInc(double x, double a, double b)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double bt = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
        return x < (a + 1) / (a + b + 2)
            ? bt * BetaCf(a, b, x) / a
            : 1 - bt * BetaCf(b, a, 1 - x) / b;
    }

    private static double BetaCf(double a, double b, double x)
    {
        const int maxIt = 200;
        const double eps = 3e-12, fpMin = 1e-300;
        double qab = a + b, qap = a + 1, qam = a - 1;
        double c = 1, d = 1 - qab * x / qap;
        if (Math.Abs(d) < fpMin) d = fpMin;
        d = 1 / d;
        double h = d;
        for (int m = 1; m <= maxIt; m++)
        {
            int m2 = 2 * m;
            double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d; if (Math.Abs(d) < fpMin) d = fpMin;
            c = 1 + aa / c; if (Math.Abs(c) < fpMin) c = fpMin;
            d = 1 / d; h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d; if (Math.Abs(d) < fpMin) d = fpMin;
            c = 1 + aa / c; if (Math.Abs(c) < fpMin) c = fpMin;
            d = 1 / d;
            double del = d * c; h *= del;
            if (Math.Abs(del - 1) < eps) break;
        }
        return h;
    }
}

/// <summary>Student's t distribution — confidence intervals and hypothesis tests on small samples.</summary>
public static class StudentT
{
    /// <summary>P(T &#8804; t) for t ~ Student-t(dof).</summary>
    public static double Cdf(double t, double dof)
    {
        double x = dof / (dof + t * t);
        double ib = StatsMath.BetaInc(x, dof / 2, 0.5);
        return t >= 0 ? 1 - 0.5 * ib : 0.5 * ib;
    }

    /// <summary>The t* such that P(|T| &gt; t*) = alpha (e.g. alpha=0.05 for a 95% CI), found by
    /// bisection — simpler and just as robust as an inverse-beta formula for our purposes.</summary>
    public static double Quantile(double p, double dof)
    {
        double lo = -1000, hi = 1000;
        for (int i = 0; i < 100; i++)
        {
            double mid = (lo + hi) / 2;
            if (Cdf(mid, dof) < p) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    public static double TwoTailedCritical(double dof, double alpha) => Quantile(1 - alpha / 2, dof);
}

/// <summary>F distribution — comparing the spread (SD) of two chronograph strings.</summary>
public static class FDist
{
    /// <summary>P(F &#8804; f) for F ~ F(d1,d2).</summary>
    public static double Cdf(double f, double d1, double d2)
    {
        if (f <= 0) return 0;
        double x = d1 * f / (d1 * f + d2);
        return StatsMath.BetaInc(x, d1 / 2, d2 / 2);
    }

    /// <summary>Two-tailed p-value for the variance ratio <paramref name="f"/> = var1/var2.</summary>
    public static double TwoTailedPValue(double f, double d1, double d2)
    {
        double p = Cdf(f, d1, d2);
        return 2 * Math.Min(p, 1 - p);
    }
}
