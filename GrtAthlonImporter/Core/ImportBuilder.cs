using System.Globalization;
using GrtAthlonImporter.Grt;

namespace GrtAthlonImporter.Core;

/// <summary>Turns parsed Athlon strings (+ per-string temperature in °C) into GRT charges.</summary>
internal static class ImportBuilder
{
    public sealed record Row(AthlonString String_, double? TempC, bool Include);

    public static List<ChargeInput> Build(IEnumerable<Row> rows, double? baseChargeGrainsFallback, bool perShotTemp)
    {
        var included = rows.Where(r => r.Include).ToList();

        // order charges by charge weight when known, else keep file order
        included.Sort((a, b) =>
        {
            double ga = a.String_.ChargeGrains ?? double.MaxValue;
            double gb = b.String_.ChargeGrains ?? double.MaxValue;
            return ga.CompareTo(gb);
        });

        var charges = new List<ChargeInput>(included.Count);
        foreach (Row row in included)
        {
            AthlonString s = row.String_;
            var vel = s.Velocities.ToList();
            StringStats stats = StringStats.From(vel);

            double? grains = s.ChargeGrains ?? baseChargeGrainsFallback;
            double kg = grains is { } g ? GrtLoadWriter.GrainsToKg(g) : 0.002;

            string name = BuildName(s, grains, row.TempC);
            string note = BuildChargeNote(s, stats, row.TempC);

            var shots = new List<(double vMps, string? note)>(vel.Count);
            for (int i = 0; i < s.Shots.Count; i++)
            {
                ShotMeta m = s.Shots[i];
                var pieces = new List<string>();
                if (!string.IsNullOrWhiteSpace(m.Time)) pieces.Add("TIME=" + m.Time!.Trim());
                if (!string.IsNullOrWhiteSpace(m.Note)) pieces.Add("NOTE=" + m.Note!.Trim());
                if (perShotTemp && row.TempC is { } tc) pieces.Add("TEMP=" + Str.Num(tc, 1) + "°C");
                shots.Add((m.VelocityMps, pieces.Count > 0 ? string.Join(' ', pieces) : null));
            }

            charges.Add(new ChargeInput { Name = name, ValueKg = kg, NoteText = note, Shots = shots });
        }
        return charges;
    }

    private static string BuildName(AthlonString s, double? grains, double? tempC)
    {
        string head = grains is { } g
            ? g.ToString("0.0#", CultureInfo.InvariantCulture) + " gr"
            : Path.GetFileNameWithoutExtension(s.SourceFile);
        return tempC is { } t ? $"{head} @ {Str.Num(t, 1)} °C" : head;
    }

    private static string BuildChargeNote(AthlonString s, StringStats stats, double? tempC)
    {
        string unit = s.SourceUnit == "fps" ? "ft/s" : "m/s";
        double avg = s.DeviceAvg ?? ToFileUnit(stats.Mean, s.SourceUnit);
        double sd = s.DeviceSd ?? ToFileUnit(stats.Sd, s.SourceUnit);
        double es = s.DeviceEs ?? ToFileUnit(stats.Es, s.SourceUnit);

        var p = new List<string>
        {
            "AVG-E=" + Str.Num(avg, 1),
            "SD=" + Str.Num(sd, 1),
            "ES=" + Str.Num(es, 1),
            "N=" + stats.N.ToString(CultureInfo.InvariantCulture),
            "UNIT=" + unit,
        };
        if (!string.IsNullOrWhiteSpace(s.DevicePowerFactor)) p.Add("PF=" + s.DevicePowerFactor!.Trim());
        if (!string.IsNullOrWhiteSpace(s.DeviceEnergy)) p.Add("KE=" + s.DeviceEnergy!.Trim());
        if (tempC is { } t) p.Add("T=" + Str.Num(t, 1) + "°C");
        if (!string.IsNullOrWhiteSpace(s.SessionNote)) p.Add("SESSION=" + s.SessionNote!.Trim());
        p.Add("SRC=Athlon Rangecraft");
        return string.Join(' ', p);
    }

    private static double ToFileUnit(double mps, string unit) => unit == "fps" ? mps * 3.28084 : mps;
}
