using GrtPluginKit.Grt;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The bullet's own diameter and the barrel's twist, read the way the load file stores them (millimetres, the twist as the length of one turn).</summary>
public sealed class GrtLoadDocBulletTests
{
    private static GrtLoadDoc Doc(string projectileInputs, string gunInputs)
    {
        string path = Path.Combine(Path.GetTempPath(), "grt-bullet-test-" + Guid.NewGuid().ToString("N") + ".grtload");
        File.WriteAllText(path,
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<GordonsReloadingTool version=\"2021.2030-NIGHTLY\">\n<InnerBallistikInput>\n" +
            "<title>t</title><caliber><input name=\"CaliberName\" value=\"x\" /></caliber>" +
            "<gun>" + gunInputs + "</gun><projectile>" + projectileInputs + "</projectile>" +
            "<propellant><input name=\"pname\" value=\"x\" /></propellant><appendix></appendix>\n</InnerBallistikInput>\n</GordonsReloadingTool>");
        try { return GrtLoadDoc.Load(path); } finally { File.Delete(path); }
    }

    [Fact]
    public void DiameterAndTwistAreReadInMillimetres()
    {
        var d = Doc("<input name=\"Dbul\" value=\"6.71\" unit=\"mm\" /><input name=\"glen\" value=\"34.55\" unit=\"mm\" />",
                    "<input name=\"twistlen\" value=\"202.99934\" unit=\"mm\" />");
        Assert.Equal(6.71, d.BulletDiameterMm!.Value, 6);
        Assert.Equal(202.99934, d.TwistLengthMm!.Value, 6);
        Assert.Equal(34.55, d.BulletLengthMm!.Value, 6);
    }

    [Fact]
    public void AMissingFieldIsNullNotZero()
    {
        var d = Doc("<input name=\"glen\" value=\"34.55\" unit=\"mm\" />", "");
        Assert.Null(d.BulletDiameterMm);
        Assert.Null(d.TwistLengthMm);
    }
}
