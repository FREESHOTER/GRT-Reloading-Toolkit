using System.Globalization;
using System.Text.Json;

namespace GrtReloadingToolkit.Cartridges;

/// <summary>One piece read from a weighing session: where it sat in the tray and what was measured of it.</summary>
public sealed record WeighedPiece(int Number, int? Tray, int? Hole, string HoleName, bool Rejected, string Note,
    IReadOnlyDictionary<string, double> Measures, IReadOnlyDictionary<string, string> MeasureNotes);

/// <summary>A whole weighing session of PesataOgive (bullets) or PesataBossoli (cases, primers), as the archive will take it.</summary>
public sealed class WeighingBatch
{
    /// <summary>"bullet", "case" or "primer": the kind of piece the tray held.</summary>
    public required string PieceKind { get; init; }
    /// <summary>"PesataOgive" or "PesataBossoli".</summary>
    public required string Program { get; init; }
    public required string Name { get; init; }
    public string Caliber { get; init; } = "";
    public DateTime Created { get; init; }
    /// <summary>What tells this session from every other: kind, name and the moment it was created. A session read again has the same one.</summary>
    public required string Identity { get; init; }
    public required IReadOnlyList<WeighedPiece> Pieces { get; init; }
    /// <summary>Things the shooter should know about the data (relative measures, holes with nothing measured).</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Reads the session files the two weighing programs save (<c>Documenti\Pesata ogive\*.json</c> and the like) without touching them. The programs
/// keep working on their own, on the bench laptop, with their own files; the Banco takes what they measured into the archive when the shooter asks.
/// Weights are in grains and lengths in millimetres, as in the files; a length the program showed in inches is already stored in millimetres.
/// </summary>
public static class WeighingReader
{
    public static WeighingBatch Read(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new InvalidDataException(Tr.T("This is not a weighing session file.") + " " + ex.Message); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException(Tr.T("This is not a weighing session file."));
            if (Has(root, "Cases") && Has(root, "Passes")) return ReadCases(root);
            if (Has(root, "Pieces") && Has(root, "Mode")) return ReadBullets(root);
            throw new InvalidDataException(Tr.T("This is not a weighing session file."));
        }
    }

    // ---- bullets (PesataOgive) --------------------------------------------------------------------------------------

    private static WeighingBatch ReadBullets(JsonElement root)
    {
        string name = Str(root, "Name"), mode = Str(root, "Mode");
        int cols = Int(root, "TrayCols") ?? 10;
        bool oal = Bool(root, "Oal");
        var cal = root.TryGetProperty("Bto", out var c) && c.ValueKind == JsonValueKind.Object ? c : (JsonElement?)null;
        bool relative = cal is { } cc && Bool(cc, "Relative");
        double width = root.TryGetProperty("Lots", out var l) ? Num(l, "WidthGn") ?? 0.5 : 0.5;
        double anchor = root.TryGetProperty("Lots", out var l2) ? Num(l2, "AnchorGn") ?? 0 : 0;

        var warnings = new List<string>();
        var pieces = new List<WeighedPiece>();
        int withoutLength = 0;
        foreach (var p in root.GetProperty("Pieces").EnumerateArray())
        {
            int n = Int(p, "N") ?? pieces.Count + 1;
            double? w = Num(p, "WeightGn");
            if (w is null) continue;
            var m = new Dictionary<string, double> { ["bullet_weight_gr"] = w.Value };
            var notes = new Dictionary<string, string>();
            if (Num(p, "BtoMm") is { } bto)
            {
                string key = oal ? "bullet_length_mm" : "bullet_bto_mm";
                m[key] = bto;
                if (relative) notes[key] = Tr.T("relative to the sample");
            }
            else withoutLength++;
            bool reject = Bool(p, "Reject");
            int? tray = Int(p, "Tray"), hole = Int(p, "Hole");
            string holeName = hole is { } h ? HoleName(h, cols) : "";
            string note;
            if (mode == "Vassoio") note = tray is { } t ? Tr.F("tray {0}, hole {1}", t, holeName) : "";
            else note = Int(p, "Lot") is { } lot ? Tr.F("weight range {0}", Range(anchor, width, lot)) : (reject ? "" : "");
            if (reject) note = (note.Length > 0 ? note + "; " : "") + Tr.T("set aside (outside the weight limits)");
            pieces.Add(new WeighedPiece(n, tray, hole, holeName, reject, note, m, notes));
        }
        if (relative) warnings.Add(Tr.T("The lengths are relative to the sample piece: they can be compared inside this session only."));
        if (withoutLength > 0 && withoutLength < pieces.Count) warnings.Add(Tr.F("{0} pieces have no length.", withoutLength));
        return new WeighingBatch
        {
            PieceKind = "bullet", Program = "PesataOgive", Name = name, Caliber = Str(root, "Caliber"), Created = Date(root, "Created"),
            Identity = "bullet|" + name + "|" + Str(root, "Created"), Pieces = pieces, Warnings = warnings,
        };
    }

