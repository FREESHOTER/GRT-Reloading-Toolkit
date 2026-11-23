using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Cartridges;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>Tolerance bands, classes of like pieces and the budget read from the archive (Phase B4).</summary>
public sealed class ToleranceBandsTests
{
    [Fact]
    public void ABandIsTheWidthThatPutsAtMostTheWantedSdIntoTheVelocity()
    {
        // sqrt(12) * wanted SD / slope; with 4.7 m/s per grain that is about 0.74 gr for 1 m/s and 0.37 gr for 0.5 m/s
        // (the manual of Analisi DS prints 0.73 and 0.37 for its own, unrounded, slope)
        Assert.Equal(Math.Sqrt(12) * 1.0 / 4.7, ToleranceBands.Band(4.7, 1.0), 12);
        Assert.Equal(Math.Sqrt(12) * 0.5 / 4.7, ToleranceBands.Band(4.7, 0.5), 12);
        Assert.Equal(0.74, ToleranceBands.Band(4.7, 1.0), 2);
        Assert.Equal(0.37, ToleranceBands.Band(4.7, 0.5), 2);
        Assert.Equal(ToleranceBands.Band(4.7, 1.0), ToleranceBands.Band(-4.7, 1.0));          // the sign of the effect does not matter
        // pieces spread evenly over the band have an SD of band/sqrt(12): times the slope, that is the SD wanted
        double w = ToleranceBands.Band(3.0, 0.8);
        Assert.Equal(0.8, 3.0 * w / Math.Sqrt(12), 12);
    }

    [Fact]
    public void EachProvenMeasureOfTheModelGetsItsRow()
    {
        var rnd = new Random(8);
        var shots = new List<ShotSample>();
        for (int s = 0; s < 4; s++)
            for (int i = 0; i < 12; i++)
            {
                double cw = Math.Round(120 + (rnd.NextDouble() - 0.5) * 1.4, 2);
                shots.Add(new ShotSample(s, Math.Round(820 + 5 * s + 4.7 * (cw - 120) + (rnd.NextDouble() - 0.5) * 2, 1),
                    new Dictionary<string, double> { ["case_weight_gr"] = cw, ["powder_weight_gr"] = 40.3 + (rnd.NextDouble() - 0.5) * 0.04 }));
            }
        var model = VelocityModel.Learn(shots)!;
        var rows = ToleranceBands.From(model);
        var row = Assert.Single(rows);
        Assert.Equal("case_weight_gr", row.Key);
        Assert.Equal(Math.Abs(row.Slope) * row.SdOfMeasure, row.SdTodayMps, 12);
        Assert.Equal(row.BandForOne / 2, row.BandForHalf, 12);
        Assert.Equal(row.BandForOne, row.BandFor(1.0), 12);
        Assert.Empty(ToleranceBands.From(null));
    }
}

public sealed class MeasureClassesTests
{
    private static ClassPiece P(long id, double w, double? l = null)
    {
        var m = new Dictionary<string, double> { ["case_weight_gr"] = w };
        if (l is { } len) m["case_length_mm"] = len;
        return new ClassPiece(id, id.ToString(), m);
    }

    [Fact]
    public void PiecesAreCutIntoTheFewestClassesTheBandAllows()
    {
        var pieces = new[] { 1.0, 1.2, 1.5, 1.9, 2.0, 2.4, 3.1 }.Select((w, i) => P(i + 1, w)).ToList();
        var r = MeasureClasses.Build(pieces, new[] { ("case_weight_gr", 0.8) });
        Assert.Equal(new[] { "A", "B", "C" }, r.Classes.Select(c => c.Label));
        Assert.Equal(new long[] { 1, 2, 3 }, r.Classes[0].Pieces.Select(p => p.Id));
        Assert.Equal(new long[] { 4, 5, 6 }, r.Classes[1].Pieces.Select(p => p.Id));
        Assert.Equal(new long[] { 7 }, r.Classes[2].Pieces.Select(p => p.Id));
        Assert.Equal((1.0, 1.5), r.Classes[0].Ranges["case_weight_gr"]);
        Assert.Empty(r.Unclassified);
    }

