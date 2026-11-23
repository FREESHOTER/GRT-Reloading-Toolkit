namespace GrtReloadingToolkit.Archive;

/// <summary>One component of the library as a list shows it.</summary>
public sealed record LibItem(long Id, string Kind, string Maker, string Name, string Caliber, string Sources);

/// <summary>One value of a component: what it is, from where, when; <see cref="InUse"/> marks the one the tools read.</summary>
public sealed record LibValue(long Id, string Field, string Label, double? Value, string? Text, string Unit, string Source, string SourceRef,
    string RecordedAt, string Notes, bool InUse);

/// <summary>One proposed match of a component of the shooter's with one of the EP library, waiting for a yes or a no.</summary>
public sealed record LibSuggestion(long Id, long ItemId, long CandidateId, string CandidateMaker, string CandidateName, double? Score, string Reason);

/// <summary>Reading the component library, adding the shooter's own figures, and settling the proposed matches.</summary>
public static class LibraryBrowse
{
    /// <summary>The fields a shooter measures or reads off a box, by kind, with their unit (the stored one, metric).</summary>
    public static IReadOnlyList<(string Field, string Unit)> FieldsFor(string kind) => kind switch
    {
        ComponentLibrary.Bullet => new[] { ("weight_gr", "gr"), ("length_mm", "mm"), ("diameter_mm", "mm"), ("tip_length_mm", "mm"),
            ("ogive_length_mm", "mm"), ("boat_tail_mm", "mm"), ("bearing_mm", "mm"), ("bc_g1", ""), ("bc_g7", "") },
        ComponentLibrary.Cartridge => new[] { ("case_capacity_cm3", "cm³"), ("case_length_max_mm", "mm"), ("trim_length_mm", "mm"),
            ("oal_max_mm", "mm"), ("base_to_shoulder_mm", "mm"), ("neck_diameter_mm", "mm"), ("pmax_cip_bar", "bar"), ("pmax_saami_psi", "psi") },
        ComponentLibrary.Case => new[] { ("case_capacity_cm3", "cm³") },
        ComponentLibrary.Powder => new[] { ("bulk_density_kgm3", "kg/m³") },
        ComponentLibrary.Rifle => new[] { ("barrel_length_mm", "mm"), ("twist_mm", "mm") },
        _ => Array.Empty<(string, string)>(),
    };

