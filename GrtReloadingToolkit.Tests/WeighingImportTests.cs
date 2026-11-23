using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Cartridges;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>Taking the sessions of the weighing programs into the archive (Phase C). The files below are invented, in the shape the programs save.</summary>
public sealed class WeighingImportTests : IDisposable
{
    private const string OgiveTray = """
    {
      "Name": "Ogive prova", "Caliber": ".308 Win", "Mode": "Vassoio", "Created": "2026-11-01T10:00:00.0000000+01:00",
      "Lots": { "WidthGn": 0.2, "AnchorGn": 0, "MinGn": 160, "MaxGn": null },
      "TrayRows": 10, "TrayCols": 10, "Oal": false,
      "Bto": { "OffsetMm": 0.1, "ReferenceMm": 0, "Relative": true, "Direction": 1 },
      "Pieces": [
        { "N": 1, "Time": "2026-11-01T10:01:00", "WeightGn": 168.20, "BtoMm": 20.50, "Lot": null, "Reject": false, "Tray": 1, "Hole": 0 },
        { "N": 2, "Time": "2026-11-01T10:02:00", "WeightGn": 168.30, "BtoMm": null,  "Lot": null, "Reject": false, "Tray": 1, "Hole": 1 },
        { "N": 3, "Time": "2026-11-01T10:03:00", "WeightGn": 150.00, "BtoMm": 20.40, "Lot": null, "Reject": true,  "Tray": 1, "Hole": 11 },
        { "N": 101, "Time": "2026-11-01T11:03:00", "WeightGn": 168.10, "BtoMm": 20.55, "Lot": null, "Reject": false, "Tray": 2, "Hole": 0 }
      ]
    }
    """;

    private const string OgiveLots = """
    {
      "Name": "Ogive a scaglioni", "Caliber": "6.5 CM", "Mode": "Lotti", "Created": "2026-11-02T09:00:00+01:00",
      "Lots": { "WidthGn": 0.5, "AnchorGn": 0, "MinGn": null, "MaxGn": null },
      "Oal": true,
      "Bto": { "OffsetMm": 0, "ReferenceMm": 34.5, "Relative": false, "Direction": 1 },
      "Pieces": [
        { "N": 1, "WeightGn": 139.20, "BtoMm": 34.52, "Lot": 278, "Reject": false },
        { "N": 2, "WeightGn": 139.60, "BtoMm": 34.49, "Lot": 279, "Reject": false }
      ]
    }
    """;

    private const string CasesPhase1 = """
    {
      "Name": "Bossoli fase 1", "Caliber": "6.5 CM", "Created": "2026-11-03T08:00:00+01:00", "TrayRows": 10, "TrayCols": 10, "Count": 4,
      "Passes": [ "Vuoto", "Pieno" ],
      "Cases": [
        { "Hole": 0, "EmptyGn": 120.00, "FullGn": 150.50 },
        { "Hole": 1, "EmptyGn": 119.80, "FullGn": null },
        { "Hole": 2 },
        { "Hole": 3, "EmptyGn": 120.30, "FullGn": 151.00 }
      ]
    }
    """;

    private const string CasesPhase2 = """
    {
      "Name": "Bossoli fase 2", "Caliber": "6.5 CM", "Created": "2026-11-04T08:00:00+01:00", "TrayRows": 10, "TrayCols": 10, "Count": 2,
      "Passes": [ "Spalla", "Lunghezza" ],
      "Shoulder": { "OffsetMm": 0, "ReferenceMm": 0, "Relative": true, "Direction": 1 },
      "CaseLength": { "OffsetMm": 0, "ReferenceMm": 51.2, "Relative": false, "Direction": 1 },
      "Cases": [
        { "Hole": 0, "ShoulderMm": -0.0508, "LengthMm": 51.21 },
        { "Hole": 1, "ShoulderMm": 0.0254, "LengthMm": 51.18 }
      ]
    }
    """;

    private const string Primers = """
    {
      "Name": "Inneschi small", "Created": "2026-11-05T08:00:00+01:00", "TrayRows": 10, "TrayCols": 10, "Count": 2,
      "Passes": [ "Innesco" ],
      "Primer": { "Relative": true },
      "Cases": [ { "Hole": 0, "PrimerMm": 0.0254 }, { "Hole": 1, "PrimerMm": -0.0127 } ]
    }
    """;

