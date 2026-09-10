namespace GrtReloadingToolkit.Athlon;

/// <summary>Per-shot metadata carried from the Athlon export into GRT shot notes.</summary>
internal sealed class ShotMeta
{
    public double VelocityMps { get; init; }
    public string? Time { get; init; }
    public string? Note { get; init; }
}

/// <summary>One parsed Athlon Rangecraft export = one chronograph string.</summary>
internal sealed class AthlonString
{
    public required string SourceFile { get; init; }
    public string FileName => Path.GetFileName(SourceFile);

    public List<ShotMeta> Shots { get; } = new();
    public IEnumerable<double> Velocities => Shots.Select(s => s.VelocityMps);

    /// <summary>"mps" or "fps" as declared by the velocity column header.</summary>
    public string SourceUnit { get; set; } = "mps";

    // values read straight from the Athlon summary block (already in file unit)
    public double? DeviceAvg { get; set; }
    public double? DeviceSd { get; set; }
    public double? DeviceEs { get; set; }
    public string? DevicePowerFactor { get; set; }
    public string? DeviceEnergy { get; set; }

    public string? SessionNote { get; set; }
    public double? ChargeGrains { get; set; }

    /// <summary>Session temperature in °C (converted from the export's °F field) if present.</summary>
    public double? SessionTempC { get; set; }

    public List<string> Warnings { get; } = new();
}
