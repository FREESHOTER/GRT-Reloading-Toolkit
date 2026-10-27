namespace GrtReloadingToolkit.Log;

public enum StabilityLevel { Stable, Adequate, Marginal, Unstable }

/// <param name="Sg">Gyroscopic stability factor at the given velocity and air.</param>
/// <param name="SgStandard">The same at the standard day (15 °C, sea level) and 2800 ft/s, the number the rule is written for.</param>
/// <param name="GreenhillTwistIn">The slowest twist the old Greenhill rule would still accept, as a cross-check only.</param>
/// <param name="TwistFor15In">The slowest twist (inches per turn) that gives Sg 1.5 in these conditions.</param>
/// <param name="TwistFor14In">The slowest twist (inches per turn) that gives Sg 1.4 in these conditions.</param>
public sealed record GyroStabilityResult(
    double Sg, double SgStandard, double VelocityFactor, double AtmosphereFactor, double TwistCalibers, double LengthCalibers,
    StabilityLevel Level, double GreenhillTwistIn, double TwistFor15In, double TwistFor14In, double PressureInHg);

/// <summary>
/// Gyroscopic stability of a bullet by the Miller twist rule (Don Miller, Precision Shooting, 2005 and 2009), the one
/// Berger and Applied Ballistics use: <c>Sg = 30 m / (t² d³ l (1 + l²))</c> with the mass m in grains, the diameter d in
/// inches, and the twist t and the length l in calibres, then scaled for velocity, <c>(v / 2800)^(1/3)</c>, and for the air,
/// <c>(T / 519 °R)(29.92 / P)</c>. Cold air lowers Sg (it is denser) and thin air at
/// altitude raises it, so the number to worry about is the one for the coldest, lowest day the rifle will be shot on. The rule is for a spitzer or
/// boat-tail bullet: it says nothing about a flat-base or a very odd shape, and a measured length that includes a plastic
/// tip has to be the bullet's real overall length.
///
/// Sg ≥ 1.5 is taken as stable, 1.4 to 1.5 as adequate, 1.0 to 1.4 as marginal (the bullet flies but its drag rises, and a cold
/// morning can push it under), below 1.0 as unstable. The tool does NOT claim how much ballistic coefficient is lost in the
/// marginal band: published figures disagree and none could be checked here.
/// </summary>
public static class GyroStability
{
    public const double StdPressureInHg = 29.92;
    public const double StdTempRankine = 519.0;     // 59 °F
    public const double StdVelocityFps = 2800.0;
    public const double HpaPerInHg = 33.8638866667;

    /// <summary>Static pressure at an altitude in the standard atmosphere, in inches of mercury.</summary>
    public static double PressureAtAltitudeInHg(double altitudeM) =>
        StdPressureInHg * Math.Pow(1.0 - 2.25577e-5 * altitudeM, 5.25588);

    public static StabilityLevel Classify(double sg) =>
        sg >= 1.5 ? StabilityLevel.Stable : sg >= 1.4 ? StabilityLevel.Adequate : sg >= 1.0 ? StabilityLevel.Marginal : StabilityLevel.Unstable;

    /// <summary>
    /// Null (with <paramref name="error"/> set) when an input cannot be used: a non-positive or non-finite number, a bullet no
    /// longer than it is wide (units mixed up), an altitude outside -500..9000 m.
    /// </summary>
    public static GyroStabilityResult? Compute(
        double massGr, double lengthIn, double diameterIn, double twistIn, double velocityFps, double tempC, double altitudeM,
        out string? error)
    {
        error = null;
        foreach (var (name, v) in new[] { ("bullet mass", massGr), ("bullet length", lengthIn), ("bullet diameter", diameterIn), ("twist", twistIn), ("velocity", velocityFps) })
            if (!double.IsFinite(v) || v <= 0) { error = name; return null; }
        if (!double.IsFinite(tempC)) { error = "temperature"; return null; }
        if (!double.IsFinite(altitudeM) || altitudeM < -500 || altitudeM > 9000) { error = "altitude"; return null; }
        if (lengthIn <= diameterIn) { error = "length"; return null; }

        double d = diameterIn, tCal = twistIn / d, lCal = lengthIn / d;
        double fv = Math.Pow(velocityFps / StdVelocityFps, 1.0 / 3.0);
        double pIn = Math.Max(PressureAtAltitudeInHg(altitudeM), 1e-6);
        double fa = (CelsiusToRankine(tempC) / StdTempRankine) * (StdPressureInHg / pIn);

        // Sg = k / t²: k carries everything but the twist, so the twist that gives a wanted Sg is sqrt(k / Sg) calibres.
        double k = 30.0 * massGr * fv * fa / (d * d * d * lCal * (1.0 + lCal * lCal));
        double sg = k / (tCal * tCal);
        double sgStd = 30.0 * massGr / (tCal * tCal * d * d * d * lCal * (1.0 + lCal * lCal));

        double greenhillC = velocityFps >= StdVelocityFps ? 180.0 : 150.0;
        return new GyroStabilityResult(sg, sgStd, fv, fa, tCal, lCal, Classify(sg), greenhillC * d * d / lengthIn,
            Math.Sqrt(k / 1.5) * d, Math.Sqrt(k / 1.4) * d, pIn);
    }

    private static double CelsiusToRankine(double c) => c * 9.0 / 5.0 + 32.0 + 459.67;
}
