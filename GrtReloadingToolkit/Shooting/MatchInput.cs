using System.Globalization;
using System.Text.RegularExpressions;
using GrtReloadingToolkit.Cartridges;

namespace GrtReloadingToolkit.Shooting;

public sealed record MatchParse(IReadOnlyList<MatchShot> Shots, IReadOnlyList<string> Problems);

/// <summary>
/// A match typed or pasted by the shooter, one line for each shot (each target): <c>x y distance</c> in millimetres on the target (x to the right, y UP,
/// from the point of aim) and metres, then, if you have them, <c>v=831</c> (velocity in m/s), <c>w=+3</c> (across wind in m/s, positive when it pushes
/// to the right), <c>pos=prone</c> (position), <c>lot=A</c> (cartridge lot), <c>name=T4</c> (the target) and the time of the shot (<c>10:14:02</c>).
/// The distance may be left out when it is the same for all (the one given to the reader). Lines that begin with <c>#</c> are comments.
/// </summary>
public static class MatchInput
{
    private static readonly Regex Time = new(@"^(\d{1,2}):(\d{2})(?::(\d{2}))?$", RegexOptions.Compiled);

    private static string Key(string k) => k.ToLowerInvariant() switch
    {
        "v" or "vel" or "velocity" or "velocità" or "velocita" => "v",
        "w" or "wind" or "vento" => "w",
        "pos" or "position" or "posizione" => "pos",
        "lot" or "lotto" => "lot",
        "t" or "time" or "ora" => "t",
        "name" or "nome" or "target" or "bersaglio" => "name",
        _ => "",
    };

    /// <param name="defaultDistanceM">The distance of a line that gives none, in metres.</param>
    /// <param name="mmPerUnit">Millimetres in the unit the coordinates were typed in (1 for millimetres, 25.4 for inches).</param>
    /// <param name="metresPerUnit">Metres in the unit the distances were typed in (1 for metres, 0.9144 for yards).</param>
    public static MatchParse Parse(string? text, double defaultDistanceM = 100, double mmPerUnit = 1, double metresPerUnit = 1)
    {
        var shots = new List<MatchShot>();
        var problems = new List<string>();
        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var tokens = line.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 || !CartridgeText.TryParseNumber(tokens[0], out double x) || !CartridgeText.TryParseNumber(tokens[1], out double up))
            {
                problems.Add(Tr.F("Line {0}: expected two numbers (x and y in mm).", i + 1));
                continue;
            }
            double? distance = null, velocity = null, clock = null, wind = null;
            string? position = null, lot = null, name = null;
            bool bad = false;
            for (int t = 2; t < tokens.Length && !bad; t++)
            {
                string tok = tokens[t];
                var m = Time.Match(tok);
                int eq = tok.IndexOf('=');
                if (m.Success || (eq > 0 && Key(tok[..eq]) == "t" && Time.IsMatch(tok[(eq + 1)..])))
                {
                    if (!m.Success) m = Time.Match(tok[(eq + 1)..]);
                    int h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), mi = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    int s = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                    string? ampm = t + 1 < tokens.Length && (tokens[t + 1].Equals("AM", StringComparison.OrdinalIgnoreCase) || tokens[t + 1].Equals("PM", StringComparison.OrdinalIgnoreCase)) ? tokens[t + 1] : null;
                    if (ampm is not null) { h = h % 12 + (ampm.Equals("PM", StringComparison.OrdinalIgnoreCase) ? 12 : 0); t++; }
                    if (h > 23 || mi > 59 || s > 59) bad = true; else clock = h * 3600 + mi * 60 + s;
                }
                else if (eq > 0)
                {
                    string k = Key(tok[..eq]), val = tok[(eq + 1)..];
                    switch (k)
                    {
                        case "v": if (CartridgeText.TryParseNumber(val, out double v) && v > 0) velocity = v; else bad = true; break;
                        case "w": if (CartridgeText.TryParseNumber(val, out double w)) wind = w; else bad = true; break;
                        case "pos": if (val.Length > 0) position = val; else bad = true; break;
                        case "lot": if (val.Length > 0) lot = val; else bad = true; break;
                        case "name": if (val.Length > 0) name = val; else bad = true; break;
                        default: bad = true; break;
                    }
                }
                else if (distance is null && CartridgeText.TryParseNumber(tok, out double d) && d > 0) distance = d;
                else bad = true;
            }
            if (bad) { problems.Add(Tr.F("Line {0}: a value cannot be read (distance, v=, w=, pos=, lot=, name= or the time).", i + 1)); continue; }
            int number = shots.Count + 1;
            shots.Add(new MatchShot(number, name ?? "T" + number.ToString(CultureInfo.InvariantCulture), distance is { } typedDistance ? typedDistance * metresPerUnit : defaultDistanceM, x * mmPerUnit, up * mmPerUnit, velocity, clock, wind, position, lot));
        }
        return new MatchParse(shots, problems);
    }
}
