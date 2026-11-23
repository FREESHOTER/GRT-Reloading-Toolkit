using GrtPluginKit.Analysis;
using GrtReloadingToolkit.Cartridges;
using GrtReloadingToolkit.Measuring;

namespace GrtReloadingToolkit.Shooting;

/// <summary>
/// One shot of a match: one shot for each target, several targets, each with its own point of aim. The error is measured from the aim of its own
/// target, in millimetres on that target (to the right and up).
/// </summary>
/// <param name="Number">The order the shots were fired in (1, 2, 3…).</param>
/// <param name="WindMps">Across wind in m/s at the time of the shot, positive when it pushes the bullet to the right.</param>
/// <param name="Position">How the shooter was (prone, kneeling…), when it changes during the match.</param>
/// <param name="Lot">The cartridge lot, or any label that tells cartridges apart (charge, batch).</param>
public sealed record MatchShot(int Number, string Target, double DistanceM, double XMm, double UpMm, double? VelocityMps = null,
    double? ClockSeconds = null, double? WindMps = null, string? Position = null, string? Lot = null);

public sealed record MatchOptions
{
    public ShootingMode Mode { get; init; } = ShootingMode.Unknown;
    /// <summary>The value of one click of the scope, e.g. "1/4 MOA" or "0.1 mrad"; null is 1/4 MOA.</summary>
    public string? ScopeClick { get; init; }
    /// <summary>The unit the angles are worked out and shown in: "moa" or "mrad".</summary>
    public string AngleUnit { get; init; } = "moa";
}

/// <summary>Where a finding comes from: the cartridges, the settings of the rifle and scope, the conditions (wind) or the shooter's own execution.</summary>
public enum MatchOrigin { Cartridge, Settings, Conditions, Shooter }

/// <param name="Weak">True when the evidence is only just there (a probability of chance between 1 % and 5 %): to be confirmed in the next match.</param>
public sealed record MatchFinding(MatchOrigin Origin, string Key, string Title, string Detail, string Advice, double? P, bool Weak = false);

public sealed record ClickMoa(string Name, double Moa, int Right, int Up);

/// <summary>
/// What one shot is made of (in MOA, from its own point of aim): the part from the velocity of its cartridge, the part from the settings (zero and the
/// elevation or windage at its distance), the part from the wind that was not corrected, and what is left, which is the shooting itself.
/// </summary>
/// <param name="Cause">"cartridge", "settings", "wind", "execution" (a shot out of the shooter's own scatter) or "normal".</param>
public sealed record ShotDiagnosis(int Number, string Target, double DistanceM, double XMoa, double UpMoa, double CartridgeMoa, double SettingsMoa,
    double WindMoa, double ExecutionMoa, string Cause, string Text)
{
    public double TotalMoa => Math.Sqrt(XMoa * XMoa + UpMoa * UpMoa);
}

public sealed class MatchReport
{
    public int N { get; init; }
    public int Targets { get; init; }
    public double MinDistanceM { get; init; }
    public double MaxDistanceM { get; init; }
    public double CentreXMoa { get; init; }
    public double CentreUpMoa { get; init; }
    public double MeanErrorMoa { get; init; }
    /// <summary>The average size of each part of the errors, in MOA.</summary>
    public double MeanCartridgeMoa { get; init; }
    public double MeanSettingsMoa { get; init; }
    public double MeanWindMoa { get; init; }
    public double MeanExecutionMoa { get; init; }
    /// <summary>Half a click: below this a part cannot be corrected on the scope.</summary>
    public double NoticeMoa { get; init; }
    /// <summary>The unit of every angle of the report ("moa" or "mrad"); the property names that say Moa hold angles in this unit.</summary>
    public string AngleUnit { get; init; } = "moa";
    public required IReadOnlyList<ShotDiagnosis> Shots { get; init; }
    public required IReadOnlyList<MatchFinding> Findings { get; init; }
    public required IReadOnlyList<string> Fine { get; init; }
    public IReadOnlyList<ClickMoa> Clicks { get; init; } = Array.Empty<ClickMoa>();
}

/// <summary>
/// Reading a match shot by shot: one shot for each target, targets at different distances, in different wind, perhaps in different positions.
/// Each error is turned into MOA from the aim of its own target and then taken apart into four parts — the velocity of the cartridge (the physics of
/// the drop, checked against what was measured), the settings (a zero that is off, an elevation or windage that does not follow the distance), the wind
/// that was not corrected, and the shooting — each part kept only when the numbers show it is there (a test at 5 %). Every shot is then named for the
/// part that weighs most on it, and the whole match is read for what points to the cartridges, the settings, the conditions or the shooter.
/// </summary>
public static class MatchReading
{
    public const int MinShots = 6;
    private const double MoaPerMrad = 3.437746771;
    /// <summary>Below this probability of chance a finding stands; between this and 5 % it is only a hint to be confirmed, and it does not count when a shot is named for its cause.</summary>
    public const double Strong = 0.01;

    /// <summary>Two tests were made (sideways and vertical): the smaller p is multiplied by two, so that the pair is read as one.</summary>
    private static double Pair(double a, double b) => Math.Min(1, 2 * Math.Min(a, b));


    private static string F(double v, int d, bool signed = false) => ShooterErrors.F(v, d, signed);
    private static string Dir(double v, string pos, string neg) => v >= 0 ? pos : neg;

