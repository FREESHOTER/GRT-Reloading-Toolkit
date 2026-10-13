using System.Text.RegularExpressions;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Reads the charge back out of a "Chosen Charge" note (<see cref="ChosenChargeNote"/>), for the tools that carry it on to the
/// next phase. Kept apart from the note builder so it needs no translation table.
/// </summary>
internal static class ChosenChargeReader
{
    /// <summary>The charge the note names ("40.2 gr"), or null. The line reads "Charge: ..." or, in an Italian interface,
    /// "Carica: ...", because the label is translated when the note is written.</summary>
    public static string? ChargeFrom(string? noteText)
    {
        if (string.IsNullOrWhiteSpace(noteText)) return null;
        var m = Regex.Match(noteText, @"^\s*(?:Charge|Carica):\s*(.+?)\s*$", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }
}