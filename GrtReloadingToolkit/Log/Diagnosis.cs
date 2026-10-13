using System.Globalization;

namespace GrtReloadingToolkit.Log;

/// <summary>How far a hypothesis can be trusted. "High" is never given by a single rule: it needs two independent sources to agree.</summary>
public enum DiagnosisConfidence { Low, Medium, High }

/// <summary>One string of the open load: the shots of one charge, with what is known about how it was shot.</summary>
/// <param name="ColdShots">For each shot, whether it was fired from a cold bore (<c>COLD=1</c> in its note, see <see cref="DataTokens"/>); null when none is marked.</param>
public sealed record DiagnosisString(string Name, double? ChargeGr, IReadOnlyList<double> VelocitiesMps, double? TempC, double? GroupMoa, IReadOnlyList<bool>? ColdShots = null);

/// <summary>A shot group as the observation on wind and support needs it. <paramref name="WindMps"/> comes from <c>WIND=…</c> in the tab title or the group name.</summary>
public sealed record DiagnosisGroup(string Name, double DistanceM, double HorizontalMoa, double VerticalMoa, int Shots, double? WindMps, double? WindDegrees);

/// <summary>The node the ladder analysis suggests, in the same unit as the chosen charge.</summary>
public sealed record DiagnosisNode(double Low, double High);

/// <summary>
/// Everything the guided diagnosis reads, taken from the open load by the window and kept free of WinForms so the rules can be tested.
/// <paramref name="Vel"/> turns a velocity in m/s into text in the unit GRT shows (with the unit), so the findings quote what the shooter sees.
/// </summary>
public sealed record DiagnosisInput(
    IReadOnlyList<DiagnosisString> Strings,
    double? ChosenCharge,
    bool NodeSearched,
    DiagnosisNode? Node,
    Func<double, string> Vel,
    IReadOnlyList<DiagnosisGroup>? Groups = null);

/// <summary>A finding: what may be going on, on what numbers, how sure, and the test that would confirm it.
/// Templates are English translation keys with {0}, {1}... for the numbers, filled in by the window.</summary>
public sealed record DiagnosisFinding(string Rule, string Template, object[] Args, DiagnosisConfidence Confidence, string ProofTemplate, int ReturnToPhase);

/// <summary>A rule that could not be applied, and why, so the shooter knows what measurement is missing rather than assuming all is well.</summary>
public sealed record DiagnosisGap(string Rule, string Template, object[] Args);

/// <summary>A number the toolkit can read off the data but whose cause it does not decide: the rule behind it is not validated yet, so it is shown as an
/// observation, and the community is asked whether the reading fits their cases. Same templates as a finding.</summary>
public sealed record DiagnosisObservation(string Rule, string Template, object[] Args);

public sealed record DiagnosisResult(IReadOnlyList<DiagnosisFinding> Findings, IReadOnlyList<DiagnosisGap> Gaps, IReadOnlyList<DiagnosisObservation> Observations);

/// <summary>
/// Guided diagnosis (step 17 of the recommended path): from the strings and groups of the open load, the hypotheses the data support.
/// Experimental by design: every rule reuses a detector the toolkit already has and its tiers (no new thresholds), every finding carries
/// its numbers, a confidence and the test that would confirm it, and a rule without enough data says so instead of staying quiet.
/// Rules R1, R2, R6, R7 live here; the manual lists the others (R3, R4, R5, R8) as waiting for data nobody has yet.
/// </summary>
public static class Diagnosis
{
    /// <summary>Fewest shots at which a string's spread is read at all.</summary>
    public const int MinShots = 5;

    /// <summary>Shots from which a finding is Medium rather than Low.</summary>
    public const int ShotsForMedium = 10;

    /// <summary>Strings at least this far apart in temperature (degrees C) are compared for temperature sensitivity.</summary>
    public const double MinTemperatureGapC = 5.0;

    /// <summary>Shots from which a drift along a string is read.</summary>
    public const int MinShotsForDrift = 10;

    /// <summary>A group is read for wind and support from this distance on, with at least <see cref="MinShots"/> shots.</summary>
    public const double MinDistanceForWindM = 300.0;

    /// <summary>A horizontal-to-vertical spread ratio beyond this (or below its inverse) is worth a mention even without a wind reading.</summary>
    public const double SkewRatio = 1.5;

    /// <summary>A velocity score below this is "weak" (below Good in <see cref="LoadScoring.ScoreLabel"/>).</summary>
    public const double WeakScore = 6.0;

