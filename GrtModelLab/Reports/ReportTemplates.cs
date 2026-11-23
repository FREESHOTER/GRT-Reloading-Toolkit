using System.Text;

namespace GrtModelLab.Reports;

/// <summary>
/// Installs a DokuWiki report page into GRT's <c>doku/&lt;lang&gt;/report/</c> folders so the
/// vibration-model note and its two charts can be pulled into a printable GRT report — same
/// mechanism and file conventions as the sibling GRT Reloading Toolkit's own
/// <c>Reports/ReportTemplates.cs</c> (bare-CR line endings, UTF-8 no BOM, idempotent start.txt
/// linking). GRT's doku system has no Italian language folder in this install (only de/en/fr/ru/uk
/// exist) — the page is still written in Italian into every existing folder regardless, since these
/// are our own custom pages, not part of GRT's own translated doku content.
/// </summary>
public static class ReportTemplates
{
    private const string PageName = "modelobt-vibrazione";

    private static readonly string PageBody = """
        ======Modello di Vibrazione Canna======
        ^ **File:** ^ //"~~file.path~~"// ^

        ^  Canna  ^^
        |  Calibro  |  ~~work.CaliberName~~  |
        |  Diametro foro (GRT)  |  ~~work.Dz~~  |
        |  Lunghezza canna (GRT)  |  ~~work.xe~~  |

        =====Carica vs. moto della volata all'uscita=====
        {{~~result.picture.vibration_timeline.png~~?100%}}

        =====Tempo di canna vs. OBT nativo di GRT=====
        {{~~result.picture.vibration_obt_timeline.png~~?100%}}

        ~~result.Note("Modello di Vibrazione Canna")~~

        //Generato da GRT Model OBT — Modello di Vibrazione Canna. Esegui "Scansiona la scala di
        cariche GRT" e poi "Scrivi nota sul caricamento GRT" per popolare questa pagina; è vuota
        finché non l'hai fatto almeno una volta su questo caricamento.//
        """;

    private const string LinkLabel = "GRT Model OBT — Vibrazione canna";

    /// <summary>The GRT install root (parent of <c>doku/</c>), inferred from the running exe.</summary>
    public static string? InferGrtRoot()
    {
        // exe lives in <root>/plugins/GrtModelOBT/
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 5 && dir != null; up++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "doku")))
                return dir.FullName;
        return null;
    }

    /// <summary>Writes the report page into every <c>doku/&lt;lang&gt;/report</c> folder found under
    /// <paramref name="grtRoot"/>. Returns a per-language log, safe to run more than once.</summary>
    public static List<string> Install(string grtRoot)
    {
        var log = new List<string>();
        string doku = Path.Combine(grtRoot, "doku");
        if (!Directory.Exists(doku)) { log.Add($"nessuna cartella doku sotto {grtRoot}"); return log; }

        var enc = new UTF8Encoding(false);
        int langs = 0;
        foreach (var langDir in Directory.GetDirectories(doku))
        {
            string reportDir = Path.Combine(langDir, "report");
            if (!Directory.Exists(reportDir)) continue;
            langs++;
            string lang = Path.GetFileName(langDir);

            string path = Path.Combine(reportDir, PageName + ".txt");
            File.WriteAllText(path, PageBody.Replace("\r\n", "\n").Replace("\n", "\r"), enc);
            log.Add($"{lang}: scritto {PageName}.txt");

            string startPath = Path.Combine(reportDir, "start.txt");
            if (File.Exists(startPath))
            {
                string start = File.ReadAllText(startPath);
                if (start.Contains($"report:{PageName}", StringComparison.Ordinal))
                    log.Add($"{lang}: start.txt collega già la pagina");
                else
                {
                    string link = $"  * [[report:{PageName}|{LinkLabel}]]\r";
                    int anchor = start.IndexOf("default-compact", StringComparison.Ordinal);
                    if (anchor >= 0)
                    {
                        int eol = start.IndexOf('\r', anchor);
                        if (eol < 0) eol = start.Length;
                        start = start[..eol] + "\r" + link.TrimEnd('\r') + start[eol..];
                    }
                    else start += "\r" + link;
                    File.WriteAllText(startPath, start, enc);
                    log.Add($"{lang}: aggiunto il link in start.txt");
                }
            }
        }
        log.Add(langs == 0 ? "nessuna cartella report trovata" : $"fatto - {langs} lingua/e");
        return log;
    }
}
