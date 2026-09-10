using System.Text.RegularExpressions;
using GrtPluginKit.Util;
using GrtPluginKit.Import;
using GrtPluginKit.Analysis;

namespace GrtReloadingToolkit.Athlon;

/// <summary>
/// Deterministic parser for chronograph exports (.xlsx / .csv) — Athlon Rangecraft
/// Velocity Pro and Garmin Xero C1 Pro / ShotView both fit this shape:
///   - an optional summary block of "label | value" rows (average / SD / ES / session note / temp)
///   - a shot-table header row: a velocity column ("speed"/"velocity"/"(m/s)"/"(fps)") plus at
///     least one other data column (#, shot, PF, KE, time, …)
///   - shot rows, usually starting with an integer shot index; a trailing summary footer is stopped at
///   - unit is taken from the header if it says so, else inferred from the velocity magnitude
/// </summary>
internal static class AthlonParser
{
    private const double VSaneLo = 100, VSaneHi = 5200;     // unit-agnostic (covers m/s and ft/s)
    private const double FpsToMps = 1.0 / 3.280839895;

    private static readonly string[] VelWords =
        { "velocit", "velocity", "speed", "geschwindigk", "vitesse", "velocidad", "snelheid", "v0", "vo" };
    private static readonly string[] HeaderMates =    // other columns that confirm a real shot table
        { "#", "shot", "no", "nr", "pf", "power", "factor", "ke", "energy", "time", "dev", "avg", "note", "cold", "clean" };
    private static readonly string[] ShotColWords = { "#", "shot", "no", "no.", "nr", "nr.", "colpo", "tir" };
    private static readonly string[] TimeWords = { "tempo", "time", "zeit", "heure", "hora", "ora" };
    private static readonly string[] NoteWords = { "note di scatto", "shot note", "note", "notes", "bemerkung", "anmerkung", "comment" };
    private static readonly string[] AvgWords = { "medi", "average", "avg", "mittel", "moyenne", "promedio", "mean" };

    // Summary-block labels (specific phrases only — short tokens like "es"/"sd"
    // matched too much, e.g. "es" inside "peso del proiettile").
    private static readonly string[] PfWords = { "fattore di potenza", "power factor", "facteur de puissance" };
    private static readonly string[] KeWords = { "energia cinetica", "kinetic energy", "energie cinetique" };
    private static readonly string[] SessionNoteWords = { "appunti della sessione", "session note", "note della sessione", "notizen" };
    private static readonly string[] TempWords = { "temperatura", "temperature", "temperatur" };

    public static AthlonString Parse(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        List<string[]> rows = ext is ".csv" or ".tsv" or ".txt"
            ? DelimitedReader.ReadRows(path)
            : XlsxReader.ReadFirstSheet(path);
        var result = new AthlonString { SourceFile = path };

        int headerRow = FindHeaderRow(rows);
        if (headerRow < 0)
            throw new InvalidDataException($"{Path.GetFileName(path)}: could not locate the shot table (no velocity column header).");

        string[] header = rows[headerRow];
        int velCol = IndexOfHeader(header, h => GrtPluginKit.Util.Str.ContainsAny(h, VelWords) || h.Contains("(mps)") || h.Contains("(fps)") || h.Contains("(m/s)"));
        int timeCol = IndexOfHeader(header, h => GrtPluginKit.Util.Str.ContainsAny(h, TimeWords));
        int noteCol = IndexOfHeader(header, h => GrtPluginKit.Util.Str.ContainsAny(h, NoteWords) && !GrtPluginKit.Util.Str.ContainsAny(h, "dev", "media", "mean"));
        int shotCol = IndexOfHeader(header, h => GrtPluginKit.Util.Str.ContainsAny(h, ShotColWords) && !GrtPluginKit.Util.Str.ContainsAny(h, "note", "shoot"));
        if (shotCol < 0 && velCol != 0) shotCol = 0;   // Athlon: shot index is column 0

        // collect candidate shots first (unit-agnostic), then decide fps vs m/s
        var raw = new List<(double v, string? time, string? note)>();
        for (int r = headerRow + 1; r < rows.Count; r++)
        {
            string[] row = rows[r];
            if (shotCol >= 0)
            {
                double? shotNo = GrtPluginKit.Util.Str.ParseNumber(Cell(row, shotCol));
                bool isShotRow = shotNo is >= 0.5 and < 10000 && Math.Abs(shotNo.Value - Math.Round(shotNo.Value)) < 1e-6;
                if (!isShotRow) { if (raw.Count > 0) break; else continue; }
            }

            double? v = GrtPluginKit.Util.Str.ParseNumber(Cell(row, velCol));
            if (v is null) { if (raw.Count > 0) break; else continue; }
            if (v < VSaneLo || v > VSaneHi)
            {
                if (raw.Count > 0) break;              // trailing footer value
                result.Warnings.Add($"row {r + 1}: velocity '{Cell(row, velCol)}' out of range, skipped");
                continue;
            }
            raw.Add((v.Value,
                     timeCol >= 0 ? NullIfBlank(Cell(row, timeCol)) : null,
                     noteCol >= 0 ? NullIfBlank(Cell(row, noteCol)) : null));
        }

        if (raw.Count == 0)
            throw new InvalidDataException($"{Path.GetFileName(path)}: shot table found but no valid velocities in it.");

        result.SourceUnit = DetectUnit(header, velCol, raw.Select(x => x.v), result.Warnings);
        double k = result.SourceUnit == "fps" ? FpsToMps : 1.0;
        foreach (var s in raw)
            result.Shots.Add(new ShotMeta { VelocityMps = s.v * k, Time = s.time, Note = s.note });

        ReadSummaryBlock(rows, headerRow, result);
        result.ChargeGrains = GuessChargeGrains(result.SessionNote, path);

        if (result.SourceUnit == "fps")
            result.Warnings.Add("velocity is in fps — converted to m/s for GRT");

        return result;
    }

