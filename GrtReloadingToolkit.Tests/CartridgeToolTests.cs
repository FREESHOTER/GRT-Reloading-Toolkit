using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Cartridges;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>What the "Cartridges" window writes and reads, and the operations behind its buttons.</summary>
public sealed class CartridgeTextTests
{
    [Fact]
    public void ATrayIsNumbersAndLettersColumnByColumn()
    {
        Assert.Equal(new[] { "1A", "1B", "2A", "2B", "3A", "3B" }, CartridgeText.TrayMarks(3, 2));
        var full = CartridgeText.TrayMarks(10, 10);
        Assert.Equal(100, full.Count);
        Assert.Equal(100, full.Distinct().Count());
        Assert.Equal("1A", full[0]); Assert.Equal("1J", full[9]); Assert.Equal("10J", full[99]);
        Assert.Throws<ArgumentOutOfRangeException>(() => CartridgeText.TrayMarks(0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => CartridgeText.TrayMarks(5, 27));
    }

    [Fact]
    public void MarksCanBeSeparatedByAnythingAndKeepTheirOrder()
    {
        Assert.Equal(new[] { "1A", "B2", "3C", "4D", "5E" }, CartridgeText.ParseMarks("1A B2,3C;\n4D\t 5E ,"));
        Assert.Empty(CartridgeText.ParseMarks("  \n , ;"));
        Assert.Empty(CartridgeText.ParseMarks(null));
    }

    [Theory]
    [InlineData("120,5", 120.5)]
    [InlineData("120.5", 120.5)]
    [InlineData(" 20,30 ", 20.30)]
    [InlineData("1.234,5", 1234.5)]
    [InlineData("1,234.5", 1234.5)]
    [InlineData("-0,04", -0.04)]
    [InlineData("3", 3)]
    public void ANumberIsReadWithACommaOrADot(string text, double expected)
    {
        Assert.True(CartridgeText.TryParseNumber(text, out double v));
        Assert.Equal(expected, v, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12 g")]
    [InlineData("1e999")]
    [InlineData(null)]
    public void SomethingThatIsNotANumberIsRefused(string? text) => Assert.False(CartridgeText.TryParseNumber(text, out _));

    private static VelocityModelResult Model()
    {
        var rnd = new Random(3);
        var shots = new List<ShotSample>();
        for (int s = 0; s < 3; s++)
            for (int i = 0; i < 12; i++)
            {
                double cw = Math.Round(120 + (rnd.NextDouble() - 0.5) * 1.4, 2);
                shots.Add(new ShotSample(s, Math.Round(820 + 5 * s + 4.7 * (cw - 120) + (rnd.NextDouble() - 0.5) * 2, 1),
                    new Dictionary<string, double> { ["case_weight_gr"] = cw, ["powder_weight_gr"] = 40.30 + (rnd.NextDouble() - 0.5) * 0.04 }));
            }
        return VelocityModel.Learn(shots)!;
    }

    [Fact]
    public void TheModelIsSummedUpInPlainWords()
    {
        string t = CartridgeText.ModelSummary(Model());
        Assert.Contains("36 shots fired from measured cartridges, in 3 strings", t);
        Assert.Contains("Case weight: +", t);
        Assert.Contains("m/s per gr", t);
        Assert.Contains("What the model does not explain", t);
        Assert.Contains("Each shot estimated without itself", t);
    }

    [Fact]
    public void TheModelSummaryCanSpeakInTheUnitChosenForAMeasure()
    {
        var m = Model();
        string grains = CartridgeText.ModelSummary(m);
        string grams = CartridgeText.ModelSummary(m, (key, v) => key == "case_weight_gr" ? (v * 0.06479891, "g") : (v, MeasureFields.UnitOf(key)));
        Assert.Contains("m/s per g", grams);
        Assert.DoesNotContain("m/s per gr", grams);
        // one gram is 15.43 grains: the slope per gram is 15.43 times the slope per grain
        double Slope(string t) => double.Parse(System.Text.RegularExpressions.Regex.Match(t, @"Case weight: ([+-]\d+\.\d+) m/s per").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Slope(grains) * 15.4323584, Slope(grams), 1);
    }

    [Fact]
    public void WithNothingProvenTheModelSaysSoAndNamesTheMinimum()
    {
        var rnd = new Random(1);
        var shots = Enumerable.Range(0, 30).Select(i => new ShotSample(i / 10, 820 + rnd.NextDouble() * 4, new Dictionary<string, double> { ["case_weight_gr"] = 120 + rnd.NextDouble() })).ToList();
        string t = CartridgeText.ModelSummary(VelocityModel.Learn(shots)!);
        Assert.Contains("No measure of the cartridges has a proven effect", t);
        Assert.Contains("at least 8 shots", t);
    }

    [Fact]
    public void TheVerdictSaysWhetherThePredictionHeldAndWhetherTheCheckIsFair()
    {
        var good = new PredictionCheckResult(12, 1, 2.0, 1.0, 0.9, 1.1, 0.0001, CheckVerdict.Works);
        Assert.StartsWith("The prediction WORKS", CartridgeText.VerdictText(good, true));
        Assert.Contains("link +0.90", CartridgeText.VerdictText(good, true));
        Assert.Contains("Slope 1.10", CartridgeText.VerdictText(good, true));
        Assert.StartsWith("ATTENTION: this session is one the model learned from", CartridgeText.VerdictText(good, false));

        var weak = new PredictionCheckResult(12, 1, 2.0, 1.9, 0.2, 0.3, 0.3, CheckVerdict.RightDirection);
        Assert.Contains("not shown yet", CartridgeText.VerdictText(weak, true));
        var bad = new PredictionCheckResult(12, 1, 2.0, 3.0, -0.4, -0.5, 0.9, CheckVerdict.Failed);
        Assert.Contains("did NOT work", CartridgeText.VerdictText(bad, true));
        Assert.Contains("link -0.40", CartridgeText.VerdictText(bad, true));
        var none = new PredictionCheckResult(5, 1, 2.0, 2.0, null, null, null, CheckVerdict.NothingToCheck);
        Assert.Contains("nothing to check", CartridgeText.VerdictText(none, true));
    }
}

public sealed class MarkOrderTests
{
    [Fact]
    public void MarksAreCountedTheWayAShooterCountsThem()
    {
        var shuffled = new[] { "10A", "2A", "1B", "1A", "9J", "10J", "2B", "O1·10A", "O1·2A" };
        var sorted = shuffled.OrderBy(m => m, Comparer<string>.Create(CartridgeText.CompareMarks)).ToArray();
        Assert.Equal(new[] { "1A", "1B", "2A", "2B", "9J", "10A", "10J", "O1·2A", "O1·10A" }, sorted);
    }

    [Theory]
    [InlineData("a1", "A1", 0)]
    [InlineData("01A", "1A", 0)]
    [InlineData("1A", "1AB", -1)]
    [InlineData("2", "10", -1)]
    [InlineData("", "A", -1)]
    public void ComparingMarksIgnoresCaseAndLeadingZeros(string a, string b, int sign) => Assert.Equal(sign, Math.Sign(CartridgeText.CompareMarks(a, b)));
}

public sealed class CartridgeOperationsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cartucce_op_" + Guid.NewGuid().ToString("N"));
    private readonly ArchiveDb _a;
    private readonly long _sh;

    public CartridgeOperationsTests()
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
    public void TheSameCaseMarkWrittenAnyWayIsTheSamePiece()
    {
        long a = CartridgeStore.FindOrAddPiece(_a, _sh, "case", "5A");
        Assert.Equal(a, CartridgeStore.FindOrAddPiece(_a, _sh, "case", "A5"));
        Assert.Equal(a, CartridgeStore.FindOrAddPiece(_a, _sh, "case", " a05 "));
        Assert.NotEqual(a, CartridgeStore.FindOrAddPiece(_a, _sh, "bullet", "5A"));                            // another kind of piece
        Assert.Equal(2L, _a.Count("pieces"));
        Assert.Throws<ArgumentException>(() => CartridgeStore.FindOrAddPiece(_a, _sh, "case", "  "));
    }

    [Fact]
    public void ARemovedMeasureIsGoneAndASecondShooterHasHisOwnPieces()
    {
        long p = CartridgeStore.AddPiece(_a, _sh, "case", "1A");
        CartridgeStore.SetMeasure(_a, "piece", p, "case_weight_gr", 120);
        CartridgeStore.SetMeasure(_a, "piece", p, "case_length_mm", 51.5);
        CartridgeStore.DeleteMeasure(_a, "piece", p, "case_weight_gr");
        Assert.Equal(new[] { "case_length_mm" }, CartridgeStore.Measures(_a, "piece", p).Keys);

        long other = _a.Insert("shooters", ("name", "Altro"));
        Assert.Empty(CartridgeStore.Pieces(_a, other));
        Assert.Single(CartridgeStore.Pieces(_a, _sh));
    }

    [Fact]
    public void ADeletedPieceLeavesItsCartridgesWithoutIt()
    {
        long lot = CartridgeStore.CreateLot(_a, _sh, "L");
        long c = CartridgeStore.AddPiece(_a, _sh, "case", "1A");
        long b = CartridgeStore.AddPiece(_a, _sh, "bullet", "B1");
        long cart = CartridgeStore.AddCartridge(_a, lot, 1, c, b);
        CartridgeStore.DeletePiece(_a, c);
        var row = CartridgeStore.Cartridges(_a, lot).Single();
        Assert.Equal(cart, row.Id);
        Assert.Null(row.CasePieceId);
        Assert.Equal(b, row.BulletPieceId);
    }

    [Fact]
    public void ACartridgeCanBeRetargetedAndMarkedOutOfSpec()
    {
        long lot = CartridgeStore.CreateLot(_a, _sh, "L");
        long cart = CartridgeStore.AddCartridge(_a, lot, 1);
        long c = CartridgeStore.AddPiece(_a, _sh, "case", "1A");
        CartridgeStore.UpdateCartridge(_a, cart, c, null, null, "2", true, "runout");
        var row = CartridgeStore.Cartridges(_a, lot).Single();
        Assert.Equal(c, row.CasePieceId);
        Assert.Equal("2", row.Target);
        Assert.True(row.OutOfSpec);
        Assert.Equal("runout", row.OutReason);
    }

    [Fact]
    public void NumbersContinueInALot()
    {
        long lot = CartridgeStore.CreateLot(_a, _sh, "L");
        Assert.Equal(1, CartridgeStore.NextNumber(_a, lot));
        for (int i = 1; i <= 3; i++) CartridgeStore.AddCartridge(_a, lot, i);
        Assert.Equal(4, CartridgeStore.NextNumber(_a, lot));
        Assert.Throws<SqliteException>(() => CartridgeStore.AddCartridge(_a, lot, 2));                       // a number is used once per lot
    }

    [Fact]
    public void ALotWithPredictionsCannotBeDeletedAndOneWithoutCan()
    {
        long lot = CartridgeStore.CreateLot(_a, _sh, "L");
        long cart = CartridgeStore.AddCartridge(_a, lot, 1);
        CartridgeStore.SetMeasure(_a, "cartridge", cart, "powder_weight_gr", 40.3);
        _a.Exec(@"INSERT INTO predictions(shooter_id, lot_id, mode, created_at, model, rows) VALUES($s,$l,'training','2026-11-19 10:00:00','{}','[]')",
            ("$s", _sh), ("$l", lot));
        Assert.Throws<InvalidOperationException>(() => CartridgeStore.DeleteLot(_a, lot));
        Assert.Single(CartridgeStore.Predictions(_a, _sh));
        Assert.Equal("L", CartridgeStore.Predictions(_a, _sh).Single().LotName);
        Assert.False(CartridgeStore.Predictions(_a, _sh).Single().Verified);

        long free = CartridgeStore.CreateLot(_a, _sh, "libero");
        long c2 = CartridgeStore.AddCartridge(_a, free, 1);
        CartridgeStore.SetMeasure(_a, "cartridge", c2, "powder_weight_gr", 40.3);
        CartridgeStore.DeleteLot(_a, free);                                                                  // its cartridges and their measures go with it
        Assert.Empty(CartridgeStore.Cartridges(_a, free));
        Assert.Empty(CartridgeStore.Measures(_a, "cartridge", c2));
    }
}
