using GrtPluginKit.Analysis;
using GrtReloadingToolkit.Cartridges;
using GrtReloadingToolkit.Measuring;

namespace GrtReloadingToolkit.Shooting;

/// <summary>One shot on the target: where it hit, relative to the point of aim (when that is known) or to anything fixed on the target.</summary>
/// <param name="Series">The series (a target, a group, a string) the shot belongs to: the centre of the group is taken series by series.</param>
/// <param name="Order">0 for the first shot of the series, 1 for the second…</param>
/// <param name="XMm">Millimetres to the right of the origin.</param>
/// <param name="UpMm">Millimetres above the origin.</param>
public sealed record PatternShot(string Series, int Order, int Number, double XMm, double UpMm, double? VelocityMps = null, double? ClockSeconds = null);

public enum ShootingMode { Unknown, Mono, DoubleRest, Bipod }

public sealed record PatternOptions
{
    public double DistanceM { get; init; } = 100;
    /// <summary>True when the coordinates are measured from the point of aim: only then can the centre of the group say anything about the zero of the sight.</summary>
    public bool AimKnown { get; init; } = true;
    public ShootingMode Mode { get; init; } = ShootingMode.Unknown;
    /// <summary>The value of one click of the scope, e.g. "1/4 MOA" or "0.1 mrad"; null gives the two most common.</summary>
    public string? ScopeClick { get; init; }
    /// <summary>The unit the angles are shown in: "moa" or "mrad".</summary>
    public string AngleUnit { get; init; } = "moa";
}

public sealed record ClickAdvice(string Name, double MmAtDistance, int Right, int Up);

/// <summary>Something the pattern of the shots points to, with the numbers behind it.</summary>
public sealed record ShooterFinding(string Key, string Title, string Detail, string Advice, double? P);

public sealed record SeriesPattern(string Series, int N, double CentreX, double CentreUp, double? SdX, double? SdUp, double MeanRadius);

/// <summary>How much of the height of the shots comes from the velocity of the cartridges (so not from the shooter).</summary>
public sealed record VelocityEffect(double SlopeMmPerMps, double P, double R2, double? TheoreticalMmPerMps, double SdMps);

public sealed class ShooterReport
{
    public int N { get; init; }
    public double DistanceM { get; init; }
    public double CentreX { get; init; }
    public double CentreUp { get; init; }
    /// <summary>Around the centre of each series, over all the series.</summary>
    public double SdX { get; init; }
    public double SdUp { get; init; }
    public double Sigma { get; init; }
    /// <summary>The unit the angles of the report are in ("moa" or "mrad") and the millimetres that one unit of it is at 100 m.</summary>
    public string AngleUnit { get; init; } = "moa";
    public double AngleMmAt100 { get; init; } = ShooterErrors.MoaMmAt100;
    /// <summary>The direction of the longest spread: degrees from horizontal (−90…90), and how much longer it is than the other.</summary>
    public double MajorAxisDegrees { get; init; }
    public double AxisRatio { get; init; }
    public VelocityEffect? Velocity { get; init; }
    public required IReadOnlyList<SeriesPattern> Series { get; init; }
    public required IReadOnlyList<ShooterFinding> Findings { get; init; }
    /// <summary>Things that were checked and are fine.</summary>
    public required IReadOnlyList<string> Fine { get; init; }
    public IReadOnlyList<ClickAdvice> Clicks { get; init; } = Array.Empty<ClickAdvice>();
    public required IReadOnlyList<(string Series, int Number, double Z)> Flyers { get; init; }
}

/// <summary>
/// Reading the shots on a target for the mistakes the shooter makes. Each check is a test with a probability, never a bare claim: the zero of
/// the sight, sideways against vertical spread (with the part the cartridges' velocity explains taken out of the height), shots far from their
/// group, the point of impact moving during the series, the time kept between shots, the first shot of a series, and a group that gets worse
/// from one series to the next. A port, made general, of the shooting analysis of "Analisi DS" (which also scored the shots on the FSTP rings and
/// read the position on the card, left out here); checked against it on the same data.
/// </summary>
public static class ShooterErrors
{
    public const double MoaMmAt100 = 29.0888;
    public const int MinShots = 5;

