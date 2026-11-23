using System.Text.RegularExpressions;
using GrtPluginKit.Import;
using GrtPluginKit.Util;
using GrtPluginKit.Analysis;

namespace GrtOcwAnalyzer.Athlon;

/// <summary>
/// Deterministic parser for Athlon Rangecraft Velocity Pro .xlsx exports.
///
/// Layout (localised headers, fixed structure):
///   - a summary block of "label | value" rows (Velocità media / Deviazione standard / …)
///   - a shot-table header row containing "… (MPS)" / "… (FPS)" in the velocity column
///   - shot rows: [# , velocity , dev/mean , KE , power factor , time , clean bore , cold bore , shot notes]
/// </summary>
internal static class AthlonParser
{
    private const double VMinMps = 60, VMaxMps = 2000;      // sane m/s window
    private const double VMinFps = 200, VMaxFps = 6000;

    private static readonly string[] VelWords =
        { "velocit", "velocity", "speed", "geschwindigk", "vitesse", "velocidad", "snelheid" };
    private static readonly string[] TimeWords = { "tempo", "time", "zeit", "heure", "hora", "ora" };
    private static readonly string[] NoteWords = { "note di scatto", "shot note", "bemerkung", "comment" };

    // Summary-block labels (specific phrases only — short tokens like "es"/"sd"
    // matched too much, e.g. "es" inside "peso del proiettile").
    private static readonly string[] PfWords = { "fattore di potenza", "power factor", "facteur de puissance" };
    private static readonly string[] KeWords = { "energia cinetica", "kinetic energy", "energie cinetique" };
    private static readonly string[] SessionNoteWords = { "appunti della sessione", "session note", "note della sessione", "notizen" };
    private static readonly string[] TempWords = { "temperatura", "temperature", "temperatur" };

    public static AthlonString Parse(string path)
    {
        List<string[]> rows = XlsxReader.ReadFirstSheet(path);
        var result = new AthlonString { SourceFile = path };

        int headerRow = FindHeaderRow(rows);
        if (headerRow < 0)
            throw new InvalidDataException($"{Path.GetFileName(path)}: could not locate the shot table (no velocity column header).");

        string[] header = rows[headerRow];
        int velCol = IndexOfHeader(header, h => Str.ContainsAny(h, VelWords) || h.Contains("(mps)") || h.Contains("(fps)") || h.Contains("(m/s)"));
        int timeCol = IndexOfHeader(header, h => Str.ContainsAny(h, TimeWords));
        int noteCol = IndexOfHeader(header, h => Str.ContainsAny(h, NoteWords) && !Str.ContainsAny(h, "dev", "media", "mean"));
        int shotCol = 0; // Athlon: shot index is always the first column

        result.SourceUnit = DetectUnit(header, velCol);
        (double lo, double hi) = result.SourceUnit == "fps" ? (VMinFps, VMaxFps) : (VMinMps, VMaxMps);

        for (int r = headerRow + 1; r < rows.Count; r++)
        {
            string[] row = rows[r];
            // Shot index: usually 1,2,3… but some exports write it as "1.0","2.0".
            double? shotNo = Str.ParseNumber(Cell(row, shotCol));
            bool isShotRow = shotNo is >= 0.5 and < 1000 && Math.Abs(shotNo.Value - Math.Round(shotNo.Value)) < 1e-6;
            if (!isShotRow)
            {
                if (result.Shots.Count > 0) break;   // reached the end of the table
                continue;
            }

            double? v = Str.ParseNumber(Cell(row, velCol));
            if (v is null || v < lo || v > hi)
            {
                result.Warnings.Add($"row {r + 1}: velocity '{Cell(row, velCol)}' out of range, skipped");
                continue;
            }

            double vMps = result.SourceUnit == "fps" ? v.Value / 3.28084 : v.Value;
            string? time = timeCol >= 0 ? NullIfBlank(Cell(row, timeCol)) : null;
            string? note = noteCol >= 0 ? NullIfBlank(Cell(row, noteCol)) : null;
            result.Shots.Add(new ShotMeta { VelocityMps = vMps, Time = time, Note = note });
        }

        if (result.Shots.Count == 0)
            throw new InvalidDataException($"{Path.GetFileName(path)}: shot table found but no valid velocities in it.");

        ReadSummaryBlock(rows, headerRow, result);
        result.ChargeGrains = GuessChargeGrains(result.SessionNote, path);

        if (result.SourceUnit == "fps")
            result.Warnings.Add("velocity column is in FPS — converted to m/s for GRT");

        return result;
    }

