using Microsoft.Data.Sqlite;

namespace GrtReloadingToolkit.Archive;

/// <summary>Where the shooter's own archive lives. Overridable for tests and live checks.</summary>
public static class ArchivePaths
{
    public static string Folder =>
        Environment.GetEnvironmentVariable("RICARICA_CARTELLA") is { Length: > 0 } env
            ? env
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ricarica");

    /// <summary>Documenti\Ricarica\archivio.db: the live archive, on the local disk (never inside a synced folder, where a
    /// sync while the file is open can leave it half-written).</summary>
    public static string Database =>
        Environment.GetEnvironmentVariable("RICARICA_ARCHIVIO") is { Length: > 0 } env ? env : Path.Combine(Folder, "archivio.db");

    /// <summary>Where the dated copies go (the shooter may point it at a synced folder or a USB stick).</summary>
    public static string Backups =>
        Environment.GetEnvironmentVariable("RICARICA_COPIE") is { Length: > 0 } env ? env : Path.Combine(Folder, "copie");
}

/// <summary>
/// The shooter's own archive: one SQLite file holding everything measured and kept, in metric units, readable without
/// GRT, Empirical Precision or this program. Every row carries an <c>origin</c> saying where it came from (a table and row
/// of the old databases, or a .grtload), and <c>legacy_rows</c> keeps every row of the old databases verbatim as JSON,
/// so nothing a mapping missed is ever lost.
/// </summary>
public sealed class ArchiveDb : IDisposable
{
    /// <summary>1: archive. 2: component library (lib_items, lib_values, lib_links, lib_effective).
    /// 3: strings.temp_c and the origin indexes the refresh from the old databases looks rows up by.
    /// 4: analyses.report_file_id (the PDF report of an analysis, kept in files).
    /// 5: pieces, cartridge lots, cartridges, their measures and the frozen predictions (CartridgeStore), and shots.cartridge_id.</summary>
    public const int SchemaVersion = 6;

    private readonly SqliteConnection _cn;
    public string Path { get; }
    public SqliteConnection Connection => _cn;

    public ArchiveDb(string path)
    {
        Path = path;
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _cn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _cn.Open();
        Exec("PRAGMA foreign_keys=ON;");
        Create();
    }

    public void Dispose() => _cn.Dispose();

