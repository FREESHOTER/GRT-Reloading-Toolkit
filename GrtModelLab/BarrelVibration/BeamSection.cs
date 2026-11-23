namespace GrtModelLab.BarrelVibration;

/// <summary>
/// Cross-section properties for a hollow cylinder (a barrel's bore removed from its outer
/// diameter), shared by V1 (BeamModel, one equivalent uniform section) and V2 (BeamFem, one
/// section per element) so both ever compute I and A the identical way — if they didn't, V2's
/// uniform-profile case wouldn't exactly reproduce V1's closed form, and a real modeling gap
/// would be indistinguishable from an arithmetic mismatch between the two.
/// </summary>
internal static class BeamSection
{
    /// <summary>(momentOfInertia [m^4], area [m^2]) for an annulus of the given diameters in mm.
    /// Reduces to a solid rod's I/A when <paramref name="boreDiameterMm"/> is 0, no special-casing
    /// needed.</summary>
    public static (double MomentOfInertia, double Area) Annulus(double outerDiameterMm, double boreDiameterMm)
    {
        double od = outerDiameterMm / 1000.0; // mm -> m
        double id = boreDiameterMm / 1000.0;
        double momentOfInertia = Math.PI / 64.0 * (Math.Pow(od, 4) - Math.Pow(id, 4));
        double area = Math.PI / 4.0 * (od * od - id * id);
        return (momentOfInertia, area);
    }
}
