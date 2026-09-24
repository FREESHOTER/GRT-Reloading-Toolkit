using System.Globalization;
using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// Idea #5 from the original next-plugin brainstorm: walk a folder of .grtload, emit one document
/// grouped by caliber. <see cref="LoadBook.DiscoverFamilyRepresentatives"/> covers the folder-scan/
/// sibling-collapsing half; <see cref="LoadBook.BuildEntry"/> covers the Journal-vs-file-only merge.
/// </summary>
public sealed class LoadBookTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grt-loadbook-tests-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private Db NewDb() => new(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db"));

    // grams -> grains, the same conversion GrtLoadDoc.ParseInputG applies in reverse when reading "mc"/"mp".
    private static string G(double grains) => (grains * 0.06479891).ToString("0.#######", CultureInfo.InvariantCulture);

    /// <summary>A minimal .grtload, same shape as BaBackfillTests' own fixture writer, extended with
    /// the fields Load Book actually reads: gun, projectile, charge and OAL.</summary>
    private string WriteLoad(string fileName, string caliber, string gun, string powder, string bullet,
        double chargeGr, double? coalMm = null, double? ba = null)
    {
        string oal = coalMm is { } c ? $"      <input name=\"oal\" value=\"{c.ToString(CultureInfo.InvariantCulture)}\" />\n" : "";
        string baTag = ba is { } b ? $"      <input name=\"Ba\" value=\"{b.ToString(CultureInfo.InvariantCulture)}\" />\n" : "";
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n"
                   + "<GordonsReloadingTool>\n  <InnerBallistikInput>\n"
                   + $"    <caliber>\n      <input name=\"CaliberName\" value=\"{caliber}\" />\n{oal}    </caliber>\n"
                   + $"    <gun>\n      <input name=\"GunName\" value=\"{gun}\" />\n    </gun>\n"
                   + $"    <projectile>\n      <input name=\"ProjectileName\" value=\"{bullet}\" />\n    </projectile>\n"
                   + $"    <propellant>\n      <input name=\"pname\" value=\"{powder}\" />\n"
                   + $"      <input name=\"mc\" value=\"{G(chargeGr)}\" />\n{baTag}    </propellant>\n"
                   + "    <appendix>\n    </appendix>\n  </InnerBallistikInput>\n</GordonsReloadingTool>\n";
        string path = Path.Combine(_dir, fileName + ".grtload");
        File.WriteAllText(path, xml);
        return path;
    }

    // ── DiscoverFamilyRepresentatives ───────────────────────────────────

    [Fact]
    public void DiscoveryFindsEveryDistinctRecipeAcrossSubfolders()
    {
        WriteLoad("recipe-a", "6.5 Creedmoor", "Tikka", "N140", "Berger 140", 41.0);
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllText(Path.Combine(_dir, "sub", "recipe-b.grtload"),
            File.ReadAllText(Path.Combine(_dir, "recipe-a.grtload")).Replace("6.5 Creedmoor", ".308 Win"));

        var found = LoadBook.DiscoverFamilyRepresentatives(_dir);
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public void GeneratedSiblingsFromGrtItselfAreExcludedOutright()
    {
        WriteLoad("recipe", "6.5 Creedmoor", "Tikka", "N140", "Berger 140", 41.0);
        // GRT's own auto-save naming (not this toolkit's "_toolkit_" tag) -- LooksLikeGeneratedSibling's job.
        File.WriteAllText(Path.Combine(_dir, "recipe_calibration_20260101_1200.grtload"),
            File.ReadAllText(Path.Combine(_dir, "recipe.grtload")));

        var found = LoadBook.DiscoverFamilyRepresentatives(_dir);
        Assert.Single(found);
        Assert.EndsWith("recipe.grtload", found[0]);
    }

    [Fact]
    public void ThisToolkitsOwnWriteBackSiblingsCollapseToOneFamilyPickingTheNewest()
    {
        string pristine = WriteLoad("recipe", "6.5 Creedmoor", "Tikka", "N140", "Berger 140", 41.0);
        string older = Path.Combine(_dir, "recipe_toolkit_20260101_120000.grtload");
        string newer = Path.Combine(_dir, "recipe_toolkit_20260102_120000.grtload");
        File.WriteAllText(older, File.ReadAllText(pristine));
        File.WriteAllText(newer, File.ReadAllText(pristine));
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddDays(-1));

        var found = LoadBook.DiscoverFamilyRepresentatives(_dir);
        Assert.Single(found);
        Assert.Equal(newer, found[0], ignoreCase: true);
    }

    [Fact]
    public void ANonExistentFolderYieldsNoEntriesRatherThanThrowing()
    {
        Assert.Empty(LoadBook.DiscoverFamilyRepresentatives(Path.Combine(_dir, "does-not-exist")));
    }

    // ── BuildEntry ───────────────────────────────────────────────────────

    [Fact]
    public void AFileWithNoMatchingJournalEntryFallsBackToTheLoadsOwnData()
    {
        string path = WriteLoad("solo", "6BR", "Custom", "Varget", "Sierra 107", 30.5, coalMm: 57.2, ba: 0.44);
        var entry = LoadBook.BuildEntry(path, journal: Array.Empty<JournalEntry>(), componentsById: new Dictionary<long, Component>());

        Assert.False(entry.FromJournal);
        Assert.Equal("6BR", entry.Caliber);
        Assert.Equal("Custom", entry.Firearm);
        Assert.Equal("Varget", entry.PowderName);
        Assert.Equal("Sierra 107", entry.BulletName);
        Assert.Equal(30.5, entry.ChargeGr!.Value, 2);
        Assert.Equal(57.2, entry.CoalMm!.Value, 2);
        Assert.Equal(0.44, entry.Ba!.Value, 5);
        Assert.Null(entry.GroupMoa);
        Assert.Null(entry.Notes);
    }

    [Fact]
    public void AMatchingJournalEntryWinsOverTheFilesOwnDataAndAddsGroupAndNotes()
    {
        string path = WriteLoad("matched", "6.5 Creedmoor", "Tikka", "N140", "Berger 140", 41.0);
        var bullet = new Component { Id = 7, Kind = ComponentKind.Bullet, Brand = "Berger", Name = "140gr Hybrid" };
        var je = new JournalEntry
        {
            Id = 1, Date = "2026-09-20", Caliber = "6.5 Creedmoor", Firearm = "Tikka T3x",
            ChargeGr = 41.2, BulletId = 7, GroupMoa = 0.42, DistanceM = 100, Notes = "clean group",
            VelocityAvgMs = 850, SdMs = 4.2, EsMs = 9.0, Rounds = 5, GrtloadPath = path,
        };

        var entry = LoadBook.BuildEntry(path, new[] { je }, new Dictionary<long, Component> { [7] = bullet });

        Assert.True(entry.FromJournal);
        Assert.Equal("Tikka T3x", entry.Firearm);       // Journal's own value wins over the file's "Tikka"
        Assert.Equal(41.2, entry.ChargeGr!.Value, 2);    // Journal's logged charge wins over the file's 41.0
        Assert.Equal("Berger 140gr Hybrid", entry.BulletName);
        Assert.Equal(0.42, entry.GroupMoa!.Value, 2);
        Assert.Equal("clean group", entry.Notes);
        Assert.Equal("2026-09-20", entry.JournalDate);
    }

    [Fact]
    public void MultipleJournalEntriesForTheSameFilePickTheMostRecentlyLoggedOne()
    {
        string path = WriteLoad("re-logged", "308 Win", "AR-10", "Varget", "168gr", 44.0);
        var older = new JournalEntry { Id = 1, Caliber = "308 Win", GrtloadPath = path, Notes = "first pass", Date = "2026-09-01" };
        var newer = new JournalEntry { Id = 2, Caliber = "308 Win", GrtloadPath = path, Notes = "re-checked", Date = "2026-09-10" };

        var entry = LoadBook.BuildEntry(path, new[] { older, newer }, new Dictionary<long, Component>());
        Assert.Equal("re-checked", entry.Notes);
    }

    [Fact]
    public void AJournalEntryForADifferentFileIsIgnored()
    {
        string path = WriteLoad("mine", "6.5 Creedmoor", "Tikka", "N140", "Berger 140", 41.0);
        string other = WriteLoad("someone-elses", "6.5 Creedmoor", "Tikka", "N140", "Berger 140", 39.0);
        var je = new JournalEntry { Id = 1, Caliber = "6.5 Creedmoor", GrtloadPath = other, Notes = "not this one" };

        var entry = LoadBook.BuildEntry(path, new[] { je }, new Dictionary<long, Component>());
        Assert.False(entry.FromJournal);
        Assert.Equal(41.0, entry.ChargeGr!.Value, 1);
    }

    // ── GroupByCaliber ───────────────────────────────────────────────────

    [Fact]
    public void GroupingIsCaseInsensitiveAndAlphabeticallyOrdered()
    {
        var entries = new[]
        {
            MakeEntry(caliber: "6.5 Creedmoor"),
            MakeEntry(caliber: "6.5 CREEDMOOR"),
            MakeEntry(caliber: ".308 Win"),
        };
        var groups = LoadBook.GroupByCaliber(entries);

        Assert.Equal(2, groups.Count);
        Assert.Equal(".308 Win", groups[0].Key);
        Assert.Equal(2, groups[1].Count());
    }

    [Fact]
    public void AnEmptyCaliberGetsItsOwnLabelledGroupRatherThanBeingDropped()
    {
        var groups = LoadBook.GroupByCaliber(new[] { MakeEntry(caliber: "") });
        Assert.Equal("(unknown caliber)", Assert.Single(groups).Key);
    }

    private static LoadBook.Entry MakeEntry(string caliber) => new(
        EffectivePath: "x", LoadName: "x", Caliber: caliber, Firearm: "", PowderName: "",
        BulletName: null, ChargeGr: null, CoalMm: null, Ba: null, A0: null,
        VelocityAvgMs: null, SdMs: null, EsMs: null, Shots: 0,
        GroupMoa: null, DistanceM: null, JournalDate: null, Notes: null, FromJournal: false);
}
