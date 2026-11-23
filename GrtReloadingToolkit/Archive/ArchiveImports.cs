using System.Globalization;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Archive;

/// <summary>One shot of a chronograph string as it goes into the archive.</summary>
public sealed record ChronoShot(double VelocityMps, string? Time, string? Note);

/// <summary>One chronograph string (one export file) as it goes into the archive; <see cref="Notes"/> keeps the device's own
/// summary (average, its SD, ES, power factor…) in the words the import wrote it.</summary>
public sealed record ChronoString(string Label, double? ChargeGr, double? TempC, string Notes, IReadOnlyList<ChronoShot> Shots,
    string SourceFile, byte[] SourceBytes);

public sealed record ChronoImportResult(long SessionId, int Strings, int Shots, IReadOnlyList<string> AlreadyInArchive);

/// <summary>The archive's side of the imports: a chronograph session goes here first, and to GRT only if the shooter asks.</summary>
public static class ArchiveImports
{
    /// <summary>The archive shooter for the launcher's shooter (an id of the toolkit's own database), or the main one.</summary>
    public static long ShooterFor(ArchiveDb a, long? toolkitShooterId)
    {
        if (toolkitShooterId is { } t && a.ScalarP("SELECT id FROM shooters WHERE origin=$o", ("$o", $"reloading_log.shooters#{t}")) is long id) return id;
        return a.ScalarP("SELECT id FROM shooters WHERE origin IN ('reloading_log.shooters#1','creato: tiratore principale') ORDER BY id LIMIT 1") as long?
               ?? a.Insert("shooters", ("name", "Io"), ("origin", "creato: tiratore principale"));
    }

    /// <summary>The shooter's loads in the archive, newest first, as (id, "name · caliber"). The toolkit's own snapshot files
    /// (…_toolkit_…, …_athlon_…, …_shotmarker_…) stay in the archive but are not offered: a new session belongs to the load.</summary>
    public static List<(long Id, string Label)> LoadsOf(ArchiveDb a, long shooterId)
    {
        var l = new List<(long, string)>();
        using var cmd = a.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, caliber FROM loads WHERE shooter_id=$s AND archived=0 " +
                          "AND instr(lower(name),'_toolkit_')=0 AND instr(lower(name),'_athlon_')=0 AND instr(lower(name),'_shotmarker_')=0 " +
                          "ORDER BY IFNULL(created_at,'') DESC, id DESC";
        cmd.Parameters.AddWithValue("$s", shooterId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string cal = r.IsDBNull(2) ? "" : r.GetString(2);
            l.Add((r.GetInt64(0), r.GetString(1) + (cal.Length > 0 ? " · " + cal : "")));
        }
        return l;
    }

    /// <summary>A new load typed by the shooter (no GRT file behind it).</summary>
    public static long NewLoad(ArchiveDb a, long shooterId, string name, string caliber)
        => a.Insert("loads", ("shooter_id", shooterId), ("name", name.Trim()), ("caliber", caliber.Trim()), ("created_at", ArchiveDb.Now()),
            ("origin", "inserito a mano"));

    /// <summary>
    /// The archive load for a GRT file: the one already made from that file (or from the file the toolkit's snapshot was
    /// taken from), else a new one, with the file kept inside the archive and its components filed in the library.
    /// </summary>
    public static long LoadForGrtFile(ArchiveDb a, string grtPath, long shooterId, List<string> log)
    {
        string pristine = GrtLoadDoc.PristineBasePath(grtPath);
        foreach (string p in new[] { grtPath, pristine }.Distinct(StringComparer.OrdinalIgnoreCase))
            if (a.ScalarP("SELECT id FROM loads WHERE grtload_path=$p COLLATE NOCASE AND archived=0 ORDER BY id DESC LIMIT 1", ("$p", p)) is long id)
                return id;

        string path = File.Exists(pristine) ? pristine : grtPath;
        byte[] data = File.ReadAllBytes(path);
        GrtLoadDoc doc;
        using (var ms = new MemoryStream(data)) doc = GrtLoadDoc.Load(ms, path);
        long fileId = a.AddFile(data, Path.GetFileName(path), "carico GRT", path, File.GetLastWriteTime(path), out _);
        long load = a.Insert("loads", ("shooter_id", shooterId), ("name", Path.GetFileNameWithoutExtension(path)), ("caliber", doc.CaliberName),
            ("firearm_text", doc.GunName), ("bullet", doc.ProjectileName), ("bullet_weight_gr", doc.BulletMassGr),
            ("powder", (doc.PropellantMaker + " " + doc.PropellantName).Trim()), ("charge_gr", doc.PropellantChargeGr), ("coal_mm", doc.CoalMm),
            ("ba", doc.PropellantBa), ("grtload_file_id", fileId), ("grtload_path", path), ("created_at", ArchiveDb.Now()),
            ("origin", "grt:" + path));
        ComponentLibrary.FromArchivedGrtLoads(a, log, load);
        log.Add($"nuovo carico nell'archivio dal file di GRT: {Path.GetFileName(path)}");
        return load;
    }