    public static MatchReport? Analyze(IReadOnlyList<MatchShot> input, MatchOptions? options = null)
    {
        var opt = options ?? new MatchOptions();
        var shots = input.Where(s => double.IsFinite(s.XMm) && double.IsFinite(s.UpMm) && double.IsFinite(s.DistanceM) && s.DistanceM > 0).OrderBy(s => s.Number).ToList();
        int n = shots.Count;
        if (n < MinShots) return null;

        // every angle is worked out in the unit the shooter reads them in: one unit of it is mmAt100 millimetres at 100 m
        bool mrad = string.Equals(opt.AngleUnit, "mrad", StringComparison.OrdinalIgnoreCase);
        double mmAt100 = mrad ? 100.0 : ShooterErrors.MoaMmAt100, perMoa = mrad ? 1.0 / MoaPerMrad : 1.0;
        string? clickName = ScopeClicks.Canonical(opt.ScopeClick);
        double clickUnit = (ScopeClicks.InMoa(clickName) ?? 0.25) * perMoa;
        double notice = 0.5 * clickUnit;

        // every error in MOA from the aim of its own target
        var dist = shots.Select(s => s.DistanceM).ToArray();
        var x = new double[n];
        var u = new double[n];
        for (int i = 0; i < n; i++)
        {
            double mm = mmAt100 * dist[i] / 100;
            x[i] = shots[i].XMm / mm;
            u[i] = shots[i].UpMm / mm;
        }
        double meanD = dist.Average(), minD = dist.Min(), maxD = dist.Max();
        var dh = dist.Select(d => (d - meanD) / 100).ToArray();                          // hectometres from the average distance
        bool hasDist = dist.Select(d => Math.Round(d, 1)).Distinct().Count() >= 2;

        // the velocity of the cartridges, as the drop it should cause (the physics, no air)
        var vAll = shots.Where(s => s.VelocityMps is > 0).Select(s => s.VelocityMps!.Value).ToList();
        bool hasVel = vAll.Count >= MinShots && ShooterErrors.SampleSd(vAll) > 0;
        double vMean = hasVel ? vAll.Average() : 0, vSd = hasVel ? ShooterErrors.SampleSd(vAll) : 0;
        var dv = new double[n];
        var velTerm = new double[n];
        if (hasVel)
            for (int i = 0; i < n; i++)
                if (shots[i].VelocityMps is > 0 and { } v)
                {
                    dv[i] = v - vMean;
                    velTerm[i] = dv[i] * ShooterErrors.MmPerMps(dist[i], vMean) / (mmAt100 * dist[i] / 100);
                }

        // the wind, as the push it gives at that distance (m/s times hectometres)
        var windVals = shots.Where(s => s.WindMps.HasValue).Select(s => s.WindMps!.Value).ToList();
        bool hasWind = windVals.Count >= MinShots && ShooterErrors.SampleSd(windVals) > 0;
        var windTerm = shots.Select((s, i) => s.WindMps.HasValue ? s.WindMps.Value * dist[i] / 100 : 0.0).ToArray();

        // A shot is out of the scatter when by chance one clean match in twenty would show one (the radius of a shot is Rayleigh: P(r > z sigma) = exp(-z^2/2))
        double zLim = Math.Max(3.0, Math.Sqrt(2 * Math.Log(20.0 * n)));

        // The models are fitted on the shots in idx; every shot is then taken apart with them.
        Pass Run(int[] idx)
        {
            double[] Sub(double[] a) => idx.Select(i => a[i]).ToArray();
            var pass = new Pass();
            var colsU = new List<(string Name, double[] Col)>();
            if (hasVel) colsU.Add(("vel", Sub(velTerm)));
            if (hasDist) colsU.Add(("dist", Sub(dh)));
            var uSub = Sub(u);
            var full = colsU.Count > 0 ? Fit(uSub, colsU) : null;
            var fuL = FitEliminating(uSub, colsU)!;
            if (hasVel && full is not null && full.Index("vel") is int vi and > 0)
            {
                pass.VelMeasured = full.Beta[vi];
                pass.VelP = full.P[vi];
                double t = full.Beta[vi] / Math.Max(full.Se[vi], 1e-12);
                pass.VelPartial = full.Se[vi] <= 0 ? 1 : t * t / (t * t + full.Dof);
            }
            pass.VelKept = hasVel && fuL.Index("vel") is int vk and > 0 && fuL.Beta[vk] > 0;
            pass.VelB = 1;
            if (pass.VelKept) pass.VelB = fuL.Beta[fuL.Index("vel")];
            else if (hasVel)
            {
                // not shown by the numbers: the physics is still subtracted (the cartridge did have that velocity), but nothing is claimed
                var uAdj = Enumerable.Range(0, idx.Length).Select(k => uSub[k] - velTerm[idx[k]]).ToArray();
                fuL = FitEliminating(uAdj, hasDist ? new() { ("dist", Sub(dh)) } : new())!;
            }
            var colsX = new List<(string Name, double[] Col)>();
            if (hasWind) colsX.Add(("wind", Sub(windTerm)));
            if (hasDist) colsX.Add(("dist", Sub(dh)));
            var fxL = FitEliminating(Sub(x), colsX)!;
            pass.Fu = fuL;
            pass.Fx = fxL;
            pass.UseBiasU = fuL.P[0] < 0.05 && Math.Abs(fuL.Beta[0]) >= notice;
            pass.UseBiasX = fxL.P[0] < 0.05 && Math.Abs(fxL.Beta[0]) >= notice;
            int iDU = fuL.Index("dist"), iDX = fxL.Index("dist"), iW = fxL.Index("wind");
            pass.VelPart = new double[n]; pass.SettingsU = new double[n]; pass.SettingsX = new double[n]; pass.WindPart = new double[n]; pass.Ex = new double[n]; pass.Eu = new double[n];
            for (int i = 0; i < n; i++)
            {
                pass.VelPart[i] = hasVel ? velTerm[i] * pass.VelB : 0;
                pass.SettingsU[i] = (pass.UseBiasU ? fuL.Beta[0] : 0) + (iDU > 0 ? fuL.Beta[iDU] * dh[i] : 0);
                pass.SettingsX[i] = (pass.UseBiasX ? fxL.Beta[0] : 0) + (iDX > 0 ? fxL.Beta[iDX] * dh[i] : 0);
                pass.WindPart[i] = iW > 0 ? fxL.Beta[iW] * windTerm[i] : 0;
                pass.Eu[i] = u[i] - pass.VelPart[i] - pass.SettingsU[i];
                pass.Ex[i] = x[i] - pass.SettingsX[i] - pass.WindPart[i];
            }
            pass.Sx = Math.Sqrt(idx.Sum(i => pass.Ex[i] * pass.Ex[i]) / Math.Max(fxL.Dof, 1));
            pass.Su = Math.Sqrt(idx.Sum(i => pass.Eu[i] * pass.Eu[i]) / Math.Max(fuL.Dof, 1));
            pass.Sigma = Math.Max(Math.Sqrt((pass.Sx * pass.Sx + pass.Su * pass.Su) / 2), 0.01);
            return pass;
        }

        // First on every shot; a shot far out of what the others show (on a scale that one bad shot cannot inflate) is then left out of the fit,
        // so that it does not hide the cartridge, the settings or the wind, and the models are fitted again. Every shot is diagnosed with them.
        var all = Enumerable.Range(0, n).ToArray();
        var first = Run(all);
        var radial = Enumerable.Range(0, n).Select(i => Math.Sqrt(first.Ex[i] * first.Ex[i] + first.Eu[i] * first.Eu[i])).ToArray();
        double sigmaRob = Math.Max(radial.OrderBy(v => v).ElementAt(n / 2) / 1.1774, 0.01);   // the median of a Rayleigh radius is 1.1774 sigma
        var kept = all.Where(i => radial[i] / sigmaRob <= zLim).ToArray();
        var pass1 = kept.Length < n && kept.Length >= MinShots ? Run(kept) : first;

        var fu = pass1.Fu!;
        var fx = pass1.Fx!;
        bool velKept = pass1.VelKept;
        double velB = pass1.VelB;
        double? velMeasured = pass1.VelMeasured, velP = pass1.VelP, velPartial = pass1.VelPartial;
        bool useBiasU = pass1.UseBiasU, useBiasX = pass1.UseBiasX;
        int iDistU = fu.Index("dist"), iDistX = fx.Index("dist"), iWind = fx.Index("wind");
        var velPart = pass1.VelPart;
        var settingsU = pass1.SettingsU;
        var settingsX = pass1.SettingsX;
        var windPart = pass1.WindPart;
        var ex = pass1.Ex;
        var eu = pass1.Eu;
        double sx = pass1.Sx, su = pass1.Su, sigma = pass1.Sigma;
        var z = Enumerable.Range(0, n).Select(i => Math.Sqrt(ex[i] * ex[i] + eu[i] * eu[i]) / sigma).ToArray();

        var findings = new List<MatchFinding>();
        var fine = new List<string>();
        void Add(MatchOrigin o, string key, string title, string detail, string advice, double? p) => findings.Add(new MatchFinding(o, key, title, detail, advice, p, p is >= Strong));
        var advice = ShooterErrors.Advice(opt.Mode);

        // ---------------- the cartridges ----------------
        double maxMoaPerMps = hasVel ? ShooterErrors.MmPerMps(maxD, vMean) / (mmAt100 * maxD / 100) : 0;
        if (hasVel)
        {
            double es = vAll.Max() - vAll.Min(), cost = vSd * maxMoaPerMps;
            if (velKept && cost * velB >= notice)
                Add(MatchOrigin.Cartridge, "velocity", Tr.T("The velocity of the cartridges moves the shots"),
                    Tr.F("Once the distance is taken into account, the velocity explains {0} % of the vertical error that is left: the shots with a higher velocity go higher, {1} times what the drop without air predicts (the probability that it is chance is {2} %). The spread of the velocity ({3} m/s, extreme spread {4} m/s) alone opens the group by {5} MOA at {6} m.",
                        F((velPartial ?? 0) * 100, 0), F(velB, 1), F((velP ?? 1) * 100, 1), F(vSd, 1), F(es, 0), F(cost, 2), F(maxD, 0)),
                    Tr.T("Reduce the spread of the velocity: weigh every charge, sort the cases by weight and volume, keep the same neck tension and the same primers, and check a string of the same lot on the chronograph before the match."), velP);
            else
                fine.Add(Tr.F("The velocity of the cartridges does not move the shots: its spread ({0} m/s) would open the group by only {1} MOA at {2} m.", F(vSd, 1), F(cost, 2), F(maxD, 0)));

            if (vAll.Count >= 8)
            {
                // each velocity against the mean and the spread of all the others (a Student t on n-2 degrees of freedom), with the n shots read as one test:
                // out of line when by chance one clean match in twenty would show one
                var withV = Enumerable.Range(0, n).Where(i => shots[i].VelocityMps is > 0).ToList();
                var odd = new List<int>();
                foreach (int i in withV)
                {
                    var others = withV.Where(j => j != i).Select(j => shots[j].VelocityMps!.Value).ToList();
                    double so = ShooterErrors.SampleSd(others);
                    if (so <= 0) continue;
                    double t = (shots[i].VelocityMps!.Value - others.Average()) / (so * Math.Sqrt(1.0 + 1.0 / others.Count));
                    double pOdd = 2 * (1 - StudentT.Cdf(Math.Abs(t), others.Count - 1));
                    if (withV.Count * pOdd < 0.05 && Math.Abs(velTerm[i] * velB) >= notice) odd.Add(i);
                }
                odd = odd.OrderBy(i => shots[i].Number).ToList();
                if (odd.Count > 0)
                    Add(MatchOrigin.Cartridge, "velocity_outliers", Tr.T("Cartridges with a velocity out of line"),
                        Tr.T("Cartridges whose velocity is far from all the others:") + Environment.NewLine + "• " + string.Join(Environment.NewLine + "• ", odd.Select(i =>
                            Tr.F("Shot {0} ({1}, {2} m): {3} m/s, {4} m/s from the average; at that distance it moves the shot {5} MOA {6}",
                                shots[i].Number, shots[i].Target, F(dist[i], 0), F(shots[i].VelocityMps!.Value, 0), F(dv[i], 1, true), F(Math.Abs(velTerm[i]), 2), Dir(velTerm[i], Tr.T("high"), Tr.T("low"))))),
                        Tr.T("A cartridge out of line: look at the others of the same lot (charge, primer seating, case) and mark the cartridges so that you know which one it was."), null);
            }
        }

        var lotGroups = Groups(shots, s => s.Lot);
        if (lotGroups.Count >= 2)
        {
            double pu = Anova(lotGroups.Select(g => g.Select(i => eu[i]).ToList()).ToList()), px = Anova(lotGroups.Select(g => g.Select(i => ex[i]).ToList()).ToList());
            if (Pair(pu, px) < 0.05 && Spread(lotGroups, ex, eu) >= notice)
                Add(MatchOrigin.Cartridge, "lots", Tr.T("Cartridges of different lots shoot to different points"),
                    Tr.F("Not explained by the velocity: {0} (vertical: the probability that it is chance is {1} %, sideways: {2} %).",
                        string.Join("; ", lotGroups.Select(g => Tr.F("{0}: {1} MOA {2}, {3} MOA {4}", shots[g[0]].Lot!, F(Math.Abs(g.Average(i => eu[i])), 2), Dir(g.Average(i => eu[i]), Tr.T("high"), Tr.T("low")),
                            F(Math.Abs(g.Average(i => ex[i])), 2), Dir(g.Average(i => ex[i]), Tr.T("right"), Tr.T("left"))))), F(pu * 100, 1), F(px * 100, 1)),
                    Tr.T("Zero the rifle for each lot, or find what changes from one lot to the other: lot of powder, of primers, of bullets, seating depth, case brand."), Pair(pu, px));
            else
                fine.Add(Tr.T("The lots shoot to the same point."));
        }

        // ---------------- the settings ----------------
        var clicks = new List<ClickMoa>();
        double pZero = Pair(useBiasX ? fx.P[0] : 1, useBiasU ? fu.P[0] : 1);
        if ((useBiasU || useBiasX) && pZero < 0.05)
        {
            var list = clickName is not null ? new[] { clickName } : new[] { "1/4 MOA", "1/10 mrad" };       // the click is not known: the two most common
            double bx = useBiasX ? fx.Beta[0] : 0, bu = useBiasU ? fu.Beta[0] : 0;
            foreach (string name in list)
            {
                double c = ScopeClicks.InMoa(name)!.Value * perMoa;                                              // the click in the unit of the report
                clicks.Add(new ClickMoa(name, c, -(int)Math.Round(bx / c, MidpointRounding.ToEven), -(int)Math.Round(bu / c, MidpointRounding.ToEven)));
            }
            string fix = string.Join(" ", clicks.Select(c => Tr.F("With {0} clicks: {1}.", c.Name, ClickText(c))));
            Add(MatchOrigin.Settings, "zero", Tr.T("The zero of the sight is not right"),
                Tr.F("On average, at the middle distance of the targets ({0} m), the shots are {1} MOA to the {2} and {3} MOA {4} of the point of aim (the probability that it is chance is {5} %).",
                    F(meanD, 0), F(Math.Abs(bx), 2), Dir(bx, Tr.T("right"), Tr.T("left")), F(Math.Abs(bu), 2), Dir(bu, Tr.T("above"), Tr.T("below")), F(Math.Min(useBiasX ? fx.P[0] : 1, useBiasU ? fu.P[0] : 1) * 100, 2)) + " " + fix,
                Tr.T("After the proving shots look at where the CENTRE of the group is, not the single shot: if it is more than half a click off, correct the sight before the series (and again before each return, because the point of impact moves with the heat of the barrel)."),
                pZero);
        }
        else if (!useBiasU && !useBiasX)
            fine.Add(Tr.F("The zero of the sight is right: on average the shots are {0} / {1} MOA from the point of aim, which cannot be told from nothing.", F(fx.Beta[0], 2, true), F(fu.Beta[0], 2, true)));

        if (hasDist)
        {
            double span = (maxD - minD) / 100;
            if (iDistU > 0 && Math.Abs(fu.Beta[iDistU]) * span >= notice)
            {
                double b = fu.Beta[iDistU];
                Add(MatchOrigin.Settings, "elevation", Tr.T("The elevation does not follow the distance"),
                    Tr.F("The farther the target, the {0} the shots land: {1} MOA more for every 100 m ({2} MOA between {3} m and {4} m; the probability that it is chance is {5} %).",
                        Dir(b, Tr.T("higher"), Tr.T("lower")), F(Math.Abs(b), 2), F(Math.Abs(b) * span, 2), F(minD, 0), F(maxD, 0), F(fu.P[iDistU] * 100, 2)),
                    Tr.T("The elevations do not suit the rifle: check the muzzle velocity and the ballistic coefficient used in the ballistic program, the distance of the zero, the height of the scope over the bore, the air (temperature and pressure) and that a click is really worth what it says (dial 10 MOA up and check on a target)."), fu.P[iDistU]);
            }
            else
                fine.Add(Tr.T("The elevation follows the distance: the vertical error does not change with it."));
            if (iDistX > 0 && Math.Abs(fx.Beta[iDistX]) * span >= notice)
            {
                double b = fx.Beta[iDistX];
                Add(MatchOrigin.Settings, "lateral_distance", Tr.T("The sideways error grows with the distance"),
                    Tr.F("The farther the target, the more the shots go to the {0}: {1} MOA more for every 100 m ({2} MOA between {3} m and {4} m; the probability that it is chance is {5} %).",
                        Dir(b, Tr.T("right"), Tr.T("left")), F(Math.Abs(b), 2), F(Math.Abs(b) * span, 2), F(minD, 0), F(maxD, 0), F(fx.P[iDistX] * 100, 2)),
                    Tr.T("Typical causes: scope not level (the error grows with the elevation dialled, so with the distance: check the reticle with a level), windage zero not right at long range, spin drift not applied, a steady wind from one side all the match."), fx.P[iDistX]);
            }
        }

        // ---------------- the conditions: the wind ----------------
        if (hasWind)
        {
            if (iWind > 0 && Math.Abs(fx.Beta[iWind]) * windTerm.Max(v => Math.Abs(v)) >= notice)
            {
                double b = fx.Beta[iWind];
                Add(MatchOrigin.Conditions, "wind", b > 0 ? Tr.T("The wind is not corrected enough") : Tr.T("The wind is over-corrected"),
                    Tr.F("The shots {0} the wind: {1} MOA for every 1 m/s of across wind at 100 m of distance (the probability that it is chance is {2} %).",
                        b > 0 ? Tr.T("follow") : Tr.T("go against"), F(Math.Abs(b), 2), F(fx.P[iWind] * 100, 2)),
                    b > 0 ? Tr.T("You hold less than the wind needs: take the holds from a wind table or a ballistic program for the speed and the direction, and read the wind along the whole way to the target (the flags near the target count more than those at the firing line).")
                          : Tr.T("You hold more than the wind needs: you read more wind than there is, or you hold for the wind of the firing line; check the holds with a wind table and read the wind along the whole way to the target."), fx.P[iWind]);
            }
            else
                fine.Add(Tr.T("The wind is corrected well: the shots do not follow it."));
        }

        // ---------------- the shooter ----------------
        var posGroups = Groups(shots, s => s.Position);
        if (posGroups.Count >= 2)
        {
            double pu = Anova(posGroups.Select(g => g.Select(i => eu[i]).ToList()).ToList()), px = Anova(posGroups.Select(g => g.Select(i => ex[i]).ToList()).ToList());
            if (Pair(pu, px) < 0.05 && Spread(posGroups, ex, eu) >= notice)
                Add(MatchOrigin.Shooter, "positions", Tr.T("The position changes the point of impact"),
                    Tr.F("{0} (vertical: the probability that it is chance is {1} %, sideways: {2} %).",
                        string.Join("; ", posGroups.Select(g => Tr.F("{0}: {1} MOA {2}, {3} MOA {4}", shots[g[0]].Position!, F(Math.Abs(g.Average(i => eu[i])), 2), Dir(g.Average(i => eu[i]), Tr.T("high"), Tr.T("low")),
                            F(Math.Abs(g.Average(i => ex[i])), 2), Dir(g.Average(i => ex[i]), Tr.T("right"), Tr.T("left"))))), F(pu * 100, 1), F(px * 100, 1)),
                    Tr.T("The rifle is not held the same way in every position, so the zero moves with it: learn the shift of each position and check the support (sling, bag, bipod loading, cheek)."), Pair(pu, px));
            else
                fine.Add(Tr.T("The positions shoot to the same point."));
        }

        // the same target visited again and again (the barilotti of a card, shot in the same order in every series) and missed the same way
        var flaggedTargets = new Dictionary<string, (double Dx, double Du, int N, double P)>(StringComparer.OrdinalIgnoreCase);
        {
            var repeated = Enumerable.Range(0, n).Where(i => !string.IsNullOrWhiteSpace(shots[i].Target))
                .GroupBy(i => shots[i].Target.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= 2).ToList();
            int nRep = repeated.Sum(g => g.Count());
            if (repeated.Count >= 3 && nRep >= 8 && nRep - repeated.Count >= 5)
            {
                var idx = repeated.SelectMany(g => g).ToArray();
                var group = new int[idx.Length];
                int at = 0;
                for (int g = 0; g < repeated.Count; g++) foreach (int _ in repeated[g]) group[at++] = g;
                var gx = idx.Select(i => ex[i]).ToArray();
                var gu = idx.Select(i => eu[i]).ToArray();
                var obs = TargetStats(gx, gu, group, repeated.Count, out var devs);
                if (obs is not null)
                {
                    // how often the worst target of a shuffled match is as far off as the real one: the choice of the worst of many is allowed for
                    const int Perms = 2000;
                    var shuffle = new Random(20261119);
                    var px = (double[])gx.Clone();
                    var pu = (double[])gu.Clone();
                    var maxima = new double[Perms];
                    for (int k = 0; k < Perms; k++)
                    {
                        for (int i = px.Length - 1; i > 0; i--)
                        {
                            int j = shuffle.Next(i + 1);
                            (px[i], px[j]) = (px[j], px[i]);
                            (pu[i], pu[j]) = (pu[j], pu[i]);
                        }
                        maxima[k] = TargetStats(px, pu, group, repeated.Count, out _)!.Max();
                    }
                    var hits = new List<(int Group, double P)>();
                    for (int g = 0; g < repeated.Count; g++)
                    {
                        double adj = (1.0 + maxima.Count(m => m >= obs[g] - 1e-12)) / (1.0 + Perms);
                        double dx = devs[g].Dx, du = devs[g].Du;
                        if (adj < 0.05 && Math.Sqrt(dx * dx + du * du) >= notice) hits.Add((g, adj));
                    }
                    if (hits.Count > 0)
                    {
                        var lines = hits.OrderBy(h => h.P).Select(h =>
                        {
                            var members = repeated[h.Group].OrderBy(i => shots[i].Number).ToList();
                            var (dx, du) = devs[h.Group];
                            flaggedTargets[shots[members[0]].Target.Trim()] = (dx, du, members.Count, h.P);
                            return Tr.F("{0}: {1} visits (shots {2}), on average {3} MOA {4} and {5} MOA {6} of where the other targets go.", shots[members[0]].Target.Trim(), members.Count,
                                string.Join(", ", members.Select(i => shots[i].Number)), F(Math.Abs(dx), 2), Dir(dx, Tr.T("to the right"), Tr.T("to the left")), F(Math.Abs(du), 2), Dir(du, Tr.T("high"), Tr.T("low")));
                        });
                        Add(MatchOrigin.Shooter, "target_repeat", Tr.T("Some targets are always missed the same way"),
                            Tr.F("The shots on the same target, visited again, go to the same side more than shuffled shots would (the probability that it is chance is {0} %, counted on the worst of {1} targets):", F(hits.Min(h => h.P) * 100, 1), repeated.Count)
                                + Environment.NewLine + "• " + string.Join(Environment.NewLine + "• ", lines),
                            Tr.T("The same targets are shot in the same order every time, so what you see may be the place on the card or the place in the series (a hold that changes with the position, the barrel warming, tiredness): shoot the barilotti in a different order in one series to tell the two apart, and look at the sight picture and the position at those targets."),
                            hits.Min(h => h.P));
                    }
                    else
                        fine.Add(Tr.T("No target is missed the same way every time."));
                }
            }
        }

        // a shot at a target that is always missed the same way is judged against its own target's shift: what is left is the shooting of that shot
        var exLeft = (double[])ex.Clone();
        var euLeft = (double[])eu.Clone();
        for (int i = 0; i < n; i++)
            if (flaggedTargets.TryGetValue(shots[i].Target.Trim(), out var ftd))
            {
                exLeft[i] -= ftd.Dx;
                euLeft[i] -= ftd.Du;
                z[i] = Math.Sqrt(exLeft[i] * exLeft[i] + euLeft[i] * euLeft[i]) / sigma;
            }

        if (sx > 0 && su > 0 && n >= 8)
        {
            double f = sx * sx / (su * su);
            int dx = Math.Max(fx.Dof, 1), du = Math.Max(fu.Dof, 1);
            double pF = Math.Clamp(2 * Math.Min(1 - FDist.Cdf(f, dx, du), FDist.Cdf(f, dx, du)), 0, 1);
            if (pF < 0.05 && Math.Max(sx, su) >= notice)
            {
                string noWind = hasWind ? "" : " " + Tr.T("The wind was not recorded, so it cannot be told apart from the shooting: write the wind for each shot next time.");
                if (f > 1)
                    Add(MatchOrigin.Shooter, "horizontal", Tr.T("Sideways error bigger than the vertical one"),
                        Tr.F("After the cartridges, the settings and the wind are taken out, the shots spread {0} MOA sideways and {1} MOA in height (the probability that it is chance is {2} %).", F(sx, 2), F(su, 2), F(pF * 100, 1)) + noWind,
                        advice.Horizontal, pF);
                else
                    Add(MatchOrigin.Shooter, "vertical", Tr.T("Vertical error bigger than the sideways one"),
                        Tr.F("After the cartridges, the settings and the wind are taken out, the shots spread {0} MOA in height and {1} MOA sideways (the probability that it is chance is {2} %).", F(su, 2), F(sx, 2), F(pF * 100, 1)),
                        advice.Vertical, pF);
            }
            else
                fine.Add(Tr.F("Sideways ({0} MOA) and vertical ({1} MOA) are alike: neither direction is worse than the other.", F(sx, 2), F(su, 2)));
        }

        var flyers = Enumerable.Range(0, n).Where(i => z[i] > zLim).OrderByDescending(i => z[i]).ToList();
        if (flyers.Count > 0)
            Add(MatchOrigin.Shooter, "flyers", Tr.T("Shots out of the shooter's normal scatter"),
                Tr.F("Shots further from the aim than {0} times the normal scatter, once the cartridge, the settings and the wind are taken out:", F(zLim, 1)) + Environment.NewLine + "• " +
                string.Join(Environment.NewLine + "• ", flyers.Select(i => Tr.F("Shot {0} ({1}, {2} m): {3} MOA {4} and {5} MOA {6} more than the others would explain", shots[i].Number, shots[i].Target, F(dist[i], 0),
                    F(Math.Abs(exLeft[i]), 2), Dir(exLeft[i], Tr.T("to the right"), Tr.T("to the left")), F(Math.Abs(euLeft[i]), 2), Dir(euLeft[i], Tr.T("high"), Tr.T("low"))))),
                Tr.T("A badly fired shot (pulled, a flinch, a changed position) or a gust of wind that was not seen: write on the target at once what you felt, so that next time you know which of the two it was."), null);

        // the point of impact moving during the match
        if (n >= 8)
        {
            var order = Enumerable.Range(1, n).Select(v => (double)v).ToArray();
            foreach (var (col, label, pos, neg, key) in new[]
            {
                (ex, Tr.T("sideways"), Tr.T("to the right"), Tr.T("to the left"), "drift_x"), (eu, Tr.T("in height"), Tr.T("upwards"), Tr.T("downwards"), "drift_u"),
            })
            {
                var reg = VelocityModel.SimpleRegression(order, col);
                if (2 * reg.P < 0.05 && Math.Abs(reg.Slope) * (n - 1) >= notice)
                    Add(MatchOrigin.Shooter, key, Tr.F("The shot moves {0} during the match", label),
                        Tr.F("From the first shot to the last the point of impact moves {0} MOA {1} (the probability that it is chance is {2} %).", F(Math.Abs(reg.Slope) * (n - 1), 2), reg.Slope >= 0 ? pos : neg, F(reg.P * 100, 1)),
                        Tr.T("A position that settles or tires during the series, a bipod or a bag that slips, light or mirage that change: check the position again in the middle of the series."), Math.Min(1, 2 * reg.P));
            }
        }

        // the time kept between shots (the move from one target to the next is in it)
        var gaps = new List<(double Gap, double R)>();
        for (int i = 1; i < n; i++)
            if (shots[i].ClockSeconds is { } t1 && shots[i - 1].ClockSeconds is { } t0 && t1 > t0) gaps.Add((t1 - t0, Math.Sqrt(ex[i] * ex[i] + eu[i] * eu[i])));
        if (gaps.Count >= 8)
        {
            var (rho, p) = ShooterErrors.Spearman(gaps.Select(g => g.Gap).ToArray(), gaps.Select(g => g.R).ToArray());
            double meanGap = gaps.Average(g => g.Gap);
            if (p < 0.05 && rho > 0)
                Add(MatchOrigin.Shooter, "hold_long", Tr.T("Shots held a long time go worse"),
                    Tr.F("The more time passes from the shot before (the move to the next target is included), the further the shot goes from where it should (Spearman {0}, the probability that it is chance is {1} %). On average {2} s between one shot and the next.", F(rho, 2), F(p * 100, 1), F(meanGap, 0)),
                    Tr.T("If the shot does not go within a few seconds of the aim being steady, come off, breathe and start again."), p);
            else if (p < 0.05)
                Add(MatchOrigin.Shooter, "rushed", Tr.T("Shots fired in a hurry go worse"),
                    Tr.F("Shots fired soon after the previous one go further from where they should (Spearman {0}, the probability that it is chance is {1} %).", F(rho, 2), F(p * 100, 1)),
                    Tr.T("After each shot put yourself back in position and check the wind again before firing the next."), p);
            else
                fine.Add(Tr.F("Regular pace ({0} s between one shot and the next on average), not linked to the errors.", F(meanGap, 0)));
        }

        // a bad shot dragging the next one
        if (n >= 9)
        {
            var cur = new double[n - 1];
            var prev = new double[n - 1];
            for (int i = 1; i < n; i++) { cur[i - 1] = Math.Sqrt(ex[i] * ex[i] + eu[i] * eu[i]); prev[i - 1] = Math.Sqrt(ex[i - 1] * ex[i - 1] + eu[i - 1] * eu[i - 1]); }
            var (rho, p) = ShooterErrors.Spearman(prev, cur);
            if (p < 0.05 && rho > 0)
                Add(MatchOrigin.Shooter, "chain", Tr.T("A bad shot drags the next one"),
                    Tr.F("The shot after a bad shot is also worse (Spearman {0}, the probability that it is chance is {1} %).", F(rho, 2), F(p * 100, 1)),
                    Tr.T("After a bad shot do not rush: come off, breathe, find the cause and write it down, then put yourself back in position before the next target."), p);
            else
                fine.Add(Tr.T("A bad shot does not drag the next one."));
        }

        // ---------------- shot by shot ----------------
        // A shot is named for a part only when that part stands (p under 1 %); a part that is only a hint is left to the shooting.
        bool sVel = velKept && fu.P[fu.Index("vel")] < Strong;
        bool sBiasU = useBiasU && fu.P[0] < Strong, sBiasX = useBiasX && fx.P[0] < Strong;
        bool sDistU = iDistU > 0 && fu.P[iDistU] < Strong, sDistX = iDistX > 0 && fx.P[iDistX] < Strong, sWind = iWind > 0 && fx.P[iWind] < Strong;
        string TargetText(string label, (double Dx, double Du, int N, double P) t)
        {
            string text = Tr.F("{0}: always the same target. It is missed {1} MOA {2} and {3} MOA {4} on average, in {5} visits.", label,
                F(Math.Abs(t.Dx), 2), Dir(t.Dx, Tr.T("to the right"), Tr.T("to the left")), F(Math.Abs(t.Du), 2), Dir(t.Du, Tr.T("high"), Tr.T("low")), t.N);
            return t.P >= Strong ? Tr.F("{0} (to be confirmed)", text) : text;
        }
        var diag = new List<ShotDiagnosis>();
        for (int i = 0; i < n; i++)
        {
            double sU = (sBiasU ? fu.Beta[0] : 0) + (sDistU ? fu.Beta[iDistU] * dh[i] : 0), sX = (sBiasX ? fx.Beta[0] : 0) + (sDistX ? fx.Beta[iDistX] * dh[i] : 0);
            double cart = sVel ? Math.Abs(velPart[i]) : 0, sett = Math.Sqrt(sX * sX + sU * sU), wnd = sWind ? Math.Abs(windPart[i]) : 0, exe = Math.Sqrt(exLeft[i] * exLeft[i] + euLeft[i] * euLeft[i]);
            var cand = new List<(string Cause, double Size)>();
            if (cart >= notice) cand.Add(("cartridge", cart));
            if (sett >= notice) cand.Add(("settings", sett));
            if (wnd >= notice) cand.Add(("wind", wnd));
            if (z[i] > zLim) cand.Add(("execution", exe));
            if (flaggedTargets.TryGetValue(shots[i].Target.Trim(), out var ft) && Math.Sqrt(ft.Dx * ft.Dx + ft.Du * ft.Du) >= notice) cand.Add(("target", Math.Sqrt(ft.Dx * ft.Dx + ft.Du * ft.Du)));
            string cause = cand.Count == 0 ? "normal" : cand.OrderByDescending(c => c.Size).First().Cause;
            string label = Tr.F("Shot {0} ({1}, {2} m)", shots[i].Number, shots[i].Target, F(dist[i], 0));
            string text = cause switch
            {
                "cartridge" => Tr.F("{0}: the cartridge. Its velocity ({1} m/s from the average) moves the shot {2} MOA {3}.", label, F(dv[i], 1, true), F(cart, 2), Dir(velPart[i], Tr.T("high"), Tr.T("low"))),
                "settings" => Tr.F("{0}: the settings. The zero and the elevation or windage at this distance leave the shot {1} MOA {2} and {3} MOA {4}.", label,
                    F(Math.Abs(sX), 2), Dir(sX, Tr.T("to the right"), Tr.T("to the left")), F(Math.Abs(sU), 2), Dir(sU, Tr.T("high"), Tr.T("low"))),
                "wind" => Tr.F("{0}: the wind. {1} m/s across was {2}: {3} MOA {4}.", label, F(shots[i].WindMps ?? 0, 1, true), (iWind > 0 && fx.Beta[iWind] > 0) ? Tr.T("not corrected enough") : Tr.T("over-corrected"),
                    F(wnd, 2), Dir(windPart[i], Tr.T("to the right"), Tr.T("to the left"))),
                "target" => TargetText(label, flaggedTargets[shots[i].Target.Trim()]),
                "execution" => Tr.F("{0}: the shooting. {1} times the normal scatter once the cartridge, the settings and the wind are taken out.", label, F(z[i], 1)),
                _ => "",
            };
            diag.Add(new ShotDiagnosis(shots[i].Number, shots[i].Target, dist[i], x[i], u[i], cart, sett, wnd, exe, cause, text));
        }

        return new MatchReport
        {
            N = n, Targets = shots.Select(s => s.Target).Distinct().Count(), MinDistanceM = minD, MaxDistanceM = maxD,
            CentreXMoa = x.Average(), CentreUpMoa = u.Average(), MeanErrorMoa = diag.Average(d => d.TotalMoa),
            MeanCartridgeMoa = diag.Average(d => d.CartridgeMoa), MeanSettingsMoa = diag.Average(d => d.SettingsMoa), MeanWindMoa = diag.Average(d => d.WindMoa), MeanExecutionMoa = diag.Average(d => d.ExecutionMoa),
            NoticeMoa = notice, AngleUnit = mrad ? "mrad" : "moa",
            Shots = diag, Clicks = clicks, Fine = fine,
            Findings = findings.OrderBy(f => f.Origin).ThenBy(f => f.P ?? 1).ToList(),
        };
    }

