using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Cartridges;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>Lots for the match and lots for the training: what a lot is for, which match lot a training lot imitates, and where the shots come from.</summary>
public sealed class TrainingLotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "allenamento_test_" + Guid.NewGuid().ToString("N"));
    private readonly ArchiveDb _a;
    private readonly long _sh;

    public TrainingLotTests()
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

    private long Cartridge(long lot, int number, double caseWeight)
    {
        long c = CartridgeStore.AddPiece(_a, _sh, "case", $"{lot}-{number}");
        CartridgeStore.SetMeasure(_a, "piece", c, "case_weight_gr", caseWeight);
        return CartridgeStore.AddCartridge(_a, lot, number, c);
    }

    /// <summary>One session on a date with several strings; the ids of the shots of each string, in order.</summary>
    private List<List<long>> StringsOn(DateTime date, params double[][] strings)
    {
        long load = ArchiveImports.NewLoad(_a, _sh, "Carico " + Guid.NewGuid().ToString("N")[..4], "6.5 Creedmoor");
        var list = strings.Select((v, k) => new ChronoString("s" + k, null, null, "", v.Select(x => new ChronoShot(x, null, null)).ToArray(), $"m{date.Ticks}_{k}.xlsx", new byte[] { (byte)k, (byte)(date.Ticks % 251), (byte)v.Length })).ToArray();
        long session = ArchiveImports.AddChronoSession(_a, _sh, load, date, "", list, "prova").SessionId;
        var all = new List<List<long>>();
        foreach (var _ in strings) all.Add(new List<long>());
        using var cmd = _a.Connection.CreateCommand();
        cmd.CommandText = "SELECT st.id, sh.id FROM shots sh JOIN strings st ON st.id=sh.string_id WHERE st.session_id=$s ORDER BY st.id, sh.idx";
        cmd.Parameters.AddWithValue("$s", session);
        using var r = cmd.ExecuteReader();
        var order = new List<long>();
        while (r.Read())
        {
            long sid = r.GetInt64(0);
            if (!order.Contains(sid)) order.Add(sid);
            all[order.IndexOf(sid)].Add(r.GetInt64(1));
        }
        return all;
    }

    private List<long> ShotsOf(params double[] velocities) => ShotsOn(new DateTime(2026, 11, 19), velocities);

    private List<long> ShotsOn(DateTime date, double[] velocities)
    {
        long load = ArchiveImports.NewLoad(_a, _sh, "Carico " + Guid.NewGuid().ToString("N")[..4], "6.5 Creedmoor");
        var s = new ChronoString("s", null, null, "", velocities.Select(v => new ChronoShot(v, null, null)).ToArray(), "s" + date.Ticks + ".xlsx", new byte[] { (byte)velocities.Length, (byte)(date.Ticks % 251) });
        long session = ArchiveImports.AddChronoSession(_a, _sh, load, date, "", new[] { s }, "prova").SessionId;
        var ids = new List<long>();
        using var cmd = _a.Connection.CreateCommand();
        cmd.CommandText = "SELECT sh.id FROM shots sh JOIN strings st ON st.id=sh.string_id WHERE st.session_id=$s ORDER BY sh.idx";
        cmd.Parameters.AddWithValue("$s", session);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt64(0));
        return ids;
    }

    [Fact]
    public void ALotIsForTheMatchUnlessSaidOtherwise()
    {
        long race = CartridgeStore.CreateLot(_a, _sh, "Gara");
        long train = CartridgeStore.CreateLot(_a, _sh, "Allenamento", purpose: "training", referenceLotId: race);
        var lots = CartridgeStore.Lots(_a, _sh).ToDictionary(l => l.Id);
        Assert.Equal(("race", null, false), (lots[race].Purpose, lots[race].ReferenceLotId, lots[race].IsTraining));
        Assert.Equal(("training", (long?)race, true), (lots[train].Purpose, lots[train].ReferenceLotId, lots[train].IsTraining));
        Assert.Throws<ArgumentException>(() => CartridgeStore.CreateLot(_a, _sh, "x", purpose: "boh"));
    }

    [Fact]
    public void AnArchiveOfSchemaFiveGainsThePurposeAndItsLotsAreMatchLots()
    {
        long lot = CartridgeStore.CreateLot(_a, _sh, "Match lot");
        _a.Dispose();
        SqliteConnection.ClearAllPools();
        string path = Path.Combine(_dir, "archivio.db");
        using (var cn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            cn.Open();
            foreach (string sql in new[] { "ALTER TABLE cartridge_lots DROP COLUMN reference_lot_id", "ALTER TABLE cartridge_lots DROP COLUMN purpose", "UPDATE meta SET value='5' WHERE key='schema_version'" })
            { using var c = cn.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
        }
        using var again = new ArchiveDb(path);
        Assert.Equal("6", again.GetMeta("schema_version"));
        var l = Assert.Single(CartridgeStore.Lots(again, _sh));
        Assert.Equal((lot, "race", null), (l.Id, l.Purpose, l.ReferenceLotId));       // what was made before was the lot of the match
    }

    [Fact]
    public void OnlyATrainingLotHasAReferenceAndItIsAMatchLot()
    {
        long race = CartridgeStore.CreateLot(_a, _sh, "Gara");
        long race2 = CartridgeStore.CreateLot(_a, _sh, "Gara 2");
        long train = CartridgeStore.CreateLot(_a, _sh, "Allenamento", purpose: "training");
        long train2 = CartridgeStore.CreateLot(_a, _sh, "Allenamento 2", purpose: "training");
        CartridgeStore.SetReference(_a, train, race);
        Assert.Equal(race, CartridgeStore.Lots(_a, _sh).Single(l => l.Id == train).ReferenceLotId);
        CartridgeStore.SetReference(_a, train, race2);                                                     // it can change
        CartridgeStore.SetReference(_a, train, null);                                                      // and be cleared
        Assert.Null(CartridgeStore.Lots(_a, _sh).Single(l => l.Id == train).ReferenceLotId);
        Assert.Throws<InvalidOperationException>(() => CartridgeStore.SetReference(_a, race, race2));        // a match lot imitates nothing
        Assert.Throws<InvalidOperationException>(() => CartridgeStore.SetReference(_a, train, train2));      // the reference is a match lot
        Assert.Throws<InvalidOperationException>(() => CartridgeStore.SetReference(_a, train, train));
        Assert.Throws<InvalidOperationException>(() => CartridgeStore.CreateLot(_a, _sh, "x", referenceLotId: race));   // a match lot cannot be created with one
        Assert.Throws<InvalidOperationException>(() => CartridgeStore.SetReference(_a, 9999, race));
    }

    [Fact]
    public void ALotChangesSectionOnlyIfNothingWasShotWithIt()
    {
        long race = CartridgeStore.CreateLot(_a, _sh, "Gara");
        long train = CartridgeStore.CreateLot(_a, _sh, "Allenamento", purpose: "training", referenceLotId: race);
        Assert.Throws<InvalidOperationException>(() => CartridgeStore.SetPurpose(_a, race, "training"));   // it is the reference of a training lot

        CartridgeStore.SetPurpose(_a, train, "race");                                                       // free to change: the reference goes
        var moved = CartridgeStore.Lots(_a, _sh).Single(l => l.Id == train);
        Assert.Equal(("race", null), (moved.Purpose, moved.ReferenceLotId));
        CartridgeStore.SetPurpose(_a, train, "race");                                                       // already there: nothing happens

        long c = Cartridge(train, 1, 120);
        var shot = ShotsOf(820).Single();
        CartridgeStore.LinkShot(_a, shot, c);
        Assert.Throws<InvalidOperationException>(() => CartridgeStore.SetPurpose(_a, train, "training")); // it has been shot: what it was for stays
        Assert.Throws<ArgumentException>(() => CartridgeStore.SetPurpose(_a, train, "boh"));
        Assert.Throws<InvalidOperationException>(() => CartridgeStore.SetPurpose(_a, 9999, "training"));
    }

    [Fact]
    public void DeletingTheMatchLotLeavesTheTrainingLotWithoutAReference()
    {
        long race = CartridgeStore.CreateLot(_a, _sh, "Gara");
        long train = CartridgeStore.CreateLot(_a, _sh, "Allenamento", purpose: "training", referenceLotId: race);
        CartridgeStore.DeleteLot(_a, race);
        var l = Assert.Single(CartridgeStore.Lots(_a, _sh));
        Assert.Equal((train, null), (l.Id, l.ReferenceLotId));
    }

    [Fact]
    public void TheModelSaysHowManyOfItsShotsCameFromTrainingAndHowManyFromTheMatchLot()
    {
        long race = CartridgeStore.CreateLot(_a, _sh, "Gara");
        long train = CartridgeStore.CreateLot(_a, _sh, "Allenamento", purpose: "training", referenceLotId: race);
        long r1 = Cartridge(race, 1, 120), r2 = Cartridge(race, 2, 121);
        long t1 = Cartridge(train, 1, 120), t2 = Cartridge(train, 2, 122), t3 = Cartridge(train, 3, 119);
        var shots = ShotsOf(820, 821, 822, 823, 824);
        foreach (var (shot, cart) in shots.Zip(new[] { t1, t2, t3, r1, r2 })) CartridgeStore.LinkShot(_a, shot, cart);
        var set = CartridgeStore.LearningShots(_a, _sh);
        Assert.Equal(5, set.Samples.Count);
        Assert.Equal((3, 2), (set.TrainingShots, set.RaceShots));
        Assert.Equal(5, set.Origins!.Count);                                                  // one origin for each sample, in the same order
        Assert.Equal(new[] { true, true, true, false, false }, set.Origins.Select(o => o.Training));
        Assert.All(set.Origins, o => Assert.Equal("2026-11-19", o.Date));
    }

    [Fact]
    public void TheLatestTrainingSessionIsTheNewestByDateAndOnlyItsTrainingStringsAreFound()
    {
        long race = CartridgeStore.CreateLot(_a, _sh, "Gara");
        long train = CartridgeStore.CreateLot(_a, _sh, "Allenamento", purpose: "training");
        long t1 = Cartridge(train, 1, 120), t2 = Cartridge(train, 2, 121), r1 = Cartridge(race, 1, 120);
        // the newer session (12 November) is made FIRST, so that by number it is the older: the date decides. It has two strings, one shot with training cartridges
        // and one with the match lot.
        var recent = StringsOn(new DateTime(2026, 11, 12), new double[] { 830, 831 }, new double[] { 832, 833 });
        var old = StringsOn(new DateTime(2026, 11, 2), new double[] { 820, 821 });
        var match = StringsOn(new DateTime(2026, 11, 18), new double[] { 840 });           // a match-lot session even newer: it does not count
        CartridgeStore.LinkShot(_a, recent[0][0], t1); CartridgeStore.LinkShot(_a, recent[0][1], t2);
        CartridgeStore.LinkShot(_a, recent[1][0], r1);                                   // the second string was shot with the match lot
        CartridgeStore.LinkShot(_a, old[0][0], t1); CartridgeStore.LinkShot(_a, old[0][1], t2);
        CartridgeStore.LinkShot(_a, match[0][0], r1);
        var strings = CartridgeStore.LatestTrainingStrings(_a, _sh);
        long first = Convert.ToInt64(_a.ScalarP("SELECT string_id FROM shots WHERE id=$i", ("$i", recent[0][0])));
        Assert.Equal(new[] { first }, strings);
        Assert.Empty(CartridgeStore.LatestTrainingStrings(_a, _sh + 99));                  // another shooter's sessions are not his
    }
}

