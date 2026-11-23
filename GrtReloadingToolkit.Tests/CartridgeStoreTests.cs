using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Cartridges;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>Pieces, cartridges, measures and frozen predictions in the archive (Phase B2).</summary>
public sealed class CartridgeStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cartucce_test_" + Guid.NewGuid().ToString("N"));
    private readonly ArchiveDb _a;
    private readonly long _sh;

    public CartridgeStoreTests()
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

    private static ChronoString Str(string label, params double[] v) =>
        new(label, null, null, "", v.Select(x => new ChronoShot(x, null, null)).ToArray(), label + ".xlsx", new byte[] { (byte)label.GetHashCode() });

    private (long Lot, long[] Cartridges, long[] CasePieces) Lot(int count, Func<int, double> caseWeight, Func<int, double> bto)
    {
        long lot = CartridgeStore.CreateLot(_a, _sh, "Lotto prova");
        var cart = new long[count]; var cases = new long[count];
        for (int i = 0; i < count; i++)
        {
            long c = CartridgeStore.AddPiece(_a, _sh, "case", $"{i / 10 + 1}{(char)('A' + i % 10)}");
            long b = CartridgeStore.AddPiece(_a, _sh, "bullet", "B" + (i + 1));
            CartridgeStore.SetMeasure(_a, "piece", c, "case_weight_gr", caseWeight(i));
            CartridgeStore.SetMeasure(_a, "piece", b, "bullet_bto_mm", bto(i));
            cart[i] = CartridgeStore.AddCartridge(_a, lot, i + 1, c, b);
            CartridgeStore.SetMeasure(_a, "cartridge", cart[i], "powder_weight_gr", 40.30);
            cases[i] = c;
        }
        return (lot, cart, cases);
    }

    private long Session(params ChronoString[] strings)
    {
        long load = ArchiveImports.NewLoad(_a, _sh, "Carico " + Guid.NewGuid().ToString("N")[..4], "6.5 Creedmoor");
        return ArchiveImports.AddChronoSession(_a, _sh, load, new DateTime(2026, 11, 19), "", strings, "prova").SessionId;
    }

    private List<long> ShotIds(long stringId)
    {
        var ids = new List<long>();
        using var cmd = _a.Connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM shots WHERE string_id=$s ORDER BY idx";
        cmd.Parameters.AddWithValue("$s", stringId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt64(0));
        return ids;
    }

    private List<long> StringIds(long sessionId)
    {
        var ids = new List<long>();
        using var cmd = _a.Connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM strings WHERE session_id=$s ORDER BY id";
        cmd.Parameters.AddWithValue("$s", sessionId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt64(0));
        return ids;
    }

    // ---- schema ----------------------------------------------------------------------------------------------------

    [Fact]
    public void ANewArchiveHasTheTablesAndTheShotColumn()
    {
        Assert.Equal("6", _a.GetMeta("schema_version"));
        foreach (string t in new[] { "pieces", "cartridge_lots", "cartridges", "measures", "predictions" })
            Assert.Equal(0L, _a.Count(t));
        Assert.Null(_a.ScalarP("SELECT cartridge_id FROM shots LIMIT 1"));      // the column exists (no rows, so no value, but no error)
    }

    [Fact]
    public void AnArchiveOfTheOldSchemaGainsThemWithoutLosingAnything()
    {
        long session = Session(Str("a", 820, 821, 822));
        _a.Dispose();
        SqliteConnection.ClearAllPools();
        string path = Path.Combine(_dir, "archivio.db");
        using (var cn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            cn.Open();
            foreach (string sql in new[]
            {
                "DROP TABLE predictions", "DROP TABLE measures", "DROP TABLE cartridges", "DROP TABLE cartridge_lots", "DROP TABLE pieces",
                "DROP INDEX ix_shots_cartridge", "ALTER TABLE shots DROP COLUMN cartridge_id", "UPDATE meta SET value='4' WHERE key='schema_version'",
            })
            { using var c = cn.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
        }
        using var again = new ArchiveDb(path);
        Assert.Equal("6", again.GetMeta("schema_version"));
        Assert.Equal(3L, again.Count("shots"));                                  // what was there is still there
        Assert.Equal(0L, again.Count("cartridges"));
        Assert.Null(again.ScalarP("SELECT cartridge_id FROM shots LIMIT 1"));
        Assert.True(session > 0);
    }

    [Fact]
    public void OpeningTheArchiveAgainChangesNothing()
    {
        long lot = CartridgeStore.CreateLot(_a, _sh, "Lotto");
        _a.Dispose();
        SqliteConnection.ClearAllPools();
        using var again = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        Assert.Equal(1L, again.Count("cartridge_lots"));
        Assert.Equal(lot, CartridgeStore.Lots(again, _sh).Single().Id);
    }

    // ---- measures --------------------------------------------------------------------------------------------------

    [Fact]
    public void AMeasureTakenAgainReplacesTheOldOneOrIsAveragedIn()
    {
        long p = CartridgeStore.AddPiece(_a, _sh, "case", "5A");
        CartridgeStore.SetMeasure(_a, "piece", p, "case_weight_gr", 120.0);
        CartridgeStore.SetMeasure(_a, "piece", p, "case_weight_gr", 120.4);
        Assert.Equal(120.4, CartridgeStore.Measures(_a, "piece", p)["case_weight_gr"]);
        Assert.Equal(1L, _a.Count("measures"));

        CartridgeStore.SetMeasure(_a, "piece", p, "case_weight_gr", 120.8, average: true);       // mean of 120.4 (one reading) and 120.8
        Assert.Equal(120.6, CartridgeStore.Measures(_a, "piece", p)["case_weight_gr"], 9);
        CartridgeStore.SetMeasure(_a, "piece", p, "case_weight_gr", 121.5, average: true);       // two readings so far: (120.6*2 + 121.5) / 3
        Assert.Equal((120.6 * 2 + 121.5) / 3, CartridgeStore.Measures(_a, "piece", p)["case_weight_gr"], 9);
        Assert.Equal(3L, _a.ScalarP("SELECT readings FROM measures WHERE owner='piece' AND owner_id=$i", ("$i", p)));
    }

    [Fact]
    public void ABadMeasureIsRefused()
    {
        long p = CartridgeStore.AddPiece(_a, _sh, "case", "5A");
        Assert.Throws<ArgumentException>(() => CartridgeStore.SetMeasure(_a, "piece", p, "case_weight_gr", double.NaN));
        Assert.Throws<ArgumentException>(() => CartridgeStore.SetMeasure(_a, "shot", p, "case_weight_gr", 120));
        Assert.Throws<SqliteException>(() => CartridgeStore.AddPiece(_a, _sh, "barrel", "x"));               // not a kind of piece
    }

    [Fact]
    public void ACartridgeCollectsTheMeasuresOfItsPiecesAndOfItself()
    {
        var (lot, cart, _) = Lot(3, i => 120 + i * 0.1, i => 20.30 + i * 0.01);
        var inputs = CartridgeStore.CartridgeInputs(_a, lot);
        Assert.Equal(new[] { 1, 2, 3 }, inputs.Select(c => c.Number));
        Assert.Equal(new[] { "1A", "1B", "1C" }, inputs.Select(c => c.Mark));                                // the mark is the case's
        var second = inputs[1].Measures;
        Assert.Equal(120.1, second["case_weight_gr"], 9);
        Assert.Equal(20.31, second["bullet_bto_mm"], 9);
        Assert.Equal(40.30, second["powder_weight_gr"]);
        Assert.True(cart.Length == 3);
    }

    [Fact]
    public void DeletingACartridgeOrAPieceTakesItsMeasuresAndUnlinksItsShots()
    {
        var (lot, cart, cases) = Lot(2, i => 120 + i, i => 20.3);
        long s = Session(Str("x", 820, 821));
        long str = StringIds(s).Single();
        var shots = ShotIds(str);
        CartridgeStore.LinkShot(_a, shots[0], cart[0]);

        long before = _a.Count("measures");
        _a.Exec("DELETE FROM cartridges WHERE id=$i", ("$i", cart[0]));
        Assert.Equal(before - 1, _a.Count("measures"));                                                      // its powder weight
        Assert.Null(_a.ScalarP("SELECT cartridge_id FROM shots WHERE id=$i", ("$i", shots[0])));
        Assert.Equal(2L, _a.Count("shots"));                                                                 // the shot itself stays

        _a.Exec("UPDATE cartridges SET case_piece_id=NULL WHERE case_piece_id=$p", ("$p", cases[0]));
        _a.Exec("DELETE FROM pieces WHERE id=$p", ("$p", cases[0]));
        Assert.Empty(CartridgeStore.Measures(_a, "piece", cases[0]));
        Assert.True(lot > 0);
    }

    // ---- shots ↔ cartridges ----------------------------------------------------------------------------------------

    [Fact]
    public void ShotsAreLinkedByMarkWhateverTheWayItIsWritten()
    {
        var (lot, cart, _) = Lot(12, i => 120 + i * 0.1, i => 20.3);                                         // marks 1A..1J, 2A, 2B
        long s = Session(Str("serie", 820, 821, 822, 823, 824));
        long str = StringIds(s).Single();
        int linked = CartridgeStore.LinkStringByMarks(_a, str, lot, new[] { "1a", "B1", "1-C", "9Z", "2A" });   // 1A, 1B, 1C, not in the lot, 2A
        Assert.Equal(4, linked);
        var ids = ShotIds(str);
        Assert.Equal(cart[0], _a.ScalarP("SELECT cartridge_id FROM shots WHERE id=$i", ("$i", ids[0])));
        Assert.Equal(cart[1], _a.ScalarP("SELECT cartridge_id FROM shots WHERE id=$i", ("$i", ids[1])));
        Assert.Equal(cart[2], _a.ScalarP("SELECT cartridge_id FROM shots WHERE id=$i", ("$i", ids[2])));
        Assert.Null(_a.ScalarP("SELECT cartridge_id FROM shots WHERE id=$i", ("$i", ids[3])));
        Assert.Equal(cart[10], _a.ScalarP("SELECT cartridge_id FROM shots WHERE id=$i", ("$i", ids[4])));
    }

    [Fact]
    public void WhatTheModelLearnsFromLeavesOutShotsSetAsideAndEmptySlots()
    {
        var (lot, cart, _) = Lot(4, i => 120 + i, i => 20.3);
        long s = Session(Str("a", 820, 821, 822, 823));
        long str = StringIds(s).Single();
        var shots = ShotIds(str);
        for (int i = 0; i < 4; i++) CartridgeStore.LinkShot(_a, shots[i], cart[i]);
        _a.Exec("UPDATE shots SET set_aside=1 WHERE id=$i", ("$i", shots[3]));

        var set = CartridgeStore.LearningShots(_a, _sh);
        Assert.Equal(3, set.Samples.Count);
        Assert.Equal(new[] { 820.0, 821.0, 822.0 }, set.Samples.Select(x => x.VelocityMps));
        Assert.All(set.Samples, x => Assert.Equal(str, x.SeriesId));
        Assert.Equal(121, set.Samples[1].Measures["case_weight_gr"]);
        Assert.Equal(new[] { s }, set.SessionIds);
        Assert.Empty(CartridgeStore.LearningShots(_a, _sh + 99).Samples);                                    // another shooter's shots are not his
        Assert.True(lot > 0);
    }

    // ---- predictions -----------------------------------------------------------------------------------------------

    /// <summary>Three strings of 14 shots each, with a velocity that follows the case weight (+4.7 m/s per grain), linked to a lot.</summary>
    private (long Lot, long[] Cartridges, LearningSet Set) Learned()
    {
        var rnd = new Random(5);
        var (lot, cart, _) = Lot(42, i => Math.Round(120 + (rnd.NextDouble() - 0.5) * 1.4, 2), i => Math.Round(20.30 + (rnd.NextDouble() - 0.5) * 0.04, 3));
        var weights = CartridgeStore.CartridgeInputs(_a, lot).Select(c => c.Measures["case_weight_gr"]).ToArray();
        for (int k = 0; k < 3; k++)
        {
            var v = Enumerable.Range(0, 14).Select(j => Math.Round(820 + 5 * k + 4.7 * (weights[k * 14 + j] - 120) + (rnd.NextDouble() - 0.5) * 2, 1)).ToArray();
            long s = Session(Str("serie" + k, v));
            long str = StringIds(s).Single();
            var shots = ShotIds(str);
            for (int j = 0; j < 14; j++) CartridgeStore.LinkShot(_a, shots[j], cart[k * 14 + j]);
        }
        return (lot, cart, CartridgeStore.LearningShots(_a, _sh));
    }

    [Fact]
    public void ThePredictionIsSavedWithItsModelAndComesBackWhole()
    {
        var (lot, cart, set) = Learned();
        var model = VelocityModel.Learn(set.Samples)!;
        Assert.Contains("case_weight_gr", model.Significant.Select(t => t.Key));
        var (est, _) = CartridgePlanner.Predict(CartridgeStore.CartridgeInputs(_a, lot), model);
        var plan = CartridgePlanner.Compose(est, PlanMode.Training, perSeries: 5, residualSd: model.SdResidual);
        Assert.Null(plan.Error);

        long id = CartridgeStore.SavePrediction(_a, _sh, lot, plan, model, set.SessionIds, "prima della gara");
        var p = CartridgeStore.GetPrediction(_a, id)!;
        Assert.Equal("training", p.Mode);
        Assert.Equal("prima della gara", p.Note);
        Assert.Equal(model.SdResidual, p.ResidualSd, 12);
        Assert.Equal(42, p.Rows.Count);
        var row = p.Rows.Single(r => r.Number == 7);
        Assert.Equal(est.Single(e => e.Number == 7).Estimate, row.Estimate, 12);
        Assert.Contains("case_weight_gr", row.Measures.Keys);
        Assert.Null(p.VerifiedAt);
        Assert.True(cart.Length == 42);
    }

    [Fact]
    public void AFrozenPredictionCannotBeChangedAndAVerifiedOneCannotBeDeleted()
    {
        var (lot, _, set) = Learned();
        var model = VelocityModel.Learn(set.Samples)!;
        var (est, _) = CartridgePlanner.Predict(CartridgeStore.CartridgeInputs(_a, lot), model);
        long id = CartridgeStore.SavePrediction(_a, _sh, lot, CartridgePlanner.Compose(est, PlanMode.Training, residualSd: model.SdResidual), model, set.SessionIds);

        foreach (string col in new[] { "rows", "model", "residual_sd", "created_at", "mode", "lot_id" })
        {
            var ex = Assert.Throws<SqliteException>(() => _a.Exec($"UPDATE predictions SET {col}={col} WHERE id=$i", ("$i", id)));
            Assert.Contains("frozen", ex.Message);
        }
        _a.Exec("UPDATE predictions SET note='aggiunta dopo' WHERE id=$i", ("$i", id));                       // a note may be added
        Assert.Equal("aggiunta dopo", CartridgeStore.GetPrediction(_a, id)!.Note);

        _a.Exec("UPDATE predictions SET verified_at='2026-11-20 10:00:00', verification='{}' WHERE id=$i", ("$i", id));
        var del = Assert.Throws<SqliteException>(() => _a.Exec("DELETE FROM predictions WHERE id=$i", ("$i", id)));
        Assert.Contains("cannot be deleted", del.Message);

        long other = CartridgeStore.SavePrediction(_a, _sh, lot, CartridgePlanner.Compose(est, PlanMode.Training, residualSd: model.SdResidual), model, set.SessionIds);
        _a.Exec("DELETE FROM predictions WHERE id=$i", ("$i", other));                                       // one never checked can go
        Assert.Null(CartridgeStore.GetPrediction(_a, other));
    }

    [Fact]
    public void AnErroringPlanIsNotSaved()
    {
        var (lot, _, set) = Learned();
        var model = VelocityModel.Learn(set.Samples)!;
        var (est, _) = CartridgePlanner.Predict(CartridgeStore.CartridgeInputs(_a, lot), model);
        var plan = CartridgePlanner.Compose(est, PlanMode.Race, targets: 9, shotsPerTarget: 10, residualSd: model.SdResidual);   // 90 + 5 needed, 42 there
        Assert.NotNull(plan.Error);
        Assert.Throws<InvalidOperationException>(() => CartridgeStore.SavePrediction(_a, _sh, lot, plan, model, set.SessionIds));
    }

    [Fact]
    public void AfterTheShootingThePredictionIsCheckedAgainstWhatWasMeasured()
    {
        var (lot, cart, set) = Learned();
        var model = VelocityModel.Learn(set.Samples)!;
        var inputs = CartridgeStore.CartridgeInputs(_a, lot);
        var (est, _) = CartridgePlanner.Predict(inputs, model);
        long id = CartridgeStore.SavePrediction(_a, _sh, lot, CartridgePlanner.Compose(est, PlanMode.Training, residualSd: model.SdResidual), model, set.SessionIds);

        // a new day: 12 of the cartridges are fired, velocity following the case weight again
        var rnd = new Random(11);
        var chosen = Enumerable.Range(0, 12).Select(i => i * 3).ToList();
        var v = chosen.Select(i => Math.Round(830 + 4.7 * (inputs[i].Measures["case_weight_gr"] - 120) + (rnd.NextDouble() - 0.5) * 1.6, 1)).ToArray();
        long s = Session(Str("giorno", v));
        long str = StringIds(s).Single();
        var shots = ShotIds(str);
        for (int j = 0; j < 12; j++) CartridgeStore.LinkShot(_a, shots[j], cart[chosen[j]]);

        var (res, independent) = CartridgeStore.Verify(_a, id, s)!.Value;
        Assert.True(independent);                                                                            // this session was not in the learning set
        Assert.Equal(12, res.N);
        Assert.Equal(1, res.Series);
        Assert.True(res.Slope > 0.5 && res.Slope < 1.5);
        Assert.Equal(CheckVerdict.Works, res.Verdict);
        Assert.True(res.SdWith < res.SdWithout);
        var saved = CartridgeStore.GetPrediction(_a, id)!;
        Assert.NotNull(saved.VerifiedAt);
        Assert.Equal(s, saved.VerificationSessionId);

        // a session the model learned from is checked too, but said not to be independent
        var firstLearned = set.SessionIds[0];
        Assert.False(CartridgeStore.Verify(_a, id, firstLearned)!.Value.Independent);
    }

    [Fact]
    public void ACheckWithTooFewMatchedShotsSaysNothing()
    {
        var (lot, cart, set) = Learned();
        var model = VelocityModel.Learn(set.Samples)!;
        var (est, _) = CartridgePlanner.Predict(CartridgeStore.CartridgeInputs(_a, lot), model);
        long id = CartridgeStore.SavePrediction(_a, _sh, lot, CartridgePlanner.Compose(est, PlanMode.Training, residualSd: model.SdResidual), model, set.SessionIds);
        long s = Session(Str("poche", 830, 831, 832));
        var shots = ShotIds(StringIds(s).Single());
        for (int j = 0; j < 3; j++) CartridgeStore.LinkShot(_a, shots[j], cart[j]);
        Assert.Null(CartridgeStore.Verify(_a, id, s));
        Assert.Null(CartridgeStore.GetPrediction(_a, id)!.VerifiedAt);                                       // nothing was written
    }
}

