using GrtReloadingToolkit.Log;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The store's one hard invariant: qty_current is always qty_initial plus the sum of that
/// component's ledger rows. Every test here is a way that used to be false.
/// </summary>
public sealed class DbStockTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grt-db-tests-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private Db NewDb() => new(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db"));

    private static long AddPrimers(Db db, double qty) => db.UpsertComponent(new Component
    {
        Kind = ComponentKind.Primer,
        Brand = "Test",
        Name = "SRP",
        Unit = "pcs",
        QtyInitial = qty,
        QtyCurrent = qty,
    });

    /// <summary>Reads the ledger directly — Db exposes no accessor, and the point is to check it.</summary>
    private static double LedgerSum(Db db, long componentId)
    {
        using var cn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.Path }.ToString());
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(delta), 0) FROM ledger WHERE component_id=$id";
        cmd.Parameters.AddWithValue("$id", componentId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static void AssertLedgerAgrees(Db db, long componentId)
    {
        Component c = db.Component(componentId)!;
        Assert.Equal(c.QtyInitial + LedgerSum(db, componentId), c.QtyCurrent, 6);
    }

    [Fact]
    public void DeletingAnOversubscribedEntryGivesBackOnlyWhatItTook()
    {
        using Db db = NewDb();
        long primers = AddPrimers(db, 150);

        long entry = db.SaveEntry(new JournalEntry { PrimerId = primers, Rounds = 200 }, applyStock: true);
        Assert.Equal(-50, db.Component(primers)!.QtyCurrent);

        db.DeleteEntry(entry);

        // the clamped version restored the full 200 against a deduction of only 150, inventing 50 primers
        Assert.Equal(150, db.Component(primers)!.QtyCurrent);
        AssertLedgerAgrees(db, primers);
    }

    [Fact]
    public void EditingAnOversubscribedEntryDownReconcilesExactly()
    {
        using Db db = NewDb();
        long primers = AddPrimers(db, 150);

        var e = new JournalEntry { PrimerId = primers, Rounds = 200 };
        e.Id = db.SaveEntry(e, applyStock: true);

        e.Rounds = 20;
        db.SaveEntry(e, applyStock: true);

        Assert.Equal(130, db.Component(primers)!.QtyCurrent);
        AssertLedgerAgrees(db, primers);
    }

    [Fact]
    public void ManualCorrectionBelowZeroStaysOnTheBooks()
    {
        using Db db = NewDb();
        long primers = AddPrimers(db, 100);

        db.AdjustStock(primers, -250, "manual restock");

        Assert.Equal(-150, db.Component(primers)!.QtyCurrent);
        AssertLedgerAgrees(db, primers);
    }

    [Fact]
    public void PowderIsDeductedInGramsAndReturnedInFull()
    {
        using Db db = NewDb();
        long powder = db.UpsertComponent(new Component
        {
            Kind = ComponentKind.Powder, Brand = "VV", Name = "N550",
            Unit = "g", QtyInitial = 500, QtyCurrent = 500,
        });

        long entry = db.SaveEntry(new JournalEntry { PowderId = powder, ChargeGr = 41.5, Rounds = 20 }, applyStock: true);
        Assert.Equal(500 - 41.5 * 0.06479891 * 20, db.Component(powder)!.QtyCurrent, 6);

        db.DeleteEntry(entry);
        Assert.Equal(500, db.Component(powder)!.QtyCurrent, 6);
        AssertLedgerAgrees(db, powder);
    }

    [Fact]
    public void NormalDeductionIsUnchanged()
    {
        using Db db = NewDb();
        long primers = AddPrimers(db, 1000);

        db.SaveEntry(new JournalEntry { PrimerId = primers, Rounds = 50 }, applyStock: true);

        Assert.Equal(950, db.Component(primers)!.QtyCurrent);
        AssertLedgerAgrees(db, primers);
    }

    [Fact]
    public void EntrySavedWithoutStockLeavesInventoryAlone()
    {
        using Db db = NewDb();
        long primers = AddPrimers(db, 1000);

        long entry = db.SaveEntry(new JournalEntry { PrimerId = primers, Rounds = 50 }, applyStock: false);
        db.DeleteEntry(entry);

        Assert.Equal(1000, db.Component(primers)!.QtyCurrent);
        Assert.Equal(0, LedgerSum(db, primers));
    }

    [Fact]
    public void DatabasePathWithNoDirectoryPartOpens()
    {
        // RELOADING_LOG_DB=reloading.db — Path.GetDirectoryName gives "", and CreateDirectory("") throws.
        string bare = "grt-bare-" + Guid.NewGuid().ToString("N") + ".db";
        try
        {
            using (Db db = new(bare)) Assert.Empty(db.Components());
            Assert.True(File.Exists(bare));
        }
        finally
        {
            // the pooled SQLite connection keeps a Windows file lock past Dispose()
            SqliteConnection.ClearAllPools();
            foreach (string f in Directory.EnumerateFiles(".", bare + "*"))
                try { File.Delete(f); } catch (IOException) { }
        }
    }
    [Fact]
    public void StockGoesNegativeAndStaysThere()
    {
        // Not a corruption: a component added with no opening count, then loaded against, owes
        // the ledger. The edit dialog used to throw on exactly this, because NumericUpDown
        // defaults to a Minimum of 0 -- so the number the store round-trips is the number the
        // dialog has to be able to show, and zeroing it here would erase a real debt.
        using var db = NewDb();
        long id = AddPrimers(db, 0);
        db.AdjustStock(id, -117, "loaded 117 rounds");

        var c = db.Components().Single(x => x.Id == id);
        Assert.Equal(-117, c.QtyCurrent, 9);
        Assert.Equal(-117, LedgerSum(db, id), 9);
        Assert.Equal(0, c.FractionRemaining);        // guarded on QtyInitial, so no divide by zero
        Assert.Equal(0, c.CostPerUnit);
    }

    [Fact]
    public void ABarrelIsALotLikeAnyOther()
    {
        // Barrels went in as a kind so a shooter can record what a tube cost and which one it is;
        // the journal does not consume them, so the only thing to prove is that the new enum
        // member survives the round trip through a TEXT column.
        using var db = NewDb();
        long id = db.UpsertComponent(new Component
        {
            Kind = ComponentKind.Barrel, Brand = "Bartlein", Name = "7mm 1:8 5R", Lot = "B-4471",
            Unit = "pcs", QtyInitial = 1, QtyCurrent = 1, CostTotal = 450, Currency = "USD",
        });

        var b = db.Components().Single(x => x.Id == id);
        Assert.Equal(ComponentKind.Barrel, b.Kind);
        Assert.Equal("Bartlein 7mm 1:8 5R [B-4471]", b.Display);
        Assert.Equal("1 pcs", b.QtyLeftText);
        Assert.Equal(450, b.CostPerUnitIn, 9);
    }

    [Fact]
    public void PowderCountedInPoundsStillLosesGramsToTheLedger()
    {
        // The unit is a label on the lot, not a change of store: logging 20 rounds of 41.5 gr has
        // to move the same grams whether the jug is counted in pounds or in grams.
        using var db = NewDb();
        long jug = db.UpsertComponent(new Component
        {
            Kind = ComponentKind.Powder, Brand = "Hodgdon", Name = "H4831SC", Unit = "lb",
            QtyInitial = 8 * 453.59237, QtyCurrent = 8 * 453.59237,
        });
        db.SaveEntry(new JournalEntry { PowderId = jug, ChargeGr = 41.5, Rounds = 20 }, applyStock: true);

        var c = db.Components().Single(x => x.Id == jug);
        double burnedG = 41.5 * 0.06479891 * 20;
        Assert.Equal(8 * 453.59237 - burnedG, c.QtyCurrent, 9);
        Assert.Equal(8 - burnedG / 453.59237, c.QtyCurrentIn, 9);
        AssertLedgerAgrees(db, jug);
    }
}