    /// <summary>
    /// The components of a kind, matching <paramref name="search"/> in name, maker or caliber. With <paramref name="onlyMine"/>
    /// only those with a value that is not from the EP library (the shooter's loads, measurements, maker's figures): the EP
    /// library alone holds over a thousand bullets.
    /// </summary>
    public static List<LibItem> Items(ArchiveDb a, string kind, string search, bool onlyMine, int limit = 2000)
    {
        var list = new List<LibItem>();
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = @"
SELECT i.id, i.kind, IFNULL(i.maker,''), i.name, IFNULL(i.caliber,''),
       (SELECT GROUP_CONCAT(DISTINCT v.source) FROM lib_values v WHERE v.item_id=i.id)
FROM lib_items i
WHERE i.kind=$k AND ($s='' OR lower(i.name) LIKE $p OR lower(IFNULL(i.maker,'')) LIKE $p OR lower(IFNULL(i.caliber,'')) LIKE $p)
  AND ($m=0 OR EXISTS(SELECT 1 FROM lib_values v WHERE v.item_id=i.id AND v.source<>'libreria EP'))
ORDER BY lower(i.maker), lower(i.name) LIMIT $l";
        cmd.Parameters.AddWithValue("$k", kind);
        cmd.Parameters.AddWithValue("$s", search.Trim());
        cmd.Parameters.AddWithValue("$p", "%" + search.Trim().ToLowerInvariant() + "%");
        cmd.Parameters.AddWithValue("$m", onlyMine ? 1 : 0);
        cmd.Parameters.AddWithValue("$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new LibItem(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.IsDBNull(5) ? "" : r.GetString(5)));
        return list;
    }

    /// <summary>Every value of a component, the ones in use first, most trusted source first; GRT's raw fields last.</summary>
    public static List<LibValue> Values(ArchiveDb a, long item)
    {
        var inUse = new HashSet<long>();
        using (var cmd = a.Connection.CreateCommand())
        {
            cmd.Transaction = a.CurrentTransaction;
            cmd.CommandText = "SELECT id FROM lib_effective WHERE item_id=$i";
            cmd.Parameters.AddWithValue("$i", item);
            using var r = cmd.ExecuteReader();
            while (r.Read()) inUse.Add(r.GetInt64(0));
        }
        var list = new List<LibValue>();
        using (var cmd = a.Connection.CreateCommand())
        {
            cmd.Transaction = a.CurrentTransaction;
            cmd.CommandText = @"SELECT id, field, IFNULL(label,''), value, text, IFNULL(unit,''), source, IFNULL(source_ref,''), IFNULL(recorded_at,''), IFNULL(notes,'')
                                FROM lib_values WHERE item_id=$i ORDER BY (field LIKE 'grt.%' OR field LIKE 'ep.%'), field, priority, recorded_at DESC, id DESC";
            cmd.Parameters.AddWithValue("$i", item);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                long id = r.GetInt64(0);
                list.Add(new LibValue(id, r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetDouble(3), r.IsDBNull(4) ? null : r.GetString(4),
                    r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9), inUse.Contains(id)));
            }
        }
        return list;
    }

    /// <summary>
    /// A figure of the shooter's (<see cref="ComponentLibrary.Measured"/>) or off the maker's box or site
    /// (<see cref="ComponentLibrary.Maker"/>), dated today. A measurement outranks every other source at once. The archive
    /// becomes the master. False when the very same figure from that source is already there.
    /// </summary>
    public static bool AddFigure(ArchiveDb a, long item, string field, double value, string unit, string source, string note)
    {
        if (source != ComponentLibrary.Measured && source != ComponentLibrary.Maker)
            throw new ArgumentException("Only a measurement or a maker's figure is entered by hand.");
        bool added = false;
        a.InTransaction(() =>
        {
            added = ComponentLibrary.Value(a, item, field, value, null, unit, source,
                source == ComponentLibrary.Measured ? "misura del tiratore" : "dato del produttore", ArchiveDb.Now(), notes: note);
            if (added) a.MakeMaster();
        });
        return added;
    }

    /// <summary>The proposed matches still waiting, best first within each component.</summary>
    public static List<LibSuggestion> Pending(ArchiveDb a, long? item = null)
    {
        var list = new List<LibSuggestion>();
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = @"SELECT s.id, s.item_id, s.candidate_id, IFNULL(c.maker,''), c.name, s.score, IFNULL(s.reason,'')
                            FROM lib_suggestions s JOIN lib_items c ON c.id=s.candidate_id
                            WHERE s.status='da confermare' AND ($i IS NULL OR s.item_id=$i)
                            ORDER BY s.item_id, s.score DESC, s.id";
        cmd.Parameters.AddWithValue("$i", (object?)item ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new LibSuggestion(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetDouble(5), r.GetString(6)));
        return list;
    }

    /// <summary>
    /// "Yes, it is the same component": the candidate's library values are copied onto the shooter's component (they keep
    /// their source and rank below the shooter's own), its name kept as an alias, this match confirmed and the component's
    /// other proposals set aside. The library's own record stays as it is. The archive becomes the master.
    /// </summary>
    public static void Confirm(ArchiveDb a, long suggestionId)
    {
        a.InTransaction(() =>
        {
            if (a.ScalarP("SELECT item_id FROM lib_suggestions WHERE id=$s AND status='da confermare'", ("$s", suggestionId)) is not long item)
                throw new InvalidOperationException("Abbinamento non trovato o già deciso.");
            long cand = (long)a.ScalarP("SELECT candidate_id FROM lib_suggestions WHERE id=$s", ("$s", suggestionId))!;
            string name = a.ScalarP("SELECT TRIM(IFNULL(maker,'') || ' ' || name) FROM lib_items WHERE id=$c", ("$c", cand)) as string ?? "";
            a.Exec(@"INSERT INTO lib_values(item_id, field, value, text, unit, label, source, priority, source_ref, recorded_at, notes)
                     SELECT $i, field, value, text, unit, label, source, priority, source_ref, recorded_at, 'abbinamento confermato'
                     FROM lib_values WHERE item_id=$c AND source='libreria EP'", ("$i", item), ("$c", cand));
            ComponentLibrary.Value(a, item, "alias", null, name, "", ComponentLibrary.EpLibrary, "abbinamento confermato dal tiratore", ArchiveDb.Now(),
                "nome nella libreria EP");
            a.Exec("UPDATE lib_suggestions SET status='confermato' WHERE id=$s", ("$s", suggestionId));
            a.Exec("UPDATE lib_suggestions SET status='scartato' WHERE item_id=$i AND status='da confermare'", ("$i", item));
            a.MakeMaster();
        });
    }

    /// <summary>"No, it is not": the proposal is set aside (kept, so it is not proposed again).</summary>
    public static void Reject(ArchiveDb a, long suggestionId)
        => a.InTransaction(() =>
        {
            a.Exec("UPDATE lib_suggestions SET status='scartato' WHERE id=$s AND status='da confermare'", ("$s", suggestionId));
            a.MakeMaster();
        });
}