    private const string Tension = """
    {
      "Name": "Tensione", "Created": "2026-11-06T08:00:00+01:00", "TrayRows": 10, "TrayCols": 10, "Count": 1,
      "Passes": [ "ColloVuoto", "ColloCarico" ],
      "Neck": { "Relative": true },
      "Cases": [ { "Hole": 0, "NeckEmptyMm": 0.0, "NeckLoadedMm": 0.0450 } ]
    }
    """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pesate_test_" + Guid.NewGuid().ToString("N"));
    private readonly ArchiveDb _a;
    private readonly long _sh;

    public WeighingImportTests()
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

    // ---- reading -----------------------------------------------------------------------------------------------------

    [Fact]
    public void ABulletTrayIsReadWithItsMeasuresAndItsRejects()
    {
        var b = WeighingReader.Read(OgiveTray);
        Assert.Equal("bullet", b.PieceKind);
        Assert.Equal("PesataOgive", b.Program);
        Assert.Equal(4, b.Pieces.Count);
        var first = b.Pieces[0];
        Assert.Equal("A1", first.HoleName);
        Assert.Equal(168.20, first.Measures["bullet_weight_gr"]);
        Assert.Equal(20.50, first.Measures["bullet_bto_mm"]);
        Assert.Equal("relative to the sample", first.MeasureNotes["bullet_bto_mm"]);
        Assert.False(b.Pieces[1].Measures.ContainsKey("bullet_bto_mm"));                      // no length typed for this one
        var rej = b.Pieces[2];
        Assert.True(rej.Rejected);
        Assert.Equal("B2", rej.HoleName);                                                      // hole 11 of 10 columns
        Assert.Contains("set aside", rej.Note);
        Assert.Equal(2, b.Pieces[3].Tray);
        Assert.Contains(b.Warnings, w => w.Contains("relative to the sample piece"));
        Assert.Contains(b.Warnings, w => w.Contains("1 pieces have no length"));
    }

    [Fact]
    public void AnOalSessionKeepsTheLengthAsABulletLengthAndTheWeightRangeAsANote()
    {
        var b = WeighingReader.Read(OgiveLots);
        Assert.Equal("6.5 CM", b.Caliber);
        Assert.Equal(34.52, b.Pieces[0].Measures["bullet_length_mm"]);
        Assert.False(b.Pieces[0].Measures.ContainsKey("bullet_bto_mm"));
        Assert.Empty(b.Pieces[0].MeasureNotes);                                                // an absolute calibration: no note
        Assert.Equal("", b.Pieces[0].HoleName);
        Assert.Contains("weight range 139.0–139.5", b.Pieces[0].Note);
        Assert.Contains("weight range 139.5–140.0", b.Pieces[1].Note);
        Assert.Empty(b.Warnings);
    }

    [Fact]
    public void CasesGiveTheirWeightAndTheirCapacityAndEmptyHolesAreLeftOut()
    {
        var b = WeighingReader.Read(CasesPhase1);
        Assert.Equal("case", b.PieceKind);
        Assert.Equal(3, b.Pieces.Count);                                                       // the hole with nothing measured is not a piece
        Assert.Equal(120.00, b.Pieces[0].Measures["case_weight_gr"]);
        Assert.Equal(30.50, b.Pieces[0].Measures["case_capacity_gn"], 9);                      // full − empty
        Assert.False(b.Pieces[1].Measures.ContainsKey("case_capacity_gn"));                    // no water weight yet
        Assert.Equal("A4", b.Pieces[2].HoleName);                                              // hole 3 of 10 columns
        Assert.Contains(b.Warnings, w => w.Contains("1 holes have nothing measured"));
    }

    [Fact]
    public void ShoulderAndLengthComeWithTheirRelativeNotes()
    {
        var b = WeighingReader.Read(CasesPhase2);
        Assert.Equal(-0.0508, b.Pieces[0].Measures["case_shoulder_mm"]);
        Assert.Equal(51.21, b.Pieces[0].Measures["case_length_mm"]);
        Assert.Equal("relative to the sample", b.Pieces[0].MeasureNotes["case_shoulder_mm"]);
        Assert.False(b.Pieces[0].MeasureNotes.ContainsKey("case_length_mm"));                  // the length sample had its true length
        Assert.Contains(b.Warnings, w => w.Contains("shoulder") && !w.Contains("length"));
    }

    [Fact]
    public void APrimerTrayIsATrayOfPrimers()
    {
        var b = WeighingReader.Read(Primers);
        Assert.Equal("primer", b.PieceKind);
        Assert.Equal(0.0254, b.Pieces[0].Measures["primer_height_mm"]);
        Assert.Equal(-0.0127, b.Pieces[1].Measures["primer_height_mm"]);
    }