public sealed class TrainingTransferTests
{
    private static VelocityModelResult Model(double min, double max) => new()
    {
        Tested = Array.Empty<ModelTerm>(),
        Significant = new[] { new ModelTerm { Key = "case_weight_gr", Min = min, Max = max, Slope = 4.7, Se = 0.5 } },
    };

    private static List<CartridgeInput> Lot(params double[] weights) =>
        weights.Select((w, i) => new CartridgeInput(i + 1, "m" + i, new Dictionary<string, double> { ["case_weight_gr"] = w })).ToList();

    [Fact]
    public void AMatchLotInsideWhatTheModelSawIsAnInterpolation()
    {
        var r = TrainingTransfer.Compare(Model(119, 122), Lot(119.5, 120, 120.5, 121, 121.5), Lot(119, 120, 121, 122, 120.5, 121.5));
        var m = Assert.Single(r.Measures);
        Assert.Equal((5, 5, 1.0), (m.RaceWith, m.RaceInside, m.InsideShare));
        Assert.Equal("good", r.Verdict);
        string text = TrainingTransfer.Text(r);
        Assert.Contains("5 of the 5 cartridges of the match lot are inside", text);
        Assert.Contains("interpolation", text);
    }

    [Fact]
    public void AMatchLotOutsideWhatTheModelSawIsAnExtrapolationAndTheTextSaysSo()
    {
        var r = TrainingTransfer.Compare(Model(119, 120.5), Lot(119.5, 120, 121.5, 122, 122.5, 123), Lot(119, 119.5, 120, 120.5));
        Assert.Equal("poor", r.Verdict);                                     // 2 of 6 inside
        Assert.Equal(2, r.Measures[0].RaceInside);
        Assert.Contains("extrapolation", TrainingTransfer.Text(r));
        // 4 of 5 inside: partial
        var p = TrainingTransfer.Compare(Model(119, 122), Lot(120, 120.5, 121, 121.5, 123), null);
        Assert.Equal("partial", p.Verdict);
    }

