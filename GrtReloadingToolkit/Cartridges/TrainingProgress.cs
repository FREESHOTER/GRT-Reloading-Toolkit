using System.Globalization;
using GrtReloadingToolkit.Archive;

namespace GrtReloadingToolkit.Cartridges;

public sealed record ModelProof(string Key, double Slope, double Se, double PAdj);

/// <summary>What the model knew at one moment: how many shots, how well it explains the velocity, and which measures it had proven.</summary>
public sealed record ModelSnapshot(int Shots, int Series, double SdWithin, double SdResidual, double? LeaveOneOutR, IReadOnlyList<ModelProof> Proven);

/// <summary>What a model expects of a lot of cartridges shot in random order: the SD from the cartridges themselves and the whole SD with what the model leaves unexplained.</summary>
public sealed record LotExpectation(int Usable, double CartridgeSd, double SdAtRandom);

/// <param name="Before">The model without the latest training session; null when what was left was too little to prove anything.</param>
public sealed record ProgressReport(long LatestSessionId, string LatestDate, int LatestShots, ModelSnapshot? Before, ModelSnapshot After, LotExpectation? LotBefore, LotExpectation? LotAfter);

/// <summary>
/// What the latest training session taught: the same model learned twice, without that session and with it, and the difference in what it has proven, in what it
/// leaves unexplained and in what it now expects of the match lot. The model always learns from every shot linked to a cartridge, training or match, so this does
/// not change what it does; it shows it, so that the effect of one more session on the predictions can be seen instead of assumed.
/// </summary>
public static class TrainingProgress
{
    public static ProgressReport? Compute(LearningSet set, IReadOnlyList<CartridgeInput>? matchLot = null)
    {
        if (set.Origins is not { Count: > 0 } origins || origins.Count != set.Samples.Count) return null;
        var training = origins.Where(o => o.Training).ToList();
        if (training.Count == 0) return null;
        var latest = training.OrderByDescending(o => o.Date, StringComparer.Ordinal).ThenByDescending(o => o.SessionId).First();

        var after = VelocityModel.Learn(set.Samples);
        var keep = Enumerable.Range(0, set.Samples.Count).Where(i => origins[i].SessionId != latest.SessionId).ToList();
        VelocityModelResult? before = keep.Count >= VelocityModel.MinShotsPerMeasure ? VelocityModel.Learn(keep.Select(i => set.Samples[i]).ToList()) : null;

        return new ProgressReport(latest.SessionId, latest.Date, origins.Count(o => o.SessionId == latest.SessionId), before is null ? null : Snapshot(before!), Snapshot(after!),
            before is null || matchLot is null ? null : Expect(before!, matchLot), matchLot is null ? null : Expect(after!, matchLot));
    }

    private static ModelSnapshot Snapshot(VelocityModelResult m) =>
        new(m.NShots, m.NSeries, m.SdWithin, m.SdResidual, m.LeaveOneOut?.R, m.Significant.Select(t => new ModelProof(t.Key, t.Slope, t.Se, t.PAdj)).ToList());

    private static LotExpectation? Expect(VelocityModelResult m, IReadOnlyList<CartridgeInput> lot)
    {
        if (m.Significant.Count == 0) return null;
        var (est, _) = CartridgePlanner.Predict(lot, m);
        var usable = est.Where(x => !x.Cartridge.OutOfSpec && x.Missing.Count == 0).Select(x => x.Estimate).ToList();
        if (usable.Count == 0) return null;
        double cart = usable.Count > 1 ? CartridgePlanner.SampleSd(usable) : 0;
        return new LotExpectation(usable.Count, cart, Math.Sqrt(cart * cart + m.SdResidual * m.SdResidual));
    }

    private static string N(double v, int d) => v.ToString("0." + new string('0', d), CultureInfo.InvariantCulture);

    private static string Label(string key) => Tr.T(MeasureFields.LabelOf(key)) + (MeasureFields.UnitOf(key) is { Length: > 0 } u ? " (" + u + ")" : "");

    public static string Text(ProgressReport r)
    {
        var lines = new List<string>
        {
            Tr.F("The latest training session ({0}) gave {1} shots to learn from.", r.LatestDate.Length > 0 ? r.LatestDate : "?", r.LatestShots),
            r.Before is { } b
                ? Tr.F("Before it the model knew {0} shots in {1} series; now it knows {2} shots in {3} series.", b.Shots, b.Series, r.After.Shots, r.After.Series)
                : Tr.F("Before it there were too few linked shots to prove anything; now the model knows {0} shots in {1} series.", r.After.Shots, r.After.Series),
            "",
        };

        var keys = (r.Before?.Proven.Select(p => p.Key) ?? Enumerable.Empty<string>()).Concat(r.After.Proven.Select(p => p.Key)).Distinct().ToList();
        if (keys.Count == 0) lines.Add(Tr.T("No measure has a proven effect on velocity yet."));
        foreach (string k in keys)
        {
            var was = r.Before?.Proven.FirstOrDefault(p => p.Key == k);
            var now = r.After.Proven.FirstOrDefault(p => p.Key == k);
            if (was is not null && now is not null)
                lines.Add(Tr.F("{0}: {1} m/s per unit (p {2}) → {3} (p {4}).", Label(k), Signed(was.Slope), N(was.PAdj, 4), Signed(now.Slope), N(now.PAdj, 4)));
            else if (now is not null)
                lines.Add(Tr.F("{0}: now proven, {1} m/s per unit (p {2}).", Label(k), Signed(now.Slope), N(now.PAdj, 4)));
            else
                lines.Add(Tr.F("{0}: it was proven ({1} m/s per unit) and with this session it is no longer.", Label(k), Signed(was!.Slope)));
        }
        lines.Add("");
        if (r.Before is { } bf)
        {
            lines.Add(Tr.F("What the model does not explain: {0} → {1} m/s.", N(bf.SdResidual, 2), N(r.After.SdResidual, 2)));
            if (bf.LeaveOneOutR is { } b1 && r.After.LeaveOneOutR is { } a1)
                lines.Add(Tr.F("Check on shots it has not seen (correlation): {0} → {1}.", Signed(b1), Signed(a1)));
        }
        else lines.Add(Tr.F("What the model does not explain: {0} m/s.", N(r.After.SdResidual, 2)));

        if (r.LotAfter is { } la)
            lines.Add(r.LotBefore is { } lb
                ? Tr.F("For the match lot ({0} usable cartridges), shot in random order, the SD expected is {1} → {2} m/s ({3} → {4} of it from the cartridges).", la.Usable, N(lb.SdAtRandom, 2), N(la.SdAtRandom, 2), N(lb.CartridgeSd, 2), N(la.CartridgeSd, 2))
                : Tr.F("For the match lot ({0} usable cartridges), shot in random order, the SD expected is {1} m/s ({2} of it from the cartridges).", la.Usable, N(la.SdAtRandom, 2), N(la.CartridgeSd, 2)));
        else
            lines.Add(Tr.T("Nothing can be said yet of the match lot: the model has no proven measure, or the lot lacks it."));
        lines.Add("");
        lines.Add(Tr.T("Predictions already saved stay as they were (they are frozen); the plan of the race in Phase 7 uses the updated model."));
        return string.Join(Environment.NewLine, lines);
    }

    private static string Signed(double v) => v.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
}
