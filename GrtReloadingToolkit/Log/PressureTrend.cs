namespace GrtReloadingToolkit.Log;

/// <summary>
/// Ported from Ballistic Lab v2's <c>core/pressure_trend.py</c>: a heuristic pressure-proximity
/// signal from the FLATTENING of the velocity-vs-charge slope (dv/dCharge) — as each extra grain of
/// powder buys less and less velocity, that is the classic sign of approaching the case/action's
/// pressure limit. This is explicitly NOT a rigorous pressure model (that would need a real interior-
/// ballistics model with actual pressure data) — an honest heuristic, same as the old software's
/// <c>engines/statistical.py::pressure_analysis</c> it was ported from, never reinvented here.
///
/// A genuinely different diagnostic angle from both this toolkit's Ladder/OCW analyzer (looks for
/// SIMILAR velocities across a window) and its OCW half (looks for the flattest GROUP on target) —
/// neither looks at the SHAPE of the velocity/charge curve the way this does.
///
/// Known, inherited limitation (not corrected here — correcting it would be a design change, not a
/// verification of an existing formula): the severity checks compare the last slope against a
/// multiple of the SIGNED mean slope, not its absolute value. With physically anomalous input
/// (velocity decreasing as charge rises — never expected, not prevented here) multiplying by a
/// negative mean slope flips what "much smaller/larger than average" means. If that happens, the
/// input data is suspect regardless of what this heuristic reports.
/// </summary>
public static class PressureTrend
{
    public sealed class PressureTrendError : ArgumentException
    {
        public PressureTrendError(string message) : base(message) { }
    }

    public const int MinCharges = 3;

    public sealed record VelocityJump(double ChargeGr, double SlopeMpsPerGr, string Severity);

    public sealed record PressureTrendResult(
        IReadOnlyList<double> ChargesGr, IReadOnlyList<double> VelocitiesMps, IReadOnlyList<double> SlopesMpsPerGr,
        double MeanSlopeMpsPerGr, IReadOnlyList<VelocityJump> VelocityJumps, bool FlatteningTrend,
        string PressureEstimate, string PressureLevel);

    /// <param name="chargesGr">Charges in whatever test order — re-sorted by ascending weight along
    /// with their matching velocities.</param>
    /// <param name="velocitiesMps">Mean velocity per charge, same order as <paramref name="chargesGr"/>.</param>
    public static PressureTrendResult AnalyzePressureTrend(IReadOnlyList<double> chargesGr, IReadOnlyList<double> velocitiesMps)
    {
        if (chargesGr.Count != velocitiesMps.Count)
            throw new PressureTrendError("chargesGr and velocitiesMps must have the same length.");
        if (chargesGr.Count < MinCharges)
            throw new PressureTrendError($"Need at least {MinCharges} charges for the pressure trend.");

        var paired = chargesGr.Zip(velocitiesMps, (c, v) => (Charge: c, Velocity: v)).OrderBy(p => p.Charge).ToList();
        var charges = paired.Select(p => p.Charge).ToList();
        var velocities = paired.Select(p => p.Velocity).ToList();

        var slopes = new List<double>();
        for (int i = 0; i < charges.Count - 1; i++)
        {
            double dc = charges[i + 1] - charges[i];
            slopes.Add(dc > 0 ? (velocities[i + 1] - velocities[i]) / dc : 0.0);
        }
        double meanSlope = slopes.Average();

        // Velocity jump: a slope spike (>3x the mean) — dirty data or genuine ignition instability
        // between two nearby charges.
        var jumps = new List<VelocityJump>();
        for (int i = 0; i < slopes.Count; i++)
        {
            if (slopes[i] > meanSlope * 3)
                jumps.Add(new VelocityJump(charges[i + 1], Math.Round(slopes[i], 2),
                    slopes[i] > meanSlope * 5 ? "High" : "Medium"));
        }

        // Flattening: the last (up to 2) slopes are much smaller than the mean while staying
        // positive — the classic signature of approaching a pressure plateau.
        bool flattening = false;
        for (int i = slopes.Count - 1; i >= Math.Max(0, slopes.Count - 2); i--)
            if (slopes[i] < meanSlope * 0.2 && slopes[i] >= 0) { flattening = true; break; }

        double lastSlope = slopes.Count > 0 ? slopes[^1] : meanSlope;
        string estimate, level;
        if (lastSlope < meanSlope * 0.1) { estimate = "Maximum — possible overpressure signal"; level = "critical"; }
        else if (lastSlope < meanSlope * 0.4) { estimate = "High — near the limit"; level = "warning"; }
        else if (lastSlope > meanSlope * 1.5) { estimate = "Low — ample margin"; level = "safe"; }
        else { estimate = "Normal"; level = "normal"; }

        return new PressureTrendResult(charges, velocities, slopes.Select(s => Math.Round(s, 4)).ToList(),
            Math.Round(meanSlope, 4), jumps, flattening, estimate, level);
    }
}