    /// <summary>The export files of <paramref name="strings"/> that the archive already holds, by content.</summary>
    public static List<string> AlreadyImported(ArchiveDb a, IEnumerable<ChronoString> strings)
        => strings.Where(s => a.ScalarP("SELECT 1 FROM strings st JOIN files f ON st.origin = 'cronografo:' || f.sha256 WHERE f.sha256=$h",
                ("$h", Sha(s.SourceBytes))) != null)
            .Select(s => Path.GetFileName(s.SourceFile)).ToList();

    /// <summary>
    /// Writes one range session: the session (date, shooter, load, the temperature when every string shares it), one string
    /// per export with its own temperature, every shot with its clock time, and each export file kept as it was. The
    /// archive becomes the master: from here on it holds what no other database does. One transaction: all of it, or nothing.
    /// </summary>
    public static ChronoImportResult AddChronoSession(ArchiveDb a, long shooterId, long? loadId, DateTime date, string firearmText,
        IReadOnlyList<ChronoString> strings, string device)
    {
        if (strings.Count == 0) throw new ArgumentException("Nessuna serie da importare.");
        var already = AlreadyImported(a, strings);
        long sid = 0;
        int nShots = 0;
        a.InTransaction(() =>
        {
            var temps = strings.Select(s => s.TempC).Distinct().ToList();
            double? sessionTemp = temps.Count == 1 ? temps[0] : null;
            sid = a.Insert("sessions", ("shooter_id", shooterId), ("load_id", loadId), ("firearm_text", firearmText),
                ("date", date.ToString("yyyy-MM-dd")), ("date_estimated", 0), ("temp_c", sessionTemp),
                ("notes", $"Importata dal cronografo ({device}): {strings.Count} serie."), ("created_at", ArchiveDb.Now()),
                ("origin", "import cronografo " + ArchiveDb.Now()));
            foreach (var s in strings.OrderBy(s => s.ChargeGr ?? double.MaxValue))
            {
                a.AddFile(s.SourceBytes, Path.GetFileName(s.SourceFile), "export cronografo", s.SourceFile,
                    File.Exists(s.SourceFile) ? File.GetLastWriteTime(s.SourceFile) : DateTime.Now, out _);
                long st = a.Insert("strings", ("session_id", sid), ("label", s.Label), ("charge_gr", s.ChargeGr), ("temp_c", s.TempC),
                    ("device", device), ("notes", s.Notes), ("content_key", Sha(s.SourceBytes)[..16]), ("origin", "cronografo:" + Sha(s.SourceBytes)));
                for (int i = 0; i < s.Shots.Count; i++)
                {
                    var sh = s.Shots[i];
                    string? note = string.Join(" ", new[] { sh.Time is { Length: > 0 } t ? "ora " + t.Trim() : null, sh.Note?.Trim() }
                        .Where(x => !string.IsNullOrEmpty(x)));
                    a.Insert("shots", ("string_id", st), ("idx", i + 1), ("velocity_mps", sh.VelocityMps), ("note", string.IsNullOrEmpty(note) ? null : note));
                    nShots++;
                }
            }
            a.Insert("imports", ("at", ArchiveDb.Now()), ("source", "cronografo " + device),
                ("path", string.Join(" | ", strings.Select(s => s.SourceFile))),
                ("summary", $"{strings.Count} serie, {nShots} colpi" + (already.Count > 0 ? $"; già nell'archivio prima: {string.Join(", ", already)}" : "")));
            a.MakeMaster();
        });
        return new ChronoImportResult(sid, strings.Count, nShots, already);
    }

    /// <summary>Where a ShotMarker string sits in the archive: the device's own id and time, which tell it apart for good.</summary>
    public static string ShotMarkerOrigin(ShotMarker.Core.Sm.SmString s) => $"shotmarker:{s.Id}:{s.Timestamp:O}";

    public static bool ShotMarkerAlreadyImported(ArchiveDb a, ShotMarker.Core.Sm.SmString s)
        => a.ScalarP("SELECT 1 FROM sessions WHERE origin=$o", ("$o", ShotMarkerOrigin(s))) != null;

