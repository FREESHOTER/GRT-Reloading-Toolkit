using System.Globalization;

namespace GrtReloadingToolkit;

/// <summary>
/// The language hook for the text the pure engines write (calibration, ladder, temperature-coefficient, chronograph reports): they
/// know nothing of the UI, but what they write ends up in the shooter's own window and in the note written into GRT, so it has to
/// speak the shooter's language. <see cref="Translate"/> is identity until the program installs the UI's own translator at start-up
/// (see <c>Program.Main</c>), which is also why every test still sees the English text.
///
/// Templates keep their numbers as {0}, {1}... and are filled in AFTER translation, always with the invariant culture, so the digits
/// read the same in every window and note.
/// </summary>
internal static class Tr
{
    public static Func<string, string> Translate { get; set; } = s => s;

    public static string T(string en) => Translate(en);

    /// <summary>The translated template with its arguments filled in (invariant culture).</summary>
    public static string F(string en, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Translate(en), args);
}
