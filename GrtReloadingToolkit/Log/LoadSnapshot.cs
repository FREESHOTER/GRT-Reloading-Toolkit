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
    public double? CoalMm { get; init; }
    public double? ChargeGr { get; init; }
    public double? VelocityAvgMs { get; init; }
    public double? SdMs { get; init; }
    public double? EsMs { get; init; }
    public int Shots { get; init; }
    public string Path { get; init; } = "";
    public double? Ba { get; init; }
    public double? A0 { get; init; }

    /// <summary>Set only by <see cref="AllFromGrtload"/> — "{charge} ({measurement title}) — N shots,
    /// mean m/s", the same "{charge}gr ({measurement.Title})" label every other GRT-reading form
    /// (ChronoStatsForm, SdRootCauseForm, LadderAnalyzerForm...) already uses for its own picker.</summary>
    public string? ChargeLabel { get; init; }

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
        Ba = Ba,
        A0 = A0,
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
            PowderName = doc.PropellantName,
            BulletName = doc.ProjectileName,
            BulletWeightGr = doc.BulletMassGr is > 0 ? doc.BulletMassGr : null,
            CoalMm = doc.CoalMm is > 0 ? doc.CoalMm : null,
            ChargeGr = chg,
            VelocityAvgMs = st is { N: > 0 } ? st.Value.Mean : null,
            SdMs = st is { N: > 0 } ? st.Value.Sd : null,
            EsMs = st is { N: > 0 } ? st.Value.Es : null,
            Shots = st?.N ?? 0,
            Ba = doc.PropellantBa is > 0 ? doc.PropellantBa : null,
            A0 = doc.InputNumber("propellant", "a0") is { } a0 && a0 > 0 ? a0 : null,
        };
    }

    /// <summary>
    /// One snapshot per charge that was actually chronographed, across every measurement in the
    /// load -- for a load with several logged charges (any ladder), <see cref="FromGrtload"/> alone
    /// always resolves to the exact same one (the last measurement's last charge), silently, no
    /// matter which charge the user actually wants to log. Every other tool that reads charges back
    /// out of a GRT load (ChronoStatsForm, SdRootCauseForm, LadderAnalyzerForm...) lists all of them
    /// and lets the user pick; this gives <see cref="Ui.JournalForm"/> the same list to build that
    /// picker from, instead of guessing on its own. Never empty: a load with no chronographed charge
    /// at all still returns one snapshot carrying just the recipe fields (mirroring the "no charge"
    /// fallback the field-name reader elsewhere in this class already applies), so a caller can
    /// always take the single result when there's nothing to choose between.
    /// </summary>
    public static IReadOnlyList<LoadSnapshot> AllFromGrtload(string path)
    {
        var doc = GrtLoadDoc.Load(path);
        string loadName = System.IO.Path.GetFileNameWithoutExtension(path);
        double? ba = doc.PropellantBa is > 0 ? doc.PropellantBa : null;
        double? a0 = doc.InputNumber("propellant", "a0") is { } a0v && a0v > 0 ? a0v : null;

        LoadSnapshot Base(double? chargeGr, string? label, StringStats? st) => new()
        {
            Path = path,
            LoadName = loadName,
            Caliber = doc.CaliberName,
            Firearm = doc.GunName,
            PowderName = doc.PropellantName,
            BulletName = doc.ProjectileName,
            BulletWeightGr = doc.BulletMassGr is > 0 ? doc.BulletMassGr : null,
            CoalMm = doc.CoalMm is > 0 ? doc.CoalMm : null,
            ChargeGr = chargeGr,
            VelocityAvgMs = st is { N: > 0 } ? st.Value.Mean : null,
            SdMs = st is { N: > 0 } ? st.Value.Sd : null,
            EsMs = st is { N: > 0 } ? st.Value.Es : null,
            Shots = st?.N ?? 0,
            Ba = ba,
            A0 = a0,
            ChargeLabel = label,
        };

        var candidates = new List<LoadSnapshot>();
        foreach (var meas in doc.Measurements())
            foreach (var ch in meas.Charges)
            {
                var vs = ch.Shots.Select(s => s.VelocityMps).Where(v => v > 0).ToList();
                if (vs.Count == 0) continue;
                var st = StringStats.From(vs);
                string label = ch.ChargeGrains is { } g
                    ? FormattableString.Invariant($"{g:0.0##} gr ({meas.Title}) — {st.N} shots, {st.Mean:0} m/s")
                    : $"{ch.Name} ({meas.Title}) — {st.N} shots, {st.Mean:0} m/s";
                candidates.Add(Base(ch.ChargeGrains ?? doc.PropellantChargeGr, label, st));
            }

        if (candidates.Count == 0)
            candidates.Add(Base(doc.PropellantChargeGr, null, null));

        return candidates;
    }
}
