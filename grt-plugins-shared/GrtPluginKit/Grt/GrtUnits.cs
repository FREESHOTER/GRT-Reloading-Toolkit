using System.Globalization;
using GrtPluginKit.Util;

namespace GrtPluginKit.Grt;

/// <summary>
/// Renders a stored value the way the GRT beside us would show it.
///
/// Everything the toolkit holds is metric, because that is what a .grtload stores whatever GRT is
/// set to (see <see cref="GrtConfig"/>). That makes this the one place where metric becomes what
/// the shooter actually reads: a card, a box label, the QR block. A load worked up in GRT in
/// inches and ft/s should not come out of the label printer in mm and m/s.
///
/// <see cref="Current"/> is the ambient answer for UI code. <see cref="From"/> takes an explicit
/// config, which is how this is exercised against an install that is not the one we are running
/// beside.
/// </summary>
public sealed class GrtUnits
{
    /// <summary>Exact by definition: an inch is 25.4 mm.</summary>
    public const double MmPerInch = 25.4;

    /// <summary>Exact by definition: a foot is 0.3048 m, so m/s divided by this is ft/s.</summary>
    public const double MetresPerFoot = 0.3048;

    /// <summary>Exact by definition: a yard is three feet.</summary>
    public const double MetresPerYard = MetresPerFoot * 3;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private GrtUnits(bool inch, bool fps, bool fahrenheit, bool yards, bool chargeGrams, bool bulletGrams, bool twistInch)
    {
        LengthInInch = inch;
        TwistInInch = twistInch;
        VelocityInFps = fps;
        TemperatureInF = fahrenheit;
        DistanceInYards = yards;
        ChargeInGrams = chargeGrams;
        BulletMassInGrams = bulletGrams;
    }

    /// <summary>What the running GRT shows, read once. Metric when there is no install to ask.</summary>
    public static GrtUnits Current { get; } = From(GrtConfig.Current);

    /// <summary>True when GRT shows lengths in inches. Follows <c>oal</c> — COAL is GRT's oal.</summary>
    public bool LengthInInch { get; }

    /// <summary>True when GRT shows velocities in feet per second.</summary>
    public bool VelocityInFps { get; }

    /// <summary>True when GRT shows temperatures in Fahrenheit. Follows <c>pt</c>, GRT's powder temp.</summary>
    public bool TemperatureInF { get; }

    /// <summary>True when GRT shows shooting distances in yards. Follows <c>range</c>.</summary>
    public bool DistanceInYards { get; }

    /// <summary>True when GRT shows powder charges in grams. Follows <c>charge</c>.</summary>
    public bool ChargeInGrams { get; }

    /// <summary>
    /// True when GRT shows projectile weights in grams. Follows <c>mp</c>, which a user sets
    /// separately from <c>charge</c> — a 140 gr bullet over 2.5 g of powder is a real config.
    /// </summary>
    public bool BulletMassInGrams { get; }

    /// <summary>
    /// True when GRT shows a twist length in inches. Follows <c>twistlen</c>, its own field in the
    /// unit map — a barrel is sold as "1:8" in an imperial shop and "1:203" in a metric one, and
    /// GRT lets that be set apart from the length it shows a COAL in.
    /// </summary>
    public bool TwistInInch { get; }