    private static int FindHeaderRow(List<string[]> rows)
    {
        // Pass 1: a cell carrying the unit in parentheses — unambiguous.
        for (int r = 0; r < rows.Count; r++)
            foreach (string cell in rows[r])
            {
                string h = GrtPluginKit.Util.Str.Normalize(cell);
                if (h.Contains("(mps)") || h.Contains("(fps)") || h.Contains("(m/s)"))
                    return r;
            }

        // Pass 2: a real table header — a velocity column AND at least one other data-column
        // header in the same row (a 2-column summary line "Velocità media | 806.6" must not qualify).
        for (int r = 0; r < rows.Count; r++)
        {
            var cells = rows[r].Select(GrtPluginKit.Util.Str.Normalize).ToArray();
            bool hasVel = cells.Any(c => GrtPluginKit.Util.Str.ContainsAny(c, VelWords)
                                         || c.Contains("(mps)") || c.Contains("(fps)") || c.Contains("(m/s)"));
            if (!hasVel) continue;
            int mates = cells.Count(c => c.Length > 0 && GrtPluginKit.Util.Str.ContainsAny(c, HeaderMates));
            if (mates >= 1) return r;
        }
        return -1;
    }

    private static int IndexOfHeader(string[] header, Func<string, bool> match)
    {
        for (int c = 0; c < header.Length; c++)
            if (match(GrtPluginKit.Util.Str.Normalize(header[c])))
                return c;
        return -1;
    }

    private static string DetectUnit(string[] header, int velCol, IEnumerable<double> velocities, List<string> warnings)
    {
        string h = velCol >= 0 && velCol < header.Length ? GrtPluginKit.Util.Str.Normalize(header[velCol]) : "";
        foreach (string cell in header)
        {
            string n = GrtPluginKit.Util.Str.Normalize(cell);
            if (n.Contains("fps") || n.Contains("ft/s") || n.Contains("f/s")) return "fps";
            if (n.Contains("(m/s)") || n.Contains("(mps)") || n.Contains(" mps") || n.EndsWith("mps")) return "mps";
        }
        if (h.Contains("fps") || h.Contains("ft/s")) return "fps";
        if (h.Contains("m/s") || h.Contains("mps")) return "mps";

        // no unit in the header — infer from magnitude. No small-arms load is 1400 m/s;
        // fps rifle rounds sit ~2000-3500, m/s ~250-1200.
        var v = velocities.ToList();
        double median = v.Count == 0 ? 0 : v.OrderBy(x => x).ElementAt(v.Count / 2);
        string guess = median >= 1400 ? "fps" : "mps";
        warnings.Add($"no unit in the header — assuming {(guess == "fps" ? "fps" : "m/s")} from a median of {median:0} (override by adding the unit to the file if wrong)");
        return guess;
    }

