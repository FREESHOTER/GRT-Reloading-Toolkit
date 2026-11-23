using System.Globalization;
using GrtReloadingToolkit.Cartridges;
using GrtReloadingToolkit.Measuring;

namespace GrtReloadingToolkit.Shooting;

/// <summary>What the reading of a match says: the size of each part of the errors, what points to the cartridges, the settings, the conditions and the shooter, then every shot.</summary>
public static class MatchText
{
    private static string N(double v, int d = 1) => ShooterErrors.F(v, d);

    /// <param name="units">The units of the program (distances); without them the text is in metres.</param>
    public static string Report(MatchReport r, Measuring.ProgramUnitView? units = null)
    {
        string du = units?.DistanceUnitName ?? "m";
        double df = units?.DistanceValue(1) ?? 1;
        var lines = new List<string>
        {
            Tr.F("{0} shots on {1} targets, from {2} m to {3} m. The shots are on average {4} MOA from the point of aim of their own target.", r.N, r.Targets, N(r.MinDistanceM, 0), N(r.MaxDistanceM, 0), N(r.MeanErrorMoa, 2)),
            Tr.F("Each error is made of four parts; on average they are worth (MOA): cartridges {0}, settings {1}, wind not corrected {2}, shooting {3}. Below {4} MOA (half a click) a part cannot be corrected on the scope.",
                N(r.MeanCartridgeMoa, 2), N(r.MeanSettingsMoa, 2), N(r.MeanWindMoa, 2), N(r.MeanExecutionMoa, 2), N(r.NoticeMoa, 3)),
            "",
        };

        if (r.Findings.Count == 0)
            lines.Add(Tr.T("Nothing in this match points to a problem of the cartridges, of the settings or of the shooter. That is not proof that there is none: with few shots only large effects show."));
        else
        {
            lines.Add(Tr.F("What points to a problem ({0}):", r.Findings.Count));
            if (r.Findings.Any(f => f.Weak)) lines.Add(Tr.T("(to be confirmed) = the evidence is weak, between 1 % and 5 % of chance: it counts only if the next match shows it again."));
            int i = 1;
            foreach (var origin in new[] { MatchOrigin.Cartridge, MatchOrigin.Settings, MatchOrigin.Conditions, MatchOrigin.Shooter })
            {
                var group = r.Findings.Where(f => f.Origin == origin).ToList();
                if (group.Count == 0) continue;
                lines.Add("");
                lines.Add("== " + OriginName(origin) + " ==");
                foreach (var f in group)
                {
                    lines.Add("");
                    lines.Add($"{i++}. " + (f.Weak ? Tr.F("{0} (to be confirmed)", f.Title) : f.Title));
                    lines.Add("   " + f.Detail.Replace(Environment.NewLine, Environment.NewLine + "   "));
                    lines.Add("   → " + f.Advice);
                }
            }
        }

        lines.Add("");
        lines.Add(Tr.T("Shot by shot (errors in MOA from the point of aim: + right and up; then the four parts):"));
        var header = new[] { Tr.T("Shot"), Tr.T("Target"), du, Tr.T("Right"), Tr.T("Up"), Tr.T("Cartridge"), Tr.T("Settings"), Tr.T("Wind"), Tr.T("Shooting"), Tr.T("Main cause") };
        var rows = r.Shots.Select(s => new[]
        {
            s.Number.ToString(CultureInfo.InvariantCulture), s.Target, N(s.DistanceM * df, 0), Signed(s.XMoa), Signed(s.UpMoa), N(s.CartridgeMoa, 2), N(s.SettingsMoa, 2), N(s.WindMoa, 2), N(s.ExecutionMoa, 2), CauseName(s.Cause),
        }).ToList();
        var w = header.Select(h => h.Length).ToArray();
        foreach (var row in rows) for (int c = 0; c < row.Length; c++) w[c] = Math.Max(w[c], row[c].Length);
        string Row(string[] c) => string.Join("  ", c.Select((t, k) => k is 1 or 9 ? t.PadRight(w[k]) : t.PadLeft(w[k])));
        lines.Add(Row(header));
        lines.Add(new string('-', w.Sum() + 2 * (w.Length - 1)));
        lines.AddRange(rows.Select(Row));

        var noted = r.Shots.Where(s => s.Text.Length > 0).ToList();
        if (noted.Count > 0)
        {
            lines.Add("");
            lines.Add(Tr.T("What is behind the shots that stand out:"));
            // the same words for three or more shots (a zero that is off weighs on every shot alike) are said once
            string Label(ShotDiagnosis d) => Tr.F("Shot {0} ({1}, {2} m)", d.Number, d.Target, N(d.DistanceM, 0));
            string Message(ShotDiagnosis d) => d.Text.StartsWith(Label(d) + ": ", StringComparison.Ordinal) ? d.Text[(Label(d).Length + 2)..] : d.Text;
            var common = noted.GroupBy(Message).Where(g => g.Count() >= 3).ToDictionary(g => g.Key, g => g.Select(d => d.Number).ToList());
            var said = new HashSet<string>();
            foreach (var d in noted)
            {
                string m = Message(d);
                if (!common.TryGetValue(m, out var numbers)) { lines.Add("• " + d.Text); continue; }
                if (!said.Add(m)) continue;
                lines.Add("• " + (numbers.Count == r.Shots.Count ? Tr.F("All the shots: {0}", m) : Tr.F("Shots {0}: {1}", string.Join(", ", numbers), m)));
            }
        }

        if (r.Fine.Count > 0)
        {
            lines.Add("");
            lines.Add(Tr.T("What was checked and is fine:"));
            lines.AddRange(r.Fine.Select(t => "• " + t));
        }
        lines.Add("");
        lines.Add(Tr.T("Each reading is a test on these shots, with the probability that it is chance written beside it. In a match the shots are few and every one has its own distance and wind: a problem that is not found may still be there, and one that is found should be confirmed in the next match. The parts are separated by the numbers of this match only; the velocity part uses the drop without air."));
        string text = UnitText.Angle(string.Join(Environment.NewLine, lines), r.AngleUnit);
        return units == null ? text : units.DistanceAll(text);
    }

    private static string Signed(double v) => v.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    private static string OriginName(MatchOrigin o) => o switch
    {
        MatchOrigin.Cartridge => Tr.T("The cartridges"),
        MatchOrigin.Settings => Tr.T("The settings of the rifle and the scope"),
        MatchOrigin.Conditions => Tr.T("The conditions (wind)"),
        _ => Tr.T("The shooter"),
    };

    private static string CauseName(string cause) => cause switch
    {
        "cartridge" => Tr.T("cartridge"),
        "settings" => Tr.T("settings"),
        "wind" => Tr.T("wind"),
        "execution" => Tr.T("shooting"),
        "target" => Tr.T("same target"),
        _ => Tr.T("normal"),
    };
}