    // ---- helpers --------------------------------------------------------------------------------------------------------------

    private static string ClickText(ClickMoa c)
    {
        var parts = new List<string>();
        if (c.Up != 0) parts.Add(Math.Abs(c.Up).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + (c.Up > 0 ? Tr.T("up") : Tr.T("down")));
        if (c.Right != 0) parts.Add(Math.Abs(c.Right).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + (c.Right > 0 ? Tr.T("to the right") : Tr.T("to the left")));
        return parts.Count > 0 ? string.Join(" " + Tr.T("and") + " ", parts) : Tr.T("no click (too coarse)");
    }

    /// <summary>The shots that share a label (lot, position): only labels with at least 3 shots count, and at least two of them are needed.</summary>
    private static List<List<int>> Groups(List<MatchShot> shots, Func<MatchShot, string?> label)
    {
        var groups = Enumerable.Range(0, shots.Count).Where(i => !string.IsNullOrWhiteSpace(label(shots[i]))).GroupBy(i => label(shots[i])!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 3).Select(g => g.ToList()).ToList();
        return groups.Count >= 2 ? groups : new List<List<int>>();
    }

    // ---- the least squares with a test for every term -------------------------------------------------------------------

    private sealed class Pass
    {
        public Model? Fu, Fx;
        public bool VelKept, UseBiasU, UseBiasX;
        public double VelB, Sx, Su, Sigma;
        public double? VelMeasured, VelP, VelPartial;
        public double[] VelPart = Array.Empty<double>(), SettingsU = Array.Empty<double>(), SettingsX = Array.Empty<double>(), WindPart = Array.Empty<double>(), Ex = Array.Empty<double>(), Eu = Array.Empty<double>();
    }

