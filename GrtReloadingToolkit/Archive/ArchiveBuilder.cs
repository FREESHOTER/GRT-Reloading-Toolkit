using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrtReloadingToolkit.Log;
using GrtReloadingToolkit.Recovery;
using Microsoft.Data.Sqlite;

namespace GrtReloadingToolkit.Archive;

/// <summary>What the archive is built from. Every path may be missing; a missing source is reported, not guessed.</summary>
public sealed class ArchiveSources
{
    /// <summary>The toolkit's own inventory/journal database (reloading_log.db).</summary>
    public string? LogDb { get; init; }
    /// <summary>"Segui un carico" (segui_carico.db).</summary>
    public string? TrackerDb { get; init; }
    /// <summary>Barrel Vibration Model lab (model_lab.db): kept verbatim only, it belongs to that tool.</summary>
    public string? ModelLabDb { get; init; }
    /// <summary>Folders written by <c>--recupera</c> (recupero.json + foto\), most important first: an item already taken
    /// from an earlier folder is not taken again from a later one.</summary>
    public List<string> RecoveryFolders { get; init; } = new();
    /// <summary>Empirical Precision's master-db.json (Apache 2.0), with LICENSE and FONTE.txt beside it; null or missing to leave it out.</summary>
    public string? EpLibrary { get; init; }

    public static ArchiveSources Defaults(IEnumerable<string> recoveryFolders)
    {
        string app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GRTPlugins");
        return new ArchiveSources
        {
            LogDb = Path.Combine(app, "reloading_log.db"),
            TrackerDb = Path.Combine(app, "segui_carico.db"),
            ModelLabDb = Path.Combine(app, "model_lab.db"),
            RecoveryFolders = recoveryFolders.ToList(),
            EpLibrary = Path.Combine(ArchivePaths.Folder, "librerie", "empirical_precision", "master-db.json"),
        };
    }
}

/// <summary>One line of the build check: what the sources hold against what landed in the archive.</summary>
public sealed record ArchiveCheck(string What, double Expected, double Found)
{
    public bool Ok => Math.Abs(Expected - Found) <= 1e-6 * Math.Max(1, Math.Abs(Expected));
}

public sealed class ArchiveBuildReport
{
    public List<string> Log { get; } = new();
    public List<ArchiveCheck> Checks { get; } = new();
    public string Integrity { get; set; } = "";
    public bool ForeignKeysOk { get; set; }
    public bool Ok => Checks.All(c => c.Ok) && Integrity == "ok" && ForeignKeysOk;

    public string Text()
    {
        var sb = new StringBuilder();
        foreach (string l in Log) sb.AppendLine(l);
        sb.AppendLine();
        sb.AppendLine("CONTROLLI (atteso dalle fonti → trovato nell'archivio)");
        foreach (var c in Checks)
            sb.AppendLine($"  {(c.Ok ? "ok " : "NO!")}  {c.What,-58} {Fmt(c.Expected),12} → {Fmt(c.Found)}");
        sb.AppendLine($"  {(Integrity == "ok" ? "ok " : "NO!")}  integrità del file SQLite: {Integrity}");
        sb.AppendLine($"  {(ForeignKeysOk ? "ok " : "NO!")}  collegamenti tra le tabelle");
        sb.AppendLine();
        sb.AppendLine(Ok ? "ESITO: tutto combacia." : "ESITO: qualcosa NON combacia, vedi le righe NO!");
        return sb.ToString();
    }

    private static string Fmt(double v) => v == Math.Floor(v) && Math.Abs(v) < 1e12
        ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// Fills an empty <see cref="ArchiveDb"/> from the old databases and the history recovered from GRT, in one transaction,
/// then checks it against the sources. The sources are opened read only and never changed.
/// </summary>
public static class ArchiveBuilder
{
    private const string MainShooterFolders = "loads|(cartella principale)";

    public static ArchiveBuildReport Build(ArchiveDb a, ArchiveSources src)
    {
        if (a.Count("shooters") > 0 || a.Count("loads") > 0)
            throw new InvalidOperationException("L'archivio non è vuoto: si costruisce solo in un file nuovo.");
        var rep = new ArchiveBuildReport();
        var exp = new Expected();
        a.InTransaction(() =>
        {
            var shooters = new Dictionary<long, long>();   // old shooter id -> archive id
            var firearms = new Dictionary<long, long>();
            var comps = new Dictionary<long, long>();

            SqliteConnection? log = OpenReadOnly(src.LogDb, rep, "archivio del Toolkit (reloading_log.db)");
            SqliteConnection? trk = OpenReadOnly(src.TrackerDb, rep, "Segui un carico (segui_carico.db)");
            SqliteConnection? lab = OpenReadOnly(src.ModelLabDb, rep, "modello della canna (model_lab.db)");
            try
            {
                foreach (var (cn, name) in new[] { (log, "reloading_log.db"), (trk, "segui_carico.db"), (lab, "model_lab.db") })
                    if (cn != null) exp.Legacy += CopyLegacy(a, cn, name);

                if (log != null) FromLog(a, log, shooters, firearms, comps, exp, rep);
                if (!shooters.ContainsKey(1))
                    shooters[1] = a.Upsert("shooters", ("name", "Io"), ("origin", "creato: tiratore principale"));
                if (trk != null) FromTracker(a, trk, shooters, exp, rep);
            }
            finally { log?.Dispose(); trk?.Dispose(); lab?.Dispose(); }

            var keys = new HashSet<string>();
            foreach (string dir in src.RecoveryFolders) FromRecovery(a, dir, shooters[1], keys, exp, rep);

            exp.GrtLib = ComponentLibrary.FromArchivedGrtLoads(a, rep.Log);
            exp.GrtLoadFiles = (int)a.Count("loads", "grtload_file_id IS NOT NULL AND archived=0");
            if (src.EpLibrary is { } ep && File.Exists(ep))
            {
                exp.Ep = FromEp(a, ep, rep);
                ComponentLibrary.MatchCartridgesByDrawing(a, rep.Log);
                ComponentLibrary.SuggestBulletMatches(a, rep.Log);
            }
            else rep.Log.Add("• libreria di Empirical Precision: non trovata, saltata.");

            a.SetMeta("built_at", ArchiveDb.Now());
            a.SetMeta("built_from", string.Join(" | ", new[] { src.LogDb, src.TrackerDb, src.ModelLabDb }.Concat(src.RecoveryFolders).Where(s => s != null)));
        });
        Verify(a, exp, rep);
        return rep;
    }

