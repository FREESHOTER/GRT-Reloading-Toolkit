namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The "Chosen Charge" note written into the load when the shooter picks the charge that goes on to the seating-depth test,
/// from the ladder analysis or from the Journal leaderboard. One layout for both, so later steps can rely on it.
/// </summary>
internal static class ChosenChargeNote
{
    public const string Title = "Chosen Charge";

    /// <param name="charge">The charge with its unit, as GRT shows it ("40.2 gr").</param>
    /// <param name="where">Caliber, powder and bullet, already joined.</param>
    /// <param name="ranking">One line on how it ranked.</param>
    /// <param name="figures">One line with SD, ES, group and rounds.</param>
    public static string Build(DateTime when, string charge, string where, string ranking, string figures, string nextLine)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{Title} {when:yyyy-MM-dd}");
        sb.AppendLine(new string('-', 20));
        sb.AppendLine();
        sb.AppendLine(string.Format(Lang.T("Charge: {0}"), charge));
        sb.AppendLine(where);
        sb.AppendLine(ranking);
        sb.AppendLine(figures);
        sb.AppendLine();
        sb.AppendLine(nextLine);
        return sb.ToString();
    }
}
