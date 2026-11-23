namespace GrtReloadingToolkit.Cartridges;

/// <summary>One fired shot of a prediction: the velocity measured, and the estimate the prediction had saved for its cartridge.</summary>
public sealed record CheckedShot(string Series, double VelocityMps, double Estimate);

public enum CheckVerdict
{
    /// <summary>The cartridges estimated faster really were faster, and it is unlikely to be chance (one-sided p under 5 %).</summary>
    Works,
    /// <summary>Right direction, not yet shown with these shots.</summary>
    RightDirection,
    /// <summary>The estimated faster cartridges were not faster.</summary>
    Failed,
    /// <summary>Every cartridge had the same estimate: there is nothing to check.</summary>
    NothingToCheck,
}

public sealed record PredictionCheckResult(
    int N, int Series, double SdWithout, double SdWith, double? R, double? Slope, double? POneSided, CheckVerdict Verdict);

/// <summary>
/// Predicted against measured, INSIDE each series (temperature and barrel move a whole series, and the prediction says nothing about
/// them). The slope is 1 when the estimate is right, below 1 when it exaggerates the differences, above 1 when it understates them.
/// "With" is the SD left inside the series once the estimated part is taken away. A port of "Analisi DS".
/// </summary>
public static class PredictionCheck
{
    public const int MinShots = 5;
    public const double Alpha = 0.05;

    /// <summary>Null with fewer than <see cref="MinShots"/> shots.</summary>
    public static PredictionCheckResult? Verify(IReadOnlyList<CheckedShot> shots)
    {
        if (shots.Count < MinShots) return null;
        var sv = new Dictionary<string, (double Sum, int N)>();
        var se = new Dictionary<string, (double Sum, int N)>();
        foreach (var s in shots)
        {
            sv.TryGetValue(s.Series, out var a); sv[s.Series] = (a.Sum + s.VelocityMps, a.N + 1);
            se.TryGetValue(s.Series, out var b); se[s.Series] = (b.Sum + s.Estimate, b.N + 1);
        }
        int n = shots.Count, series = sv.Count;
        var dv = new double[n]; var dp = new double[n];
        for (int i = 0; i < n; i++)
        {
            dv[i] = shots[i].VelocityMps - sv[shots[i].Series].Sum / sv[shots[i].Series].N;
            dp[i] = shots[i].Estimate - se[shots[i].Series].Sum / se[shots[i].Series].N;
        }
        int dfree = Math.Max(1, n - series);
        double sdWithout = Math.Sqrt(dv.Sum(v => v * v) / dfree);
        double sdWith = Math.Sqrt(Enumerable.Range(0, n).Sum(i => (dv[i] - dp[i]) * (dv[i] - dp[i])) / dfree);

        double mean = dp.Average();
        double sdP = n > 1 ? Math.Sqrt(dp.Sum(v => (v - mean) * (v - mean)) / (n - 1)) : 0;
        if (!(sdP > 0)) return new PredictionCheckResult(n, series, sdWithout, sdWith, null, null, null, CheckVerdict.NothingToCheck);

        var reg = VelocityModel.SimpleRegression(dp, dv);
        double pOne = reg.Slope > 0 ? reg.P / 2 : 1 - reg.P / 2;
        var verdict = pOne < Alpha && reg.Slope > 0 ? CheckVerdict.Works
            : reg.Slope > 0 ? CheckVerdict.RightDirection
            : CheckVerdict.Failed;
        return new PredictionCheckResult(n, series, sdWithout, sdWith, reg.R, reg.Slope, pOne, verdict);
    }
}
