using GrtPluginKit.Analysis;

namespace GrtReloadingToolkit.Log;

/// <summary>What <see cref="HitProbability.Compute"/> found for one target at one distance.</summary>
/// <param name="HitPct">Chance (0-100) that one shot lands on the target, from the group's own spread.</param>
/// <param name="CautiousHitPct">The same with the spread read at its upper 90 % confidence bound, so a short group
/// does not flatter the rifle; null when the group is too small to bound.</param>
public sealed record HitProbabilityResult(
    double HitPct, double? CautiousHitPct, double SigmaMm, double R50Mm, double R90Mm, double R95Mm);

/// <summary>
/// The chance of putting a shot on a target of a given size, given how a real group spread. Impacts are taken as a
/// circular bivariate normal (the same assumption as the group's CEP50 and mean radius), so the distance from the
/// centre of the group follows a Rayleigh law; the spread is an angle, so it grows in proportion to the distance.
///
/// The group is a sample: five shots tell very little about the true spread, and a lucky group reads tighter than the
/// rifle really is. <see cref="UpperSigmaFactor"/> says by how much the spread could be larger and still be
/// consistent with the shots seen, and the result carries the probability at that cautious spread as well.
/// Not modelled: wind, drift, errors of range, and any correlation between the two axes.
/// </summary>
public static class HitProbability
{
    /// <summary>Millimetres in one minute of angle at one metre.</summary>
    public const double MmPerMoaPerMetre = 1000.0 * Math.PI / (180.0 * 60.0);

    public static double SigmaMm(double sigmaMoa, double distanceM) => sigmaMoa * distanceM * MmPerMoaPerMetre;

    /// <summary>Radius within which a fraction <paramref name="p"/> of the shots fall (Rayleigh quantile).</summary>
    public static double Quantile(double sigmaMm, double p) => sigmaMm * Math.Sqrt(-2.0 * Math.Log(1.0 - p));

    /// <summary>Chance of landing inside a circle of the given radius whose centre is (dx, dy) from the centre of the group.</summary>
    public static double Circle(double radiusMm, double sigmaMm, double dxMm = 0, double dyMm = 0)
    {
        if (radiusMm <= 0 || sigmaMm <= 0) return 0;
        double d = Math.Sqrt(dxMm * dxMm + dyMm * dyMm);
        if (d < 1e-9) return 1.0 - Math.Exp(-(radiusMm * radiusMm) / (2.0 * sigmaMm * sigmaMm));

        // Off-centre: the distance follows a Rice law, which has no closed form. Integrated numerically rather than by
        // sampling, so the same input always gives the same answer. exp(-(r²+d²)/2σ²)·I0(rd/σ²) is written as
        // exp(-(r-d)²/2σ²)·I0e(rd/σ²), with I0e the exponentially scaled Bessel function, so nothing overflows.
        const int steps = 2000;
        double h = radiusMm / steps, s2 = sigmaMm * sigmaMm;
        double F(double r) => r / s2 * Math.Exp(-(r - d) * (r - d) / (2 * s2)) * BesselI0Scaled(r * d / s2);
        double sum = F(0) + F(radiusMm);
        for (int i = 1; i < steps; i++) sum += F(i * h) * (i % 2 == 1 ? 4 : 2);
        return Math.Clamp(sum * h / 3.0, 0, 1);
    }

    /// <summary>Chance of landing inside a rectangle centred (dx, dy) from the centre of the group; the axes are independent.</summary>
    public static double Rectangle(double widthMm, double heightMm, double sigmaMm, double dxMm = 0, double dyMm = 0)
    {
        if (widthMm <= 0 || heightMm <= 0 || sigmaMm <= 0) return 0;
        double px = StatsMath.NormalCdf((widthMm / 2 + dxMm) / sigmaMm) - StatsMath.NormalCdf((-widthMm / 2 + dxMm) / sigmaMm);
        double py = StatsMath.NormalCdf((heightMm / 2 + dyMm) / sigmaMm) - StatsMath.NormalCdf((-heightMm / 2 + dyMm) / sigmaMm);
        return Math.Clamp(px * py, 0, 1);
    }

    /// <summary>
    /// By what factor the true spread may exceed the one measured, at one-sided 90 % confidence, for a group of
    /// <paramref name="impacts"/> shots: sqrt(ν / χ²₀.₁₀(ν)) with ν = 2(n-1) degrees of freedom (two axes, each centred on the
    /// group's own mean). The chi-square quantile is the Wilson-Hilferty approximation, good to a fraction of a percent here.
    /// Null for fewer than 3 shots, where the bound is meaningless.
    /// </summary>
    public static double? UpperSigmaFactor(int impacts)
    {
        if (impacts < 3) return null;
        double nu = 2.0 * (impacts - 1);
        const double z10 = -1.2815515655446004;     // 10th percentile of the standard normal
        double a = 2.0 / (9.0 * nu);
        double chi2 = nu * Math.Pow(1 - a + z10 * Math.Sqrt(a), 3);
        return Math.Sqrt(nu / chi2);
    }

    public enum Shape { Circle, Rectangle }

    /// <summary>
    /// <paramref name="sigmaMoa"/> is the group's spread in MOA, <paramref name="impacts"/> its shot count;
    /// <paramref name="sizeAMm"/> is the diameter of a circle or the width of a rectangle, <paramref name="sizeBMm"/> the height.
    /// <paramref name="offsetXMoa"/>/<paramref name="offsetYMoa"/> is how far the centre of the group sits from the centre of the target.
    /// </summary>
    public static HitProbabilityResult Compute(
        double sigmaMoa, int impacts, double distanceM, Shape shape, double sizeAMm, double sizeBMm,
        double offsetXMoa = 0, double offsetYMoa = 0)
    {
        double sigma = SigmaMm(sigmaMoa, distanceM);
        double dx = offsetXMoa * distanceM * MmPerMoaPerMetre, dy = offsetYMoa * distanceM * MmPerMoaPerMetre;

        double P(double s) => shape == Shape.Circle ? Circle(sizeAMm / 2, s, dx, dy) : Rectangle(sizeAMm, sizeBMm, s, dx, dy);

        double? cautious = UpperSigmaFactor(impacts) is { } f ? P(sigma * f) * 100 : null;
        return new HitProbabilityResult(P(sigma) * 100, cautious, sigma, Quantile(sigma, 0.50), Quantile(sigma, 0.90), Quantile(sigma, 0.95));
    }

    /// <summary>exp(-x)·I0(x) for x ≥ 0 (Abramowitz &amp; Stegun 9.8.1 and 9.8.2, relative error below 2e-7).</summary>
    internal static double BesselI0Scaled(double x)
    {
        if (x < 3.75)
        {
            double t = x / 3.75; t *= t;
            double i0 = 1 + t * (3.5156229 + t * (3.0899424 + t * (1.2067492 + t * (0.2659732 + t * (0.0360768 + t * 0.0045813)))));
            return i0 * Math.Exp(-x);
        }
        double u = 3.75 / x;
        double poly = 0.39894228 + u * (0.01328592 + u * (0.00225319 + u * (-0.00157565 + u * (0.00916281
                      + u * (-0.02057706 + u * (0.02635537 + u * (-0.01647633 + u * 0.00392377)))))));
        return poly / Math.Sqrt(x);
    }
}
