using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Log;

/// <summary>Prefill values pulled from the .grtload currently open in GRT.</summary>
public sealed class LoadSnapshot
{
    public string LoadName { get; init; } = "";
    public string Caliber { get; init; } = "";
    public string Firearm { get; init; } = "";
    public string PowderName { get; init; } = "";
    public string BulletName { get; init; } = "";
    public double? BulletWeightGr { get; init; }
    public double? ChargeGr { get; init; }
    public double? VelocityAvgMs { get; init; }
    public double? SdMs { get; init; }
    public double? EsMs { get; init; }
    public int Shots { get; init; }
    public string Path { get; init; } = "";

    public JournalEntry ToEntry() => new()
    {
        Date = DateTime.Now.ToString("yyyy-MM-dd"),
        LoadName = LoadName,
        Caliber = Caliber,
        Firearm = Firearm,
        ChargeGr = ChargeGr ?? 0,
        VelocityAvgMs = VelocityAvgMs,
        SdMs = SdMs,
        EsMs = EsMs,
        Rounds = Shots > 0 ? Shots : 0,
        GrtloadPath = Path,
    };

    public static LoadSnapshot FromGrtload(string path)
    {
        var doc = GrtLoadDoc.Load(path);

        // most-recent measurement's last charge, if any, gives charge + velocity stats
        GrtCharge? charge = doc.Measurements().SelectMany(m => m.Charges).LastOrDefault(c => c.Shots.Count > 0);
        double? chg = charge?.ChargeGrains ?? doc.PropellantChargeGr;
        StringStats? st = charge is { Shots.Count: > 0 }
            ? StringStats.From(charge.Shots.Select(s => s.VelocityMps).Where(v => v > 0).ToList())
            : null;

        return new LoadSnapshot
        {
            Path = path,
            LoadName = System.IO.Path.GetFileNameWithoutExtension(path),
            Caliber = doc.CaliberName,
            Firearm = doc.GunName,
            ChargeGr = chg,
            VelocityAvgMs = st is { N: > 0 } ? st.Value.Mean : null,
            SdMs = st is { N: > 0 } ? st.Value.Sd : null,
            EsMs = st is { N: > 0 } ? st.Value.Es : null,
            Shots = st?.N ?? 0,
        };
    }
}
