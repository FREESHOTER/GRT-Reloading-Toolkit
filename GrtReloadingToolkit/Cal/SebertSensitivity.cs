using System.Globalization;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Cal;

/// <summary>One GRT simulation, reduced to the numbers this check needs.</summary>
public sealed record SebertSim(double MvMps, double Pmax, double BltMs, string PmaxUnit);

/// <summary>The load simulated with a different Sebert factor and Ba and k re-matched to the same velocity.</summary>
public sealed record SebertSide(double Sebert, double Factor, SebertSim Sim, double MvErrorMps)
{
    public double MatchedWithin => Math.Abs(MvErrorMps);
}

/// <summary>
/// How far the OBT node moves if the load's Sebert factor is wrong, with the velocity still matching the
/// chronograph. The Sebert factor (the share of the powder charge that travels with the bullet) lives with
/// the caliber, not the powder, and GRT's calibration never touches it. Velocity cannot tell it apart from Ba
/// and k -- change it, re-match Ba and k to the same velocity and the chronograph sees nothing -- but the
/// pressure curve changes, and with it the bullet lead time (BLT) GRT's node matching uses. Measured live on a
/// 6.5 Creedmoor / N550 load at the same velocity: Sebert 0.50 -> 0.70 gave +9 % Pmax and 0.021 ms less BLT,
/// the same as about 0.45 gr of charge.
/// </summary>
public sealed class SebertSensitivity
{
    /// <summary>How far the factor is moved each way.</summary>
    public const double Step = 0.1;
    /// <summary>GRT documents the factor as lying between 0.1 and 1.0.</summary>
    public const double MinSebert = 0.1, MaxSebert = 1.0;
    /// <summary>A re-match that stays further than this from the target velocity is not reported (m/s).</summary>
    public const double MatchToleranceMps = 0.3;
    /// <summary>The charge step used to turn a BLT shift into grains.</summary>
    public const double ChargeStepGr = 0.2;

    public double Sebert { get; init; }
    public double ChargeGr { get; init; }
    public SebertSim Baseline { get; init; } = new(0, 0, 0, "");
    /// <summary>ms of BLT per grain of charge at this load (negative: more powder, shorter lead time).</summary>
    public double BltPerGrMs { get; init; }
    public SebertSide? Higher { get; init; }
    public SebertSide? Lower { get; init; }

    /// <summary>Where the node's charge moves, in grains, if the real Sebert is that side's value instead:
    /// the BLT shift at the same charge, divided by how fast BLT moves with charge. A shorter BLT (the factor is
    /// higher) means the same BLT is reached with less powder, so the node's charge moves down.</summary>
    public double NodeShiftGr(SebertSide s) => BltPerGrMs == 0 ? 0 : (s.Sim.BltMs - Baseline.BltMs) / Math.Abs(BltPerGrMs);

    /// <summary>The larger of the two shifts, in grains: the uncertainty a +/-<see cref="Step"/> doubt about the factor
    /// leaves on the node's charge.</summary>
    public double WorstShiftGr => new[] { Higher, Lower }.Where(s => s != null).Select(s => Math.Abs(NodeShiftGr(s!))).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Runs the check against any simulator (GRT in the form, a model in the tests). <paramref name="simulate"/> gets
    /// (sebert, factor on Ba and k, charge in grains) and returns what GRT computes, or null if it could not.
    /// </summary>
    public static async Task<SebertSensitivity?> RunAsync(Func<double, double, double, Task<SebertSim?>> simulate,
        double sebert, double chargeGr)
    {
        var b = await simulate(sebert, 1.0, chargeGr);
        var up = await simulate(sebert, 1.0, chargeGr + ChargeStepGr);
        if (b is null || up is null || b.BltMs <= 0 || up.BltMs <= 0) return null;
        double slope = (up.BltMs - b.BltMs) / ChargeStepGr;
        if (slope >= 0) return null;                      // more powder must shorten the lead time, or the sims are not what we think

        return new SebertSensitivity
        {
            Sebert = sebert, ChargeGr = chargeGr, Baseline = b, BltPerGrMs = slope,
            Higher = sebert + Step <= MaxSebert + 1e-9 ? await MatchedSideAsync(simulate, sebert + Step, chargeGr, b) : null,
            Lower = sebert - Step >= MinSebert - 1e-9 ? await MatchedSideAsync(simulate, sebert - Step, chargeGr, b) : null,
        };
    }

    /// <summary>Re-matches Ba and k (one common factor, like GRT's own OBT tool) to the baseline velocity.</summary>
    private static async Task<SebertSide?> MatchedSideAsync(Func<double, double, double, Task<SebertSim?>> simulate,
        double sebert, double chargeGr, SebertSim target)
    {
        const double nudge = 0.01;
        var s1 = await simulate(sebert, 1.0, chargeGr);
        var s2 = await simulate(sebert, 1.0 + nudge, chargeGr);
        if (s1 is null || s2 is null) return null;
        double slope = (s2.MvMps - s1.MvMps) / nudge;      // m/s per unit relative change of Ba and k together
        if (slope <= 0) return null;

        double f = 1.0 + (target.MvMps - s1.MvMps) / slope;
        SebertSim? cur = null;
        for (int i = 0; i < 3; i++)
        {
            cur = await simulate(sebert, f, chargeGr);
            if (cur is null) return null;
            double err = cur.MvMps - target.MvMps;
            if (Math.Abs(err) <= 0.05) break;
            f -= err / slope;
        }
        double miss = cur!.MvMps - target.MvMps;
        return Math.Abs(miss) <= MatchToleranceMps ? new SebertSide(sebert, f, cur, miss) : null;
    }

    public string BuildReport()
    {
        var gu = GrtUnits.Current;
        var ci = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Sebert factor sensitivity");
        sb.AppendLine(new string('-', 25));
        sb.AppendLine(string.Format(ci,
            "This load uses Sebert {0:0.00}; simulated in GRT at {1}.", Sebert, gu.Charge(ChargeGr)));
        sb.AppendLine("The chronograph cannot tell the Sebert factor from Ba and k: change it, re-match Ba and k to the same");
        sb.AppendLine("velocity, and the velocity looks identical. It still moves the pressure and the OBT node:");
        foreach (var side in new[] { Higher, Lower }.Where(s => s != null).OrderByDescending(s => s!.Sebert))
        {
            var s = side!;
            double shift = NodeShiftGr(s);
            sb.AppendLine(string.Format(ci,
                "  Sebert {0:0.00}: Pmax {1:+0;-0;0} {2}, BLT {3:+0.0000;-0.0000;0.0000} ms  -> node charge about {4:+0.00;-0.00;0.00} {5}",
                s.Sebert, s.Sim.Pmax - Baseline.Pmax, Baseline.PmaxUnit, s.Sim.BltMs - Baseline.BltMs,
                gu.ChargeValue(shift), gu.ChargeUnitName));
        }
        sb.AppendLine(string.Format(ci,
            "BLT moves {0:0.0000} ms per {1} of charge here, so a Sebert known only to +/-{2:0.0#} leaves the node's charge uncertain by about +/-{3} {4}",
            Math.Abs(BltPerGrMs / gu.ChargeValue(1.0)), gu.ChargeUnitName, Step,
            gu.ChargeValue(WorstShiftGr).ToString(gu.ChargeFormat, ci), gu.ChargeUnitName));
        sb.AppendLine("(GRT quotes 0.1 to 0.2 gr of node precision.) A measured Pmax, not a velocity, is what constrains the factor.");
        return sb.ToString();
    }
}
