using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Recovery;

/// <summary>What one .grtload holds, as found by <see cref="HistoryRecovery"/>.</summary>
public sealed record RecoveredLoad(
    string RelPath, string Shooter, string Family, DateTime Modified,
    string Caliber, string Gun, string Bullet, double? BulletGr, string Powder, double? ChargeGr, double? CoalMm, double? Ba,
    int Strings, int Shots, int Groups, int Notes, int Pictures, string? Error);

/// <summary>One chronograph string (one &lt;charge&gt; of a Measurement tab) with every shot, m/s.</summary>
public sealed record RecoveredString(
    string RelPath, string Measurement, string ChargeName, double? ChargeGr, string ChargeNote,
    IReadOnlyList<double> VelocitiesMps, IReadOnlyList<string?> ShotNotes, string Key, string? DuplicateOf);

/// <summary>One shot, in millimetres from the origin (the aim point when marked, else the centre of the hits), +X right, +Y up;
/// also the raw picture fractions GRT stores, so nothing is lost when the scale is missing.</summary>
public sealed record RecoveredHit(double? XMm, double? YMm, double FracX, double FracY, bool Flyer);

/// <summary>One group of one "Shot group analysis" tab.</summary>
public sealed record RecoveredGroup(
    string RelPath, string Tab, string Set, double? DistanceM, double? Step, string Origin,
    IReadOnlyList<RecoveredHit> Hits, string Key, string? DuplicateOf, string? Warning, int TabIndex = 0);

public sealed record RecoveredNote(string RelPath, string Title, string Text, string Key, string? DuplicateOf);

/// <summary>A target picture; <see cref="TabIndex"/> is the tab's position among the file's shot-group tabs (the same as on its
/// <see cref="RecoveredGroup"/>s), which tells apart two tabs with the same title.</summary>
public sealed record RecoveredPicture(string RelPath, string Tab, string FileName, string Key, string? DuplicateOf, int TabIndex = 0);

public sealed class RecoveryResult
{
    public string Root { get; init; } = "";
    public DateTime When { get; init; } = DateTime.Now;
    public List<RecoveredLoad> Loads { get; } = new();
    public List<RecoveredString> Strings { get; } = new();
    public List<RecoveredGroup> Groups { get; } = new();
    public List<RecoveredNote> Notes { get; } = new();
    public List<RecoveredPicture> Pictures { get; } = new();
}

/// <summary>
/// Reads every .grtload under a GRT folder — read only, nothing is ever written there — and gives back what is locked inside:
/// chronograph strings shot by shot, shot groups with every hit, the toolkit's notes and the target pictures. The toolkit's
/// own snapshots (<c>_toolkit_</c>, <c>_shotmarker_</c>…) repeat what the original holds, so every item carries a content key
/// and the copies after the first are marked as duplicates of it rather than counted twice. Files are visited original first,
/// oldest first, so the first copy is the one the shooter wrote.
/// </summary>
public static class HistoryRecovery
{
    /// <summary>Folders of a GRT install that hold the program, its manuals or exported powders, not anybody's loads.</summary>
    public static readonly string[] SkippedFolders =
        { "doku", "libs", "locales", "plugins", "plugins_backup", "datasheet", "Software Analisi", "Polveri" };

    public static string FamilyOf(string fileName)
    {
        string n = fileName.ToLowerInvariant();
        if (n.Contains("_toolkit_")) return "copia del Toolkit";
        if (n.Contains("_shotmarker_")) return "import ShotMarker";
        if (n.Contains("_athlon_")) return "import cronografo";
        return "originale";
    }

    private static int FamilyRank(string family) => family == "originale" ? 0 : 1;

    /// <summary>The value the chronograph import writes into a charge whose weight it does not know: 2 g, GRT's own default.</summary>
    internal const double ImportPlaceholderKg = 0.002;

