using System.Globalization;
using Microsoft.Data.Sqlite;

namespace GrtReloadingLog.Data;

/// <summary>SQLite store for component inventory + a load/range journal, with a stock ledger.</summary>
public sealed class Db : IDisposable
{
    private readonly SqliteConnection _cn;

    public string Path { get; }

    public Db(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        _cn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        _cn.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        Migrate();
    }

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GRTPlugins", "reloading_log.db");

    private void Migrate()
    {
        Exec(@"
CREATE TABLE IF NOT EXISTS components (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  kind TEXT NOT NULL, brand TEXT DEFAULT '', name TEXT NOT NULL, lot TEXT DEFAULT '',
  unit TEXT NOT NULL, qty_initial REAL NOT NULL, qty_current REAL NOT NULL,
  cost_total REAL DEFAULT 0, currency TEXT DEFAULT 'EUR', expected_uses INTEGER DEFAULT 1,
  bullet_weight_gr REAL, notes TEXT DEFAULT '', acquired_at TEXT DEFAULT (datetime('now')),
  archived INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS journal (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  date TEXT NOT NULL, load_name TEXT DEFAULT '', caliber TEXT DEFAULT '', firearm TEXT DEFAULT '',
  powder_id INTEGER, primer_id INTEGER, brass_id INTEGER, bullet_id INTEGER,
  charge_gr REAL DEFAULT 0, coal_mm REAL, cbto_mm REAL, rounds INTEGER DEFAULT 0,
  velocity_avg_ms REAL, sd_ms REAL, es_ms REAL, group_moa REAL, distance_m REAL,
  notes TEXT DEFAULT '', grtload_path TEXT DEFAULT '', created_at TEXT DEFAULT (datetime('now')),
  stock_applied INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS ledger (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  component_id INTEGER NOT NULL, delta REAL NOT NULL, reason TEXT DEFAULT '',
  at TEXT DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS ix_ledger_comp ON ledger(component_id);");
    }

    // ---- components ------------------------------------------------------

    public List<Component> Components(bool includeArchived = false)
    {
        var list = new List<Component>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM components" + (includeArchived ? "" : " WHERE archived=0") + " ORDER BY kind, brand, name";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadComponent(r));
        return list;
    }

    public Component? Component(long id)
    {
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM components WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadComponent(r) : null;
    }

    public long UpsertComponent(Component c)
    {
        using var cmd = _cn.CreateCommand();
        if (c.Id == 0)
            cmd.CommandText = @"INSERT INTO components
              (kind,brand,name,lot,unit,qty_initial,qty_current,cost_total,currency,expected_uses,bullet_weight_gr,notes,archived)
              VALUES ($kind,$brand,$name,$lot,$unit,$qi,$qc,$cost,$cur,$exp,$bw,$notes,$arch);
              SELECT last_insert_rowid();";
        else
        {
            cmd.CommandText = @"UPDATE components SET kind=$kind,brand=$brand,name=$name,lot=$lot,unit=$unit,
              qty_initial=$qi,qty_current=$qc,cost_total=$cost,currency=$cur,expected_uses=$exp,
              bullet_weight_gr=$bw,notes=$notes,archived=$arch WHERE id=$id; SELECT $id;";
            cmd.Parameters.AddWithValue("$id", c.Id);
        }
        cmd.Parameters.AddWithValue("$kind", c.Kind.ToString());
        cmd.Parameters.AddWithValue("$brand", c.Brand);
        cmd.Parameters.AddWithValue("$name", c.Name);
        cmd.Parameters.AddWithValue("$lot", c.Lot);
        cmd.Parameters.AddWithValue("$unit", c.Unit);
        cmd.Parameters.AddWithValue("$qi", c.QtyInitial);
        cmd.Parameters.AddWithValue("$qc", c.QtyCurrent);
        cmd.Parameters.AddWithValue("$cost", c.CostTotal);
        cmd.Parameters.AddWithValue("$cur", c.Currency);
        cmd.Parameters.AddWithValue("$exp", Math.Max(1, c.ExpectedUses));
        cmd.Parameters.AddWithValue("$bw", (object?)c.BulletWeightGr ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", c.Notes);
        cmd.Parameters.AddWithValue("$arch", c.Archived ? 1 : 0);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void AdjustStock(long componentId, double delta, string reason)
    {
        using var tx = _cn.BeginTransaction();
        Exec("UPDATE components SET qty_current = MAX(0, qty_current + $d) WHERE id=$id", tx,
            ("$d", delta), ("$id", componentId));
        Exec("INSERT INTO ledger(component_id,delta,reason) VALUES ($id,$d,$r)", tx,
            ("$id", componentId), ("$d", delta), ("$r", reason));
        tx.Commit();
    }

    // ---- journal --------------------------------------------------------

    public List<JournalEntry> Journal()
    {
        var list = new List<JournalEntry>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM journal ORDER BY date DESC, id DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadEntry(r));
        return list;
    }

    /// <summary>Insert/update an entry and reconcile component stock (undo old deduction, apply new).</summary>
    public long SaveEntry(JournalEntry e, bool applyStock)
    {
        using var tx = _cn.BeginTransaction();

        JournalEntry? old = e.Id != 0 ? ReadEntryById(e.Id, tx) : null;
        if (old is { StockApplied: true })
            ApplyStock(old, +1, tx, $"reverse journal:{old.Id}");

        long id;
        using (var cmd = _cn.CreateCommand())
        {
            cmd.Transaction = tx;
            if (e.Id == 0)
                cmd.CommandText = @"INSERT INTO journal
                  (date,load_name,caliber,firearm,powder_id,primer_id,brass_id,bullet_id,charge_gr,coal_mm,cbto_mm,rounds,
                   velocity_avg_ms,sd_ms,es_ms,group_moa,distance_m,notes,grtload_path,stock_applied)
                  VALUES ($date,$ln,$cal,$fa,$pw,$pr,$br,$bu,$chg,$coal,$cbto,$rnd,$v,$sd,$es,$grp,$dist,$notes,$path,$sa);
                  SELECT last_insert_rowid();";
            else
            {
                cmd.CommandText = @"UPDATE journal SET date=$date,load_name=$ln,caliber=$cal,firearm=$fa,powder_id=$pw,
                  primer_id=$pr,brass_id=$br,bullet_id=$bu,charge_gr=$chg,coal_mm=$coal,cbto_mm=$cbto,rounds=$rnd,
                  velocity_avg_ms=$v,sd_ms=$sd,es_ms=$es,group_moa=$grp,distance_m=$dist,notes=$notes,grtload_path=$path,
                  stock_applied=$sa WHERE id=$id; SELECT $id;";
                cmd.Parameters.AddWithValue("$id", e.Id);
            }
            void Q(string n, object? v) => cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            Q("$date", e.Date); Q("$ln", e.LoadName); Q("$cal", e.Caliber); Q("$fa", e.Firearm);
            Q("$pw", e.PowderId); Q("$pr", e.PrimerId); Q("$br", e.BrassId); Q("$bu", e.BulletId);
            Q("$chg", e.ChargeGr); Q("$coal", e.CoalMm); Q("$cbto", e.CbtoMm); Q("$rnd", e.Rounds);
            Q("$v", e.VelocityAvgMs); Q("$sd", e.SdMs); Q("$es", e.EsMs); Q("$grp", e.GroupMoa); Q("$dist", e.DistanceM);
            Q("$notes", e.Notes); Q("$path", e.GrtloadPath);
            Q("$sa", applyStock ? 1 : 0);
            id = Convert.ToInt64(cmd.ExecuteScalar());
        }

        e.Id = id;
        if (applyStock) ApplyStock(e, -1, tx, $"journal:{id}");
        tx.Commit();
        return id;
    }

    public void DeleteEntry(long id)
    {
        using var tx = _cn.BeginTransaction();
        JournalEntry? old = ReadEntryById(id, tx);
        if (old is { StockApplied: true }) ApplyStock(old, +1, tx, $"delete journal:{id}");
        Exec("DELETE FROM journal WHERE id=$id", tx, ("$id", id));
        tx.Commit();
    }

    /// <summary>sign −1 deducts, +1 restores. Applies rounds×(1 per primer/brass/bullet) and charge×rounds grams of powder.</summary>
    private void ApplyStock(JournalEntry e, int sign, SqliteTransaction tx, string reason)
    {
        void Move(long? id, double amount)
        {
            if (id is not { } cid || amount <= 0) return;
            Exec("UPDATE components SET qty_current = MAX(0, qty_current + $d) WHERE id=$id", tx, ("$d", sign * amount), ("$id", cid));
            Exec("INSERT INTO ledger(component_id,delta,reason) VALUES ($id,$d,$r)", tx, ("$id", cid), ("$d", sign * amount), ("$r", reason));
        }
        double powderG = e.ChargeGr * 0.06479891 * e.Rounds; // grains -> grams
        Move(e.PowderId, powderG);
        Move(e.PrimerId, e.Rounds);
        Move(e.BulletId, e.Rounds);
        Move(e.BrassId, e.Rounds);   // 1 firing per round; amortisation is in cost, not stock
    }

    // ---- helpers -------------------------------------------------------

    private static Component ReadComponent(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        Kind = Enum.TryParse<ComponentKind>(Str(r, "kind"), out var k) ? k : ComponentKind.Bullet,
        Brand = Str(r, "brand"), Name = Str(r, "name"), Lot = Str(r, "lot"), Unit = Str(r, "unit"),
        QtyInitial = Dbl(r, "qty_initial"), QtyCurrent = Dbl(r, "qty_current"),
        CostTotal = Dbl(r, "cost_total"), Currency = Str(r, "currency"),
        ExpectedUses = (int)Lng(r, "expected_uses"),
        BulletWeightGr = Nul(r, "bullet_weight_gr"),
        Notes = Str(r, "notes"), AcquiredAt = Str(r, "acquired_at"),
        Archived = Lng(r, "archived") != 0,
    };

    private JournalEntry? ReadEntryById(long id, SqliteTransaction tx)
    {
        using var cmd = _cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT * FROM journal WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadEntry(r) : null;
    }

    private static JournalEntry ReadEntry(SqliteDataReader r) => new()
    {
        Id = Lng(r, "id"), Date = Str(r, "date"), LoadName = Str(r, "load_name"),
        Caliber = Str(r, "caliber"), Firearm = Str(r, "firearm"),
        PowderId = NulL(r, "powder_id"), PrimerId = NulL(r, "primer_id"),
        BrassId = NulL(r, "brass_id"), BulletId = NulL(r, "bullet_id"),
        ChargeGr = Dbl(r, "charge_gr"), CoalMm = Nul(r, "coal_mm"), CbtoMm = Nul(r, "cbto_mm"),
        Rounds = (int)Lng(r, "rounds"),
        VelocityAvgMs = Nul(r, "velocity_avg_ms"), SdMs = Nul(r, "sd_ms"), EsMs = Nul(r, "es_ms"),
        GroupMoa = Nul(r, "group_moa"), DistanceM = Nul(r, "distance_m"),
        Notes = Str(r, "notes"), GrtloadPath = Str(r, "grtload_path"), CreatedAt = Str(r, "created_at"),
        StockApplied = Lng(r, "stock_applied") != 0,
    };

    private static string Str(SqliteDataReader r, string c) { int i = r.GetOrdinal(c); return r.IsDBNull(i) ? "" : r.GetString(i); }
    private static double Dbl(SqliteDataReader r, string c) { int i = r.GetOrdinal(c); return r.IsDBNull(i) ? 0 : r.GetDouble(i); }
    private static long Lng(SqliteDataReader r, string c) { int i = r.GetOrdinal(c); return r.IsDBNull(i) ? 0 : r.GetInt64(i); }
    private static double? Nul(SqliteDataReader r, string c) { int i = r.GetOrdinal(c); return r.IsDBNull(i) ? null : r.GetDouble(i); }
    private static long? NulL(SqliteDataReader r, string c) { int i = r.GetOrdinal(c); return r.IsDBNull(i) ? null : r.GetInt64(i); }

    private void Exec(string sql, SqliteTransaction? tx = null, params (string, object)[] ps)
    {
        using var cmd = _cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _cn.Dispose();
}