    private static readonly (string Name, double MmAt100)[] KnownClicks =
    {
        ("1/8 MOA", 0.125 * MoaMmAt100), ("1/4 MOA", 0.25 * MoaMmAt100), ("1/2 MOA", 0.5 * MoaMmAt100), ("1/20 mrad", 5.0), ("1/10 mrad", 10.0),
    };

    /// <summary>Millimetres a bullet drops more for each m/s less, at <paramref name="distanceM"/> (no air).</summary>
    public static double MmPerMps(double distanceM, double velocityMps) => VarianceBudget.MmPerMps(distanceM, velocityMps);

    public static ShooterReport? Analyze(IReadOnlyList<PatternShot> input, PatternOptions? options = null)
    {
        var opt = options ?? new PatternOptions();
        var shots = input.Where(s => double.IsFinite(s.XMm) && double.IsFinite(s.UpMm)).ToList();
        if (shots.Count < MinShots) return null;
        double D = opt.DistanceM > 0 ? opt.DistanceM : 100;

        var names = shots.Select(s => s.Series).Distinct().ToList();
        int n = shots.Count;
        // each shot around the centre of its own series
        var cx = names.ToDictionary(k => k, k => shots.Where(s => s.Series == k).Average(s => s.XMm));
        var cu = names.ToDictionary(k => k, k => shots.Where(s => s.Series == k).Average(s => s.UpMm));
        var xc = shots.Select(s => s.XMm - cx[s.Series]).ToArray();
        var uc = shots.Select(s => s.UpMm - cu[s.Series]).ToArray();
        var rc = xc.Select((x, i) => Math.Sqrt(x * x + uc[i] * uc[i])).ToArray();
        int dof = Math.Max(n - names.Count, 1);
        double sdX = Math.Sqrt(xc.Sum(v => v * v) / dof), sdU = Math.Sqrt(uc.Sum(v => v * v) / dof);
        double sigma = Math.Sqrt((sdX * sdX + sdU * sdU) / 2);
        if (sigma <= 0) sigma = 1.0;
        double meanX = shots.Average(s => s.XMm), meanU = shots.Average(s => s.UpMm);

        // the part of the height that comes from the velocity of the cartridges
        VelocityEffect? vel = null;
        var dv = new double?[n];
        foreach (var k in names)
        {
            var idx = Enumerable.Range(0, n).Where(i => shots[i].Series == k && shots[i].VelocityMps is { } v && double.IsFinite(v)).ToList();
            if (idx.Count == 0) continue;
            double mv = idx.Average(i => shots[i].VelocityMps!.Value);
            foreach (int i in idx) dv[i] = shots[i].VelocityMps!.Value - mv;
        }
        var okIdx = Enumerable.Range(0, n).Where(i => dv[i].HasValue).ToList();
        var allV = shots.Where(s => s.VelocityMps.HasValue).Select(s => s.VelocityMps!.Value).ToList();
        if (okIdx.Count >= 6 && SampleSd(okIdx.Select(i => dv[i]!.Value).ToList()) > 0)
        {
            var reg = VelocityModel.SimpleRegression(okIdx.Select(i => dv[i]!.Value).ToArray(), okIdx.Select(i => uc[i]).ToArray());
            double? theo = allV.Count > 0 ? MmPerMps(D, allV.Average()) : null;
            vel = new VelocityEffect(reg.Slope, reg.P, reg.R * reg.R, theo, SampleSd(okIdx.Select(i => dv[i]!.Value).ToList()));
        }
        double slopeUsed = vel is not null && vel.P < 0.05 ? vel.SlopeMmPerMps : (vel?.TheoreticalMmPerMps ?? 0);
        var uVel = Enumerable.Range(0, n).Select(i => dv[i].HasValue ? dv[i]!.Value * slopeUsed : 0.0).ToArray();

        var table = names.Select(k =>
        {
            var idx = Enumerable.Range(0, n).Where(i => shots[i].Series == k).ToList();
            return new SeriesPattern(k, idx.Count, cx[k], cu[k],
                idx.Count > 1 ? SampleSd(idx.Select(i => xc[i]).ToList()) : null, idx.Count > 1 ? SampleSd(idx.Select(i => uc[i]).ToList()) : null,
                idx.Average(i => rc[i]));
        }).ToList();

        var findings = new List<ShooterFinding>();
        var fine = new List<string>();
        var advice = Advice(opt.Mode);
        bool mrad = string.Equals(opt.AngleUnit, "mrad", StringComparison.OrdinalIgnoreCase);
        double angleMm100 = mrad ? 100.0 : MoaMmAt100;
        double moa = angleMm100 * D / 100;                       // millimetres of one unit of the angle shown, at the distance of the shots
        string? sel = ScopeClicks.Canonical(opt.ScopeClick);
        var clicks = new List<ClickAdvice>();

        // 1. the zero of the sight (only when the coordinates are measured from the point of aim)
        if (opt.AimKnown)
        {
            double px = TTestOneSample(shots.Select(s => s.XMm).ToList()), pu = TTestOneSample(shots.Select(s => s.UpMm).ToList());
            if ((px < 0.05 && Math.Abs(meanX) >= 2) || (pu < 0.05 && Math.Abs(meanU) >= 2))
            {
                var list = sel is not null && KnownClicks.Any(c => c.Name == sel)
                    ? new[] { KnownClicks.First(c => c.Name == sel) }
                    : KnownClicks.Where(c => c.Name is "1/4 MOA" or "1/10 mrad").ToArray();
                foreach (var c in list)
                {
                    double mm = c.MmAt100 * D / 100;
                    // the correction is the opposite of where the centre is; Python (Analisi DS) rounds halves to even, so does this
                    clicks.Add(new ClickAdvice(c.Name, mm, -(int)Math.Round(meanX / mm, MidpointRounding.ToEven), -(int)Math.Round(meanU / mm, MidpointRounding.ToEven)));
                }
                string perSeries = string.Join("; ", table.Select(t => Tr.F("{0} {1} mm {2} / {3} mm {4}", t.Series, F(Math.Abs(t.CentreX), 1), t.CentreX >= 0 ? Tr.T("right") : Tr.T("left"),
                    F(Math.Abs(t.CentreUp), 1), t.CentreUp >= 0 ? Tr.T("high") : Tr.T("low"))));
                string detail = Tr.F("The centre of all the shots is {0} mm to the {1} and {2} mm {3} of the point of aim ({4} and {5} MOA at {6} m). It is not chance: the probability that it is chance is {7} %. Series by series (mm): {8}.",
                    F(Math.Abs(meanX), 1), meanX >= 0 ? Tr.T("right") : Tr.T("left"), F(Math.Abs(meanU), 1), meanU >= 0 ? Tr.T("above") : Tr.T("below"),
                    F(Math.Abs(meanX) / moa, 2), F(Math.Abs(meanU) / moa, 2), F(D, 0), F(Math.Max(Math.Min(px, pu), 0.0001) * 100, 2), perSeries);
                string fix = string.Join(" ", clicks.Select(c => Tr.F("With {0} clicks ({1} mm at {2} m): {3}.", c.Name, F(c.MmAtDistance, 2), F(D, 0), ClickText(c))));
                findings.Add(new ShooterFinding("zero", Tr.T("The zero of the sight is not right"), detail + " " + fix,
                    Tr.T("After the proving shots look at where the CENTRE of the group is, not the single shot: if it is more than half a click off, correct the sight before the series (and again before each return, because the point of impact moves with the heat of the barrel)."),
                    Math.Min(px, pu)));
            }
            else
                fine.Add(Tr.F("The zero of the sight is right: the centre of the shots ({0} / {1} mm) cannot be told from the point of aim.", F(meanX, 1, true), F(meanU, 1, true)));
        }

        // 2. sideways against vertical spread (the vertical without the part the velocity explains)
        double varX = xc.Sum(v => v * v) / dof;
        double varU = Enumerable.Range(0, n).Sum(i => (uc[i] - uVel[i]) * (uc[i] - uVel[i])) / dof;
        if (varX > 0 && varU > 0)
        {
            double f = varX / varU;
            double pF = Math.Clamp(2 * Math.Min(1 - FDist.Cdf(f, dof, dof), FDist.Cdf(f, dof, dof)), 0, 1);
            if (pF < 0.05 && f > 1)
            {
                findings.Add(new ShooterFinding("horizontal", Tr.T("Sideways error bigger than the vertical one"),
                    Tr.F("Around the centre of the group the shots spread {0} mm sideways and {1} mm in height (the part due to velocity taken out). At {2} m the cartridges do not move the shot sideways, so the difference comes from the shooting or the wind (the probability that it is chance is {3} %).",
                        F(Math.Sqrt(varX), 1), F(Math.Sqrt(varU), 1), F(D, 0), F(pF * 100, 1)), advice.Horizontal, pF));
                if (advice.GoodVertical is { } gv) fine.Add(Tr.F("Vertical {0} mm against {1} mm sideways.", F(Math.Sqrt(varU), 1), F(Math.Sqrt(varX), 1)) + " " + gv);
            }
            else if (pF < 0.05)
                findings.Add(new ShooterFinding("vertical", Tr.T("Vertical error bigger than the sideways one"),
                    Tr.F("Around the centre of the group the shots spread {0} mm in height (the part due to velocity taken out) and {1} mm sideways (the probability that it is chance is {2} %).",
                        F(Math.Sqrt(varU), 1), F(Math.Sqrt(varX), 1), F(pF * 100, 1)), advice.Vertical, pF));
            else
                fine.Add(Tr.F("Sideways ({0} mm) and vertical ({1} mm) are alike: neither direction is worse than the other.", F(Math.Sqrt(varX), 1), F(Math.Sqrt(varU), 1)));
        }

        // 3. shots far from their group
        var z = rc.Select(r => r / sigma).ToArray();
        double zLim = Math.Max(2.5, Math.Sqrt(2 * Math.Log(2 * n)));
        var flyers = Enumerable.Range(0, n).Where(i => z[i] > zLim).OrderByDescending(i => z[i]).ToList();
        if (flyers.Count > 0)
        {
            var lines = flyers.Select(i =>
            {
                double velPart = Math.Sign(uVel[i]) == Math.Sign(uc[i]) ? uVel[i] : 0;
                string velTxt = dv[i] is { } d
                    ? Tr.F("the velocity ({0} m/s) explains {1} % of it", F(d, 1, true), F(Math.Min(Math.Abs(velPart) / Math.Max(rc[i], 0.1), 1) * 100, 0))
                    : Tr.T("velocity not measured");
                return Tr.F("{0}, shot {1}: {2} mm {3} and {4} mm {5} of the group; {6}", shots[i].Series, shots[i].Number, F(Math.Abs(xc[i]), 0),
                    xc[i] >= 0 ? Tr.T("to the right") : Tr.T("to the left"), F(Math.Abs(uc[i]), 0), uc[i] >= 0 ? Tr.T("high") : Tr.T("low"), velTxt);
            });
            findings.Add(new ShooterFinding("flyers", Tr.T("Shots out of the group"),
                Tr.F("Shots further from the centre of their own series than {0} times the normal spread (by chance fewer than half a shot would be):", F(zLim, 1)) + Environment.NewLine + "• " + string.Join(Environment.NewLine + "• ", lines),
                Tr.T("If the velocity does not explain it, it is a badly fired shot or a gust of wind: write on the target at once what you felt (a \"pulled\" shot, a movement, wind) so that next time you know which of the two it was."), null));
        }

        // 4. the point of impact moving during the series
        if (n >= 8)
        {
            var order = shots.Select(s => (double)s.Order).ToArray();
            foreach (var (col, label, pos, neg, key) in new[]
            {
                (xc, Tr.T("sideways"), Tr.T("to the right"), Tr.T("to the left"), "drift_x"), (uc, Tr.T("in height"), Tr.T("upwards"), Tr.T("downwards"), "drift_u"),
            })
            {
                var reg = VelocityModel.SimpleRegression(order, col);
                if (reg.P < 0.05)
                    findings.Add(new ShooterFinding(key, Tr.F("The shot moves {0} during the series", label),
                        Tr.F("Shot after shot the point of impact moves {0} mm {1} (the probability that it is chance is {2} %).", F(Math.Abs(reg.Slope), 1), reg.Slope >= 0 ? pos : neg, F(reg.P * 100, 1)),
                        Tr.T("A position that settles or tires during the series, a bipod or a bag that slips, light or mirage that change: check the position again in the middle of the series."), reg.P));
            }
        }

        // 5. the time kept between shots
        var gaps = new List<(double Gap, double R)>();
        foreach (var k in names)
        {
            var idx = Enumerable.Range(0, n).Where(i => shots[i].Series == k).OrderBy(i => shots[i].Order).ToList();
            for (int j = 1; j < idx.Count; j++)
                if (shots[idx[j]].ClockSeconds is { } t1 && shots[idx[j - 1]].ClockSeconds is { } t0) gaps.Add((t1 - t0, rc[idx[j]]));
        }
        if (gaps.Count >= 8)
        {
            var (rho, p) = Spearman(gaps.Select(g => g.Gap).ToArray(), gaps.Select(g => g.R).ToArray());
            double meanGap = gaps.Average(g => g.Gap);
            if (p < 0.05 && rho > 0)
                findings.Add(new ShooterFinding("hold_long", Tr.T("Shots held a long time go worse"),
                    Tr.F("The more time passes from the shot before, the further the shot goes from the group (Spearman {0}, the probability that it is chance is {1} %). On average {2} s between one shot and the next.", F(rho, 2), F(p * 100, 1), F(meanGap, 0)),
                    Tr.T("If the shot does not go within a few seconds of the aim being steady, come off, breathe and start again."), p));
            else if (p < 0.05)
                findings.Add(new ShooterFinding("rushed", Tr.T("Shots fired in a hurry go worse"),
                    Tr.F("Shots fired close to the previous one go further from the group (Spearman {0}, the probability that it is chance is {1} %).", F(rho, 2), F(p * 100, 1)),
                    Tr.T("After each shot put yourself back in position and check the wind again before firing the next."), p));
            else
                fine.Add(Tr.F("Regular pace ({0} s between one shot and the next on average), not linked to the errors.", F(meanGap, 0)));
        }

        // 6. the first shot of each series
        if (names.Count >= 3)
        {
            var first = Enumerable.Range(0, n).Where(i => shots[i].Order == 0).Select(i => rc[i]).ToList();
            var rest = Enumerable.Range(0, n).Where(i => shots[i].Order > 0).Select(i => rc[i]).ToList();
            if (first.Count >= 3 && rest.Count >= 3)
            {
                double p = TTestIndependent(first, rest);
                if (p < 0.05 && first.Average() > rest.Average())
                    findings.Add(new ShooterFinding("first_shot", Tr.T("The first shot of each series goes worse than the others"),
                        Tr.F("The first shot of each series is on average {0} mm from the centre of its group, the others {1} mm (the probability that it is chance is {2} %).", F(first.Average(), 1), F(rest.Average(), 1), F(p * 100, 1)),
                        Tr.T("Go over the position calmly before the first shot of each series; if you can, fire a settling shot first."), p));
                else
                    fine.Add(Tr.F("The first shot of the series does not go worse than the others ({0} mm against {1} mm).", F(first.Average(), 1), F(rest.Average(), 1)));
            }
        }

        // 7. the group getting worse from one series to the next
        if (names.Count >= 2 && names.All(k => shots.Count(s => s.Series == k) >= 3))
        {
            var groups = names.Select(k => Enumerable.Range(0, n).Where(i => shots[i].Series == k).Select(i => rc[i]).ToList()).ToList();
            double pb = Bartlett(groups);
            if (!double.IsFinite(pb)) { }
            else if (pb >= 0.05)
                fine.Add(Tr.T("The series have the same spread: constant concentration from the first series to the last."));
            else
            {
                var worst = table.OrderByDescending(t => t.MeanRadius).First();
                findings.Add(new ShooterFinding("concentration", Tr.F("Concentration dropped in {0}", worst.Series),
                    Tr.F("The spread is not the same in all the series (the probability that it is chance is {0} %): mean radius highest in {1} ({2} mm).", F(pb * 100, 1), worst.Series, F(worst.MeanRadius, 1)),
                    Tr.T("Tiredness or hurry in that series: between series rest and start again from the complete routine."), pb));
            }
        }

        // the direction of the longest spread (descriptive: no claim is made about it)
        double sxx = xc.Sum(v => v * v), suu = uc.Sum(v => v * v), sxu = Enumerable.Range(0, n).Sum(i => xc[i] * uc[i]);
        double tr = sxx + suu, det = sxx * suu - sxu * sxu;
        double disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
        double l1 = tr / 2 + disc, l2 = Math.Max(tr / 2 - disc, 1e-12);
        double angle = 0.5 * Math.Atan2(2 * sxu, sxx - suu) * 180 / Math.PI;
        var report = new ShooterReport
        {
            N = n, DistanceM = D, CentreX = meanX, CentreUp = meanU, SdX = sdX, SdUp = sdU, Sigma = sigma, MajorAxisDegrees = angle, AxisRatio = Math.Sqrt(l1 / l2),
            AngleUnit = mrad ? "mrad" : "moa", AngleMmAt100 = angleMm100, Velocity = vel, Series = table, Findings = findings, Fine = fine, Clicks = clicks,
            Flyers = flyers.Select(i => (shots[i].Series, shots[i].Number, z[i])).ToList(),
        };
        return report;
    }