    private static void ReadSummaryBlock(List<string[]> rows, int headerRow, AthlonString result)
    {
        for (int r = 0; r < rows.Count; r++)
        {
            if (r == headerRow) continue;
            string[] row = rows[r];
            if (row.Length < 2) continue;

            string label = GrtPluginKit.Util.Str.Normalize(row[0]);
            string value = row.Length > 1 ? row[1] : "";
            if (label.Length == 0) continue;

            bool isVelLabel = GrtPluginKit.Util.Str.ContainsAny(label, VelWords);
            bool isAvgLabel = GrtPluginKit.Util.Str.ContainsAny(label, AvgWords)
                              && !GrtPluginKit.Util.Str.ContainsAny(label, "delta", "avg dev", "std", "abweich");
            // "Velocità media 806.6" (Athlon) or a bare "AVERAGE 806.6" (Garmin), but not "Δ AVG"
            if (result.DeviceAvg is null && (isVelLabel && isAvgLabel || isAvgLabel && GrtPluginKit.Util.Str.ParseNumber(value) is >= 60))
                result.DeviceAvg = GrtPluginKit.Util.Str.ParseNumber(value);
            else if (result.DeviceSd is null && GrtPluginKit.Util.Str.ContainsAny(label, "deviazione standard", "standard deviation", "std dev", "abweichung", "ecart type"))
                result.DeviceSd = GrtPluginKit.Util.Str.ParseNumber(value);
            else if (result.DeviceEs is null && GrtPluginKit.Util.Str.ContainsAny(label, "diffusione estrema", "extreme spread", "spread", "estrema"))
                result.DeviceEs = GrtPluginKit.Util.Str.ParseNumber(value);
            else if (result.DevicePowerFactor is null && GrtPluginKit.Util.Str.ContainsAny(label, PfWords))
                result.DevicePowerFactor = NullIfBlank(value);
            else if (result.DeviceEnergy is null && GrtPluginKit.Util.Str.ContainsAny(label, KeWords))
                result.DeviceEnergy = NullIfBlank(value);
            else if (result.SessionNote is null && GrtPluginKit.Util.Str.ContainsAny(label, SessionNoteWords))
                result.SessionNote = NullIfBlank(value);
            else if (result.SessionTempC is null && GrtPluginKit.Util.Str.ContainsAny(label, TempWords) && GrtPluginKit.Util.Str.ParseNumber(value) is { } t)
            {
                bool fahrenheit = label.Contains("f°") || label.Contains("°f") || label.EndsWith(" f") || value.Contains('F');
                result.SessionTempC = fahrenheit ? GrtPluginKit.Util.Str.FahrenheitToCelsius(t) : t;
            }
        }
    }

    private static double? GuessChargeGrains(string? sessionNote, string path)
    {
        // e.g. "carica 39.2" / "charge 41,5 gr" / "load: 44"
        if (!string.IsNullOrWhiteSpace(sessionNote))
        {
            Match m = Regex.Match(sessionNote, @"(?:caric[ao]|charge|load|carga|ladung)\s*[:=]?\s*([0-9]+(?:[.,][0-9]+)?)",
                RegexOptions.IgnoreCase);
            if (m.Success && GrtPluginKit.Util.Str.ParseNumber(m.Groups[1].Value) is { } g) return g;

            // some sessions just put the number, e.g. "39.6"
            if (Regex.IsMatch(sessionNote.Trim(), @"^[0-9]{1,3}([.,][0-9]{1,2})?$") && GrtPluginKit.Util.Str.ParseNumber(sessionNote) is { } g2)
                return g2;
        }
        // fall back to the file name, e.g. "39.20.xlsx"
        Match f = Regex.Match(Path.GetFileNameWithoutExtension(path), @"([0-9]{1,3}[.,][0-9]{1,2})");
        return f.Success ? GrtPluginKit.Util.Str.ParseNumber(f.Groups[1].Value) : null;
    }

    private static string Cell(string[] row, int col) => col >= 0 && col < row.Length ? row[col] ?? "" : "";
    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
