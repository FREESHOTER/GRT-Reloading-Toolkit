using System.Globalization;
using System.Text.RegularExpressions;
using GrtReloadingToolkit.Log;
using GrtReloadingToolkit.Ocw;

namespace GrtReloadingToolkit.Archive;

/// <summary>One range session of the archive, as the pickers list it.</summary>
public sealed record ArchiveSessionInfo(long Id, string Date, bool DateEstimated, long? LoadId, string LoadName, string Caliber,
    int Strings, int Shots, int Groups, string Notes);

public sealed record ArchiveShot(double VelocityMps, string? Note);

/// <summary>One chronograph string of the archive, every shot in firing order.</summary>
public sealed record ArchiveString(long Id, long SessionId, string Label, double? ChargeGr, double? TempC, string Notes, IReadOnlyList<ArchiveShot> Shots)
{
    public List<double> Velocities => Shots.Select(s => s.VelocityMps).ToList();
}

public sealed record ArchiveHit(double? XMm, double? YMm, bool Flyer);

/// <summary>One shot group of the archive; hits in mm from the aim point (or the centre of the hits), +X right, +Y up.</summary>
public sealed record ArchiveGroup(long Id, long SessionId, string Label, string Tab, double? DistanceM, double? Step, string OriginPoint,
    IReadOnlyList<ArchiveHit> Hits, string? Warning);

/// <summary>What the analysis tools read from the archive, and where they keep their results.</summary>
public static class ArchiveQueries
{
    /// <summary>The shooter's sessions that hold strings or groups, newest first; only one load's when <paramref name="loadId"/> is set.</summary>
    public static List<ArchiveSessionInfo> Sessions(ArchiveDb a, long shooterId, long? loadId = null)
    {
        var list = new List<ArchiveSessionInfo>();
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = @"
SELECT s.id, s.date, s.date_estimated, s.load_id, IFNULL(l.name,''), IFNULL(l.caliber,''),
       (SELECT COUNT(*) FROM strings st WHERE st.session_id=s.id),
       (SELECT COUNT(*) FROM shots sh JOIN strings st ON st.id=sh.string_id WHERE st.session_id=s.id),
       (SELECT COUNT(*) FROM shot_groups g WHERE g.session_id=s.id), IFNULL(s.notes,'')
FROM sessions s LEFT JOIN loads l ON l.id=s.load_id
WHERE s.shooter_id=$sh AND ($l IS NULL OR s.load_id=$l)
  AND (EXISTS(SELECT 1 FROM strings st WHERE st.session_id=s.id) OR EXISTS(SELECT 1 FROM shot_groups g WHERE g.session_id=s.id))
ORDER BY s.date DESC, s.id DESC";
        cmd.Parameters.AddWithValue("$sh", shooterId);
        cmd.Parameters.AddWithValue("$l", (object?)loadId ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ArchiveSessionInfo(r.GetInt64(0), r.GetString(1), r.GetInt64(2) != 0, r.IsDBNull(3) ? null : r.GetInt64(3),
                r.GetString(4), r.GetString(5), (int)r.GetInt64(6), (int)r.GetInt64(7), (int)r.GetInt64(8), r.GetString(9)));
        return list;
    }

