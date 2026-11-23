namespace GrtModelLab.BarrelProfile;

/// <summary>
/// Typical elastic modulus / density for common barrel steels — the two material properties GRT
/// never asks for but a barrel vibration model needs. "Custom" lets the user override both by hand
/// (a maker's datasheet, a composite/carbon-wrapped barrel, etc.).
/// </summary>
public enum SteelType { ChromeMoly, Stainless, ChromeLined, Custom }

public sealed record SteelProperties(string Label, double ElasticModulusGPa, double DensityKgM3);

public static class SteelLibrary
{
    public static readonly IReadOnlyDictionary<SteelType, SteelProperties> Known = new Dictionary<SteelType, SteelProperties>
    {
        [SteelType.ChromeMoly] = new("Chrome-Moly (4140 / 4150)", 205, 7850),
        [SteelType.Stainless] = new("Stainless (416R / 17-4 PH)", 200, 7750),
        [SteelType.ChromeLined] = new("Chrome-lined Chrome-Moly", 205, 7850),
        [SteelType.Custom] = new("Custom / other (enter your own)", 205, 7850),
    };
}

/// <summary>One measured point on the barrel's outer-diameter profile, distance from the breech face.</summary>
public sealed class BarrelProfilePoint
{
    public double PositionMm { get; set; }
    public double OuterDiameterMm { get; set; }
}

/// <summary>
/// The physical data a barrel vibration model needs and GRT has no field for: material, and the
/// outer-diameter profile along the barrel's length. Bore diameter is a single value for now
/// (matches GRT's own <c>Dz</c> — group diameter, constant along the bore in this first version).
/// </summary>
public sealed class BarrelProfileRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string SteelLabel { get; set; } = "";
    public double ElasticModulusGPa { get; set; }
    public double DensityKgM3 { get; set; }
    public double BoreDiameterMm { get; set; }
    public double LengthMm { get; set; }
    public List<BarrelProfilePoint> Points { get; } = new();
}