    // ---- cases, primers and neck tension (PesataBossoli) -----------------------------------------------------------

    private static WeighingBatch ReadCases(JsonElement root)
    {
        string name = Str(root, "Name");
        int cols = Int(root, "TrayCols") ?? 10;
        var passes = root.GetProperty("Passes").EnumerateArray().Select(p => p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : p.ToString()).ToList();
        bool primers = passes.Count == 1 && passes[0] == "Innesco";
        string kind = primers ? "primer" : "case";

        bool Rel(string prop) => root.TryGetProperty(prop, out var c) && c.ValueKind == JsonValueKind.Object && Bool(c, "Relative");
        bool relShoulder = Rel("Shoulder"), relLength = Rel("CaseLength"), relPrimer = Rel("Primer"), relNeck = Rel("Neck");

        var warnings = new List<string>();
        var pieces = new List<WeighedPiece>();
        int empty = 0;
        foreach (var c in root.GetProperty("Cases").EnumerateArray())
        {
            int hole = Int(c, "Hole") ?? pieces.Count;
            var m = new Dictionary<string, double>();
            var notes = new Dictionary<string, string>();
            void Put(string key, double? v, bool relative)
            {
                if (v is not { } x) return;
                m[key] = x;
                if (relative) notes[key] = Tr.T("relative to the sample");
            }
            double? emptyGn = Num(c, "EmptyGn"), fullGn = Num(c, "FullGn");
            Put("case_weight_gr", emptyGn, false);
            Put("case_shoulder_mm", Num(c, "ShoulderMm"), relShoulder);
            if (emptyGn is { } e && fullGn is { } f) Put("case_capacity_gn", f - e, false);
            Put("case_length_mm", Num(c, "LengthMm"), relLength);
            Put("primer_height_mm", Num(c, "PrimerMm"), relPrimer);
            double? neckEmpty = Num(c, "NeckEmptyMm"), neckLoaded = Num(c, "NeckLoadedMm");
            Put("case_neck_diameter_mm", neckEmpty, relNeck);
            Put("loaded_neck_diameter_mm", neckLoaded, relNeck);
            if (neckEmpty is { } ne && neckLoaded is { } nl) Put("neck_tension_mm", nl - ne, false);
            if (m.Count == 0) { empty++; continue; }
            pieces.Add(new WeighedPiece(hole + 1, 1, hole, HoleName(hole, cols), false, Tr.F("hole {0}", HoleName(hole, cols)), m, notes));
        }
        var relative = new List<string>();
        if (relShoulder && passes.Contains("Spalla")) relative.Add(Tr.T("shoulder"));
        if (relLength && passes.Contains("Lunghezza")) relative.Add(Tr.T("length"));
        if (relPrimer && primers) relative.Add(Tr.T("primer height"));
        if (relNeck && (passes.Contains("ColloVuoto") || passes.Contains("ColloCarico"))) relative.Add(Tr.T("neck diameter"));
        if (relative.Count > 0)
            warnings.Add(Tr.F("Relative to the sample piece, comparable only inside this session: {0}.", string.Join(", ", relative)));
        if (empty > 0) warnings.Add(Tr.F("{0} holes have nothing measured and were left out.", empty));
        return new WeighingBatch
        {
            PieceKind = kind, Program = "PesataBossoli", Name = name, Caliber = Str(root, "Caliber"), Created = Date(root, "Created"),
            Identity = kind + "|" + name + "|" + Str(root, "Created"), Pieces = pieces, Warnings = warnings,
        };
    }

    // ---- small helpers -----------------------------------------------------------------------------------------------

    /// <summary>The hole of the tray as the programs write it: row letter, then the column (A1 … J10).</summary>
    public static string HoleName(int hole, int columns)
    {
        string letters = "";
        int i = hole / columns + 1;
        while (i > 0) { i--; letters = (char)('A' + i % 26) + letters; i /= 26; }
        return letters + (hole % columns + 1).ToString(CultureInfo.InvariantCulture);
    }

    private static string Range(double anchor, double width, int index)
    {
        static bool OneDecimal(double v) => Math.Abs(v * 10 - Math.Round(v * 10)) < 1e-6;
        string f = OneDecimal(width) && OneDecimal(anchor) ? "0.0" : "0.00";
        return (anchor + index * width).ToString(f, CultureInfo.InvariantCulture) + "–" + (anchor + (index + 1) * width).ToString(f, CultureInfo.InvariantCulture);
    }

    private static bool Has(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Array or JsonValueKind.String or JsonValueKind.Object;
    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static int? Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : null;
    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d) ? d : null;
    private static DateTime Date(JsonElement e, string name) =>
        DateTime.TryParse(Str(e, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue;
}