    /// <summary>
    /// The charge of a string in grains, or null when the file does not really say. An Athlon string imported without a charge was
    /// stored with the placeholder 2 g, which read back as a charge of 30.86 gr. It counts as unknown only when it is exactly that
    /// value, the name carries no weight and the note says the string came from the Athlon import: a real 2 g charge has a name of
    /// its own.
    /// </summary>
    internal static double? ChargeOf(GrtCharge c)
    {
        bool placeholder = Math.Abs(c.ValueKg - ImportPlaceholderKg) < 1e-12
            && !System.Text.RegularExpressions.Regex.IsMatch(c.Name, @"\d\s*gr")
            && c.Note.Contains("SRC=Athlon", StringComparison.OrdinalIgnoreCase);
        return placeholder ? null : c.ChargeGrains;
    }

    public static IEnumerable<string> FindLoads(string root)
    {
        foreach (string f in Directory.EnumerateFiles(root, "*.grtload", SearchOption.AllDirectories))
        {
            string first = Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar)[0];
            if (SkippedFolders.Any(s => s.Equals(first, StringComparison.OrdinalIgnoreCase))) continue;
            yield return f;
        }
    }

    /// <param name="pictureSink">Called with (suggested file name, bytes) for every picture seen the first time; null to skip pictures.</param>
    public static RecoveryResult Run(string root, Action<string, byte[]>? pictureSink = null)
    {
        var res = new RecoveryResult { Root = root };
        var seen = new Dictionary<string, string>();   // content key -> first file that held it
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // picture file names already given out

        var files = FindLoads(root)
            .Select(f => (Path: f, Family: FamilyOf(Path.GetFileName(f)), Time: File.GetLastWriteTime(f)))
            .OrderBy(x => FamilyRank(x.Family)).ThenBy(x => x.Time).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var (path, family, time) in files)
        {
            string rel = Path.GetRelativePath(root, path);
            string[] parts = rel.Split(Path.DirectorySeparatorChar);
            string shooter = parts.Length > 1 ? parts[0] : "(cartella principale)";
            GrtLoadDoc doc;
            try { doc = GrtLoadDoc.Load(path); }
            catch (Exception ex)
            {
                res.Loads.Add(new RecoveredLoad(rel, shooter, family, time, "", "", "", null, "", null, null, null, 0, 0, 0, 0, 0, ex.Message));
                continue;
            }

            string? Dup(string key)
            {
                if (seen.TryGetValue(key, out string? first)) return first;
                seen[key] = rel;
                return null;
            }

            int nStrings = 0, nShots = 0, nGroups = 0, nNotes = 0, nPics = 0;
            string? error = null;
            try
            {
                foreach (var m in doc.Measurements())
                    foreach (var c in m.Charges)
                    {
                        // GRT's blank template rows ("Measurement 20 Shots", the ladder placeholders) hold velocities of 0: they are
                        // empty slots, not shots, and counting them gave strings with a mean and SD of 0
                        var real = c.Shots.Where(s => s.VelocityMps > 0).ToList();
                        if (real.Count == 0) continue;
                        var v = real.Select(s => s.VelocityMps).ToList();
                        string key = "S|" + c.Name + "|" + string.Join(",", v.Select(x => x.ToString("0.00", CultureInfo.InvariantCulture)));
                        res.Strings.Add(new RecoveredString(rel, m.Title, c.Name, ChargeOf(c), c.Note, v,
                            real.Select(s => s.Note).ToList(), Hash(key), Dup(Hash(key))));
                        nStrings++; nShots += v.Count;
                    }

                int tabIndex = 0;
                foreach (var sg in doc.ShotGroups())
                {
                    tabIndex++;
                    foreach (var g in ReadGroups(rel, sg, Dup, tabIndex)) { res.Groups.Add(g); nGroups++; }
                }

                foreach (var (title, text) in doc.Notes())
                {
                    string key = Hash("N|" + title + "|" + text);
                    res.Notes.Add(new RecoveredNote(rel, title, text, key, Dup(key)));
                    nNotes++;
                }

                foreach (var (tabIdx, tab, type, data) in doc.ShotGroupPictures())
                {
                    string key = Hash(data);
                    string? dup = Dup(key);
                    string ext = string.IsNullOrWhiteSpace(type) ? "png" : type.Trim('.').ToLowerInvariant();
                    // the tab's position is in the name: two tabs of one file may carry the same title
                    string name = UniqueName(SafeName(Path.ChangeExtension(rel, null) + "__" + tabIdx + " " + tab) + "." + ext, usedNames);
                    if (dup == null) pictureSink?.Invoke(name, data);
                    res.Pictures.Add(new RecoveredPicture(rel, tab, dup == null ? name : "", key, dup, tabIdx));
                    nPics++;
                }
            }
            catch (Exception ex) { error = ex.Message; }

            res.Loads.Add(new RecoveredLoad(rel, shooter, family, time,
                doc.CaliberName, doc.GunName, doc.ProjectileName, doc.BulletMassGr,
                (doc.PropellantMaker + " " + doc.PropellantName).Trim(), doc.PropellantChargeGr, doc.CoalMm, doc.PropellantBa,
                nStrings, nShots, nGroups, nNotes, nPics, error));
        }
        return res;
    }

    /// <summary>The hits of every group of one tab, in mm from the aim point (or the centre of the hits), scaled the way GRT
    /// does: two reference points a known distance apart (stored in mm), the picture size for the aspect ratio, the shooting
    /// distance stored in metres.</summary>
    public static IEnumerable<RecoveredGroup> ReadGroups(string rel, GrtShotGroup sg, Func<string, string?> dup, int tabIndex = 0)
    {
        int w = sg.ImageWidth, h = sg.ImageHeight;
        string? warning = null;
        double? mmPerPx = null;
        if (w > 0 && h > 0)
        {
            double dpx = (sg.RefP2X - sg.RefP1X) * w, dpy = (sg.RefP2Y - sg.RefP1Y) * h;
            double refPx = Math.Sqrt(dpx * dpx + dpy * dpy);
            if (refPx > 1e-6 && sg.RefDistance > 0) mmPerPx = sg.RefDistance / refPx;
            else warning = "scala non impostata in GRT: solo le posizioni sulla foto";
        }
        else warning = "dimensioni della foto mancanti: solo le posizioni sulla foto";
        double? distM = sg.ShootDistance > 0 ? sg.ShootDistance : null;
        if (distM == null) warning = (warning == null ? "" : warning + "; ") + "distanza di tiro non impostata";

        foreach (var set in sg.Groups)
        {
            var poa = set.Points.FirstOrDefault(p => p.PointOfAim);
            var hits = set.Points.Where(p => !p.PointOfAim).ToList();
            if (hits.Count == 0) continue;
            double ox = poa?.X ?? hits.Average(p => p.X), oy = poa?.Y ?? hits.Average(p => p.Y);
            var list = hits.Select(p => new RecoveredHit(
                mmPerPx is { } s ? (p.X - ox) * w * s : null,
                mmPerPx is { } s2 ? -(p.Y - oy) * h * s2 : null,
                p.X, p.Y, p.Flyer)).ToList();
            string key = Hash("G|" + (distM ?? 0).ToString("0.0", CultureInfo.InvariantCulture) + "|" + string.Join(";",
                hits.Select(p => p.X.ToString("0.0000", CultureInfo.InvariantCulture) + "," + p.Y.ToString("0.0000", CultureInfo.InvariantCulture))));
            yield return new RecoveredGroup(rel, sg.Title, set.Name, distM, StepOf(set.Name) ?? StepOf(sg.Title),
                poa != null ? "punto di mira" : "centro dei colpi", list, key, dup(key), warning, tabIndex);
        }
    }

    private static double? StepOf(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s ?? "", @"(-?\d+[.,]\d+)");
        return m.Success && double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
    }

    /// <summary><paramref name="name"/>, or "name (2).ext", "name (3).ext"… if already in <paramref name="used"/>; the result is added to it.</summary>
    public static string UniqueName(string name, HashSet<string> used)
    {
        string candidate = name;
        for (int i = 2; !used.Add(candidate); i++)
            candidate = Path.GetFileNameWithoutExtension(name) + $" ({i})" + Path.GetExtension(name);
        return candidate;
    }

    public static string Hash(string s) => Hash(Encoding.UTF8.GetBytes(s));
    public static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b))[..16].ToLowerInvariant();

    public static string SafeName(string s)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/' }).ToHashSet();
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(bad.Contains(c) ? '_' : c);
        string r = sb.ToString().Trim();
        return r.Length > 150 ? r[..150] : r;
    }
}

