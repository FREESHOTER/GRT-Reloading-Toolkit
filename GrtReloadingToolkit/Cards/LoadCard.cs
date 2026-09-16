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

    /// <summary>The barrel the load was worked up in, named the way the inventory names it.</summary>
    public string Barrel { get; set; } = "";

    /// <summary>
    /// The barrel's twist, as the length of one full turn in mm — the same metric-inside rule
    /// every other length on this card follows. 1:8 in is 203.2 mm.
    /// </summary>
    public double? BarrelTwistMm { get; set; }

    /// <summary>The barrel's length in mm.</summary>
    public double? BarrelLengthMm { get; set; }
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
        ? $"{GrtUnits.Current.Charge(g)} {Powder}".Trim()
        : Powder;

    /// <summary>
    /// The barrel as one line: what it is called, then the two numbers that identify it, in GRT's
    /// units. The same load out of a 1:8 26 in tube and a 1:9 20 in one is two different loads, so
    /// a card that names the barrel is worth more later than one that does not. Empty when no
    /// barrel is on the card, which is how a caller decides whether to print the row at all.
    /// </summary>
    public string BarrelLine
    {
        get
        {
            var u = GrtUnits.Current;
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(Barrel)) parts.Add(Barrel.Trim());
            if (BarrelTwistMm is > 0 and { } t) parts.Add(u.Twist(t));
            if (BarrelLengthMm is > 0 and { } l) parts.Add(u.Length(l));
            return string.Join("  ", parts);
        }
    }

    /// <summary>Compact human+machine readable block for the QR code.</summary>
    public string QrText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("GRT LOAD");
        Add(sb, "cal", Caliber);
        Add(sb, "gun", Firearm);
        var u = GrtUnits.Current;
        Add(sb, "bullet", Bullet + (BulletGr is { } b ? " " + u.BulletMass(b) : ""));
        Add(sb, "powder", Powder);
        if (ChargeGr is { } c) Add(sb, "charge", u.Charge(c));
        Add(sb, "primer", Primer);
        Add(sb, "brass", Brass);
        Add(sb, "barrel", BarrelLine);
        if (CoalMm is { } o) Add(sb, "COAL", u.Length(o));
        if (CbtoMm is { } t) Add(sb, "CBTO", u.Length(t));
        if (MvMs is { } v) Add(sb, "MV", u.Velocity(v) + (SdMs is { } s ? " SD " + u.VelocitySd(s) : ""));
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
            // Lengths are carried at the precision GRT stored them and rounded only where they
            // are displayed; rounding here would have thrown the extra places away for every
            // consumer at once, invisibly.
            CoalMm = doc.CoalMm,
            SeatingDepthMm = doc.SeatingDepthMm,
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
