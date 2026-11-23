using System.Globalization;
using Microsoft.Data.Sqlite;

namespace GrtReloadingToolkit.Archive;

/// <summary>
/// Dated copies of the archive, the newest <see cref="Keep"/> kept. The copy is made with SQLite's own backup, so it is
/// consistent even while the archive is open, and is checked before the old copies are pruned: a failed copy never costs
/// a good one. Only files this class named (<c>archivio_yyyy-MM-dd_HHmmss.db</c>) are ever deleted.
/// </summary>
public static class ArchiveBackup
{
    public const int Keep = 30;
    private const string Prefix = "archivio_";
    private const string Stamp = "yyyy-MM-dd_HHmmss";

    public static string Copy(string dbPath, string destDir, int keep = Keep, DateTime? now = null)
    {
        if (!File.Exists(dbPath)) throw new FileNotFoundException("Archivio non trovato.", dbPath);
        Directory.CreateDirectory(destDir);
        string name = Prefix + (now ?? DateTime.Now).ToString(Stamp, CultureInfo.InvariantCulture) + ".db";
        string dest = Path.Combine(destDir, name);
        string tmp = dest + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);

        using (var src = Open(dbPath, SqliteOpenMode.ReadOnly))
        using (var dst = Open(tmp, SqliteOpenMode.ReadWriteCreate))
            src.BackupDatabase(dst);
        using (var chk = Open(tmp, SqliteOpenMode.ReadOnly))
        using (var cmd = chk.CreateCommand())
        {
            cmd.CommandText = "PRAGMA integrity_check";
            string r = cmd.ExecuteScalar()?.ToString() ?? "";
            if (r != "ok") { chk.Close(); File.Delete(tmp); throw new InvalidDataException("La copia non ha passato il controllo di integrità: " + r); }
        }
        SqliteConnection.ClearAllPools();
        File.Move(tmp, dest, overwrite: true);

        foreach (string old in List(destDir).Skip(keep)) File.Delete(old);
        return dest;
    }

    /// <summary>This class's copies in <paramref name="dir"/>, newest first.</summary>
    public static IReadOnlyList<string> List(string dir)
        => !Directory.Exists(dir) ? Array.Empty<string>()
            : Directory.GetFiles(dir, Prefix + "*.db")
                .Where(f => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f)[Prefix.Length..], Stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                .ToList();

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var cn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        cn.Open();
        return cn;
    }
}
