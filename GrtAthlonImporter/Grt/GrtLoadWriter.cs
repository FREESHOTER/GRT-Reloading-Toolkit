using System.Globalization;
using System.Text;
using System.Xml;
using GrtAthlonImporter.Core;

namespace GrtAthlonImporter.Grt;

internal sealed class GrtLoadWriteResult
{
    public required string OutputPath { get; init; }
    public required bool MergedIntoBase { get; init; }
    public List<string> Notes { get; } = new();
}

internal sealed class WriteOptions
{
    /// <summary>Measurement title (a date is normally already appended by the caller).</summary>
    public string Title { get; init; } = "Athlon Measurement";

    /// <summary>Drop any earlier "Athlon …" Measurement from the base before appending.</summary>
    public bool ReplacePreviousAthlon { get; init; } = true;

    /// <summary>Where to write when there is no base load. Required in that case.</summary>
    public string? MinimalOutputPath { get; init; }
}

/// <summary>The Athlon "Measurement" titles this tool writes all start with this (pre-encoding).</summary>
internal static class AthlonMarker
{
    public const string TitlePrefix = "Athlon ";
}

/// <summary>
/// Writes the imported strings into a .grtload file. When a base file is given
/// (the load currently open in GRT) the new &lt;Measurement&gt; is appended to its
/// &lt;appendix&gt; and everything else is preserved verbatim. Otherwise a minimal
/// file is produced (with a caller-visible warning).
/// </summary>
internal static class GrtLoadWriter
{
    private const double GrainToKg = 0.00006479891;

    public static double GrainsToKg(double grains) => grains * GrainToKg;

    public static GrtLoadWriteResult Write(string? basePath, IReadOnlyList<ChargeInput> charges, WriteOptions opt)
    {
        if (!string.IsNullOrWhiteSpace(basePath) && File.Exists(basePath))
            return MergeIntoBase(basePath!, charges, opt);

        if (string.IsNullOrWhiteSpace(opt.MinimalOutputPath))
            throw new InvalidOperationException("No base load and no output path was chosen.");
        return WriteMinimal(charges, opt.Title, opt.MinimalOutputPath!);
    }

    /// <summary>True if the path looks like a file THIS tool generated (avoid importing into our own output).</summary>
    public static bool LooksLikeGeneratedSibling(string? path)
        => !string.IsNullOrEmpty(path) &&
           System.Text.RegularExpressions.Regex.IsMatch(
               Path.GetFileName(path), @"_athlon_\d{8}_\d{4}\.grtload$|^Athlon_Import_", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Reads the base file's caliber name (empty / "Generic" ⇒ probably not a real load).</summary>
    public static string ReadCaliberName(string path)
    {
        try
        {
            var doc = new XmlDocument();
            doc.Load(path);
            foreach (XmlElement e in doc.SelectNodes("//caliber/input")!.OfType<XmlElement>())
                if (e.GetAttribute("name") == "CaliberName")
                    return Uri.UnescapeDataString(e.GetAttribute("value"));
        }
        catch { }
        return "";
    }

    private static GrtLoadWriteResult MergeIntoBase(string basePath, IReadOnlyList<ChargeInput> charges, WriteOptions opt)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.Load(basePath);

        XmlElement? inner = doc.DocumentElement?["InnerBallistikInput"];
        if (doc.DocumentElement?.Name != "GordonsReloadingTool" || inner == null)
            throw new InvalidDataException("Base file is not a recognisable .grtload (no <InnerBallistikInput>).");

        XmlElement appendix;
        if (inner["appendix"] is { } existing)
        {
            appendix = existing;
        }
        else
        {
            appendix = doc.CreateElement("appendix");
            inner.AppendChild(doc.CreateWhitespace("\n    "));
            inner.AppendChild(appendix);
            inner.AppendChild(doc.CreateWhitespace("\n  "));
        }

        int removed = 0;
        if (opt.ReplacePreviousAthlon)
        {
            string encPrefix = Enc(AthlonMarker.TitlePrefix);
            foreach (XmlElement m in appendix.ChildNodes.OfType<XmlElement>()
                         .Where(e => e.Name == "Measurement" && e.GetAttribute("title").StartsWith(encPrefix, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                while (m.PreviousSibling is XmlWhitespace ws) ws.ParentNode!.RemoveChild(ws);
                appendix.RemoveChild(m);
                removed++;
            }
        }

        int index = NextAppendixIndex(appendix);
        string uniqueTitle = UniqueTitle(appendix, Enc(opt.Title));

        XmlElement measurement = doc.CreateElement("Measurement");
        measurement.SetAttribute("index", index.ToString(CultureInfo.InvariantCulture));
        measurement.SetAttribute("showinreport", "true");
        measurement.SetAttribute("title", uniqueTitle);
        AppendCharges(doc, measurement, charges, indent: "        ");

        appendix.AppendChild(doc.CreateWhitespace("  "));
        appendix.AppendChild(measurement);
        appendix.AppendChild(doc.CreateWhitespace("\n    "));

        string outPath = SiblingOutPath(basePath);
        Save(doc, outPath);

        var res = new GrtLoadWriteResult { OutputPath = outPath, MergedIntoBase = true };
        if (removed > 0)
            res.Notes.Add($"replaced {removed} earlier \"Athlon …\" Measurement(s)");
        res.Notes.Add($"appended <Measurement title=\"{Uri.UnescapeDataString(uniqueTitle)}\"> " +
                      $"with {charges.Count} charge(s); the rest of the load is unchanged");
        return res;
    }

    private static GrtLoadWriteResult WriteMinimal(IReadOnlyList<ChargeInput> charges, string title, string outPath)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n" +
            "<GordonsReloadingTool version=\"2021.2030-NIGHTLY\">\n" +
            "  <InnerBallistikInput>\n" +
            $"    <title>{Enc(title)}</title>\n" +
            "    <caliber><input name=\"CaliberName\" value=\"Generic\" /></caliber>\n" +
            "    <gun><input name=\"GunName\" value=\"Generic\" /></gun>\n" +
            "    <projectile><input name=\"ProjectileName\" value=\"Generic\" /></projectile>\n" +
            "    <propellant><input name=\"pname\" value=\"Generic\" /></propellant>\n" +
            "    <appendix>\n    </appendix>\n" +
            "  </InnerBallistikInput>\n" +
            "</GordonsReloadingTool>");

        var appendix = (XmlElement)doc.SelectSingleNode("//appendix")!;
        XmlElement measurement = doc.CreateElement("Measurement");
        measurement.SetAttribute("index", "1");
        measurement.SetAttribute("showinreport", "true");
        measurement.SetAttribute("title", Enc(title));
        AppendCharges(doc, measurement, charges, indent: "        ");
        appendix.PrependChild(measurement);
        appendix.InsertBefore(NL(doc, "\n      "), measurement);

        Save(doc, outPath);

        var res = new GrtLoadWriteResult { OutputPath = outPath, MergedIntoBase = false };
        res.Notes.Add("no saved load was open in GRT — wrote a stand-alone file with a Generic caliber/gun/projectile/propellant");
        return res;
    }