public sealed class PredictionCheckTests
{
    private static CheckedShot[] Shots(params (string S, double V, double E)[] rows) => rows.Select(r => new CheckedShot(r.S, r.V, r.E)).ToArray();

    // expected values from scipy.stats.linregress on the same numbers, with the series means taken out as in "Analisi DS"
    private static readonly CheckedShot[] Good = Shots(
        ("A", 820.0, -1.0), ("A", 822.5, 1.0), ("A", 819.0, -2.0), ("A", 823.5, 2.0), ("A", 821.0, 0.0),
        ("B", 830.0, 1.5), ("B", 827.0, -1.5), ("B", 831.5, 2.0), ("B", 828.0, -1.0), ("B", 829.0, -1.0));

    [Fact]
    public void ACorrectPredictionWorks()
    {
        var r = PredictionCheck.Verify(Good)!;
        Assert.Equal(10, r.N); Assert.Equal(2, r.Series);
        Assert.Equal(1.7853571071357126, r.SdWithout, 9);
        Assert.Equal(0.5, r.SdWith, 9);
        Assert.Equal(0.9622228370495296, r.R!.Value, 9);
        Assert.Equal(1.0731707317073167, r.Slope!.Value, 9);
        Assert.Equal(4.25637860597107e-06, r.POneSided!.Value, 9);
        Assert.Equal(CheckVerdict.Works, r.Verdict);
    }

