namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The "Chosen Depth" note written into the load when the shooter picks the seating depth that goes on to the check at
/// distance, from the seating-depth analyzer. The same layout as <see cref="ChosenChargeNote"/>, so later steps can rely on it.
/// </summary>
internal static class ChosenDepthNote
{
    public const string Title = "Chosen Depth";

    /// <param name="depth">The depth with its unit, as the analyzer shows it ("2.89 mm").</param>
    /// <param name="where">Caliber, powder and bullet, already joined.</param>
    /// <param name="ranking">One line on how it ranked.</param>
    /// <param name="figures">One line with SD, ES, group and rounds.</param>
    public static string Build(DateTime when, string depth, string where, string ranking, string figures, string nextLine)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{Title} {when:yyyy-MM-dd}");
        sb.AppendLine(new string('-', 20));
        sb.AppendLine();
        sb.AppendLine(string.Format(Lang.T("Seating depth: {0}"), depth));
        sb.AppendLine(where);
        sb.AppendLine(ranking);
        sb.AppendLine(figures);
        sb.AppendLine();
        sb.AppendLine(nextLine);
        return sb.ToString();
    }
}