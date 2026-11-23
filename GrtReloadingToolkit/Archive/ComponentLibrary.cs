using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Archive;

/// <summary>
/// The component library inside the archive: what a bullet, a cartridge, a powder, a case, a primer or a rifle IS (length,
/// BC, case drawing, capacity, pressure limit…), apart from what is on the shelf (the inventory). Every value is a row of
/// its own with its source, unit and date, and the value a tool uses is the one from the most trusted source:
/// <list type="number">
/// <item>the shooter's own measurement,</item><item>the maker's published figure,</item>
/// <item>the shooter's own GRT loads (what they entered in GRT, or GRT's library copied into the load),</item>
/// <item>the Empirical Precision library (Apache 2.0, copied once), a starting point for components never used.</item>
/// </list>
/// Among values of the same rank the newest wins. Nothing is ever overwritten: a new figure is a new row.
/// </summary>
public static class ComponentLibrary
{
    public const string Measured = "misura", Maker = "produttore", GrtLoad = "carico GRT", EpLibrary = "libreria EP";

    public static int Priority(string source) => source switch
    {
        Measured => 1, Maker => 2, GrtLoad => 3, EpLibrary => 4, _ => 9,
    };

    public const string Bullet = "palla", Cartridge = "cartuccia", Powder = "polvere", Case = "bossolo", Primer = "innesco", Rifle = "arma";

    private const double GramsPerGrain = 0.06479891;
    private const double PaPerPsi = 6894.757293168;

