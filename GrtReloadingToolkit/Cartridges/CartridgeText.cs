using System.Globalization;

namespace GrtReloadingToolkit.Cartridges;

/// <summary>
/// What the "Cartridges" window writes and reads that is not drawing: the marks of a tray, the numbers the shooter types, and the plain
/// sentences that say what the model found and whether a prediction held. The sentences go through <see cref="Tr"/>, like every text the
/// engines write.
/// </summary>
public static class CartridgeText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The marks of a tray of <paramref name="columns"/> × <paramref name="rows"/> holes, as engraved: number then letter, column by column (1A, 1B … 1J, 2A …).</summary>
    public static List<string> TrayMarks(int columns, int rows)
    {
        if (columns < 1 || rows < 1 || rows > 26) throw new ArgumentOutOfRangeException(nameof(rows), "A tray has 1–26 rows and at least one column.");
        var marks = new List<string>(columns * rows);
        for (int c = 1; c <= columns; c++)
            for (int r = 0; r < rows; r++)
                marks.Add(c.ToString(Inv) + (char)('A' + r));
        return marks;
    }

    /// <summary>
    /// Marks in the order a shooter counts them: by the numbers inside, so 1A, 2A … 9A, 10A (not 10A before 2A). Letters compare without regard to case.
    /// </summary>
    public static int CompareMarks(string? a, string? b)
    {
        string x = a ?? "", y = b ?? "";
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                string nx = x[si..i].TrimStart('0'), ny = y[sj..j].TrimStart('0');
                if (nx.Length != ny.Length) return nx.Length.CompareTo(ny.Length);
                int c = string.CompareOrdinal(nx, ny);
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }

    /// <summary>Marks written one after the other, separated by spaces, commas, semicolons or new lines; empty ones dropped, order kept.</summary>
    public static List<string> ParseMarks(string? text)
        => (text ?? "").Split(new[] { ' ', '\t', '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>A number as typed by the shooter: the comma or the dot as decimal sign, spaces ignored; with both, the last one is the decimal sign.</summary>
    public static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Replace(" ", "").Replace(" ", "");
        int comma = t.LastIndexOf(','), dot = t.LastIndexOf('.');
        if (comma >= 0 && dot >= 0) t = comma > dot ? t.Replace(".", "").Replace(',', '.') : t.Replace(",", "");
        else if (comma >= 0) t = t.Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, Inv, out value) && double.IsFinite(value);
    }

    /// <summary>A probability as a percentage, "&lt; 0.1" when it is smaller than that (a p of 0.00004 is not "0.0").</summary>
    private static string Pct(double p) => p * 100 < 0.1 ? "< 0.1" : N(p * 100, 1);

    private static string N(double v, int decimals = 2) => v.ToString("0." + new string('0', decimals), Inv);
    private static string Signed(double v, int decimals = 2)
    {
        string f = "0." + new string('0', decimals);
        return v.ToString("+" + f + ";-" + f + ";+" + f, Inv);
    }

    /// <summary>What the model found: how many shots it learned from, which measures have a proven effect and how much of the SD they carry.</summary>
    /// <param name="measureShown">How a measure is shown (its value and unit in the unit chosen for that field, all linear); without it, as it is stored.</param>
    public static string ModelSummary(VelocityModelResult m, Func<string, double, (double Value, string Unit)>? measureShown = null)
    {
        var lines = new List<string>
        {
            Tr.F("{0} shots fired from measured cartridges, in {1} strings. Inside the strings the SD is {2} m/s.", m.NShots, m.NSeries, N(m.SdWithin)),
        };
        if (m.Significant.Count == 0)
        {
            lines.Add(Tr.F("No measure of the cartridges has a proven effect on velocity yet: more shots from measured cartridges are needed (a measure is tested only once at least {0} shots carry it).",
                VelocityModel.MinShotsPerMeasure));
            return string.Join(Environment.NewLine, lines);
        }
        foreach (var t in m.Significant)
        {
            var one = measureShown?.Invoke(t.Key, 1) ?? (1.0, MeasureFields.UnitOf(t.Key));
            double f = one.Value == 0 ? 1 : one.Value;                    // shown units per stored unit: a slope per shown unit is the stored one over it
            lines.Add(Tr.F("{0}: {1} m/s per {2}, probability of chance {3} %. Inside the strings it varies by {4} {2}, which puts {5} m/s into the SD.",
                Tr.T(MeasureFields.LabelOf(t.Key)), Signed(t.Slope / f), one.Unit, Pct(t.PAdj), N(t.SdX * f, 3), N(t.SdFrom)));
        }
        lines.Add(Tr.F("What the model does not explain is {0} m/s of SD (primer, barrel, what was not measured); the measures explain {1} % of the spread inside the strings.",
            N(m.SdResidual), N(m.R2 * 100, 0)));
        if (m.LeaveOneOut is { } l)
            lines.Add(Tr.F("Each shot estimated without itself: link {0}; the SD inside the strings would go from {1} to {2} m/s.", Signed(l.R), N(l.SdWithout), N(l.SdWith)));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The verdict on a prediction after the shooting, in the words of the shooter; a session the model learned from says so first.</summary>
    public static string VerdictText(PredictionCheckResult r, bool independent)
    {
        string text;
        switch (r.Verdict)
        {
            case CheckVerdict.NothingToCheck:
                text = Tr.T("The cartridges of this session all have the same estimate: there is nothing to check.");
                break;
            case CheckVerdict.Works:
                text = Tr.F("The prediction WORKS: the cartridges estimated faster really were faster (link {0}, probability of chance {1} %).", Signed(r.R!.Value), Pct(r.POneSided!.Value));
                text += " " + SlopeSentence(r);
                break;
            case CheckVerdict.RightDirection:
                text = Tr.F("The prediction goes the right way (link {0}) but is not shown yet with these shots (probability of chance {1} %).", Signed(r.R!.Value), N(r.POneSided!.Value * 100, 0));
                text += " " + SlopeSentence(r);
                break;
            default:
                text = Tr.F("The prediction did NOT work in these shots (link {0}): the measures of the model need to be reviewed with more data.", Signed(r.R!.Value));
                text += " " + SlopeSentence(r);
                break;
        }
        return independent
            ? text
            : Tr.T("ATTENTION: this session is one the model learned from, so the check is not independent (the result is bound to be good). The real check is made with shots fired AFTER the prediction.") + " " + text;
    }

    /// <summary>"excellent" up to 2 m/s, "acceptable" up to 4, "to improve" beyond: the thresholds of Analisi DS.</summary>
    public static string SdVerdict(double sdMps) =>
        sdMps <= VarianceBudget.SdGood ? Tr.T("excellent") : sdMps <= VarianceBudget.SdAcceptable ? Tr.T("acceptable") : Tr.T("to improve");

    /// <summary>What the budget says, as plain text with aligned columns (it is shown in a fixed-width box and can be copied).</summary>
    /// <summary>The name of one item of the SD budget, in the language of the text (the report and the chart say it the same way).</summary>
    public static string PartLabel(BudgetPart p) => p.Kind switch
    {
        "between" => Tr.T("Change of velocity from one series to the next"),
        "trend" => Tr.T("Movement inside the series (first shots, barrel warming)"),
        "measure" => Tr.T(MeasureFields.LabelOf(p.Key ?? "")) + " " + Tr.T("(measure of the cartridges)"),
        "chrono" => Tr.T("Chronograph error"),
        _ => Tr.T("Rest: cartridges and barrel (powder, bullet, neck, primer…)"),
    };

    /// <param name="units">The units of the program for the distances, lengths and angles of the target-cost table; without them metres, millimetres and MOA.</param>
    /// <param name="measureShown">How a measure of the cartridges is shown (its value and unit in the unit chosen for that field); without it, as it is stored.</param>
    public static string BudgetReport(VarianceBudgetResult b, double chronographSd = 0, Measuring.ProgramUnitView? units = null, Func<string, double, (double Value, string Unit)>? measureShown = null)
    {
        var lines = new List<string>();
        void Table(string[] header, List<string[]> rows)
        {
            var w = header.Select(h => h.Length).ToArray();
            foreach (var r in rows) for (int i = 0; i < r.Length; i++) w[i] = Math.Max(w[i], r[i].Length);
            string Row(string[] r) => string.Join("   ", r.Select((c, i) => i == 0 ? c.PadRight(w[i]) : c.PadLeft(w[i])));
            lines.Add(Row(header));
            lines.Add(new string('-', w.Sum() + 3 * (w.Length - 1)));
            foreach (var r in rows) lines.Add(Row(r));
        }

        lines.Add(Tr.F("{0} shots in {1} series. The SD that counts for the cartridges is the one inside the series: {2} m/s ({3}; excellent up to {4}, acceptable up to {5} m/s).",
            b.N, b.SeriesCount, N(b.SdWithin), SdVerdict(b.SdWithin), N(VarianceBudget.SdGood, 1), N(VarianceBudget.SdAcceptable, 1)));
        lines.Add(Tr.F("With these shots the true SD lies, with a 90 % probability, between {0} and {1} m/s.", N(b.SdWithinInterval.Lo, 1), N(b.SdWithinInterval.Hi, 1)));
        if (b.SeriesCount > 1 && b.SdTotal > b.SdWithin * 1.05)
            lines.Add(Tr.F("All the shots together give {0} m/s: the difference comes from the change of velocity between one series and the next.", N(b.SdTotal)));
        lines.Add("");

        Table(new[] { Tr.T("Series"), Tr.T("Shots"), Tr.T("Mean (m/s)"), Tr.T("SD (m/s)"), Tr.T("True SD at 90 %"), Tr.T("ES (m/s)") },
            b.Series.Select((s, i) => new[] { (i + 1).ToString(Inv), s.N.ToString(Inv), N(s.Mean, 1), N(s.Sd),
                s.Interval is { } iv ? N(iv.Lo, 1) + " – " + N(iv.Hi, 1) : "—", N(s.Es, 1) }).ToList());
        lines.Add("");

        lines.Add(Tr.T("Where the SD comes from (the shares add up the variance, the SD squared: that is why the SDs of the items do not add up):"));
        string Label(BudgetPart p) => PartLabel(p);
        (double Value, string Unit) Shown(string key, double v) => measureShown?.Invoke(key, v) ?? (v, MeasureFields.UnitOf(key));
        string Proven(BudgetPart p) => p.P is null ? "—" : p.Proven ? Tr.T("yes") : Tr.T("no");
        Table(new[] { Tr.T("Item"), Tr.T("SD (m/s)"), Tr.T("Share"), Tr.T("Proven") },
            b.Parts.Select(p => new[] { Label(p), N(p.SdMps), N(p.Share * 100, 0) + " %", Proven(p) }).Append(new[] { Tr.T("Total"), N(b.SdTotal), "100 %", "" }).ToList());
        lines.Add(Tr.T("\"Proven\" means that with these shots the item cannot be chance (probability under 5 %).") + (chronographSd > 0 ? "" : " " + Tr.T("The chronograph precision is not entered: its part stays inside the \"Rest\".")));
        lines.Add(Tr.F("Take away what can be corrected and an SD of about {0} m/s would remain: the part of the cartridges and the barrel{1}.", N(b.SdRest),
            b.SdRest > VarianceBudget.SdAcceptable ? ", " + Tr.T("which alone is above the acceptable limit") : ""));
        if (b.Trend is { } tr && b.Parts.FirstOrDefault(p => p.Kind == "trend") is { Proven: true })
            lines.Add(Tr.F("The first shot of each series is on average {0} m/s {1} than the others: use the proving shots to bring the barrel to regime and keep the same pace between series.",
                N(Math.Abs(tr.FirstShotOffsetMps), 1), tr.FirstShotOffsetMps < 0 ? Tr.T("slower") : Tr.T("faster")));
        foreach (var a in b.Advice)
            lines.Add(Tr.F("{0}: halve its variation and the SD inside the series would go from {1} to about {2} m/s; remove it altogether and it would be {3} m/s. Today it goes from {4} to {5} {6}.",
                Tr.T(MeasureFields.LabelOf(a.Key)), N(b.SdWithin), N(a.SdIfHalved), N(a.SdIfRemoved), N(Shown(a.Key, a.Min).Value, 3), N(Shown(a.Key, a.Max).Value, 3), Shown(a.Key, a.Min).Unit));
        lines.Add("");

        lines.Add(Tr.F("What the velocity costs on the target (no air: it is the minimum, at 300 m the real figure is higher), with the SD inside the series, {0} m/s:", N(b.SdWithin)));
        string lu = units?.LengthUnitName ?? "mm", au = units?.AngleUnitName ?? "MOA", du = units?.DistanceUnitName ?? "m";
        double lf = units?.LengthValue(1) ?? 1;
        int ld = units is { LengthInInch: true } ? 3 : 1;
        Table(new[] { Tr.T("Distance"), Tr.T("mm per m/s").Replace("mm", lu), Tr.T("Height SD (mm)").Replace("(mm)", "(" + lu + ")"), Tr.T("Height SD (MOA)").Replace("(MOA)", "(" + au + ")") },
            b.TargetCost.Select(r => new[] { N((units?.DistanceValue(r.DistanceM) ?? r.DistanceM), 0) + " " + du, N(r.MmPerMs * lf, units is { LengthInInch: true } ? 3 : 2), N(r.SdMm * lf, ld), N(units?.AngleValue(r.SdMoa) ?? r.SdMoa, 2) }).ToList());
        lines.Add("");

        lines.Add(b.ShotsNeeded is { } need
            ? Tr.F("To show that a change brings the SD from {0} to {1} m/s, about {2} shots for each of the two variants are needed (fired in the same session, alternating the cartridges).", N(b.SdWithin, 1), N(b.TargetSd, 1), need)
            : Tr.T("The improvement looked for is too small to be shown with a reasonable number of shots."));
        lines.Add(Tr.T("The SD of 5 shots can come out half or double the true one: to judge a charge at least 10–15 shots are needed, better in several series."));
        string text = string.Join(Environment.NewLine, lines);
        return units == null ? text : units.DistanceAll(text);
    }

    private static string SlopeSentence(PredictionCheckResult r) =>
        Tr.F("Slope {0} (1 = estimate right, below 1 = the estimate exaggerates the differences, above 1 = it understates them). Take away the estimated part and the SD inside the strings would go from {1} to {2} m/s.",
            N(r.Slope!.Value), N(r.SdWithout), N(r.SdWith));
}