    [Fact]
    public void AnEstimateThatPointsTheWrongWayFails()
    {
        var wrong = Shots(
            ("A", 820.0, 1.0), ("A", 822.5, -1.0), ("A", 819.0, 2.0), ("A", 823.5, -2.0), ("A", 821.0, 0.0),
            ("B", 830.0, -1.5), ("B", 827.0, 1.5), ("B", 831.5, -2.0), ("B", 828.0, 1.0), ("B", 829.0, 1.0));
        var r = PredictionCheck.Verify(wrong)!;
        Assert.Equal(3.3541019662496847, r.SdWith, 9);
        Assert.Equal(-0.9622228370495296, r.R!.Value, 9);
        Assert.Equal(0.999995743621394, r.POneSided!.Value, 9);
        Assert.Equal(CheckVerdict.Failed, r.Verdict);
    }

    [Fact]
    public void AWeakLinkIsRightDirectionButNotShown()
    {
        var weak = Shots(("A", 820.0, -1.0), ("A", 822.5, 0.4), ("A", 819.0, 1.2), ("A", 823.5, -0.3), ("A", 821.0, 0.2), ("A", 824.0, 0.5), ("A", 818.5, -0.4));
        var r = PredictionCheck.Verify(weak)!;
        Assert.Equal(2.1767166186932183, r.SdWithout, 9);
        Assert.Equal(2.2313779641843783, r.SdWith, 9);
        Assert.Equal(0.26595744680851047, r.Slope!.Value, 9);
        Assert.Equal(0.4258754058646891, r.POneSided!.Value, 9);
        Assert.Equal(CheckVerdict.RightDirection, r.Verdict);
    }

