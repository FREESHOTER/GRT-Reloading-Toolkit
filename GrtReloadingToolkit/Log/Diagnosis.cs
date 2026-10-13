using System.Globalization;

namespace GrtReloadingToolkit.Log;

/// <summary>How far a hypothesis can be trusted. "High" is never given by a single rule: it needs two independent sources to agree.</summary>
public enum DiagnosisConfidence { Low, Medium, High }

/// <summary>One string of the open load: the shots of one charge, with what is known about how it was shot.</summary>
public sealed record DiagnosisString(string Name, double? ChargeGr, IReadOnlyList<double> VelocitiesMps, double? TempC, double? GroupMoa);

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
    Func<double, string> Vel);

/// <summary>A finding: what may be going on, on what numbers, how sure, and the test that would confirm it.
/// Templates are English translation keys with {0}, {1}... for the numbers, filled in by the window.</summary>
public sealed record DiagnosisFinding(string Rule, string Template, object[] Args, DiagnosisConfidence Confidence, string ProofTemplate, int ReturnToPhase);

/// <summary>A rule that could not be applied, and why, so the shooter knows what measurement is missing rather than assuming all is well.</summary>
public sealed record DiagnosisGap(string Rule, string Template, object[] Args);

public sealed record DiagnosisResult(IReadOnlyList<DiagnosisFinding> Findings, IReadOnlyList<DiagnosisGap> Gaps);

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

    /// <summary>A velocity score below this is "weak" (below Good in <see cref="LoadScoring.ScoreLabel"/>).</summary>
    public const double WeakScore = 6.0;

    public static DiagnosisResult Run(DiagnosisInput input)
    {
        var findings = new List<DiagnosisFinding>();
        var gaps = new List<DiagnosisGap>();
        RuleSpreadVersusGroup(input, findings, gaps);
        RuleSingleShot(input, findings, gaps);
        RuleTemperature(input, findings, gaps);
        RuleChosenChargeOffNode(input, findings, gaps);
        // strongest first; within a confidence, the order the rules ran in
        var ordered = findings.Select((f, i) => (f, i)).OrderByDescending(x => x.f.Confidence).ThenBy(x => x.i).Select(x => x.f).ToList();
        return new DiagnosisResult(ordered, gaps);
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
}
