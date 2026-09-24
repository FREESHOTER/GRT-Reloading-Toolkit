using System.Net;
using System.Text;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Log;

/// <summary>
/// Renders a folder's worth of <see cref="LoadBook.Entry"/> into one static HTML document -- deliberately
/// HTML rather than a generated PDF, matching this plugin's standing choice not to add a PDF library
/// dependency (see <c>Targets/TargetRenderer.cs</c>'s own doc comment): the user's browser already
/// prints an HTML page to PDF or paper on its own, at whatever page size they choose, the same
/// decoupled-from-any-printer approach as <c>TargetForm.SavePdf</c>'s "Microsoft Print to PDF" path.
/// </summary>
public static class LoadBookHtml
{
    public static string Render(IReadOnlyList<LoadBook.Entry> entries, string title)
    {
        var u = GrtUnits.Current;
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(Enc(title)).Append("</title>");
        sb.Append("""
            <style>
              body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1a1a1a;background:#fff}
              h1{font-size:20px;margin-bottom:2px}
              .sub{color:#666;font-size:12px;margin-bottom:20px}
              h2{font-size:15px;border-bottom:2px solid #3aa0d8;padding-bottom:3px;margin-top:28px}
              table{border-collapse:collapse;width:100%;font-size:12px;margin-top:6px}
              th,td{border:1px solid #ddd;padding:5px 7px;text-align:left;vertical-align:top}
              th{background:#f2f6f9}
              tr.no-journal{color:#888;font-style:italic}
              .notes{max-width:220px}
              @media print{ h2{page-break-after:avoid} tr{page-break-inside:avoid} }
            </style>
            """);
        sb.Append("</head><body>");
        sb.Append("<h1>").Append(Enc(title)).Append("</h1>");
        sb.Append("<div class=\"sub\">").Append(Enc(DateTime.Now.ToString("yyyy-MM-dd HH:mm"))).Append(" — ")
            .Append(entries.Count).Append(" recipes</div>");

        foreach (var group in LoadBook.GroupByCaliber(entries))
        {
            sb.Append("<h2>").Append(Enc(group.Key)).Append("</h2>");
            sb.Append("<table><thead><tr>")
                .Append("<th>Load</th><th>Firearm</th><th>Powder</th><th>Charge</th><th>Bullet</th>")
                .Append("<th>COAL</th><th>Ba / a0</th><th>Velocity (SD, ES)</th><th>Group</th><th>Date</th><th>Notes</th>")
                .Append("</tr></thead><tbody>");

            foreach (var e in group.OrderBy(e => e.ChargeGr ?? double.MaxValue))
            {
                sb.Append("<tr").Append(e.FromJournal ? "" : " class=\"no-journal\"").Append('>');
                sb.Append("<td>").Append(Enc(e.LoadName)).Append("</td>");
                sb.Append("<td>").Append(Enc(e.Firearm)).Append("</td>");
                sb.Append("<td>").Append(Enc(e.PowderName)).Append("</td>");
                sb.Append("<td>").Append(e.ChargeGr is { } c ? Enc(u.Charge(c)) : "–").Append("</td>");
                sb.Append("<td>").Append(e.BulletName is { } bn ? Enc(bn) : "–").Append("</td>");
                sb.Append("<td>").Append(e.CoalMm is { } coal ? Enc(u.Length(coal)) : "–").Append("</td>");
                sb.Append("<td>").Append(FormatBaA0(e.Ba, e.A0)).Append("</td>");
                sb.Append("<td>").Append(FormatVelocity(u, e.VelocityAvgMs, e.SdMs, e.EsMs, e.Shots)).Append("</td>");
                sb.Append("<td>").Append(FormatGroup(u, e.GroupMoa, e.DistanceM)).Append("</td>");
                sb.Append("<td>").Append(e.JournalDate is { } d ? Enc(d) : "–").Append("</td>");
                sb.Append("<td class=\"notes\">").Append(e.Notes is { } n ? Enc(n) : "").Append("</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string FormatBaA0(double? ba, double? a0)
    {
        if (ba is null && a0 is null) return "–";
        string b = ba is { } bv ? FormattableString.Invariant($"{bv:0.#####}") : "–";
        string a = a0 is { } av ? FormattableString.Invariant($"{av:0.###}") : "–";
        return Enc($"{b} / {a}");
    }

    private static string FormatVelocity(GrtUnits u, double? avg, double? sd, double? es, int shots)
    {
        if (avg is not { } v) return "–";
        string s = u.Velocity(v);
        // VelocitySd already returns an invariant-formatted bare number (see GrtUnits.VelocitySd) --
        // no further formatting happens on it here, just plain concatenation.
        if (sd is { } sdv) s += " (SD " + u.VelocitySd(sdv) + (es is { } esv ? ", ES " + u.VelocitySd(esv) : "") + ")";
        if (shots > 0) s += ", n=" + shots;
        return Enc(s);
    }

    private static string FormatGroup(GrtUnits u, double? groupMoa, double? distanceM)
    {
        if (groupMoa is not { } g) return "–";
        string s = FormattableString.Invariant($"{g:0.00} MOA");
        if (distanceM is { } d) s += " @ " + u.Distance(d);
        return Enc(s);
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