    [Fact]
    public void ABandCanBeUsedToItsFullWidth() =>
        Assert.Single(MeasureClasses.Build(new[] { P(1, 1.0), P(2, 1.8) }, new[] { ("case_weight_gr", 0.8) }).Classes);

    [Fact]
    public void WithSeveralCriteriaEachClassIsDividedAgain()
    {
        var pieces = new[] { P(1, 1.0, 48.40), P(2, 1.1, 48.60), P(3, 1.2, 48.41), P(4, 3.0, 48.40), P(5, 3.1, 48.42) }.ToList();
        var r = MeasureClasses.Build(pieces, new[] { ("case_weight_gr", 0.5), ("case_length_mm", 0.05) });
        Assert.Equal(new[] { "A1", "A2", "B1" }, r.Classes.Select(c => c.Label));
        Assert.Equal(new long[] { 1, 3 }, r.Classes[0].Pieces.Select(p => p.Id));         // short ones of the light group
        Assert.Equal(new long[] { 2 }, r.Classes[1].Pieces.Select(p => p.Id));
        Assert.Equal(new long[] { 4, 5 }, r.Classes[2].Pieces.Select(p => p.Id));
    }

    [Fact]
    public void APieceWithoutAMeasureIsListedApartAndNeverGuessed()
    {
        var r = MeasureClasses.Build(new[] { P(1, 1.0, 48.4), P(2, 1.1), P(3, 1.2, 48.4) }, new[] { ("case_weight_gr", 1.0), ("case_length_mm", 1.0) });
        Assert.Equal(new long[] { 2 }, r.Unclassified.Select(p => p.Id));
        Assert.Equal(2, r.Classes.Sum(c => c.Pieces.Count));
    }

    [Fact]
    public void ClassNamesContinuePastTheLastLetter()
    {
        var pieces = Enumerable.Range(0, 30).Select(i => P(i + 1, i * 10.0)).ToList();                 // every piece far from the next
        var labels = MeasureClasses.Build(pieces, new[] { ("case_weight_gr", 1.0) }).Classes.Select(c => c.Label).ToList();
        Assert.Equal(30, labels.Distinct().Count());
        Assert.Equal("Z", labels[25]); Assert.Equal("AA", labels[26]); Assert.Equal("AD", labels[29]);
    }

    [Fact]
    public void ABadCriterionIsRefused()
    {
        Assert.Throws<ArgumentException>(() => MeasureClasses.Build(new[] { P(1, 1) }, Array.Empty<(string, double)>()));
        Assert.Throws<ArgumentException>(() => MeasureClasses.Build(new[] { P(1, 1) }, new[] { ("case_weight_gr", 0.0) }));
        Assert.Empty(MeasureClasses.Build(Array.Empty<ClassPiece>(), new[] { ("case_weight_gr", 1.0) }).Classes);
    }

    [Fact]
    public void NoClassIsWiderThanItsBandAndNoSplitCouldHaveBeenAvoided()
    {
        var rnd = new Random(42);
        for (int round = 0; round < 60; round++)
        {
            int n = rnd.Next(1, 9);
            double width = 0.2 + rnd.NextDouble();
            var pieces = Enumerable.Range(0, n).Select(i => P(i + 1, Math.Round(rnd.NextDouble() * 3, 2))).ToList();
            var r = MeasureClasses.Build(pieces, new[] { ("case_weight_gr", width) });
            Assert.Equal(n, r.Classes.Sum(c => c.Pieces.Count));
            foreach (var c in r.Classes) Assert.True(c.Ranges["case_weight_gr"].Max - c.Ranges["case_weight_gr"].Min <= width + 1e-9);
            Assert.Equal(MinimumClasses(pieces.Select(p => p.Measures["case_weight_gr"]).OrderBy(x => x).ToArray(), width), r.Classes.Count);
        }
    }