    // ---- what the shooter is told, by the way the rifle rests ----------------------------------------------------------

    internal sealed record ModeAdvice(string Horizontal, string Vertical, string? GoodVertical);

    internal static ModeAdvice Advice(ShootingMode mode) => mode switch
    {
        ShootingMode.Mono => new ModeAdvice(
            Tr.T("With the front rest only the rifle turns on the front support and the back is governed by you, so the sideways error comes mostly from behind. Check in this order: shoulder pressing in a different way from one shot to the next; cheek pushing the stock sideways; rear hand pulling or squeezing at the moment of the shot; rifle tilted in a different way every shot (easy without a rear rest: level on the scope); trigger pulled sideways. The wind comes after."),
            Tr.T("With the front rest only the height is held by the rear hand: a different grip or support from one shot to the next, a shot fired at a different moment of the breath, a shoulder that pushes more or less, a front rest loaded in a different way."),
            Tr.T("With the front rest only, a vertical spread smaller than the sideways one means the rear hand holds the height well: the work to do is on the side.")),
        ShootingMode.DoubleRest => new ModeAdvice(
            Tr.T("With the rifle rested in front and behind, typical causes in this order: wind not read or not corrected; rear bag that moves or is squeezed sideways; cheek pressing on the stock in a different way; trigger pulled sideways; rifle tilted (level on the scope)."),
            Tr.T("With the double rest: rear bag squeezed in a different way; shoulder pushing more or less; shot fired at a different moment of the breath; recoil not always accompanied the same way."), null),
        ShootingMode.Bipod => new ModeAdvice(
            Tr.T("With the bipod: bipod loaded sideways (a different preload from one shot to the next); shoulder and cheek pushing sideways; rifle tilted (level on the scope); trigger pulled sideways; wind."),
            Tr.T("With the bipod: a different forward preload from one shot to the next, bipod that jumps at the recoil, rear hand or bag supporting in a different way, moment of the breath."), null),
        _ => new ModeAdvice(
            Tr.T("Typical causes, to check in this order: wind not read or not corrected; rifle tilted (level on the scope); trigger pulled sideways (the finger must press straight back); cheek pressing on the stock in a different way from one shot to the next; tight grip. (Say how the rifle rests for more precise advice.)"),
            Tr.T("Typical causes: shot fired at a different moment of the breath; rear support or hand holding in a different way; shoulder pushing more or less."), null),
    };

