using GrtReloadingToolkit.Archive;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The component library window's side of the archive: list, own figures, matches.</summary>
public sealed class LibraryBrowseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "libreria2_test_" + Guid.NewGuid().ToString("N"));
    private readonly ArchiveDb _a;
    private readonly long _mine, _ep1, _ep2;

    public LibraryBrowseTests()
    {
        Directory.CreateDirectory(_dir);
        _a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        _mine = ComponentLibrary.Item(_a, ComponentLibrary.Bullet, "Lapua", "Scenar GB458", "6.71 mm", "palla|lapua|scenar gb458|139.0", "test");
        ComponentLibrary.Value(_a, _mine, "length_mm", 34.55, null, "mm", ComponentLibrary.GrtLoad, "a.grtload", "2026-01-01 00:00:00");
        ComponentLibrary.Value(_a, _mine, "weight_gr", 139, null, "gr", ComponentLibrary.GrtLoad, "a.grtload", "2026-01-01 00:00:00");
        _ep1 = ComponentLibrary.Item(_a, ComponentLibrary.Bullet, "Lapua", "Scenar", "6.71 mm", "palla|ep:BUL_1", "test");
        ComponentLibrary.Value(_a, _ep1, "length_mm", 34.56, null, "mm", ComponentLibrary.EpLibrary, "ep", "2026-01-01 00:00:00");
        ComponentLibrary.Value(_a, _ep1, "bc_g7", 0.29, null, "", ComponentLibrary.EpLibrary, "ep", "2026-01-01 00:00:00");
        _ep2 = ComponentLibrary.Item(_a, ComponentLibrary.Bullet, "Lapua", "Scenar-L", "6.71 mm", "palla|ep:BUL_2", "test");
        ComponentLibrary.Value(_a, _ep2, "length_mm", 34.9, null, "mm", ComponentLibrary.EpLibrary, "ep", "2026-01-01 00:00:00");
        foreach (var (c, sc) in new[] { (_ep1, 1.9), (_ep2, 1.5) })
            _a.Insert("lib_suggestions", ("item_id", _mine), ("candidate_id", c), ("score", sc), ("reason", "prova"));
    }

    public void Dispose()
    {
        _a.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void OnlyMineLeavesTheLibraryOnlyComponentsOut()
    {
        Assert.Equal(3, LibraryBrowse.Items(_a, ComponentLibrary.Bullet, "", onlyMine: false).Count);
        var mine = Assert.Single(LibraryBrowse.Items(_a, ComponentLibrary.Bullet, "", onlyMine: true));
        Assert.Equal(_mine, mine.Id);
        Assert.Single(LibraryBrowse.Items(_a, ComponentLibrary.Bullet, "scenar-l", onlyMine: false));
    }

    [Fact]
    public void AMeasurementIsUsedAtOnceAndNothingIsLost()
    {
        Assert.True(LibraryBrowse.AddFigure(_a, _mine, "length_mm", 34.62, "mm", ComponentLibrary.Measured, "calibro, 10 palle"));
        var inUse = LibraryBrowse.Values(_a, _mine).Single(v => v.Field == "length_mm" && v.InUse);
        Assert.Equal((34.62, ComponentLibrary.Measured), (inUse.Value!.Value, inUse.Source));
        Assert.Equal(2, LibraryBrowse.Values(_a, _mine).Count(v => v.Field == "length_mm"));      // the GRT figure is still there
        Assert.False(LibraryBrowse.AddFigure(_a, _mine, "length_mm", 34.62, "mm", ComponentLibrary.Measured, ""));
        Assert.Throws<ArgumentException>(() => LibraryBrowse.AddFigure(_a, _mine, "length_mm", 1, "mm", ComponentLibrary.EpLibrary, ""));
        Assert.Equal("true", _a.GetMeta("master"));
    }

    [Fact]
    public void ConfirmingAMatchBringsTheLibraryFiguresBesideMineAndSettlesTheOthers()
    {
        var pending = LibraryBrowse.Pending(_a);
        Assert.Equal(new[] { _ep1, _ep2 }, pending.Select(p => p.CandidateId));      // best first
        LibraryBrowse.Confirm(_a, pending[0].Id);

        Assert.Empty(LibraryBrowse.Pending(_a));
        var vals = LibraryBrowse.Values(_a, _mine);
        Assert.Equal(0.29, vals.Single(v => v.Field == "bc_g7" && v.InUse).Value);     // new from the library
        Assert.Equal(34.55, vals.Single(v => v.Field == "length_mm" && v.InUse).Value); // mine still counts first
        Assert.Equal("Lapua Scenar", vals.Single(v => v.Field == "alias").Text);
        Assert.Equal(2L, _a.Count("lib_values", $"item_id={_ep1}"));                     // the library record is untouched
        Assert.Equal("scartato", _a.ScalarP("SELECT status FROM lib_suggestions WHERE candidate_id=$c", ("$c", _ep2)));
        Assert.Throws<InvalidOperationException>(() => LibraryBrowse.Confirm(_a, pending[0].Id));
    }

    [Fact]
    public void RejectingSetsOneProposalAside()
    {
        var pending = LibraryBrowse.Pending(_a);
        LibraryBrowse.Reject(_a, pending[0].Id);
        var left = Assert.Single(LibraryBrowse.Pending(_a));
        Assert.Equal(_ep2, left.CandidateId);
        Assert.DoesNotContain(LibraryBrowse.Values(_a, _mine), v => v.Field == "bc_g7");
    }
}