    private static int FindHeaderRow(List<string[]> rows)
    {
        // Pass 1: a cell carrying the unit in parentheses — unambiguous.
        for (int r = 0; r < rows.Count; r++)
            foreach (string cell in rows[r])
            {
                string h = Str.Normalize(cell);
                if (h.Contains("(mps)") || h.Contains("(fps)") || h.Contains("(m/s)"))
                    return r;
            }

        // Pass 2: a real table header — a velocity word in a wide row (the 2-column
        // summary block "Velocità media | 806.6 MPS" must not qualify).
        for (int r = 0; r < rows.Count; r++)
        {
            string[] row = rows[r];
            if (row.Count(c => !string.IsNullOrWhiteSpace(c)) < 3) continue;
            if (row.Any(c => Str.ContainsAny(Str.Normalize(c), VelWords)))
                return r;
        }
        return -1;
    }

    private static int IndexOfHeader(string[] header, Func<string, bool> match)
    {
        for (int c = 0; c < header.Length; c++)
            if (match(Str.Normalize(header[c])))
                return c;
        return -1;
    }

    private static string DetectUnit(string[] header, int velCol)
    {
        string h = velCol >= 0 ? Str.Normalize(header[velCol]) : "";
        if (h.Contains("fps") || h.Contains("ft/s")) return "fps";
        if (h.Contains("mps") || h.Contains("m/s")) return "mps";
        return "mps"; // Athlon default; user confirmed always m/s
    }

    private static void ReadSummaryBlock(List<string[]> rows, int headerRow, AthlonString result)
    {
        for (int r = 0; r < rows.Count; r++)
        {
            if (r == headerRow) continue;
            string[] row = rows[r];
            if (row.Length < 2) continue;

            string label = Str.Normalize(row[0]);
            string value = row.Length > 1 ? row[1] : "";
            if (label.Length == 0) continue;

            bool isVelLabel = Str.ContainsAny(label, VelWords);
            if (result.DeviceAvg is null && isVelLabel && Str.ContainsAny(label, "medi", "average", "avg", "mittel", "moyenne", "promedio"))
                result.DeviceAvg = Str.ParseNumber(value);
            else if (result.DeviceSd is null && Str.ContainsAny(label, "deviazione standard", "standard deviation", "std dev", "abweichung", "ecart type"))
                result.DeviceSd = Str.ParseNumber(value);
            else if (result.DeviceEs is null && Str.ContainsAny(label, "diffusione estrema", "extreme spread", "spread", "estrema"))
                result.DeviceEs = Str.ParseNumber(value);
            else if (result.DevicePowerFactor is null && Str.ContainsAny(label, PfWords))
                result.DevicePowerFactor = NullIfBlank(value);
            else if (result.DeviceEnergy is null && Str.ContainsAny(label, KeWords))
                result.DeviceEnergy = NullIfBlank(value);
            else if (result.SessionNote is null && Str.ContainsAny(label, SessionNoteWords))
                result.SessionNote = NullIfBlank(value);
            else if (result.SessionTempC is null && Str.ContainsAny(label, TempWords) && Str.ParseNumber(value) is { } t)
            {
                bool fahrenheit = label.Contains("f°") || label.Contains("°f") || label.EndsWith(" f") || value.Contains('F');
                result.SessionTempC = fahrenheit ? Str.FahrenheitToCelsius(t) : t;
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
            if (m.Success && Str.ParseNumber(m.Groups[1].Value) is { } g) return g;

            // some sessions just put the number, e.g. "39.6"
            if (Regex.IsMatch(sessionNote.Trim(), @"^[0-9]{1,3}([.,][0-9]{1,2})?$") && Str.ParseNumber(sessionNote) is { } g2)
                return g2;
        }
        // fall back to the file name, e.g. "39.20.xlsx"
        Match f = Regex.Match(Path.GetFileNameWithoutExtension(path), @"([0-9]{1,3}[.,][0-9]{1,2})");
        return f.Success ? Str.ParseNumber(f.Groups[1].Value) : null;
    }

    private static string Cell(string[] row, int col) => col >= 0 && col < row.Length ? row[col] ?? "" : "";
    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