    /// <summary>The shooter's loads that have at least one session with strings or groups, as (id, "name · caliber").</summary>
    public static List<(long Id, string Label)> LoadsWithSessions(ArchiveDb a, long shooterId)
        => Sessions(a, shooterId).Where(s => s.LoadId != null).GroupBy(s => s.LoadId!.Value)
            .Select(g => (g.Key, g.First().LoadName + (g.First().Caliber.Length > 0 ? " · " + g.First().Caliber : "")))
            .OrderBy(x => x.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static List<ArchiveString> Strings(ArchiveDb a, long sessionId)
    {
        var heads = new List<(long Id, string Label, double? Charge, double? Temp, string Notes)>();
        using (var cmd = a.Connection.CreateCommand())
        {
            cmd.Transaction = a.CurrentTransaction;
            cmd.CommandText = "SELECT id, IFNULL(label,''), charge_gr, temp_c, IFNULL(notes,'') FROM strings WHERE session_id=$s ORDER BY id";
            cmd.Parameters.AddWithValue("$s", sessionId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) heads.Add((r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetDouble(2), r.IsDBNull(3) ? null : r.GetDouble(3), r.GetString(4)));
        }
        var list = new List<ArchiveString>();
        foreach (var h in heads)
        {
            var shots = new List<ArchiveShot>();
            using var cmd = a.Connection.CreateCommand();
            cmd.Transaction = a.CurrentTransaction;
            cmd.CommandText = "SELECT velocity_mps, note FROM shots WHERE string_id=$i ORDER BY idx";
            cmd.Parameters.AddWithValue("$i", h.Id);
            using var r = cmd.ExecuteReader();
            while (r.Read()) shots.Add(new ArchiveShot(r.GetDouble(0), r.IsDBNull(1) ? null : r.GetString(1)));
            list.Add(new ArchiveString(h.Id, sessionId, h.Label, h.Charge, h.Temp, h.Notes, shots));
        }
        return list;
    }

    public static List<ArchiveGroup> Groups(ArchiveDb a, long sessionId)
    {
        var heads = new List<(long Id, string Label, string Tab, double? Dist, double? Step, string Origin, string? Warning)>();
        using (var cmd = a.Connection.CreateCommand())
        {
            cmd.Transaction = a.CurrentTransaction;
            cmd.CommandText = "SELECT id, IFNULL(label,''), IFNULL(tab,''), distance_m, step, IFNULL(origin_point,''), warning FROM shot_groups WHERE session_id=$s ORDER BY id";
            cmd.Parameters.AddWithValue("$s", sessionId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                heads.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetDouble(3), r.IsDBNull(4) ? null : r.GetDouble(4),
                    r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));
        }
        var list = new List<ArchiveGroup>();
        foreach (var h in heads)
        {
            var hits = new List<ArchiveHit>();
            using var cmd = a.Connection.CreateCommand();
            cmd.Transaction = a.CurrentTransaction;
            cmd.CommandText = "SELECT x_mm, y_mm, flyer FROM hits WHERE group_id=$g ORDER BY idx";
            cmd.Parameters.AddWithValue("$g", h.Id);
            using var r = cmd.ExecuteReader();
            while (r.Read()) hits.Add(new ArchiveHit(r.IsDBNull(0) ? null : r.GetDouble(0), r.IsDBNull(1) ? null : r.GetDouble(1), r.GetInt64(2) != 0));
            list.Add(new ArchiveGroup(h.Id, sessionId, h.Label, h.Tab, h.Dist, h.Step, h.Origin, hits, h.Warning));
        }
        return list;
    }

    /// <summary>
    /// An archive group as the analysis tools take it: hits in MOA at the group's distance, the step (charge or seating)
    /// it was shot at. Null when the group cannot be measured: no distance, or no scale in mm (a GRT tab without its
    /// reference line keeps only the picture positions). Flyers marked in GRT stay unless <paramref name="dropFlyers"/>.
    /// </summary>
    public static TargetGroup? ToTargetGroup(ArchiveGroup g, bool dropFlyers = false)
    {
        if (g.DistanceM is not > 0 || g.Hits.Any(h => h.XMm is null || h.YMm is null)) return null;
        double d = g.DistanceM.Value;
        var t = new TargetGroup { SourceFile = "archivio: " + (g.Tab.Length > 0 && g.Tab != g.Label ? g.Tab + " · " : "") + g.Label, DistanceM = d, ChargeGrains = g.Step };
        foreach (var h in g.Hits.Where(h => !dropFlyers || !h.Flyer))
            t.Impacts.Add(new Impact(TargetGroup.MmToMoa(h.XMm!.Value, d), TargetGroup.MmToMoa(h.YMm!.Value, d)));
        // the origin is the aim point when GRT had one marked: it is the zero of these coordinates
        if (g.OriginPoint == "punto di mira") { t.AimXMoa = 0; t.AimYMoa = 0; }
        return t.Impacts.Count > 0 ? t : null;
    }

    private static readonly Regex OraToken = new(@"\bora\s+(\d{1,2}:\d{2}:\d{2}(?:\s*[AaPp][Mm])?)", RegexOptions.Compiled);

    /// <summary>The clock time of a shot in seconds from midnight, from a note as GRT keeps it ("TIME=10:14:02 AM") or as the
    /// archive import writes it ("ora 10:14:02 AM").</summary>
    public static double? ClockSeconds(string? note)
    {
        if (ShotDrift.ClockSeconds(note) is { } s) return s;
        var m = note == null ? null : OraToken.Match(note);
        return m is { Success: true } ? ShotDrift.ClockSeconds("TIME=" + m.Groups[1].Value) : null;
    }