    /// <summary>
    /// Brings the archive up to date with the old databases the tools still write to (inventory, journal, firearms,
    /// shooters, "Segui un carico"), in one transaction: a row already copied is updated in place (its id, and whatever
    /// points at it, stays), a new one is added, and one deleted in the old database is archived (shooters, firearms,
    /// components, loads) or removed (sessions, movements, measurements). Only rows that came from those databases are
    /// touched: imports, the GRT history and the library belong to the archive alone. A missing old database changes nothing.
    /// </summary>
    public static ArchiveBuildReport Refresh(ArchiveDb a, ArchiveSources src)
    {
        var rep = new ArchiveBuildReport();
        var exp = new Expected();
        a.InTransaction(() =>
        {
            a.Touched = new HashSet<string>();
            try
            {
                var shooters = new Dictionary<long, long>();
                var firearms = new Dictionary<long, long>();
                var comps = new Dictionary<long, long>();
                SqliteConnection? log = OpenReadOnly(src.LogDb, rep, "archivio del Toolkit (reloading_log.db)");
                SqliteConnection? trk = OpenReadOnly(src.TrackerDb, rep, "Segui un carico (segui_carico.db)");
                SqliteConnection? lab = OpenReadOnly(src.ModelLabDb, rep, "modello della canna (model_lab.db)");
                var refreshed = new List<string>();
                try
                {
                    foreach (var (cn, name) in new[] { (log, "reloading_log.db"), (trk, "segui_carico.db"), (lab, "model_lab.db") })
                        if (cn != null)
                        {
                            a.Exec("DELETE FROM legacy_rows WHERE source=$s", ("$s", name));
                            exp.Legacy += CopyLegacy(a, cn, name);
                        }
                    if (log != null) { FromLog(a, log, shooters, firearms, comps, exp, rep); refreshed.Add("reloading_log."); }
                    if (!shooters.ContainsKey(1))
                        shooters[1] = a.ScalarP("SELECT id FROM shooters WHERE origin IN ('reloading_log.shooters#1','creato: tiratore principale') ORDER BY id LIMIT 1") as long?
                                      ?? a.Upsert("shooters", ("name", "Io"), ("origin", "creato: tiratore principale"));
                    if (trk != null) { FromTracker(a, trk, shooters, exp, rep); refreshed.Add("segui_carico."); }
                }
                finally { log?.Dispose(); trk?.Dispose(); lab?.Dispose(); }

                // what was copied before and is no longer in the old database it came from
                a.Exec("CREATE TEMP TABLE IF NOT EXISTS _seen (origin TEXT PRIMARY KEY); DELETE FROM _seen;");
                foreach (string o in a.Touched) a.Exec("INSERT OR IGNORE INTO _seen(origin) VALUES($o)", ("$o", o));
                int archived = 0, removed = 0;
                foreach (string prefix in refreshed)
                {
                    string gone = $"origin LIKE '{prefix}%' AND origin NOT IN (SELECT origin FROM _seen)";
                    foreach (var (table, flag) in new[] { ("shooters", "archived"), ("firearms", "retired"), ("components", "archived"), ("loads", "archived") })
                    {
                        archived += (int)a.Count(table, $"{gone} AND {flag}=0");
                        a.Exec($"UPDATE {table} SET {flag}=1, notes=TRIM(IFNULL(notes,'') || ' (eliminato nel vecchio archivio)') WHERE {gone} AND {flag}=0");
                    }
                    foreach (string table in new[] { "ledger", "measurements", "strings", "sessions" })
                    {
                        removed += (int)a.Count(table, gone);
                        a.Exec($"DELETE FROM {table} WHERE {gone}");
                    }
                }
                a.Exec("DROP TABLE IF EXISTS _seen");
                rep.Log.Add($"• aggiornato dai vecchi archivi: {a.Touched.Count} righe lette; {archived} non più presenti archiviate, {removed} tolte.");
                a.SetMeta("refreshed_at", ArchiveDb.Now());
            }
            finally { a.Touched = null; }
        });

        // the rows that came from the old databases match them again; everything else was left alone
        string fromOld = "(origin LIKE 'reloading_log.%' OR origin LIKE 'segui_carico.%')";
        rep.Checks.Add(new("righe originali dei vecchi archivi conservate", exp.Legacy,
            a.Count("legacy_rows", "source IN ('reloading_log.db','segui_carico.db','model_lab.db')")));
        rep.Checks.Add(new("armi dai vecchi archivi", exp.Firearms, a.Count("firearms", $"{fromOld} AND notes NOT LIKE '%(eliminato nel vecchio archivio)%'")));
        rep.Checks.Add(new("componenti dai vecchi archivi", exp.Components, a.Count("components", $"{fromOld} AND notes NOT LIKE '%(eliminato nel vecchio archivio)%'")));
        rep.Checks.Add(new("movimenti di magazzino dai vecchi archivi", exp.Ledger, a.Count("ledger", fromOld)));
        rep.Checks.Add(new("sedute dai vecchi archivi", exp.Sessions, a.Count("sessions", fromOld)));
        rep.Checks.Add(new("serie dai vecchi archivi", exp.Strings, a.Count("strings", fromOld)));
        rep.Checks.Add(new("colpi dai vecchi archivi", exp.Shots,
            Convert.ToDouble(a.Scalar($"SELECT COUNT(*) FROM shots WHERE string_id IN (SELECT id FROM strings WHERE {fromOld})"))));
        rep.Integrity = a.Scalar("PRAGMA integrity_check")?.ToString() ?? "";
        rep.ForeignKeysOk = a.Scalar("PRAGMA foreign_key_check") == null;
        return rep;
    }

