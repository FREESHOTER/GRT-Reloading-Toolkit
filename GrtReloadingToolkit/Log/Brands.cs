namespace GrtReloadingToolkit.Log;

/// <summary>
/// The makers a shooter is most likely to be holding, offered per kind so the brand box is a pick
/// rather than a retype. The same convenience <see cref="Money"/> gives the currency box, and the
/// same rule applies: the box stays typeable, because this is a shortlist and not a whitelist —
/// a wildcat barrel from a one-man shop has to be enterable.
/// </summary>
public static class Brands
{
    private static readonly string[] Powder =
    {
        "Hodgdon", "IMR", "Alliant", "Vihtavuori", "Accurate", "Ramshot",
        "Norma", "Lovex", "Vectan", "Shooters World", "Winchester",
    };

    private static readonly string[] Primer =
    {
        "CCI", "Federal", "Winchester", "Remington", "Fiocchi",
        "Sellier & Bellot", "RWS", "Murom", "Ginex",
    };

    private static readonly string[] Brass =
    {
        "Lapua", "Alpha Munitions", "Peterson", "Starline", "ADG", "Norma",
        "Hornady", "Winchester", "Federal", "Remington", "RWS", "Sako",
    };

    private static readonly string[] Bullet =
    {
        "Berger", "Hornady", "Sierra", "Nosler", "Barnes", "Lapua", "Speer",
        "Cutting Edge", "Blackjack", "Lehigh", "Norma", "Swift",
    };

    private static readonly string[] Barrel =
    {
        "Bartlein", "Krieger", "Proof Research", "Benchmark", "Brux", "Hart",
        "Shilen", "Lilja", "Rock Creek", "Douglas", "McGowen", "Lothar Walther",
        "Pac-Nor", "Border Barrels",
    };

    /// <summary>Suggestions for this kind, alphabetical within the list as written.</summary>
    public static IReadOnlyList<string> For(ComponentKind kind) => kind switch
    {
        ComponentKind.Powder => Powder,
        ComponentKind.Primer => Primer,
        ComponentKind.Brass => Brass,
        ComponentKind.Bullet => Bullet,
        ComponentKind.Barrel => Barrel,
        _ => Array.Empty<string>(),
    };
}