    private static string ClickText(ClickAdvice c)
    {
        var parts = new List<string>();
        if (c.Up != 0) parts.Add(Math.Abs(c.Up).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + (c.Up > 0 ? Tr.T("up") : Tr.T("down")));
        if (c.Right != 0) parts.Add(Math.Abs(c.Right).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + (c.Right > 0 ? Tr.T("to the right") : Tr.T("to the left")));
        return parts.Count > 0 ? string.Join(" " + Tr.T("and") + " ", parts) : Tr.T("no click (too coarse)");
    }

    // ---- statistics ----------------------------------------------------------------------------------------------------

    internal static string F(double v, int decimals, bool signed = false)
    {
        string f = "0." + new string('0', decimals);
        if (decimals == 0) f = "0";
        return signed ? v.ToString("+" + f + ";-" + f + ";" + f, System.Globalization.CultureInfo.InvariantCulture) : v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static double SampleSd(IReadOnlyList<double> v)
    {
        if (v.Count < 2) return 0;
        double m = v.Average();
        return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / (v.Count - 1));
    }

    /// <summary>Two-sided p of "the mean is zero" (Student t).</summary>
    public static double TTestOneSample(IReadOnlyList<double> v)
    {
        int n = v.Count;
        double sd = SampleSd(v);
        if (n < 2 || sd <= 0) return 1;
        double t = v.Average() / (sd / Math.Sqrt(n));
        return Math.Clamp(2 * (1 - StudentT.Cdf(Math.Abs(t), n - 1)), 0, 1);
    }