    // ---- old databases ------------------------------------------------------------------------------------------

    private static SqliteConnection? OpenReadOnly(string? path, ArchiveBuildReport rep, string what)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { rep.Log.Add($"• {what}: non trovato, saltato."); return null; }
        var cn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        cn.Open();
        rep.Log.Add($"• {what}: letto ({path}).");
        return cn;
    }

    private static List<Dictionary<string, object?>> Rows(SqliteConnection cn, string sql)
    {
        var list = new List<Dictionary<string, object?>>();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            list.Add(d);
        }
        return list;
    }

    private static bool HasTable(SqliteConnection cn, string table)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$t";
        cmd.Parameters.AddWithValue("$t", table);
        return cmd.ExecuteScalar() != null;
    }

    private static bool HasColumn(SqliteConnection cn, string table, string column)
        => Rows(cn, $"PRAGMA table_info([{table}])").Any(r => string.Equals(S(r, "name"), column, StringComparison.OrdinalIgnoreCase));

    private static int CopyLegacy(ArchiveDb a, SqliteConnection cn, string source)
    {
        int n = 0;
        foreach (var t in Rows(cn, "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"))
        {
            string table = (string)t["name"]!;
            foreach (var row in Rows(cn, $"SELECT * FROM [{table}]"))
            {
                var json = row.ToDictionary(kv => kv.Key, kv => kv.Value is byte[] b ? "base64:" + Convert.ToBase64String(b) : kv.Value);
                long? id = row.TryGetValue("id", out var v) && v is long l ? l : null;
                a.Insert("legacy_rows", ("source", source), ("table_name", table), ("row_id", id), ("data", JsonSerializer.Serialize(json)));
                n++;
            }
        }
        return n;
    }

    private static object? V(Dictionary<string, object?> r, string col) => r.TryGetValue(col, out var v) ? v : null;
    private static double? D(Dictionary<string, object?> r, string col) => V(r, col) switch { null => null, long l => l, double d => d, string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) => x, _ => null };
    private static long? L(Dictionary<string, object?> r, string col) => V(r, col) switch { long l => l, double d => (long)d, _ => null };
    private static string S(Dictionary<string, object?> r, string col) => V(r, col)?.ToString() ?? "";
    private static long? Map(Dictionary<long, long> m, long? id) => id is { } i && m.TryGetValue(i, out long n) ? n : null;

    private static void FromLog(ArchiveDb a, SqliteConnection cn, Dictionary<long, long> shooters, Dictionary<long, long> firearms,
        Dictionary<long, long> comps, Expected exp, ArchiveBuildReport rep)
    {
        if (HasTable(cn, "shooters"))
            foreach (var r in Rows(cn, "SELECT * FROM shooters ORDER BY id"))
                shooters[(long)r["id"]!] = a.Upsert("shooters", ("name", S(r, "name")), ("notes", S(r, "notes")), ("archived", L(r, "archived") ?? 0),
                    ("origin", $"reloading_log.shooters#{r["id"]}"));
        if (!shooters.ContainsKey(1)) shooters[1] = a.Upsert("shooters", ("name", "Io"), ("origin", "creato: tiratore principale"));
        long Sh(Dictionary<string, object?> r) => Map(shooters, L(r, "shooter_id") ?? 1) ?? shooters[1];

        if (HasTable(cn, "firearms"))
            foreach (var r in Rows(cn, "SELECT * FROM firearms ORDER BY id"))
            {
                firearms[(long)r["id"]!] = a.Upsert("firearms", ("shooter_id", Sh(r)), ("name", S(r, "name")), ("caliber", S(r, "caliber")),
                    ("rounds_before", L(r, "rounds_before") ?? 0), ("rounds_limit", L(r, "rounds_limit")), ("retired", L(r, "retired") ?? 0),
                    ("notes", S(r, "notes")), ("created_at", S(r, "created_at")), ("origin", $"reloading_log.firearms#{r["id"]}"));
                exp.Firearms++;
            }

        if (HasTable(cn, "components"))
            foreach (var r in Rows(cn, "SELECT * FROM components ORDER BY id"))
            {
                comps[(long)r["id"]!] = a.Upsert("components", ("shooter_id", Sh(r)), ("kind", S(r, "kind")), ("brand", S(r, "brand")),
                    ("name", S(r, "name")), ("lot", S(r, "lot")), ("unit", S(r, "unit")), ("qty_initial", D(r, "qty_initial") ?? 0),
                    ("qty_current", D(r, "qty_current") ?? 0), ("cost_total", D(r, "cost_total") ?? 0), ("currency", S(r, "currency")),
                    ("expected_uses", L(r, "expected_uses") ?? 1), ("bullet_weight_gr", D(r, "bullet_weight_gr")), ("twist_mm", D(r, "twist_mm")),
                    ("barrel_length_mm", D(r, "barrel_length_mm")), ("anneal_every_uses", L(r, "anneal_every_uses")),
                    ("annealed_at_uses", D(r, "annealed_at_uses")), ("notes", S(r, "notes")), ("acquired_at", S(r, "acquired_at")),
                    ("archived", L(r, "archived") ?? 0), ("origin", $"reloading_log.components#{r["id"]}"));
                exp.Components++; exp.QtyCurrent += D(r, "qty_current") ?? 0;
            }

        if (HasTable(cn, "ledger"))
            foreach (var r in Rows(cn, "SELECT * FROM ledger ORDER BY id"))
            {
                if (Map(comps, L(r, "component_id")) is not { } cid) { rep.Log.Add($"  movimento di magazzino #{r["id"]} senza componente: tenuto solo come riga originale."); continue; }
                a.Upsert("ledger", ("component_id", cid), ("delta", D(r, "delta") ?? 0), ("reason", S(r, "reason")), ("at", S(r, "at")),
                    ("origin", $"reloading_log.ledger#{r["id"]}"));
                exp.Ledger++; exp.LedgerSum += D(r, "delta") ?? 0;
            }

        string CompText(long? id)
        {
            if (id == null) return "";
            var c = Rows(cn, $"SELECT brand, name FROM components WHERE id={id.Value}").FirstOrDefault();
            return c == null ? "" : (S(c, "brand") + " " + S(c, "name")).Trim();
        }

        var loads = new Dictionary<string, long>();
        if (HasTable(cn, "followed_loads"))
            foreach (var r in Rows(cn, "SELECT * FROM followed_loads ORDER BY id"))
            {
                a.Upsert("loads", ("shooter_id", shooters[1]), ("name", S(r, "name")), ("caliber", S(r, "caliber")),
                    ("firearm_id", Map(firearms, L(r, "firearm_id"))), ("bullet", CompText(L(r, "bullet_id"))), ("powder", CompText(L(r, "powder_id"))),
                    ("bullet_id", Map(comps, L(r, "bullet_id"))), ("powder_id", Map(comps, L(r, "powder_id"))), ("charge_gr", D(r, "charge_gr")),
                    ("notes", S(r, "notes")), ("archived", L(r, "archived") ?? 0), ("created_at", S(r, "created_at")),
                    ("origin", $"reloading_log.followed_loads#{r["id"]}"));
                exp.Loads++;
            }

        if (HasTable(cn, "journal"))
            foreach (var r in Rows(cn, "SELECT * FROM journal ORDER BY id"))
            {
                long sh = Sh(r);
                string key = string.Join("|", sh, S(r, "caliber"), S(r, "load_name"), L(r, "powder_id"), L(r, "bullet_id"), L(r, "primer_id"),
                    L(r, "brass_id"), Math.Round(D(r, "charge_gr") ?? 0, 3), Math.Round(D(r, "coal_mm") ?? 0, 3), Math.Round(D(r, "cbto_mm") ?? 0, 3));
                if (!loads.TryGetValue(key, out long loadId))
                {
                    string name = S(r, "load_name");
                    loadId = loads[key] = a.Upsert("loads", ("shooter_id", sh),
                        ("name", name.Length > 0 ? name : ($"{S(r, "caliber")} {CompText(L(r, "powder_id"))} " + (D(r, "charge_gr") ?? 0).ToString("0.##", CultureInfo.InvariantCulture) + " gr").Trim()),
                        ("caliber", S(r, "caliber")), ("firearm_text", S(r, "firearm")), ("firearm_id", Map(firearms, L(r, "firearm_id"))),
                        ("bullet", CompText(L(r, "bullet_id"))), ("powder", CompText(L(r, "powder_id"))), ("primer", CompText(L(r, "primer_id"))),
                        ("brass", CompText(L(r, "brass_id"))), ("bullet_id", Map(comps, L(r, "bullet_id"))), ("powder_id", Map(comps, L(r, "powder_id"))),
                        ("primer_id", Map(comps, L(r, "primer_id"))), ("brass_id", Map(comps, L(r, "brass_id"))),
                        ("charge_gr", D(r, "charge_gr")), ("coal_mm", D(r, "coal_mm")), ("cbto_mm", D(r, "cbto_mm")),
                        ("grtload_path", S(r, "grtload_path")), ("created_at", S(r, "created_at")), ("origin", $"reloading_log.journal#{r["id"]} (ricetta)"));
                    exp.Loads++;
                }
                var notes = new StringBuilder(S(r, "notes"));
                if (D(r, "velocity_avg_ms") is { } avg)
                    notes.Append((notes.Length > 0 ? "\n" : "") + string.Format(CultureInfo.InvariantCulture,
                        "Registrato nel diario: media {0:0.#} m/s, SD {1:0.##}, ES {2:0.#}", avg, D(r, "sd_ms"), D(r, "es_ms")));
                long sid = a.Upsert("sessions", ("shooter_id", sh), ("load_id", loadId), ("firearm_id", Map(firearms, L(r, "firearm_id"))),
                    ("firearm_text", S(r, "firearm")), ("date", S(r, "date")), ("distance_m", D(r, "distance_m")), ("temp_c", D(r, "temperature_c")),
                    ("pressure_hpa", D(r, "pressure_hpa")), ("humidity_pct", D(r, "humidity_pct")), ("wind_mps", D(r, "wind_mps")),
                    ("wind_deg", D(r, "wind_deg")), ("rounds", L(r, "rounds")), ("group_moa", D(r, "group_moa")),
                    ("ba", D(r, "ba")), ("a0", D(r, "a0")), ("k", D(r, "k")), ("notes", notes.ToString()), ("created_at", S(r, "created_at")),
                    ("origin", $"reloading_log.journal#{r["id"]}"));
                exp.Sessions++;
                var v = JournalEntry.ParseShots(S(r, "shots_ms"));
                if (v.Count > 0) { AddString(a, sid, "diario", D(r, "charge_gr"), "", v.Select(x => (x, (string?)null)).ToList(), null, $"reloading_log.journal#{r["id"]}"); exp.Strings++; exp.Shots += v.Count; exp.VelSum += v.Sum(); }
            }

        if (HasTable(cn, "case_head_measurements"))
            foreach (var r in Rows(cn, "SELECT * FROM case_head_measurements ORDER BY id"))
            {
                a.Upsert("measurements", ("kind", "diametro testa bossolo"), ("date", S(r, "date")), ("firearm_id", Map(firearms, L(r, "firearm_id"))),
                    ("component_id", Map(comps, L(r, "powder_id"))), ("charge_gr", D(r, "charge_gr")), ("value", D(r, "head_diameter_mm")),
                    ("unit", "mm"), ("notes", S(r, "notes")), ("origin", $"reloading_log.case_head_measurements#{r["id"]}"));
                exp.Measurements++;
            }
    }

    private static void FromTracker(ArchiveDb a, SqliteConnection cn, Dictionary<long, long> shooters, Expected exp, ArchiveBuildReport rep)
    {
        if (!HasTable(cn, "loads")) return;
        var map = new Dictionary<long, long>();
        foreach (var r in Rows(cn, "SELECT * FROM loads ORDER BY id"))
        {
            map[(long)r["id"]!] = a.Upsert("loads", ("shooter_id", Map(shooters, L(r, "shooter_id") ?? 1) ?? shooters[1]), ("name", S(r, "name")),
                ("caliber", S(r, "caliber")), ("bullet", S(r, "bullet")), ("powder", S(r, "powder")), ("charge_gr", D(r, "charge_gr")),
                ("notes", S(r, "notes")), ("archived", L(r, "archived") ?? 0), ("created_at", S(r, "created_at")), ("origin", $"segui_carico.loads#{r["id"]}"));
            exp.Loads++;
        }
        if (!HasTable(cn, "outings")) return;
        // an older segui_carico.db has no shooter column yet (the toolkit adds it when it opens the file): everything is the main shooter's
        string ls = HasColumn(cn, "loads", "shooter_id") ? "l.shooter_id" : "1";
        foreach (var r in Rows(cn, $"SELECT o.*, {ls} AS ls FROM outings o JOIN loads l ON l.id=o.load_id ORDER BY o.id"))
        {
            long sid = a.Upsert("sessions", ("shooter_id", Map(shooters, L(r, "ls") ?? 1) ?? shooters[1]), ("load_id", map[(long)r["load_id"]!]),
                ("firearm_text", S(r, "firearm")), ("date", S(r, "date")), ("distance_m", D(r, "distance_m")), ("temp_c", D(r, "temp_c")),
                ("pressure_hpa", D(r, "pressure_hpa")), ("humidity_pct", D(r, "humidity_pct")), ("wind_mps", D(r, "wind_mps")), ("wind_deg", D(r, "wind_deg")),
                ("powder_lot", S(r, "powder_lot")), ("primer_lot", S(r, "primer_lot")), ("seating_mm", D(r, "seating_mm")),
                ("brass_firings", L(r, "brass_firings")), ("barrel_rounds", L(r, "barrel_rounds")), ("group_moa", D(r, "group_moa")),
                ("notes", S(r, "notes")), ("created_at", S(r, "created_at")), ("origin", $"segui_carico.outings#{r["id"]}"));
            exp.Sessions++;
            var v = JournalEntry.ParseShots(S(r, "shots_ms"));
            if (v.Count > 0) { AddString(a, sid, "uscita", null, "", v.Select(x => (x, (string?)null)).ToList(), null, $"segui_carico.outings#{r["id"]}"); exp.Strings++; exp.Shots += v.Count; exp.VelSum += v.Sum(); }
        }
    }

    private static long AddString(ArchiveDb a, long sessionId, string label, double? chargeGr, string measurement,
        IReadOnlyList<(double V, string? Note)> shots, string? key, string origin, string notes = "")
    {
        // a string read again from the same old row replaces the earlier copy (its shots go with it)
        if (origin.StartsWith("reloading_log.", StringComparison.Ordinal) || origin.StartsWith("segui_carico.", StringComparison.Ordinal))
            a.Exec("DELETE FROM strings WHERE origin=$o", ("$o", origin));
        long id = a.Insert("strings", ("session_id", sessionId), ("label", label), ("charge_gr", chargeGr), ("measurement", measurement),
            ("notes", notes), ("content_key", key), ("origin", origin));
        for (int i = 0; i < shots.Count; i++)
            a.Insert("shots", ("string_id", id), ("idx", i + 1), ("velocity_mps", shots[i].V), ("note", shots[i].Note));
        return id;
    }

    // ---- history recovered from GRT ---------------------------------------------------------------------------------

    private sealed class RecoveryFile
    {
        public string Root { get; set; } = "";
        public DateTime When { get; set; }
        public List<RecoveredLoad> Loads { get; set; } = new();
        public List<RecoveredString> Strings { get; set; } = new();
        public List<RecoveredGroup> Groups { get; set; } = new();
        public List<RecoveredNote> Notes { get; set; } = new();
        public List<RecoveredPicture> Pictures { get; set; } = new();
    }

    private static void FromRecovery(ArchiveDb a, string dir, long mainShooter, HashSet<string> keys, Expected exp, ArchiveBuildReport rep)
    {
        string json = Path.Combine(dir, "recupero.json");
        if (!File.Exists(json)) { rep.Log.Add($"• storia recuperata in {dir}: recupero.json non trovato, saltato."); return; }
        var r = JsonSerializer.Deserialize<RecoveryFile>(File.ReadAllText(json))!;
        string importedAt = ArchiveDb.Now();

        // shooters by folder name; the GRT default folder and the root belong to the main shooter
        long ShooterOf(string folder)
        {
            if (MainShooterFolders.Split('|').Contains(folder, StringComparer.OrdinalIgnoreCase)) return mainShooter;
            var existing = a.Scalar($"SELECT id FROM shooters WHERE name='{folder.Replace("'", "''")}'");
            return existing is long id ? id : a.Insert("shooters", ("name", folder), ("origin", $"cartella di GRT \"{folder}\""));
        }

        // pictures first, by content: a duplicate picture points at the file of its first copy
        var picByKey = new Dictionary<string, long>();
        foreach (var p in r.Pictures.Where(p => p.DuplicateOf == null && p.FileName.Length > 0))
        {
            string f = Path.Combine(dir, "foto", p.FileName);
            if (!File.Exists(f)) { rep.Log.Add($"  foto mancante: {p.FileName}"); continue; }
            picByKey[p.Key] = AddFile(a, File.ReadAllBytes(f), p.FileName, "foto bersaglio", Path.Combine(r.Root, p.RelPath), File.GetLastWriteTime(f), importedAt, out bool isNew);
            if (keys.Add("P|" + p.Key)) exp.Pictures++;
        }

        int newLoads = 0, newStrings = 0, newGroups = 0, newNotes = 0;
        bool firstFolder = keys.Count(k => k.StartsWith("F|")) == 0;
        foreach (var l in r.Loads.OrderBy(l => l.Modified))
        {
            var strings = r.Strings.Where(s => s.RelPath == l.RelPath && s.DuplicateOf == null && !keys.Contains("S|" + s.Key)).ToList();
            var groups = r.Groups.Where(g => g.RelPath == l.RelPath && g.DuplicateOf == null && !keys.Contains("G|" + g.Key)).ToList();
            var notes = r.Notes.Where(n => n.RelPath == l.RelPath && n.DuplicateOf == null && !keys.Contains("N|" + n.Key)).ToList();
            bool hasNew = strings.Count + groups.Count + notes.Count > 0;
            // every original recipe from the first folder; from a later (older) copy only a file that brings something new
            if (!(hasNew || (firstFolder && l.Family == "originale" && l.Error == null))) continue;
            keys.Add("F|" + dir + "|" + l.RelPath);

            long? fileId = null;
            string full = Path.Combine(r.Root, l.RelPath);
            if (File.Exists(full))
                fileId = AddFile(a, File.ReadAllBytes(full), Path.GetFileName(l.RelPath), "carico GRT", full, File.GetLastWriteTime(full), importedAt, out _);
            long sh = ShooterOf(l.Shooter);
            string tag = firstFolder ? "" : $" (dalla copia {r.Root})";
            // GRT's own demonstration load (its "HOW TO USE" note begins "Dear GRT user"): kept, but archived, and left out of the library
            bool demo = r.Notes.Any(n => n.RelPath == l.RelPath && n.Text.Contains("Dear GRT user", StringComparison.OrdinalIgnoreCase));
            long loadId = a.Insert("loads", ("shooter_id", sh), ("name", Path.GetFileNameWithoutExtension(l.RelPath)), ("caliber", l.Caliber),
                ("firearm_text", l.Gun), ("bullet", l.Bullet), ("bullet_weight_gr", l.BulletGr), ("powder", l.Powder), ("charge_gr", l.ChargeGr),
                ("coal_mm", l.CoalMm), ("ba", l.Ba), ("grtload_file_id", fileId), ("grtload_path", full),
                ("notes", demo ? "Modello dimostrativo di GRT, non un carico del tiratore." : l.Family == "originale" ? "" : $"Da un file \"{l.Family}\"."),
                ("archived", demo ? 1 : 0), ("created_at", l.Modified.ToString("yyyy-MM-dd HH:mm:ss")),
                ("origin", $"grt:{l.RelPath}{tag}"));
            exp.Loads++; newLoads++;

            foreach (var n in notes)
            {
                a.Insert("notes", ("load_id", loadId), ("title", n.Title), ("text", n.Text), ("content_key", n.Key), ("origin", $"grt:{n.RelPath}{tag}"));
                keys.Add("N|" + n.Key); exp.Notes++; newNotes++;
            }
            if (strings.Count + groups.Count == 0) continue;

            // GRT keeps no date on a measurement: the file's own date stands in, marked as estimated
            long sid = a.Insert("sessions", ("shooter_id", sh), ("load_id", loadId), ("firearm_text", l.Gun),
                ("date", l.Modified.ToString("yyyy-MM-dd")), ("date_estimated", 1),
                ("distance_m", groups.Select(g => g.DistanceM).FirstOrDefault(d => d != null)),
                ("notes", "Dati recuperati da GRT: la data è quella del file."), ("created_at", importedAt), ("origin", $"grt:{l.RelPath}{tag}"));
            exp.Sessions++;
            foreach (var s in strings)
            {
                AddString(a, sid, s.ChargeName, s.ChargeGr, s.Measurement, s.VelocitiesMps.Select((v, i) => (v, s.ShotNotes.ElementAtOrDefault(i))).ToList(),
                    s.Key, $"grt:{s.RelPath}{tag}", s.ChargeNote);
                keys.Add("S|" + s.Key); exp.Strings++; exp.Shots += s.VelocitiesMps.Count; exp.VelSum += s.VelocitiesMps.Sum(); newStrings++;
            }
            foreach (var g in groups)
            {
                // by the tab's position, not its title: two tabs of one file may share a title
                var pic = r.Pictures.FirstOrDefault(p => p.RelPath == g.RelPath && p.TabIndex == g.TabIndex);
                long? picId = pic != null && picByKey.TryGetValue(pic.Key, out long pid) ? pid
                    : pic != null && a.Scalar($"SELECT id FROM files WHERE kind='foto bersaglio' AND sha256 LIKE '{pic.Key}%'") is long pid2 ? pid2 : null;
                long gid = a.Insert("shot_groups", ("session_id", sid), ("label", g.Set), ("tab", g.Tab), ("distance_m", g.DistanceM), ("step", g.Step),
                    ("origin_point", g.Origin), ("picture_file_id", picId), ("warning", g.Warning), ("content_key", g.Key), ("origin", $"grt:{g.RelPath}{tag}"));
                for (int i = 0; i < g.Hits.Count; i++)
                {
                    var h = g.Hits[i];
                    a.Insert("hits", ("group_id", gid), ("idx", i + 1), ("x_mm", h.XMm), ("y_mm", h.YMm), ("frac_x", h.FracX), ("frac_y", h.FracY), ("flyer", h.Flyer ? 1 : 0));
                }
                keys.Add("G|" + g.Key); exp.Groups++; exp.Hits += g.Hits.Count; newGroups++;
            }
        }
        a.Insert("imports", ("at", importedAt), ("source", "storia recuperata da GRT"), ("path", dir),
            ("summary", $"{newLoads} carichi, {newStrings} serie, {newGroups} rosate, {newNotes} note"));
        rep.Log.Add($"• storia recuperata da {r.Root}: {newLoads} carichi, {newStrings} serie, {newGroups} rosate, {newNotes} note nuove.");
    }

    private static long AddFile(ArchiveDb a, byte[] data, string name, string kind, string originalPath, DateTime modified, string importedAt, out bool isNew)
        => a.AddFile(data, name, kind, originalPath, modified, out isNew);

    /// <summary>Keeps Empirical Precision's file, licence and source note inside the archive, then copies its library.
    /// The expected counts are read from the JSON on their own, not from what the copy did.</summary>
    private static (ComponentLibrary.EpLibraryStats, int, int, int, int, int, double) FromEp(ArchiveDb a, string json, ArchiveBuildReport rep)
    {
        string dir = Path.GetDirectoryName(json)!, at = ArchiveDb.Now();
        string fonte = Path.Combine(dir, "FONTE.txt");
        string version = File.Exists(fonte)
            ? File.ReadAllLines(fonte).FirstOrDefault(l => l.StartsWith("Versione"))?.Split(':', 2)[1].Trim() ?? "versione sconosciuta"
            : "versione sconosciuta";
        foreach (string f in new[] { json, Path.Combine(dir, "LICENSE"), fonte })
            if (File.Exists(f)) AddFile(a, File.ReadAllBytes(f), Path.GetFileName(f), "libreria EP", f, File.GetLastWriteTime(f), at, out _);
        if (!File.Exists(Path.Combine(dir, "LICENSE"))) rep.Log.Add("  ATTENZIONE: manca il file LICENSE accanto alla libreria EP (la licenza Apache 2.0 chiede di conservarlo).");

        using var d = JsonDocument.Parse(File.ReadAllText(json));
        var r = d.RootElement;
        int Len(string t) => r.TryGetProperty(t, out var x) ? x.GetArrayLength() : 0;
        int cartNames = r.GetProperty("cartridges").EnumerateArray()
            .Select(c => ComponentLibrary.Norm(c.GetProperty("name").GetString() ?? "")).Distinct().Count();
        double g7Sum = r.GetProperty("bullets").EnumerateArray()
            .Select(b => b.GetProperty("ballistics").TryGetProperty("g7BC", out var g) && g.ValueKind == JsonValueKind.Number ? g.GetDouble() : 0).Sum();

        var stats = ComponentLibrary.FromEmpiricalPrecision(a, json, version, rep.Log);
        a.SetMeta("lib_ep_version", version);
        a.Insert("imports", ("at", at), ("source", "libreria Empirical Precision (Apache 2.0)"), ("path", json), ("summary", version));
        return (stats, Len("bullets"), cartNames, Len("powders"), Len("brass"), Len("primers"), g7Sum);
    }

    // ---- check ------------------------------------------------------------------------------------------------------

    private sealed class Expected
    {
        public int Legacy, Firearms, Components, Ledger, Loads, Sessions, Strings, Shots, Groups, Hits, Notes, Pictures, Measurements;
        public double QtyCurrent, LedgerSum, VelSum;
        public ComponentLibrary.GrtLibraryStats? GrtLib;
        public int GrtLoadFiles;
        public (ComponentLibrary.EpLibraryStats Stats, int Bullets, int Cartridges, int Powders, int Cases, int Primers, double G7Sum)? Ep;
    }

    private static void Verify(ArchiveDb a, Expected e, ArchiveBuildReport rep)
    {
        double Num(string sql) => a.Scalar(sql) switch { long l => l, double d => d, _ => 0 };
        rep.Checks.Add(new("righe originali dei vecchi archivi conservate", e.Legacy, a.Count("legacy_rows")));
        rep.Checks.Add(new("armi", e.Firearms, a.Count("firearms")));
        rep.Checks.Add(new("componenti di magazzino", e.Components, a.Count("components")));
        rep.Checks.Add(new("quantità in magazzino (somma)", e.QtyCurrent, Num("SELECT TOTAL(qty_current) FROM components")));
        rep.Checks.Add(new("movimenti di magazzino", e.Ledger, a.Count("ledger")));
        rep.Checks.Add(new("movimenti di magazzino (somma)", e.LedgerSum, Num("SELECT TOTAL(delta) FROM ledger")));
        rep.Checks.Add(new("carichi (ricette)", e.Loads, a.Count("loads")));
        rep.Checks.Add(new("sedute", e.Sessions, a.Count("sessions")));
        rep.Checks.Add(new("serie del cronografo", e.Strings, a.Count("strings")));
        rep.Checks.Add(new("colpi cronometrati", e.Shots, a.Count("shots")));
        rep.Checks.Add(new("somma di tutte le velocità (m/s)", Math.Round(e.VelSum, 3), Math.Round(Num("SELECT TOTAL(velocity_mps) FROM shots"), 3)));
        rep.Checks.Add(new("rosate", e.Groups, a.Count("shot_groups")));
        rep.Checks.Add(new("colpi sul bersaglio", e.Hits, a.Count("hits")));
        rep.Checks.Add(new("note", e.Notes, a.Count("notes")));
        rep.Checks.Add(new("foto dei bersagli", e.Pictures, a.Count("files", "kind='foto bersaglio'")));
        rep.Checks.Add(new("misure", e.Measurements, a.Count("measurements")));
        if (e.GrtLib is { } g)
        {
            rep.Checks.Add(new("carichi GRT letti per la libreria", e.GrtLoadFiles, g.Loads));
            // a load that names no component at all (GRT's empty "noname") has nothing to link
            rep.Checks.Add(new("carichi con componenti collegati alla loro palla/cartuccia/polvere/arma",
                a.Count("loads", "grtload_file_id IS NOT NULL AND archived=0 AND (caliber<>'' OR bullet<>'' OR powder<>'' OR firearm_text<>'')"),
                Num("SELECT COUNT(DISTINCT load_id) FROM lib_links")));
        }
        if (e.Ep is { } ep)
        {
            rep.Checks.Add(new("libreria EP: palle", ep.Bullets, a.Count("lib_items", "identity LIKE 'palla|ep:%'")));
            rep.Checks.Add(new("libreria EP: cartucce (nomi diversi)", ep.Cartridges,
                Num("SELECT COUNT(DISTINCT item_id) FROM lib_values v JOIN lib_items i ON i.id=v.item_id WHERE i.kind='cartuccia' AND v.source='libreria EP'")));
            rep.Checks.Add(new("libreria EP: polveri", ep.Powders, a.Count("lib_items", "identity LIKE 'polvere|ep:%'")));
            rep.Checks.Add(new("libreria EP: bossoli", ep.Cases, a.Count("lib_items", "identity LIKE 'bossolo|ep:%'")));
            rep.Checks.Add(new("libreria EP: inneschi", ep.Primers, a.Count("lib_items", "identity LIKE 'innesco|ep:%'")));
            rep.Checks.Add(new("libreria EP: BC G7 delle palle (somma)",
                Math.Round(ep.G7Sum, 6),
                Math.Round(Num("SELECT TOTAL(value) FROM lib_values WHERE field='bc_g7' AND source='libreria EP'"), 6)));
        }
        rep.Integrity = a.Scalar("PRAGMA integrity_check")?.ToString() ?? "";
        rep.ForeignKeysOk = a.Scalar("PRAGMA foreign_key_check") == null;
        long files = a.Count("files"), size = (long)Num("SELECT TOTAL(size) FROM files");
        string mb = (size / 1e6).ToString("0.0", CultureInfo.GetCultureInfo("it-IT"));
        rep.Log.Add($"• file originali conservati nell'archivio: {files} ({mb} MB), di cui {a.Count("files", "kind='carico GRT'")} carichi GRT.");
        rep.Log.Add($"• tiratori: {string.Join(", ", RowsOf(a, "SELECT name FROM shooters ORDER BY id"))}.");
    }

    private static IEnumerable<string> RowsOf(ArchiveDb a, string sql)
    {
        using var cmd = a.Connection.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetValue(0)?.ToString() ?? "");
        return l;
    }
}