    /// <summary>
    /// Keeps the result of an analysis in the archive: which tool and version, what it read (the archive's ids or the
    /// files), and the report it wrote. The archive becomes the master: it now holds something no other database does.
    /// </summary>
    public static long SaveAnalysis(ArchiveDb a, string tool, string toolVersion, long? loadId, long? sessionId, string inputs, string result,
        (byte[] Data, string Name)? pdf = null)
    {
        long id = 0;
        a.InTransaction(() =>
        {
            long? fileId = pdf is { } f ? a.AddFile(f.Data, f.Name, "rapporto PDF", null, DateTime.Now, out _) : null;
            id = a.Insert("analyses", ("tool", tool), ("tool_version", toolVersion), ("created_at", ArchiveDb.Now()),
                ("load_id", loadId), ("session_id", sessionId), ("inputs", inputs), ("result", result), ("origin", "analisi " + tool),
                ("report_file_id", fileId));
            a.MakeMaster();
        });
        return id;
    }

    /// <summary>What a report is about, as label/value lines: shooter, load (with its components), session (date, place
    /// conditions). Empty values are left out by the report.</summary>
    public static List<(string Label, string Value)> ReportFacts(ArchiveDb a, long? loadId, long? sessionId)
    {
        var facts = new List<(string, string)>();
        var c = CultureInfo.GetCultureInfo("it-IT");
        static string N(object? o) => o is null or DBNull ? "" : Convert.ToString(o, CultureInfo.InvariantCulture) ?? "";
        string F(object? o, string fmt, string unit) => o is null or DBNull ? "" : Convert.ToDouble(o, CultureInfo.InvariantCulture).ToString(fmt, c) + unit;
        if (sessionId is { } sid && loadId == null)
            loadId = a.ScalarP("SELECT load_id FROM sessions WHERE id=$i", ("$i", sid)) as long?;
        using (var cmd = a.Connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT s.name, l.name, l.caliber, l.firearm_text, l.bullet, l.bullet_weight_gr, l.powder, l.charge_gr,
                l.primer, l.brass, l.coal_mm, l.cbto_mm FROM loads l JOIN shooters s ON s.id=l.shooter_id WHERE l.id=$i";
            cmd.Parameters.AddWithValue("$i", (object?)loadId ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            if (loadId != null && r.Read())
            {
                facts.Add(("Tiratore", N(r[0])));
                facts.Add(("Carico", N(r[1])));
                facts.Add(("Calibro", N(r[2])));
                facts.Add(("Arma", N(r[3])));
                // GRT's bullet names often carry the weight already ("… 100.00 grain"): not written twice
                string bullet = N(r[4]);
                string weight = r[5] is null or DBNull || Regex.IsMatch(bullet, @"\d\s*(gr|grain|grs)\b", RegexOptions.IgnoreCase) ? "" : F(r[5], "0.#", " gr");
                facts.Add(("Palla", string.Join(" ", new[] { bullet, weight }.Where(x => x != ""))));
                facts.Add(("Polvere", string.Join(" ", new[] { N(r[6]), F(r[7], "0.0#", " gr") }.Where(x => x != ""))));
                facts.Add(("Innesco", N(r[8])));
                facts.Add(("Bossolo", N(r[9])));
                facts.Add(("COAL / CBTO", string.Join(" / ", new[] { F(r[10], "0.00", " mm"), F(r[11], "0.00", " mm") }.Where(x => x != ""))));
            }
        }
        if (sessionId != null)
        {
            using var cmd = a.Connection.CreateCommand();
            cmd.CommandText = @"SELECT date, date_estimated, firearm_text, distance_m, temp_c, pressure_hpa, humidity_pct, notes
                FROM sessions WHERE id=$i";
            cmd.Parameters.AddWithValue("$i", sessionId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                facts.Add(("Seduta", N(r[0]) + (Convert.ToInt64(r[1] is DBNull ? 0L : r[1]) != 0 ? " (data stimata)" : "")));
                int arma = facts.FindIndex(f => f.Item1 == "Arma");
                if (arma < 0) facts.Add(("Arma", N(r[2])));
                else if (facts[arma].Item2 == "") facts[arma] = ("Arma", N(r[2]));
                facts.Add(("Distanza", F(r[3], "0", " m")));
                facts.Add(("Condizioni", string.Join(", ", new[] { F(r[4], "0.#", " °C"), F(r[5], "0", " hPa"), F(r[6], "0", " % umidità") }
                    .Where(x => x != ""))));
            }
        }
        return facts;
    }
}
