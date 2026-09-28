using GrtPluginKit.Grt;
using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// <see cref="LoadSnapshot.AllFromGrtload"/> exists because <see cref="LoadSnapshot.FromGrtload"/>
/// alone always resolves a load with several chronographed charges (any ladder) to the exact same
/// one -- the last measurement's last charge -- with no way for the Journal's "Log from GRT" button
/// to offer any other. This is the user-reported bug: importing from GRT always imported "the same"
/// velocity, regardless of which charge was actually meant. These tests pin the fix: every
/// chronographed charge across every measurement comes back as its own candidate.
/// </summary>
public sealed class LoadSnapshotTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grt-loadsnapshot-tests-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private static GrtCharge Charge(string name, params double[] velocities)
    {
        var c = new GrtCharge { Name = name };
        foreach (double v in velocities) c.Shots.Add(new GrtShot(v, null));
        return c;
    }

    private string WriteLoad(string fileName, Action<GrtLoadDoc> build)
    {
        var doc = GrtLoadDoc.CreateMinimal("Test", fileName + ".grtload");
        build(doc);
        string path = Path.Combine(_dir, fileName + ".grtload");
        doc.Save(path);
        return path;
    }

    [Fact]
    public void ALadderWithSeveralChargesReturnsOneCandidatePerCharge()
    {
        string path = WriteLoad("ladder", doc => doc.AddMeasurement("Session 1", new[]
        {
            Charge("40.0 gr @ 2.850", 800, 802, 799),
            Charge("40.3 gr @ 2.850", 810, 812, 811),
            Charge("40.6 gr @ 2.850", 820, 822, 819),
        }));

        var candidates = LoadSnapshot.AllFromGrtload(path);

        Assert.Equal(3, candidates.Count);
        Assert.Equal(new[] { 40.0, 40.3, 40.6 }, candidates.Select(c => c.ChargeGr!.Value));
        Assert.All(candidates, c => Assert.NotNull(c.ChargeLabel));
        Assert.All(candidates, c => Assert.Equal(3, c.Shots));
    }

    [Fact]
    public void EachCandidateCarriesItsOwnVelocityStatsNotTheLastChargesStats()
    {
        // Regression case for the reported bug: before AllFromGrtload existed, every entry logged
        // from this load -- no matter which charge the user meant -- got 40.6's own 820.3 mean,
        // because FromGrtload always picked the last charge with shots.
        string path = WriteLoad("ladder2", doc => doc.AddMeasurement("Session 1", new[]
        {
            Charge("40.0 gr", 800, 801),
            Charge("40.6 gr", 820, 821),
        }));

        var candidates = LoadSnapshot.AllFromGrtload(path);

        var low = candidates.Single(c => c.ChargeGr == 40.0);
        var high = candidates.Single(c => c.ChargeGr == 40.6);
        Assert.Equal(800.5, low.VelocityAvgMs!.Value, 3);
        Assert.Equal(820.5, high.VelocityAvgMs!.Value, 3);
        Assert.NotEqual(low.VelocityAvgMs, high.VelocityAvgMs);
    }

    [Fact]
    public void ChargesAcrossSeveralMeasurementsAreAllListed()
    {
        string path = WriteLoad("twosessions", doc =>
        {
            doc.AddMeasurement("Day 1", new[] { Charge("40.0 gr", 800, 801) });
            doc.AddMeasurement("Day 2", new[] { Charge("40.3 gr", 810, 811) });
        });

        var candidates = LoadSnapshot.AllFromGrtload(path);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.ChargeLabel!.Contains("Day 1"));
        Assert.Contains(candidates, c => c.ChargeLabel!.Contains("Day 2"));
    }

    [Fact]
    public void AChargeWithNoShotsIsNotACandidate()
    {
        string path = WriteLoad("mixed", doc => doc.AddMeasurement("Session 1", new[]
        {
            Charge("40.0 gr", 800, 801),
            Charge("40.3 gr"), // never chronographed
        }));

        var candidates = LoadSnapshot.AllFromGrtload(path);

        Assert.Single(candidates);
        Assert.Equal(40.0, candidates[0].ChargeGr);
    }

    [Fact]
    public void ALoadWithNoChronographedChargeAtAllStillReturnsOneFallbackCandidate()
    {
        string path = WriteLoad("recipe-only", _ => { });

        var candidates = LoadSnapshot.AllFromGrtload(path);

        var only = Assert.Single(candidates);
        Assert.Null(only.ChargeLabel);
        Assert.Null(only.VelocityAvgMs);
        Assert.Equal(0, only.Shots);
    }

    [Fact]
    public void ASingleCandidateMatchesWhatFromGrtloadAlreadyReturned()
    {
        string path = WriteLoad("single", doc => doc.AddMeasurement("Session 1", new[]
        {
            Charge("41.0 gr", 830, 831, 829),
        }));

        var all = LoadSnapshot.AllFromGrtload(path);
        var single = Assert.Single(all);
        var legacy = LoadSnapshot.FromGrtload(path);

        Assert.Equal(legacy.ChargeGr, single.ChargeGr);
        Assert.Equal(legacy.VelocityAvgMs, single.VelocityAvgMs);
        Assert.Equal(legacy.Shots, single.Shots);
    }
}
