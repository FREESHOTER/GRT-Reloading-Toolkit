using System.Text.RegularExpressions;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Ocw;

/// <summary>Unit of the GRT shot-group "reference distance" field (not stored in the file).</summary>
public enum RefUnit { Mm, Cm, Inch }

/// <summary>Unit of the GRT shot-group "shooting distance" field.</summary>
public enum ShootUnit { Meters, Yards }

/// <summary>
/// Turns GRT "Shot group analysis" tabs (stored in a .grtload) into <see cref="TargetGroup"/>s
/// the ladder analyzer already understands — no Ballistic-X export needed.
///
/// GRT stores hits as image fractions (0..1). Two reference points a known distance apart
/// calibrate the scale; the picture's width/height give the pixel aspect ratio.
/// </summary>
public static class GrtShotGroups
{
    private const double MoaMmPer100m = 29.0888;   // 1 true MOA at 100 m

    /// <summary>Millimetres subtended by 1 MOA at the given distance.</summary>
    public static double MoaMm(double distanceM) => MoaMmPer100m * (distanceM / 100.0);

    public static double RefToMm(double v, RefUnit u) => u switch
    {
        RefUnit.Cm => v * 10.0,
        RefUnit.Inch => v * 25.4,
        _ => v,
    };

    public static double ShootToM(double v, ShootUnit u) => u == ShootUnit.Yards ? v * 0.9144 : v;

    public sealed record Options(RefUnit Ref = RefUnit.Mm, ShootUnit Shoot = ShootUnit.Meters, bool ExcludeFlyers = false);

    /// <summary>
    /// One <see cref="TargetGroup"/> per &lt;group&gt; across every shot-group tab in the load.
    /// The ladder step is a number parsed from the group name (then the tab title).
    /// </summary>
    public static (List<TargetGroup> Groups, List<string> Log) FromDoc(GrtLoadDoc doc, Options? opt = null)
    {
        opt ??= new Options();
        var log = new List<string>();
        var outp = new List<TargetGroup>();

        int tabIdx = 0;
        foreach (var sg in doc.ShotGroups())
        {
            tabIdx++;
            int w = sg.ImageWidth, h = sg.ImageHeight;
            if (w <= 0 || h <= 0)
            {
                w = h = 1000;
                log.Add($"shot-group '{sg.Title}': no picture size, assuming square target (distances may be off)");
            }

            double dpx = (sg.RefP2X - sg.RefP1X) * w;
            double dpy = (sg.RefP2Y - sg.RefP1Y) * h;
            double refPx = Math.Sqrt(dpx * dpx + dpy * dpy);
            double refMm = RefToMm(sg.RefDistance, opt.Ref);
            if (refPx < 1e-6 || refMm <= 0)
            {
                log.Add($"shot-group '{sg.Title}': reference points/distance not set — skipped");
                continue;
            }
            double mmPerPx = refMm / refPx;
            double distM = ShootToM(sg.ShootDistance, opt.Shoot);
            if (distM <= 0) { distM = 100; log.Add($"shot-group '{sg.Title}': no shooting distance, assuming 100 m"); }
            double mmToMoa = 1.0 / (MoaMmPer100m * (distM / 100.0));

            int groupIdx = 0;
            foreach (var set in sg.Groups)
            {
                groupIdx++;
                var poa = set.Points.FirstOrDefault(p => p.PointOfAim);
                var hits = set.Points.Where(p => !p.PointOfAim && (!opt.ExcludeFlyers || !p.Flyer)).ToList();
                if (hits.Count == 0) { log.Add($"'{sg.Title}' / {set.Name}: no hits, skipped"); continue; }

                // reference for X/Y offsets: the aim point if marked, otherwise the hit centroid
                double ox = poa?.X ?? hits.Average(p => p.X);
                double oy = poa?.Y ?? hits.Average(p => p.Y);

                double? step = StepOf(set.Name) ?? StepOf(sg.Title);
                var tg = new TargetGroup { SourceFile = $"GRT:{sg.Title}#{set.Name}", DistanceM = distM };
                foreach (var p in hits)
                {
                    double xMoa = (p.X - ox) * w * mmPerPx * mmToMoa;
                    double yMoa = -(p.Y - oy) * h * mmPerPx * mmToMoa;   // image Y grows down; target "up" is +Y
                    tg.Impacts.Add(new Impact(xMoa, yMoa));
                }
                tg.ChargeGrains = step;

                int flyers = set.Points.Count(p => p.Flyer && !p.PointOfAim);
                string tag = step is { } s ? $"step {s:0.0##}" : $"ordinal {groupIdx}";
                log.Add($"'{sg.Title}' / {set.Name}: {tg.Impacts.Count} hits @ {distM:0} m, {tag}"
                        + (flyers > 0 ? $" ({flyers} flyer{(flyers > 1 ? "s" : "")}{(opt.ExcludeFlyers ? " excluded" : " kept")})" : ""));
                outp.Add(tg);
            }
        }

        if (outp.Count == 0 && tabIdx == 0)
            log.Add("no shot-group tabs in this load — add one in GRT (target image + reference distance + hits).");
        return (outp, log);
    }

    private static double? StepOf(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s, @"(?:salto|jump|seat\w*|cbto|coal|profond\w*|caric\w*|charge|load)?\s*[:=]?\s*(-?\d+(?:[.,]\d+)?)",
            RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : null;
    }
}