    private static void AppendCharges(XmlDocument doc, XmlElement measurement, IReadOnlyList<ChargeInput> charges, string indent)
    {
        foreach (ChargeInput c in charges)
        {
            XmlElement charge = doc.CreateElement("charge");
            charge.SetAttribute("name", Enc(c.Name));
            charge.SetAttribute("showinreport", "true");
            charge.SetAttribute("expanded", "true");
            charge.SetAttribute("expandedstats", "true");
            charge.SetAttribute("note", Enc(c.NoteText));
            charge.SetAttribute("value", c.ValueKg.ToString("0.000000000000", CultureInfo.InvariantCulture));
            charge.SetAttribute("menu", "");
            charge.SetAttribute("menuvalue", "");
            charge.SetAttribute("menuunit", "m");
            charge.SetAttribute("source", Enc("Athlon Rangecraft Import"));
            charge.SetAttribute("sourceoptions", "%7B%7D");

            foreach ((double vMps, string? note) in c.Shots)
            {
                charge.AppendChild(NL(doc, "\n" + indent + "  "));
                XmlElement shot = doc.CreateElement("shot");
                shot.SetAttribute("name", "");
                shot.SetAttribute("velocity", vMps.ToString("0.0", CultureInfo.InvariantCulture));
                shot.SetAttribute("pressure", "0");
                if (!string.IsNullOrEmpty(note)) shot.SetAttribute("note", Enc(note));
                charge.AppendChild(shot);
            }
            charge.AppendChild(NL(doc, "\n" + indent));

            measurement.AppendChild(NL(doc, "\n" + indent));
            measurement.AppendChild(charge);
        }
        measurement.AppendChild(NL(doc, "\n      "));
    }

    private static XmlWhitespace NL(XmlDocument doc, string s) => doc.CreateWhitespace(s);

    private static int NextAppendixIndex(XmlElement appendix)
    {
        int max = 0;
        foreach (XmlNode n in appendix.ChildNodes)
            if (n is XmlElement e && int.TryParse(e.GetAttribute("index"), out int i) && i > max)
                max = i;
        return max + 1;
    }

    private static string UniqueTitle(XmlElement appendix, string encodedBase)
    {
        bool Exists(string x) => appendix.ChildNodes.OfType<XmlElement>().Any(e => e.GetAttribute("title") == x);
        if (!Exists(encodedBase)) return encodedBase;
        for (int i = 1; i < 100; i++)
            if (!Exists($"{encodedBase}-{i}")) return $"{encodedBase}-{i}";
        return $"{encodedBase}-{DateTime.Now:HHmmss}";
    }

    private static string SiblingOutPath(string basePath)
    {
        string dir = Path.GetDirectoryName(basePath) ?? Path.GetTempPath();
        // strip a previous "_athlon_<stamp>" so re-imports don't nest the suffix
        string stem = System.Text.RegularExpressions.Regex.Replace(
            Path.GetFileNameWithoutExtension(basePath), @"_athlon_\d{8}_\d{4}$", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return Path.Combine(dir, $"{stem}_athlon_{DateTime.Now:yyyyMMdd_HHmm}.grtload");
    }

    private static void Save(XmlDocument doc, string path)
    {
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false };
        using var w = XmlWriter.Create(path, settings);
        doc.Save(w);
    }

    /// <summary>GRT stores attribute text percent-encoded (spaces as %20, etc.).</summary>
    private static string Enc(string s) => Uri.EscapeDataString(s ?? string.Empty);
}