    /// <summary>
    /// One ShotMarker string as one session of the archive, in one transaction: the device's date (a real one, not a file
    /// date) and distance, a chronograph string of the shots that carry a velocity, a group of every plotted shot that is
    /// not a sighter (in mm from the target centre, the aim; shots left out on the device or by the shooter kept as flyers),
    /// each hit linked to its velocity, the target picture and the export file itself. The archive becomes the master.
    /// </summary>
    public static ChronoImportResult AddShotMarkerString(ArchiveDb a, long shooterId, long? loadId, ShotMarker.Core.Sm.SmString s,
        double? chargeGr, string exportPath, byte[] exportBytes, byte[]? picturePng)
    {
        var already = ShotMarkerAlreadyImported(a, s) ? new List<string> { s.Name } : new List<string>();
        long sid = 0;
        int nShots = 0;
        a.InTransaction(() =>
        {
            var shots = s.Shots.Where(x => x.IsImported).OrderBy(x => x.Number).ToList();
            var temps = shots.Where(x => x.TempC is { } t && double.IsFinite(t)).Select(x => x.TempC!.Value).ToList();
            double? temp = temps.Count > 0 ? Math.Round(temps.Average(), 1) : null;
            a.AddFile(exportBytes, Path.GetFileName(exportPath), "export ShotMarker", exportPath,
                File.Exists(exportPath) ? File.GetLastWriteTime(exportPath) : DateTime.Now, out _);
            long? pic = picturePng is { Length: > 0 }
                ? a.AddFile(picturePng, Recovery.HistoryRecovery.SafeName("ShotMarker " + s.Name) + ".png", "foto bersaglio", exportPath, DateTime.Now, out _)
                : null;

            sid = a.Insert("sessions", ("shooter_id", shooterId), ("load_id", loadId), ("date", s.Timestamp.LocalDateTime.ToString("yyyy-MM-dd")),
                ("date_estimated", 0), ("distance_m", s.DistanceMetres), ("temp_c", temp),
                ("notes", $"Importata da ShotMarker: {s.Name} ({s.Timestamp.LocalDateTime:yyyy-MM-dd HH:mm}), bersaglio {s.FaceId}"
                          + (s.ScoreText is { Length: > 0 } sc ? $", punteggio {sc}" : "") + (s.FiringPoint is { } fp ? $", postazione {fp}" : "") + "."),
                ("created_at", ArchiveDb.Now()), ("origin", ShotMarkerOrigin(s)));

            var shotIds = new Dictionary<int, long>();
            var withVel = shots.Where(x => x.VelocityMps is { } v && double.IsFinite(v) && v > 0).ToList();
            if (withVel.Count > 0)
            {
                long st = a.Insert("strings", ("session_id", sid), ("label", chargeGr is { } g ? Grains(g) : s.Name), ("charge_gr", chargeGr),
                    ("temp_c", temp), ("device", "ShotMarker"), ("notes", s.Name), ("content_key", Sha(exportBytes)[..16]),
                    ("origin", ShotMarkerOrigin(s)));
                int i = 0;
                foreach (var x in withVel)
                {
                    string note = $"colpo {x.Number}" + (x.Score is { Length: > 0 } sco ? $", punti {sco}" : "")
                                  + (x.TempC is { } t && double.IsFinite(t) ? $", {t.ToString("0.#", CultureInfo.InvariantCulture)} °C" : "")
                                  + (x.IsFlyer ? ", escluso" : "");
                    shotIds[x.Number] = a.Insert("shots", ("string_id", st), ("idx", ++i), ("velocity_mps", x.VelocityMps!.Value), ("note", note),
                        ("set_aside", x.IsFlyer ? 1 : 0), ("set_aside_reason", x.IsFlyer ? "escluso su ShotMarker o dal tiratore" : null));
                    nShots++;
                }
            }
            if (shots.Count > 0)
            {
                long g = a.Insert("shot_groups", ("session_id", sid), ("label", s.Name), ("tab", "ShotMarker"), ("distance_m", s.DistanceMetres),
                    ("step", chargeGr), ("origin_point", "punto di mira"), ("picture_file_id", pic), ("content_key", Sha(exportBytes)[..16]),
                    ("origin", ShotMarkerOrigin(s)));
                int i = 0;
                foreach (var x in shots)
                    a.Insert("hits", ("group_id", g), ("idx", ++i), ("x_mm", x.XMm), ("y_mm", x.YMm), ("flyer", x.IsFlyer ? 1 : 0),
                        ("shot_id", shotIds.TryGetValue(x.Number, out long sh) ? sh : null));
            }
            a.Insert("imports", ("at", ArchiveDb.Now()), ("source", "ShotMarker"), ("path", exportPath),
                ("summary", $"{s.Name}: {shots.Count} colpi sul bersaglio, {nShots} con velocità" + (already.Count > 0 ? "; già nell'archivio prima" : "")));
            a.MakeMaster();
        });
        return new ChronoImportResult(sid, nShots > 0 ? 1 : 0, nShots, already);
    }

    public static string Sha(byte[] data) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>The earliest last-write date among the export files: the day the strings were most likely shot, since the
    /// exports carry clock times only.</summary>
    public static DateTime LikelyDate(IEnumerable<string> files)
        => files.Where(File.Exists).Select(File.GetLastWriteTime).DefaultIfEmpty(DateTime.Today).Min().Date;

    public static string Grains(double? g) => g is { } v ? v.ToString("0.##", CultureInfo.InvariantCulture) + " gr" : "";
}
