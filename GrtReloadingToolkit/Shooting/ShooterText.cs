using System.Globalization;
using GrtReloadingToolkit.Cartridges;
using GrtReloadingToolkit.Measuring;

namespace GrtReloadingToolkit.Shooting;

/// <summary>What the reading of the shots says, in the words of the shooter: the numbers first, then what points to a mistake, then what is fine.</summary>
public static class ShooterText
{
    private static string N(double v, int d = 1) => v.ToString("0." + new string('0', d), CultureInfo.InvariantCulture);

    /// <param name="units">The units of the program (lengths, distances); without them the text is in millimetres and metres.</param>
    public static string Report(ShooterReport r, ProgramUnitView? units = null)
    {
        var lines = new List<string>();
        double lf = units?.LengthValue(1) ?? 1;                              // millimetres to the length unit shown (the table is converted here, the sentences below by LengthAll)
        string lu = units?.LengthUnitName ?? "mm";
        string LN(double mm, int d = 1) => N(mm * lf, units is { LengthInInch: true } ? d + 2 : d);
        string LS(double mm) => (mm * lf).ToString(units is { LengthInInch: true } ? "+0.000;-0.000;0.000" : "+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
        double moa = r.AngleMmAt100 * r.DistanceM / 100;
        lines.Add(Tr.F("{0} shots in {1} series at {2} m. Around the centre of each series the shots spread {3} mm sideways and {4} mm in height (SD), that is {5} and {6} MOA.",
            r.N, r.Series.Count, N(r.DistanceM, 0), N(r.SdX), N(r.SdUp), N(r.SdX / moa, 2), N(r.SdUp / moa, 2)));
        lines.Add(Tr.F("The longest spread runs {0}; it is {1} times the shortest.", AxisText(r.MajorAxisDegrees), N(r.AxisRatio)));
        lines.Add("");

        var rows = r.Series.Select(s => new[]
        {
            s.Series, s.N.ToString(CultureInfo.InvariantCulture), units == null ? Signed(s.CentreX) : LS(s.CentreX), units == null ? Signed(s.CentreUp) : LS(s.CentreUp), s.SdX is { } a ? LN(a) : "—", s.SdUp is { } b ? LN(b) : "—", LN(s.MeanRadius),
        }).ToList();
        var header = new[] { Tr.T("Series"), Tr.T("Shots"), Tr.T("Centre X (mm)").Replace("(mm)", "(" + lu + ")"), Tr.T("Centre Y (mm)").Replace("(mm)", "(" + lu + ")"), Tr.T("SD X (mm)").Replace("(mm)", "(" + lu + ")"), Tr.T("SD Y (mm)").Replace("(mm)", "(" + lu + ")"), Tr.T("Mean radius (mm)").Replace("(mm)", "(" + lu + ")") };
        var w = header.Select(h => h.Length).ToArray();
        foreach (var row in rows) for (int i = 0; i < row.Length; i++) w[i] = Math.Max(w[i], row[i].Length);
        string Row(string[] c) => string.Join("   ", c.Select((t, i) => i == 0 ? t.PadRight(w[i]) : t.PadLeft(w[i])));
        lines.Add(Row(header));
        lines.Add(new string('-', w.Sum() + 3 * (w.Length - 1)));
        lines.AddRange(rows.Select(Row));
        lines.Add("");

        if (r.Velocity is { } v)
        {
            lines.Add(Tr.F("The cartridges: each m/s more lifts the shot by {0} mm{1}. The velocity explains {2} % of the spread in height{3} The rest of the height and all the sideways spread come from the rifle, the shooter and the wind.",
                N(v.SlopeMmPerMps, 2), v.TheoreticalMmPerMps is { } th ? " " + Tr.F("(theory at {0} m: about {1} mm)", N(r.DistanceM, 0), N(th, 2)) : "",
                N(v.R2 * 100, 0), v.P < 0.05 ? " " + Tr.T("(a sure figure).") : " " + Tr.T("(not a sure figure with these shots).")));
            lines.Add("");
        }

        if (r.Findings.Count == 0)
            lines.Add(Tr.T("Nothing in the pattern of these shots points to a mistake of the shooter. That is not proof that there is none: with few shots only large effects show."));
        else
        {
            lines.Add(Tr.F("What points to a mistake ({0}):", r.Findings.Count));
            int i = 1;
            foreach (var f in r.Findings)
            {
                lines.Add("");
                lines.Add($"{i++}. {f.Title}");
                lines.Add("   " + f.Detail.Replace(Environment.NewLine, Environment.NewLine + "   "));
                lines.Add("   → " + f.Advice);
            }
        }
        if (r.Fine.Count > 0)
        {
            lines.Add("");
            lines.Add(Tr.T("What was checked and is fine:"));
            lines.AddRange(r.Fine.Select(t => "• " + t));
        }
        lines.Add("");
        lines.Add(Tr.T("Each reading is a test on these shots, with the probability that it is chance written beside it. A series of few shots (under 10) says little: a mistake that is not found may still be there, and one that is found should be confirmed on the next shooting."));
        string text = UnitText.Angle(string.Join(Environment.NewLine, lines), r.AngleUnit);
        return units == null ? text : units.DistanceAll(units.LengthAll(text));
    }

    private static string Signed(double v) => v.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

    /// <summary>"nearly sideways", "nearly vertical", "slanting up to the right (45°)"…</summary>
    private static string AxisText(double degrees)
    {
        double a = degrees;                                   // −90…90 from horizontal, positive = rising to the right
        if (Math.Abs(a) <= 15) return Tr.T("nearly sideways");
        if (Math.Abs(a) >= 75) return Tr.T("nearly vertical");
        return Tr.F("slanting {0} ({1}°)", a > 0 ? Tr.T("up to the right") : Tr.T("down to the right"), N(Math.Abs(a), 0));
    }
}
