using GrtPluginKit.Analysis;
using GrtReloadingToolkit.Athlon;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The note written next to each imported charge. The chronograph's own SD is the population SD (÷n); the one GRT shows on the
/// measurement tab is the sample SD (÷n−1). Both go in, each labelled, so the note never passes one off as the other.
/// </summary>
public sealed class AthlonChargeNoteSdTests
{
    // the 4-shot string checked against a real GRT: population SD 2.5, sample SD 2.8
    private static readonly double[] Shots = { 810.3, 803.5, 805.8, 806.9 };

    private static AthlonString Make(double? deviceSd)
    {
        var s = new AthlonString { SourceFile = "string1.xlsx", SourceUnit = "mps", DeviceSd = deviceSd };
        foreach (double v in Shots) s.Shots.Add(new ShotMeta { VelocityMps = v });
        return s;
    }

    [Fact]
    public void ADeviceSdIsWrittenBesideTheSampleSd()
    {
        var s = Make(2.5);
        string note = ImportBuilder.BuildChargeNote(s, StringStats.From(Shots), null);

        Assert.Contains("SD-DEVICE=2.5", note);
        Assert.Contains("SD-SAMPLE=2.8", note);
        Assert.DoesNotContain(" SD=", " " + note);
    }

    [Fact]
    public void WithoutADeviceSdOnlyTheSampleSdIsWritten()
    {
        string note = ImportBuilder.BuildChargeNote(Make(null), StringStats.From(Shots), null);

        Assert.Contains("SD-SAMPLE=2.8", note);
        Assert.DoesNotContain("SD-DEVICE", note);
    }
}
