using System.Globalization;
using System.Xml;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Cal;

/// <summary>
/// One powder model as GRT's propellant database exports it (File → Export, or right click → Export): the
/// <c>&lt;propellantfile&gt;</c> block of a .propellant / .xml file. <see cref="ModelDate"/> is the file's
/// <c>lotid</c>, which for a model taken from GRT's own database carries the date of that model version
/// (e.g. "2021-04-18" for Vihtavuori N133).
/// </summary>
internal sealed record PowderModel(
    string Maker, string Name, string ModelDate,
    double? Ba, double? K, double? A0, double? A1, double? Z1, double? Z2, double? Qex, double? Quality,
    string SourceFile)
{
    public string Label => string.IsNullOrWhiteSpace(ModelDate) ? $"{Maker} {Name}" : $"{Maker} {Name}, {ModelDate}";
}

/// <summary>How far one model constant of a load has moved from the original powder model, in percent of the model's value.</summary>
internal sealed record ModelDeviation(string Field, double LoadValue, double ModelValue)
{
    public double Pct => ModelValue == 0 ? 0 : (LoadValue - ModelValue) / ModelValue * 100;
}

/// <summary>
/// The original powder models the user exported from GRT, kept in a folder of their own, so the calibration can say
/// how far a load's Ba, k, a0, z1 and z2 have moved from the model GRT shipped. GRT's plugin interface cannot read
/// its database, and GRT only exports records the user created or changed, so only what the user put in the folder
/// is known; everything else is reported as unknown, never guessed.
/// </summary>
internal static class PowderModels
{
    /// <summary>The folder name, inside GRT's own folder, the toolkit reads exported powders from.</summary>
    public const string FolderName = "Polveri";

    /// <summary>A calibrated Ba this far from the original model points at a wrong load input (case volume, bullet
    /// length, barrel length...) more often than at the powder itself.</summary>
    public const double LargeBaDeviationPct = 10;

    /// <summary>Every model in one exported file. A file can hold several blocks; one that is not a GRT
    /// propellant export, or not XML at all, yields none.</summary>
    public static IReadOnlyList<PowderModel> ReadFile(string path)
    {
        var doc = new XmlDocument();
        try { doc.Load(path); }
        catch (Exception) { return Array.Empty<PowderModel>(); }
        if (doc.DocumentElement?.Name != "GordonsReloadingTool") return Array.Empty<PowderModel>();

        var list = new List<PowderModel>();
        foreach (XmlElement block in doc.DocumentElement.ChildNodes.OfType<XmlElement>().Where(e => e.Name == "propellantfile"))
        {
            var vars = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XmlElement v in block.ChildNodes.OfType<XmlElement>().Where(e => e.Name == "var"))
                vars[v.GetAttribute("name")] = Uri.UnescapeDataString(v.GetAttribute("value"));
            string S(string n) => vars.TryGetValue(n, out var s) ? s.Trim() : "";
            double? D(string n) => double.TryParse(S(n), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
            if (S("pname").Length == 0) continue;
            list.Add(new PowderModel(S("mname"), S("pname"), S("lotid"),
                D("Ba"), D("k"), D("a0"), D("a1"), D("z1"), D("z2"), D("Qex"), D("Qlty"), path));
        }
        return list;
    }

    /// <summary>Every model in the .propellant and .xml files of <paramref name="folder"/>; empty when it does not exist.</summary>
    public static IReadOnlyList<PowderModel> ReadFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return Array.Empty<PowderModel>();
        return Directory.EnumerateFiles(folder)
            .Where(f => f.EndsWith(".propellant", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .SelectMany(ReadFile)
            .ToList();
    }

    /// <summary>Lower case, letters and digits only: "RS 14", "RS14" and "rs-14" are one name.</summary>
    internal static string Key(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// The model for a load's powder. The product name must match; the maker breaks a tie, because GRT's own files spell
    /// the same maker more than one way ("Vhitavuori" in a load, "Vihtavuori" in the database). Among equal candidates the
    /// newest model version wins. Null when no model has that name, or when several makers have it and none is the load's.
    /// </summary>
    public static PowderModel? Match(IEnumerable<PowderModel> models, string maker, string name)
    {
        string n = Key(name), m = Key(maker);
        if (n.Length == 0) return null;
        var byName = models.Where(x => Key(x.Name) == n).ToList();
        if (byName.Count == 0) return null;
        var byMaker = byName.Where(x => Key(x.Maker) == m).ToList();
        var pool = byMaker.Count > 0 ? byMaker
                 : byName.Select(x => Key(x.Maker)).Distinct().Count() == 1 ? byName
                 : null;
        return pool?.OrderByDescending(x => x.ModelDate, StringComparer.Ordinal).First();
    }

    /// <summary>The load's own model constants against the original model's, for the ones both have.</summary>
    public static IReadOnlyList<ModelDeviation> Compare(GrtLoadDoc load, PowderModel model)
    {
        var list = new List<ModelDeviation>();
        void Add(string field, double? modelValue)
        {
            if (modelValue is { } mv && load.InputNumber("propellant", field) is { } lv) list.Add(new ModelDeviation(field, lv, mv));
        }
        Add("Ba", model.Ba);
        Add("k", model.K);
        Add("a0", model.A0);
        Add("z1", model.Z1);
        Add("z2", model.Z2);
        return list;
    }

    /// <summary>The percentage <paramref name="value"/> lies from <paramref name="modelValue"/>; null without a usable model value.</summary>
    public static double? PctFrom(double value, double? modelValue) =>
        modelValue is { } mv && mv != 0 ? (value - mv) / mv * 100 : null;
}
