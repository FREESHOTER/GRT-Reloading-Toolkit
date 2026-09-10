using System.Globalization;
using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Cards;

/// <summary>Everything a recipe card / box label shows for one load.</summary>
public sealed class LoadCard
{
    public string Caliber { get; set; } = "";
    public string Firearm { get; set; } = "";
    public string Bullet { get; set; } = "";
    public double? BulletGr { get; set; }
    public string Powder { get; set; } = "";
    public double? ChargeGr { get; set; }
    public string Primer { get; set; } = "";
    public string Brass { get; set; } = "";
    public double? CoalMm { get; set; }
    public double? CbtoMm { get; set; }
    public double? SeatingDepthMm { get; set; }
    public double? MvMs { get; set; }
    public double? SdMs { get; set; }
    public string Date { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
    public string LotNote { get; set; } = "";
    public string CostPerRound { get; set; } = "";
    public string SourceFile { get; set; } = "";

    public string Title => string.IsNullOrWhiteSpace(Caliber) ? "Load" : Caliber;

    public string ChargeLine => ChargeGr is { } g
        ? $"{g.ToString("0.0#", CultureInfo.InvariantCulture)} gr {Powder}".Trim()
        : Powder;

    /// <summary>Compact human+machine readable block for the QR code.</summary>
    public string QrText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("GRT LOAD");
        Add(sb, "cal", Caliber);
        Add(sb, "gun", Firearm);
        Add(sb, "bullet", Bullet + (BulletGr is { } b ? " " + b.ToString("0.#", CultureInfo.InvariantCulture) + "gr" : ""));
        Add(sb, "powder", Powder);
        if (ChargeGr is { } c) Add(sb, "charge", c.ToString("0.0##", CultureInfo.InvariantCulture) + " gr");
        Add(sb, "primer", Primer);
        Add(sb, "brass", Brass);
        if (CoalMm is { } o) Add(sb, "COAL", o.ToString("0.00", CultureInfo.InvariantCulture) + " mm");
        if (CbtoMm is { } t) Add(sb, "CBTO", t.ToString("0.00", CultureInfo.InvariantCulture) + " mm");
        if (MvMs is { } v) Add(sb, "MV", v.ToString("0", CultureInfo.InvariantCulture) + " m/s" + (SdMs is { } s ? " SD " + s.ToString("0.0", CultureInfo.InvariantCulture) : ""));
        Add(sb, "date", Date);
        Add(sb, "cost", CostPerRound);
        return sb.ToString().TrimEnd();

        static void Add(System.Text.StringBuilder s, string k, string v)
        { if (!string.IsNullOrWhiteSpace(v)) s.Append(k).Append('=').Append(v).Append('\n'); }
    }

    public static LoadCard FromGrtload(string path)
    {
        var doc = GrtLoadDoc.Load(path);
        static double? R(double? v, int dp) => v is { } x ? Math.Round(x, dp) : null;

        var card = new LoadCard
        {
            Caliber = doc.CaliberName,
            Firearm = doc.GunName,
            Bullet = CleanBullet(doc.ProjectileName),
            BulletGr = R(doc.BulletMassGr, 1),
            Powder = doc.PropellantName,
            ChargeGr = R(doc.PropellantChargeGr, 2),
            CoalMm = R(doc.CoalMm, 3),
            SeatingDepthMm = R(doc.SeatingDepthMm, 3),
            SourceFile = path,
        };

        GrtCharge? last = doc.Measurements().SelectMany(m => m.Charges).LastOrDefault(c => c.Shots.Count > 0);
        if (last is { Shots.Count: > 0 })
        {
            var v = last.Shots.Select(s => s.VelocityMps).Where(x => x > 0).ToList();
            if (v.Count > 0) { var st = StringStats.From(v); card.MvMs = Math.Round(st.Mean, 1); card.SdMs = Math.Round(st.Sd, 1); }
            if (last.ChargeGrains is { } g) card.ChargeGr = Math.Round(g, 2);
        }
        return card;
    }

    /// <summary>GRT projectile names read "Maker, Product, 0.264, 139.00 grain" — keep maker + product.</summary>
    private static string CleanBullet(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var parts = name.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        parts.RemoveAll(p => System.Text.RegularExpressions.Regex.IsMatch(
            p, @"^(0?\.\d+|\d+(\.\d+)?\s*(grain|gr|gn)|\d+(\.\d+)?\s*mm)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        return parts.Count > 0 ? string.Join(", ", parts) : name;
    }
}
