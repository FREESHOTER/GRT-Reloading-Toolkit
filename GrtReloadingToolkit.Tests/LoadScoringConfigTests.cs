using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public class LoadScoringConfigTests
{
    [Fact]
    public void ScoreReturnsFirstTierTheValueDoesNotExceed()
    {
        var tiers = new List<ScoreTier> { new(2.0, 10.0), new(4.0, 5.0), new(double.PositiveInfinity, 1.0) };
        Assert.Equal(10.0, LoadScoringConfig.Score(tiers, 1.0));
        Assert.Equal(10.0, LoadScoringConfig.Score(tiers, 2.0)); // exactly on a boundary counts as that tier
        Assert.Equal(5.0, LoadScoringConfig.Score(tiers, 3.0));
        Assert.Equal(1.0, LoadScoringConfig.Score(tiers, 100.0));
    }

    [Fact]
    public void ScoreFallsBackToLastTierIfNoneHasInfinity()
    {
        // A user could delete the last row entirely -- the table must still produce a number.
        var tiers = new List<ScoreTier> { new(2.0, 10.0), new(4.0, 5.0) };
        Assert.Equal(5.0, LoadScoringConfig.Score(tiers, 999.0));
    }

    [Fact]
    public void ScoreOnAnEmptyTierListDoesNotThrow() =>
        Assert.Equal(0.5, LoadScoringConfig.Score(new List<ScoreTier>(), 5.0));

    [Fact]
    public void DefaultTiersMatchThePublishedAnchors()
    {
        var d = LoadScoringConfig.Default;
        Assert.Equal(10.0, LoadScoringConfig.Score(d.SdTiers, 3.05));   // 10 fps handloader goal
        Assert.Equal(4.5, LoadScoringConfig.Score(d.EsTiers, 18.3));    // Mk 316 ES ceiling, 60 fps
        Assert.Equal(10.0, LoadScoringConfig.Score(d.GroupMoaTiers, 0.225)); // winning benchrest group
    }

    [Fact]
    public void SaveThenLoadRoundTripsAnEditedTier()
    {
        // Isolate this test from any real load-scoring.json on the machine running it, and clean
        // up afterwards -- this writes to the same %AppData% path Load()/Save() always use.
        string path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GRTPlugins", "load-scoring.json");
        string? backup = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            var edited = LoadScoringConfig.Default;
            edited.SdTiers[0] = new ScoreTier(2.5, 10.0); // a stricter "10/10" than the default 3.05
            edited.Save();

            var reloaded = LoadScoringConfig.Load();
            Assert.Equal(2.5, reloaded.SdTiers[0].Threshold);
            // Untouched curves still round-trip correctly alongside the edited one.
            Assert.Equal(edited.EsTiers.Count, reloaded.EsTiers.Count);
        }
        finally
        {
            if (backup != null) File.WriteAllText(path, backup);
            else if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadToleratesAMissingFile()
    {
        string path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GRTPlugins", "load-scoring.json");
        string? backup = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var loaded = LoadScoringConfig.Load();
            Assert.Equal(LoadScoringConfig.Default.SdTiers.Count, loaded.SdTiers.Count);
        }
        finally
        {
            if (backup != null) File.WriteAllText(path, backup);
        }
    }

    [Fact]
    public void LoadToleratesACorruptFile()
    {
        string path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GRTPlugins", "load-scoring.json");
        string? backup = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ not valid json ][");
            var loaded = LoadScoringConfig.Load();
            Assert.Equal(LoadScoringConfig.Default.SdTiers.Count, loaded.SdTiers.Count);
        }
        finally
        {
            if (backup != null) File.WriteAllText(path, backup);
            else if (File.Exists(path)) File.Delete(path);
        }
    }
}
