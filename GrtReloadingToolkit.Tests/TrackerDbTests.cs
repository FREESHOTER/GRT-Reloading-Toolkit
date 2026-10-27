using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class TrackerDbTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grt-tracker-tests-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private TrackerDb NewDb(string? name = null) => new(Path.Combine(_dir, (name ?? Guid.NewGuid().ToString("N")) + ".db"));

    private static TrackedLoad Load(string name = "139 LVN N550", string cal = "6.5 Creedmoor", string bullet = "Lapua Scenar-L 139", string powder = "N550", double charge = 41.5)
        => new() { Name = name, Caliber = cal, Bullet = bullet, Powder = powder, ChargeGr = charge };

    [Fact]
    public void AnOutingKeepsEveryFieldThroughASaveAndAReload()
    {
        using var db = NewDb();
        long load = db.SaveLoad(Load());
        var o = new Outing
        {
            LoadId = load, Date = "2026-03-01", TempC = 12.5, PressureHpa = 1008, HumidityPct = 61, ShotsMs = "800.1,801.4,799.8,802",
            GroupMoa = 0.41, DistanceM = 300, WindMps = 3.2, WindDeg = 90, Firearm = "Tikka T3x", PowderLot = "L-77", PrimerLot = "P-9",
            SeatingMm = 5.02, BrassFirings = 3, BarrelRounds = 412, Notes = "sereno",
        };
        db.SaveOuting(o);

        var back = Assert.Single(db.OutingsOf(load));
        Assert.Equal(o.Id, back.Id);
        Assert.Equal(new[] { 800.1, 801.4, 799.8, 802 }, back.Velocities());
        Assert.Equal((12.5, 1008.0, 61.0), (back.TempC!.Value, back.PressureHpa!.Value, back.HumidityPct!.Value));
        Assert.Equal((3.2, 90.0), (back.WindMps!.Value, back.WindDeg!.Value));
        Assert.Equal(("Tikka T3x", "L-77", "P-9"), (back.Firearm, back.PowderLot, back.PrimerLot));
        Assert.Equal((5.02, 3, 412), (back.SeatingMm!.Value, back.BrassFirings!.Value, back.BarrelRounds!.Value));
        Assert.Equal("sereno", back.Notes);
    }

    [Fact]
    public void OptionalFieldsComeBackEmptyNotZero()
    {
        using var db = NewDb();
        long load = db.SaveLoad(Load());
        db.SaveOuting(new Outing { LoadId = load, Date = "2026-03-01", ShotsMs = "800,801,802" });
        var back = Assert.Single(db.OutingsOf(load));
        Assert.Null(back.TempC); Assert.Null(back.WindMps); Assert.Null(back.GroupMoa); Assert.Null(back.SeatingMm); Assert.Null(back.BrassFirings);
    }

    [Fact]
    public void AnEditedOutingIsUpdatedNotDuplicated()
    {
        using var db = NewDb();
        long load = db.SaveLoad(Load());
        var o = new Outing { LoadId = load, Date = "2026-03-01", ShotsMs = "800,801,802" };
        db.SaveOuting(o);
        o.ShotsMs = "810,811,812"; o.Notes = "corretto";
        db.SaveOuting(o);
        var back = Assert.Single(db.OutingsOf(load));
        Assert.Equal(new[] { 810.0, 811, 812 }, back.Velocities());
    }

    [Fact]
    public void OutingsComeBackOldestFirstAndOnlyOfTheirOwnLoad()
    {
        using var db = NewDb();
        long a = db.SaveLoad(Load()), b = db.SaveLoad(Load("168 SMK", ".308 Winchester", "SMK 168", "H4350", 43.0));
        db.SaveOuting(new Outing { LoadId = a, Date = "2026-05-01" });
        db.SaveOuting(new Outing { LoadId = a, Date = "2026-03-01" });
        db.SaveOuting(new Outing { LoadId = b, Date = "2026-04-01" });
        Assert.Equal(new[] { "2026-03-01", "2026-05-01" }, db.OutingsOf(a).Select(x => x.Date));
        Assert.Single(db.OutingsOf(b));
    }

    [Fact]
    public void DeletingALoadTakesItsOutingsWithIt()
    {
        using var db = NewDb();
        long a = db.SaveLoad(Load()), b = db.SaveLoad(Load("altro", ".223 Remington", "", "", 24));
        db.SaveOuting(new Outing { LoadId = a, Date = "2026-03-01" });
        db.SaveOuting(new Outing { LoadId = b, Date = "2026-03-02" });
        db.DeleteLoad(a);
        Assert.Empty(db.OutingsOf(a));
        Assert.Single(db.OutingsOf(b));
        Assert.Single(db.Loads());
    }

    [Fact]
    public void DataSurvivesClosingAndReopeningTheFile()
    {
        string name = Guid.NewGuid().ToString("N");
        using (var db = NewDb(name)) { long l = db.SaveLoad(Load()); db.SaveOuting(new Outing { LoadId = l, Date = "2026-03-01", ShotsMs = "800,801,802" }); }
        using var again = NewDb(name);
        var load = Assert.Single(again.Loads());
        Assert.Single(again.OutingsOf(load.Id));
    }

    [Fact]
    public void LoadsAreListedByNameAndArchivedOnesOnlyOnRequest()
    {
        using var db = NewDb();
        db.SaveLoad(Load("b load")); db.SaveLoad(Load("A load")); var old = Load("vecchio"); old.Archived = true; db.SaveLoad(old);
        Assert.Equal(new[] { "A load", "b load" }, db.Loads().Select(l => l.Name));
        Assert.Equal(3, db.Loads(includeArchived: true).Count);
    }

    // ---- recognising the load that is open in GRT

    [Theory]
    [InlineData("6.5 Creedmoor", "Scenar-L 139gr", "N550", 41.5, true)]
    [InlineData("6,5 creedmoor", "Lapua Scenar-L 139 grain", "Vihtavuori N550", 41.54, true)]   // wording, case, decimal comma, 0.04 gr
    [InlineData("6.5 Creedmoor", "Scenar-L 139gr", "N550", 41.6, false)]                         // another charge
    [InlineData(".308 Winchester", "Scenar-L 139gr", "N550", 41.5, false)]                       // another caliber
    [InlineData("6.5 Creedmoor", "SMK 140", "N550", 41.5, false)]                                // another bullet
    [InlineData("6.5 Creedmoor", "Scenar-L 139gr", "H4350", 41.5, false)]                        // another powder
    public void TheOpenLoadIsRecognisedByCaliberChargeBulletAndPowder(string caliber, string bullet, string powder, double charge, bool expected)
        => Assert.Equal(expected, TrackerDb.Matches(Load(), caliber, bullet, powder, charge));

    [Fact]
    public void ALoadThatNamesNoBulletOrPowderMatchesAnyOfThem()
        => Assert.True(TrackerDb.Matches(Load(bullet: "", powder: ""), "6.5 Creedmoor", "whatever", "anything", 41.5));

    [Fact]
    public void FindReturnsTheLoadOnlyWhenExactlyOneFits()
    {
        using var db = NewDb();
        db.SaveLoad(Load());
        Assert.Equal("139 LVN N550", db.Find("6.5 Creedmoor", "Scenar-L 139gr", "N550", 41.5)?.Name);
        Assert.Null(db.Find("6.5 Creedmoor", "Scenar-L 139gr", "N550", 43.0));
        db.SaveLoad(Load("generico", bullet: "", powder: ""));                       // names nothing: fits too
        Assert.Null(db.Find("6.5 Creedmoor", "Scenar-L 139gr", "N550", 41.5));      // two fit: the caller asks
    }

    // ---- shots

    [Theory]
    [InlineData("800 801.5 802", new[] { 800.0, 801.5, 802 })]
    [InlineData("800,801,802", new[] { 800.0, 801, 802 })]                          // commas between shots
    [InlineData("800,5 801,25", new[] { 800.5, 801.25 })]                           // commas as decimals
    [InlineData("800.1;801.4\n799.8", new[] { 800.1, 801.4, 799.8 })]
    [InlineData("800, x, -3, 0, 801.5,,", new[] { 800.0, 801.5 })]
    public void ShotsAreReadWhateverSeparatesThem(string text, double[] expected) => Assert.Equal(expected, Outing.ParseShots(text));

    [Theory]
    [InlineData(800.0, true)]
    [InlineData(2650.0 * 0.3048, true)]
    [InlineData(5.0, false)]
    [InlineData(9000.0, false)]
    public void OnlyAVelocityOfAShotIsPlausible(double mps, bool expected) => Assert.Equal(expected, Outing.IsPlausibleShotMps(mps));

    [Fact]
    public void FormattedShotsReadBackTheSame()
        => Assert.Equal(new[] { 800.1, 799.85 }, Outing.ParseShots(Outing.FormatShots(new[] { 800.1, 799.85 })));

    // ---- the outings become sessions

    [Fact]
    public void OutingsBecomeSessionsAndAnUnreadableDateIsSkipped()
    {
        var sessions = FollowSessions.From(new[]
        {
            new Outing { Date = "2026-03-01", TempC = 14, ShotsMs = "800,801,802", PowderLot = " L-77 ", PrimerLot = "", SeatingMm = 5.02, GroupMoa = 0.4, DistanceM = 300 },
            new Outing { Date = "non è una data", ShotsMs = "800,801,802" },
        });
        var s = Assert.Single(sessions);
        Assert.Equal(new DateTime(2026, 3, 1), s.Date);
        Assert.Equal(14, s.TempC);
        Assert.Equal(3, s.VelocitiesMps.Count);
        Assert.Equal("L-77", s.PowderLot);
        Assert.Null(s.PrimerLot);                        // nothing written: nothing to compare
        Assert.Equal(5.02, s.SeatingMm);
    }
}
