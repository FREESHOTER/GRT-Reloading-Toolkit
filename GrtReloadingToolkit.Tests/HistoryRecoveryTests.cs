using GrtPluginKit.Grt;
using GrtReloadingToolkit.Recovery;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class HistoryRecoveryTests
{
    private static void WriteLoad(string path, double[] velocities, string note)
    {
        var doc = GrtLoadDoc.CreateMinimal("prova", path);
        var c = new GrtCharge { Name = "41.5", ValueKg = 41.5 * 0.00006479891 };
        foreach (double v in velocities) c.Shots.Add(new GrtShot(v, null));
        doc.AddMeasurement("Cronografo", new[] { c });
        doc.AddNote("Nota di prova", note);
        doc.Save(path);
    }

    [Fact]
    public void ReadsEveryLoadAndCountsAToolkitCopyAsADuplicate()
    {
        string root = Path.Combine(Path.GetTempPath(), "recupero_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Mario", "6.5CM"));
            Directory.CreateDirectory(Path.Combine(root, "doku"));
            string original = Path.Combine(root, "Mario", "6.5CM", "carico.grtload");
            WriteLoad(original, new[] { 820.1, 822.4, 819.8 }, "testo");
            File.SetLastWriteTime(original, new DateTime(2026, 1, 1));
            // the toolkit's snapshot repeats the same string and note, and is newer
            WriteLoad(Path.Combine(root, "Mario", "6.5CM", "carico_toolkit_20260102_1200.grtload"), new[] { 820.1, 822.4, 819.8 }, "testo");
            // a different string elsewhere, and a manual page that must be skipped
            WriteLoad(Path.Combine(root, "Mario", "altro.grtload"), new[] { 800.0, 801.0 }, "altro");
            WriteLoad(Path.Combine(root, "doku", "esempio.grtload"), new[] { 700.0 }, "manuale");
            var before = Directory.GetFiles(root, "*.grtload", SearchOption.AllDirectories)
                .ToDictionary(f => f, f => (File.GetLastWriteTimeUtc(f), File.ReadAllBytes(f).Length));

            var r = HistoryRecovery.Run(root);

            Assert.Equal(3, r.Loads.Count);                                  // doku skipped
            Assert.All(r.Loads, l => Assert.Equal("Mario", l.Shooter));
            Assert.Equal(3, r.Strings.Count);
            Assert.Equal(2, r.Strings.Count(s => s.DuplicateOf == null));
            var dup = Assert.Single(r.Strings, s => s.DuplicateOf != null);
            Assert.Contains("_toolkit_", dup.RelPath);
            Assert.Equal(Path.Combine("Mario", "6.5CM", "carico.grtload"), dup.DuplicateOf);
            Assert.Equal(new[] { 820.1, 822.4, 819.8 }, r.Strings.First(s => s.DuplicateOf == null && s.RelPath.EndsWith("carico.grtload")).VelocitiesMps);
            Assert.Equal(2, r.Notes.Count(n => n.DuplicateOf == null));
            Assert.Equal("copia del Toolkit", r.Loads.Single(l => l.RelPath.Contains("_toolkit_")).Family);

            // read only: nothing under the root changed
            foreach (var (f, (t, len)) in before)
            {
                Assert.Equal(t, File.GetLastWriteTimeUtc(f));
                Assert.Equal(len, File.ReadAllBytes(f).Length);
            }

            string outDir = Path.Combine(root, "_uscita");
            RecoveryWriter.Write(r, outDir);
            foreach (string f in new[] { "recupero.json", "carichi.csv", "velocita.csv", "rosate.csv", "note.csv", "RIEPILOGO.txt" })
                Assert.True(File.Exists(Path.Combine(outDir, f)), f);
            string vel = File.ReadAllText(Path.Combine(outDir, "velocita.csv"));
            Assert.Contains("820,1", vel);                                    // decimal comma for an Italian Excel
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void BlankTemplateRowsWithVelocityZeroAreNotShots()
    {
        string root = Path.Combine(Path.GetTempPath(), "recupero_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "modello.grtload");
            var doc = GrtLoadDoc.CreateMinimal("prova", path);
            var empty = new GrtCharge { Name = "Charge 1", ValueKg = 0 };          // an untouched template string: 20 blank rows
            for (int i = 0; i < 20; i++) empty.Shots.Add(new GrtShot(0, null));
            var mixed = new GrtCharge { Name = "41.5", ValueKg = 41.5 * 0.00006479891 };   // two blank rows left after real shots
            foreach (double v in new[] { 820.1, 822.4, 0, 0 }) mixed.Shots.Add(new GrtShot(v, null));
            doc.AddMeasurement("Cronografo", new[] { empty, mixed });
            doc.Save(path);

            var r = HistoryRecovery.Run(root);

            var s = Assert.Single(r.Strings);                                       // the all-zero string is not a string
            Assert.Equal(new[] { 820.1, 822.4 }, s.VelocitiesMps);
            Assert.Equal(2, s.ShotNotes.Count);
            Assert.Equal(2, r.Loads.Single().Shots);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void AnAthlonStringImportedWithoutAChargeHasNoChargeNotThePlaceholderTwoGrams()
    {
        string root = Path.Combine(Path.GetTempPath(), "recupero_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "carico.grtload");
            var doc = GrtLoadDoc.CreateMinimal("prova", path);
            var unknown = new GrtCharge { Name = "139LVN550_11-10-2026", ValueKg = 0.002, Note = "AVG-E=838.9 N=40 UNIT=m/s SRC=Athlon Rangecraft" };
            var plainTwoGrams = new GrtCharge { Name = "pistola", ValueKg = 0.002, Note = "" };                  // a real 2 g charge
            var known = new GrtCharge { Name = "39.2 gr", ValueKg = 0.002, Note = "SRC=Athlon Rangecraft" };      // the name says the weight
            foreach (var c in new[] { unknown, plainTwoGrams, known }) { c.Shots.Add(new GrtShot(800, null)); c.Shots.Add(new GrtShot(801, null)); }
            doc.AddMeasurement("Cronografo", new[] { unknown, plainTwoGrams, known });
            doc.Save(path);

            var r = HistoryRecovery.Run(root);

            Assert.Null(r.Strings.Single(s => s.ChargeName.StartsWith("139LVN")).ChargeGr);
            Assert.Equal(0.002 / 0.00006479891, r.Strings.Single(s => s.ChargeName == "pistola").ChargeGr!.Value, 6);
            Assert.Equal(39.2, r.Strings.Single(s => s.ChargeName == "39.2 gr").ChargeGr!.Value, 6);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Theory]
    [InlineData("carico.grtload", "originale")]
    [InlineData("carico_toolkit_20260101_1200.grtload", "copia del Toolkit")]
    [InlineData("carico_shotmarker_20260101.grtload", "import ShotMarker")]
    [InlineData("POWDER-MEASURE-TEMPLATE_athlon_20260908_1720.grtload", "import cronografo")]
    public void TellsTheFamilyFromTheFileName(string name, string family)
        => Assert.Equal(family, HistoryRecovery.FamilyOf(name));
}
