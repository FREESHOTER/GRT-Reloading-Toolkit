using System.Globalization;
using System.Text.RegularExpressions;
using GrtReloadingToolkit.Cartridges;

namespace GrtReloadingToolkit.Shooting;

/// <summary>Shots typed or pasted by the shooter, read into the pattern the reading works on.</summary>
public sealed record PatternParse(IReadOnlyList<PatternShot> Shots, IReadOnlyList<string> Problems);

/// <summary>
/// One shot per line: <c>x y</c> in millimetres (x to the right, y UP), then, if you have them, the velocity in m/s and the time of the shot
/// (<c>10:14:02</c>) in either order. A blank line starts a new series; a line that begins with <c>#</c> starts one and names it. The decimal sign
/// may be a comma or a dot.
/// </summary>
public static class PatternInput
{
    private static readonly Regex Time = new(@"^(\d{1,2}):(\d{2})(?::(\d{2}))?$", RegexOptions.Compiled);

    /// <param name="mmPerUnit">Millimetres in the unit the coordinates were typed in (1 for millimetres, 25.4 for inches).</param>
    public static PatternParse Parse(string? text, double mmPerUnit = 1)
    {
        var shots = new List<PatternShot>();
        var problems = new List<string>();
        int series = 1, order = 0;
        string name = "1";
        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) { if (order > 0) { series++; order = 0; name = series.ToString(CultureInfo.InvariantCulture); } continue; }
            if (line.StartsWith('#'))
            {
                if (order > 0) { series++; order = 0; }
                name = line.TrimStart('#').Trim();
                if (name.Length == 0) name = series.ToString(CultureInfo.InvariantCulture);
                continue;
            }
            var tokens = line.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 || !CartridgeText.TryParseNumber(tokens[0], out double x) || !CartridgeText.TryParseNumber(tokens[1], out double up))
            {
                problems.Add(Tr.F("Line {0}: expected two numbers (x and y in mm).", i + 1));
                continue;
            }
            double? velocity = null, clock = null;
            bool bad = false;
            for (int t = 2; t < tokens.Length && !bad; t++)
            {
                string tok = tokens[t];
                var m = Time.Match(tok);
                if (m.Success)
                {
                    int h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), mi = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    int s = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                    string? ampm = t + 1 < tokens.Length && (tokens[t + 1].Equals("AM", StringComparison.OrdinalIgnoreCase) || tokens[t + 1].Equals("PM", StringComparison.OrdinalIgnoreCase)) ? tokens[t + 1] : null;
                    if (ampm is not null) { h = h % 12 + (ampm.Equals("PM", StringComparison.OrdinalIgnoreCase) ? 12 : 0); t++; }
                    if (h > 23 || mi > 59 || s > 59) bad = true; else clock = h * 3600 + mi * 60 + s;
                }
                else if (CartridgeText.TryParseNumber(tok, out double v) && v > 0) velocity = v;
                else bad = true;
            }
            if (bad) { problems.Add(Tr.F("Line {0}: the velocity or the time is not readable.", i + 1)); continue; }
            shots.Add(new PatternShot(name, order, order + 1, x * mmPerUnit, up * mmPerUnit, velocity, clock));
            order++;
        }
        return new PatternParse(shots, problems);
    }
}