    private sealed class Model
    {
        public required string[] Names { get; init; }     // Names[0] is the constant
        public required double[] Beta { get; init; }
        public required double[] Se { get; init; }
        public required double[] P { get; init; }
        public int Dof { get; init; }
        public int Index(string name) => Array.IndexOf(Names, name);
    }

    private static Model? Fit(double[] y, List<(string Name, double[] Col)> cols)
    {
        int n = y.Length, p = cols.Count + 1;
        if (n - p < 1) return null;
        var X = new double[n][];
        for (int i = 0; i < n; i++)
        {
            X[i] = new double[p];
            X[i][0] = 1;
            for (int j = 0; j < cols.Count; j++) X[i][j + 1] = cols[j].Col[i];
        }
        var f = VelocityModel.Ols(X, y);
        if (f is null) return null;
        int dof = n - p;
        double s2 = f.Residuals.Sum(r => r * r) / dof;
        var se = new double[p];
        var pv = new double[p];
        for (int j = 0; j < p; j++)
        {
            se[j] = Math.Sqrt(Math.Max(s2 * f.XtXInverse[j][j], 0));
            pv[j] = se[j] <= 1e-12 ? (Math.Abs(f.Beta[j]) > 1e-9 ? 0 : 1) : Math.Clamp(2 * (1 - StudentT.Cdf(Math.Abs(f.Beta[j] / se[j]), dof)), 0, 1);
        }
        return new Model { Names = new[] { "const" }.Concat(cols.Select(c => c.Name)).ToArray(), Beta = f.Beta, Se = se, P = pv, Dof = dof };
    }

