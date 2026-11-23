namespace GrtSensitivityLab.WhatIf;

/// <summary>
/// One GRT input the "what if" tool can substitute a real measured value into, in place of
/// whatever the .grtload file currently holds. Same catalogue of inputs GrtModelLab's dormant
/// SensitivitySweepForm/SweepEngine used for its own delta-sweep design — it's still the right
/// list of "worth checking against a real measurement" GRT inputs, just used the other way round
/// here: one concrete substitution per parameter instead of a synthetic +/- delta pair.
/// </summary>
internal sealed record WhatIfParam(string Label, string Section, string Name, string Unit);

internal static class WhatIfParams
{
    public static readonly WhatIfParam[] All =
    {
        new("Case volume (casevol)", "caliber", "casevol", "cm3"),
        new("Case length (caselen)", "caliber", "caselen", "mm"),
        new("Barrel length (xe)", "gun", "xe", "mm"),
        new("Groove diameter (Dz)", "caliber", "Dz", "mm"),
        new("Bullet mass (mp)", "projectile", "mp", "gr"),
        new("Seating depth (gdepth)", "projectile", "gdepth", "mm"),
        new("Bearing-surface pressure (gpressure)", "projectile", "gpressure", "bar"),
        new("Bullet friction (gfriction)", "projectile", "gfriction", "bar"),
    };
}

/// <summary>One captured result: substituting <see cref="Param"/>'s file value with
/// <see cref="MeasuredValue"/> and re-simulating in GRT.</summary>
internal sealed class WhatIfPoint
{
    public required WhatIfParam Param { get; init; }
    public double? FileValue { get; init; }
    public double MeasuredValue { get; init; }
    public double? MvMps { get; set; }
    public double? Pmax { get; set; }
    public string PmaxUnit { get; set; } = "";
}

internal sealed class WhatIfReport
{
    public double? BaselineMv { get; init; }
    public double? BaselinePmax { get; init; }
    public string PmaxUnit { get; init; } = "";

    // Insertion order, not re-sorted by impact: this is a log of "what if I use my measured X"
    // questions asked one at a time, and re-sorting after each new one would keep moving rows the
    // user just read.
    public List<WhatIfPoint> Points { get; } = new();

    /// <summary>
    /// Explicit sign + <c>Math.Abs</c> instead of a custom "+0;-0" two-section format string: under
    /// .NET 8 a negative value that rounds to zero at the target precision comes out of a 2-section
    /// custom format as a double-signed "-+0" instead of a clean "+0" (doesn't reproduce under
    /// Windows PowerShell's own .NET Framework runtime, which is what made it easy to miss).
    /// Formatting the sign ourselves sidesteps the section-selection edge case entirely.
    /// </summary>
    internal static string Signed(double v, string absFormat, System.Globalization.CultureInfo ci)
        => (v < 0 ? "-" : "+") + Math.Abs(v).ToString(absFormat, ci);

    public string Build(string caliberName, string powderName)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.Append("GRT Sensitivity Lab -- what if (measured-value substitution)\r\n");
        sb.Append($"{caliberName}, {powderName}\r\n\r\n");
        if (BaselineMv is { } bmv)
            sb.Append(FormattableString.Invariant($"File as-is: MV {bmv:0.0} m/s") +
                      (BaselinePmax is { } bpm ? FormattableString.Invariant($", Pmax {bpm:0} {PmaxUnit}") : "") + "\r\n\r\n");
        sb.Append("Parameter                              File value   Measured    MV m/s   dMV      Pmax     dPmax\r\n");
        sb.Append(new string('-', 100)).Append("\r\n");
        foreach (var p in Points)
        {
            double? dmv = p.MvMps is { } mv && BaselineMv is { } b ? mv - b : null;
            double? dpm = p.Pmax is { } pm && BaselinePmax is { } bp ? pm - bp : null;
            sb.Append(string.Format(ci, "{0,-38} {1,-12} {2,-11} {3,7} {4,8} {5,8} {6,8}\r\n",
                p.Param.Label.Length > 38 ? p.Param.Label[..38] : p.Param.Label,
                p.FileValue is { } fv ? fv.ToString("0.###", ci) : "-",
                p.MeasuredValue.ToString("0.###", ci),
                p.MvMps is { } m ? m.ToString("0.0", ci) : "-",
                dmv is { } d ? Signed(d, "0.0", ci) : "-",
                p.Pmax is { } pp ? pp.ToString("0", ci) : "-",
                dpm is { } dp ? Signed(dp, "0", ci) : "-"));
        }
        sb.Append("\r\nEach row replaces the file's own value for that one input with a real\r\n");
        sb.Append("measurement and re-simulates -- it shows what GRT predicts for the barrel you\r\n");
        sb.Append("actually have, one input at a time, not a hypothetical +/- tolerance.\r\n");
        return sb.ToString();
    }
}
