using System.Globalization;
using System.Text;

namespace GrtReloadingToolkit.Log;

/// <summary>How a number is written in the unit GRT shows. Passed in so this stays free of the window and of the translations.</summary>
public sealed record FollowUpFormat(
    Func<double, string> Velocity,          // a velocity in m/s, with its unit
    Func<double, string> Spread,            // a velocity spread in m/s, the number only
    string VelocityUnit,
    Func<double, string> Temperature,       // a temperature in C, with its unit
    Func<double, string> VelocityPerDegree, // a velocity change in m/s per C, with its unit
    Func<double, string> VelocityPer30Days, // a velocity change in m/s per 30 days, with its unit
    Func<double, string>? TemperatureDelta = null) // a difference of temperature in C, with its unit
{
    public string Delta(double celsius) => (TemperatureDelta ?? (c => c.ToString("0.#", CultureInfo.InvariantCulture) + " C"))(celsius);

    /// <summary>Metric, for the tests and for a window with no GRT behind it.</summary>
    public static FollowUpFormat Metric { get; } = new(
        v => v.ToString("0.0", CultureInfo.InvariantCulture) + " m/s",
        v => v.ToString("0.0", CultureInfo.InvariantCulture),
        "m/s",
        c => c.ToString("0.0", CultureInfo.InvariantCulture) + " C",
        v => v.ToString("0.00", CultureInfo.InvariantCulture) + " m/s per C",
        v => v.ToString("0.0", CultureInfo.InvariantCulture) + " m/s per 30 days");
}

/// <summary>The plain-text answer of "follow a load": what the sessions of one load say, with the numbers and with what could not be said.
/// <paramref name="t"/> translates a template (English text is the key); the templates use {0}, {1}... for the numbers.</summary>
public static class FollowUpReport
{
    public static string Build(FollowUpResult r, string loadName, Func<string, string> t, FollowUpFormat f)
    {
        var ci = CultureInfo.InvariantCulture;
        string T(string template, params object[] args) => string.Format(ci, t(template), args);
        var sb = new StringBuilder();
        int used = r.Sessions.Count(s => s.Stats.N >= LoadFollowUp.MinShots);
        sb.AppendLine(T("{0}: {1} outings, {2} with at least {3} shots (the ones the analysis uses).", loadName, r.Sessions.Count, used, LoadFollowUp.MinShots));
        sb.AppendLine();

        if (r.Latest is { } l)
        {
            var last = r.Sessions.Last(s => s.Stats.N >= LoadFollowUp.MinShots);
            sb.AppendLine(T("LATEST OUTING ({0}), against the {1} before it", last.Session.Date.ToString("yyyy-MM-dd", ci), l.BaselineSessions));
            string expected = l.ExpectedMps is { } e ? f.Velocity(e) : "";
            sb.AppendLine(" - " + (l.TemperatureAdjusted
                ? T("Mean {0}; at {1} this load usually gives {2}: {3}.", f.Velocity(l.ObservedMps), f.Temperature(last.Session.TempC ?? 0), expected, Signal(l.Mean, t))
                : T("Mean {0}; this load usually gives {1}: {2}.", f.Velocity(l.ObservedMps), expected, Signal(l.Mean, t))));
            if (l.SdRatio is { } ratio)
                sb.AppendLine(" - " + T("Spread: SD {0} {1}, {2} times what the earlier outings showed together: {3}.",
                    f.Spread(last.Stats.Sd), f.VelocityUnit, ratio.ToString("0.0", ci), SpreadSignal(l.Spread, t)));
            sb.AppendLine();
        }

        if (r.Temperature is { } temp)
        {
            var e = temp.MpsPerC;
            sb.AppendLine(T("TEMPERATURE: {0} (95% between {1} and {2}), from {3} outings {4} apart{5}.",
                f.VelocityPerDegree(e.Estimate), f.VelocityPerDegree(e.CiLow), f.VelocityPerDegree(e.CiHigh), temp.Sessions,
                f.Delta(temp.SpanC), temp.TimeAccountedFor ? t(", with time taken into account") : ""));
            sb.AppendLine(" - " + t(e.Significant
                ? "The data say this load does change with temperature."
                : "The data do not show a temperature effect yet: the interval includes zero. It may be small or the outings too few."));
            sb.AppendLine();
        }

        if (r.Drift is { } drift)
        {
            var e = drift.MpsPer30Days;
            sb.AppendLine(T("OVER TIME: {0} (95% between {1} and {2}), over {3} days and {4} outings{5}.",
                f.VelocityPer30Days(e.Estimate), f.VelocityPer30Days(e.CiLow), f.VelocityPer30Days(e.CiHigh), drift.SpanDays.ToString("0", ci), drift.Sessions,
                drift.TemperatureAccountedFor ? t(", with temperature taken into account") : t(", temperature NOT taken into account")));
            // detectable is not the same as it matters: a drift well under the spread of one string is statistics, not a finding
            var sds = r.Sessions.Where(s => s.Stats.N >= LoadFollowUp.MinShots).Select(s => s.Stats.Sd).OrderBy(x => x).ToList();
            double typicalSd = sds.Count == 0 ? 0 : sds[sds.Count / 2];
            bool small = e.Significant && Math.Abs(e.Estimate) < 0.5 * typicalSd;
            sb.AppendLine(" - " + t(!e.Significant
                ? "No movement over time can be told apart from the noise yet."
                : small
                    ? "A movement over time can be detected, but it is small: well under the SD of one string."
                    : "The mean velocity is moving over time."));
            sb.AppendLine();
        }

        if (r.Changes.Count > 0)
        {
            sb.AppendLine(t("WHAT CHANGED BETWEEN OUTINGS"));
            foreach (var c in r.Changes)
            {
                var when = r.Sessions[c.Index].Session.Date.ToString("yyyy-MM-dd", ci);
                sb.AppendLine(" - " + T("{0}: {1}", when, string.Join(", ", c.Changes.Select(x => t(x)))));
            }
            sb.AppendLine();
        }

        if (r.NotEvaluated.Count > 0)
        {
            sb.AppendLine(t("NOT EVALUATED (missing data, not a clean bill of health)"));
            foreach (var g in r.NotEvaluated)
                sb.AppendLine(" - " + string.Format(ci, t(g.Template), g.Args.Select(a => a is TempDelta d ? f.Delta(d.Celsius) : a).ToArray()));
            sb.AppendLine();
        }

        sb.AppendLine(t("Everything above is about THIS load's own history. With few outings a verdict can be wrong either way; it firms up as the outings add up."));
        return sb.ToString();
    }

    private static string Signal(FollowSignal s, Func<string, string> t) => s switch
    {
        FollowSignal.Out => t("outside what is usual for it"),
        FollowSignal.Warning => t("on the edge of what is usual for it"),
        _ => t("normal for it"),
    };

    private static string SpreadSignal(FollowSignal s, Func<string, string> t) => s switch
    {
        FollowSignal.Out => t("clearly wider than usual"),
        FollowSignal.Warning => t("wider than usual, to watch"),
        _ => t("as usual"),
    };
}