    /// <summary>
    /// Reads a config's opinion on the units we display. A null config — no GRT installed beside
    /// us — gives metric, which is what the toolkit has always displayed.
    /// </summary>
    public static GrtUnits From(GrtConfig? cfg)
    {
        if (cfg == null) return new GrtUnits(false, false, false, false, false, false, false);
        string? v = cfg.UnitFor("velocity");
        bool fps = v is not null
            && (v.StartsWith("ft", StringComparison.OrdinalIgnoreCase) || v.Equals("fps", StringComparison.OrdinalIgnoreCase));
        // "F" and "°F" both appear; anything else (C, °C, K) we treat as the metric side.
        string? t = cfg.UnitFor("pt")?.TrimStart('°', ' ');
        bool fahrenheit = t is not null && t.StartsWith("F", StringComparison.OrdinalIgnoreCase);
        string? r = cfg.UnitFor("range");
        bool yards = r is not null
            && (r.StartsWith("yd", StringComparison.OrdinalIgnoreCase) || r.StartsWith("yard", StringComparison.OrdinalIgnoreCase));
        // An install that has no twistlen entry at all follows its lengths rather than dropping an
        // otherwise imperial user into millimetres for this one number.
        bool twist = cfg.UnitFor("twistlen") is null ? cfg.IsInch("oal") : cfg.IsInch("twistlen");
        return new GrtUnits(cfg.IsInch("oal"), fps, fahrenheit, yards, Grams(cfg, "charge"), Grams(cfg, "mp"), twist);

        // GRT writes "grain" for the imperial side; grams appear as "g" and milligrams as "mg",
        // and only "g" is offered for a charge, so anything that is not a grain is the gram side.
        static bool Grams(GrtConfig c, string field)
        {
            string? u = c.UnitFor(field);
            return u is not null && !u.StartsWith("gr", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The unit name alone, for labelling a number we cannot convert — see <see cref="Length"/>.</summary>
    public string LengthUnitName => LengthInInch ? "in" : "mm";

    /// <summary>The unit name alone, for an axis label or a column header.</summary>
    public string VelocityUnitName => VelocityInFps ? "ft/s" : "m/s";

    /// <summary>The unit name alone, for a column header the user types into.</summary>
    public string TemperatureUnitName => TemperatureInF ? "°F" : "°C";

    /// <summary>The unit name alone, for a shooting distance.</summary>
    public string DistanceUnitName => DistanceInYards ? "yd" : "m";

    /// <summary>A stored length, with its unit. Four decimals either way, per <see cref="Str.LengthFormat"/>.</summary>
    public string Length(double mm) => LengthInInch
        ? Str.Len(mm / MmPerInch) + " in"
        : Str.Len(mm) + " mm";

    /// <summary>A stored length as a bare number, for a box the user reads and edits.</summary>
    public double LengthValue(double mm) => LengthInInch ? mm / MmPerInch : mm;

    /// <summary>The inverse of <see cref="LengthValue"/>: what the user typed, back to stored mm.</summary>
    public double LengthToMm(double shown) => LengthInInch ? shown * MmPerInch : shown;

    /// <summary>The unit name alone, for a twist the call site prints itself.</summary>
    public string TwistUnitName => TwistInInch ? "in" : "mm";

    /// <summary>A stored twist length as a bare number, for a box headed with its unit.</summary>
    public double TwistValue(double mm) => TwistInInch ? mm / MmPerInch : mm;

    /// <summary>The inverse of <see cref="TwistValue"/>: what the user typed, back to stored mm.</summary>
    public double TwistToMm(double shown) => TwistInInch ? shown * MmPerInch : shown;

    /// <summary>
    /// A twist the way a barrel is described rather than the way a length is: "1:8 in",
    /// "1:203.2 mm". Every trailing zero goes, because that barrel is sold as 1:8 and neither
    /// "1:8.0" nor "1:8.0000" is anybody's idea of a twist rate — but four places stay available
    /// for the 1:7.875 and 1:203.2 that exist.
    /// </summary>
    public string Twist(double mm) => "1:" + TwistValue(mm).ToString("0.####", Inv) + " " + TwistUnitName;

    /// <summary>A stored velocity, with its unit. Whole units — no chronograph resolves finer.</summary>
    public string Velocity(double ms) => VelocityInFps
        ? (ms / MetresPerFoot).ToString("0", Inv) + " ft/s"
        : ms.ToString("0", Inv) + " m/s";

    /// <summary>
    /// A velocity spread (SD, ES) in the same unit as <see cref="Velocity"/> but without the
    /// suffix, because it is always printed next to a velocity that already carries one. One
    /// decimal: an SD is a statistic, and dropping it to whole units hides the difference
    /// between good ammunition and lucky ammunition.
    /// </summary>
    public string VelocitySd(double ms) =>
        (VelocityInFps ? ms / MetresPerFoot : ms).ToString("0.0", Inv);

    /// <summary>A stored velocity as a bare number, for an axis whose unit is written once on the axis.</summary>
    public double VelocityValue(double ms) => VelocityInFps ? ms / MetresPerFoot : ms;

    /// <summary>The inverse of <see cref="VelocityValue"/>: what the user typed, back to stored m/s.</summary>
    public double VelocityToMps(double shown) => VelocityInFps ? shown * MetresPerFoot : shown;

    /// <summary>A stored shooting distance as a bare number, for a column headed with its unit.</summary>
    public double DistanceValue(double m) => DistanceInYards ? m / MetresPerYard : m;

    /// <summary>The inverse of <see cref="DistanceValue"/>: what the user typed, back to stored metres.</summary>
    public double DistanceToMetres(double shown) => DistanceInYards ? shown * MetresPerYard : shown;

    /// <summary>A stored temperature as a bare number, for a cell the user reads and edits.</summary>
    public double TemperatureValue(double celsius) => TemperatureInF ? celsius * 9.0 / 5.0 + 32.0 : celsius;

    /// <summary>The inverse of <see cref="TemperatureValue"/>: what the user typed, back to stored °C.</summary>
    public double TemperatureToCelsius(double shown) => TemperatureInF ? (shown - 32.0) * 5.0 / 9.0 : shown;

    /// <summary>
    /// A powder-temperature sensitivity — m/s per °C — in the units being shown. Both axes convert,
    /// and the temperature one is an interval, not a reading: a degree F spans 5/9 of a degree C,
    /// with no 32 to add. Dropping that factor overstates an imperial slope by 80%.
    /// </summary>
    public double VelocityPerDegreeValue(double mpsPerCelsius) =>
        VelocityValue(mpsPerCelsius) * (TemperatureInF ? 5.0 / 9.0 : 1.0);

    /// <summary>The unit <see cref="VelocityPerDegreeValue"/> returns, e.g. "ft/s per °F".</summary>
    public string VelocityPerDegreeUnitName => VelocityUnitName + " per " + TemperatureUnitName;

    /// <summary>Grains in a gram. A grain is exactly 64.79891 mg, so this is exact to the digits shown.</summary>
    public const double GrainsPerGram = 1000.0 / 64.79891;

    /// <summary>The unit a powder charge is shown in.</summary>
    public string ChargeUnitName => ChargeInGrams ? "g" : "gr";

    /// <summary>The unit a projectile weight is shown in.</summary>
    public string BulletMassUnitName => BulletMassInGrams ? "g" : "gr";

    /// <summary>
    /// A stored charge weight as a bare number, for a column or axis headed with its unit. The
    /// toolkit holds charges in grains throughout — a .grtload records them that way and so does
    /// every chronograph file it reads — so grains in is the only case this has to handle.
    /// </summary>
    public double ChargeValue(double gr) => ChargeInGrams ? gr / GrainsPerGram : gr;

    /// <summary>The inverse of <see cref="ChargeValue"/>: what the user typed, back to stored grains.</summary>
    public double ChargeToGrains(double shown) => ChargeInGrams ? shown * GrainsPerGram : shown;

    /// <summary>
    /// A stored charge, with its unit. Grains keep the three trailing-trimmed places a powder
    /// scale resolves; grams need four fixed, because 0.02 gr — a good scale's last digit — is
    /// 0.0013 g, and three places would round two neighbouring ladder steps onto each other.
    /// </summary>
    public string Charge(double gr) => ChargeValue(gr).ToString(ChargeFormat, Inv) + " " + ChargeUnitName;

    /// <summary>The numeric format <see cref="Charge"/> uses, for tables that place the unit themselves.</summary>
    public string ChargeFormat => ChargeInGrams ? "0.0000" : "0.0##";

    /// <summary>A stored projectile weight as a bare number. See <see cref="ChargeValue"/>.</summary>
    public double BulletMassValue(double gr) => BulletMassInGrams ? gr / GrainsPerGram : gr;

    /// <summary>A stored projectile weight, with its unit. Bullets come in whole grains, or tenths of a gram.</summary>
    public string BulletMass(double gr) =>
        BulletMassValue(gr).ToString(BulletMassInGrams ? "0.00" : "0.#", Inv) + " " + BulletMassUnitName;

    /// <summary>A stored temperature, with its unit. One decimal — chronograph sensors resolve no finer.</summary>
    public string Temperature(double celsius) =>
        TemperatureValue(celsius).ToString("0.0", Inv) + " " + TemperatureUnitName;
}
