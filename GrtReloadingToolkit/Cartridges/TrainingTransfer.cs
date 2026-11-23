using System.Globalization;

namespace GrtReloadingToolkit.Cartridges;

/// <summary>
/// For one measure that the model proved to move the velocity: the range of values the model learned it from, where the cartridges of the match lot
/// fall against that range, and how the training lot compares with the match lot.
/// </summary>
public sealed record MeasureTransfer(string Key, double LearnedMin, double LearnedMax, int RaceWith, int RaceInside, double RaceMin, double RaceMax, double RaceMean,
    int TrainingWith, double TrainingMean, double TrainingSd, double? ShiftSd)
{
    public double InsideShare => RaceWith == 0 ? 0 : (double)RaceInside / RaceWith;
}

/// <param name="Verdict">"good" (the match lot stays inside what the model saw), "partial", "poor" (much of it lies outside) or "unknown" (nothing to compare).</param>
public sealed record TransferResult(IReadOnlyList<MeasureTransfer> Measures, string Verdict);

/// <summary>
/// Whether what the model learned from shooting training cartridges can be used for the match lot. A model only knows the measures inside the range it
/// was shown: a match cartridge outside that range is an extrapolation, and its predicted velocity cannot be trusted. The check is a plain count of how
/// many cartridges of the match lot fall inside the learned range of each proven measure, and how far the two lots sit from each other.
/// The thresholds (at least 90 % inside and the lots within one SD of each other is "good", at least 60 % is "partial") are working rules, not tests.
/// </summary>
public static class TrainingTransfer
{
    public static TransferResult Compare(VelocityModelResult model, IReadOnlyList<CartridgeInput> raceLot, IReadOnlyList<CartridgeInput>? trainingLot = null)
    {
        var rows = new List<MeasureTransfer>();
        foreach (var t in model.Significant)
        {
            var race = raceLot.Where(c => !c.OutOfSpec && c.Measures.ContainsKey(t.Key)).Select(c => c.Measures[t.Key]).ToList();
            if (race.Count == 0) continue;
            int inside = race.Count(v => v >= t.Min - 1e-9 && v <= t.Max + 1e-9);
            var train = trainingLot?.Where(c => !c.OutOfSpec && c.Measures.ContainsKey(t.Key)).Select(c => c.Measures[t.Key]).ToList() ?? new List<double>();
            double tm = train.Count > 0 ? train.Average() : 0, ts = train.Count > 1 ? Math.Sqrt(train.Sum(v => (v - tm) * (v - tm)) / (train.Count - 1)) : 0;
            double rm = race.Average();
            rows.Add(new MeasureTransfer(t.Key, t.Min, t.Max, race.Count, inside, race.Min(), race.Max(), rm, train.Count, tm, ts, train.Count >= 3 && ts > 0 ? (rm - tm) / ts : null));
        }
        if (rows.Count == 0) return new TransferResult(rows, "unknown");
        double worstShare = rows.Min(r => r.InsideShare);
        double worstShift = rows.Where(r => r.ShiftSd is not null).Select(r => Math.Abs(r.ShiftSd!.Value)).DefaultIfEmpty(0).Max();
        string verdict = worstShare >= 0.9 && worstShift <= 1 ? "good" : worstShare >= 0.6 ? "partial" : "poor";
        return new TransferResult(rows, verdict);
    }

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Text(TransferResult r)
    {
        if (r.Measures.Count == 0)
            return Tr.T("Nothing to compare yet: the model has no proven measure, or the cartridges of the match lot do not carry it.");
        var lines = new List<string>();
        foreach (var m in r.Measures)
        {
            string label = Tr.T(MeasureFields.LabelOf(m.Key)) + (MeasureFields.UnitOf(m.Key) is { Length: > 0 } u ? " (" + u + ")" : "");
            lines.Add(Tr.F("{0}: the model saw values from {1} to {2}; {3} of the {4} cartridges of the match lot are inside ({5} to {6}).",
                label, N(m.LearnedMin), N(m.LearnedMax), m.RaceInside, m.RaceWith, N(m.RaceMin), N(m.RaceMax)));
            if (m.ShiftSd is { } s)
                lines.Add("   " + Tr.F("The training cartridges average {0}, the match lot {1}: {2} SD of the training lot apart.", N(m.TrainingMean), N(m.RaceMean), N(Math.Abs(s))));
        }
        lines.Add("");
        lines.Add(r.Verdict switch
        {
            "good" => Tr.T("The match lot stays inside what the model has seen: the prediction for it is an interpolation and can be used."),
            "partial" => Tr.T("Part of the match lot lies outside what the model has seen, or the two lots are not alike: use the prediction with care, and shoot training cartridges that cover the missing values."),
            "poor" => Tr.T("Much of the match lot lies outside what the model has seen: for those cartridges the prediction is an extrapolation and cannot be trusted. Make training cartridges that cover the values of the match lot."),
            _ => "",
        });
        return string.Join(Environment.NewLine, lines);
    }
}