    /// <summary>Two-sided p of "the two means are equal", with the variances pooled.</summary>
    public static double TTestIndependent(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        int na = a.Count, nb = b.Count;
        double va = SampleSd(a) * SampleSd(a), vb = SampleSd(b) * SampleSd(b);
        double sp2 = ((na - 1) * va + (nb - 1) * vb) / (na + nb - 2);
        double se = Math.Sqrt(sp2 * (1.0 / na + 1.0 / nb));
        if (se <= 0) return 1;
        double t = (a.Average() - b.Average()) / se;
        return Math.Clamp(2 * (1 - StudentT.Cdf(Math.Abs(t), na + nb - 2)), 0, 1);
    }

    /// <summary>Spearman's rank correlation and its two-sided p (Student t on n−2 degrees of freedom).</summary>
    public static (double Rho, double P) Spearman(double[] x, double[] y)
    {
        double[] rx = Ranks(x), ry = Ranks(y);
        double? r = VelocityModel.Pearson(rx, ry);
        if (r is null) return (0, 1);
        int n = x.Length;
        if (Math.Abs(r.Value) >= 1) return (r.Value, 0);
        double t = r.Value * Math.Sqrt((n - 2) / (1 - r.Value * r.Value));
        return (r.Value, Math.Clamp(2 * (1 - StudentT.Cdf(Math.Abs(t), n - 2)), 0, 1));
    }