    /// <summary>Fits the constant and the given columns, then drops the term that is least sure until every one left has p under 5 %. The constant stays.</summary>
    private static Model? FitEliminating(double[] y, List<(string Name, double[] Col)> columns)
    {
        var cur = columns.ToList();
        while (true)
        {
            while (cur.Count > 0 && y.Length - (cur.Count + 1) < 3) cur.RemoveAt(cur.Count - 1);
            var m = Fit(y, cur);
            if (m is null)
            {
                if (cur.Count == 0) return null;
                cur.RemoveAt(cur.Count - 1);
                continue;
            }
            int worst = -1;
            double wp = 0.05;
            for (int j = 1; j < m.P.Length; j++) if (m.P[j] > wp) { wp = m.P[j]; worst = j; }
            if (worst < 0) return m;
            cur.RemoveAt(worst - 1);
        }
    }

    /// <summary>
    /// For each group of shots, how far its average point is from the average of all of them, in units of the scatter of the shots inside the groups
    /// (the Mahalanobis distance with the pooled covariance, times the number of shots of the group). Null when the groups leave too few degrees of freedom.
    /// </summary>
    private static double[]? TargetStats(double[] x, double[] u, int[] group, int groups, out (double Dx, double Du)[] devs)
    {
        int n = x.Length;
        devs = new (double, double)[groups];
        var cnt = new int[groups];
        var mx = new double[groups];
        var mu = new double[groups];
        for (int i = 0; i < n; i++) { cnt[group[i]]++; mx[group[i]] += x[i]; mu[group[i]] += u[i]; }
        for (int g = 0; g < groups; g++) { mx[g] /= cnt[g]; mu[g] /= cnt[g]; }
        double gx = x.Average(), gu = u.Average(), sxx = 0, suu = 0, sxu = 0;
        for (int i = 0; i < n; i++)
        {
            double a = x[i] - mx[group[i]], b = u[i] - mu[group[i]];
            sxx += a * a; suu += b * b; sxu += a * b;
        }
        int dof = n - groups;
        if (dof < 1) return null;
        sxx /= dof; suu /= dof; sxu /= dof;
        double det = sxx * suu - sxu * sxu;
        if (det <= 1e-12) return null;
        var stat = new double[groups];
        for (int g = 0; g < groups; g++)
        {
            double dx = mx[g] - gx, du = mu[g] - gu;
            devs[g] = (dx, du);
            stat[g] = cnt[g] * (dx * dx * suu - 2 * dx * du * sxu + du * du * sxx) / det;
        }
        return stat;
    }

