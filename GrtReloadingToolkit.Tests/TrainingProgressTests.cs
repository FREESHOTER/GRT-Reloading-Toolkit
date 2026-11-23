using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Cartridges;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>What one more training session changes in what the model has proven and in what it expects of the match lot.</summary>
public sealed class TrainingProgressTests
{
    private static double Gauss(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());

    /// <summary>Sessions of 14 shots (one string each) whose velocity follows the case weight, +4.7 m/s for each grain, as the data of the model tests do.</summary>
    private static LearningSet Set(params (long Session, string Date, bool Training)[] sessions)
    {
        var rnd = new Random(5);
        var samples = new List<ShotSample>();
        var origins = new List<ShotOrigin>();
        foreach (var (session, date, training) in sessions)
            for (int j = 0; j < 14; j++)
            {
                double w = Math.Round(120 + (rnd.NextDouble() - 0.5) * 1.4, 2);
                samples.Add(new ShotSample((int)session, Math.Round(820 + 3 * session + 4.7 * (w - 120) + 1.0 * Gauss(rnd), 1), new Dictionary<string, double> { ["case_weight_gr"] = w }));
                origins.Add(new ShotOrigin(session, date, training));
            }
        return new LearningSet(samples, sessions.Select(s => s.Session).ToList(), origins.Count(o => o.Training), origins.Count(o => !o.Training), origins);
    }

    private static List<CartridgeInput> MatchLot(int n, int outOfSpec = 0)
    {
        var rnd = new Random(9);
        return Enumerable.Range(0, n).Select(i => new CartridgeInput(i + 1, "m" + i, new Dictionary<string, double> { ["case_weight_gr"] = Math.Round(120 + (rnd.NextDouble() - 0.5) * 1.4, 2) }, i < outOfSpec)).ToList();
    }

    [Fact]
    public void TheLatestTrainingSessionIsComparedWithTheModelWithoutIt()
    {
        var set = Set((1, "2026-11-01", false), (2, "2026-11-08", true), (3, "2026-11-15", true));
        var r = TrainingProgress.Compute(set, MatchLot(12))!;
        Assert.Equal((3L, "2026-11-15", 14), (r.LatestSessionId, r.LatestDate, r.LatestShots));
        Assert.Equal(28, r.Before!.Shots);                      // the model without session 3
        Assert.Equal(42, r.After.Shots);
        Assert.Equal((2, 3), (r.Before.Series, r.After.Series));
        Assert.Contains(r.After.Proven, p => p.Key == "case_weight_gr");
        Assert.InRange(r.After.Proven.Single().Slope, 4.0, 5.4);
        Assert.NotNull(r.LotBefore);
        Assert.NotNull(r.LotAfter);
        Assert.Equal(12, r.LotAfter!.Usable);
        Assert.True(r.LotAfter.SdAtRandom >= r.LotAfter.CartridgeSd);
    }

    [Fact]
    public void ThePredictionForTheMatchLotLeavesOutTheCartridgesOutOfSpec()
    {
        var r = TrainingProgress.Compute(Set((1, "2026-11-01", true), (2, "2026-11-08", true)), MatchLot(12, outOfSpec: 3))!;
        Assert.Equal(9, r.LotAfter!.Usable);
    }

    [Fact]
    public void TheLatestIsTheNewestByDateNotByNumber()
    {
        var r = TrainingProgress.Compute(Set((9, "2026-11-01", true), (2, "2026-11-20", true), (4, "2026-11-10", false)))!;
        Assert.Equal((2L, "2026-11-20"), (r.LatestSessionId, r.LatestDate));
        Assert.Equal(28, r.Before!.Shots);
        Assert.Null(r.LotAfter);                                // no lot given: nothing said of it
    }

    [Fact]
    public void WithoutAnythingBeforeTheModelIsNotComparedAndNothingFiredInTrainingGivesNothing()
    {
        var one = TrainingProgress.Compute(Set((1, "2026-11-08", true)))!;
        Assert.Null(one.Before);
        Assert.Equal(14, one.After.Shots);
        Assert.Contains("Before it there were too few linked shots to prove anything", TrainingProgress.Text(one));

        Assert.Null(TrainingProgress.Compute(Set((1, "2026-11-01", false), (2, "2026-11-08", false))));          // only the match lot was ever shot
        Assert.Null(TrainingProgress.Compute(new LearningSet(Array.Empty<ShotSample>(), Array.Empty<long>())));      // an archive read the old way, no origins
    }

    [Fact]
    public void TheTextSaysWhatChangedMeasureBySureMeasure()
    {
        var before = new ModelSnapshot(28, 2, 3.1, 2.40, 0.31, new[] { new ModelProof("case_weight_gr", 4.10, 1.20, 0.0210), new ModelProof("case_length_mm", 150.0, 60.0, 0.0400) });
        var after = new ModelSnapshot(42, 3, 3.3, 2.05, 0.55, new[] { new ModelProof("case_weight_gr", 4.70, 0.80, 0.0006), new ModelProof("primer_height_mm", -35.0, 10.0, 0.0300) });
        var text = TrainingProgress.Text(new ProgressReport(3, "2026-11-15", 14, before, after, new LotExpectation(12, 2.0, 3.1), new LotExpectation(12, 2.3, 2.9)));
        Assert.Contains("The latest training session (2026-11-15) gave 14 shots to learn from.", text);
        Assert.Contains("Before it the model knew 28 shots in 2 series; now it knows 42 shots in 3 series.", text);
        Assert.Contains("+4.10 m/s per unit (p 0.0210) → +4.70 (p 0.0006)", text);                                     // proven before and now
        Assert.Contains("now proven, -35.00 m/s per unit (p 0.0300)", text);                                              // new
        Assert.Contains("it was proven (+150.00 m/s per unit) and with this session it is no longer", text);            // lost
        Assert.Contains("What the model does not explain: 2.40 → 2.05 m/s.", text);
        Assert.Contains("(correlation): +0.31 → +0.55.", text);
        Assert.Contains("the SD expected is 3.10 → 2.90 m/s (2.00 → 2.30 of it from the cartridges)", text);
        Assert.Contains("Predictions already saved stay as they were (they are frozen)", text);
        // no lot or nothing proven
        var none = TrainingProgress.Text(new ProgressReport(3, "", 5, null, new ModelSnapshot(5, 1, 3, 3, null, Array.Empty<ModelProof>()), null, null));
        Assert.Contains("No measure has a proven effect on velocity yet.", none);
        Assert.Contains("Nothing can be said yet of the match lot", none);
        Assert.Contains("The latest training session (?) gave 5 shots to learn from.", none);                            // a session without a date
    }
}
