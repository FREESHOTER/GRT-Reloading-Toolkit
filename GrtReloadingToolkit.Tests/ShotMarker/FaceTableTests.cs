using System;
using System.Linq;
using ShotMarker.Core.Faces;
using ShotMarker.Core.Faces.Tables;
using Xunit;

namespace ShotMarker.Core.Tests;

/// <summary>The two guards the face tables rely on. Both fire while a static field is being
/// initialised, so if either stopped working the symptom would not be an exception anyone
/// reads — it would be a face quietly missing, or drawn inside out.</summary>
public class FaceTableTests
{
    [Fact]
    public void ZonesOutOfOrderAreRejected()
    {
        // Concentric fills from the inside out and stops filling at the aiming mark, so a
        // table typed smallest-last would draw the black over the rings instead of under
        // them — a face that still loads and still renders, just wrong. The guard names the
        // face and the pair, because the ring tables are long.
        var ex = Assert.Throws<ArgumentException>(() => FaceTable.Concentric(
            "TEST", "Test", "Test",
            new[] { FaceTable.Z(100, "10"), FaceTable.Z(50, "9") },
            aimingMarkMm: 100, ringLineMm: 1));

        Assert.Contains("TEST", ex.Message);
        Assert.Contains("100", ex.Message);
        Assert.Contains("50", ex.Message);
    }

    [Fact]
    public void EveryFaceIdIsUniqueAcrossTheTables()
    {
        // TargetFaceLibrary.Load() throws on a duplicate rather than letting Dictionary
        // overwrite, because the loser of a collision would simply stop being drawn. Ten
        // files author faces; nothing but this stops two of them claiming one id.
        string[] ids = IcfraTables.Faces().Concat(DcraTables.Faces()).Concat(NraTables.Faces())
            .Concat(UkTables.Faces()).Concat(SabuTables.Faces()).Concat(IbsTables.Faces())
            .Concat(AuTables.Faces()).Concat(SwissTables.Faces()).Concat(NorwayTables.Faces())
            .Concat(MiscTables.Faces())
            .Select(f => f.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ids.Length, TargetFaceLibrary.Count);

        // And every one of them is reachable by the id a ShotMarker CSV writes.
        foreach (string id in ids) Assert.NotNull(TargetFaceLibrary.Find(id));
    }
}
