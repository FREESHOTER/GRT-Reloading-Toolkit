using GrtReloadingToolkit.Cartridges;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The plan of a race with any number of targets, any number of shots on each (the "barilotti") and any number of returns (rientri).</summary>
public sealed class PlanReturnsTests
{
    /// <summary>n cartridges whose estimated velocities are spread unevenly, so that blocks of them have different ranges.</summary>
    private static List<CartridgeEstimate> Lot(int n) => Enumerable.Range(0, n)
        .Select(i => new CartridgeEstimate
        {
            Cartridge = new CartridgeInput(i + 1, "m" + (i + 1), new Dictionary<string, double>()),
            Estimate = Math.Round(Math.Pow((i * 7 % n) + 1, 1.4) / 6 - 3, 3),
        }).ToList();

    private static double Range(PlanGroup g) => g.To - g.From;

    [Fact]
    public void WithoutReturnsThePlanIsTheOneOfBefore()
    {
        var a = CartridgePlanner.Compose(Lot(40), PlanMode.Race, 3, 10, 5, residualSd: 1.2);
        var b = CartridgePlanner.Compose(Lot(40), PlanMode.Race, 3, 10, 5, residualSd: 1.2, returns: 0, shotsPerReturn: 7);   // the size of a return that is not shot does not matter
        Assert.Equal(new[] { "target 1", "target 2", "target 3", "proving shots" }, a.Groups.Select(g => g.Name));
        Assert.Equal(a.Groups.Select(g => g.Name), b.Groups.Select(g => g.Name));
        Assert.Equal(a.Groups.Select(g => string.Join(",", g.Cartridges.Select(c => c.Number))), b.Groups.Select(g => string.Join(",", g.Cartridges.Select(c => c.Number))));
    }

    [Fact]
    public void ReturnsAreGroupsOfTheirOwnWithTheirOwnSize()
    {
        var lot = Lot(40);
        var plan = CartridgePlanner.Compose(lot, PlanMode.Race, targets: 2, shotsPerTarget: 8, proving: 4, residualSd: 1.0, returns: 2, shotsPerReturn: 6);
        Assert.Null(plan.Error);
        Assert.Equal(new[] { "target 1", "target 2", "return 1", "return 2", "proving shots" }, plan.Groups.Select(g => g.Name));
        Assert.Equal(new[] { 8, 8, 6, 6, 4 }, plan.Groups.Select(g => g.Cartridges.Count));
        Assert.Equal(new[] { "1", "2", "r1", "r2", "P" }, plan.Groups.Select(g => g.Cartridges[0].Assigned));
        var used = plan.Groups.SelectMany(g => g.Cartridges).Select(c => c.Number).ToList();
        Assert.Equal(32, used.Count);
        Assert.Equal(32, used.Distinct().Count());                                         // nobody is shot twice
        Assert.Equal(8, lot.Count(c => c.Assigned == "R"));                                 // the rest stays in reserve
    }

    [Fact]
    public void AmongBlocksOfTheSameSizeTheMostUniformGoesFirst()
    {
        var plan = CartridgePlanner.Compose(Lot(60), PlanMode.Race, targets: 3, shotsPerTarget: 10, proving: 2, residualSd: 1.0, returns: 2, shotsPerReturn: 6);
        var targets = plan.Groups.Where(g => g.Name.StartsWith("target")).Select(Range).ToList();
        var returns = plan.Groups.Where(g => g.Name.StartsWith("return")).Select(Range).ToList();
        Assert.Equal(targets.OrderBy(x => x), targets);
        Assert.Equal(returns.OrderBy(x => x), returns);
        // when targets and returns are the same size they are ranked together: the returns get the blocks that are left
        var same = CartridgePlanner.Compose(Lot(60), PlanMode.Race, targets: 3, shotsPerTarget: 8, proving: 2, residualSd: 1.0, returns: 2, shotsPerReturn: 8);
        var all = same.Groups.Where(g => g.Name != "proving shots").Select(Range).ToList();
        Assert.Equal(5, all.Count);
        Assert.Equal(all.OrderBy(x => x), all);
    }

    [Fact]
    public void TheProvingShotsStillComeFromBothEndsOfTheSpeedRange()
    {
        var lot = Lot(40);
        var plan = CartridgePlanner.Compose(lot, PlanMode.Race, targets: 2, shotsPerTarget: 8, proving: 4, residualSd: 1.0, returns: 2, shotsPerReturn: 6);
        var proving = plan.Groups.Single(g => g.Name == "proving shots").Cartridges;
        var shot = plan.Groups.SelectMany(g => g.Cartridges).ToList();
        double lowest = shot.Min(c => c.Estimate), highest = shot.Max(c => c.Estimate);
        Assert.Contains(proving, c => c.Estimate == lowest);
        Assert.Contains(proving, c => c.Estimate == highest);
    }

    [Fact]
    public void TooFewCartridgesAreSaidWithTheWholeShapeOfTheRace()
    {
        string old = CartridgePlanner.Compose(Lot(20), PlanMode.Race, 3, 10, 5).Error!;
        Assert.Contains("35 cartridges are needed (3 targets of 10 plus 5 proving): the lot has 20 usable.", old);
        string withReturns = CartridgePlanner.Compose(Lot(20), PlanMode.Race, 2, 10, 5, returns: 2, shotsPerReturn: 10).Error!;
        Assert.Contains("45 cartridges are needed (2 targets of 10, 2 returns of 10 and 5 proving): the lot has 20 usable.", withReturns);
    }

    [Fact]
    public void ATrainingPlanIgnoresTheReturns()
    {
        var a = CartridgePlanner.Compose(Lot(40), PlanMode.Training, perSeries: 5, residualSd: 1.0);
        var b = CartridgePlanner.Compose(Lot(40), PlanMode.Training, perSeries: 5, residualSd: 1.0, returns: 3, shotsPerReturn: 9);
        Assert.Equal(a.Groups.Select(g => g.Name), b.Groups.Select(g => g.Name));
    }
}
