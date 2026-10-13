using System.Globalization;
using System.Text.RegularExpressions;

namespace GrtReloadingToolkit.Log;

/// <summary>
/// The two facts the guided diagnosis cannot work without and no GRT field holds: which shot was fired from a cold bore, and the wind a
/// group was shot in. They travel with the load as short tokens in text GRT already lets the shooter write, like the temperature the
/// chronograph import writes as <c>T=15C</c>:
/// <list type="bullet">
/// <item><c>COLD=1</c> in the note of the cold-bore shot (the shot's note in a measurement);</item>
/// <item><c>WIND=3.2m/s</c> (or <c>km/h</c>, <c>mph</c>, <c>ft/s</c>; m/s when no unit is given), optionally with <c>@90</c> for the direction
/// in degrees from the shooter's twelve o'clock, in the title of the shot-group tab or in the name of the group.</item>
/// </list>
/// Pure logic. The convention is in the manual, and the diagnosis tells the shooter which token is missing when a rule needs it.
/// </summary>
public static class DataTokens
{
    private static readonly Regex Cold = new(@"(?<![A-Za-z])COLD\s*=\s*1(?![0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Wind = new(
        @"(?<![A-Za-z])WIND\s*=\s*(?<v>\d+(?:[.,]\d+)?)\s*(?<u>m/s|mps|ms|km/h|kph|mph|ft/s|fps)?(?:\s*@\s*(?<d>\d{1,3}))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True when a shot's note carries <c>COLD=1</c>.</summary>
    public static bool IsCold(string? shotNote) => !string.IsNullOrEmpty(shotNote) && Cold.IsMatch(shotNote);

    /// <summary>The wind a text carries as <c>WIND=…</c>: speed in m/s and, when given, the direction in degrees; null when there is none.</summary>
    public static (double Mps, double? Degrees)? WindOf(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = Wind.Match(text);
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups["v"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return null;
        double mps = m.Groups["u"].Value.ToLowerInvariant() switch
        {
            "km/h" or "kph" => v / 3.6,
            "mph" => v * 0.44704,
            "ft/s" or "fps" => v * 0.3048,
            _ => v,
        };
        double? deg = m.Groups["d"].Success && double.TryParse(m.Groups["d"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv) ? dv : null;
        return (mps, deg);
    }
}