/// <summary>Writes a <see cref="RecoveryResult"/> as files anybody can open: a JSON with everything (invariant numbers, for
/// the archive import) and semicolon CSVs with decimal commas for an Italian Excel, plus a plain-text summary.</summary>
public static class RecoveryWriter
{
    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");

    public static void Write(RecoveryResult r, string outDir)
    {
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "recupero.json"),
            JsonSerializer.Serialize(new { r.Root, r.When, r.Loads, r.Strings, r.Groups, r.Notes, r.Pictures },
                new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            new UTF8Encoding(false));

        Csv(outDir, "carichi.csv",
            new[] { "file", "tiratore", "tipo", "modificato", "calibro", "arma", "palla", "peso palla gr", "polvere", "carica gr", "COAL mm", "Ba",
                    "serie", "colpi", "rosate", "note", "foto", "errore" },
            r.Loads.Select(l => new object?[] { l.RelPath, l.Shooter, l.Family, l.Modified.ToString("yyyy-MM-dd HH:mm"), l.Caliber, l.Gun, l.Bullet,
                l.BulletGr, l.Powder, l.ChargeGr, l.CoalMm, l.Ba, l.Strings, l.Shots, l.Groups, l.Notes, l.Pictures, l.Error }));

        Csv(outDir, "velocita.csv",
            new[] { "file", "misura", "serie", "carica gr", "nota serie", "colpo", "velocita m/s", "nota colpo", "codice serie", "doppione di" },
            r.Strings.SelectMany(s => s.VelocitiesMps.Select((v, i) => new object?[] {
                s.RelPath, s.Measurement, s.ChargeName, s.ChargeGr, s.ChargeNote, i + 1, v, s.ShotNotes[i], s.Key, s.DuplicateOf })));