    [Fact]
    public void WithTheSameEstimateEverywhereThereIsNothingToCheck()
    {
        var same = Shots(("A", 820, 0.5), ("A", 822, 0.5), ("A", 819, 0.5), ("A", 823, 0.5), ("A", 821, 0.5));
        var r = PredictionCheck.Verify(same)!;
        Assert.Equal(CheckVerdict.NothingToCheck, r.Verdict);
        Assert.Null(r.R);
    }

    [Fact]
    public void FewerThanFiveShotsAreNotChecked() => Assert.Null(PredictionCheck.Verify(Good.Take(4).ToArray()));
}

public sealed class MeasureFieldsTests
{
    [Theory]
    [InlineData("1A", "A1")]
    [InlineData("A1", "A1")]
    [InlineData("a01", "A1")]
    [InlineData("1-A", "A1")]
    [InlineData(" 12 b ", "B12")]
    [InlineData("a_5", "A5")]
    [InlineData("xyz", "XYZ")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TheSameMarkWrittenInDifferentWaysIsOne(string? written, string expected) => Assert.Equal(expected, MeasureFields.NormalizeMark(written));

    [Theory]
    [InlineData("A5", "5A")]
    [InlineData("B12", "12B")]
    [InlineData("5A", "5A")]
    [InlineData("", "")]
    public void TheHoleOfTheTrayBecomesTheCodeEngravedOnTheCase(string hole, string code) => Assert.Equal(code, MeasureFields.EngravedCode(hole));

    [Theory]
    [InlineData("case_weight_gr", "gr")]
    [InlineData("bullet_bto_mm", "mm")]
    [InlineData("case_capacity_gn", "gn H2O")]
    [InlineData("case_internal_volume_ml", "ml")]
    [InlineData("bullet_seating_force_n", "N")]
    [InlineData("whatever", "")]
    public void TheUnitIsReadFromTheEndOfTheKey(string key, string unit) => Assert.Equal(unit, MeasureFields.UnitOf(key));

    [Fact]
    public void EveryKnownFieldHasAUnitAndALabelAndAKnownOwner()
    {
        foreach (var f in MeasureFields.All)
        {
            Assert.NotEqual("", MeasureFields.UnitOf(f.Key));
            Assert.NotEqual("", f.Label);
            Assert.Contains(f.Owner, new[] { "case", "bullet", "primer", MeasureFields.Cartridge });
        }
        Assert.Equal(MeasureFields.All.Count, MeasureFields.All.Select(f => f.Key).Distinct().Count());
    }
}