    private void Create()
    {
        Exec(@"
CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE IF NOT EXISTS shooters (
  id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, notes TEXT DEFAULT '', archived INTEGER DEFAULT 0, origin TEXT);
CREATE TABLE IF NOT EXISTS firearms (
  id INTEGER PRIMARY KEY AUTOINCREMENT, shooter_id INTEGER NOT NULL REFERENCES shooters(id),
  name TEXT NOT NULL, caliber TEXT DEFAULT '', twist_mm REAL, barrel_length_mm REAL, sight_height_mm REAL,
  rounds_before INTEGER DEFAULT 0, rounds_limit INTEGER, retired INTEGER DEFAULT 0, notes TEXT DEFAULT '', created_at TEXT, origin TEXT);
CREATE TABLE IF NOT EXISTS components (
  id INTEGER PRIMARY KEY AUTOINCREMENT, shooter_id INTEGER NOT NULL REFERENCES shooters(id),
  kind TEXT NOT NULL, brand TEXT DEFAULT '', name TEXT NOT NULL, lot TEXT DEFAULT '', unit TEXT NOT NULL,
  qty_initial REAL NOT NULL, qty_current REAL NOT NULL, cost_total REAL DEFAULT 0, currency TEXT DEFAULT 'EUR',
  expected_uses INTEGER DEFAULT 1, bullet_weight_gr REAL, twist_mm REAL, barrel_length_mm REAL,
  anneal_every_uses INTEGER, annealed_at_uses REAL, notes TEXT DEFAULT '', acquired_at TEXT, archived INTEGER DEFAULT 0, origin TEXT);
CREATE TABLE IF NOT EXISTS ledger (
  id INTEGER PRIMARY KEY AUTOINCREMENT, component_id INTEGER NOT NULL REFERENCES components(id),
  delta REAL NOT NULL, reason TEXT DEFAULT '', at TEXT, origin TEXT);
CREATE TABLE IF NOT EXISTS files (
  id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, kind TEXT NOT NULL, sha256 TEXT NOT NULL UNIQUE, size INTEGER NOT NULL,
  data BLOB NOT NULL, original_path TEXT, modified_at TEXT, imported_at TEXT);
CREATE TABLE IF NOT EXISTS loads (
  id INTEGER PRIMARY KEY AUTOINCREMENT, shooter_id INTEGER NOT NULL REFERENCES shooters(id),
  name TEXT NOT NULL, caliber TEXT DEFAULT '', firearm_text TEXT DEFAULT '', firearm_id INTEGER REFERENCES firearms(id),
  bullet TEXT DEFAULT '', bullet_weight_gr REAL, powder TEXT DEFAULT '', charge_gr REAL, primer TEXT DEFAULT '', brass TEXT DEFAULT '',
  powder_id INTEGER REFERENCES components(id), bullet_id INTEGER REFERENCES components(id),
  primer_id INTEGER REFERENCES components(id), brass_id INTEGER REFERENCES components(id),
  coal_mm REAL, cbto_mm REAL, case_capacity_cm3 REAL, case_length_mm REAL, seating_depth_mm REAL, ba REAL, a0 REAL, k REAL, notes TEXT DEFAULT '',
  grtload_file_id INTEGER REFERENCES files(id), grtload_path TEXT, archived INTEGER DEFAULT 0, created_at TEXT, origin TEXT);
CREATE TABLE IF NOT EXISTS sessions (
  id INTEGER PRIMARY KEY AUTOINCREMENT, shooter_id INTEGER NOT NULL REFERENCES shooters(id),
  load_id INTEGER REFERENCES loads(id), firearm_id INTEGER REFERENCES firearms(id), firearm_text TEXT DEFAULT '',
  date TEXT NOT NULL, date_estimated INTEGER DEFAULT 0, distance_m REAL,
  temp_c REAL, cartridge_temp_c REAL, pressure_hpa REAL, humidity_pct REAL, wind_mps REAL, wind_deg REAL,
  powder_lot TEXT DEFAULT '', primer_lot TEXT DEFAULT '', seating_mm REAL, brass_firings INTEGER, barrel_rounds INTEGER,
  rounds INTEGER, group_moa REAL, ba REAL, a0 REAL, k REAL, notes TEXT DEFAULT '', created_at TEXT, origin TEXT);
CREATE TABLE IF NOT EXISTS strings (
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id INTEGER NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
  label TEXT DEFAULT '', charge_gr REAL, measurement TEXT DEFAULT '', device TEXT DEFAULT '', notes TEXT DEFAULT '',
  content_key TEXT, origin TEXT);
CREATE TABLE IF NOT EXISTS shots (
  id INTEGER PRIMARY KEY AUTOINCREMENT, string_id INTEGER NOT NULL REFERENCES strings(id) ON DELETE CASCADE,
  idx INTEGER NOT NULL, velocity_mps REAL NOT NULL, note TEXT, barrel_temp_c REAL,
  set_aside INTEGER DEFAULT 0, set_aside_reason TEXT);
CREATE TABLE IF NOT EXISTS shot_groups (
  id INTEGER PRIMARY KEY AUTOINCREMENT, session_id INTEGER NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
  label TEXT DEFAULT '', tab TEXT DEFAULT '', distance_m REAL, step REAL, origin_point TEXT DEFAULT '',
  picture_file_id INTEGER REFERENCES files(id), warning TEXT, content_key TEXT, origin TEXT);
CREATE TABLE IF NOT EXISTS hits (
  id INTEGER PRIMARY KEY AUTOINCREMENT, group_id INTEGER NOT NULL REFERENCES shot_groups(id) ON DELETE CASCADE,
  idx INTEGER NOT NULL, x_mm REAL, y_mm REAL, frac_x REAL, frac_y REAL, flyer INTEGER DEFAULT 0,
  shot_id INTEGER REFERENCES shots(id));
CREATE TABLE IF NOT EXISTS notes (
  id INTEGER PRIMARY KEY AUTOINCREMENT, load_id INTEGER REFERENCES loads(id), session_id INTEGER REFERENCES sessions(id),
  title TEXT DEFAULT '', text TEXT DEFAULT '', content_key TEXT, origin TEXT);
CREATE TABLE IF NOT EXISTS measurements (
  id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, date TEXT, firearm_id INTEGER REFERENCES firearms(id),
  load_id INTEGER REFERENCES loads(id), component_id INTEGER REFERENCES components(id), charge_gr REAL,
  value REAL, unit TEXT DEFAULT '', notes TEXT DEFAULT '', origin TEXT);
CREATE TABLE IF NOT EXISTS analyses (
  id INTEGER PRIMARY KEY AUTOINCREMENT, tool TEXT NOT NULL, tool_version TEXT, created_at TEXT,
  load_id INTEGER REFERENCES loads(id), session_id INTEGER REFERENCES sessions(id), inputs TEXT, result TEXT, origin TEXT);
CREATE TABLE IF NOT EXISTS legacy_rows (
  id INTEGER PRIMARY KEY AUTOINCREMENT, source TEXT NOT NULL, table_name TEXT NOT NULL, row_id INTEGER, data TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS imports (
  id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, source TEXT NOT NULL, path TEXT, summary TEXT);
CREATE INDEX IF NOT EXISTS ix_sessions_load ON sessions(load_id, date);
CREATE INDEX IF NOT EXISTS ix_strings_session ON strings(session_id);
CREATE INDEX IF NOT EXISTS ix_shots_string ON shots(string_id, idx);
CREATE INDEX IF NOT EXISTS ix_groups_session ON shot_groups(session_id);
CREATE INDEX IF NOT EXISTS ix_hits_group ON hits(group_id, idx);
CREATE INDEX IF NOT EXISTS ix_loads_shooter ON loads(shooter_id);");
        ComponentLibrary.CreateSchema(this);
        AddColumnIfMissing("strings", "temp_c", "REAL");
        AddColumnIfMissing("analyses", "report_file_id", "INTEGER REFERENCES files(id)");
        CartridgeStore.CreateSchema(this);
        foreach (string t in new[] { "shooters", "firearms", "components", "ledger", "loads", "sessions", "strings", "measurements" })
            Exec($"CREATE INDEX IF NOT EXISTS ix_{t}_origin ON {t}(origin)");
        if (GetMeta("schema_version") == null)
        {
            SetMeta("schema_version", SchemaVersion.ToString());
            SetMeta("created_at", Now());
            // While false the archive is a copy built from the old databases and may be rebuilt; once the tools write to it,
            // it becomes the master and is never rebuilt over.
            SetMeta("master", "false");
        }
        // Every change of schema so far only adds tables (CREATE … IF NOT EXISTS above), so an older archive is brought
        // up to date just by opening it; the version says what it now holds.
        else if (int.TryParse(GetMeta("schema_version"), out int v) && v < SchemaVersion)
            SetMeta("schema_version", SchemaVersion.ToString());
    }

    public static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    internal void AddColumnIfMissing(string table, string column, string type)
    {
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        bool has = false;
        using (var r = cmd.ExecuteReader())
            while (r.Read()) has |= string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase);
        if (!has) Exec($"ALTER TABLE {table} ADD COLUMN {column} {type}");
    }

    /// <summary>Changes made through this connection since it was opened (SQLite's total_changes()).</summary>
    public long TotalChanges() => (long)(Scalar("SELECT total_changes()") ?? 0L);

    /// <summary>The origins written by <see cref="Upsert"/> while set: a refresh uses it to tell which old rows were seen.</summary>
    public HashSet<string>? Touched { get; set; }

    /// <summary>
    /// The row of <paramref name="table"/> whose <c>origin</c> is the one in <paramref name="values"/>: updated in place when it
    /// is already there (its id, and so everything that points at it, stays the same), inserted otherwise.
    /// </summary>
    public long Upsert(string table, params (string Column, object? Value)[] values)
    {
        string origin = values.First(v => v.Column == "origin").Value as string
                        ?? throw new ArgumentException("Upsert needs an origin.");
        Touched?.Add(origin);
        if (ScalarP($"SELECT id FROM {table} WHERE origin=$o ORDER BY id LIMIT 1", ("$o", origin)) is not long id)
            return Insert(table, values);
        using var cmd = _cn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"UPDATE {table} SET {string.Join(",", values.Select((v, i) => v.Column + "=$p" + i))} WHERE id=$id";
        for (int i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("$p" + i, values[i].Value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return id;
    }

    /// <summary>Keeps a file inside the archive once (by its SHA-256) and returns its id; <paramref name="isNew"/> says whether
    /// it was not there before.</summary>
    public long AddFile(byte[] data, string name, string kind, string? originalPath, DateTime modified, out bool isNew)
    {
        string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();
        if (ScalarP("SELECT id FROM files WHERE sha256=$s", ("$s", sha)) is long id) { isNew = false; return id; }
        isNew = true;
        return Insert("files", ("name", name), ("kind", kind), ("sha256", sha), ("size", data.LongLength), ("data", data),
            ("original_path", originalPath), ("modified_at", modified.ToString("yyyy-MM-dd HH:mm:ss")), ("imported_at", Now()));
    }

    /// <summary>Marks the archive as the master: from now on the tools write to it and it is never rebuilt from the old databases.</summary>
    public void MakeMaster()
    {
        if (IsMaster) return;
        SetMeta("master", "true");
        SetMeta("master_since", Now());
    }

    public string? GetMeta(string key)
    {
        using var cmd = _cn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = "SELECT value FROM meta WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string value)
        => Exec("INSERT INTO meta(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
            ("$k", key), ("$v", value));

    public bool IsMaster => GetMeta("master") == "true";

    public void Exec(string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = _cn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Inserts one row into <paramref name="table"/> from (column, value) pairs and returns its id.</summary>
    public long Insert(string table, params (string Column, object? Value)[] values)
    {
        using var cmd = _cn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"INSERT INTO {table}({string.Join(",", values.Select(v => v.Column))}) " +
                          $"VALUES({string.Join(",", values.Select((_, i) => "$p" + i))}); SELECT last_insert_rowid();";
        for (int i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("$p" + i, values[i].Value ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    public long Count(string table, string where = "1=1")
    {
        using var cmd = _cn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {where}";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>First column of the first row, with parameters; null when there is no row or the value is NULL.</summary>
    public object? ScalarP(string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = _cn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var r = cmd.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    public object? Scalar(string sql)
    {
        using var cmd = _cn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is DBNull ? null : v;
    }

    private SqliteTransaction? _tx;

    /// <summary>The transaction <see cref="InTransaction"/> has open, for a caller that builds its own command.</summary>
    public SqliteTransaction? CurrentTransaction => _tx;

    /// <summary>Runs <paramref name="work"/> in one transaction: all of it lands, or none of it.</summary>
    public void InTransaction(Action work)
    {
        if (_tx != null) { work(); return; }
        _tx = _cn.BeginTransaction();
        try { work(); _tx.Commit(); }
        catch { _tx.Rollback(); throw; }
        finally { _tx.Dispose(); _tx = null; }
    }
}
