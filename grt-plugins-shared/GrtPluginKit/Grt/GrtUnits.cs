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

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private GrtUnits(bool inch, bool fps)
    {
        LengthInInch = inch;
        VelocityInFps = fps;
    }

    /// <summary>What the running GRT shows, read once. Metric when there is no install to ask.</summary>
    public static GrtUnits Current { get; } = From(GrtConfig.Current);

    /// <summary>True when GRT shows lengths in inches. Follows <c>oal</c> — COAL is GRT's oal.</summary>
    public bool LengthInInch { get; }

    /// <summary>True when GRT shows velocities in feet per second.</summary>
    public bool VelocityInFps { get; }

    /// <summary>
    /// Reads a config's opinion on the two units a card shows. A null config — no GRT installed
    /// beside us — gives metric, which is what the toolkit has always displayed.
    /// </summary>
    public static GrtUnits From(GrtConfig? cfg)
    {
        if (cfg == null) return new GrtUnits(false, false);
        string? v = cfg.UnitFor("velocity");
        bool fps = v is not null
            && (v.StartsWith("ft", StringComparison.OrdinalIgnoreCase) || v.Equals("fps", StringComparison.OrdinalIgnoreCase));
        return new GrtUnits(cfg.IsInch("oal"), fps);
    }

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
}