    /// <summary>The library's own Italian names of the common fields, for whoever reads the archive by hand.</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["weight_gr"] = "peso (grani)", ["diameter_mm"] = "diametro (mm)", ["length_mm"] = "lunghezza totale (mm)",
        ["tip_length_mm"] = "lunghezza punta in plastica (mm)", ["ogive_length_mm"] = "lunghezza ogiva (mm)",
        ["boat_tail_mm"] = "lunghezza boat tail (mm)", ["bearing_mm"] = "superficie portante (mm)", ["bc_g1"] = "BC G1", ["bc_g7"] = "BC G7",
        ["drag_g7_f7"] = "curva di resistenza (fattore di forma G7 per Mach)", ["engraving_pressure_bar"] = "pressione d'impegno (bar)",
        ["case_capacity_cm3"] = "capacità del bossolo (cm³ d'acqua)", ["case_length_max_mm"] = "lunghezza massima bossolo (mm)",
        ["trim_length_mm"] = "lunghezza di rifilatura (mm)", ["oal_max_mm"] = "lunghezza massima cartuccia (mm)",
        ["base_to_shoulder_mm"] = "fondello-spalla (mm)", ["body_diameter_mm"] = "diametro corpo al fondello (mm)",
        ["shoulder_diameter_mm"] = "diametro alla spalla (mm)", ["neck_diameter_mm"] = "diametro esterno colletto (mm)",
        ["shoulder_angle_deg"] = "semiangolo della spalla (°)", ["rim_diameter_mm"] = "diametro collarino (mm)",
        ["groove_diameter_mm"] = "diametro a fondo righe (mm)", ["bore_diameter_mm"] = "diametro sui pieni (mm)",
        ["pmax_cip_bar"] = "pressione massima C.I.P. (bar)", ["pmax_saami_psi"] = "pressione massima SAAMI (psi)",
        ["twist_mm"] = "passo di rigatura (mm per giro)", ["barrel_length_mm"] = "lunghezza canna (mm)", ["gun_weight_kg"] = "peso arma (kg)",
        ["bulk_density_kgm3"] = "densità apparente (kg/m³)", ["solid_density_kgm3"] = "densità del grano (kg/m³)",
        ["heat_explosion_kjkg"] = "calore di esplosione (kJ/kg)", ["grain_type"] = "forma del grano",
        ["primer_pocket"] = "sede dell'innesco", ["primer_class"] = "uso dell'innesco", ["brisance_j"] = "energia di brisanza (J)",
        ["bc_basis"] = "da dove viene il BC", ["tip_basis"] = "da dove viene la punta", ["standard"] = "norma del disegno",
    };

    public static void CreateSchema(ArchiveDb a) => a.Exec(@"
CREATE TABLE IF NOT EXISTS lib_items (
  id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, maker TEXT DEFAULT '', name TEXT NOT NULL, caliber TEXT DEFAULT '',
  identity TEXT NOT NULL UNIQUE, notes TEXT DEFAULT '', created_at TEXT, origin TEXT);
CREATE TABLE IF NOT EXISTS lib_values (
  id INTEGER PRIMARY KEY AUTOINCREMENT, item_id INTEGER NOT NULL REFERENCES lib_items(id) ON DELETE CASCADE,
  field TEXT NOT NULL, value REAL, text TEXT, unit TEXT DEFAULT '', label TEXT DEFAULT '',
  source TEXT NOT NULL, priority INTEGER NOT NULL, source_ref TEXT DEFAULT '', recorded_at TEXT, notes TEXT DEFAULT '');
CREATE INDEX IF NOT EXISTS ix_lib_values_item ON lib_values(item_id, field, priority, recorded_at);
CREATE TABLE IF NOT EXISTS lib_links (
  load_id INTEGER NOT NULL REFERENCES loads(id) ON DELETE CASCADE, role TEXT NOT NULL,
  item_id INTEGER NOT NULL REFERENCES lib_items(id), PRIMARY KEY(load_id, role));
CREATE TABLE IF NOT EXISTS lib_suggestions (
  id INTEGER PRIMARY KEY AUTOINCREMENT, item_id INTEGER NOT NULL REFERENCES lib_items(id) ON DELETE CASCADE,
  candidate_id INTEGER NOT NULL REFERENCES lib_items(id) ON DELETE CASCADE, score REAL, reason TEXT DEFAULT '',
  status TEXT DEFAULT 'da confermare', created_at TEXT);
CREATE VIEW IF NOT EXISTS lib_effective AS
  SELECT v.* FROM lib_values v WHERE v.id = (
    SELECT v2.id FROM lib_values v2 WHERE v2.item_id = v.item_id AND v2.field = v.field
    ORDER BY v2.priority, v2.recorded_at DESC, v2.id DESC LIMIT 1);");

    public static string Norm(string s) => Regex.Replace((s ?? "").Trim().Trim(',', ';', '.').ToLowerInvariant(), @"\s+", " ");

    private static string Grain(double? gr) => gr is { } g ? g.ToString("0.0", CultureInfo.InvariantCulture) : "";

    public static long Item(ArchiveDb a, string kind, string maker, string name, string caliber, string identity, string origin)
    {
        if (a.ScalarP("SELECT id FROM lib_items WHERE identity=$i", ("$i", identity)) is long id) return id;
        return a.Insert("lib_items", ("kind", kind), ("maker", maker.Trim()), ("name", name.Trim()), ("caliber", caliber.Trim()),
            ("identity", identity), ("created_at", ArchiveDb.Now()), ("origin", origin));
    }

    /// <summary>Adds one value unless the same value from the same source is already on the item (the first copy keeps its
    /// reference). Returns whether a row was written.</summary>
    public static bool Value(ArchiveDb a, long item, string field, double? value, string? text, string unit, string source,
        string sourceRef, string recordedAt, string? label = null, string notes = "")
    {
        if (value == null && string.IsNullOrEmpty(text)) return false;
        if (value is { } v && (double.IsNaN(v) || double.IsInfinity(v))) return false;
        bool same = a.ScalarP(
            "SELECT 1 FROM lib_values WHERE item_id=$i AND field=$f AND source=$s AND " +
            "((value IS NULL AND $v IS NULL) OR ABS(value-$v) <= 1e-9*MAX(1,ABS($v))) AND IFNULL(text,'')=IFNULL($t,'')",
            ("$i", item), ("$f", field), ("$s", source), ("$v", value), ("$t", text)) != null;
        if (same) return false;
        a.Insert("lib_values", ("item_id", item), ("field", field), ("value", value), ("text", text), ("unit", unit),
            ("label", label ?? (Labels.TryGetValue(field, out var l) ? l : "")), ("source", source), ("priority", Priority(source)),
            ("source_ref", sourceRef), ("recorded_at", recordedAt), ("notes", notes));
        return true;
    }

    // ---- from the shooter's GRT loads -------------------------------------------------------------------------------

    /// <summary>Fields of a GRT section that belong to the load, not to the component (seating depth, charge, lot, ladder…).</summary>
    private static readonly HashSet<string> LoadOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "gdepth", "lotid", "mode", "mc", "laddercnt", "laddermc", "imageUuid", "imageDetailUuid", "oal", "gjump", "gjumpUnlock",
        "gfrictionUnlock", "shotshell", "CaliberName", "GunName", "ProjectileName", "mname", "pname",
        // the case volume and the trimmed length are the brass of THAT load (often measured), not the cartridge's: they go on the load
        "casevol", "caselen",
    };

    public sealed record GrtLibraryStats(int Loads, int Bullets, int Cartridges, int Powders, int Rifles, int Values, int Links);

    /// <summary>Reads every GRT load kept in the archive and files its bullet, cartridge, powder and rifle in the library,
    /// linking the load to each.</summary>
    public static GrtLibraryStats FromArchivedGrtLoads(ArchiveDb a, List<string> log, long? onlyLoad = null)
    {
        int nLoads = 0, nValues = 0, nLinks = 0;
        var kinds = new Dictionary<string, HashSet<long>> { [Bullet] = new(), [Cartridge] = new(), [Powder] = new(), [Rifle] = new() };
        var rows = new List<(long LoadId, string Path, string Modified, byte[] Data)>();
        using (var cmd = a.Connection.CreateCommand())
        {
            cmd.Transaction = a.CurrentTransaction;
            // archived loads (GRT's own demonstration file) stay out of the library
            cmd.CommandText = "SELECT l.id, f.original_path, f.modified_at, f.data FROM loads l JOIN files f ON f.id = l.grtload_file_id WHERE l.archived = 0"
                              + (onlyLoad is { } one ? $" AND l.id = {one}" : "") + " ORDER BY f.modified_at, l.id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) rows.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), (byte[])r.GetValue(3)));
        }
        foreach (var (loadId, path, modified, data) in rows)
        {
            GrtLoadDoc doc;
            try { using var ms = new MemoryStream(data); doc = GrtLoadDoc.Load(ms, path); }
            catch (Exception ex) { log.Add($"  carico {Path.GetFileName(path)} non leggibile per la libreria: {ex.Message}"); continue; }
            nLoads++;
            var inputs = doc.AllInputs().ToList();
            string In(string sec, string name) => inputs.FirstOrDefault(i => i.Section == sec && i.Name == name).Value ?? "";
            double? Num(string sec, string name) => double.TryParse(In(sec, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

            long Add(string kind, string maker, string name, string caliber, string identity, string role)
            {
                long id = Item(a, kind, maker, name, caliber, identity, "dai carichi GRT");
                kinds[kind].Add(id);
                a.Exec("INSERT OR IGNORE INTO lib_links(load_id, role, item_id) VALUES($l,$r,$i)", ("$l", loadId), ("$r", role), ("$i", id));
                nLinks++;
                return id;
            }
            void V(long item, string field, double? v, string unit, string? text = null) { if (Value(a, item, field, v, text, unit, GrtLoad, path, modified)) nValues++; }
            void Raw(long item, string section)
            {
                foreach (var i in inputs.Where(i => i.Section == section && !LoadOnly.Contains(i.Name) && i.Value.Length > 0))
                {
                    bool isNum = double.TryParse(i.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
                    if (Value(a, item, $"grt.{section}.{i.Name}", isNum ? v : null, isNum ? null : i.Value, i.Unit, GrtLoad, path, modified,
                        label: i.Description.Length > 0 ? "GRT: " + i.Description : "GRT: " + i.Name)) nValues++;
                }
            }

            // what belongs to this load's own brass and seating, not to any component
            a.Exec("UPDATE loads SET case_capacity_cm3=$c, case_length_mm=$l, seating_depth_mm=$s WHERE id=$id",
                ("$c", Num("caliber", "casevol")), ("$l", Num("caliber", "caselen")), ("$s", Num("projectile", "gdepth")), ("$id", loadId));

            // bullet
            string bMaker = In("projectile", "mname"), bName = In("projectile", "pname");
            if (bName.Length == 0) bName = In("projectile", "ProjectileName");
            double? wGr = Num("projectile", "mp") is { } g ? g / GramsPerGrain : null;
            double? dia = Num("projectile", "Dbul");
            if (bName.Length > 0)
            {
                long b = Add(Bullet, bMaker, bName, dia is { } d ? d.ToString("0.00", CultureInfo.InvariantCulture) + " mm" : "",
                    $"{Bullet}|{Norm(bMaker)}|{Norm(bName)}|{Grain(wGr)}", Bullet);
                V(b, "weight_gr", wGr is { } w ? Math.Round(w, 3) : null, "gr"); V(b, "diameter_mm", dia, "mm");
                V(b, "length_mm", Num("projectile", "glen"), "mm"); V(b, "boat_tail_mm", Num("projectile", "gtailh"), "mm");
                V(b, "bc_g1", Num("projectile", "g1bc") is > 0 and var g1 ? g1 : null, "");
                V(b, "bc_g7", Num("projectile", "g7bc") is > 0 and var g7 ? g7 : null, "");
                Raw(b, "projectile");
            }

            // cartridge (caliber + its C.I.P./SAAMI drawing)
            string cal = In("caliber", "CaliberName");
            if (cal.Length > 0)
            {
                long c = Add(Cartridge, "", cal, cal, $"{Cartridge}|{Norm(cal)}", Cartridge);
                V(c, "groove_diameter_mm", Num("caliber", "Dz"), "mm");
                string method = In("caliber", "method");
                if (Num("caliber", "pMaxZul") is { } pm && pm > 0)
                {
                    if (method.Contains("SAAMI", StringComparison.OrdinalIgnoreCase)) V(c, "pmax_saami_psi", Math.Round(pm * 1e5 / PaPerPsi), "psi");
                    else V(c, "pmax_cip_bar", pm, "bar");
                }
                double? Cf(string n) => double.TryParse(In("caliberfile", n), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? v : null;
                V(c, "base_to_shoulder_mm", Cf("L1"), "mm"); V(c, "case_length_max_mm", Cf("L3"), "mm"); V(c, "oal_max_mm", Cf("L6"), "mm");
                V(c, "body_diameter_mm", Cf("P1"), "mm"); V(c, "shoulder_diameter_mm", Cf("P2"), "mm"); V(c, "neck_diameter_mm", Cf("H2"), "mm");
                V(c, "rim_diameter_mm", Cf("R1"), "mm"); V(c, "shoulder_angle_deg", Cf("alpha") is { } al ? al / 2 : null, "°");
                if (In("caliberfile", "standard") is { Length: > 0 } std) V(c, "standard", null, "", std);
                Raw(c, "caliber"); Raw(c, "caliberfile");
            }

            // powder
            string pMaker = In("propellant", "mname"), pName = In("propellant", "pname");
            if (pName.Length > 0)
            {
                long p = Add(Powder, pMaker, pName, "", $"{Powder}|{Norm(pMaker)}|{Norm(pName)}", Powder);
                V(p, "bulk_density_kgm3", Num("propellant", "pcd"), "kg/m³"); V(p, "solid_density_kgm3", Num("propellant", "pc"), "kg/m³");
                V(p, "heat_explosion_kjkg", Num("propellant", "Qex"), "kJ/kg");
                Raw(p, "propellant");
            }

            // rifle
            string gun = In("gun", "GunName");
            if (gun.Length > 0)
            {
                long r = Add(Rifle, "", gun, cal, $"{Rifle}|{Norm(gun)}", Rifle);
                V(r, "barrel_length_mm", Num("gun", "xe"), "mm"); V(r, "twist_mm", Num("gun", "twistlen"), "mm"); V(r, "gun_weight_kg", Num("gun", "mgun"), "kg");
                Raw(r, "gun");
            }
        }
        var s = new GrtLibraryStats(nLoads, kinds[Bullet].Count, kinds[Cartridge].Count, kinds[Powder].Count, kinds[Rifle].Count, nValues, nLinks);
        log.Add($"• libreria dai tuoi carichi GRT ({s.Loads} carichi letti): {s.Bullets} palle, {s.Cartridges} cartucce, {s.Powders} polveri, " +
                $"{s.Rifles} armi, {s.Values} valori, {s.Links} collegamenti carico→componente.");
        return s;
    }

    // ---- from the Empirical Precision library -----------------------------------------------------------------------

    public sealed record EpLibraryStats(int Bullets, int Cartridges, int Powders, int Cases, int Primers, int Values, string Version);

    /// <summary>Copies Empirical Precision's component library (master-db.json, Apache 2.0) into the library as
    /// <see cref="EpLibrary"/> values. Bullets, powders, cases and primers are items of their own (EP's id in the identity);
    /// a cartridge joins the item of the same name if the shooter's GRT loads already have one.</summary>
    public static EpLibraryStats FromEmpiricalPrecision(ArchiveDb a, string jsonPath, string version, List<string> log)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = doc.RootElement;
        string at = ArchiveDb.Now(), refBase = "Empirical Precision master-db.json " + version;
        int nValues = 0;
        Dictionary<string, JsonElement> ById(string table)
            => root.TryGetProperty(table, out var t) ? t.EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!, e => e) : new();
        var makers = ById("manufacturers"); var dias = ById("diameters"); var pockets = ById("primerPockets"); var cartridges = ById("cartridges");
        string Maker(JsonElement e) => e.TryGetProperty("manufacturerId", out var m) && makers.TryGetValue(m.GetString() ?? "", out var me) ? Str(me, "name") : "";
        string Pocket(JsonElement e) => e.TryGetProperty("primerPocketId", out var p) && pockets.TryGetValue(p.GetString() ?? "", out var pe) ? Str(pe, "name") : "";
        void V(long item, string field, double? v, string unit, string? text = null, string id = "")
        { if (Value(a, item, field, v, text, unit, EpLibrary, refBase + (id.Length > 0 ? " · " + id : ""), at)) nValues++; }

        int nB = 0, nC = 0, nP = 0, nK = 0, nPr = 0;
        var mach = root.TryGetProperty("bulletDragGrid", out var grid) && grid.TryGetProperty("mach", out var mg) ? mg.EnumerateArray().Select(x => x.GetDouble()).ToArray() : Array.Empty<double>();

        foreach (var b in root.GetProperty("bullets").EnumerateArray())
        {
            string id = Str(b, "id"), name = Str(b, "name"), maker = Maker(b);
            var ph = b.GetProperty("physis"); var ba = b.GetProperty("ballistics");
            double? dia = b.TryGetProperty("diameterId", out var di) && dias.TryGetValue(di.GetString() ?? "", out var de) ? Num(de, "bulletDiameterMm") : null;
            double? wGr = Num(ph, "weightGrams") is { } g ? Math.Round(g / GramsPerGrain, 3) : null;
            long it = Item(a, Bullet, maker, name, dia is { } d ? d.ToString("0.00", CultureInfo.InvariantCulture) + " mm" : "", $"{Bullet}|ep:{id}", "libreria EP");
            nB++;
            V(it, "weight_gr", wGr, "gr", id: id); V(it, "diameter_mm", dia, "mm", id: id); V(it, "length_mm", Num(ph, "overallLengthMm"), "mm", id: id);
            V(it, "ogive_length_mm", Num(ph, "ogiveLengthMm"), "mm", id: id); V(it, "boat_tail_mm", Num(ph, "boatTailLengthMm"), "mm", id: id);
            V(it, "bearing_mm", Num(ph, "bearingSurfaceMm"), "mm", id: id); V(it, "tip_length_mm", Num(ph, "tipLengthMm"), "mm", id: id);
            V(it, "tip_basis", null, "", Str(ph, "tipLengthBasis"), id); V(it, "engraving_pressure_bar", Num(ph, "engravingPressurePa") is { } ep ? ep / 1e5 : null, "bar", id: id);
            V(it, "bc_g1", Num(ba, "g1BC"), "", id: id); V(it, "bc_g7", Num(ba, "g7BC"), "", id: id); V(it, "bc_basis", null, "", Str(ba, "basis"), id);
            if (b.TryGetProperty("drag", out var dr) && dr.ValueKind == JsonValueKind.Object && dr.TryGetProperty("f7", out var f7))
                V(it, "drag_g7_f7", null, "", JsonSerializer.Serialize(new { mach, f7 = f7.EnumerateArray().Select(x => x.GetDouble()).ToArray(), g7 = Num(dr, "g7BC"), shape = Str(dr, "shape"), source = Str(dr, "source") }), id);
        }

        foreach (var c in root.GetProperty("cartridges").EnumerateArray())
        {
            string id = Str(c, "id"), name = Str(c, "name");
            long it = Item(a, Cartridge, "", name, name, $"{Cartridge}|{Norm(name)}", "libreria EP");
            nC++;
            var dia = c.TryGetProperty("diameterId", out var di) && dias.TryGetValue(di.GetString() ?? "", out var de) ? de : default;
            V(it, "case_capacity_cm3", Num(c, "baseCapacityH2oGrams"), "cm³", id: id);   // grams of water read as cm³, as GRT stores it
            V(it, "case_length_max_mm", Num(c, "maxCaseLengthMm"), "mm", id: id); V(it, "trim_length_mm", Num(c, "trimLengthMm"), "mm", id: id);
            V(it, "base_to_shoulder_mm", Num(c, "baseToShoulderMm"), "mm", id: id); V(it, "oal_max_mm", Num(c, "oalMm"), "mm", id: id);
            V(it, "body_diameter_mm", Num(c, "bodyDiameterMm"), "mm", id: id); V(it, "shoulder_diameter_mm", Num(c, "shoulderDiameterMm"), "mm", id: id);
            V(it, "neck_diameter_mm", Num(c, "neckDiameterMm"), "mm", id: id); V(it, "shoulder_angle_deg", Num(c, "shoulderAngleDeg"), "°", id: id);
            V(it, "rim_diameter_mm", Num(c, "rimDiameterMm"), "mm", id: id); V(it, "twist_mm", Num(c, "twistRateMm"), "mm", id: id);
            V(it, "pmax_saami_psi", Num(c, "maxSaamiPa") is { } ps ? Math.Round(ps / PaPerPsi) : null, "psi", id: id);
            V(it, "pmax_cip_bar", Num(c, "maxCipPa") is { } pc ? pc / 1e5 : null, "bar", id: id);
            if (dia.ValueKind == JsonValueKind.Object)
            { V(it, "groove_diameter_mm", Num(dia, "bulletDiameterMm"), "mm", id: id); V(it, "bore_diameter_mm", Num(dia, "boreDiameterMm"), "mm", id: id); }
            V(it, "primer_pocket", null, "", Pocket(c), id);
        }

        foreach (var p in root.GetProperty("powders").EnumerateArray())
        {
            string id = Str(p, "id"), name = Str(p, "name"), maker = Maker(p);
            long it = Item(a, Powder, maker, name, "", $"{Powder}|ep:{id}", "libreria EP");
            nP++;
            V(it, "grain_type", null, "", Str(p, "grainType"), id); V(it, "bulk_density_kgm3", Num(p, "bulkDensityKgM3"), "kg/m³", id: id);
            V(it, "solid_density_kgm3", Num(p, "propellantDensityKgM3"), "kg/m³", id: id); V(it, "heat_explosion_kjkg", Num(p, "heatOfExplosionKjKg"), "kJ/kg", id: id);
            V(it, "ep.burnSurfacePeakGain", Num(p, "burnSurfacePeakGain"), "", id: id);
            V(it, "ep.burnFractionAtSurfacePeak", Num(p, "burnFractionAtSurfacePeak"), "", id: id);
            V(it, "ep.burnFractionAtSliverStart", Num(p, "burnFractionAtSliverStart"), "", id: id);
        }

        foreach (var k in root.GetProperty("brass").EnumerateArray())
        {
            string id = Str(k, "id"), maker = Maker(k);
            string cart = k.TryGetProperty("cartridgeId", out var ci) && cartridges.TryGetValue(ci.GetString() ?? "", out var ce) ? Str(ce, "name") : "";
            long it = Item(a, Case, maker, $"{maker} {cart}".Trim(), cart, $"{Case}|ep:{id}", "libreria EP");
            nK++;
            V(it, "case_capacity_cm3", Num(k, "capacityH2oGrams"), "cm³", id: id); V(it, "primer_pocket", null, "", Pocket(k), id);
        }

        if (root.TryGetProperty("primers", out var prs))
            foreach (var p in prs.EnumerateArray())
            {
                string id = Str(p, "id"), maker = Maker(p);
                long it = Item(a, Primer, maker, Str(p, "name"), "", $"{Primer}|ep:{id}", "libreria EP");
                nPr++;
                V(it, "primer_pocket", null, "", Pocket(p), id); V(it, "primer_class", null, "", Str(p, "primerClass"), id);
                V(it, "brisance_j", Num(p, "brisanceEnergyJ"), "J", id: id);
            }

        var s = new EpLibraryStats(nB, nC, nP, nK, nPr, nValues, version);
        log.Add($"• libreria di Empirical Precision ({version}): {nB} palle, {nC} cartucce, {nP} polveri, {nK} bossoli, {nPr} inneschi, {nValues} valori.");
        return s;
    }

    // ---- tying the sources together ---------------------------------------------------------------------------------

    private static Dictionary<string, double> Effective(ArchiveDb a, long item, string? source = null)
    {
        var d = new Dictionary<string, double>();
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = source == null
            ? "SELECT field, value FROM lib_effective WHERE item_id=$i AND value IS NOT NULL"
            : "SELECT field, value FROM lib_values WHERE item_id=$i AND source=$s AND value IS NOT NULL ORDER BY recorded_at, id";
        cmd.Parameters.AddWithValue("$i", item);
        if (source != null) cmd.Parameters.AddWithValue("$s", source);
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[r.GetString(0)] = r.GetDouble(1);   // with a source: the newest of each field wins
        return d;
    }

    private static List<(long Id, string Name, string Maker)> Items(ArchiveDb a, string sql)
    {
        var l = new List<(long, string, string)>();
        using var cmd = a.Connection.CreateCommand();
        cmd.Transaction = a.CurrentTransaction;
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read()) l.Add((r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return l;
    }

    /// <summary>
    /// A cartridge named one way in GRT (".223 Rem. (5.56 x 45 NATO)") and another in the EP library (".223 Remington") is
    /// the same case when its drawing says so: case length, base to shoulder, body, shoulder, neck and rim diameters, each
    /// within 0.06 mm. The maximum overall length is not compared: it belongs to the standard, not to the case (C.I.P. gives
    /// the .308 Winchester 71.12 mm, SAAMI 71.37; the 6.5x55 80.0 against 75.06). When exactly one EP cartridge fits,
    /// its values join the GRT item and its name is kept as an alias; when several fit, they are offered as suggestions;
    /// a GRT cartridge without its drawing is left alone. Names are never used to decide.
    /// </summary>
    public static void MatchCartridgesByDrawing(ArchiveDb a, List<string> log)
    {
        var tol = new (string Field, double Tol)[]
        {
            ("case_length_max_mm", 0.06), ("base_to_shoulder_mm", 0.06), ("body_diameter_mm", 0.06),
            ("shoulder_diameter_mm", 0.06), ("neck_diameter_mm", 0.06), ("rim_diameter_mm", 0.06),
        };
        var grt = Items(a, "SELECT id, name, maker FROM lib_items i WHERE kind='cartuccia' AND EXISTS(SELECT 1 FROM lib_values v WHERE v.item_id=i.id AND v.source='carico GRT') AND NOT EXISTS(SELECT 1 FROM lib_values v WHERE v.item_id=i.id AND v.source='libreria EP')");
        int merged = 0, suggested = 0, noDrawing = 0, none = 0;
        foreach (var (gid, gname, _) in grt)
        {
            var g = Effective(a, gid, GrtLoad);
            if (tol.Any(t => !g.ContainsKey(t.Field))) { noDrawing++; log.Add($"  cartuccia \"{gname}\": il carico GRT non ha il disegno C.I.P., non abbinata."); continue; }
            var ep = Items(a, "SELECT id, name, maker FROM lib_items i WHERE kind='cartuccia' AND EXISTS(SELECT 1 FROM lib_values v WHERE v.item_id=i.id AND v.source='libreria EP') AND NOT EXISTS(SELECT 1 FROM lib_values v WHERE v.item_id=i.id AND v.source='carico GRT')");
            var fits = ep.Where(e =>
            {
                var d = Effective(a, e.Id, EpLibrary);
                return tol.All(t => d.ContainsKey(t.Field) && Math.Abs(d[t.Field] - g[t.Field]) <= t.Tol);
            }).ToList();
            if (fits.Count == 1)
            {
                var (eid, ename, _) = fits[0];
                a.Exec("UPDATE lib_values SET item_id=$g WHERE item_id=$e", ("$g", gid), ("$e", eid));
                a.Exec("UPDATE lib_suggestions SET candidate_id=$g WHERE candidate_id=$e", ("$g", gid), ("$e", eid));
                a.Exec("DELETE FROM lib_items WHERE id=$e", ("$e", eid));
                Value(a, gid, "alias", null, ename, "", EpLibrary, "abbinata per le misure del disegno", ArchiveDb.Now(), "nome nella libreria EP");
                merged++;
                log.Add($"  cartuccia \"{gname}\" = \"{ename}\" della libreria EP (stesse misure del disegno).");
            }
            else if (fits.Count > 1)
            {
                foreach (var f in fits)
                    a.Insert("lib_suggestions", ("item_id", gid), ("candidate_id", f.Id), ("score", 1.0), ("reason", "stesse misure del disegno, più candidati"), ("created_at", ArchiveDb.Now()));
                suggested++;
                log.Add($"  cartuccia \"{gname}\": {fits.Count} cartucce EP con le stesse misure, da scegliere ({string.Join(", ", fits.Select(f => f.Name))}).");
            }
            else { none++; log.Add($"  cartuccia \"{gname}\": nessuna cartuccia EP con le stesse misure."); }
        }
        log.Add($"• cartucce dei tuoi carichi abbinate alla libreria EP per misure: {merged}; da scegliere: {suggested}; senza disegno: {noDrawing}; senza corrispondenza: {none}.");
    }

    /// <summary>
    /// Bullets are never joined by themselves: names differ ("Scenar GB489 6032" / "Scenar") and one maker sells several
    /// bullets of a weight. For each bullet of the shooter's GRT loads, the EP bullets of the same maker, weight (±0.3 gr)
    /// and diameter (±0.03 mm) are written as suggestions to confirm, best first: the closer the length and the more words
    /// of the name in common, the higher the score.
    /// </summary>
    public static void SuggestBulletMatches(ArchiveDb a, List<string> log)
    {
        var grt = Items(a, "SELECT id, name, maker FROM lib_items WHERE kind='palla' AND identity NOT LIKE 'palla|ep:%'");
        var ep = Items(a, "SELECT id, name, maker FROM lib_items WHERE kind='palla' AND identity LIKE 'palla|ep:%'")
            .Select(e => (e.Id, e.Name, e.Maker, V: Effective(a, e.Id))).ToList();
        int withAny = 0, single = 0;
        foreach (var (gid, gname, gmaker) in grt)
        {
            var g = Effective(a, gid);
            if (!g.TryGetValue("weight_gr", out double w) || !g.TryGetValue("diameter_mm", out double d)) continue;
            string m = Norm(gmaker);
            var words = Regex.Split(Norm(gname), @"[^a-z0-9]+").Where(x => x.Length > 1).ToHashSet();
            var cands = ep.Where(e => m.Length > 0 && Norm(e.Maker).Length > 0 && (Norm(e.Maker).Contains(m) || m.Contains(Norm(e.Maker)))
                                      && e.V.TryGetValue("weight_gr", out double ew) && Math.Abs(ew - w) <= 0.3
                                      && e.V.TryGetValue("diameter_mm", out double ed) && Math.Abs(ed - d) <= 0.03)
                .Select(e =>
                {
                    int common = Regex.Split(Norm(e.Name), @"[^a-z0-9]+").Count(x => x.Length > 1 && words.Contains(x));
                    double lenDiff = g.TryGetValue("length_mm", out double gl) && e.V.TryGetValue("length_mm", out double el) ? Math.Abs(gl - el) : 1.0;
                    double score = common + 1.0 / (1.0 + lenDiff);
                    string why = "stesso produttore, peso e diametro; parole in comune " + common +
                                 (lenDiff < 1.0 ? ", lunghezze diverse di " + lenDiff.ToString("0.00", CultureInfo.InvariantCulture) + " mm" : "");
                    return (e.Id, Score: score, Why: why);
                })
                .OrderByDescending(c => c.Score).ToList();
            if (cands.Count == 0) continue;
            withAny++; if (cands.Count == 1) single++;
            foreach (var c in cands)
                a.Insert("lib_suggestions", ("item_id", gid), ("candidate_id", c.Id), ("score", Math.Round(c.Score, 3)), ("reason", c.Why), ("created_at", ArchiveDb.Now()));
        }
        log.Add($"• palle dei tuoi carichi con un possibile corrispondente nella libreria EP: {withAny} su {grt.Count} ({single} con un solo candidato). Restano da confermare: nessuna è stata unita da sola.");
    }

    private static string Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static double? Num(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