        Csv(outDir, "rosate.csv",
            new[] { "file", "scheda", "n. scheda", "rosata", "distanza m", "passo", "origine", "colpo", "x mm", "y mm", "fuori gruppo", "x foto", "y foto",
                    "codice rosata", "doppione di", "avviso" },
            r.Groups.SelectMany(g => g.Hits.Select((h, i) => new object?[] {
                g.RelPath, g.Tab, g.TabIndex, g.Set, g.DistanceM, g.Step, g.Origin, i + 1, h.XMm, h.YMm, h.Flyer ? "sì" : "", h.FracX, h.FracY,
                g.Key, g.DuplicateOf, g.Warning })));

        Csv(outDir, "note.csv", new[] { "file", "titolo", "testo", "codice", "doppione di" },
            r.Notes.Select(n => new object?[] { n.RelPath, n.Title, n.Text, n.Key, n.DuplicateOf }));

        File.WriteAllText(Path.Combine(outDir, "RIEPILOGO.txt"), Summary(r), new UTF8Encoding(true));
    }

    public static string Summary(RecoveryResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RECUPERO DELLA STORIA DAI FILE DI GRT");
        sb.AppendLine("=====================================");
        sb.AppendLine($"Cartella letta: {r.Root}   (solo lettura: nessun file di GRT è stato modificato)");
        sb.AppendLine($"Data: {r.When:dd/MM/yyyy HH:mm}");
        sb.AppendLine();
        int bad = r.Loads.Count(l => l.Error != null);
        sb.AppendLine($"File .grtload letti: {r.Loads.Count}" + (bad > 0 ? $"  ({bad} con errori, vedi sotto)" : ""));
        foreach (var fam in r.Loads.GroupBy(l => l.Family).OrderBy(g => g.Key))
            sb.AppendLine($"   {fam.Key,-20} {fam.Count(),4}");
        sb.AppendLine();
        int uS = r.Strings.Count(s => s.DuplicateOf == null), uG = r.Groups.Count(g => g.DuplicateOf == null);
        int uN = r.Notes.Count(n => n.DuplicateOf == null), uP = r.Pictures.Count(p => p.DuplicateOf == null);
        sb.AppendLine("Cosa c'era dentro (unici = senza contare le copie dello stesso dato nelle copie del Toolkit):");
        sb.AppendLine($"   serie del cronografo  {uS,5} unici  ({r.Strings.Count} in tutto), {r.Strings.Where(s => s.DuplicateOf == null).Sum(s => s.VelocitiesMps.Count)} colpi");
        sb.AppendLine($"   rosate                {uG,5} unici  ({r.Groups.Count} in tutto), {r.Groups.Where(g => g.DuplicateOf == null).Sum(g => g.Hits.Count)} colpi");
        sb.AppendLine($"   note                  {uN,5} unici  ({r.Notes.Count} in tutto)");
        sb.AppendLine($"   foto dei bersagli     {uP,5} unici  ({r.Pictures.Count} in tutto)");
        sb.AppendLine();
        sb.AppendLine("Per tiratore (cartella), dati unici:");
        foreach (var sh in r.Loads.GroupBy(l => l.Shooter).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var files = sh.Select(l => l.RelPath).ToHashSet();
            sb.AppendLine($"   {sh.Key,-24} file {sh.Count(),3}   serie {r.Strings.Count(s => files.Contains(s.RelPath) && s.DuplicateOf == null),4}" +
                          $"   rosate {r.Groups.Count(g => files.Contains(g.RelPath) && g.DuplicateOf == null),4}" +
                          $"   note {r.Notes.Count(n => files.Contains(n.RelPath) && n.DuplicateOf == null),4}");
        }
        sb.AppendLine("   (\"loads\" è la cartella dei carichi predefinita di GRT)");
        var warn = r.Groups.Where(g => g.Warning != null && g.DuplicateOf == null).ToList();
        if (warn.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Rosate con un avviso ({warn.Count}): le posizioni sulla foto sono salvate comunque.");
            foreach (var g in warn.Take(40)) sb.AppendLine($"   {g.RelPath} · {g.Tab} · {g.Set}: {g.Warning}");
            if (warn.Count > 40) sb.AppendLine($"   … e altre {warn.Count - 40} (elenco completo in rosate.csv)");
        }
        if (bad > 0)
        {
            sb.AppendLine();
            sb.AppendLine("File che non si sono potuti leggere:");
            foreach (var l in r.Loads.Where(l => l.Error != null)) sb.AppendLine($"   {l.RelPath}: {l.Error}");
        }
        sb.AppendLine();
        sb.AppendLine("File prodotti:");
        sb.AppendLine("   carichi.csv   un rigo per file: calibro, arma, palla, polvere, carica, quante serie/rosate/note contiene");
        sb.AppendLine("   velocita.csv  ogni colpo di ogni serie del cronografo, in m/s");
        sb.AppendLine("   rosate.csv    ogni colpo di ogni rosata, in mm dal punto di mira (o dal centro dei colpi), + la posizione sulla foto");
        sb.AppendLine("   note.csv      le note scritte nei carichi (anche quelle del Toolkit)");
        sb.AppendLine("   foto\\        le foto dei bersagli salvate nelle schede delle rosate");
        sb.AppendLine("   recupero.json tutto quanto sopra in un solo file, per l'archivio");
        sb.AppendLine("La colonna \"doppione di\" indica il file in cui lo stesso dato compare per la prima volta.");
        return sb.ToString();
    }

    private static void Csv(string dir, string name, string[] header, IEnumerable<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", header.Select(Cell)));
        foreach (var r in rows) sb.AppendLine(string.Join(";", r.Select(Cell)));
        File.WriteAllText(Path.Combine(dir, name), sb.ToString(), new UTF8Encoding(true));
    }

    private static string Cell(object? v)
    {
        string s = v switch
        {
            null => "",
            double d => d.ToString("0.######", It),
            float f => ((double)f).ToString("0.######", It),
            IFormattable fm => fm.ToString(null, It),
            _ => v.ToString() ?? "",
        };
        return s.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