    [Fact]
    public void NeckTensionIsTheLoadedDiameterMinusTheEmptyOne()
    {
        var m = WeighingReader.Read(Tension).Pieces.Single().Measures;
        Assert.Equal(0.0, m["case_neck_diameter_mm"]);
        Assert.Equal(0.0450, m["loaded_neck_diameter_mm"]);
        Assert.Equal(0.0450, m["neck_tension_mm"], 12);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{ \"Name\": \"x\" }")]
    [InlineData("{ \"Cases\": [] }")]
    public void ThingsThatAreNotASessionAreRefused(string text) => Assert.Throws<InvalidDataException>(() => WeighingReader.Read(text));

    [Theory]
    [InlineData(0, 10, "A1")]
    [InlineData(9, 10, "A10")]
    [InlineData(10, 10, "B1")]
    [InlineData(99, 10, "J10")]
    [InlineData(260, 10, "AA1")]
    public void HolesAreNamedRowLetterThenColumn(int hole, int cols, string name) => Assert.Equal(name, WeighingReader.HoleName(hole, cols));

    // ---- importing -------------------------------------------------------------------------------------------------

    [Fact]
    public void ASessionBecomesPiecesMarkedWithItsOwnCode()
    {
        var r = CartridgeStore.ImportWeighing(_a, _sh, WeighingReader.Read(OgiveTray), sourcePath: @"C:\x\ogive.json");
        Assert.Equal("O1", r.BatchCode);
        Assert.Equal(4, r.Added); Assert.Equal(0, r.Updated); Assert.Equal(1, r.SetAside);
        var pieces = CartridgeStore.Pieces(_a, _sh, "bullet");
        Assert.Equal(new[] { "O1·1A", "O1·2A", "O1·2B", "O1·T2·1A" }.OrderBy(x => x, StringComparer.Ordinal), pieces.Select(p => p.Mark).OrderBy(x => x, StringComparer.Ordinal));
        var one = pieces.Single(p => p.Mark == "O1·1A");
        var m = CartridgeStore.Measures(_a, "piece", one.Id);
        Assert.Equal(168.20, m["bullet_weight_gr"]); Assert.Equal(20.50, m["bullet_bto_mm"]);
        Assert.Contains("Ogive prova", one.Notes); Assert.Contains("tray 1, hole A1", one.Notes);
        Assert.Equal("relative to the sample", _a.ScalarP("SELECT note FROM measures WHERE owner='piece' AND owner_id=$i AND field='bullet_bto_mm'", ("$i", one.Id)));
        Assert.True(pieces.Single(p => p.Mark == "O1·2B").Retired);                           // set aside by the program
        Assert.Equal(3, CartridgeStore.Pieces(_a, _sh, "bullet", includeRetired: false).Count);
        Assert.Equal("pesate: PesataOgive", _a.ScalarP("SELECT source FROM imports ORDER BY id DESC LIMIT 1"));
    }

    [Fact]
    public void ReadingTheSameSessionAgainUpdatesInsteadOfDuplicating()
    {
        var batch = WeighingReader.Read(OgiveTray);
        var first = CartridgeStore.ImportWeighing(_a, _sh, batch);
        long pieces = _a.Count("pieces"), measures = _a.Count("measures");
        var second = CartridgeStore.ImportWeighing(_a, _sh, batch);
        Assert.Equal(first.BatchCode, second.BatchCode);
        Assert.Equal(0, second.Added); Assert.Equal(4, second.Updated);
        Assert.Equal(pieces, _a.Count("pieces")); Assert.Equal(measures, _a.Count("measures"));

        // the same piece measured again: the new value replaces the old one
        var changed = WeighingReader.Read(OgiveTray.Replace("168.20", "168.25"));
        CartridgeStore.ImportWeighing(_a, _sh, changed);
        var id = CartridgeStore.Pieces(_a, _sh, "bullet").Single(p => p.Mark == "O1·1A").Id;
        Assert.Equal(168.25, CartridgeStore.Measures(_a, "piece", id)["bullet_weight_gr"]);
    }

    [Fact]
    public void EachSessionGetsTheNextCodeOfItsKind()
    {
        Assert.Equal("O1", CartridgeStore.ImportWeighing(_a, _sh, WeighingReader.Read(OgiveTray)).BatchCode);
        Assert.Equal("O2", CartridgeStore.ImportWeighing(_a, _sh, WeighingReader.Read(OgiveLots)).BatchCode);
        Assert.Equal("B1", CartridgeStore.ImportWeighing(_a, _sh, WeighingReader.Read(CasesPhase1)).BatchCode);
        Assert.Equal("I1", CartridgeStore.ImportWeighing(_a, _sh, WeighingReader.Read(Primers)).BatchCode);
        Assert.Equal("B2", CartridgeStore.ImportWeighing(_a, _sh, WeighingReader.Read(CasesPhase2)).BatchCode);
        Assert.Equal("O1", CartridgeStore.ImportWeighing(_a, _sh, WeighingReader.Read(OgiveTray)).BatchCode);       // seen before: same code
        Assert.Equal(2, CartridgeStore.Pieces(_a, _sh, "bullet").Select(p => p.Mark).Count(m => m.StartsWith("O2·", StringComparison.Ordinal)));
    }

