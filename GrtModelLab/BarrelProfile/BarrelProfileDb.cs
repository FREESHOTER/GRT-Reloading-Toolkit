using Microsoft.Data.Sqlite;

namespace GrtModelLab.BarrelProfile;

/// <summary>SQLite store for saved barrel profiles — own database, separate from the Reloading Toolkit's.</summary>
public sealed class BarrelProfileDb : IDisposable
{
    private readonly SqliteConnection _cn;

    public string Path { get; }

    public BarrelProfileDb(string path)
    {
        Path = path;
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _cn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        _cn.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        Migrate();
    }

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GRTPlugins", "model_lab.db");

    private void Migrate() => Exec(@"
CREATE TABLE IF NOT EXISTS barrel_profiles (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  steel_label TEXT NOT NULL,
  elastic_modulus_gpa REAL NOT NULL,
  density_kg_m3 REAL NOT NULL,
  bore_diameter_mm REAL NOT NULL,
  length_mm REAL NOT NULL,
  created_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS barrel_profile_points (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  profile_id INTEGER NOT NULL REFERENCES barrel_profiles(id) ON DELETE CASCADE,
  position_mm REAL NOT NULL,
  od_mm REAL NOT NULL
);");

    public List<(long Id, string Name)> ListProfiles()
    {
        var list = new List<(long, string)>();
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM barrel_profiles ORDER BY name";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1)));
        return list;
    }

    public BarrelProfileRecord? Load(long id)
    {
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT id, name, steel_label, elastic_modulus_gpa, density_kg_m3, bore_diameter_mm, length_mm FROM barrel_profiles WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var rec = new BarrelProfileRecord
        {
            Id = r.GetInt64(0),
            Name = r.GetString(1),
            SteelLabel = r.GetString(2),
            ElasticModulusGPa = r.GetDouble(3),
            DensityKgM3 = r.GetDouble(4),
            BoreDiameterMm = r.GetDouble(5),
            LengthMm = r.GetDouble(6),
        };
        r.Close();

        using var pcmd = _cn.CreateCommand();
        pcmd.CommandText = "SELECT position_mm, od_mm FROM barrel_profile_points WHERE profile_id=$id ORDER BY position_mm";
        pcmd.Parameters.AddWithValue("$id", id);
        using var pr = pcmd.ExecuteReader();
        while (pr.Read()) rec.Points.Add(new BarrelProfilePoint { PositionMm = pr.GetDouble(0), OuterDiameterMm = pr.GetDouble(1) });
        return rec;
    }

    /// <summary>Insert or, if <see cref="BarrelProfileRecord.Id"/> is set, replace a profile and its points.</summary>
    public long Save(BarrelProfileRecord rec)
    {
        using var tx = _cn.BeginTransaction();
        if (rec.Id > 0)
        {
            Exec("DELETE FROM barrel_profile_points WHERE profile_id=$id", tx, ("$id", rec.Id));
            Exec(@"UPDATE barrel_profiles SET name=$n, steel_label=$s, elastic_modulus_gpa=$e,
                   density_kg_m3=$d, bore_diameter_mm=$b, length_mm=$l WHERE id=$id", tx,
                ("$n", rec.Name), ("$s", rec.SteelLabel), ("$e", rec.ElasticModulusGPa),
                ("$d", rec.DensityKgM3), ("$b", rec.BoreDiameterMm), ("$l", rec.LengthMm), ("$id", rec.Id));
        }
        else
        {
            using var cmd = _cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO barrel_profiles(name, steel_label, elastic_modulus_gpa, density_kg_m3, bore_diameter_mm, length_mm, created_utc)
                VALUES ($n,$s,$e,$d,$b,$l,$c); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$n", rec.Name);
            cmd.Parameters.AddWithValue("$s", rec.SteelLabel);
            cmd.Parameters.AddWithValue("$e", rec.ElasticModulusGPa);
            cmd.Parameters.AddWithValue("$d", rec.DensityKgM3);
            cmd.Parameters.AddWithValue("$b", rec.BoreDiameterMm);
            cmd.Parameters.AddWithValue("$l", rec.LengthMm);
            cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));
            rec.Id = (long)cmd.ExecuteScalar()!;
        }
        foreach (var p in rec.Points)
            Exec("INSERT INTO barrel_profile_points(profile_id, position_mm, od_mm) VALUES ($id,$p,$o)", tx,
                ("$id", rec.Id), ("$p", p.PositionMm), ("$o", p.OuterDiameterMm));
        tx.Commit();
        return rec.Id;
    }

    public void Delete(long id)
    {
        using var tx = _cn.BeginTransaction();
        Exec("DELETE FROM barrel_profile_points WHERE profile_id=$id", tx, ("$id", id));
        Exec("DELETE FROM barrel_profiles WHERE id=$id", tx, ("$id", id));
        tx.Commit();
    }

    private void Exec(string sql, SqliteTransaction? tx = null, params (string, object)[] args)
    {
        using var cmd = _cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _cn.Dispose();
}
