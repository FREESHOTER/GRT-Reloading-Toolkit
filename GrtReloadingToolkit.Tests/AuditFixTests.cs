using System.Globalization;
using GrtPluginKit.Grt;
using GrtReloadingToolkit.Cal;
using GrtReloadingToolkit.Cards;
using GrtReloadingToolkit.ChronoStats;
using GrtReloadingToolkit.Log;
using GrtReloadingToolkit.Ocw;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// Each test pins one defect found by running every tool against a real multi-charge ladder (the
/// N550 data) with GRT open. They are written from the user-visible symptom, not from the fix.
/// </summary>
public sealed class AuditFixTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grt-auditfix-tests-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private static string G(double gr) => (gr * 0.06479891).ToString("0.########", CultureInfo.InvariantCulture);

    private static GrtCharge Charge(string name, params double[] velocities)
    {
        var c = new GrtCharge { Name = name };
        foreach (double v in velocities) c.Shots.Add(new GrtShot(v, null));
        return c;
    }

    /// <summary>A load whose recipe charge is <paramref name="recipeGr"/> and which carries a chrono ladder.</summary>
    private string WriteLadderLoad(string name, double recipeGr, params (double gr, double[] v)[] ladder)
    {
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n<GordonsReloadingTool>\n  <InnerBallistikInput>\n"
                   + "    <caliber><input name=\"CaliberName\" value=\"6.5 Creedmoor\" /></caliber>\n"
                   + "    <gun><input name=\"GunName\" value=\"Tikka\" /></gun>\n"
                   + "    <projectile><input name=\"ProjectileName\" value=\"Lapua Scenar\" /></projectile>\n"
                   + $"    <propellant><input name=\"pname\" value=\"N550\" /><input name=\"mc\" value=\"{G(recipeGr)}\" /></propellant>\n"
                   + "    <appendix>\n    </appendix>\n  </InnerBallistikInput>\n</GordonsReloadingTool>\n";
        string path = Path.Combine(_dir, name + ".grtload");
        File.WriteAllText(path, xml);
        if (ladder.Length > 0)
        {
            var doc = GrtLoadDoc.Load(path);
            doc.AddMeasurement("Misurazione", ladder.Select(l => Charge($"{l.gr:0.0} gr", l.v)).ToArray());
            doc.Save(path);
        }
        return path;
    }

    // ── F2: the user's own newer save must not be shadowed by an older toolkit sibling ───────────

    [Fact]
    public void ASiblingNewerThanTheUsersFileStillWins()
    {
        string p = WriteLadderLoad("load", 40.2);
        string sib = Path.Combine(_dir, "load_toolkit_20261002_100000.grtload");
        File.Copy(p, sib);
        File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(sib, DateTime.UtcNow.AddHours(-1));

        Assert.Equal(sib, GrtLoadDoc.FreshestFamilyFile(p), ignoreCase: true);
        Assert.Equal(sib, GrtLoadDoc.EffectiveReadPath(p), ignoreCase: true);
    }

    [Fact]
    public void TheUsersOwnSaveAfterTheLastSiblingWins()
    {
        // The reported case: the toolkit wrote a sibling at 24.70 gr, the user then saved their own
        // file at 24.50 gr -- every tool kept reading the stale sibling.
        string p = WriteLadderLoad("load", 24.5);
        string sib = Path.Combine(_dir, "load_toolkit_20261001_100000.grtload");
        File.WriteAllText(sib, File.ReadAllText(p).Replace(G(24.5), G(24.7)));
        File.SetLastWriteTimeUtc(sib, DateTime.UtcNow.AddHours(-5));
        File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddHours(-1));

        Assert.Equal(p, GrtLoadDoc.FreshestFamilyFile(sib), ignoreCase: true);   // also when GRT's active tab IS the sibling
        Assert.Equal(24.5, GrtLoadDoc.OpenForToolkitEdit(p).PropellantChargeGr!.Value, 3);
    }

    [Fact]
    public void AFileAnotherPluginGeneratedIsReadAsItIsNotReplacedByItsOriginal()
    {
        // ShotMarker Import writes "<load>_shotmarker_<stamp>.grtload" with the imported shot groups; its stem
        // strips back to the original, which has none of them. With no toolkit snapshot, read the open file.
        string original = WriteLadderLoad("session", 40.2);
        string imported = Path.Combine(_dir, "session_shotmarker_20261005_1030.grtload");
        File.Copy(original, imported);

        Assert.Equal(imported, GrtLoadDoc.FreshestFamilyFile(imported), ignoreCase: true);
        Assert.Equal(imported, GrtLoadDoc.EffectiveReadPath(imported), ignoreCase: true);
        Assert.Equal(imported, GrtLoadDoc.OpenForToolkitEdit(imported).SourcePath, ignoreCase: true);
    }

    [Fact]
    public void WithNoSiblingTheActiveFileIsUsed()
    {
        string p = WriteLadderLoad("alone", 40.0);
        Assert.Equal(p, GrtLoadDoc.FreshestFamilyFile(p), ignoreCase: true);
    }

    [Fact]
    public void TheUserIsToldWhenTheirNewerFileLacksTheSnapshotsMeasurements()
    {
        string p = WriteLadderLoad("notice", 40.2);                                   // the user's own file: no chrono data
        string sib = Path.Combine(_dir, "notice_toolkit_20261002_100000.grtload");
        File.Copy(WriteLadderLoad("tmp", 40.2, (40.2, new[] { 830.0, 831.0 })), sib);   // the snapshot holds a string
        File.SetLastWriteTimeUtc(sib, DateTime.UtcNow.AddHours(-3));
        File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddHours(-1));                       // user saved later

        var seen = new List<(string, int)>();
        void H(string s, int n) => seen.Add((s, n));
        GrtLoadDoc.SnapshotHasMeasurementsMissingFromNewerFile += H;
        try
        {
            GrtLoadDoc.OpenForToolkitEdit(p);
            GrtLoadDoc.OpenForToolkitEdit(p);   // second tool, same situation: told once
        }
        finally { GrtLoadDoc.SnapshotHasMeasurementsMissingFromNewerFile -= H; }

        Assert.Single(seen);
        Assert.Equal(1, seen[0].Item2);
        Assert.Equal(sib, seen[0].Item1, ignoreCase: true);
    }

    [Fact]
    public void NoNoticeWhenTheSnapshotIsTheNewestOrHoldsNothingExtra()
    {
        string p = WriteLadderLoad("quiet", 40.2);
        string sib = Path.Combine(_dir, "quiet_toolkit_20261002_100000.grtload");
        File.Copy(WriteLadderLoad("tmp2", 40.2, (40.2, new[] { 830.0 })), sib);
        File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddHours(-3));
        File.SetLastWriteTimeUtc(sib, DateTime.UtcNow.AddHours(-1));                     // snapshot newer: it wins, nothing lost

        int raised = 0;
        void H(string s, int n) => raised++;
        GrtLoadDoc.SnapshotHasMeasurementsMissingFromNewerFile += H;
        try { GrtLoadDoc.OpenForToolkitEdit(p); }
        finally { GrtLoadDoc.SnapshotHasMeasurementsMissingFromNewerFile -= H; }

        Assert.Equal(0, raised);
    }

    // ── F1/F3: a card or book entry must not take the ladder's last charge for the recipe ────────

    [Fact]
    public void TheCardTakesVelocityOnlyFromTheChargeThatIsTheRecipe()
    {
        string p = WriteLadderLoad("ladder", 40.2,
            (40.0, new[] { 828.0, 829.0 }), (40.2, new[] { 830.0, 831.0, 830.0 }), (41.0, new[] { 849.0, 850.0 }));

        var card = LoadCard.FromGrtload(p);

        Assert.Equal(40.2, card.ChargeGr!.Value, 2);
        Assert.Equal(830.3, card.MvMs!.Value, 1);          // 40.2's own mean, not 41.0's 849.5
    }

    [Fact]
    public void ARecipeThatWasNeverChronographedGetsNoVelocityNotAnotherChargesVelocity()
    {
        string p = WriteLadderLoad("stale", 48.8, (40.9, new[] { 783.0, 784.0 }));

        var card = LoadCard.FromGrtload(p);
        var snap = LoadSnapshot.FromGrtload(p);

        Assert.Equal(48.8, card.ChargeGr!.Value, 2);
        Assert.Null(card.MvMs);
        Assert.Equal(48.8, snap.ChargeGr!.Value, 2);
        Assert.Null(snap.VelocityAvgMs);
        Assert.Equal(0, snap.Shots);
    }

    [Fact]
    public void WithoutARecipeChargeASingleChronographedChargeIsStillUsed()
    {
        string p = WriteLadderLoad("one", 40.0, (40.0, new[] { 800.0, 802.0 }));
        File.WriteAllText(p, File.ReadAllText(p).Replace($"<input name=\"mc\" value=\"{G(40.0)}\" />", ""));

        Assert.Equal(801.0, GrtLoadDoc.Load(p).ChargeMatchingRecipe()!.Shots.Average(s => s.VelocityMps), 1);
    }

    [Fact]
    public void TheLoadNameDefaultDropsTheToolkitTimestamp()
    {
        string p = WriteLadderLoad("Nosler 77_toolkit_20261002_170922", 24.5, (24.5, new[] { 800.0 }));
        Assert.Equal("Nosler 77", LoadSnapshot.FromGrtload(p).LoadName);
        Assert.All(LoadSnapshot.AllFromGrtload(p), s => Assert.Equal("Nosler 77", s.LoadName));
    }

    // ── F4/F7: ladder steps ──────────────────────────────────────────────────────────────────────

    private static TargetGroup Group(string name, double? charge, params (double x, double y)[] hits)
    {
        var g = new TargetGroup { SourceFile = "GRT:" + name, DistanceM = 100, ChargeGrains = charge };
        foreach (var (x, y) in hits) g.Impacts.Add(new Impact(x, y));
        return g;
    }

    [Fact]
    public void GroupsWithNoChargeAreSkippedNeverNumberedAsGrains()
    {
        var rep = LadderLoader.FromGroups(new[]
        {
            Group("Gruppo 1", null, (0, 0), (1, 1)),
            Group("Gruppo 2", null, (0, 0), (1, 1)),
            Group("Gruppo 3", 40.2, (0, 0), (1, 1)),
        }, LadderMode.Charge);

        Assert.Single(rep.Rows);
        Assert.Equal(40.2, rep.Rows[0].X, 3);
        Assert.Contains(rep.Log, l => l.Contains("Gruppo 1") && l.Contains("skipped"));
    }

    [Fact]
    public void OrdinalStepsAreOnlyProposedWhenTheCountsLineUp()
    {
        var groups = new List<TargetGroup> { Group("Gruppo 1", null, (0, 0)), Group("Gruppo 2", null, (0, 0)) };

        var pairs = LadderLoader.AssignOrdinalSteps(groups, new[] { 40.4, 40.0 })!;
        Assert.Equal(new[] { 40.0, 40.4 }, pairs.Select(p => p.Step));            // ascending, first group = lowest charge
        Assert.All(groups, g => Assert.Null(g.ChargeGrains));                       // nothing applied by the proposal

        Assert.Null(LadderLoader.AssignOrdinalSteps(groups, new[] { 40.0, 40.2, 40.4 }));   // 3 charges, 2 groups: no guess
        Assert.Null(LadderLoader.AssignOrdinalSteps(groups, Array.Empty<double>()));

        LadderLoader.ApplySteps(pairs);
        Assert.Equal(new double?[] { 40.0, 40.4 }, groups.Select(g => g.ChargeGrains));
    }

    [Fact]
    public void AnAlreadyNamedGroupIsNeverReassigned()
    {
        var groups = new List<TargetGroup> { Group("40.0", 40.0, (0, 0)), Group("Gruppo 2", null, (0, 0)) };
        var pairs = LadderLoader.AssignOrdinalSteps(groups, new[] { 40.0, 40.2 })!;
        Assert.Single(pairs);
        Assert.Equal(40.2, pairs[0].Step);
    }

    [Fact]
    public void TwoGroupsAtTheSameChargeArePooledNotOverwritten()
    {
        var rep = LadderLoader.FromGroups(new[]
        {
            Group("40.2 a", 40.2, (0, 0), (1, 0), (0, 1)),
            Group("40.2 b", 40.2, (2, 2), (3, 2)),
        }, LadderMode.Charge);

        Assert.Single(rep.Rows);
        Assert.Equal(5, rep.Rows[0].Impacts);
    }

    [Fact]
    public void TwoStringsAtTheSameChargeAreBothCounted()
    {
        var rep = LadderLoader.FromGroups(Array.Empty<TargetGroup>(), LadderMode.Charge);
        rep.Rows.Add(new LadderStep { X = 40.2 });
        LadderLoader.FillMissingVelocities(rep, new[]
        {
            new LadderVelocity(40.2, new[] { 830.0, 831.0 }),
            new LadderVelocity(40.2, new[] { 832.0, 833.0, 834.0 }),
        });
        Assert.Equal(5, rep.Rows[0].VelN);
    }

    // ── F13: chrono statistics count every shot unless asked otherwise ───────────────────────────

    [Fact]
    public void TheStatisticsKeepEveryShotByDefaultAndStillFlagTheSuspect()
    {
        // 4 shots, the first 12.9 m/s above three that agree: the real 40.8 gr string.
        double[] v = { 852.4, 839.5, 839.4, 839.6 };

        var raw = ChronoStatsCalc.Analyze(v);
        var cut = ChronoStatsCalc.Analyze(v, excludeOutliers: true);

        Assert.Equal(4, raw.N);
        Assert.True(raw.Es > 12);
        Assert.False(raw.OutliersExcluded);
        Assert.True(raw.HasOutliers);                 // still reported, just not removed
        Assert.Equal(3, cut.N);
        Assert.True(cut.OutliersExcluded);
        Assert.True(cut.Es < 0.5);
    }

    [Fact]
    public void TheNoteSaysWhetherAFlaggedShotWasCounted()
    {
        double[] v = { 852.4, 839.5, 839.4, 839.6 };
        string kept = ChronoStatsCalc.BuildTableReport(new[] { ("40.8 gr", ChronoStatsCalc.Analyze(v)) }, 3, 0.95);
        string cut = ChronoStatsCalc.BuildTableReport(new[] { ("40.8 gr", ChronoStatsCalc.Analyze(v, excludeOutliers: true)) }, 3, 0.95);

        Assert.Contains("kept, counted above", kept);
        Assert.Contains("EXCLUDED", cut);
        Assert.Contains("population SD", kept);
    }

    // ── F15: unlinked loads must not merge ───────────────────────────────────────────────────────

    private static JournalEntry E(string load, long? powder = null, long? bullet = null, double charge = 40.0) => new()
    {
        LoadName = load, Caliber = "6.5 Creedmoor", PowderId = powder, BulletId = bullet, ChargeGr = charge,
        Rounds = 5, SdMs = 4, EsMs = 10,
    };

    [Fact]
    public void TwoDifferentUnlinkedLoadsAtTheSameChargeStaySeparateRows()
    {
        var journal = new[] { E("Load A"), E("Load B") };
        var rows = LeaderboardRanking.Rank(journal, new Dictionary<long, string>(), null, "— none —");
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Powder.Contains("Load A"));
    }

    [Fact]
    public void TheSameUnlinkedLoadLoggedFromDifferentSiblingsIsOneRow()
    {
        var journal = new[] { E("Load A"), E("Load A_toolkit_20261002_170922") };
        Assert.Single(LeaderboardRanking.Rank(journal, new Dictionary<long, string>(), null, "— none —"));
    }

    [Fact]
    public void LinkedComponentsStillGroupByIdWhateverTheLoadIsCalled()
    {
        var journal = new[] { E("Load A", 1, 2), E("Load B", 1, 2) };
        Assert.Single(LeaderboardRanking.Rank(journal, new Dictionary<long, string> { [1] = "N550", [2] = "Scenar" }, null, "— none —"));
    }

    [Fact]
    public void PowderCompareDoesNotAverageUnrelatedUnlinkedLoadsIntoOnePowder()
    {
        var journal = new[] { E("Load A"), E("Load B") };
        Assert.Equal(2, PowderCompare.Compare(journal, new Dictionary<long, string>(), null, "— none —").Count);
    }

    [Fact]
    public void WorkflowKeepsUnlinkedLoadsApart()
    {
        var a = E("Load A"); a.DistanceM = 300;
        var b = E("Load B"); b.DistanceM = 300;
        Assert.Equal(2, WorkflowRanking.ComputeGroups(new[] { a, b }, "6.5 Creedmoor", 300).Count);
    }

    // ── F21: a person typing a quantity ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData("12,5", 12.5)]
    [InlineData("453,6", 453.6)]
    [InlineData("1.234,5", 1234.5)]
    [InlineData("1,234.5", 1234.5)]
    [InlineData("-20", -20)]
    [InlineData(" 7 ", 7)]
    public void QuantitiesAreAcceptedInBothDecimalStyles(string text, double expected)
    {
        Assert.True(StockUnit.TryParseQuantity(text, out double v));
        Assert.Equal(expected, v, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12,5,5")]
    [InlineData("NaN")]
    public void NonNumbersAreRejectedRatherThanIgnoredSilently(string text) =>
        Assert.False(StockUnit.TryParseQuantity(text, out _));
}
