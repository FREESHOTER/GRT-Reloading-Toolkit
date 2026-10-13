using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The "review the load" alerts read from the journal: each fires on the fact it is meant for and stays quiet otherwise.</summary>
public sealed class LoadAlertsTests
{
    private static string V(double mps) => mps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m/s";

    private static JournalEntry E(long id, double charge = 40.2, long? powder = 1, long? primer = 10, long? brass = 20, double? cbto = 2.50, double? coal = null, double? mv = null, string load = "Hornady 75 vv140 40.20")
        => new() { Id = id, LoadName = load, Caliber = ".223 Rem", BulletId = 5, ChargeGr = charge, PowderId = powder, PrimerId = primer, BrassId = brass, CbtoMm = cbto, CoalMm = coal, VelocityAvgMs = mv };

    private static readonly IReadOnlyDictionary<long, Component> Comps = new Dictionary<long, Component>
    {
        [1] = new() { Id = 1, Kind = ComponentKind.Powder, Brand = "Vihtavuori", Name = "N140", Lot = "A1" },
        [2] = new() { Id = 2, Kind = ComponentKind.Powder, Brand = "Vihtavuori", Name = "N140", Lot = "B2" },
        [10] = new() { Id = 10, Kind = ComponentKind.Primer, Brand = "CCI", Name = "BR2", Lot = "P1" },
        [11] = new() { Id = 11, Kind = ComponentKind.Primer, Brand = "CCI", Name = "BR2", Lot = "P2" },
        [20] = new() { Id = 20, Kind = ComponentKind.Brass, Brand = "Lapua", Name = ".223", Lot = "x" },
        [21] = new() { Id = 21, Kind = ComponentKind.Brass, Brand = "Norma", Name = ".223", Lot = "y" },
    };

    private static IReadOnlyList<LoadAlert> Alerts(IEnumerable<JournalEntry> j, params (Firearm, int)[] firearms)
        => LoadAlerts.All(new AlertContext(j.ToList(), Comps, firearms, V));

    // ---- A1
    [Fact]
    public void ANewPowderLotSincetheLastSessionIsFlagged()
    {
        var a = Assert.Single(Alerts(new[] { E(1, powder: 1), E(2, powder: 2) }), x => x.Kind == AlertKind.NewPowder);
        Assert.Equal(2, a.ReturnToPhase);
        Assert.Contains("lot A1", (string)a.Args[1]);
        Assert.Contains("lot B2", (string)a.Args[2]);
    }

    [Fact]
    public void TheSamePowderGivesNothing()
        => Assert.Empty(Alerts(new[] { E(1, powder: 1), E(2, powder: 1) }));

    [Fact]
    public void ANewPrimerLotIsFlaggedToo()
        => Assert.Single(Alerts(new[] { E(1, primer: 10), E(2, primer: 11) }), x => x.Kind == AlertKind.NewPrimer);

    [Fact]
    public void OnlyTheLastTwoSessionsOfTheSameLoadAreCompared()
        => Assert.Empty(Alerts(new[] { E(1, powder: 1), E(2, powder: 2), E(3, powder: 2) }));

    [Fact]
    public void DifferentLoadsAreNeverCompared()
        => Assert.Empty(Alerts(new[] { E(1, powder: 1, load: "load A"), E(2, powder: 2, load: "load B") }));

    // ---- A4
    [Fact]
    public void ASeatingChangeOfAtLeast005MmIsFlaggedAndSendsBackToPhase3()
    {
        var a = Assert.Single(Alerts(new[] { E(1, cbto: 2.50), E(2, cbto: 2.60) }), x => x.Kind == AlertKind.SeatingChanged);
        Assert.Equal(3, a.ReturnToPhase);
        Assert.Empty(Alerts(new[] { E(1, cbto: 2.50), E(2, cbto: 2.52) }));
    }

    [Fact]
    public void CoalIsUsedOnlyWhenBothSessionsHaveItAndNeverAgainstCbto()
    {
        Assert.Single(Alerts(new[] { E(1, cbto: null, coal: 57.00), E(2, cbto: null, coal: 57.20) }), x => x.Kind == AlertKind.SeatingChanged);
        Assert.Empty(Alerts(new[] { E(1, cbto: 2.50, coal: null), E(2, cbto: null, coal: 57.20) }));
    }

    [Fact]
    public void DifferentBrassIsFlagged()
        => Assert.Single(Alerts(new[] { E(1, brass: 20), E(2, brass: 21) }), x => x.Kind == AlertKind.BrassChanged);

    // ---- A2
    [Fact]
    public void AVelocityDriftAcrossSessionsIsFlagged()
    {
        var a = Assert.Single(Alerts(new[] { E(1, mv: 800), E(2, mv: 806), E(3, mv: 812), E(4, mv: 818) }), x => x.Kind == AlertKind.VelocityDrift);
        Assert.Equal(4, a.Args[1]);
    }

    [Fact]
    public void AStableVelocityOrTooFewSessionsGiveNoDrift()
    {
        Assert.DoesNotContain(Alerts(new[] { E(1, mv: 800), E(2, mv: 800.4), E(3, mv: 800.1), E(4, mv: 800.2) }), x => x.Kind == AlertKind.VelocityDrift);
        Assert.DoesNotContain(Alerts(new[] { E(1, mv: 800), E(2, mv: 830) }), x => x.Kind == AlertKind.VelocityDrift);
    }

    // ---- A3
    [Fact]
    public void ABarrelPastOrCloseToTheLimitTheShooterSetIsFlagged()
    {
        var fa = new Firearm { Id = 1, Name = "Tikka", Caliber = ".223 Rem", RoundsLimit = 1000 };
        Assert.Contains("past the limit", Assert.Single(Alerts(Array.Empty<JournalEntry>(), (fa, 1000))).Template);
        Assert.Contains("close to the limit", Assert.Single(Alerts(Array.Empty<JournalEntry>(), (fa, 920))).Template);
        Assert.Empty(Alerts(Array.Empty<JournalEntry>(), (fa, 500)));
    }

    [Fact]
    public void WithoutALimitTheToolkitInventsNone()
    {
        Assert.Empty(Alerts(Array.Empty<JournalEntry>(), (new Firearm { Id = 1, Name = "Tikka", RoundsLimit = null }, 50000)));
        Assert.Empty(Alerts(Array.Empty<JournalEntry>(), (new Firearm { Id = 1, Name = "Tikka", RoundsLimit = 0 }, 50000)));
    }

    // ---- the load's own
    [Fact]
    public void ForLoadKeepsTheAlertsOfThatLoadAndTheBarrelOnesOfItsCaliber()
    {
        var fa = new Firearm { Id = 1, Name = "Tikka", Caliber = ".223 Rem", RoundsLimit = 100 };
        var all = Alerts(new[] { E(1, powder: 1), E(2, powder: 2), E(3, powder: 1, load: "other"), E(4, powder: 2, load: "other") }, (fa, 120));
        Assert.Equal(3, all.Count);                                       // two powder alerts and the barrel
        var mine = LoadAlerts.ForLoad(all, ".223 rem", "Hornady 75 vv140 40.20");
        Assert.Equal(2, mine.Count);
        Assert.Contains(mine, x => x.Kind == AlertKind.NewPowder);
        Assert.Contains(mine, x => x.Kind == AlertKind.BarrelLimit);
        Assert.Empty(LoadAlerts.ForLoad(all, ".308 Win", "Hornady 75 vv140 40.20"));
    }
}