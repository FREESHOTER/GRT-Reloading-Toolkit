namespace GrtSensitivityLab.Sweep;

/// <summary>
/// One GRT input the sweep can perturb. <see cref="Delta"/> is in <see cref="Unit"/> -- the same
/// unit GRT itself stores the value in for a typical metric-configured load (see the .grtload
/// field notes: casevol[cm3], caselen/xe/Dz/gdepth[mm], gpressure/gfriction[bar], mp[g]).
/// <see cref="AbsoluteValues"/>, when set, replaces the +/-delta pair with a fixed list of test
/// values instead (used for gfriction, where 0 is itself a meaningful case). Ported from
/// GrtModelLab's dormant SensitivitySweep/SweepModels.cs -- kept unreferenced there since
/// 2026-09-11 because it studies GRT's own model in general, not the barrel specifically, which is
/// exactly what this plugin is for.
/// </summary>
internal sealed record SweepParam(string Label, string Section, string Name, double Delta, string Unit, double[]? AbsoluteValues = null)
{
    public bool IsAbsolute => AbsoluteValues is { Length: > 0 };
}

internal static class SweepParams
{
    public static readonly SweepParam[] All =
    {
        new("Case volume (casevol)", "caliber", "casevol", 0.02, "cm3"),
        new("Case length (caselen)", "caliber", "caselen", 0.1, "mm"),
        new("Barrel length (xe)", "gun", "xe", 2.0, "mm"),
        new("Groove diameter (Dz)", "caliber", "Dz", 0.02, "mm"),
        new("Bullet mass (mp)", "projectile", "mp", 0.2, "gr"),
        new("Seating depth (gdepth)", "projectile", "gdepth", 0.05, "mm"),
        new("Bearing-surface pressure (gpressure)", "projectile", "gpressure", 20, "bar"),
        new("Bullet friction (gfriction)", "projectile", "gfriction", 0, "bar", AbsoluteValues: new[] { 0.0, 50.0, 100.0 }),
    };
}

/// <summary>One captured GRT result for a parameter held at a specific test value.</summary>
internal sealed class SweepPoint
{
    public required SweepParam Param { get; init; }
    public required string VariantLabel { get; init; }
    public double? MvMps { get; set; }
    public double? Pmax { get; set; }
    public string PmaxUnit { get; set; } = "";
}

internal sealed class SweepReport
{
    public double? BaselineMv { get; init; }
    public double? BaselinePmax { get; init; }
    public string PmaxUnit { get; init; } = "";
    public List<SweepPoint> Points { get; } = new();

    public IEnumerable<SweepPoint> BySensitivity() =>
        Points.Where(p => p.MvMps is not null)
              .OrderByDescending(p => Math.Abs((p.MvMps ?? 0) - (BaselineMv ?? 0)));

    /// <summary>
    /// Explicit sign + <c>Math.Abs</c> instead of a custom "+0;-0" two-section format string:
    /// under .NET 8 a negative value that rounds to zero at the target precision comes out of a
    /// 2-section custom format as a double-signed "-+0" instead of a clean "+0" (doesn't reproduce
    /// under Windows PowerShell's own .NET Framework runtime, which made it easy to miss).
    /// Formatting the sign ourselves sidesteps the section-selection edge case entirely.
    /// </summary>
    internal static string Signed(double v, string absFormat, System.Globalization.CultureInfo ci)
        => (v < 0 ? "-" : "+") + Math.Abs(v).ToString(absFormat, ci);

    public string Build(string caliberName, string powderName)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.Append("GRT Sensitivity Lab -- sensitivity sweep\r\n");
        sb.Append($"{caliberName}, {powderName}\r\n\r\n");
        if (BaselineMv is { } bmv)
            sb.Append(FormattableString.Invariant($"Baseline: MV {bmv:0.0} m/s") +
                      (BaselinePmax is { } bpm ? FormattableString.Invariant($", Pmax {bpm:0} {PmaxUnit}") : "") + "\r\n\r\n");
        sb.Append("Parameter                              Variant        MV m/s   dMV      Pmax     dPmax\r\n");
        sb.Append(new string('-', 88)).Append("\r\n");
        foreach (var p in BySensitivity())
        {
            double? dmv = p.MvMps is { } mv && BaselineMv is { } b ? mv - b : null;
            double? dpm = p.Pmax is { } pm && BaselinePmax is { } bp ? pm - bp : null;
            sb.Append(string.Format(ci, "{0,-38} {1,-14} {2,7} {3,8} {4,8} {5,8}\r\n",
                p.Param.Label.Length > 38 ? p.Param.Label[..38] : p.Param.Label,
                p.VariantLabel,
                p.MvMps is { } m ? m.ToString("0.0", ci) : "-",
                dmv is { } d ? Signed(d, "0.0", ci) : "-",
                p.Pmax is { } pp ? pp.ToString("0", ci) : "-",
                dpm is { } dp ? Signed(dp, "0", ci) : "-"));
        }
        sb.Append("\r\nLargest |dMV| first -- the parameters GRT's prediction moves the most for a\r\n");
        sb.Append("realistic measurement/manufacturing tolerance on that input.\r\n");
        return sb.ToString();
    }
}
