using System.Globalization;
using System.Text.RegularExpressions;

namespace GrtReloadingToolkit.Ui;

/// <summary>What the open load and the journal say, as the suggestions below need it.</summary>
/// <param name="CalibrationInLoad">The load already holds a barrel-calibration note.</param>
/// <param name="TemperatureSpreadC">Spread (max - min, in degrees C) of the temperatures recorded on the strings of the load, or null when fewer than two were.</param>
/// <param name="PowdersTriedForCaliber">Distinct powders the journal holds for this load's caliber.</param>
/// <param name="CalibrationsLoggedForCaliber">Journal entries for this caliber that carry a calibrated Ba.</param>
/// <param name="DiagnosableStrings">Strings of the load with enough shots for the guided diagnosis to read their spread.</param>
internal sealed record PathSignals(bool CalibrationInLoad, double? TemperatureSpreadC, int PowdersTriedForCaliber, int CalibrationsLoggedForCaliber, int DiagnosableStrings = 0)
{
    public static readonly PathSignals None = new(false, null, 0, 0);
}

/// <summary>An optional tool worth a look now, and why. <see cref="Reason"/> is the English translation key.</summary>
internal sealed record Suggestion(string Tool, string Reason);

/// <summary>
/// The optional tools the "next step" line offers beside the next step itself, only when the data say they are worth it
/// ("Optional tools suggested in each phase" in the manual). Never a rule about the shooter's intent: each one rests on a fact
/// in the load or the journal, and says which. Pure logic, so the tests can run it anywhere.
/// </summary>
internal static class ToolSuggestions
{
    /// <summary>Strings at least this far apart in temperature are worth a temperature coefficient.</summary>
    public const double MinTemperatureSpreadC = 5.0;

    public static IReadOnlyList<Suggestion> For(string current, PathSignals s)
    {
        var list = new List<Suggestion>();
        if (current == "Cal" && s.CalibrationInLoad && s.CalibrationsLoggedForCaliber >= 1)
            list.Add(new Suggestion("FindBa", "A calibration is in this load and the journal holds earlier ones for this caliber: compare them."));
        if (current is "ChronoStats" or "Workflow" or "GroupAnalysis" && s.TemperatureSpreadC is { } d && d >= MinTemperatureSpreadC)
            list.Add(new Suggestion("Temp", "The strings of this load were shot at different temperatures: fit the powder's temperature coefficients."));
        if (current is "Cal" or "Workflow" && s.PowdersTriedForCaliber >= 2)
            list.Add(new Suggestion("PowderCompare", "The journal holds more than one powder for this caliber: compare them."));
        if (current is "Workflow" or "GroupAnalysis" && s.DiagnosableStrings >= 1)
            list.Add(new Suggestion("Diagnosis", "If the check at distance is not good, the guided diagnosis (experimental) looks in the data of this load for the likely cause."));
        return list;
    }

    private static readonly Regex TemperatureToken = new(@"(?<![A-Za-z])T=(-?\d+(?:[.,]\d+)?)\s*°?\s*([CcFf])(?![A-Za-z])", RegexOptions.Compiled);

    /// <summary>The temperature, in degrees C, that a string's note carries as "T=15.0°C" or "T=59°F" (what the chronograph
    /// import writes), or null.</summary>
    public static double? TemperatureC(string? note)
    {
        if (string.IsNullOrEmpty(note)) return null;
        var m = TemperatureToken.Match(note);
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return null;
        return char.ToUpperInvariant(m.Groups[2].Value[0]) == 'F' ? (v - 32) * 5.0 / 9.0 : v;
    }

    /// <summary>Spread of the temperatures the strings' notes carry, or null when fewer than two strings have one.</summary>
    public static double? TemperatureSpreadC(IEnumerable<string?> stringNotes)
    {
        var t = stringNotes.Select(TemperatureC).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return t.Count < 2 ? null : t.Max() - t.Min();
    }
}