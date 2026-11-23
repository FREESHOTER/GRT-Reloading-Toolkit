using System.Globalization;
using System.Text.Json;
using GrtReloadingToolkit.Cartridges;

namespace GrtReloadingToolkit.Archive;

/// <summary>A case, a bullet or a primer measured before it is loaded. <see cref="Mark"/> is what is engraved or written on it (a case: "5A").</summary>
public sealed record PieceRow(long Id, long ShooterId, string Kind, string Mark, long? ComponentId, string Notes, bool Retired);

/// <param name="Purpose">"race": the lot kept for the match, never fired in training; "training": cartridges made for the training, not the match's.</param>
/// <param name="ReferenceLotId">For a training lot, the match lot it is meant to resemble (so that what is learned with it carries over).</param>
public sealed record CartridgeLotRow(long Id, long ShooterId, long? LoadId, string Name, string Notes, bool Archived, string CreatedAt, int Cartridges,
    string Purpose = "race", long? ReferenceLotId = null)
{
    public bool IsTraining => Purpose == "training";
}

public sealed record CartridgeRow(long Id, long LotId, int Number, long? CasePieceId, long? BulletPieceId, long? PrimerPieceId,
    string Target, bool OutOfSpec, string OutReason, string Notes);

/// <summary>What importing a weighing session did.</summary>
public sealed record WeighingImportResult(string BatchCode, int Added, int Updated, int SetAside, IReadOnlyList<string> Warnings);

/// <summary>A saved prediction as the list shows it.</summary>
public sealed record PredictionListItem(long Id, long LotId, string LotName, string Mode, string CreatedAt, string Note, bool Verified, int Cartridges);

/// <summary>What the model learns from: every shot linked to a cartridge, and the sessions those shots belong to.</summary>
/// <param name="TrainingShots">How many of the shots come from training lots, and <paramref name="RaceShots"/> from match lots.</param>
/// <summary>Where a shot the model learns from was fired: its session, the date of the session and whether the cartridge belongs to a training lot.</summary>
public sealed record ShotOrigin(long SessionId, string Date, bool Training);

/// <param name="Origins">One for each sample, in the same order.</param>
public sealed record LearningSet(IReadOnlyList<ShotSample> Samples, IReadOnlyList<long> SessionIds, int TrainingShots = 0, int RaceShots = 0,
    IReadOnlyList<ShotOrigin>? Origins = null);

/// <summary>One cartridge of a saved prediction.</summary>
public sealed record PredictionRowData(int Number, string Mark, string Assigned, int Order, int Series, double Estimate, double EstimateSe,
    Dictionary<string, double> Measures);

public sealed record PredictionRecord(long Id, long ShooterId, long LotId, string Mode, string CreatedAt, double ResidualSd, string Note,
    IReadOnlyList<PredictionRowData> Rows, string MetaJson, string? VerifiedAt, string? VerificationJson, long? VerificationSessionId);

