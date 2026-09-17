using System.Globalization;
using GrtPluginKit.Grt;
using GrtReloadingToolkit.Brass;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// Seating depth writes two GRT inputs, not one. <c>projectile/gdepth</c> is the value the user
/// computed; <c>caliber/oal</c> (COAL) is derived from it and is stored, not recalculated when a
/// load is opened from disk - so a tool that moves gdepth in a file has to move oal with it or
/// leave the load describing two different cartridges.
/// </summary>
public class SeatingDepthCoalTests
{
    /// <summary>
    /// caselen / glen / gdepth / oal, read out of the .grtload files GRT itself ships in
    /// doku/en/faq/files and doku's powder-measure template. These are GRT's own numbers, which is
    /// what makes them worth asserting against: they pin the identity to the program's behaviour
    /// rather than to our reading of its manual.
    /// </summary>
    public static TheoryData<string, double, double, double, double> GrtsOwnLoads => new()
    {
        { "6.5 CM 140 HHPBT",  48.77054, 33.8328,  10.843259999999994, 71.76008 },
        { "30-06 168 HHPBT",   63.35014, 31.1404,   9.90854,           84.582   },
        { "30-06 M2 ball",     63.35014, 28.6004,   7.36854,           84.582   },
        { "6.5 CM 120 HELDM",  48.77054, 30.5308,   8.18134,           71.12    },
        { "powder template",   48.77,    30.3784,   9.2984,            69.85    },
    };

    [Theory]
    [MemberData(nameof(GrtsOwnLoads))]
    public void Coal_matches_the_value_GRT_stored(string name, double caseLen, double bulletLen, double gdepth, double oal)
    {
        double? computed = GrtLoadDoc.CoalFrom(caseLen, bulletLen, gdepth);
        Assert.NotNull(computed);
        // 1e-9 mm is far below anything a caliper resolves; it only guards the arithmetic.
        Assert.True(Math.Abs(oal - computed!.Value) < 1e-9,
            $"{name}: GRT stored oal {oal}, caselen + glen - gdepth gives {computed.Value}");
    }

    [Theory]
    [InlineData(null, 30.0, 10.0)]
    [InlineData(48.0, null, 10.0)]
    [InlineData(48.0, 30.0, null)]
    public void Coal_is_null_when_any_input_is_missing(double? caseLen, double? bulletLen, double? gdepth) =>
        Assert.Null(GrtLoadDoc.CoalFrom(caseLen, bulletLen, gdepth));

    /// <summary>The whole point of the fix: writing a new gdepth moves oal with it.</summary>
    [Fact]
    public void Writing_a_new_seating_depth_rewrites_coal()
    {
        // SaveSibling writes next to the source and prunes older snapshots, so give it a
        // directory of its own rather than scattering files through the system temp folder.
        string dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())).FullName;
        string path = Path.Combine(dir, "test.grtload");
        File.WriteAllText(path, Load(caseLen: 48.77054, glen: 33.8328, gdepth: 10.84326, oal: 71.76008));
        try
        {
            var doc = GrtLoadDoc.Load(path);
            Assert.Equal(71.76008, doc.CoalMm!.Value, 9);   // the load starts consistent

            // Seat 0.5 mm deeper. COAL must come down by exactly the same 0.5 mm.
            const double deeper = 11.34326;
            Assert.True(doc.SetInput("projectile", "gdepth", deeper.ToString("0.#########", CultureInfo.InvariantCulture), "mm"));
            double oal = GrtLoadDoc.CoalFrom(doc.CaseLenMm, doc.BulletLengthMm, deeper)!.Value;
            Assert.True(doc.SetInput("caliber", "oal", oal.ToString("0.#########", CultureInfo.InvariantCulture), "mm"));

            var reread = GrtLoadDoc.Load(doc.SaveSibling("seatdepth"));
            Assert.Equal(deeper, reread.SeatingDepthMm!.Value, 9);
            Assert.Equal(71.26008, reread.CoalMm!.Value, 9);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>Lengths keep four decimals, including trailing zeros, in both units.</summary>
    [Theory]
    [InlineData(10.843259999999994, "10.8433 mm  (0.4269 in)")]
    [InlineData(12.7, "12.7000 mm  (0.5000 in)")]
    [InlineData(3.175, "3.1750 mm  (0.1250 in)")]
    public void Lengths_show_four_decimals(double mm, string expected) =>
        Assert.Equal(expected, BrassCalc.Both(mm, inchFirst: false));

    [Fact]
    public void Lengths_show_four_decimals_with_inch_first() =>
        Assert.Equal("0.5000 in  (12.7000 mm)", BrassCalc.Both(12.7, inchFirst: true));

    private static string Load(double caseLen, double glen, double gdepth, double oal)
    {
        string N(double d) => d.ToString("0.#########", CultureInfo.InvariantCulture);
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n" +
               "<GordonsReloadingTool>\n  <InnerBallistikInput>\n" +
               "    <caliber>\n" +
               "      <input name=\"CaliberName\" value=\"Test\" />\n" +
               $"      <input name=\"caselen\" value=\"{N(caseLen)}\" unit=\"mm\" type=\"decimal\" descr=\"case length\" />\n" +
               $"      <input name=\"oal\" value=\"{N(oal)}\" unit=\"mm\" type=\"decimal\" descr=\"cartridge overall length\" />\n" +
               "    </caliber>\n" +
               "    <projectile>\n" +
               "      <input name=\"ProjectileName\" value=\"Test\" />\n" +
               $"      <input name=\"glen\" value=\"{N(glen)}\" unit=\"mm\" type=\"decimal\" descr=\"projectile overall length\" />\n" +
               $"      <input name=\"gdepth\" value=\"{N(gdepth)}\" unit=\"mm\" type=\"decimal\" descr=\"bullet seating depth\" />\n" +
               "    </projectile>\n" +
               "    <appendix>\n    </appendix>\n  </InnerBallistikInput>\n</GordonsReloadingTool>\n";
    }
}
