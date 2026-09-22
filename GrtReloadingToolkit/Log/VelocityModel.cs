namespace GrtReloadingToolkit.Log;

/// <summary>
/// An empirical MV ≈ a + b·charge + c·temperature fit, from the journal's own logged (charge,
/// temperature, velocity) triples for one caliber+powder+bullet combo. Deliberately independent of
/// GRT's own physics model -- no Ba/a0/tcc/tch anywhere in here -- so it's a measured-data
/// cross-check the user can compare against GRT's simulated/calibrated numbers, not a replacement
/// for either Barrel Calibration (fits Ba, needs no temperature spread) or Powder Temp Coefficients
/// (fits GRT's own tcc/tch formalism, needs no charge spread).
///
/// A 2-predictor fit needs charge AND temperature to each vary, AND to vary somewhat independently
/// of each other across the logged sessions -- charge locked at one grain weight, or every string
/// shot on the same day at the same temperature, or (more subtly) charge and temperature happening
/// to track each other 1:1 across sessions, all make the fit unrecoverable. <see cref="Fit"/> says
/// which of these is missing rather than returning a meaningless fit anyway; nothing here should
/// ever silently produce numbers from underdetermined data.
/// </summary>
public static class VelocityModel
{
    public enum UnfitReason { None, TooFewPoints, NoChargeVariance, NoTemperatureVariance, Collinear }

    /// <summary>Fewer than this many points leaves zero degrees of freedom over the 3 fitted
    /// parameters (intercept, charge coefficient, temperature coefficient) -- the fit would pass
    /// exactly through every point, which looks perfect and means nothing.</summary>
    public const int MinPoints = 5;

    public sealed record Result(int N, UnfitReason Reason, double Intercept, double ChargeCoef, double TempCoef, double R2)
    {
        public bool Fitted => Reason == UnfitReason.None;
    }

    public static Result Fit(IReadOnlyList<(double ChargeGr, double TemperatureC, double VelocityMs)> points)
    {
        int n = points.Count;
        if (n < MinPoints) return new Result(n, UnfitReason.TooFewPoints, 0, 0, 0, 0);

        double sx1 = 0, sx2 = 0, sy = 0, sx1x1 = 0, sx2x2 = 0, sx1x2 = 0, sx1y = 0, sx2y = 0;
        foreach (var (chg, temp, v) in points)
        {
            sx1 += chg; sx2 += temp; sy += v;
            sx1x1 += chg * chg; sx2x2 += temp * temp; sx1x2 += chg * temp;
            sx1y += chg * v; sx2y += temp * v;
        }

        // Variance (times n) of each predictor -- zero means every logged session used the same
        // charge, or the same temperature, so that axis of the fit has nothing to estimate from.
        double varX1 = sx1x1 - sx1 * sx1 / n;
        double varX2 = sx2x2 - sx2 * sx2 / n;
        if (varX1 <= 1e-9) return new Result(n, UnfitReason.NoChargeVariance, 0, 0, 0, 0);
        if (varX2 <= 1e-9) return new Result(n, UnfitReason.NoTemperatureVariance, 0, 0, 0, 0);

        // Normal equations for [a, b, c] solved by Cramer's rule -- 3 unknowns, no need for a
        // general matrix library. det == 0 (or too close to it, relative to the two predictors' own
        // variance) means charge and temperature move together across every logged session closely
        // enough that no combination of b and c is distinguishable from any other.
        double det = Det3(n, sx1, sx2, sx1, sx1x1, sx1x2, sx2, sx1x2, sx2x2);
        if (Math.Abs(det) < 1e-9 * Math.Max(varX1 * varX2, 1))
            return new Result(n, UnfitReason.Collinear, 0, 0, 0, 0);

        double a = Det3(sy, sx1, sx2, sx1y, sx1x1, sx1x2, sx2y, sx1x2, sx2x2) / det;
        double b = Det3(n, sy, sx2, sx1, sx1y, sx1x2, sx2, sx2y, sx2x2) / det;
        double c = Det3(n, sx1, sy, sx1, sx1x1, sx1y, sx2, sx1x2, sx2y) / det;

        double meanY = sy / n, ssTot = 0, ssRes = 0;
        foreach (var (chg, temp, v) in points)
        {
            double pred = a + b * chg + c * temp;
            ssRes += (v - pred) * (v - pred);
            ssTot += (v - meanY) * (v - meanY);
        }
        double r2 = ssTot > 0 ? 1 - ssRes / ssTot : 1.0;

        return new Result(n, UnfitReason.None, a, b, c, r2);
    }

    public static double Predict(Result fit, double chargeGr, double temperatureC) =>
        fit.Intercept + fit.ChargeCoef * chargeGr + fit.TempCoef * temperatureC;

    /// <summary>Inverse of <see cref="Predict"/>: the charge that should land on a target velocity at
    /// a given temperature. NaN if the charge coefficient is (numerically) zero -- would mean
    /// dividing by it swings the answer wildly for no real physical reason.</summary>
    public static double ChargeForTarget(Result fit, double targetVelocityMs, double temperatureC) =>
        Math.Abs(fit.ChargeCoef) > 1e-9
            ? (targetVelocityMs - fit.Intercept - fit.TempCoef * temperatureC) / fit.ChargeCoef
            : double.NaN;

    private static double Det3(double a, double b, double c, double d, double e, double f, double g, double h, double i) =>
        a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
}