    /// <summary>Brute force: the fewest runs of the sorted values that each fit in the band (every way of cutting the line is tried).</summary>
    private static int MinimumClasses(double[] sorted, double width)
    {
        int n = sorted.Length, best = n;
        for (int mask = 0; mask < (1 << (n - 1)); mask++)
        {
            int runs = 1, start = 0; bool ok = true;
            for (int i = 1; i < n && ok; i++)
                if ((mask & (1 << (i - 1))) != 0) { runs++; start = i; }
                else if (sorted[i] - sorted[start] > width + 1e-9) ok = false;
            if (ok) best = Math.Min(best, runs);
        }
        return best;
    }
}

public sealed class BudgetFromTheArchiveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bilancio_test_" + Guid.NewGuid().ToString("N"));
    private readonly ArchiveDb _a;
    private readonly long _sh;

    public BudgetFromTheArchiveTests()
    {
        Directory.CreateDirectory(_dir);
        _a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        _sh = ArchiveImports.ShooterFor(_a, null);
    }

    public void Dispose()
    {
        _a.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void TheShotsOfChosenStringsComeWithTheirCartridgesMeasuresWhenLinked()
    {
        long lot = CartridgeStore.CreateLot(_a, _sh, "L");
        var carts = new long[4];
        for (int i = 0; i < 4; i++)
        {
            long c = CartridgeStore.AddPiece(_a, _sh, "case", "1" + (char)('A' + i));
            CartridgeStore.SetMeasure(_a, "piece", c, "case_weight_gr", 120 + i);
            carts[i] = CartridgeStore.AddCartridge(_a, lot, i + 1, c);
        }
        long load = ArchiveImports.NewLoad(_a, _sh, "Carico", "6.5 Creedmoor");
        long session = ArchiveImports.AddChronoSession(_a, _sh, load, new DateTime(2026, 11, 19), "",
            new[] { new ChronoString("a", 40.3, 18, "", new[] { 820.0, 821, 822, 823 }.Select(v => new ChronoShot(v, null, null)).ToArray(), "a.xlsx", new byte[] { 1 }),
                    new ChronoString("b", 40.3, 18, "", new[] { 830.0, 0, 832 }.Select(v => new ChronoShot(v, null, null)).ToArray(), "b.xlsx", new byte[] { 2 }) }, "prova").SessionId;
        var strings = new List<long>();
        using (var cmd = _a.Connection.CreateCommand()) { cmd.CommandText = "SELECT id FROM strings WHERE session_id=" + session + " ORDER BY id"; using var r = cmd.ExecuteReader(); while (r.Read()) strings.Add(r.GetInt64(0)); }
        var shots = new List<long>();
        using (var cmd = _a.Connection.CreateCommand()) { cmd.CommandText = "SELECT id FROM shots WHERE string_id=" + strings[0] + " ORDER BY idx"; using var r = cmd.ExecuteReader(); while (r.Read()) shots.Add(r.GetInt64(0)); }
        CartridgeStore.LinkShot(_a, shots[0], carts[0]);
        CartridgeStore.LinkShot(_a, shots[1], carts[1]);                           // the other two shots of the string stay unlinked

        var samples = CartridgeStore.StringSamples(_a, strings);
        Assert.Equal(6, samples.Count);                                             // the shot of velocity 0 is an empty slot: left out
        Assert.Equal(new[] { 820.0, 821, 822, 823, 830, 832 }, samples.Select(s => s.VelocityMps));
        Assert.Equal(new[] { (int)strings[0], (int)strings[0], (int)strings[0], (int)strings[0], (int)strings[1], (int)strings[1] }, samples.Select(s => s.SeriesId));
        Assert.Equal(120, samples[0].Measures["case_weight_gr"]);
        Assert.Equal(121, samples[1].Measures["case_weight_gr"]);
        Assert.Empty(samples[2].Measures);
        Assert.Empty(samples[4].Measures);
    }

    [Fact]
    public void ABudgetCanBeReadFromStringsWithAndWithoutLinkedCartridges()
    {
        var rnd = new Random(9);
        long lot = CartridgeStore.CreateLot(_a, _sh, "L");
        long load = ArchiveImports.NewLoad(_a, _sh, "Carico", "6.5 Creedmoor");
        var all = new List<long>();
        int cartNo = 0;
        for (int k = 0; k < 3; k++)
        {
            var vs = new List<ChronoShot>(); var cw = new List<double>(); var cartIds = new List<long>();
            for (int j = 0; j < 14; j++)
            {
                double w = Math.Round(120 + (rnd.NextDouble() - 0.5) * 1.4, 2);
                long c = CartridgeStore.AddPiece(_a, _sh, "case", "m" + (++cartNo));
                CartridgeStore.SetMeasure(_a, "piece", c, "case_weight_gr", w);
                cartIds.Add(CartridgeStore.AddCartridge(_a, lot, cartNo, c));
                vs.Add(new ChronoShot(Math.Round(826 + 6 * k + 4.7 * (w - 120) + (rnd.NextDouble() - 0.5) * 2, 1), null, null));
            }
            long s = ArchiveImports.AddChronoSession(_a, _sh, load, new DateTime(2026, 11, 10 + k), "", new[] { new ChronoString("s" + k, 40.3, 18, "", vs.ToArray(), "s" + k + ".xlsx", new byte[] { (byte)(k + 1) }) }, "p").SessionId;
            long str = Convert.ToInt64(_a.ScalarP("SELECT id FROM strings WHERE session_id=$s", ("$s", s)));
            all.Add(str);
            var ids = new List<long>();
            using (var cmd = _a.Connection.CreateCommand()) { cmd.CommandText = "SELECT id FROM shots WHERE string_id=" + str + " ORDER BY idx"; using var r = cmd.ExecuteReader(); while (r.Read()) ids.Add(r.GetInt64(0)); }
            for (int j = 0; j < 14; j++) CartridgeStore.LinkShot(_a, ids[j], cartIds[j]);
        }
        var b = VarianceBudget.Analyze(CartridgeStore.StringSamples(_a, all), 0.4, 200)!;
        Assert.Equal(42, b.N);
        Assert.Equal(3, b.SeriesCount);
        Assert.Contains(b.Parts, p => p.Kind == "between" && p.Proven);                    // 6 m/s between the series
        Assert.Contains(b.Parts, p => p.Kind == "measure" && p.Key == "case_weight_gr" && p.Proven);
        Assert.Contains(b.Parts, p => p.Kind == "chrono");
        Assert.Contains(b.Parts, p => p.Kind == "rest");
        Assert.Equal(1.0, b.Parts.Sum(p => p.Share), 6);                                    // the shares make up the whole variance
        Assert.Contains(b.TargetCost, r => r.DistanceM == 200);
        Assert.Single(b.Advice);
        Assert.True(b.Advice[0].SdIfRemoved < b.Advice[0].SdIfHalved && b.Advice[0].SdIfHalved < b.SdWithin);

        // the same shots without the links: no measure part, everything else stays
        _a.Exec("UPDATE shots SET cartridge_id=NULL");
        var plain = VarianceBudget.Analyze(CartridgeStore.StringSamples(_a, all))!;
        Assert.DoesNotContain(plain.Parts, p => p.Kind == "measure");
        Assert.Equal(b.SdWithin, plain.SdWithin, 12);
    }

    [Fact]
    public void TooFewShotsGiveNoBudget()
    {
        Assert.Null(VarianceBudget.Analyze(Array.Empty<ShotSample>()));
        var four = Enumerable.Range(0, 4).Select(i => new ShotSample(0, 820 + i, new Dictionary<string, double>())).ToList();
        Assert.Null(VarianceBudget.Analyze(four));
        var lonely = Enumerable.Range(0, 8).Select(i => new ShotSample(i, 820 + i, new Dictionary<string, double>())).ToList();      // eight series of one shot: none counts
        Assert.Null(VarianceBudget.Analyze(lonely));
    }
}