/// <summary>
/// Pieces, cartridges, their measures and the predictions made from them. The tables only ever add to the archive (and a column on
/// <c>shots</c> that says which cartridge a shot came from). A prediction is saved BEFORE the shooting and never changes afterwards:
/// the database itself refuses to alter what it predicted, so the check after the shooting is honest. Only the verification can be
/// written later, and a verified prediction cannot be deleted.
/// </summary>
public static class CartridgeStore
{
    public static void CreateSchema(ArchiveDb a)
    {
        a.Exec(@"
CREATE TABLE IF NOT EXISTS pieces (
  id INTEGER PRIMARY KEY AUTOINCREMENT, shooter_id INTEGER NOT NULL REFERENCES shooters(id),
  kind TEXT NOT NULL CHECK (kind IN ('case','bullet','primer')), mark TEXT NOT NULL DEFAULT '',
  component_id INTEGER REFERENCES components(id), notes TEXT DEFAULT '', retired INTEGER DEFAULT 0, created_at TEXT, origin TEXT);
CREATE TABLE IF NOT EXISTS cartridge_lots (
  id INTEGER PRIMARY KEY AUTOINCREMENT, shooter_id INTEGER NOT NULL REFERENCES shooters(id),
  load_id INTEGER REFERENCES loads(id), name TEXT NOT NULL, notes TEXT DEFAULT '', archived INTEGER DEFAULT 0, created_at TEXT, origin TEXT);
CREATE TABLE IF NOT EXISTS cartridges (
  id INTEGER PRIMARY KEY AUTOINCREMENT, lot_id INTEGER NOT NULL REFERENCES cartridge_lots(id) ON DELETE CASCADE,
  number INTEGER NOT NULL, case_piece_id INTEGER REFERENCES pieces(id), bullet_piece_id INTEGER REFERENCES pieces(id),
  primer_piece_id INTEGER REFERENCES pieces(id), target TEXT DEFAULT '', out_of_spec INTEGER DEFAULT 0, out_reason TEXT DEFAULT '',
  notes TEXT DEFAULT '', origin TEXT, UNIQUE(lot_id, number));
CREATE TABLE IF NOT EXISTS measures (
  id INTEGER PRIMARY KEY AUTOINCREMENT, owner TEXT NOT NULL CHECK (owner IN ('piece','cartridge')), owner_id INTEGER NOT NULL,
  field TEXT NOT NULL, value REAL NOT NULL, readings INTEGER NOT NULL DEFAULT 1, measured_at TEXT, note TEXT DEFAULT '', origin TEXT,
  UNIQUE(owner, owner_id, field));
CREATE TABLE IF NOT EXISTS predictions (
  id INTEGER PRIMARY KEY AUTOINCREMENT, shooter_id INTEGER NOT NULL REFERENCES shooters(id),
  lot_id INTEGER NOT NULL REFERENCES cartridge_lots(id), mode TEXT NOT NULL CHECK (mode IN ('race','training')),
  created_at TEXT NOT NULL, model TEXT NOT NULL, rows TEXT NOT NULL, residual_sd REAL NOT NULL DEFAULT 0, note TEXT DEFAULT '',
  verified_at TEXT, verification TEXT, verification_session_id INTEGER REFERENCES sessions(id));
CREATE INDEX IF NOT EXISTS ix_pieces_shooter ON pieces(shooter_id, kind, mark);
CREATE INDEX IF NOT EXISTS ix_cartridges_lot ON cartridges(lot_id, number);
CREATE INDEX IF NOT EXISTS ix_measures_owner ON measures(owner, owner_id);
CREATE INDEX IF NOT EXISTS ix_predictions_lot ON predictions(lot_id);");
        // which cartridge a shot came from (the triggers below refer to it, so it comes first)
        a.AddColumnIfMissing("shots", "cartridge_id", "INTEGER REFERENCES cartridges(id)");
        // schema 6: what a lot is for (a lot made before this was for the match) and, for a training lot, the match lot it imitates
        a.AddColumnIfMissing("cartridge_lots", "purpose", "TEXT NOT NULL DEFAULT 'race' CHECK (purpose IN ('race','training'))");
        a.AddColumnIfMissing("cartridge_lots", "reference_lot_id", "INTEGER REFERENCES cartridge_lots(id)");
        a.Exec(@"
CREATE INDEX IF NOT EXISTS ix_shots_cartridge ON shots(cartridge_id);
CREATE TRIGGER IF NOT EXISTS trg_pieces_measures AFTER DELETE ON pieces
  BEGIN DELETE FROM measures WHERE owner='piece' AND owner_id=OLD.id; END;
CREATE TRIGGER IF NOT EXISTS trg_cartridges_measures AFTER DELETE ON cartridges
  BEGIN DELETE FROM measures WHERE owner='cartridge' AND owner_id=OLD.id; UPDATE shots SET cartridge_id=NULL WHERE cartridge_id=OLD.id; END;
-- a prediction is frozen: what it predicted can never be changed, and one that has been checked cannot be erased
CREATE TRIGGER IF NOT EXISTS trg_predictions_frozen
  BEFORE UPDATE OF shooter_id, lot_id, mode, created_at, model, rows, residual_sd ON predictions
  BEGIN SELECT RAISE(ABORT, 'prediction is frozen'); END;
CREATE TRIGGER IF NOT EXISTS trg_predictions_keep_verified
  BEFORE DELETE ON predictions WHEN OLD.verified_at IS NOT NULL
  BEGIN SELECT RAISE(ABORT, 'a verified prediction cannot be deleted'); END;");
    }

    // ---- pieces ----------------------------------------------------------------------------------------------------

    public static long AddPiece(ArchiveDb a, long shooterId, string kind, string mark, long? componentId = null, string notes = "")
        => a.Insert("pieces", ("shooter_id", shooterId), ("kind", kind), ("mark", mark.Trim()), ("component_id", componentId),
            ("notes", notes), ("created_at", ArchiveDb.Now()), ("origin", "tool:cartucce"));

    public static List<PieceRow> Pieces(ArchiveDb a, long shooterId, string? kind = null, bool includeRetired = true)
    {
        var list = new List<PieceRow>();
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = "SELECT id, shooter_id, kind, mark, component_id, IFNULL(notes,''), IFNULL(retired,0) FROM pieces " +
                          "WHERE shooter_id=$s AND ($k IS NULL OR kind=$k) AND ($r=1 OR IFNULL(retired,0)=0) ORDER BY kind, mark, id";
        cmd.Parameters.AddWithValue("$s", shooterId);
        cmd.Parameters.AddWithValue("$k", (object?)kind ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$r", includeRetired ? 1 : 0);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PieceRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetInt64(4), r.GetString(5), r.GetInt64(6) != 0));
        return list;
    }

    // ---- measures --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes one measure, in the unit of its field. <paramref name="owner"/> is "piece" or "cartridge". A measure taken again replaces the old
    /// one; with <paramref name="average"/> set the new reading is averaged into it (the program keeps the mean and how many readings made it).
    /// </summary>
    public static void SetMeasure(ArchiveDb a, string owner, long ownerId, string field, double value, bool average = false, string note = "")
    {
        if (!double.IsFinite(value)) throw new ArgumentException("A measure must be a finite number.", nameof(value));
        if (owner != "piece" && owner != "cartridge") throw new ArgumentException("owner must be piece or cartridge.", nameof(owner));
        a.InTransaction(() =>
        {
            if (average)
            {
                using var cmd = a.Connection.CreateCommand();
                cmd.Transaction = a.CurrentTransaction;
                cmd.CommandText = "SELECT value, readings FROM measures WHERE owner=$o AND owner_id=$i AND field=$f";
                cmd.Parameters.AddWithValue("$o", owner); cmd.Parameters.AddWithValue("$i", ownerId); cmd.Parameters.AddWithValue("$f", field);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    double old = r.GetDouble(0); long n = r.GetInt64(1);
                    r.Close();
                    a.Exec("UPDATE measures SET value=$v, readings=$n, measured_at=$t, note=$note WHERE owner=$o AND owner_id=$i AND field=$f",
                        ("$v", (old * n + value) / (n + 1)), ("$n", n + 1), ("$t", ArchiveDb.Now()), ("$note", note), ("$o", owner), ("$i", ownerId), ("$f", field));
                    return;
                }
            }
            a.Exec(@"INSERT INTO measures(owner, owner_id, field, value, readings, measured_at, note, origin) VALUES($o,$i,$f,$v,1,$t,$note,'tool:cartucce')
                     ON CONFLICT(owner, owner_id, field) DO UPDATE SET value=excluded.value, readings=1, measured_at=excluded.measured_at, note=excluded.note",
                ("$o", owner), ("$i", ownerId), ("$f", field), ("$v", value), ("$t", ArchiveDb.Now()), ("$note", note));
        });
    }

    public static Dictionary<string, double> Measures(ArchiveDb a, string owner, long ownerId)
    {
        var d = new Dictionary<string, double>(StringComparer.Ordinal);
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = "SELECT field, value FROM measures WHERE owner=$o AND owner_id=$i ORDER BY field";
        cmd.Parameters.AddWithValue("$o", owner); cmd.Parameters.AddWithValue("$i", ownerId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[r.GetString(0)] = r.GetDouble(1);
        return d;
    }

    // ---- lots and cartridges ---------------------------------------------------------------------------------------

    public static long CreateLot(ArchiveDb a, long shooterId, string name, long? loadId = null, string notes = "", string purpose = "race", long? referenceLotId = null)
    {
        if (purpose is not ("race" or "training")) throw new ArgumentException("purpose must be race or training", nameof(purpose));
        if (referenceLotId is { } r) CheckReference(a, purpose, r, null);
        return a.Insert("cartridge_lots", ("shooter_id", shooterId), ("load_id", loadId), ("name", name.Trim()), ("notes", notes),
            ("created_at", ArchiveDb.Now()), ("origin", "tool:cartucce"), ("purpose", purpose), ("reference_lot_id", referenceLotId));
    }

    /// <summary>Only a training lot has a reference, and it is a match lot (not itself).</summary>
    private static void CheckReference(ArchiveDb a, string purpose, long referenceLotId, long? lotId)
    {
        if (purpose != "training") throw new InvalidOperationException("only a training lot has a reference lot");
        if (lotId == referenceLotId) throw new InvalidOperationException("a lot cannot be its own reference");
        string? p = a.ScalarP("SELECT purpose FROM cartridge_lots WHERE id=$i", ("$i", referenceLotId)) as string;
        if (p != "race") throw new InvalidOperationException("the reference of a training lot must be a match lot");
    }

    /// <summary>Which match lot a training lot is meant to resemble (null to clear). Only a training lot can have one.</summary>
    public static void SetReference(ArchiveDb a, long lotId, long? referenceLotId)
    {
        string? purpose = a.ScalarP("SELECT purpose FROM cartridge_lots WHERE id=$i", ("$i", lotId)) as string;
        if (purpose == null) throw new InvalidOperationException("no such lot");
        if (referenceLotId is { } r) CheckReference(a, purpose, r, lotId);
        a.Exec("UPDATE cartridge_lots SET reference_lot_id=$r WHERE id=$i", ("$r", referenceLotId), ("$i", lotId));
    }

    /// <summary>
    /// Turns a lot into a match lot or a training lot. A lot that has shots linked to its cartridges keeps its purpose (what it was shot for would
    /// be rewritten), and a match lot that is the reference of a training lot cannot become one.
    /// </summary>
    public static void SetPurpose(ArchiveDb a, long lotId, string purpose)
    {
        if (purpose is not ("race" or "training")) throw new ArgumentException("purpose must be race or training", nameof(purpose));
        string? current = a.ScalarP("SELECT purpose FROM cartridge_lots WHERE id=$i", ("$i", lotId)) as string;
        if (current == null) throw new InvalidOperationException("no such lot");
        if (current == purpose) return;
        long shots = Convert.ToInt64(a.ScalarP("SELECT COUNT(*) FROM shots WHERE cartridge_id IN (SELECT id FROM cartridges WHERE lot_id=$i)", ("$i", lotId)) ?? 0L);
        if (shots > 0) throw new InvalidOperationException("the lot already has shots linked to its cartridges: its purpose cannot change");
        if (purpose == "training" && Convert.ToInt64(a.ScalarP("SELECT COUNT(*) FROM cartridge_lots WHERE reference_lot_id=$i", ("$i", lotId)) ?? 0L) > 0)
            throw new InvalidOperationException("this lot is the reference of a training lot: it must stay a match lot");
        a.Exec("UPDATE cartridge_lots SET purpose=$p, reference_lot_id=NULL WHERE id=$i", ("$p", purpose), ("$i", lotId));
    }

    public static List<CartridgeLotRow> Lots(ArchiveDb a, long shooterId, bool includeArchived = false)
    {
        var list = new List<CartridgeLotRow>();
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = @"SELECT l.id, l.shooter_id, l.load_id, l.name, IFNULL(l.notes,''), IFNULL(l.archived,0), IFNULL(l.created_at,''),
                                   (SELECT COUNT(*) FROM cartridges c WHERE c.lot_id=l.id), IFNULL(l.purpose,'race'), l.reference_lot_id
                            FROM cartridge_lots l WHERE l.shooter_id=$s AND ($all=1 OR IFNULL(l.archived,0)=0) ORDER BY l.id DESC";
        cmd.Parameters.AddWithValue("$s", shooterId);
        cmd.Parameters.AddWithValue("$all", includeArchived ? 1 : 0);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CartridgeLotRow(r.GetInt64(0), r.GetInt64(1), r.IsDBNull(2) ? null : r.GetInt64(2), r.GetString(3), r.GetString(4),
                r.GetInt64(5) != 0, r.GetString(6), (int)r.GetInt64(7), r.GetString(8), r.IsDBNull(9) ? null : r.GetInt64(9)));
        return list;
    }

    public static long AddCartridge(ArchiveDb a, long lotId, int number, long? casePieceId = null, long? bulletPieceId = null, long? primerPieceId = null,
        string target = "", bool outOfSpec = false, string outReason = "")
        => a.Insert("cartridges", ("lot_id", lotId), ("number", number), ("case_piece_id", casePieceId), ("bullet_piece_id", bulletPieceId),
            ("primer_piece_id", primerPieceId), ("target", target), ("out_of_spec", outOfSpec ? 1 : 0), ("out_reason", outReason), ("origin", "tool:cartucce"));

    public static List<CartridgeRow> Cartridges(ArchiveDb a, long lotId)
    {
        var list = new List<CartridgeRow>();
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = "SELECT id, lot_id, number, case_piece_id, bullet_piece_id, primer_piece_id, IFNULL(target,''), IFNULL(out_of_spec,0), " +
                          "IFNULL(out_reason,''), IFNULL(notes,'') FROM cartridges WHERE lot_id=$l ORDER BY number";
        cmd.Parameters.AddWithValue("$l", lotId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CartridgeRow(r.GetInt64(0), r.GetInt64(1), (int)r.GetInt64(2), N(r, 3), N(r, 4), N(r, 5), r.GetString(6), r.GetInt64(7) != 0,
                r.GetString(8), r.GetString(9)));
        return list;
    }

    private static long? N(Microsoft.Data.Sqlite.SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    /// <summary>Every measure of a cartridge: those of its case, bullet and primer, and those taken on the loaded round. Their keys never clash.</summary>
    public static Dictionary<string, double> MergedMeasures(ArchiveDb a, CartridgeRow c)
    {
        var d = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (long? pid in new[] { c.CasePieceId, c.BulletPieceId, c.PrimerPieceId })
            if (pid is { } id) foreach (var kv in Measures(a, "piece", id)) d[kv.Key] = kv.Value;
        foreach (var kv in Measures(a, "cartridge", c.Id)) d[kv.Key] = kv.Value;
        return d;
    }

    /// <summary>The cartridges of a lot as the planner wants them: the mark is that of the case.</summary>
    public static List<CartridgeInput> CartridgeInputs(ArchiveDb a, long lotId)
    {
        var marks = new Dictionary<long, string>();
        using (var cmd = a.Connection.CreateCommand())
        {
            cmd.Transaction = a.CurrentTransaction;
            cmd.CommandText = "SELECT p.id, p.mark FROM pieces p WHERE p.id IN (SELECT case_piece_id FROM cartridges WHERE lot_id=$l)";
            cmd.Parameters.AddWithValue("$l", lotId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) marks[r.GetInt64(0)] = r.GetString(1);
        }
        return Cartridges(a, lotId)
            .Select(c => new CartridgeInput(c.Number, c.CasePieceId is { } id && marks.TryGetValue(id, out string? m) ? m : "", MergedMeasures(a, c), c.OutOfSpec, c.Target))
            .ToList();
    }

    /// <summary>The piece of that kind with that mark (a case's mark written any way counts: "5A" is "A5"), or a new one.</summary>
    public static long FindOrAddPiece(ArchiveDb a, long shooterId, string kind, string mark)
    {
        string norm = MeasureFields.NormalizeMark(mark);
        if (norm.Length == 0) throw new ArgumentException("A piece needs a mark.", nameof(mark));
        foreach (var p in Pieces(a, shooterId, kind))
            if (MeasureFields.NormalizeMark(p.Mark) == norm) return p.Id;
        return AddPiece(a, shooterId, kind, mark);
    }

    public static void DeleteMeasure(ArchiveDb a, string owner, long ownerId, string field)
        => a.Exec("DELETE FROM measures WHERE owner=$o AND owner_id=$i AND field=$f", ("$o", owner), ("$i", ownerId), ("$f", field));

    /// <summary>Removes a piece with its measures; the cartridges that used it are left without it.</summary>
    public static void DeletePiece(ArchiveDb a, long pieceId)
        => a.InTransaction(() =>
        {
            foreach (string col in new[] { "case_piece_id", "bullet_piece_id", "primer_piece_id" })
                a.Exec($"UPDATE cartridges SET {col}=NULL WHERE {col}=$p", ("$p", pieceId));
            a.Exec("DELETE FROM pieces WHERE id=$p", ("$p", pieceId));
        });

    public static void UpdateCartridge(ArchiveDb a, long cartridgeId, long? casePieceId, long? bulletPieceId, long? primerPieceId,
        string target, bool outOfSpec, string outReason = "")
        => a.Exec(@"UPDATE cartridges SET case_piece_id=$c, bullet_piece_id=$b, primer_piece_id=$p, target=$t, out_of_spec=$o, out_reason=$r WHERE id=$i",
            ("$c", casePieceId), ("$b", bulletPieceId), ("$p", primerPieceId), ("$t", target), ("$o", outOfSpec ? 1 : 0), ("$r", outReason), ("$i", cartridgeId));

    /// <summary>Removes a cartridge, its measures, and the link of the shots fired from it (the shots stay).</summary>
    public static void DeleteCartridge(ArchiveDb a, long cartridgeId) => a.Exec("DELETE FROM cartridges WHERE id=$i", ("$i", cartridgeId));

    /// <summary>Removes a lot with its cartridges. A lot that predictions were made for cannot go: they are kept as they were saved.</summary>
    public static void DeleteLot(ArchiveDb a, long lotId)
    {
        if (a.Count("predictions", $"lot_id={lotId}") > 0) throw new InvalidOperationException(Tr.T("Predictions were made for this lot: it cannot be deleted."));
        a.InTransaction(() =>
        {
            a.Exec("UPDATE cartridge_lots SET reference_lot_id=NULL WHERE reference_lot_id=$i", ("$i", lotId));   // a training lot loses the match lot it imitated
            a.Exec("DELETE FROM cartridge_lots WHERE id=$i", ("$i", lotId));
        });
    }

    /// <summary>The next free number in a lot (1 for an empty one).</summary>
    public static int NextNumber(ArchiveDb a, long lotId)
        => (a.ScalarP("SELECT IFNULL(MAX(number),0)+1 FROM cartridges WHERE lot_id=$l", ("$l", lotId)) is long n ? (int)n : 1);

    // ---- weighing sessions -----------------------------------------------------------------------------------------

    /// <summary>
    /// Takes a weighing session (<see cref="WeighingReader"/>) into the archive: one piece for each tray hole that has a measure, with its measures
    /// in the unit of their field. Reading the same session again (it keeps the moment it was created) updates the same pieces instead of adding
    /// new ones. Each session gets a short code (O1, O2… for bullets, B1… for cases, I1… for primers) and its pieces are marked "O1·7C"
    /// (the hole as it is engraved: number, then letter), so two sessions never share a mark. With <paramref name="engravedMarks"/> the
    /// marks are the holes themselves ("7C") and the measures go to the cases that already carry that mark: for a tray whose holes ARE the
    /// codes engraved on the cases. Pieces set aside by the program (outside its weight limits) are kept, marked as retired.
    /// </summary>
    public static WeighingImportResult ImportWeighing(ArchiveDb a, long shooterId, WeighingBatch b, bool engravedMarks = false, string sourcePath = "")
    {
        int added = 0, updated = 0, aside = 0;
        string code = "";
        a.InTransaction(() =>
        {
            code = engravedMarks && b.PieceKind == "case" ? "" : BatchCode(a, b);          // with the engraved codes the session needs none
            foreach (var p in b.Pieces)
            {
                string engraved = p.HoleName.Length > 0 ? MeasureFields.EngravedCode(p.HoleName) : p.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string mark = engravedMarks && b.PieceKind == "case" ? engraved
                    : code + "·" + (p.Tray is > 1 ? "T" + p.Tray + "·" : "") + engraved;
                string origin = $"pesata:{b.Identity}#{p.Tray ?? 1}.{p.Hole ?? p.Number}";
                string notes = b.Name + (p.Note.Length > 0 ? " · " + p.Note : "");

                long id;
                if (engravedMarks && b.PieceKind == "case")
                {
                    int before = Pieces(a, shooterId, "case").Count;
                    id = FindOrAddPiece(a, shooterId, "case", mark);
                    if (Pieces(a, shooterId, "case").Count > before) added++; else updated++;
                }
                else if (a.ScalarP("SELECT id FROM pieces WHERE origin=$o", ("$o", origin)) is long existing)
                {
                    id = existing; updated++;
                    a.Exec("UPDATE pieces SET notes=$n WHERE id=$i", ("$n", notes), ("$i", id));
                }
                else
                {
                    id = a.Insert("pieces", ("shooter_id", shooterId), ("kind", b.PieceKind), ("mark", mark), ("notes", notes),
                        ("created_at", ArchiveDb.Now()), ("origin", origin));
                    added++;
                }
                if (p.Rejected) { a.Exec("UPDATE pieces SET retired=1 WHERE id=$i", ("$i", id)); aside++; }
                foreach (var (field, value) in p.Measures)
                    SetMeasure(a, "piece", id, field, value, false, p.MeasureNotes.GetValueOrDefault(field, ""));
            }
            a.Insert("imports", ("at", ArchiveDb.Now()), ("source", "pesate: " + b.Program), ("path", sourcePath),
                ("summary", $"{b.Name}: {added} pezzi nuovi, {updated} aggiornati, " + (code.Length > 0 ? "codice " + code : "codici incisi")));
        });
        return new WeighingImportResult(code, added, updated, aside, b.Warnings);
    }

    /// <summary>The code of a session, given the first time it is imported and kept after: the next free number of its kind (O1, O2… / B1… / I1…).</summary>
    private static string BatchCode(ArchiveDb a, WeighingBatch b)
    {
        string key = "pesata.batch." + b.Identity;
        if (a.GetMeta(key) is { Length: > 0 } known) return known;
        string letter = b.PieceKind switch { "bullet" => "O", "primer" => "I", _ => "B" };
        string counter = "pesata.counter." + letter;
        int next = int.TryParse(a.GetMeta(counter), out int n) ? n + 1 : 1;
        a.SetMeta(counter, next.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string code = letter + next.ToString(System.Globalization.CultureInfo.InvariantCulture);
        a.SetMeta(key, code);
        return code;
    }

    // ---- shots ↔ cartridges ----------------------------------------------------------------------------------------

    /// <summary>Says which cartridge a chronographed shot was fired from. Null removes the link.</summary>
    public static void LinkShot(ArchiveDb a, long shotId, long? cartridgeId)
        => a.Exec("UPDATE shots SET cartridge_id=$c WHERE id=$s", ("$c", cartridgeId), ("$s", shotId));

    /// <summary>
    /// Links the shots of a string, in firing order, to the cartridges named by <paramref name="marks"/> (matched on the case mark, written
    /// any way: "5A", "A5"…). A mark that is not in the lot, or a shot set aside, stays unlinked. Returns how many shots were linked.
    /// </summary>
    public static int LinkStringByMarks(ArchiveDb a, long stringId, long lotId, IReadOnlyList<string> marks)
    {
        var byMark = new Dictionary<string, long>(StringComparer.Ordinal);
        var marksById = new Dictionary<long, string>();
        using (var cmd = a.Connection.CreateCommand())
        {
            cmd.Transaction = a.CurrentTransaction;
            cmd.CommandText = "SELECT c.id, p.mark FROM cartridges c JOIN pieces p ON p.id=c.case_piece_id WHERE c.lot_id=$l";
            cmd.Parameters.AddWithValue("$l", lotId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) byMark[MeasureFields.NormalizeMark(r.GetString(1))] = r.GetInt64(0);
        }
        var shotIds = new List<long>();
        using (var cmd = a.Connection.CreateCommand())
        {
            cmd.Transaction = a.CurrentTransaction;
            cmd.CommandText = "SELECT id FROM shots WHERE string_id=$s ORDER BY idx";
            cmd.Parameters.AddWithValue("$s", stringId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) shotIds.Add(r.GetInt64(0));
        }
        int linked = 0;
        a.InTransaction(() =>
        {
            for (int i = 0; i < Math.Min(shotIds.Count, marks.Count); i++)
                if (byMark.TryGetValue(MeasureFields.NormalizeMark(marks[i]), out long cid)) { LinkShot(a, shotIds[i], cid); linked++; }
        });
        return linked;
    }

    /// <summary>
    /// Every shot linked to a cartridge, with that cartridge's measures: what <see cref="VelocityModel"/> learns from. A series is a chronograph
    /// string (the velocity moves with the day and the barrel, so the model only ever compares shots of one string). Shots set aside are left out.
    /// </summary>
    public static LearningSet LearningShots(ArchiveDb a, long? shooterId = null)
    {
        var rows = new List<(long Shot, long String, long Session, double V, long Cartridge, bool Training, string Date)>();
        using (var cmd = a.Connection.CreateCommand())
        {
            cmd.Transaction = a.CurrentTransaction;
            cmd.CommandText = @"SELECT sh.id, sh.string_id, st.session_id, sh.velocity_mps, sh.cartridge_id, IFNULL(lt.purpose,'race'), IFNULL(se.date,'')
                                FROM shots sh JOIN strings st ON st.id=sh.string_id JOIN sessions se ON se.id=st.session_id
                                LEFT JOIN cartridges ca ON ca.id=sh.cartridge_id LEFT JOIN cartridge_lots lt ON lt.id=ca.lot_id
                                WHERE sh.cartridge_id IS NOT NULL AND IFNULL(sh.set_aside,0)=0 AND sh.velocity_mps > 0
                                  AND ($s IS NULL OR se.shooter_id=$s) ORDER BY sh.string_id, sh.idx";
            cmd.Parameters.AddWithValue("$s", (object?)shooterId ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read()) rows.Add((r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetDouble(3), r.GetInt64(4), r.GetString(5) == "training", r.GetString(6)));
        }
        var cache = new Dictionary<long, Dictionary<string, double>>();
        var samples = new List<ShotSample>();
        foreach (var r in rows)
        {
            if (!cache.TryGetValue(r.Cartridge, out var m))
            {
                var c = CartridgeById(a, r.Cartridge);
                cache[r.Cartridge] = m = c is null ? new Dictionary<string, double>() : MergedMeasures(a, c);
            }
            samples.Add(new ShotSample((int)r.String, r.V, m));
        }
        return new LearningSet(samples, rows.Select(r => r.Session).Distinct().OrderBy(x => x).ToList(), rows.Count(r => r.Training), rows.Count(r => !r.Training),
            rows.Select(r => new ShotOrigin(r.Session, r.Date, r.Training)).ToList());
    }

    /// <summary>
    /// The chronograph strings of the latest training session (the newest date, then the newest session): the ones with shots linked to the cartridges of a
    /// training lot, in the order they were fired. Empty when nothing was ever shot with a training lot.
    /// </summary>
    public static List<long> LatestTrainingStrings(ArchiveDb a, long shooterId)
    {
        var ids = new List<long>();
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = @"
SELECT DISTINCT st.id FROM strings st
JOIN shots sh ON sh.string_id=st.id JOIN cartridges ca ON ca.id=sh.cartridge_id JOIN cartridge_lots lt ON lt.id=ca.lot_id
WHERE lt.purpose='training' AND st.session_id=(
    SELECT se.id FROM sessions se JOIN strings s2 ON s2.session_id=se.id JOIN shots h ON h.string_id=s2.id JOIN cartridges c2 ON c2.id=h.cartridge_id
      JOIN cartridge_lots l2 ON l2.id=c2.lot_id
    WHERE l2.purpose='training' AND se.shooter_id=$s ORDER BY IFNULL(se.date,'') DESC, se.id DESC LIMIT 1)
ORDER BY st.id";
        cmd.Parameters.AddWithValue("$s", shooterId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt64(0));
        return ids;
    }

    /// <summary>
    /// The shots of the given chronograph strings, one series per string, each with the measures of its cartridge when the shot is linked to one
    /// (an unlinked shot has none and still counts toward the velocity). Shots set aside and empty slots are left out.
    /// </summary>
    public static List<ShotSample> StringSamples(ArchiveDb a, IReadOnlyList<long> stringIds)
    {
        var samples = new List<ShotSample>();
        var cache = new Dictionary<long, Dictionary<string, double>>();
        foreach (long sid in stringIds)
        {
            var rows = new List<(double V, long? Cart)>();
            using (var cmd = a.Connection.CreateCommand())
            {
                cmd.Transaction = a.CurrentTransaction;
                cmd.CommandText = "SELECT velocity_mps, cartridge_id FROM shots WHERE string_id=$s AND IFNULL(set_aside,0)=0 AND velocity_mps > 0 ORDER BY idx";
                cmd.Parameters.AddWithValue("$s", sid);
                using var r = cmd.ExecuteReader();
                while (r.Read()) rows.Add((r.GetDouble(0), r.IsDBNull(1) ? null : r.GetInt64(1)));
            }
            foreach (var (v, cart) in rows)
            {
                Dictionary<string, double> m = new();
                if (cart is { } c && !cache.TryGetValue(c, out m!))
                    cache[c] = m = CartridgeById(a, c) is { } row ? MergedMeasures(a, row) : new Dictionary<string, double>();
                samples.Add(new ShotSample((int)sid, v, m ?? new Dictionary<string, double>()));
            }
        }
        return samples;
    }

    private static CartridgeRow? CartridgeById(ArchiveDb a, long id)
    {
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = "SELECT id, lot_id, number, case_piece_id, bullet_piece_id, primer_piece_id, IFNULL(target,''), IFNULL(out_of_spec,0), " +
                          "IFNULL(out_reason,''), IFNULL(notes,'') FROM cartridges WHERE id=$i";
        cmd.Parameters.AddWithValue("$i", id);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new CartridgeRow(r.GetInt64(0), r.GetInt64(1), (int)r.GetInt64(2), N(r, 3), N(r, 4), N(r, 5), r.GetString(6), r.GetInt64(7) != 0, r.GetString(8), r.GetString(9))
            : null;
    }

    // ---- predictions -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Saves a prediction for a lot BEFORE it is shot. It stores each cartridge's estimate, the model it came from and the sessions that model
    /// learned from (a check on those is not independent). From here on the database refuses to change any of it.
    /// </summary>
    public static long SavePrediction(ArchiveDb a, long shooterId, long lotId, CartridgePlan plan, VelocityModelResult model,
        IReadOnlyList<long> learnedSessions, string note = "")
    {
        if (plan.Error is not null) throw new InvalidOperationException("A plan with an error cannot be saved: " + plan.Error);
        var keys = model.Significant.Select(t => t.Key).ToList();
        var rows = plan.Groups.SelectMany(g => g.Cartridges).Select(x => new PredictionRowData(
            x.Number, x.Cartridge.Mark, x.Assigned, x.Order, x.SeriesNumber, x.Estimate, x.EstimateSe,
            keys.Where(k => x.Cartridge.Measures.ContainsKey(k)).ToDictionary(k => k, k => x.Cartridge.Measures[k]))).OrderBy(r => r.Number).ToList();
        var meta = new
        {
            terms = model.Significant.Select(t => new { t.Key, t.Slope, t.Se, t.R, t.PAdj, t.N, t.Series, t.SdX }).ToList(),
            n_shots = model.NShots, n_series = model.NSeries, sd_within = model.SdWithin, r2 = model.R2, sd_resid = model.SdResidual,
            learned_sessions = learnedSessions,
            groups = plan.Groups.Select(g => new { g.Name, g.PredictedSd, g.CartridgeSd, g.From, g.To }).ToList(),
            sd_at_random = plan.SdAtRandom, cartridge_sd_at_random = plan.CartridgeSdAtRandom, median = plan.Median,
            excluded = plan.Excluded.Select(x => new { x.Number, x.Cartridge.Mark, why = x.Cartridge.OutOfSpec ? "out of spec" : "missing " + string.Join(", ", x.Missing) }).ToList(),
        };
        return a.Insert("predictions", ("shooter_id", shooterId), ("lot_id", lotId), ("mode", plan.Mode == PlanMode.Race ? "race" : "training"),
            ("created_at", ArchiveDb.Now()), ("model", JsonSerializer.Serialize(meta)), ("rows", JsonSerializer.Serialize(rows)),
            ("residual_sd", model.SdResidual), ("note", note));
    }

    public static List<PredictionListItem> Predictions(ArchiveDb a, long shooterId)
    {
        var list = new List<PredictionListItem>();
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = @"SELECT p.id, p.lot_id, l.name, p.mode, p.created_at, IFNULL(p.note,''), p.verified_at IS NOT NULL, json_array_length(p.rows)
                            FROM predictions p JOIN cartridge_lots l ON l.id=p.lot_id WHERE p.shooter_id=$s ORDER BY p.id DESC";
        cmd.Parameters.AddWithValue("$s", shooterId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PredictionListItem(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt64(6) != 0, (int)r.GetInt64(7)));
        return list;
    }

    public static PredictionRecord? GetPrediction(ArchiveDb a, long id)
    {
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = "SELECT id, shooter_id, lot_id, mode, created_at, residual_sd, IFNULL(note,''), rows, model, verified_at, verification, verification_session_id " +
                          "FROM predictions WHERE id=$i";
        cmd.Parameters.AddWithValue("$i", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var rows = JsonSerializer.Deserialize<List<PredictionRowData>>(r.GetString(7)) ?? new();
        return new PredictionRecord(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetDouble(5), r.GetString(6), rows,
            r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetInt64(11));
    }

    /// <summary>
    /// Checks a prediction against a session: the shots of that session fired from cartridges of the prediction's lot are set against the estimates
    /// the prediction saved (matched by cartridge number). Only the verification is written; the prediction itself cannot change. Returns null
    /// when too few shots can be matched. <see cref="PredictionCheckResult"/> comes with whether the session was one the model learned from.
    /// </summary>
    public static (PredictionCheckResult Result, bool Independent)? Verify(ArchiveDb a, long predictionId, long sessionId)
    {
        var p = GetPrediction(a, predictionId) ?? throw new ArgumentException("No such prediction.", nameof(predictionId));
        var byNumber = p.Rows.ToDictionary(r => r.Number);
        var shots = new List<CheckedShot>();
        using (var cmd = a.Connection.CreateCommand())
        {
            cmd.Transaction = a.CurrentTransaction;
            cmd.CommandText = @"SELECT sh.string_id, sh.velocity_mps, c.number
                                FROM shots sh JOIN strings st ON st.id=sh.string_id JOIN cartridges c ON c.id=sh.cartridge_id
                                WHERE st.session_id=$se AND c.lot_id=$l AND IFNULL(sh.set_aside,0)=0 AND sh.velocity_mps > 0 ORDER BY sh.string_id, sh.idx";
            cmd.Parameters.AddWithValue("$se", sessionId); cmd.Parameters.AddWithValue("$l", p.LotId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (byNumber.TryGetValue((int)r.GetInt64(2), out var row))
                    shots.Add(new CheckedShot(r.GetInt64(0).ToString(CultureInfo.InvariantCulture), r.GetDouble(1), row.Estimate));
        }
        var res = PredictionCheck.Verify(shots);
        if (res is null) return null;
        bool independent = true;
        using (var doc = JsonDocument.Parse(p.MetaJson))
            if (doc.RootElement.TryGetProperty("learned_sessions", out var ls))
                foreach (var s in ls.EnumerateArray()) if (s.GetInt64() == sessionId) independent = false;
        a.Exec("UPDATE predictions SET verified_at=$t, verification=$v, verification_session_id=$s WHERE id=$i",
            ("$t", ArchiveDb.Now()), ("$v", JsonSerializer.Serialize(new { result = res, independent })), ("$s", sessionId), ("$i", predictionId));
        return (res, independent);
    }
}