    public static DiagnosisResult Run(DiagnosisInput input)
    {
        var findings = new List<DiagnosisFinding>();
        var gaps = new List<DiagnosisGap>();
        var observations = new List<DiagnosisObservation>();
        RuleSpreadVersusGroup(input, findings, gaps);
        RuleSingleShot(input, findings, gaps);
        RuleTemperature(input, findings, gaps);
        RuleChosenChargeOffNode(input, findings, gaps);
        ObserveDrift(input, observations, gaps);
        ObserveColdBore(input, observations, gaps);
        ObserveWindAndSupport(input, observations, gaps);
        // strongest first; within a confidence, the order the rules ran in
        var ordered = findings.Select((f, i) => (f, i)).OrderByDescending(x => x.f.Confidence).ThenBy(x => x.i).Select(x => x.f).ToList();
        return new DiagnosisResult(ordered, gaps, observations);
    }

    private static string F(double v, string fmt = "0.0") => v.ToString(fmt, CultureInfo.InvariantCulture);

    private static double SampleSd(IReadOnlyList<double> v)
    {
        if (v.Count < 2) return 0;
        double mean = v.Average();
        return Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1));
    }

    private static DiagnosisConfidence ByShots(int n) => n >= ShotsForMedium ? DiagnosisConfidence.Medium : DiagnosisConfidence.Low;

    // R1 -- SD / ES high for their class while the group is not worse: the charge or the ignition, not the aim.
    private static void RuleSpreadVersusGroup(DiagnosisInput input, List<DiagnosisFinding> findings, List<DiagnosisGap> gaps)
    {
        var enough = input.Strings.Where(s => s.VelocitiesMps.Count >= MinShots).ToList();
        if (enough.Count == 0)
        {
            gaps.Add(new DiagnosisGap("R1", "R1 (spread of velocity against the group): not evaluated, no string has {0} shots or more.", new object[] { MinShots }));
            return;
        }
        if (enough.All(s => s.GroupMoa is null))
        {
            gaps.Add(new DiagnosisGap("R1", "R1 (spread of velocity against the group): not evaluated, no group is attached to a string. Load the shot groups into the same load.", Array.Empty<object>()));
            return;
        }
        foreach (var s in enough.Where(s => s.GroupMoa is not null))
        {
            double sd = SampleSd(s.VelocitiesMps);
            double es = s.VelocitiesMps.Max() - s.VelocitiesMps.Min();
            double velScore = Math.Min(LoadScoring.ScoreSd(sd), LoadScoring.ScoreEs(es));
            double grpScore = LoadScoring.ScoreGroupMoa(s.GroupMoa!.Value);
            if (velScore < WeakScore && grpScore >= WeakScore && grpScore - velScore >= 2.0)
                findings.Add(new DiagnosisFinding("R1",
                    "{0}: the velocity spread is high for its class (SD {1}, ES {2}, score {3}) while the group is not worse ({4} MOA, score {5}): the cause may be in the charge or the ignition (powder weight, primer, brass, neck tension), not in the aim.",
                    new object[] { s.Name, input.Vel(sd), input.Vel(es), F(velScore), F(s.GroupMoa.Value, "0.00"), F(grpScore) },
                    ByShots(s.VelocitiesMps.Count),
                    "Weigh the charges of a 10-shot string; try another primer lot; compare neck tension (Advanced diagnostics).", 2));
        }
    }

    // R2 -- one shot accounts for the spread: that shot, not the charge.
    private static void RuleSingleShot(DiagnosisInput input, List<DiagnosisFinding> findings, List<DiagnosisGap> gaps)
    {
        var enough = input.Strings.Where(s => s.VelocitiesMps.Count >= MinShots).ToList();
        if (enough.Count == 0)
        {
            gaps.Add(new DiagnosisGap("R2", "R2 (one shot behind the spread): not evaluated, no string has {0} shots or more.", new object[] { MinShots }));
            return;
        }
        foreach (var s in enough)
        {
            var ranking = SdRootCause.LeaveOneOutRanking(s.VelocitiesMps);
            if (SdRootCause.ClassifySdPattern(ranking) != SdRootCause.SdPatternKind.SingleOutlier) continue;
            var top = ranking.First(r => r.ChauvenetFlagged);
            double sd = SampleSd(s.VelocitiesMps);
            findings.Add(new DiagnosisFinding("R2",
                "{0}: one shot (number {1}, {2}) accounts for the spread: without it the SD falls from {3} to {4}. It points at that shot, not at the charge.",
                new object[] { s.Name, top.ShotIndex + 1, input.Vel(top.VelocityMps), input.Vel(sd), input.Vel(top.SdWithoutMps) },
                // Chauvenet is known to flag the fast first shot of a short string over and over (see ChronoStatsCalc.Analyze), and on a
                // real 5-shot string the first live run flagged a shot only 1.6 standard deviations out: so Medium only from ten shots.
                ByShots(s.VelocitiesMps.Count),
                "Repeat the string; check the chronograph (sensor, alignment) and where that shot sat in the string (the first? the last?).", 2));
        }
    }

    // R6 -- the same charge shot at two temperatures: does the velocity follow?
    private static void RuleTemperature(DiagnosisInput input, List<DiagnosisFinding> findings, List<DiagnosisGap> gaps)
    {
        var byCharge = input.Strings.Where(s => s.ChargeGr is not null && s.TempC is not null && s.VelocitiesMps.Count >= 2)
            .GroupBy(s => Math.Round(s.ChargeGr!.Value, 2)).Where(g => g.Count() >= 2).ToList();
        bool evaluated = false;
        foreach (var g in byCharge)
        {
            var cold = g.OrderBy(s => s.TempC).First();
            var hot = g.OrderByDescending(s => s.TempC).First();
            double gap = hot.TempC!.Value - cold.TempC!.Value;
            if (gap < MinTemperatureGapC) continue;
            evaluated = true;
            double d = hot.VelocitiesMps.Average() - cold.VelocitiesMps.Average();
            double se = Math.Sqrt(Math.Pow(SampleSd(hot.VelocitiesMps), 2) / hot.VelocitiesMps.Count + Math.Pow(SampleSd(cold.VelocitiesMps), 2) / cold.VelocitiesMps.Count);
            if (Math.Abs(d) <= 2 * se) continue;     // within the noise of the two strings: nothing to say
            bool enough = hot.VelocitiesMps.Count >= MinShots && cold.VelocitiesMps.Count >= MinShots;
            findings.Add(new DiagnosisFinding("R6",
                "Charge {0}: the strings at {1} C and {2} C differ by {3} ({4} m/s per degree C), more than their own spread explains: the powder may be sensitive to temperature.",
                new object[] { F(g.Key, "0.0#"), F(cold.TempC.Value), F(hot.TempC.Value), input.Vel(Math.Abs(d)), F(d / gap, "0.00") },
                enough ? DiagnosisConfidence.Medium : DiagnosisConfidence.Low,
                "Powder temp coefficients tool; shoot the same charge hot and cold with at least 5 shots each.", 2));
        }
        if (!evaluated)
            gaps.Add(new DiagnosisGap("R6", "R6 (temperature sensitivity): not evaluated, it needs the same charge shot at two temperatures at least {0} C apart.", new object[] { F(MinTemperatureGapC, "0") }));
    }

    // R7 -- the chosen charge lies outside the node the ladder suggests.
    private static void RuleChosenChargeOffNode(DiagnosisInput input, List<DiagnosisFinding> findings, List<DiagnosisGap> gaps)
    {
        if (input.ChosenCharge is null)
        {
            gaps.Add(new DiagnosisGap("R7", "R7 (chosen charge against the node): not evaluated, no charge has been chosen. Choose it in the ladder analyzer.", Array.Empty<object>()));
            return;
        }
        if (!input.NodeSearched || input.Node is null)
        {
            gaps.Add(new DiagnosisGap("R7", "R7 (chosen charge against the node): not evaluated, the ladder shows no node (it needs 3 or more charges with velocities and groups).", Array.Empty<object>()));
            return;
        }
        double c = input.ChosenCharge.Value, lo = input.Node.Low, hi = input.Node.High;
        const double tol = 0.005;
        if (c >= lo - tol && c <= hi + tol) return;
        double gap = c < lo ? lo - c : c - hi;
        double width = Math.Max(hi - lo, 1e-9);
        findings.Add(new DiagnosisFinding("R7",
            "The chosen charge {0} is outside the node the ladder suggests ({1} to {2}): its group may not sit on the flat spot of the ladder.",
            new object[] { F(c, "0.0#"), F(lo, "0.0#"), F(hi, "0.0#") },
            gap > width + tol ? DiagnosisConfidence.Medium : DiagnosisConfidence.Low,
            "Shoot a confirmation group at the middle of the node and compare it with the chosen charge.", 2));
    }

    // ---- observations: rules whose reading is not validated yet. The number is real; the cause is not decided.

    // R3 -- a drift along a long string
    private static void ObserveDrift(DiagnosisInput input, List<DiagnosisObservation> obs, List<DiagnosisGap> gaps)
    {
        var long_ = input.Strings.Where(s => s.VelocitiesMps.Count >= MinShotsForDrift).ToList();
        if (long_.Count == 0)
        {
            gaps.Add(new DiagnosisGap("R3", "R3 (drift along a string): not evaluated, no string has {0} shots or more.", new object[] { MinShotsForDrift }));
            return;
        }
        foreach (var s in long_)
        {
            var r = AdvancedDiagnostics.FoulingTrend(s.VelocitiesMps);
            if (!(r.FoulingSuspected || r.VelocityDeclining)) continue;
            obs.Add(new DiagnosisObservation("R3",
                "{0}: along its {1} shots the velocity moves by {2} per shot and the rolling SD by {3} per step. This is the pattern of a drift; the cause is not named.",
                new object[] { s.Name, r.NShots, input.Vel(r.VelocityTrendSlope), input.Vel(r.SdTrendSlope) }));
        }
    }

    // R4 -- a cold-bore shot against the warm ones
    private static void ObserveColdBore(DiagnosisInput input, List<DiagnosisObservation> obs, List<DiagnosisGap> gaps)
    {
        var marked = input.Strings.Where(s => s.ColdShots is not null && s.ColdShots.Count == s.VelocitiesMps.Count && s.ColdShots.Any(c => c)).ToList();
        if (marked.Count == 0)
        {
            gaps.Add(new DiagnosisGap("R4", "R4 (cold-bore shot): not evaluated, no shot is marked. Write COLD=1 in the note of the first shot of a string.", Array.Empty<object>()));
            return;
        }
        foreach (var s in marked)
        {
            AdvancedDiagnostics.ColdBoreResult r;
            try { r = AdvancedDiagnostics.ColdBoreAnalysis(s.VelocitiesMps, s.ColdShots!); }
            catch (AdvancedDiagnostics.AdvancedDiagnosticsError) { continue; }
            if (r.Inconclusive)
            {
                gaps.Add(new DiagnosisGap("R4", "R4 (cold-bore shot): {0} is not evaluated, it has fewer than {1} warm shots.", new object[] { s.Name, AdvancedDiagnostics.MinWarmShotsForVerdict }));
                continue;
            }
            if (!r.ColdBoreIsOutlier) continue;
            obs.Add(new DiagnosisObservation("R4",
                r.ColdBoreFaster
                    ? "{0}: the cold-bore shot is {1} faster than the {2} warm shots (z = {3}). It is worth a look; the cause is not named."
                    : "{0}: the cold-bore shot is {1} slower than the {2} warm shots (z = {3}). It is worth a look; the cause is not named.",
                new object[] { s.Name, input.Vel(Math.Abs(r.DeltaMps)), r.NWarm, F(r.ZScore) }));
        }
    }

    // R5 -- horizontal against vertical spread at distance, with the wind when it was written down
    private static void ObserveWindAndSupport(DiagnosisInput input, List<DiagnosisObservation> obs, List<DiagnosisGap> gaps)
    {
        var far = (input.Groups ?? Array.Empty<DiagnosisGroup>()).Where(g => g.DistanceM >= MinDistanceForWindM && g.Shots >= MinShots && g.VerticalMoa > 0).ToList();
        if (far.Count == 0)
        {
            gaps.Add(new DiagnosisGap("R5", "R5 (wind or support): not evaluated, no group of {0} shots or more at {1} m or beyond.", new object[] { MinShots, F(MinDistanceForWindM, "0") }));
            return;
        }
        foreach (var g in far)
        {
            double ratio = g.HorizontalMoa / g.VerticalMoa;
            bool skewed = ratio >= SkewRatio || ratio <= 1 / SkewRatio;
            if (g.WindMps is { } w)
                obs.Add(new DiagnosisObservation("R5",
                    "{0} at {1} m: the group is {2} MOA wide and {3} MOA high (ratio {4}); wind written down: {5} m/s. Wind or support are possible readings; the toolkit does not decide.",
                    new object[] { g.Name, F(g.DistanceM, "0"), F(g.HorizontalMoa), F(g.VerticalMoa), F(ratio), F(w) }));
            else if (skewed)
                obs.Add(new DiagnosisObservation("R5",
                    "{0} at {1} m: the group is {2} MOA wide and {3} MOA high (ratio {4}) and no wind was written down. Wind or support are possible readings; the toolkit does not decide.",
                    new object[] { g.Name, F(g.DistanceM, "0"), F(g.HorizontalMoa), F(g.VerticalMoa), F(ratio) }));
        }
        if (far.All(g => g.WindMps is null))
            gaps.Add(new DiagnosisGap("R5", "R5 (wind or support): no wind is written down. Put WIND=3.2m/s (and @90 for its direction) in the title of the shot-group tab or in the name of the group.", Array.Empty<object>()));
    }
}
