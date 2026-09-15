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

    private GrtUnits(bool inch, bool fps, bool fahrenheit, bool yards)
    {
        LengthInInch = inch;
        VelocityInFps = fps;
        TemperatureInF = fahrenheit;
        DistanceInYards = yards;
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

    /// <summary>
    /// Reads a config's opinion on the units we display. A null config — no GRT installed beside
    /// us — gives metric, which is what the toolkit has always displayed.
    /// </summary>
    public static GrtUnits From(GrtConfig? cfg)
    {
        if (cfg == null) return new GrtUnits(false, false, false, false);
        string? v = cfg.UnitFor("velocity");
        bool fps = v is not null
            && (v.StartsWith("ft", StringComparison.OrdinalIgnoreCase) || v.Equals("fps", StringComparison.OrdinalIgnoreCase));
        // "F" and "°F" both appear; anything else (C, °C, K) we treat as the metric side.
        string? t = cfg.UnitFor("pt")?.TrimStart('°', ' ');
        bool fahrenheit = t is not null && t.StartsWith("F", StringComparison.OrdinalIgnoreCase);
        string? r = cfg.UnitFor("range");
        bool yards = r is not null
            && (r.StartsWith("yd", StringComparison.OrdinalIgnoreCase) || r.StartsWith("yard", StringComparison.OrdinalIgnoreCase));
        return new GrtUnits(cfg.IsInch("oal"), fps, fahrenheit, yards);
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

    /// <summary>A stored temperature as a bare number, for a cell the user reads and edits.</summary>
    public double TemperatureValue(double celsius) => TemperatureInF ? celsius * 9.0 / 5.0 + 32.0 : celsius;

    /// <summary>The inverse of <see cref="TemperatureValue"/>: what the user typed, back to stored °C.</summary>
    public double TemperatureToCelsius(double shown) => TemperatureInF ? (shown - 32.0) * 5.0 / 9.0 : shown;

    /// <summary>A stored temperature, with its unit. One decimal — chronograph sensors resolve no finer.</summary>
    public string Temperature(double celsius) =>
        TemperatureValue(celsius).ToString("0.0", Inv) + " " + TemperatureUnitName;
}