    private static double[] Ranks(double[] v)
    {
        var order = Enumerable.Range(0, v.Length).OrderBy(i => v[i]).ToArray();
        var ranks = new double[v.Length];
        for (int i = 0; i < order.Length;)
        {
            int j = i;
            while (j + 1 < order.Length && v[order[j + 1]] == v[order[i]]) j++;
            double avg = (i + j) / 2.0 + 1;
            for (int k = i; k <= j; k++) ranks[order[k]] = avg;
            i = j + 1;
        }
        return ranks;
    }

    /// <summary>Bartlett's test that several samples have the same variance: p from the chi-square.</summary>
    public static double Bartlett(IReadOnlyList<List<double>> groups)
    {
        int k = groups.Count;
        var n = groups.Select(g => g.Count).ToArray();
        var v = groups.Select(g => SampleSd(g) * SampleSd(g)).ToArray();
        if (v.Any(x => x <= 0)) return double.NaN;
        double N = n.Sum();
        double sp2 = Enumerable.Range(0, k).Sum(i => (n[i] - 1) * v[i]) / (N - k);
        double num = (N - k) * Math.Log(sp2) - Enumerable.Range(0, k).Sum(i => (n[i] - 1) * Math.Log(v[i]));
        double den = 1 + (1.0 / (3 * (k - 1))) * (Enumerable.Range(0, k).Sum(i => 1.0 / (n[i] - 1)) - 1.0 / (N - k));
        double stat = num / den;
        return Math.Clamp(1 - StatsExtra.ChiSquareCdf(stat, k - 1), 0, 1);
    }
}