    [Fact]
    public void WithEngravedMarksTheHolesAreTheCasesAndTwoPhasesMeetInTheSamePiece()
    {
        CartridgeStore.ImportWeighing(_a, _sh, WeighingReader.Read(CasesPhase1), engravedMarks: true);
        var r = CartridgeStore.ImportWeighing(_a, _sh, WeighingReader.Read(CasesPhase2), engravedMarks: true);
        Assert.Equal(0, r.Added); Assert.Equal(2, r.Updated);                                // holes A1 and A2 were there from phase 1
        Assert.Equal("", r.BatchCode);                                                         // the engraved codes need no code of their own
        var cases = CartridgeStore.Pieces(_a, _sh, "case");
        Assert.Equal(3, cases.Count);                                                          // 1A, 2A, 4A from phase 1
        var a1 = cases.Single(p => p.Mark == "1A");
        var m = CartridgeStore.Measures(_a, "piece", a1.Id);
        Assert.Equal(120.00, m["case_weight_gr"]);                                             // from phase 1
        Assert.Equal(51.21, m["case_length_mm"]);                                              // from phase 2
        Assert.Equal(-0.0508, m["case_shoulder_mm"]);
        // and a cartridge made with that case carries all of it
        long lot = CartridgeStore.CreateLot(_a, _sh, "L");
        CartridgeStore.AddCartridge(_a, lot, 1, a1.Id);
        var input = CartridgeStore.CartridgeInputs(_a, lot).Single();
        Assert.Equal("1A", input.Mark);
        Assert.Equal(30.50, input.Measures["case_capacity_gn"], 9);
        Assert.Equal(51.21, input.Measures["case_length_mm"]);
        // the same hole written the other way round still finds it
        Assert.Equal(a1.Id, CartridgeStore.FindOrAddPiece(_a, _sh, "case", "A1"));
    }

    [Fact]
    public void WithoutEngravedMarksTwoSessionsOfCasesStayApart()
    {
        CartridgeStore.ImportWeighing(_a, _sh, WeighingReader.Read(CasesPhase1));
        CartridgeStore.ImportWeighing(_a, _sh, WeighingReader.Read(CasesPhase2));
        var marks = CartridgeStore.Pieces(_a, _sh, "case").Select(p => p.Mark).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "B1·1A", "B1·2A", "B1·4A", "B2·1A", "B2·2A" }, marks);
    }

    [Fact]
    public void NeckTensionAndPrimersGoToTheirOwnPieces()
    {
        CartridgeStore.ImportWeighing(_a, _sh, WeighingReader.Read(Primers));
        CartridgeStore.ImportWeighing(_a, _sh, WeighingReader.Read(Tension));
        var primers = CartridgeStore.Pieces(_a, _sh, "primer");
        Assert.Equal(2, primers.Count);
        Assert.Equal(0.0254, CartridgeStore.Measures(_a, "piece", primers.Single(p => p.Mark == "I1·1A").Id)["primer_height_mm"]);
        var neck = CartridgeStore.Pieces(_a, _sh, "case").Single();
        Assert.Equal(0.0450, CartridgeStore.Measures(_a, "piece", neck.Id)["neck_tension_mm"], 12);
    }

    [Fact]
    public void AnImportIsOneStepAndLeavesTheArchiveUntouchedIfItFails()
    {
        var batch = WeighingReader.Read(OgiveTray);
        var bad = new WeighingBatch
        {
            PieceKind = "barrel", Program = "PesataOgive", Name = "x", Identity = "barrel|x|1", Warnings = Array.Empty<string>(),
            Pieces = batch.Pieces,                                                              // a kind of piece the archive does not know
        };
        Assert.Throws<SqliteException>(() => CartridgeStore.ImportWeighing(_a, _sh, bad));
        Assert.Equal(0L, _a.Count("pieces")); Assert.Equal(0L, _a.Count("measures"));
        Assert.Equal(0L, _a.Count("imports"));
        Assert.Null(_a.GetMeta("pesata.batch.barrel|x|1"));                                     // not even the code was spent
    }
}