    [Fact]
    public void TwoLotsFarApartAreNotAlikeEvenWhenEverythingIsInside()
    {
        // training lot averages 120 with SD about 0.5; the match lot averages 121.5: three SD away
        var training = Lot(119.5, 120, 120.5, 119.8, 120.2);
        var r = TrainingTransfer.Compare(Model(119, 123), Lot(121, 121.5, 122, 121.4, 121.6), training);
        var m = r.Measures[0];
        Assert.Equal(1.0, m.InsideShare);
        Assert.True(m.ShiftSd > 2.5);
        Assert.Equal("partial", r.Verdict);
        Assert.Contains("SD of the training lot apart", TrainingTransfer.Text(r));
    }

    [Fact]
    public void CartridgesOutOfSpecAndMissingMeasuresAreLeftOutAndNothingToCompareIsSaid()
    {
        var race = new List<CartridgeInput>
        {
            new(1, "a", new Dictionary<string, double> { ["case_weight_gr"] = 120 }),
            new(2, "b", new Dictionary<string, double> { ["case_weight_gr"] = 150 }, OutOfSpec: true),
            new(3, "c", new Dictionary<string, double>()),
        };
        var r = TrainingTransfer.Compare(Model(119, 122), race);
        Assert.Equal(1, r.Measures[0].RaceWith);
        var none = TrainingTransfer.Compare(Model(119, 122), new List<CartridgeInput> { race[2] });
        Assert.Equal("unknown", none.Verdict);
        Assert.Contains("Nothing to compare yet", TrainingTransfer.Text(none));
        var noTerms = TrainingTransfer.Compare(new VelocityModelResult { Tested = Array.Empty<ModelTerm>(), Significant = Array.Empty<ModelTerm>() }, race);
        Assert.Equal("unknown", noTerms.Verdict);
    }
}
