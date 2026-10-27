using System.Globalization;
using Microsoft.Data.Sqlite;

namespace GrtReloadingToolkit.Log;

/// <summary>The tracker's own SQLite file: the loads followed and their outings. Nothing here touches the Reloading Toolkit's journal.</summary>
public sealed class TrackerDb : IDisposable
{
    private readonly SqliteConnection _cn;

    public string Path { get; }

    public TrackerDb(string path)
    {
        Path = path;
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _cn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        _cn.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        Exec(@"
CREATE TABLE IF NOT EXISTS loads (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL, caliber TEXT DEFAULT '', bullet TEXT DEFAULT '', powder TEXT DEFAULT '',
  charge_gr REAL DEFAULT 0, notes TEXT DEFAULT '', archived INTEGER DEFAULT 0,
  created_at TEXT DEFAULT (datetime('now'))
);
CREATE TABLE IF NOT EXISTS outings (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  load_id INTEGER NOT NULL REFERENCES loads(id) ON DELETE CASCADE,
  date TEXT NOT NULL, temp_c REAL, pressure_hpa REAL, humidity_pct REAL,
  shots_ms TEXT DEFAULT '', group_moa REAL, distance_m REAL, wind_mps REAL, wind_deg REAL,
  firearm TEXT DEFAULT '', powder_lot TEXT DEFAULT '', primer_lot TEXT DEFAULT '', seating_mm REAL,
  brass_firings INTEGER, barrel_rounds INTEGER, notes TEXT DEFAULT '',
  created_at TEXT DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS ix_outings_load ON outings(load_id, date);");
        // Whose load it is: the id of a shooter of the Reloading Toolkit's own database (the two files sit side by side), 1 for everything
        // followed before there were several people.
        if (!ColumnExists("loads", "shooter_id"))
            Exec("ALTER TABLE loads ADD COLUMN shooter_id INTEGER NOT NULL DEFAULT 1");
    }

    private bool ColumnExists(string table, string col)
    {
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read()) if (string.Equals(r.GetString(1), col, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The person whose followed loads the list shows, or null for everybody. A new load is written to this person (to the first
    /// shooter while everybody is shown).</summary>
    public long? CurrentShooterId { get; set; }

    /// <summary>%APPDATA%\GRTPlugins\segui_carico.db, or the file in SEGUI_CARICO_DB (the tests and the live checks).</summary>
    public static string DefaultPath =>
        Environment.GetEnvironmentVariable("SEGUI_CARICO_DB") is { Length: > 0 } env
            ? env
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GRTPlugins", "segui_carico.db");

    // ---- loads ------------------------------------------------------------

    public List<TrackedLoad> Loads(bool includeArchived = false)
    {
        var list = new List<TrackedLoad>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM loads WHERE " + (includeArchived ? "1=1" : "archived=0") + (CurrentShooterId is null ? "" : " AND shooter_id=$sh")
                          + " ORDER BY name COLLATE NOCASE, id";
        if (CurrentShooterId is { } sh) cmd.Parameters.AddWithValue("$sh", sh);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new TrackedLoad
            {
                Id = Lng(r, "id"), Name = Str(r, "name"), Caliber = Str(r, "caliber"), Bullet = Str(r, "bullet"), Powder = Str(r, "powder"),
                ChargeGr = Dbl(r, "charge_gr"), Notes = Str(r, "notes"), Archived = Lng(r, "archived") != 0,
                ShooterId = Lng(r, "shooter_id"),
            });
        return list;
    }

    public long SaveLoad(TrackedLoad l)
    {
        using var cmd = _cn.CreateCommand();
        if (l.Id == 0)
            cmd.CommandText = "INSERT INTO loads (name,caliber,bullet,powder,charge_gr,notes,archived,shooter_id) VALUES ($n,$c,$b,$p,$g,$no,$ar,$sh); SELECT last_insert_rowid();";
        else
        {
            cmd.CommandText = "UPDATE loads SET name=$n,caliber=$c,bullet=$b,powder=$p,charge_gr=$g,notes=$no,archived=$ar,shooter_id=$sh WHERE id=$id; SELECT $id;";
            cmd.Parameters.AddWithValue("$id", l.Id);
        }
        cmd.Parameters.AddWithValue("$n", l.Name); cmd.Parameters.AddWithValue("$c", l.Caliber);
        cmd.Parameters.AddWithValue("$b", l.Bullet); cmd.Parameters.AddWithValue("$p", l.Powder);
        cmd.Parameters.AddWithValue("$g", l.ChargeGr); cmd.Parameters.AddWithValue("$no", l.Notes);
        cmd.Parameters.AddWithValue("$ar", l.Archived ? 1 : 0);
        cmd.Parameters.AddWithValue("$sh", l.ShooterId > 0 ? l.ShooterId : CurrentShooterId ?? 1);
        l.Id = Convert.ToInt64(cmd.ExecuteScalar());
        return l.Id;
    }

    /// <summary>Removes a load and, with it, all its outings.</summary>
    public void DeleteLoad(long id) => Exec("DELETE FROM loads WHERE id=$id", ("$id", id));

    // ---- recognising a load -----------------------------------------------

    /// <summary>Lower case, letters and digits only: "Lapua Scenar-L 139 gr" and "lapua scenar l 139gr" are one text.</summary>
    internal static string Key(string? s) => new((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Does what is open in GRT belong to this load? Same caliber, a charge within 0.05 gr, and the bullet and powder when the load
    /// names them: GRT's product names and the shooter's own wording rarely match letter for letter, so one containing the other is enough.</summary>
    public static bool Matches(TrackedLoad l, string caliber, string bullet, string powder, double chargeGr)
        => Key(l.Caliber) == Key(caliber)
           && Math.Abs(l.ChargeGr - chargeGr) <= 0.05 + 1e-9
           && Similar(l.Bullet, bullet) && Similar(l.Powder, powder);

    /// <summary>
    /// Do two names point at the same product? Empty on either side matches (nothing was said). Otherwise the words of the shorter must all be words of
    /// the longer: "N550" in "Vihtavuori N550", "Scenar-L 139" in "Lapua Scenar-L 139 gr". A weight written "139gr" and "139" is the same word.
    /// </summary>
    internal static bool Similar(string? wanted, string? actual)
    {
        var a = Words(wanted); var b = Words(actual);
        if (a.Count == 0 || b.Count == 0) return true;
        var (small, large) = a.Count <= b.Count ? (a, b) : (b, a);
        return small.All(large.Contains);
    }

    private static HashSet<string> Words(string? text)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in System.Text.RegularExpressions.Regex.Split((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}.]+"))
        {
            string w = raw.Trim('.');
            if (w.Length == 0 || w is "gr" or "grs" or "grain" or "grains") continue;
            var m = System.Text.RegularExpressions.Regex.Match(w, @"^(\d+(?:\.\d+)?)(gr|grs|grain|grains)$");
            words.Add(m.Success ? m.Groups[1].Value : w);
        }
        return words;
    }

    /// <summary>The followed load the open one belongs to; null when none does, or when several do (the caller asks).</summary>
    public TrackedLoad? Find(string caliber, string bullet, string powder, double chargeGr)
    {
        var hits = Loads().Where(l => Matches(l, caliber, bullet, powder, chargeGr)).ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    // ---- outings ------------------------------------------------------------

    public List<Outing> OutingsOf(long loadId)
    {
        var list = new List<Outing>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM outings WHERE load_id=$id ORDER BY date, id";
        cmd.Parameters.AddWithValue("$id", loadId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadOuting(r));
        return list;
    }

    public long SaveOuting(Outing o)
    {
        using var cmd = _cn.CreateCommand();
        const string cols = "date,temp_c,pressure_hpa,humidity_pct,shots_ms,group_moa,distance_m,wind_mps,wind_deg,firearm,powder_lot,primer_lot,seating_mm,brass_firings,barrel_rounds,notes";
        if (o.Id == 0)
            cmd.CommandText = $"INSERT INTO outings (load_id,{cols}) VALUES ($load,$date,$t,$p,$h,$shots,$g,$d,$wm,$wd,$fa,$pl,$pr,$se,$bf,$br,$no); SELECT last_insert_rowid();";
        else
        {
            cmd.CommandText = "UPDATE outings SET date=$date,temp_c=$t,pressure_hpa=$p,humidity_pct=$h,shots_ms=$shots,group_moa=$g,distance_m=$d,wind_mps=$wm,wind_deg=$wd," +
                              "firearm=$fa,powder_lot=$pl,primer_lot=$pr,seating_mm=$se,brass_firings=$bf,barrel_rounds=$br,notes=$no WHERE id=$id; SELECT $id;";
            cmd.Parameters.AddWithValue("$id", o.Id);
        }
        void Q(string n, object? v) => cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        Q("$load", o.LoadId); Q("$date", o.Date); Q("$t", o.TempC); Q("$p", o.PressureHpa); Q("$h", o.HumidityPct); Q("$shots", o.ShotsMs ?? "");
        Q("$g", o.GroupMoa); Q("$d", o.DistanceM); Q("$wm", o.WindMps); Q("$wd", o.WindDeg); Q("$fa", o.Firearm ?? ""); Q("$pl", o.PowderLot ?? "");
        Q("$pr", o.PrimerLot ?? ""); Q("$se", o.SeatingMm); Q("$bf", o.BrassFirings); Q("$br", o.BarrelRounds); Q("$no", o.Notes ?? "");
        o.Id = Convert.ToInt64(cmd.ExecuteScalar());
        return o.Id;
    }

    public void DeleteOuting(long id) => Exec("DELETE FROM outings WHERE id=$id", ("$id", id));

    private static Outing ReadOuting(SqliteDataReader r) => new()
    {
        Id = Lng(r, "id"), LoadId = Lng(r, "load_id"), Date = Str(r, "date"), TempC = Nul(r, "temp_c"), PressureHpa = Nul(r, "pressure_hpa"),
        HumidityPct = Nul(r, "humidity_pct"), ShotsMs = Str(r, "shots_ms"), GroupMoa = Nul(r, "group_moa"), DistanceM = Nul(r, "distance_m"),
        WindMps = Nul(r, "wind_mps"), WindDeg = Nul(r, "wind_deg"), Firearm = Str(r, "firearm"), PowderLot = Str(r, "powder_lot"),
        PrimerLot = Str(r, "primer_lot"), SeatingMm = Nul(r, "seating_mm"), BrassFirings = (int?)NulL(r, "brass_firings"),
        BarrelRounds = (int?)NulL(r, "barrel_rounds"), Notes = Str(r, "notes"),
    };

    // ---- helpers ------------------------------------------------------------

    private static string Str(SqliteDataReader r, string c) { int i = r.GetOrdinal(c); return r.IsDBNull(i) ? "" : r.GetString(i); }
    private static double Dbl(SqliteDataReader r, string c) { int i = r.GetOrdinal(c); return r.IsDBNull(i) ? 0 : r.GetDouble(i); }
    private static long Lng(SqliteDataReader r, string c) { int i = r.GetOrdinal(c); return r.IsDBNull(i) ? 0 : r.GetInt64(i); }
    private static double? Nul(SqliteDataReader r, string c) { int i = r.GetOrdinal(c); return r.IsDBNull(i) ? null : r.GetDouble(i); }
    private static long? NulL(SqliteDataReader r, string c) { int i = r.GetOrdinal(c); return r.IsDBNull(i) ? null : r.GetInt64(i); }

    private void Exec(string sql, params (string, object)[] ps)
    {
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _cn.Dispose();
}
