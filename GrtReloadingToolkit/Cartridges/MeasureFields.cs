using System.Text.RegularExpressions;

namespace GrtReloadingToolkit.Cartridges;

/// <summary>
/// The measures that can be taken of a piece (a case, a bullet, a primer) or of a loaded cartridge. The key says the unit by its suffix
/// (<c>_mm</c>, <c>_gr</c>…), and the archive keeps every value in that unit: conversions happen only on the screen. The labels are English like every other text of the program; the window translates them.
/// </summary>
public static class MeasureFields
{
    /// <summary><see cref="Owner"/> is "case", "bullet" or "primer" for a measure of a single piece, "cartridge" for one taken on the loaded round.</summary>
    public sealed record Field(string Key, string Label, string Owner);

    public const string Cartridge = "cartridge";

    public static readonly IReadOnlyList<Field> All = new Field[]
    {
        new("case_weight_gr", "Case weight", "case"),
        new("case_capacity_gn", "Case capacity (water)", "case"),
        new("case_internal_volume_ml", "Case internal volume", "case"),
        new("case_length_mm", "Case length", "case"),
        new("case_shoulder_mm", "Case shoulder", "case"),
        new("case_neck_diameter_mm", "Case neck diameter", "case"),
        new("case_neck_id_mm", "Neck inside diameter", "case"),
        new("case_neck_thickness_mm", "Neck thickness", "case"),
        new("case_head_thickness_mm", "Case head thickness", "case"),
        new("primer_pocket_mm", "Primer pocket", "case"),
        new("bullet_weight_gr", "Bullet weight", "bullet"),
        new("bullet_bto_mm", "Bullet BTO", "bullet"),
        new("bullet_bearing_mm", "Bullet bearing length", "bullet"),
        new("bullet_length_mm", "Bullet length", "bullet"),
        new("bullet_diameter_mm", "Bullet diameter", "bullet"),
        new("primer_weight_gr", "Primer weight", "primer"),
        new("primer_height_mm", "Primer height", "primer"),
        new("powder_weight_gr", "Powder charge", Cartridge),
        new("bullet_cbto_mm", "CBTO (base to ogive)", Cartridge),
        new("cartridge_oal_mm", "Cartridge OAL", Cartridge),
        new("cartridge_total_weight_gr", "Cartridge total weight", Cartridge),
        new("cartridge_concentricity_mm", "Cartridge concentricity", Cartridge),
        new("bullet_seating_force_n", "Bullet seating force", Cartridge),
        new("loaded_neck_diameter_mm", "Loaded neck diameter", Cartridge),
        new("neck_expansion_mm", "Neck expansion", Cartridge),
        new("neck_tension_mm", "Neck tension", Cartridge),
        new("primer_depth_mm", "Primer depth", Cartridge),
        new("primer_height_above_head_mm", "Primer height above the head", Cartridge),
    };

    private static readonly Dictionary<string, Field> ByKey = All.ToDictionary(f => f.Key, StringComparer.Ordinal);

    public static Field? Find(string key) => ByKey.GetValueOrDefault(key);

    public static bool IsKnown(string key) => ByKey.ContainsKey(key);

    /// <summary>"piece" owners are the three kinds of piece; anything else is a measure of the cartridge.</summary>
    public static string OwnerOf(string key) => Find(key)?.Owner ?? Cartridge;

    public static string LabelOf(string key) => Find(key)?.Label ?? key.Replace('_', ' ');

    /// <summary>The unit a field is kept in, read from the end of its key.</summary>
    public static string UnitOf(string key)
    {
        foreach (var (suffix, unit) in Units)
            if (key.EndsWith(suffix, StringComparison.Ordinal)) return unit;
        return "";
    }

    private static readonly (string Suffix, string Unit)[] Units =
    {
        ("_gr", "gr"), ("_mm", "mm"), ("_gn", "gn H2O"), ("_ml", "ml"), ("_n", "N"), ("_c", "°C"), ("_s", "s"), ("_pct", "%"), ("_hpa", "hPa"),
    };

    private static readonly Regex DigitsLetter = new(@"^(\d+)([A-Z])$", RegexOptions.Compiled);
    private static readonly Regex LetterDigits = new(@"^([A-Z])(\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// The same mark written in different ways ("1A", "A1", "a01", "1-A") as one: letter first, then the number without zeros.
    /// Anything that is not a number and a letter is only cleaned of spaces, dashes, dots and underscores and put in capitals.
    /// </summary>
    public static string NormalizeMark(string? mark)
    {
        string t = Regex.Replace((mark ?? "").ToUpperInvariant(), @"[\s\-_.]", "");
        var m = DigitsLetter.Match(t);
        if (m.Success) return m.Groups[2].Value + int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        m = LetterDigits.Match(t);
        if (m.Success) return m.Groups[1].Value + int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        return t;
    }

    /// <summary>The code as it is engraved on the case: the hole "A5" of the tray becomes "5A" (number, then letter).</summary>
    public static string EngravedCode(string? hole)
    {
        string h = hole ?? "";
        return h.Length >= 2 && char.IsLetter(h[0]) && h[1..].All(char.IsDigit) ? h[1..] + h[0] : h;
    }
}
