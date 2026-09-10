using System.Globalization;
using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;
using S = GrtPluginKit.Util.Str;

namespace GrtReloadingToolkit.Athlon;

/// <summary>Turns parsed Athlon strings (+ per-string temperature in °C) into GRT charges.</summary>
internal static class ImportBuilder
{
    public const string MeasurementTitlePrefix = "Athlon ";
    private const double GrainToKg = 0.00006479891;

    public sealed record Row(AthlonString String_, double? TempC, bool Include);

    public static List<GrtCharge> Build(IEnumerable<Row> rows, double? baseChargeGrainsFallback, bool perShotTemp)
    {
        var included = rows.Where(r => r.Include).ToList();
        included.Sort((a, b) =>
            (a.String_.ChargeGrains ?? double.MaxValue).CompareTo(b.String_.ChargeGrains ?? double.MaxValue));

        var charges = new List<GrtCharge>(included.Count);
        foreach (Row row in included)
        {
            AthlonString s = row.String_;
            var vel = s.Velocities.ToList();
            StringStats stats = StringStats.From(vel);

            double? grains = s.ChargeGrains ?? baseChargeGrainsFallback;
            double kg = grains is { } g ? g * GrainToKg : 0.002;

            var c = new GrtCharge
            {
                Name = BuildName(s, grains, row.TempC),
                ValueKg = kg,
                Note = BuildChargeNote(s, stats, row.TempC),
            };
            for (int i = 0; i < s.Shots.Count; i++)
            {
                ShotMeta m = s.Shots[i];
                var pieces = new List<string>();
                if (!string.IsNullOrWhiteSpace(m.Time)) pieces.Add("TIME=" + m.Time!.Trim());
                if (!string.IsNullOrWhiteSpace(m.Note)) pieces.Add("NOTE=" + m.Note!.Trim());
                if (perShotTemp && row.TempC is { } tc) pieces.Add("TEMP=" + S.Num(tc, 1) + "°C");
                c.Shots.Add(new GrtShot(m.VelocityMps, pieces.Count > 0 ? string.Join(' ', pieces) : null));
            }
            charges.Add(c);
        }
        return charges;
    }

    private static string BuildName(AthlonString s, double? grains, double? tempC)
    {
        string head = grains is { } g
            ? g.ToString("0.0#", CultureInfo.InvariantCulture) + " gr"
            : Path.GetFileNameWithoutExtension(s.SourceFile);
        return tempC is { } t ? $"{head} @ {S.Num(t, 1)} °C" : head;
    }

    private static string BuildChargeNote(AthlonString s, StringStats stats, double? tempC)
    {
        string unit = s.SourceUnit == "fps" ? "ft/s" : "m/s";
        double avg = s.DeviceAvg ?? ToFileUnit(stats.Mean, s.SourceUnit);
        double sd = s.DeviceSd ?? ToFileUnit(stats.Sd, s.SourceUnit);
        double es = s.DeviceEs ?? ToFileUnit(stats.Es, s.SourceUnit);

        var p = new List<string>
        {
            "AVG-E=" + S.Num(avg, 1), "SD=" + S.Num(sd, 1), "ES=" + S.Num(es, 1),
            "N=" + stats.N.ToString(CultureInfo.InvariantCulture), "UNIT=" + unit,
        };
        if (!string.IsNullOrWhiteSpace(s.DevicePowerFactor)) p.Add("PF=" + s.DevicePowerFactor!.Trim());
        if (!string.IsNullOrWhiteSpace(s.DeviceEnergy)) p.Add("KE=" + s.DeviceEnergy!.Trim());
        if (tempC is { } t) p.Add("T=" + S.Num(t, 1) + "°C");
        if (!string.IsNullOrWhiteSpace(s.SessionNote)) p.Add("SESSION=" + s.SessionNote!.Trim());
        p.Add("SRC=Athlon Rangecraft");
        return string.Join(' ', p);
    }

    private static double ToFileUnit(double mps, string unit) => unit == "fps" ? mps * 3.28084 : mps;
}
