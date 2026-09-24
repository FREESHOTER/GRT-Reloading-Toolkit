using System.Globalization;
using Microsoft.Data.Sqlite;

namespace GrtReloadingToolkit.Log;

/// <summary>SQLite store for component inventory + a load/range journal, with a stock ledger.</summary>
public sealed class Db : IDisposable
{
    private readonly SqliteConnection _cn;

    public string Path { get; }

    public Db(string path)
    {
        Path = path;
        // a bare file name ("reloading.db", e.g. from RELOADING_LOG_DB) has no directory part,
        // and CreateDirectory("") throws — that path is the current directory, already there.
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
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
  bullet_weight_gr REAL, twist_mm REAL, barrel_length_mm REAL,
  notes TEXT DEFAULT '', acquired_at TEXT DEFAULT (datetime('now')),
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
CREATE INDEX IF NOT EXISTS ix_ledger_comp ON ledger(component_id);
CREATE TABLE IF NOT EXISTS firearms (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL, caliber TEXT DEFAULT '', notes TEXT DEFAULT '',
  rounds_before INTEGER DEFAULT 0, retired INTEGER DEFAULT 0,
  created_at TEXT DEFAULT (datetime('now'))
);
CREATE TABLE IF NOT EXISTS shot_measurements (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  caliber TEXT NOT NULL, label TEXT NOT NULL, shot_index INTEGER NOT NULL,
  temperature_c REAL,
  created_at TEXT DEFAULT (datetime('now')),
  UNIQUE(caliber, label, shot_index)
);
CREATE INDEX IF NOT EXISTS ix_shot_measurements_key ON shot_measurements(caliber, label);
CREATE TABLE IF NOT EXISTS case_head_measurements (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  date TEXT NOT NULL, firearm_id INTEGER NOT NULL, powder_id INTEGER,
  charge_gr REAL NOT NULL, head_diameter_mm REAL NOT NULL, notes TEXT DEFAULT '',
  created_at TEXT DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS ix_case_head_key ON case_head_measurements(firearm_id, powder_id);");
        // additive column migration (older DBs)
        if (!ColumnExists("journal", "firearm_id"))
            Exec("ALTER TABLE journal ADD COLUMN firearm_id INTEGER");
        // Barrels arrived after the components table did, and they carry two numbers nothing else has.
        if (!ColumnExists("components", "twist_mm"))
            Exec("ALTER TABLE components ADD COLUMN twist_mm REAL");
        if (!ColumnExists("components", "barrel_length_mm"))
            Exec("ALTER TABLE components ADD COLUMN barrel_length_mm REAL");
        // Environmental conditions (GRT itself tracks none of these -- checked its own .grtload
        // field list, no temperature/pressure/humidity tag anywhere -- so they're plain manual
        // entry) + the propellant coefficients a calibration session actually produced, so a later
        // search can find "what Ba worked for this powder+bullet near this temperature" instead of
        // digging through old .grtload files by hand.
        foreach (var col in new[] { "temperature_c", "pressure_hpa", "humidity_pct", "ba", "a0" })
            if (!ColumnExists("journal", col))
                Exec($"ALTER TABLE journal ADD COLUMN {col} REAL");
        // Brass Life (see Log/BrassLife.cs): anneal reminder, expressed as an average-firings-per-
        // case interval, and the value the lot was AT the last time it was marked annealed -- both
        // null on an existing lot until the user sets a reminder or clicks "mark annealed" once.
        if (!ColumnExists("components", "anneal_every_uses"))
            Exec("ALTER TABLE components ADD COLUMN anneal_every_uses INTEGER");
        if (!ColumnExists("components", "annealed_at_uses"))
            Exec("ALTER TABLE components ADD COLUMN annealed_at_uses REAL");
    }

    private bool ColumnExists(string table, string col)
    {
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read()) if (string.Equals(r.GetString(1), col, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
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
              (kind,brand,name,lot,unit,qty_initial,qty_current,cost_total,currency,expected_uses,anneal_every_uses,annealed_at_uses,bullet_weight_gr,twist_mm,barrel_length_mm,notes,archived)
              VALUES ($kind,$brand,$name,$lot,$unit,$qi,$qc,$cost,$cur,$exp,$ae,$aa,$bw,$twist,$blen,$notes,$arch);
              SELECT last_insert_rowid();";
        else
        {
            cmd.CommandText = @"UPDATE components SET kind=$kind,brand=$brand,name=$name,lot=$lot,unit=$unit,
              qty_initial=$qi,qty_current=$qc,cost_total=$cost,currency=$cur,expected_uses=$exp,
              anneal_every_uses=$ae,annealed_at_uses=$aa,
              bullet_weight_gr=$bw,twist_mm=$twist,barrel_length_mm=$blen,
              notes=$notes,archived=$arch WHERE id=$id; SELECT $id;";
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
        cmd.Parameters.AddWithValue("$ae", (object?)c.AnnealEveryUses ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$aa", (object?)c.AnnealedAtUses ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bw", (object?)c.BulletWeightGr ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$twist", (object?)c.TwistMm ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$blen", (object?)c.BarrelLengthMm ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", c.Notes);
        cmd.Parameters.AddWithValue("$arch", c.Archived ? 1 : 0);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    /// <summary>Moves stock and records the move. May drive qty_current negative — see <see cref="ApplyStock"/>.</summary>
    public void AdjustStock(long componentId, double delta, string reason)
    {
        using var tx = _cn.BeginTransaction();
        Exec("UPDATE components SET qty_current = qty_current + $d WHERE id=$id", tx,
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
                  (date,load_name,caliber,firearm,firearm_id,powder_id,primer_id,brass_id,bullet_id,charge_gr,coal_mm,cbto_mm,rounds,
                   velocity_avg_ms,sd_ms,es_ms,group_moa,distance_m,notes,grtload_path,stock_applied,
                   temperature_c,pressure_hpa,humidity_pct,ba,a0)
                  VALUES ($date,$ln,$cal,$fa,$fid,$pw,$pr,$br,$bu,$chg,$coal,$cbto,$rnd,$v,$sd,$es,$grp,$dist,$notes,$path,$sa,
                          $temp,$pres,$hum,$ba,$a0);
                  SELECT last_insert_rowid();";
            else
            {
                cmd.CommandText = @"UPDATE journal SET date=$date,load_name=$ln,caliber=$cal,firearm=$fa,firearm_id=$fid,powder_id=$pw,
                  primer_id=$pr,brass_id=$br,bullet_id=$bu,charge_gr=$chg,coal_mm=$coal,cbto_mm=$cbto,rounds=$rnd,
                  velocity_avg_ms=$v,sd_ms=$sd,es_ms=$es,group_moa=$grp,distance_m=$dist,notes=$notes,grtload_path=$path,
                  stock_applied=$sa,temperature_c=$temp,pressure_hpa=$pres,humidity_pct=$hum,ba=$ba,a0=$a0 WHERE id=$id; SELECT $id;";
                cmd.Parameters.AddWithValue("$id", e.Id);
            }
            void Q(string n, object? v) => cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            Q("$date", e.Date); Q("$ln", e.LoadName); Q("$cal", e.Caliber); Q("$fa", e.Firearm); Q("$fid", e.FirearmId);
            Q("$pw", e.PowderId); Q("$pr", e.PrimerId); Q("$br", e.BrassId); Q("$bu", e.BulletId);
            Q("$chg", e.ChargeGr); Q("$coal", e.CoalMm); Q("$cbto", e.CbtoMm); Q("$rnd", e.Rounds);
            Q("$v", e.VelocityAvgMs); Q("$sd", e.SdMs); Q("$es", e.EsMs); Q("$grp", e.GroupMoa); Q("$dist", e.DistanceM);
            Q("$notes", e.Notes); Q("$path", e.GrtloadPath);
            Q("$sa", applyStock ? 1 : 0);
            Q("$temp", e.TemperatureC); Q("$pres", e.PressureHpa); Q("$hum", e.HumidityPct); Q("$ba", e.Ba); Q("$a0", e.A0);
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

    /// <summary>
    /// Journal entries that carry a calibrated Ba (i.e. someone ran Barrel Calibration and saved
    /// the result here), for THIS caliber + powder + bullet combination -- Ba/a0 from a different
    /// component combo aren't comparable. Caller sorts by whichever "best" means for them: tightest
    /// group, closest velocity, closest temperature.
    /// </summary>
    public List<JournalEntry> FindCalibrations(string caliber, long? powderId, long? bulletId)
    {
        var list = new List<JournalEntry>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = @"SELECT * FROM journal
            WHERE ba IS NOT NULL AND caliber = $cal COLLATE NOCASE
              AND (powder_id IS $pw) AND (bullet_id IS $bu)
            ORDER BY date DESC, id DESC";
        cmd.Parameters.AddWithValue("$cal", caliber);
        cmd.Parameters.AddWithValue("$pw", (object?)powderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bu", (object?)bulletId ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadEntry(r));
        return list;
    }

    /// <summary>Distinct calibers that have at least one calibrated (Ba-bearing) journal entry, for
    /// the "Find best Ba" search's own dropdown.</summary>
    public List<string> CalibratedCalibers()
    {
        var list = new List<string>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT caliber FROM journal WHERE ba IS NOT NULL AND caliber <> '' ORDER BY caliber";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>Distinct calibers that have at least one journal entry carrying charge, temperature
    /// AND a measured velocity together -- the three <see cref="VelocityModel"/> needs a point at
    /// all, regardless of whether that entry has ever been Ba-calibrated (unlike "Find best Ba", this
    /// model doesn't touch Ba).</summary>
    public List<string> VelocityModelCalibers()
    {
        var list = new List<string>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = @"SELECT DISTINCT caliber FROM journal
            WHERE charge_gr > 0 AND temperature_c IS NOT NULL AND velocity_avg_ms IS NOT NULL AND caliber <> ''
            ORDER BY caliber";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>The (charge, temperature, velocity) journal rows <see cref="VelocityModel"/> fits
    /// against, for one caliber+powder+bullet combo -- same combo-scoping reason as
    /// <see cref="FindCalibrations"/>: a fit mixing two different bullets or powders would be fitting
    /// noise, not a real charge/temperature relationship.</summary>
    public List<JournalEntry> EntriesForVelocityModel(string caliber, long? powderId, long? bulletId)
    {
        var list = new List<JournalEntry>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = @"SELECT * FROM journal
            WHERE charge_gr > 0 AND temperature_c IS NOT NULL AND velocity_avg_ms IS NOT NULL
              AND caliber = $cal COLLATE NOCASE AND (powder_id IS $pw) AND (bullet_id IS $bu)
            ORDER BY date, id";
        cmd.Parameters.AddWithValue("$cal", caliber);
        cmd.Parameters.AddWithValue("$pw", (object?)powderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bu", (object?)bulletId ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadEntry(r));
        return list;
    }

    /// <summary>
    /// Journal rows carrying no Ba but naming a .grtload -- the shape of a journal written before
    /// the ba column existed. These are the backfill's candidates; opening the loads is
    /// <see cref="BaBackfill"/>'s job, not a database's.
    /// </summary>
    public List<JournalEntry> EntriesMissingBa()
    {
        var list = new List<JournalEntry>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM journal WHERE ba IS NULL AND COALESCE(grtload_path,'') <> '' ORDER BY date, id";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadEntry(r));
        return list;
    }

    /// <summary>
    /// Puts a Ba (and an a0 beside it) on one entry, and only on an entry that has none. The
    /// <c>ba IS NULL</c> guard is what makes the backfill re-runnable and unable to overwrite a
    /// number the user typed; a0 is COALESCEd for the same reason. True when the row took it.
    /// </summary>
    public bool FillBa(long id, double ba, double? a0)
    {
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "UPDATE journal SET ba=$ba, a0=COALESCE(a0,$a0) WHERE id=$id AND ba IS NULL";
        cmd.Parameters.AddWithValue("$ba", ba);
        cmd.Parameters.AddWithValue("$a0", (object?)a0 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 1;
    }

    /// <summary>
    /// sign −1 deducts, +1 restores. Applies rounds×(1 per primer/brass/bullet) and charge×rounds grams of powder.
    /// Deductions are deliberately not clamped at zero: a clamp loses the overshoot while the matching
    /// reversal (edit or delete the entry) still restores the full amount, so logging 200 rounds against
    /// 150 primers and then deleting the entry would leave 200 in stock. qty_current must stay equal to
    /// its running ledger total; a negative balance is the honest signal that the lot was mis-counted.
    /// </summary>
    private void ApplyStock(JournalEntry e, int sign, SqliteTransaction tx, string reason)
    {
        void Move(long? id, double amount)
        {
            if (id is not { } cid || amount <= 0) return;
            Exec("UPDATE components SET qty_current = qty_current + $d WHERE id=$id", tx, ("$d", sign * amount), ("$id", cid));
            Exec("INSERT INTO ledger(component_id,delta,reason) VALUES ($id,$d,$r)", tx, ("$id", cid), ("$d", sign * amount), ("$r", reason));
        }
        double powderG = e.ChargeGr * 0.06479891 * e.Rounds; // grains -> grams
        Move(e.PowderId, powderG);
        Move(e.PrimerId, e.Rounds);
        Move(e.BulletId, e.Rounds);
        Move(e.BrassId, e.Rounds);   // 1 firing per round; amortisation is in cost, not stock
    }

    // ---- firearms -----------------------------------------------------

    public List<Firearm> Firearms(bool includeRetired = false)
    {
        var list = new List<Firearm>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM firearms" + (includeRetired ? "" : " WHERE retired=0") + " ORDER BY name";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Firearm
        {
            Id = r.GetInt64(r.GetOrdinal("id")), Name = Str(r, "name"), Caliber = Str(r, "caliber"),
            Notes = Str(r, "notes"), RoundsBefore = (int)Lng(r, "rounds_before"), Retired = Lng(r, "retired") != 0,
        });
        return list;
    }

    public long UpsertFirearm(Firearm f)
    {
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = f.Id == 0
            ? "INSERT INTO firearms(name,caliber,notes,rounds_before,retired) VALUES ($n,$c,$no,$rb,$rt); SELECT last_insert_rowid();"
            : "UPDATE firearms SET name=$n,caliber=$c,notes=$no,rounds_before=$rb,retired=$rt WHERE id=$id; SELECT $id;";
        if (f.Id != 0) cmd.Parameters.AddWithValue("$id", f.Id);
        cmd.Parameters.AddWithValue("$n", f.Name);
        cmd.Parameters.AddWithValue("$c", f.Caliber);
        cmd.Parameters.AddWithValue("$no", f.Notes);
        cmd.Parameters.AddWithValue("$rb", f.RoundsBefore);
        cmd.Parameters.AddWithValue("$rt", f.Retired ? 1 : 0);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>Find an existing firearm by name (case-insensitive), or create one.</summary>
    public long ResolveFirearm(string name, string caliber)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT id FROM firearms WHERE name=$n COLLATE NOCASE LIMIT 1";
        cmd.Parameters.AddWithValue("$n", name.Trim());
        if (cmd.ExecuteScalar() is { } o && o != DBNull.Value) return Convert.ToInt64(o);
        return UpsertFirearm(new Firearm { Name = name.Trim(), Caliber = caliber });
    }

    /// <summary>Best-effort match of a powder name coming from GRT (its propellant "pname") against
    /// an existing inventory Component, so "Log from GRT" can pre-select "Find best Ba"'s powder
    /// filter target -- unlike ResolveFirearm this never auto-creates: an inventory entry stands for
    /// real physical stock the user tracks, and GRT knows nothing about lot/brand/quantity, so a
    /// wrong guess is worse than leaving it as "— none —" for the user to pick by hand.</summary>
    public long? ResolvePowderId(string powderName) => ResolveComponentId(powderName, ComponentKind.Powder);

    /// <summary>Same best-effort matching as <see cref="ResolvePowderId"/>, for a bullet name coming
    /// from GRT's projectile "pname" -- used by Load Leaderboard to find which logged load the
    /// currently open GRT load corresponds to.</summary>
    public long? ResolveBulletId(string bulletName) => ResolveComponentId(bulletName, ComponentKind.Bullet);

    private long? ResolveComponentId(string name, ComponentKind kind)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string needle = name.Trim();
        var candidates = Components(includeArchived: true).Where(c => c.Kind == kind).ToList();
        var exact = candidates.FirstOrDefault(c => string.Equals(c.Name, needle, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact.Id;
        var partial = candidates.FirstOrDefault(c =>
            c.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            needle.Contains(c.Name, StringComparison.OrdinalIgnoreCase));
        return partial?.Id;
    }

    /// <summary>Total rounds through a firearm = rounds_before + Σ journal.rounds.</summary>
    public int FirearmRoundCount(long id)
    {
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = @"SELECT COALESCE((SELECT rounds_before FROM firearms WHERE id=$id),0)
                            + COALESCE((SELECT SUM(rounds) FROM journal WHERE firearm_id=$id),0)";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Journal entries for one firearm with a velocity, ordered by date; each gets the running round total.</summary>
    public List<(string date, string load, double chargeGr, int cumRounds, double mv, double? sd)> FirearmMvHistory(long id)
    {
        var raw = new List<(string date, string load, double chg, int rnd, double mv, double? sd)>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT date,load_name,charge_gr,rounds,velocity_avg_ms,sd_ms FROM journal WHERE firearm_id=$id ORDER BY date, id";
        cmd.Parameters.AddWithValue("$id", id);
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                raw.Add((r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? 0 : r.GetDouble(2),
                         r.IsDBNull(3) ? 0 : r.GetInt32(3), r.IsDBNull(4) ? double.NaN : r.GetDouble(4),
                         r.IsDBNull(5) ? null : r.GetDouble(5)));

        int before = 0;
        using (var c2 = _cn.CreateCommand())
        {
            c2.CommandText = "SELECT rounds_before FROM firearms WHERE id=$id";
            c2.Parameters.AddWithValue("$id", id);
            before = Convert.ToInt32(c2.ExecuteScalar() ?? 0);
        }

        var outp = new List<(string, string, double, int, double, double?)>();
        int cum = before;
        foreach (var x in raw)
        {
            cum += x.rnd;
            if (!double.IsNaN(x.mv)) outp.Add((x.date, x.load, x.chg, cum, x.mv, x.sd));
        }
        return outp;
    }

    // ---- brass life -----------------------------------------------------

    /// <summary>Total rounds ever logged against one brass lot -- unlike <see cref="FirearmRoundCount"/>
    /// there is no "rounds before" concept here (a lot is bought new or its history is simply
    /// unknown, so nothing to add on top of what the Journal itself has recorded).</summary>
    public int BrassRoundsFired(long id)
    {
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(rounds),0) FROM journal WHERE brass_id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Marks a brass lot as annealed right now: records the CURRENT average-firings-per-case
    /// value as the new baseline, so <see cref="BrassLife.Compute"/> only counts rounds fired after
    /// this point toward the next reminder. Returns false if the lot isn't found or isn't Brass.</summary>
    public bool MarkBrassAnnealed(long id)
    {
        var c = Component(id);
        if (c is null || c.Kind != ComponentKind.Brass) return false;
        double avgUses = c.QtyInitial > 0 ? BrassRoundsFired(id) / c.QtyInitial : 0;
        Exec("UPDATE components SET annealed_at_uses=$v WHERE id=$id", null, ("$v", avgUses), ("$id", id));
        return true;
    }

    // ---- shot measurements (SD Root-Cause manual per-shot temperature) --

    /// <summary>Every manually-entered per-shot temperature for one string, ordered by shot index —
    /// <see cref="ShotMeasurement.TemperatureC"/> may itself be null for a shot the user hasn't
    /// filled in yet (a saved row with the key but no value), so callers must still check it.</summary>
    public List<ShotMeasurement> ShotTemperatures(string caliber, string label)
    {
        var list = new List<ShotMeasurement>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM shot_measurements WHERE caliber=$cal COLLATE NOCASE AND label=$lbl ORDER BY shot_index";
        cmd.Parameters.AddWithValue("$cal", caliber);
        cmd.Parameters.AddWithValue("$lbl", label);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ShotMeasurement
            {
                Id = Lng(r, "id"), Caliber = Str(r, "caliber"), Label = Str(r, "label"),
                ShotIndex = (int)Lng(r, "shot_index"), TemperatureC = Nul(r, "temperature_c"),
            });
        return list;
    }

    /// <summary>Upserts one shot's temperature by its (caliber, label, shot_index) key; a null
    /// <paramref name="temperatureC"/> clears a previously-entered value rather than deleting the
    /// row, so re-saving an emptied grid cell is idempotent.</summary>
    public void SaveShotTemperature(string caliber, string label, int shotIndex, double? temperatureC)
    {
        Exec(@"INSERT INTO shot_measurements(caliber,label,shot_index,temperature_c) VALUES ($cal,$lbl,$idx,$t)
               ON CONFLICT(caliber,label,shot_index) DO UPDATE SET temperature_c=$t",
            null, ("$cal", caliber), ("$lbl", label), ("$idx", shotIndex), ("$t", (object?)temperatureC ?? DBNull.Value));
    }

    // ---- case-head measurements (pressure signs) ------------------------

    /// <summary>Every reading logged for one firearm, optionally narrowed to one powder lot --
    /// ordered by charge so a caller never has to re-sort before handing this to
    /// <see cref="PressureSign.BuildSeries"/>.</summary>
    public List<CaseHeadMeasurement> CaseHeadMeasurements(long firearmId, long? powderId = null)
    {
        var list = new List<CaseHeadMeasurement>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM case_head_measurements WHERE firearm_id=$fid" +
            (powderId is null ? "" : " AND powder_id=$pid") + " ORDER BY charge_gr, id";
        cmd.Parameters.AddWithValue("$fid", firearmId);
        if (powderId is { } pid) cmd.Parameters.AddWithValue("$pid", pid);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CaseHeadMeasurement
            {
                Id = Lng(r, "id"), Date = Str(r, "date"), FirearmId = Lng(r, "firearm_id"),
                PowderId = (long?)NulL(r, "powder_id"), ChargeGr = Dbl(r, "charge_gr"),
                HeadDiameterMm = Dbl(r, "head_diameter_mm"), Notes = Str(r, "notes"),
            });
        return list;
    }

    /// <summary>Distinct powders that have at least one case-head reading logged for this firearm --
    /// what the tool's own powder filter offers, so it never lists a powder with nothing to show.</summary>
    public List<long> CaseHeadPowderIds(long firearmId)
    {
        var ids = new List<long>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT powder_id FROM case_head_measurements WHERE firearm_id=$fid AND powder_id IS NOT NULL ORDER BY powder_id";
        cmd.Parameters.AddWithValue("$fid", firearmId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt64(0));
        return ids;
    }

    public long UpsertCaseHeadMeasurement(CaseHeadMeasurement m)
    {
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = m.Id == 0
            ? "INSERT INTO case_head_measurements(date,firearm_id,powder_id,charge_gr,head_diameter_mm,notes) VALUES ($d,$fid,$pid,$chg,$dia,$n); SELECT last_insert_rowid();"
            : "UPDATE case_head_measurements SET date=$d,firearm_id=$fid,powder_id=$pid,charge_gr=$chg,head_diameter_mm=$dia,notes=$n WHERE id=$id; SELECT $id;";
        if (m.Id != 0) cmd.Parameters.AddWithValue("$id", m.Id);
        cmd.Parameters.AddWithValue("$d", m.Date);
        cmd.Parameters.AddWithValue("$fid", m.FirearmId);
        cmd.Parameters.AddWithValue("$pid", (object?)m.PowderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$chg", m.ChargeGr);
        cmd.Parameters.AddWithValue("$dia", m.HeadDiameterMm);
        cmd.Parameters.AddWithValue("$n", m.Notes);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void DeleteCaseHeadMeasurement(long id) =>
        Exec("DELETE FROM case_head_measurements WHERE id=$id", null, ("$id", id));

    // ---- helpers -------------------------------------------------------

    private static Component ReadComponent(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        Kind = Enum.TryParse<ComponentKind>(Str(r, "kind"), out var k) ? k : ComponentKind.Bullet,
        Brand = Str(r, "brand"), Name = Str(r, "name"), Lot = Str(r, "lot"), Unit = Str(r, "unit"),
        QtyInitial = Dbl(r, "qty_initial"), QtyCurrent = Dbl(r, "qty_current"),
        CostTotal = Dbl(r, "cost_total"), Currency = Str(r, "currency"),
        ExpectedUses = (int)Lng(r, "expected_uses"),
        AnnealEveryUses = (int?)NulL(r, "anneal_every_uses"),
        AnnealedAtUses = Nul(r, "annealed_at_uses"),
        BulletWeightGr = Nul(r, "bullet_weight_gr"),
        TwistMm = Nul(r, "twist_mm"), BarrelLengthMm = Nul(r, "barrel_length_mm"),
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
        Caliber = Str(r, "caliber"), Firearm = Str(r, "firearm"), FirearmId = NulL(r, "firearm_id"),
        PowderId = NulL(r, "powder_id"), PrimerId = NulL(r, "primer_id"),
        BrassId = NulL(r, "brass_id"), BulletId = NulL(r, "bullet_id"),
        ChargeGr = Dbl(r, "charge_gr"), CoalMm = Nul(r, "coal_mm"), CbtoMm = Nul(r, "cbto_mm"),
        Rounds = (int)Lng(r, "rounds"),
        VelocityAvgMs = Nul(r, "velocity_avg_ms"), SdMs = Nul(r, "sd_ms"), EsMs = Nul(r, "es_ms"),
        GroupMoa = Nul(r, "group_moa"), DistanceM = Nul(r, "distance_m"),
        Notes = Str(r, "notes"), GrtloadPath = Str(r, "grtload_path"), CreatedAt = Str(r, "created_at"),
        StockApplied = Lng(r, "stock_applied") != 0,
        TemperatureC = Nul(r, "temperature_c"), PressureHpa = Nul(r, "pressure_hpa"), HumidityPct = Nul(r, "humidity_pct"),
        Ba = Nul(r, "ba"), A0 = Nul(r, "a0"),
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
