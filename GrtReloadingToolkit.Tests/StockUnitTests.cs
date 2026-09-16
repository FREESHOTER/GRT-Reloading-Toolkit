using GrtPluginKit.Grt;
using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public class StockUnitTests
{
    [Theory]
    [InlineData("g", 1)]
    [InlineData("pcs", 1)]
    [InlineData("gr", 0.06479891)]        // a grain is exactly 64.79891 mg
    [InlineData("lb", 453.59237)]         // and a pound exactly 453.59237 g
    [InlineData("kg", 1000)]
    public void AUnitIsWorthTheseManyStoredUnits(string unit, double grams)
        => Assert.Equal(grams, StockUnit.Factor(unit), 9);

    [Fact]
    public void TheGrainFactorIsTheOneTheLedgerDeductsWith()
    {
        // Db.ApplyStock turns a charge in grains into grams to subtract it, and Costing prices
        // powder per gram. If this drifted, a lot counted in grains would read back wrong the
        // moment a round was logged against it.
        Assert.Equal(1 / GrtUnits.GrainsPerGram, StockUnit.Factor("gr"), 12);
        Assert.Equal(0.06479891, 1 / GrtUnits.GrainsPerGram, 12);
    }

    [Theory]
    [InlineData("g")]
    [InlineData("gr")]
    [InlineData("lb")]
    [InlineData("kg")]
    [InlineData("pcs")]
    public void WhatTheUserTypesComesBackUnchanged(string unit)
    {
        double stored = StockUnit.ToStore(37.5, unit);
        Assert.Equal(37.5, StockUnit.FromStore(stored, unit), 9);
    }

    [Fact]
    public void AnUnknownUnitIsLeftAlone()
    {
        // Rows written before this existed carry "g" or "pcs", but a blank or a hand-typed unit
        // must read as itself rather than be scaled by a guess.
        Assert.Equal(1, StockUnit.Factor(null), 9);
        Assert.Equal(1, StockUnit.Factor(""), 9);
        Assert.Equal(250, StockUnit.FromStore(250, "boxes"), 9);
    }

    [Fact]
    public void PowderIsTheOnlyKindWithAChoice()
    {
        Assert.Equal(new[] { "g", "gr", "lb", "kg" }, StockUnit.For(ComponentKind.Powder));
        foreach (var k in new[] { ComponentKind.Primer, ComponentKind.Brass, ComponentKind.Bullet, ComponentKind.Barrel })
        {
            Assert.Equal(new[] { "pcs" }, StockUnit.For(k));
            Assert.Equal("pcs", StockUnit.DefaultFor(k));
        }
        Assert.Equal("g", StockUnit.DefaultFor(ComponentKind.Powder));
    }

    [Fact]
    public void APoundKeepsEnoughPlacesToShowARoundBeingLoaded()
    {
        // ~41 gr out of a 1 lb jug is 0.0059 lb; at one place a whole loading session reads as
        // no change at all.
        Assert.Equal(3, StockUnit.Decimals("lb"));
        Assert.Equal(3, StockUnit.Decimals("kg"));
        Assert.Equal(1, StockUnit.Decimals("g"));
        Assert.Equal(1, StockUnit.Decimals("pcs"));
        Assert.Equal("0.###", StockUnit.Format("lb"));
    }

    [Fact]
    public void AnEightPoundJugReadsInPoundsAndIsPricedInPounds()
    {
        var jug = new Component
        {
            Kind = ComponentKind.Powder, Brand = "Hodgdon", Name = "H4831SC", Unit = "lb",
            QtyInitial = 8 * 453.59237, QtyCurrent = 7 * 453.59237, CostTotal = 360,
        };

        Assert.Equal(8, jug.QtyInitialIn, 9);
        Assert.Equal(7, jug.QtyCurrentIn, 9);
        Assert.Equal("7 lb", jug.QtyLeftText);
        Assert.Equal(45, jug.CostPerUnitIn, 6);                 // 360 for 8 lb
        Assert.Equal(360.0 / (8 * 453.59237), jug.CostPerUnit, 12); // still per gram underneath
    }

    [Fact]
    public void ADebtStaysADebtInWhicheverUnitItIsCounted()
    {
        // The negative that used to take the edit dialog down, shown in pounds.
        var owed = new Component { Kind = ComponentKind.Powder, Unit = "lb", QtyCurrent = -433.5085958329548 };
        Assert.Equal(-0.9557, owed.QtyCurrentIn, 4);
        Assert.StartsWith("-0.956", owed.QtyLeftText);
    }

    [Fact]
    public void PiecesAreCountedAsThemselves()
    {
        var primers = new Component { Kind = ComponentKind.Primer, Unit = "pcs", QtyInitial = 1000, QtyCurrent = 883, CostTotal = 90 };
        Assert.Equal(883, primers.QtyCurrentIn, 9);
        Assert.Equal("883 pcs", primers.QtyLeftText);
        Assert.Equal(primers.CostPerUnit, primers.CostPerUnitIn, 12);
    }

    [Fact]
    public void EveryKindOffersBrandsAndNoneTwice()
    {
        foreach (var k in Enum.GetValues<ComponentKind>())
        {
            var list = Brands.For(k);
            Assert.NotEmpty(list);
            Assert.Equal(list.Count, list.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(list, b => Assert.False(string.IsNullOrWhiteSpace(b)));
        }
        Assert.Contains("Bartlein", Brands.For(ComponentKind.Barrel));
        Assert.Contains("Hodgdon", Brands.For(ComponentKind.Powder));
    }
}