    /// <summary>The largest distance between the average points of two groups (MOA).</summary>
    private static double Spread(List<List<int>> groups, double[] ex, double[] eu)
    {
        var c = groups.Select(g => (X: g.Average(i => ex[i]), U: g.Average(i => eu[i]))).ToList();
        double best = 0;
        for (int a = 0; a < c.Count; a++)
            for (int b = a + 1; b < c.Count; b++) best = Math.Max(best, Math.Sqrt(Math.Pow(c[a].X - c[b].X, 2) + Math.Pow(c[a].U - c[b].U, 2)));
        return best;
    }

    /// <summary>One-way analysis of variance: the p that the groups have the same mean.</summary>
    public static double Anova(IReadOnlyList<List<double>> groups)
    {
        int k = groups.Count, total = groups.Sum(g => g.Count);
        if (k < 2 || total - k < 1) return 1;
        double grand = groups.SelectMany(g => g).Average();
        double ssb = groups.Sum(g => g.Count * Math.Pow(g.Average() - grand, 2));
        double ssw = groups.Sum(g => { double m = g.Average(); return g.Sum(v => (v - m) * (v - m)); });
        if (ssw <= 0) return ssb > 0 ? 0 : 1;
        double f = (ssb / (k - 1)) / (ssw / (total - k));
        return Math.Clamp(1 - FDist.Cdf(f, k - 1, total - k), 0, 1);
    }
}

