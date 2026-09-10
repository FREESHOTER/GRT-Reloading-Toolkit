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
            foreach (string f in Directory.EnumerateFiles(".", bare + "*")) File.Delete(f);
        }
    }
}
