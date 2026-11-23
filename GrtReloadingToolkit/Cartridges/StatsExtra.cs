using GrtPluginKit.Analysis;

namespace GrtReloadingToolkit.Cartridges;

/// <summary>The few distributions the variance budget needs beyond what <see cref="StatsMath"/> has: chi-square (the interval of an SD) and the F quantile.</summary>
public static class StatsExtra
{
    /// <summary>Regularised lower incomplete gamma function P(a, x): series below a+1, continued fraction above.</summary>
    public static double GammaP(double a, double x)
    {
        if (x <= 0) return 0;
        double lg = StatsMath.LogGamma(a);
        if (x < a + 1)
        {
            double sum = 1 / a, term = sum;
            for (int n = 1; n < 1000; n++)
            {
                term *= x / (a + n);
                sum += term;
                if (Math.Abs(term) < Math.Abs(sum) * 1e-16) break;
            }
            return sum * Math.Exp(-x + a * Math.Log(x) - lg);
        }
        // Lentz's continued fraction for Q(a, x)
        const double tiny = 1e-300;
        double b = x + 1 - a, c = 1 / tiny, d = 1 / b, h = d;
        for (int i = 1; i < 1000; i++)
        {
            double an = -i * (i - a);
            b += 2;
            d = an * d + b; if (Math.Abs(d) < tiny) d = tiny;
            c = b + an / c; if (Math.Abs(c) < tiny) c = tiny;
            d = 1 / d;
            double del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < 1e-16) break;
        }
        return 1 - Math.Exp(-x + a * Math.Log(x) - lg) * h;
    }

    public static double ChiSquareCdf(double x, double dof) => GammaP(dof / 2, x / 2);

    /// <summary>The x with P(χ² ≤ x) = p, by bisection (the cdf is monotone).</summary>
    public static double ChiSquareQuantile(double p, double dof)
    {
        if (p <= 0) return 0;
        if (p >= 1) return double.PositiveInfinity;
        double lo = 0, hi = Math.Max(10, dof * 4);
        while (ChiSquareCdf(hi, dof) < p) hi *= 2;
        for (int i = 0; i < 200; i++)
        {
            double mid = (lo + hi) / 2;
            if (ChiSquareCdf(mid, dof) < p) lo = mid; else hi = mid;
            if (hi - lo <= 1e-14 * Math.Max(1, hi)) break;
        }
        return (lo + hi) / 2;
    }

    /// <summary>The x with P(F ≤ x) = p for d1 and d2 degrees of freedom, by bisection.</summary>
    public static double FQuantile(double p, double d1, double d2)
    {
        double lo = 0, hi = 10;
        while (FDist.Cdf(hi, d1, d2) < p) hi *= 2;
        for (int i = 0; i < 200; i++)
        {
            double mid = (lo + hi) / 2;
            if (FDist.Cdf(mid, d1, d2) < p) lo = mid; else hi = mid;
            if (hi - lo <= 1e-14 * Math.Max(1, hi)) break;
        }
        return (lo + hi) / 2;
    }

    /// <summary>The range the true SD lies in, given the SD of <paramref name="n"/> shots (level 0.90 = "with a 90 % probability"); null with fewer than 2 shots.</summary>
    public static (double Lo, double Hi)? SdInterval(double sd, int n, double level = 0.90)
    {
        if (n < 2) return null;
        int k = n - 1;
        double lo = sd * Math.Sqrt(k / ChiSquareQuantile(1 - (1 - level) / 2, k));
        double hi = sd * Math.Sqrt(k / ChiSquareQuantile((1 - level) / 2, k));
        return (lo, hi);
    }

    /// <summary>
    /// Shots per variant to show that one variant has a lower SD than the other (one-sided F test), at the given power; null when it takes more
    /// than 1000. Both variants are fired in the same session, alternating.
    /// </summary>
    public static int? ShotsForSdDifference(double sdSmall, double sdBig, double power = 0.8, double alpha = 0.05)
    {
        if (!(sdSmall > 0) || !(sdBig > 0)) return null;
        double ratio2 = (sdBig / sdSmall) * (sdBig / sdSmall);
        for (int n = 3; n <= 1000; n++)
        {
            double fc = FQuantile(1 - alpha, n - 1, n - 1);
            if (1 - FDist.Cdf(fc / ratio2, n - 1, n - 1) >= power) return n;
        }
        return null;
    }
}
