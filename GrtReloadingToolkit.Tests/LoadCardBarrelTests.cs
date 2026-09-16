using GrtPluginKit.Grt;
using GrtReloadingToolkit.Cards;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The barrel on a printed card. A recipe is only reproducible if you know which tube shot it,
/// so the card names the barrel and the two numbers that identify it — and says nothing at all
/// when there is no barrel to name, because the row is drawn only when it has a value.
/// </summary>
public sealed class LoadCardBarrelTests
{
    // Whatever GRT is set to on the machine running this, the card follows it; the assertions
    // compare against the same ambient units rather than hard-coding one side.
    private static readonly GrtUnits U = GrtUnits.Current;

    [Fact]
    public void TheBarrelLineIsTheNameThenTheTwoNumbersThatIdentifyIt()
    {
        var c = new LoadCard { Barrel = "Bartlein 5R", BarrelTwistMm = 203.2, BarrelLengthMm = 660.4 };
        Assert.Equal($"Bartlein 5R  {U.Twist(203.2)}  {U.Length(660.4)}", c.BarrelLine);
    }

    [Fact]
    public void ABarrelNobodyHasMeasuredIsStillWorthNaming()
    {
        // Twist and length are optional on the inventory, so a barrel entered as a name alone
        // must not print "1:0" or drag an empty unit onto the card.
        var c = new LoadCard { Barrel = "take-off factory tube" };
        Assert.Equal("take-off factory tube", c.BarrelLine);

        var zeroed = new LoadCard { Barrel = "take-off", BarrelTwistMm = 0, BarrelLengthMm = 0 };
        Assert.Equal("take-off", zeroed.BarrelLine);
    }

    [Fact]
    public void NoBarrelMeansNoRowAndNoQrEntry()
    {
        var c = new LoadCard { Caliber = "6.5 Creedmoor", Powder = "H4350" };
        Assert.Equal("", c.BarrelLine);
        Assert.DoesNotContain("barrel=", c.QrText());
    }

    [Fact]
    public void TheQrBlockCarriesTheBarrelToo()
    {
        // The QR is what a phone reads off the ammo box a year later, so it holds everything the
        // printed face holds.
        var c = new LoadCard { Barrel = "Proof CF", BarrelTwistMm = 203.2, BarrelLengthMm = 660.4 };
        Assert.Contains("barrel=Proof CF  " + U.Twist(203.2) + "  " + U.Length(660.4), c.QrText());
    }
}
